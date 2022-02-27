using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace OrganisedAssembly
{
	abstract partial class BaseConverter : ActionConverter
	{
		enum SizePriority
		{
			Undefined = 0,
			Implicit = 1,
			Explicit = 2,
			Register = 3
		}

		/// <summary>
		/// A list of opcodes that take one operand.
		/// </summary>
		protected readonly String[] unaryInstructions = { "inc", "dec", "neg", "mul", "div", "imul", "idiv" };

		/// <summary>
		/// A list of opcodes of instructions that take two operands of equal size.
		/// </summary>
		protected readonly String[] binarySameSizeInstructions = {
			"mov", "cmp", "add", "sub", "and", "or", "xor", "test",
			"cmova", "cmovae", "cmovb", "cmovbe", "cmove", "cmovg", "cmovge", "cmovl", "cmovle", "cmovna", "cmovnae", "cmovnb", "cmovnbe", "cmovne", "cmovng", "cmovnge", "cmovnl", "cmovnle", "cmovnz", "cmovz",
		};

		/// <summary>
		/// A list of shift and rotate instructions.
		/// </summary>
		protected readonly String[] shiftinstructions = { "shr", "shl", "sar", "sal", "ror", "rol", "rcr", "rcl" };

		void ConvertInstruction(JsonProperty node, LinkedList<CompilerAction> program)
		{
			String opcode = node.GetNonterminal("opcode")?.Flatten().ToLower()
							?? throw new LanguageException($"Malformed instruction: {node.Flatten()}.");

			if(unaryInstructions.Contains(opcode))
				ConvertUnary(node, program);
			else if(binarySameSizeInstructions.Contains(opcode))
				ConvertBinarySameSize(node, program);
			else if(shiftinstructions.Contains(opcode))
				ConvertShift(node, program);
			else
				program.AddLast((compiler, pass) =>
				{
					if(pass == CompilationStep.GenerateCode)
					{
						// set any aliases that the instruction might contain
						JsonProperty[] operands = node.GetNonterminal("operandList")?.GetNonterminals("operand").ToArray();
						if(operands != null)
							foreach(JsonProperty op in operands)
								if(op.GetChildNonterminal() is JsonProperty opChild)
									if(opChild.Name == "aliasDecl")
										GenerateAlias(opChild, compiler);

						// generate the instruction
						compiler.Generate(Resolve(node, compiler), "program");
					}
				});
		}

		/// <summary>
		/// Attempts to find the implicit, explicit or register size of an operand. Will generate an alias if given an alias declaration.
		/// </summary>
		(SizeSpecifier size, SizePriority priority) GetOperandSize(JsonProperty operand, ICompiler compiler) // TODO: return operand instead of size?
		{
			operand = operand.GetChildNonterminal() ?? throw new LanguageException("Encountered empty operand.");

			if(operand.Name == "register")
				return (new Register(operand.Flatten()).size, SizePriority.Register);
			else if(operand.Name == "memReference")
			{
				if(operand.GetNonterminal("sizeSpecifier") is JsonProperty size)
					return (ParseSize(size.Flatten()), SizePriority.Explicit);
				else if(MemoryReferenceToPath(operand, compiler) is Identifier[] path)
					return (compiler.ResolveSymbol(path).Size, SizePriority.Implicit);
				else
					return (SizeSpecifier.NONE, SizePriority.Undefined);
			}
			else if(operand.Name == "aliasDecl")
				return (GenerateAlias(operand, compiler).size, SizePriority.Register);
			else if(operand.Name == "immediate")
			{
				Identifier[] path = ExpressionToPath(operand.GetNonterminal("expr") ?? throw new LanguageException($"Encountered malformed immediate: '{operand}'."), compiler);
				if(path != null && compiler.ResolveSymbol(path) is AliasSymbol alias)
				{
					if(operand.GetNonterminal("sizeSpecifier") is JsonProperty size)
						if(ParseSize(size.Flatten()) != alias.Size)
							throw new LanguageException($"Bad size specified for aliased register operand: '{operand.Flatten()}'.");
					return (alias.Size, SizePriority.Register);
				}
				else if(operand.GetNonterminal("sizeSpecifier") is JsonProperty size)
					return (ParseSize(size.Flatten()), SizePriority.Explicit);
				else
					return (SizeSpecifier.NONE, SizePriority.Undefined);
			}
			else
				throw new LanguageException($"Encountered unknown operand type: '{operand}'.");
		}

		void ConvertUnary(JsonProperty node, LinkedList<CompilerAction> program)
		{
			String opcode = node.GetNonterminal("opcode")?.Flatten().ToLower()
							?? throw new LanguageException($"Malformed instruction: {node.Flatten()}.");
			bool lockPrefix = node.GetNonterminal("lockPrefix") != null;
			String repPrefix = node.GetNonterminal("repPrefix")?.Flatten();
			String instruction = (lockPrefix ? "lock " : "") + (repPrefix != null ? repPrefix + ' ' : "") + opcode;
			JsonProperty operand = node.GetNonterminal("operandList")?.GetNonterminal("operand")
								   ?? throw new LanguageException($"Malformed unary instruction '{node.Flatten()}'.");
			
			program.AddLast((compiler, pass) =>
			{
				if(pass == CompilationStep.GenerateCode)
				{
					var size = GetOperandSize(operand, compiler);
					if(size.priority == SizePriority.Implicit)
						compiler.Generate((SymbolString)instruction + Operand.SizeToNasm(size.size) + Resolve(operand, compiler), "program");
					else
						compiler.Generate(instruction + Resolve(operand, compiler), "program");
				}
			});
		}

		void ConvertBinarySameSize(JsonProperty node, LinkedList<CompilerAction> program)
		{
			String opcode = node.GetNonterminal("opcode")?.Flatten().ToLower()
							?? throw new LanguageException($"Malformed instruction: {node.Flatten()}.");
			bool lockPrefix = node.GetNonterminal("lockPrefix") != null;
			String repPrefix = node.GetNonterminal("repPrefix")?.Flatten();
			String instruction = (lockPrefix ? "lock " : "") + (repPrefix != null ? repPrefix + ' ' : "") + opcode;

			JsonProperty[] operands = node.GetNonterminal("operandList")?.GetNonterminals("operand").ToArray();
			if(operands.Length != 2)
				throw new LanguageException($"Invalid number of operands for instruction {instruction}.");

			program.AddLast((compiler, pass) =>
			{
				if(pass == CompilationStep.GenerateCode)
				{
					var op1size = GetOperandSize(operands[0], compiler);
					var op2size = GetOperandSize(operands[1], compiler);

					// determine size based on priorities
					SizeSpecifier size;
					SizePriority sizeType;
					LanguageException mismatch = new LanguageException($"Size mismatch between operands '{operands[0].Flatten()}' and '{operands[1].Flatten()}'.");
					if(op1size.priority > op2size.priority)
					{
						if(op2size.priority >= SizePriority.Explicit && op1size.size != op2size.size)
							throw mismatch;
						(size, sizeType) = op1size;
					}
					else if(op2size.priority > op1size.priority)
					{
						if(op1size.priority >= SizePriority.Explicit && op1size.size != op2size.size)
							throw mismatch;
						(size, sizeType) = op2size;
					}
					else if(op1size.size != op2size.size)
						throw mismatch;
					else // completely equal
						(size, sizeType) = op1size;

					// check if for at least one operand a size is known
					if(size == SizeSpecifier.NONE)
						throw new LanguageException($"No size specified for operands '{operands[0].Flatten()}' and '{operands[1].Flatten()}'.");

					if(sizeType == SizePriority.Implicit)
						compiler.Generate((SymbolString)instruction + Operand.SizeToNasm(size) + Resolve(operands[0], compiler) + "," + Resolve(operands[1], compiler), "program");
					else
						compiler.Generate(instruction + Resolve(operands[0], compiler) + "," + Resolve(operands[1], compiler), "program");
				}
			});
		}

		void ConvertShift(JsonProperty node, LinkedList<CompilerAction> program)
		{
			String opcode = node.GetNonterminal("opcode")?.Flatten().ToLower()
							?? throw new LanguageException($"Malformed instruction: {node.Flatten()}.");
			bool lockPrefix = node.GetNonterminal("lockPrefix") != null;
			if(node.GetNonterminal("repPrefix") != null)
				throw new LanguageException($"Attempted to use a 'rep' prefix with a shift/rotate instruction: '{node.Flatten()}'.");
			String instruction = (lockPrefix ? "lock " : "") + opcode;

			JsonProperty[] operands = node.GetNonterminal("operandList")?.GetNonterminals("operand").ToArray()
									  ?? throw new LanguageException($"Malformed shift/rotate instruction '{node.Flatten()}'.");
			if(operands.Length == 0)
				throw new LanguageException($"Malformed shift/rotate instruction '{node.Flatten()}'.");
			if(operands.Length > 2)
				throw new LanguageException($"Malformed shift/rotate instruction '{node.Flatten()}'.");

			program.AddLast((compiler, pass) =>
			{
				if(pass == CompilationStep.GenerateCode)
				{
					var op1size = GetOperandSize(operands[0], compiler);
					SymbolString line = op1size.priority == SizePriority.Implicit
						? (SymbolString)instruction + Operand.SizeToNasm(op1size.size) + Resolve(operands[0], compiler)
						: instruction + Resolve(operands[0], compiler);
					if(operands.Length == 2) // TODO: proper checking for correct operands: either an immediate byte, or 'cl'
					{
						if(operands[1].GetNonterminal("aliasDecl") != null)
							throw new LanguageException($"Attempted to set an alias in the second operand of a shift/rotate instruction: '{node.Flatten()}'.");
						line += "," + Resolve(operands[1], compiler);
					}
					compiler.Generate(line, "program");
				}
			});
		}
	}
}
