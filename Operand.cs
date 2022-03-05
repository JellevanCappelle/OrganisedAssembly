using System;
using System.Collections.Generic;
using System.Linq;

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

	enum BaseRegister
	{
		RAX,
		RBX,
		RCX,
		RDX,
		RSI,
		RDI,
		RSP,
		RBP,
		R8,
		R9,
		R10,
		R11,
		R12,
		R13,
		R14,
		R15,
		CR0,
		CR2,
		CR3,
		CR4,
		CR8
	}

	abstract class Operand
	{
		public readonly SizeSpecifier size;
		public abstract SymbolString Nasm { get; }

		public Operand(SizeSpecifier size) => this.size = size;

		public abstract bool OperandEquals(Operand other);

		public abstract Operand Resize(SizeSpecifier newSize); // should at least work whenever size != SizeSpecifier.NONE && newSize > size
	}

	sealed class Immediate : Operand
	{
		private readonly SymbolString immediate;
		public override SymbolString Nasm => size == SizeSpecifier.NONE ? immediate : size.ToNasm() + immediate;

		public override bool OperandEquals(Operand other) => other is Immediate otherImm ? immediate.Equals(otherImm.immediate) : false;

		public Immediate(SymbolString immediate, SizeSpecifier size = SizeSpecifier.NONE) : base(size) => this.immediate = immediate;

		public override Operand Resize(SizeSpecifier newSize) => new Immediate(immediate, newSize);
	}

	sealed class MemoryAddress : Operand
	{
		public readonly bool isAccess;
		public readonly SymbolString address;
		public override SymbolString Nasm => isAccess ? (SymbolString)$"{size.ToNasm()}" + "[" + address + "]" : throw new InvalidOperationException("Can't use an address as an operand directly.");

		public override bool OperandEquals(Operand other) => other is MemoryAddress otherMem ? address.Equals(otherMem.address) : false;

		public MemoryAddress(SymbolString address, SizeSpecifier size, bool isAccess = true) : base(size)
		{
			if(!isAccess && size != SizeSpecifier.NONE)
				throw new InvalidOperationException("Can't assign a size to an address.");
			this.address = address;
			this.isAccess = isAccess;
		}

		public override Operand Resize(SizeSpecifier newSize) => new MemoryAddress(address, newSize, isAccess);

		public IEnumerable<BaseRegister> EnumerateRegisters()
		{
			foreach(Symbol s in address.Symbols)
				if(s is RegisterSymbol register)
					yield return register.Register.baseRegister;
				else if(s is StackSymbol)
					yield return BaseRegister.RSP;
		}
	}

	sealed class Register : Operand
	{
		public readonly String registerName;
		public readonly BaseRegister baseRegister;
		public override SymbolString Nasm => registerName;

		public override bool OperandEquals(Operand other) => other is Register otherReg ? baseRegister == otherReg.baseRegister : false;

		public Register(BaseRegister register, SizeSpecifier size) : base(size) // TODO: upgradeSize edition
		{
			baseRegister = register;
			registerName = registerNames[(register, size)];
		}

		private Register((BaseRegister register, SizeSpecifier size) tuple) : base(tuple.size) => baseRegister = tuple.register;
		public Register(String register) : this(nameToBase[register]) => registerName = register;

		public override Operand Resize(SizeSpecifier newSize) => new Register(baseRegister, newSize);

		private static Dictionary<(BaseRegister, SizeSpecifier), String> registerNames = new Dictionary<(BaseRegister, SizeSpecifier), String>
		{
			// qword
			{ (BaseRegister.RAX, SizeSpecifier.QWORD), "rax" },
			{ (BaseRegister.RBX, SizeSpecifier.QWORD), "rbx" },
			{ (BaseRegister.RCX, SizeSpecifier.QWORD), "rcx" },
			{ (BaseRegister.RDX, SizeSpecifier.QWORD), "rdx" },
			{ (BaseRegister.RSI, SizeSpecifier.QWORD), "rsi" },
			{ (BaseRegister.RDI, SizeSpecifier.QWORD), "rdi" },
			{ (BaseRegister.RSP, SizeSpecifier.QWORD), "rsp" },
			{ (BaseRegister.RBP, SizeSpecifier.QWORD), "rbp" },
			{ (BaseRegister.R8, SizeSpecifier.QWORD), "r8" },
			{ (BaseRegister.R9, SizeSpecifier.QWORD), "r9" },
			{ (BaseRegister.R10, SizeSpecifier.QWORD), "r10" },
			{ (BaseRegister.R11, SizeSpecifier.QWORD), "r11" },
			{ (BaseRegister.R12, SizeSpecifier.QWORD), "r12" },
			{ (BaseRegister.R13, SizeSpecifier.QWORD), "r13" },
			{ (BaseRegister.R14, SizeSpecifier.QWORD), "r14" },
			{ (BaseRegister.R15, SizeSpecifier.QWORD), "r15" },
			{ (BaseRegister.CR0, SizeSpecifier.QWORD), "cr0" },
			{ (BaseRegister.CR2, SizeSpecifier.QWORD), "cr2" },
			{ (BaseRegister.CR3, SizeSpecifier.QWORD), "cr3" },
			{ (BaseRegister.CR4, SizeSpecifier.QWORD), "cr4" },
			{ (BaseRegister.CR8, SizeSpecifier.QWORD), "cr8" },

			// dword
			{ (BaseRegister.RAX, SizeSpecifier.DWORD), "eax" },
			{ (BaseRegister.RBX, SizeSpecifier.DWORD), "ebx" },
			{ (BaseRegister.RCX, SizeSpecifier.DWORD), "ecx" },
			{ (BaseRegister.RDX, SizeSpecifier.DWORD), "edx" },
			{ (BaseRegister.RSI, SizeSpecifier.DWORD), "esi" },
			{ (BaseRegister.RDI, SizeSpecifier.DWORD), "edi" },
			{ (BaseRegister.RSP, SizeSpecifier.DWORD), "esp" },
			{ (BaseRegister.RBP, SizeSpecifier.DWORD), "ebp" },
			{ (BaseRegister.R8, SizeSpecifier.DWORD), "r8d" },
			{ (BaseRegister.R9, SizeSpecifier.DWORD), "r9d" },
			{ (BaseRegister.R10, SizeSpecifier.DWORD), "r10d" },
			{ (BaseRegister.R11, SizeSpecifier.DWORD), "r11d" },
			{ (BaseRegister.R12, SizeSpecifier.DWORD), "r12d" },
			{ (BaseRegister.R13, SizeSpecifier.DWORD), "r13d" },
			{ (BaseRegister.R14, SizeSpecifier.DWORD), "r14d" },
			{ (BaseRegister.R15, SizeSpecifier.DWORD), "r15d" },

			// word
			{ (BaseRegister.RAX, SizeSpecifier.WORD), "ax" },
			{ (BaseRegister.RBX, SizeSpecifier.WORD), "bx" },
			{ (BaseRegister.RCX, SizeSpecifier.WORD), "cx" },
			{ (BaseRegister.RDX, SizeSpecifier.WORD), "dx" },
			{ (BaseRegister.RSI, SizeSpecifier.WORD), "si" },
			{ (BaseRegister.RDI, SizeSpecifier.WORD), "di" },
			{ (BaseRegister.RSP, SizeSpecifier.WORD), "sp" },
			{ (BaseRegister.RBP, SizeSpecifier.WORD), "bp" },
			{ (BaseRegister.R8, SizeSpecifier.WORD), "r8w" },
			{ (BaseRegister.R9, SizeSpecifier.WORD), "r9w" },
			{ (BaseRegister.R10, SizeSpecifier.WORD), "r10w" },
			{ (BaseRegister.R11, SizeSpecifier.WORD), "r11w" },
			{ (BaseRegister.R12, SizeSpecifier.WORD), "r12w" },
			{ (BaseRegister.R13, SizeSpecifier.WORD), "r13w" },
			{ (BaseRegister.R14, SizeSpecifier.WORD), "r14w" },
			{ (BaseRegister.R15, SizeSpecifier.WORD), "r15w" },

			// byte
			{ (BaseRegister.RAX, SizeSpecifier.BYTE), "al" },
			{ (BaseRegister.RBX, SizeSpecifier.BYTE), "bl" },
			{ (BaseRegister.RCX, SizeSpecifier.BYTE), "cl" },
			{ (BaseRegister.RDX, SizeSpecifier.BYTE), "dl" },
		};

		private static Dictionary<String, (BaseRegister, SizeSpecifier)> nameToBase = new Dictionary<String, (BaseRegister, SizeSpecifier)>((from x in registerNames select new KeyValuePair<String, (BaseRegister, SizeSpecifier)>(x.Value, x.Key)).Concat(
			new KeyValuePair<String, (BaseRegister, SizeSpecifier)>[]
			{
				new KeyValuePair<String, (BaseRegister, SizeSpecifier)>("ah", (BaseRegister.RAX, SizeSpecifier.BYTE)),
				new KeyValuePair<String, (BaseRegister, SizeSpecifier)>("bh", (BaseRegister.RBX, SizeSpecifier.BYTE)),
				new KeyValuePair<String, (BaseRegister, SizeSpecifier)>("ch", (BaseRegister.RCX, SizeSpecifier.BYTE)),
				new KeyValuePair<String, (BaseRegister, SizeSpecifier)>("dh", (BaseRegister.RDX, SizeSpecifier.BYTE)),
			}
		));

		public static IEnumerable<String> Names => nameToBase.Keys;
	}
}
