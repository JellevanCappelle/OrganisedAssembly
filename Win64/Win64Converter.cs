using System;
using System.Collections.Generic;
using System.Text.Json;

namespace OrganisedAssembly.Win64
{
	class Win64Converter : BaseConverter
	{
		private BaseRegister[] parameterRegisters = new BaseRegister[] { BaseRegister.RCX, BaseRegister.RDX, BaseRegister.R8, BaseRegister.R9 };
		private String[] parameterRegistersW = new String[] { "cx", "dx", "r8w", "r9w" };

		private void GenerateShuffle(List<(Register src, Register dst)> moves, ICompiler compiler)
		{
			while(moves.Count != 0)
			{
				int i = moves.FindIndex(x => !moves.Exists(y => y.src.OperandEquals(x.dst)));
				if(i != -1)
				{
					(Register source, Register dest) = moves[i];
					moves.RemoveAt(i);
					compiler.Generate("mov" + dest.Nasm + "," + source.Nasm, "program");
				}
				else
				{
					(Register source, Register dest) = moves[0];
					moves.RemoveAt(0);
					if(source.OperandEquals(dest)) // trivial moves are cycles too!
						continue;

					// because only cycles are left in the move graph now, there is exactly one move that needs to be updated
					i = moves.FindIndex(x => x.src.OperandEquals(dest));
					if(moves[i].src.size > source.size) // make sure no values are split up
					{
						source = new Register(source.baseRegister, moves[i].src.size);
						dest = new Register(dest.baseRegister, moves[i].src.size);
					}
					moves[i] = (new Register(source.baseRegister, moves[i].src.size), moves[i].dst);

					compiler.Generate("xchg" + dest.Nasm + "," + source.Nasm, "program");
				}
			}
		}

		// TODO: new GenerateShuffle() function
		// 1. determine per move which registers are used as source (either directly or in a memory reference)
		// 2. topologically sort moves such that a move that modifies a register is dependent on all moves that use that register as a source
		// 3. throw an exception if there's a cycle, that's a problem to be solved later
		// 4. execute all moves in topological order
		// TODO: use this new function to also implement 'params Array<T>' style arguments

		/// <summary>
		/// Generates a function call according to the Win64 ABI.
		/// </summary>
		/// <param name="function"></param>
		/// <param name="metadata">Can be null if no metadata is available.</param>
		/// <param name="arguments">Can be empty if no arguments are given.</param>
		/// <param name="returnTargets">Can be empty if there are no return targets.</param>
		/// <param name="compiler"></param>
		protected override void GenerateABICall(Operand function, FunctionMetadata metadata, Operand[] arguments, Operand[] returnTargets, ICompiler compiler)
		{
			// assert that there is only one return target
			if(returnTargets.Length > 1)
				throw new LanguageException("The Win64 ABI only supports a single return target.");
			Operand returnTarget = returnTargets.Length != 0 ? returnTargets[0] : null;

			// calculate stack needed to store arguments
			int argumentStack = arguments.Length * 8;
			if(argumentStack < 32)
				argumentStack = 32;

			// declare usage of stack space
			compiler.MoveStackPointer(-argumentStack);

			// shuffle parameters into the right registers
			List<SymbolString> lines = new List<SymbolString>();

			// register/immediate-to-stack
			for(int i = 4; i < arguments.Length; i++)
				if(arguments[i] is Register || arguments[i] is Immediate)
					lines.Add($"mov {arguments[i].size.ToNasm()} [rsp + {i * 8}], {arguments[i].Nasm}");

			// register-to-register
			List<(Register src, Register dst)> moves = new List<(Register src, Register dst)>();
			for(int i = 0; i < 4 && i < arguments.Length; i++)
				if(arguments[i] is Register arg)
				{
					Register source = arg.size == SizeSpecifier.BYTE ? new Register(arg.baseRegister, SizeSpecifier.WORD) : arg;
					Register dest = new Register(parameterRegisters[i], source.size);
					moves.Add((source, dest));
				}
			GenerateShuffle(moves, compiler);

			// handle edge-cases in byte arguments
			for(int i = 0; i < 4 && i < arguments.Length; i++)
				if(arguments[i] is Register arg && arg.size == SizeSpecifier.BYTE)
					if(arg.registerName.EndsWith('h'))
						lines.Add($"shr {parameterRegistersW[i]}, 8");

			// memory/reference/immediate-to-register (TODO: anything non-stack has potentially been trashed in the preceding steps, detect this and throw exception)
			for(int i = 0; i < 4 && i < arguments.Length; i++)
				if(arguments[i] is not Register)
					if(arguments[i] is MemoryAddress arg && !arg.isAccess) // can be 'lea'-ed directly into the target register
						lines.Add($"lea {parameterRegisters[i].ToNasm()}, [" + arg.address + "]");
					else
					{
						SizeSpecifier size = (i >= 2 && arguments[i].size == SizeSpecifier.BYTE) ? SizeSpecifier.WORD : arguments[i].size;
						Register dest = new Register(parameterRegisters[i], size);
						lines.Add($"mov {dest.Nasm}," + arguments[i].Resize(dest.size).Nasm); // TODO: throw error if the destination register size is upgraded but the argument is too large for the original size
					}

			// memory-to-stack: use rax as scratch register since it is volatile under Win64
			for(int i = 4; i < arguments.Length; i++)
				if(arguments[i] is MemoryAddress arg)
					if(arg.isAccess)
					{
						Register rax = new Register(BaseRegister.RAX, arguments[i].size);
						lines.Add($"mov {rax.Nasm}," + arguments[i].Nasm);
						lines.Add($"mov [rsp + {i * 8}], {rax.Nasm}");
					}
					else
					{
						lines.Add("lea rax, [" + arg.address + "]");
						lines.Add($"mov [rsp + {i * 8}], rax");
					}

			foreach(SymbolString line in lines)
				compiler.Generate(line, "program");

			// make the call and clean up stack
			compiler.Generate("call" + function.Nasm, "program");
			compiler.MoveStackPointer(argumentStack);
			compiler.DeclareCall();

			// move the return value to the right place (if applicable)
			if(returnTarget != null)
				if(returnTarget is Register reg1 && reg1.registerName == "ah")
					compiler.Generate("shl ax, 8", "program");
				else if(!(returnTarget is Register reg2 && reg2.baseRegister == BaseRegister.RAX))
				{
					Register rax = new Register(BaseRegister.RAX, returnTarget.size);
					compiler.Generate("mov" + returnTarget.Nasm + "," + rax.Nasm, "program");
				}
		}

		// TODO: disable generation of instructions in the same local scope after return statement? except a label might be used to legitemately jump past a return statement...
		protected override void ConvertABIReturn(JsonProperty ret, LinkedList<CompilerAction> program)
		{
			// can only return one value under Win64
			JsonProperty? valueList = ret.GetNonterminal("argumentList");
			JsonProperty? value = valueList?.GetNonterminal("argument");
			if(valueList != null && value == null)
				throw new LanguageException($"Malformed return statement: {ret.Flatten()}");

			program.AddLast((compiler, pass) =>
			{
				if(!compiler.IsLocal)
					throw new LanguageException("ABI Return statement in global scope.");
				if(pass == CompilationStep.GenerateCode)
				{
					if(value != null)
					{
						Operand retOp = ArgumentToOperand((JsonProperty)value, "return value", compiler, null, true);

						// move register only if necessary
						if(!(retOp is Register reg && reg.baseRegister == BaseRegister.RAX && reg.registerName != "ah"))
							if(retOp is MemoryAddress mem && !mem.isAccess)
								compiler.Generate("lea rax, [" + mem.address + "]", "program");
							else
							{
								Register rax = new Register(BaseRegister.RAX, retOp.size);
								compiler.Generate("mov" + rax.Nasm + "," + retOp.Nasm, "program");
							}
					}

					compiler.Generate(new DeferredSymbol(() =>
					{
						// update stack pointer if needed
						int stackSize = compiler.GetStackSize() - 8; // subtract return pointer from total stack size
						if(stackSize > 0)
							return $"add rsp, {stackSize}";
						else if(stackSize < 0)
							throw new InvalidOperationException($"Attempted to return while stack size ({stackSize}) is negative.");
						else return null;
					}),
					CompilerEvent.StackSizeSet, "program");

					// finally: return
					compiler.Generate("ret", "program");
				}
			});
		}

		protected override void GenerateFunction(String name, (ValueType type, String name)[] parameters, JsonProperty body, LinkedList<CompilerAction> program)
		{
			// prologue
			program.AddLast((compiler, pass) =>
			{
				if(pass == CompilationStep.DeclareGlobalSymbols)
				{
					String label = GetLabelString(compiler, name);
					if(parameters == null || parameters.Length == 0)
						compiler.DeclareFunction(name, label, new FunctionMetadata());
					else
					{
						// declare a function placeholder symbol if the function has parameters
						FunctionPlaceholder placeholder = compiler.DeclareFunctionPlaceholder(name, parameters, (placeholder) =>
						{
							(ValueType type, String name)[] parameters = (placeholder as FunctionPlaceholder)?.parameters
																		 ?? throw new InvalidOperationException($"Encountered wrong placeholder symbol type for function '{name}'.");
							foreach((ValueType type, String name) in parameters)
								type.ResolveDependency();
							FunctionMetadata metadata = new FunctionMetadata(parameters);
							return new FunctionSymbol(label, metadata);
						});
					}

					compiler.EnterLocal(name);
				}
				else if(pass == CompilationStep.SolveGlobalSymbolDependencies)
				{
					// declare dependencies if the function has parameters
					if(compiler.ResolveSymbol(name) is FunctionPlaceholder placeholder)
						foreach((ValueType type, String name) in placeholder.parameters)
							type.DeclareDependency(placeholder, compiler);

					compiler.EnterLocal(name);
				}
				else if(pass == CompilationStep.GenerateCode)
				{
					compiler.Generate(compiler.ResolveSymbol(name) + ":", "program");
					compiler.EnterLocal(name);
					compiler.MoveStackPointer(-8); // declare stack space occupied by return address

					// declare all parameters and move them to the shadow space
					FunctionSymbol function = compiler.ResolveSymbol(name) as FunctionSymbol
											  ?? throw new InvalidOperationException($"Attempted to generate code while no symbol was defined for function '{name}'.");
					(ValueType type, String name)[] solvedParameters = function.Metadata.parameters;
					for(int i = 0; i < solvedParameters.Length; i++)
					{
						int offset = 8 + i * 8;
						compiler.DeclareExistingStackVariable(solvedParameters[i].type, solvedParameters[i].name, offset);
						if(i < 4) // handle register / shadow space parameters
							compiler.Generate($"mov [rsp + {offset}], {parameterRegisters[i].ToNasm()}", "program"); // TODO: adapt size of register when possible?
					}

					compiler.Generate(new DeferredSymbol(() =>
					{
						int stackSize = compiler.GetStackSize();
						if(stackSize - 8 > 0)
							return $"sub rsp, {stackSize - 8}"; // allocate stack space for everything except the return pointer
						else if(stackSize - 8 < 0)
							throw new LanguageException("Encountered a negative stack size.");
						else return null;
					}), CompilerEvent.StackSizeSet, "program");
				}
			});

			// function body
			ConvertNode(body, program);

			// housekeeping
			program.AddLast((compiler, pass) =>
			{
				if(pass == CompilationStep.GenerateCode)
				{
					// calculate (aligned) stack size
					int stackSize = compiler.GetMaxStackSize();
					int padding = 0;
					if(!compiler.IsLeaf()) // don't bother with stack alignment for leaf functions
						if(stackSize % 16 != 0)
							padding = 16 - stackSize % 16;
					stackSize += padding;
					if(padding != 0)
						compiler.AllocateDummyVariable(padding); // declare the padding bytes as a dummy variable, since they won't be cleaned up until the function returns
					compiler.MoveStackPointer(8); // undo declaration of return address
					compiler.SetStackSize(stackSize);
				}
				compiler.ExitLocal();
			});
		}
	}
}
