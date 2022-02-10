using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace OrganisedAssembly.Win64
{
	class Win64Converter : BaseConverter
	{
		private String[] parameterRegisters = new String[] { "rcx", "rdx", "r8", "r9" };
		private String[] parameterRegistersW = new String[] { "cx", "dx", "r8w", "r9w" };

		private void GenerateShuffle(List<(Operand src, Operand dst)> moves, ICompiler compiler)
		{
			while(moves.Count != 0)
			{
				int i = moves.FindIndex(x => !moves.Exists(y => y.src.operand == x.dst.operand));
				if(i != -1)
				{
					(Operand source, Operand dest) = moves[i];
					moves.RemoveAt(i);
					compiler.Generate("mov" + dest.NasmRep + "," + source.NasmRep, "program");
				}
				else
				{
					(Operand source, Operand dest) = moves[0];
					moves.RemoveAt(0);
					if(source.operand == dest.operand) // trivial moves are cycles too!
						continue;

					// because only cycles are left in the move graph now, there is exactly one move that needs to be updated
					i = moves.FindIndex(x => x.src.operand == dest.operand);
					if(moves[i].src.size > source.size) // make sure no values are split up
					{
						source = source.Resize(moves[i].src.size);
						dest = dest.Resize(moves[i].src.size);
					}
					moves[i] = (source.Resize(moves[i].src.size), moves[i].dst);

					compiler.Generate("xchg" + dest.NasmRep + "," + source.NasmRep, "program");
				}
			}
		}

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
				if(arguments[i].type == OperandType.Register || arguments[i].type == OperandType.Immediate)
					lines.Add($"mov {arguments[i].Size} [rsp + {i * 8}], {arguments[i].NasmRep}");

			// register-to-register
			List<(Operand src, Operand dst)> moves = new List<(Operand src, Operand dst)>();
			for(int i = 0; i < 4 && i < arguments.Length; i++)
				if(arguments[i].type == OperandType.Register)
				{
					Operand source = arguments[i].size == SizeSpecifier.BYTE ? new Operand(arguments[i], SizeSpecifier.WORD) : arguments[i];
					Operand dest = new Operand(source.size, parameterRegisters[i], OperandType.Register);
					moves.Add((source, dest));
				}
			GenerateShuffle(moves, compiler);

			// handle edge-cases in byte arguments
			for(int i = 0; i < 4 && i < arguments.Length; i++)
				if(arguments[i].type == OperandType.Register && arguments[i].size == SizeSpecifier.BYTE)
				{
					String reg = arguments[i].registerName;
					if(reg.EndsWith('h'))
						lines.Add($"shr {parameterRegistersW[i]}, 8");
				}

			// memory/reference/immediate-to-register (TODO: anything non-stack has potentially been trashed in the preceding steps, detect this and throw exception)
			for(int i = 0; i < 4 && i < arguments.Length; i++)
				if(arguments[i].type != OperandType.Register)
					if(arguments[i].type == OperandType.Reference) // can be 'lea'-ed directly into the target register
						lines.Add($"lea {parameterRegisters[i]}," + arguments[i].operand);
					else
					{
						Operand dest = new Operand(arguments[i].size, parameterRegisters[i], OperandType.Register, upgradeByteRegisters: true);
						lines.Add($"mov {dest.NasmRep}," + arguments[i].Resize(dest.size).NasmRep); // TODO: throw error if the destination register size is upgraded but the argument is too large for the original size
					}

			// memory-to-stack: use rax as scratch register since it is volatile under Win64
			for(int i = 4; i < arguments.Length; i++)
				if(arguments[i].type == OperandType.Memory)
				{
					Operand rax = new Operand(arguments[i].size, "rax", OperandType.Register);
					lines.Add($"mov {rax.NasmRep}," + arguments[i].NasmRep);
					lines.Add($"mov [rsp + {i * 8}], {rax.NasmRep}");
				}
				else if(arguments[i].type == OperandType.Reference)
				{
					lines.Add($"lea rax," + arguments[i].operand);
					lines.Add($"mov [rsp + {i * 8}], rax");
				}

			foreach(SymbolString line in lines)
				compiler.Generate(line, "program");

			// make the call and clean up stack
			compiler.Generate("call" + function.NasmRep, "program");
			compiler.MoveStackPointer(argumentStack);
			compiler.DeclareCall();

			// move the return value to the right place (if applicable)
			if(returnTarget != null)
				if(returnTarget.NasmRep == "ah")
					compiler.Generate("shl ax, 8", "program");
				else if(returnTarget.operand != "rax")
				{
					Operand rax = new Operand(returnTarget.size, "rax", OperandType.Register);
					compiler.Generate("mov" + returnTarget.NasmRep + "," + rax.NasmRep, "program");
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
						if(retOp.type != OperandType.Register || retOp.operand != "rax" || retOp.NasmRep == "ah")
							if(retOp.type != OperandType.Reference)
							{
								Operand rax = new Operand(retOp.size, "rax", OperandType.Register);
								compiler.Generate("mov" + rax.NasmRep + "," + retOp.NasmRep, "program");
							}
							else
								compiler.Generate("lea rax, " + retOp.operand, "program");
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
							compiler.Generate($"mov [rsp + {offset}], {parameterRegisters[i]}", "program"); // TODO: adapt size of register when possible?
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
