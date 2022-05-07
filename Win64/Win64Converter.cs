using System;
using System.Collections.Generic;
using System.Text.Json;

namespace OrganisedAssembly.Win64
{
	class Win64Converter : ProgramConverter
	{
		private BaseRegister[] parameterRegisters = new BaseRegister[] { BaseRegister.RCX, BaseRegister.RDX, BaseRegister.R8, BaseRegister.R9 };
		
		private void GenerateMoves(List<(Operand src, Operand dst)> moves, ICompiler compiler)
		{
			// enumerate all source registers for all moves
			List<BaseRegister>[] sourceRegisters = new List<BaseRegister>[moves.Count];
			for(int i = 0; i < moves.Count; i++)
			{
				List<BaseRegister> registers = new List<BaseRegister>();
				if(moves[i].src is Register reg)
					registers.Add(reg.baseRegister);
				if(moves[i].src is MemoryAddress sm)
					registers.AddRange(sm.EnumerateRegisters());
				if(moves[i].dst is MemoryAddress dm)
					registers.AddRange(dm.EnumerateRegisters());
				sourceRegisters[i] = registers;
			}

			// setup a toplogical ordering of the moves
			TopologicalSort<(Operand src, Operand dst)> moveOrder = new TopologicalSort<(Operand src, Operand dst)>();
			for(int i = 0; i < moves.Count; i++)
			{
				moveOrder.AddNode(moves[i]);

				if(moves[i].dst is Register dest) // this move is dependent on all moves that read from its destination register
					for(int j = 0; j < moves.Count; j++)
						if(j != i && sourceRegisters[j].Contains(dest.baseRegister))
							moveOrder.AddEdge(moves[j], moves[i]);
				if(moves[i].src is MemoryAddress && moves[i].dst is MemoryAddress) // same deal for moves that require RAX as a scratch register
					for(int j = 0; j < moves.Count; j++)
						if(j != i && sourceRegisters[j].Contains(BaseRegister.RAX))
							moveOrder.AddEdge(moves[j], moves[i]);
			}
			
			// execute the moves
			foreach((Operand src, Operand dst) in moveOrder.SortWithException(new LanguageException("Impossible sequence of moves required.")))
				if(src is MemoryAddress srcMem && dst is MemoryAddress)
				{
					SymbolString rax = new Register(BaseRegister.RAX, dst.size).Nasm;
					if(srcMem.isAccess)
						compiler.Generate("mov" + rax + "," + src.Nasm, "program");
					else
						compiler.Generate("lea" + rax + "," + "[" + srcMem.address + "]", "program");
					compiler.Generate("mov" + dst.Nasm + "," + rax, "program");
				}
				else if(src is MemoryAddress addr && !addr.isAccess)
					compiler.Generate("lea" + dst.Nasm + "," + "[" + addr.address + "]", "program");
				else
					compiler.Generate("mov" + dst.Nasm + "," + src.Nasm, "program");
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

			// enumerate moves and generate them
			List<(Operand src, Operand dst)> moves = new List<(Operand src, Operand dst)>();
			for(int i = 0; i < 4 && i < arguments.Length; i++)
				if(i >= 2 && arguments[i].size == SizeSpecifier.BYTE)
					moves.Add((arguments[i].Resize(SizeSpecifier.WORD), new Register(parameterRegisters[i], SizeSpecifier.WORD)));
				else
					moves.Add((arguments[i], new Register(parameterRegisters[i], arguments[i] is MemoryAddress mem && !mem.isAccess ? SizeSpecifier.QWORD : arguments[i].size)));
			for(int i = 4; i < arguments.Length; i++)
				moves.Add((arguments[i], new MemoryAddress($"rsp + {i * 8}", arguments[i] is MemoryAddress mem && !mem.isAccess ? SizeSpecifier.QWORD : arguments[i].size)));
			GenerateMoves(moves, compiler);

			// fix edge case
			for(int i = 2; i < 4 && i < arguments.Length; i++)
				if(arguments[i] is Register r && r.registerName.EndsWith('h'))
					compiler.Generate((SymbolString)"shr" + (i == 2 ? "r8w" : "r9w") + "," + "8", "program");

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
								compiler.Generate("lea rax," + "[" + mem.address + "]", "program");
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

		protected override void GenerateFunction(String name, Parameter[] parameters, JsonProperty body, LinkedList<CompilerAction> program)
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
							Parameter[] parameters = (placeholder as FunctionPlaceholder)?.parameters
													 ?? throw new InvalidOperationException($"Encountered wrong placeholder symbol type for function '{name}'.");
							foreach(Parameter p in parameters)
								p.type.ResolveDependency();
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
						foreach(Parameter p in placeholder.parameters)
							p.type.DeclareDependency(placeholder, compiler);

					compiler.EnterLocal(name);
				}
				else if(pass == CompilationStep.GenerateCode)
				{
					String fullPath = String.Join<Identifier>('.', compiler.GetCurrentPath());
					fullPath += fullPath.Length > 0 ? '.' + name : name;
					compiler.Generate(compiler.ResolveSymbol(name) + ": ;" + fullPath, "program"); // add the full path as a comment after the label
					compiler.EnterLocal(name);
					compiler.MoveStackPointer(-8); // declare stack space occupied by return address

					// declare all parameters and move them to the shadow space
					FunctionSymbol function = compiler.ResolveSymbol(name) as FunctionSymbol
											  ?? throw new InvalidOperationException($"Attempted to generate code while no symbol was defined for function '{name}'.");
					Parameter[] parameters = function.Metadata.parameters;
					for(int i = 0; i < parameters.Length; i++)
					{
						int offset = 8 + i * 8;
						compiler.DeclareExistingStackVariable(parameters[i].type, parameters[i].name, offset);
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
