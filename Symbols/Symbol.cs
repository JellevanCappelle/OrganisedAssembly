using System;
using System.Collections.Generic;
using System.Linq;

namespace OrganisedAssembly
{
	abstract class Symbol
	{
		public abstract String Nasm { get; }
		public virtual SizeSpecifier Size => SizeSpecifier.NONE;

		public static SymbolString operator +(String A, Symbol B) => new SymbolString(new Symbol[] { new ConstantSymbol(A), B });
		public static SymbolString operator +(Symbol A, String B) => new SymbolString(new Symbol[] { A, new ConstantSymbol(B) });
		public static SymbolString operator +(Symbol A, Symbol B) => new SymbolString(new Symbol[] { A, B });

		public static implicit operator Symbol(String constant) => new ConstantSymbol(constant);
	}

	abstract class TypedSymbol : Symbol
	{
		protected readonly ValueType type;
		public ValueType Type => type;
		public override SizeSpecifier Size => type.SizeSpec;

		public TypedSymbol() => type = new ValueType(SizeSpecifier.NONE);
		public TypedSymbol(ValueType type)
		{
			if(type == null)
				type = new ValueType(SizeSpecifier.NONE);
			else if(!type.Defined)
				throw new InvalidOperationException("Attempted to create TypedSymbol with undefined ValueType.");
			this.type = type;
		}
	}

	class ConstantSymbol : TypedSymbol
	{
		protected readonly String constant;
		
		public override String Nasm => constant;
		
		public ConstantSymbol(String constant) : base() => this.constant = constant;
		public ConstantSymbol(String constant, ValueType type) : base(type) => this.constant = constant;

		public static implicit operator ConstantSymbol(String constant) => new ConstantSymbol(constant);

		public override bool Equals(object obj)
		{
			if(!(obj is ConstantSymbol))
				return false;
			ConstantSymbol other = obj as ConstantSymbol;
			return constant == other.constant;
		}

		public override int GetHashCode() => constant.GetHashCode();
	}

	class StackSymbol : TypedSymbol
	{
		protected readonly Stack stack;
		protected readonly int offset;
		
		public override String Nasm
		{
			get
			{
				int rspOffset = offset + stack.Size;
				return rspOffset >= 0 ? "rsp + " + rspOffset : "rsp - " + (-rspOffset);
			}
		}

		public StackSymbol(Stack stack, int offset, ValueType type) : base(type)
		{
			this.stack = stack;
			this.offset = offset;
		}
	}

	class AliasSymbol : Symbol
	{
		protected Register register;
		public Register Register => register;
		public override String Nasm => register.registerName;
		public override SizeSpecifier Size => register.size;

		public AliasSymbol(String register) => this.register = new Register(register);

		public void Assign(String register) => this.register = new Register(register);
	}

	class FunctionSymbol : ConstantSymbol
	{
		public FunctionMetadata Metadata { get; protected set; }

		public FunctionSymbol(String label, FunctionMetadata metadata) : base(label) => Metadata = metadata ?? throw new InvalidOperationException("Can't create FunctionSymbol without supplying metadata.");
	}

	class NonStaticMethodSymbol : FunctionSymbol
	{
		public NonStaticMethodSymbol(String label, FunctionMetadata metadata) : base(label, metadata) { }
	}

	class DeferredSymbol : Symbol
	{
		protected readonly Func<String> function;
		
		public override string Nasm => function();

		public DeferredSymbol(Func<String> function) => this.function = function;
	}

	class SymbolString
	{
		protected readonly List<Symbol> symbols;
		public bool IsStackReference => symbols.Exists(symbol => symbol is StackSymbol);

		public SymbolString() => symbols = new List<Symbol>();
		public SymbolString(List<Symbol> symbols) => this.symbols = symbols;
		public SymbolString(IEnumerable<Symbol> symbols) => this.symbols = symbols.ToList();
		public SymbolString(Symbol symbol) : this(new List<Symbol> { symbol }) { }

		public static implicit operator SymbolString(Symbol symbol) => new SymbolString(symbol);
		public static implicit operator SymbolString(String constant) => new SymbolString(new ConstantSymbol(constant));
		public static implicit operator SymbolString(Operand operand) => operand.NasmRep;
		public static SymbolString operator +(SymbolString A, SymbolString B) => new SymbolString(A.symbols.Concat(B.symbols).ToList());
		public static SymbolString operator +(SymbolString A, Operand B) => A + B.NasmRep;
		public static SymbolString operator +(SymbolString A, Symbol B)
		{
			List<Symbol> symbols = new List<Symbol>(A.symbols);
			symbols.Add(B);
			return new SymbolString(symbols);
		}

		public override string ToString() => String.Join(' ', from symbol in symbols select symbol.Nasm);

		public override bool Equals(object obj)
		{
			SymbolString other;
			if(obj is SymbolString)
				other = obj as SymbolString;
			else if(obj is Symbol)
				other = obj as Symbol;
			else if(obj is String)
				other = obj as String;
			else
				return false;
			return symbols.SequenceEqual(other.symbols);
		}

		public override int GetHashCode() => symbols[0].GetHashCode(); // TODO: do better

		public static bool operator ==(SymbolString A, SymbolString B) => A.Equals(B);
		public static bool operator !=(SymbolString A, SymbolString B) => !A.Equals(B);

		/// <summary>
		/// Replaces every instance of AliasSymbol with a ConstantSymbol representing its NASM representation.
		/// </summary>
		public void ResolveAliases()
		{
			for(int i = 0; i < symbols.Count; i++)
				if(symbols[i] is AliasSymbol alias)
					symbols[i] = alias.Nasm;
		}
	}
}
