using System;
using System.Collections.Generic;

namespace OrganisedAssembly
{
	enum SizeSpecifier
	{
		/// <summary>
		/// Used for constants, which don't represent a variable in memory and therefore don't have a size.
		/// </summary>
		NONE = 0,

		// regular size specifiers
		BYTE = 1,
		WORD = 2,
		DWORD = 4,
		QWORD = 8
	}

	enum OperandType
	{
		Immediate,
		Memory,
		Reference,
		Register
	}

	class Operand
	{
		public readonly SizeSpecifier size;
		public readonly SymbolString operand;
		public readonly OperandType type;
		public readonly String registerName;

		public SymbolString NasmRep => type == OperandType.Register ? registerName : type == OperandType.Reference ? null : SizeToNasm(size) + operand; // can't generate a nasm representation for reference operands, because they need to be calculated at runtime via a 'lea' instruction
		public String Size => SizeToNasm(size);
		public readonly bool supportsByteAccess;
		
		private static Dictionary<SizeSpecifier, Dictionary<String, String>> registerNames = new Dictionary<SizeSpecifier, Dictionary<String, String>>
		{
			{ SizeSpecifier.QWORD, new Dictionary<String, String>
			{
				{ "rax", "rax" },
				{ "rbx", "rbx" },
				{ "rcx", "rcx" },
				{ "rdx", "rdx" },
				{ "rsi", "rsi" },
				{ "rdi", "rdi" },
				{ "r8", "r8" },
				{ "r9", "r9" },
				{ "r10", "r10" },
				{ "r11", "r11" },
				{ "r12", "r12" },
				{ "r13", "r13" },
				{ "r14", "r14" },
				{ "r15", "r15" },
				{ "rsp", "rsp" },
				{ "rbp", "rbp" }
			}},
			{ SizeSpecifier.DWORD, new Dictionary<String, String>
			{
				{ "rax", "eax" },
				{ "rbx", "ebx" },
				{ "rcx", "ecx" },
				{ "rdx", "edx" },
				{ "rsi", "esi" },
				{ "rdi", "edi" },
				{ "r8", "r8d" },
				{ "r9", "r9d" },
				{ "r10", "r10d" },
				{ "r11", "r11d" },
				{ "r12", "r12d" },
				{ "r13", "r13d" },
				{ "r14", "r14d" },
				{ "r15", "r15d" },
				{ "rsp", "esp" },
				{ "rbp", "ebp" }
			}},
			{ SizeSpecifier.WORD, new Dictionary<String, String>
			{
				{ "rax", "ax" },
				{ "rbx", "bx" },
				{ "rcx", "cx" },
				{ "rdx", "dx" },
				{ "rsi", "si" },
				{ "rdi", "di" },
				{ "r8", "r8w" },
				{ "r9", "r9w" },
				{ "r10", "r10w" },
				{ "r11", "r11w" },
				{ "r12", "r12w" },
				{ "r13", "r13w" },
				{ "r14", "r14w" },
				{ "r15", "r15w" },
				{ "rsp", "sp" },
				{ "rbp", "bp" }
			}},
			{ SizeSpecifier.BYTE, new Dictionary<String, String>
			{
				{ "rax", "al" },
				{ "rbx", "bl" },
				{ "rcx", "cl" },
				{ "rdx", "dl" }
			}}
		};

		public Operand(String register)
		{
			type = OperandType.Register;
			registerName = register;
			switch(register)
			{
				case "rax":
				case "rbx":
				case "rcx":
				case "rdx":
				case "rsi":
				case "rdi":
				case "r8":
				case "r9":
				case "r10":
				case "r11":
				case "r12":
				case "r13":
				case "r14":
				case "r15":
				case "rsp":
				case "rbp":
				case "cr0": // TODO: do these work with all of the operand functionality???
				case "cr2":
				case "cr3":
				case "cr4":
				case "cr8":
					{
						operand = register;
						size = SizeSpecifier.QWORD;
					}
					break;
				case "eax":
				case "ebx":
				case "ecx":
				case "edx":
				case "esi":
				case "edi":
				case "r8d":
				case "r9d":
				case "r10d":
				case "r11d":
				case "r12d":
				case "r13d":
				case "r14d":
				case "r15d":
				case "esp":
				case "ebp":
					{
						operand = register.StartsWith('e') ? 'r' + register.Substring(1) : register.Replace("d", "");
						size = SizeSpecifier.DWORD;
					}
					break;
				case "ax":
				case "bx":
				case "cx":
				case "dx":
				case "si":
				case "di":
				case "r8w":
				case "r9w":
				case "r10w":
				case "r11w":
				case "r12w":
				case "r13w":
				case "r14w":
				case "r15w":
				case "sp":
				case "bp":
					{
						operand = register.StartsWith('r') ? register.Replace("w", "") :  'r' + register;
						size = SizeSpecifier.WORD;
					}
					break;
				case "al":
				case "ah":
				case "bl":
				case "bh":
				case "cl":
				case "ch":
				case "dl":
				case "dh":
					{
						operand = $"r{register[0]}x";
						size = SizeSpecifier.BYTE;
					}
					break;
				default:
					throw new LanguageException($"{register} is not recognised as a register.");
			}
			supportsByteAccess = registerNames[SizeSpecifier.BYTE].ContainsKey(operand.ToString());
		}

		public Operand(SizeSpecifier size, SymbolString operand, OperandType type, bool upgradeByteRegisters = false)
		{
			if(type != OperandType.Reference && size == SizeSpecifier.NONE)
				throw new InvalidOperationException("Attemted to construct Operand with undefined size specifier.");
			
			if(type == OperandType.Register)
			{
				String operandString = operand.ToString();
				if(upgradeByteRegisters && size == SizeSpecifier.BYTE && !registerNames[size].ContainsKey(operandString))
					size = SizeSpecifier.WORD; // upgrade to word operand if the target register can't be addressed as a byte
				if(!registerNames[size].ContainsKey(operandString))
					throw new LanguageException($"Can't resize '{operandString}' to size {size}.");
				registerName = registerNames[size][operandString];
				supportsByteAccess = registerNames[SizeSpecifier.BYTE].ContainsKey(operandString);
			}

			this.size = size;
			this.operand = operand;
			this.type = type;
		}

		public Operand(Operand original, SizeSpecifier newSize) : this(newSize, original.operand, original.type) { }
		public Operand Resize(SizeSpecifier newSize) => new Operand(this, newSize);

		public static String SizeToNasm(SizeSpecifier size)
		{
			switch(size)
			{
				case SizeSpecifier.BYTE:
					return "byte";
				case SizeSpecifier.WORD:
					return "word";
				case SizeSpecifier.DWORD:
					return "dword";
				case SizeSpecifier.QWORD:
					return "qword";
				default:
					throw new InvalidOperationException("Attempted to convert undefined size specifier to its nasm representation.");
			}
		}

		public override string ToString() => NasmRep.ToString();
	}
}
