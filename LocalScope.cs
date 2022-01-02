using System;
using System.Collections.Generic;

namespace OrganisedAssembly
{
	class LocalScope : BaseScope, Scope
	{
		public Stack Stack { get; protected set; }
		public String Name { get; protected set; } = null;
		public bool IsAnonymous => Name == null;
		protected Dictionary<String, Symbol> symbols = new Dictionary<String, Symbol>();
		public int VariableStackSize { get; protected set; } = 0;

		public LocalScope(String name)
		{
			Name = name;
			Stack = new Stack();
		}

		public LocalScope(Stack stack) => Stack = stack;

		/// <summary>
		/// Declares a new variable on the stack.
		/// </summary>
		public virtual void DeclareVariable(ValueType type, String name)
		{
			if(symbols.ContainsKey(name))
				throw new VariableException($"Attempted to redefine variable or constant {name}.");
			VariableStackSize += type.Size;
			Stack.Pointer -= type.Size;
			symbols[name] = new StackSymbol(Stack, Stack.Pointer, type);
		}

		/// <summary>
		/// Declares a variable that already exists on the stack.
		/// </summary>
		/// <param name="offset">Offset relative to the current stack pointer.</param>
		public void DeclareVariable(ValueType type, String name, int offset)
		{
			if(symbols.ContainsKey(name))
				throw new VariableException($"Attempted to redefine variable or constant {name}.");
			symbols[name] = new StackSymbol(Stack, Stack.Pointer + offset, type);
		}

		/// <summary>
		/// Declares the space on the stack that will act as a dummy variable
		/// </summary>
		public void AllocateDummyVariable(int size)
		{
			if(size < 1)
				throw new InvalidOperationException("Attempted to allocate a non-positive amount of stack space.");
			VariableStackSize += size;
			Stack.Pointer -= size;
		}

		public bool SymbolExists(params String[] path)
		{
			if(path.Length != 1) // named local scopes cannot be nested
				return false;
			String name = path[0];
			return symbols.ContainsKey(name);
		}

		public Symbol GetSymbol(params String[] path)
		{
			if(path.Length != 1) // named local scopes cannot be nested
				return null;
			String name = path[0];
			if(symbols.ContainsKey(name))
				return symbols[name];
			else return null;
		}

		public void DeclareSymbol(String name, Symbol symbol)
		{
			if(symbols.ContainsKey(name))
				throw new VariableException($"Attempted to redefine variable or constant {name}.");
			symbols[name] = symbol;
		}

		protected override Scope CreateAnonymousScope() => new LocalScope(Stack);
	}
}
