using System;
using System.Collections.Generic;

namespace OrganisedAssembly
{
	class LocalScope : BaseScope, Scope
	{
		public Stack Stack { get; protected set; }
		public Identifier Name { get; protected set; } = null;
		public bool IsAnonymous => Name == null;
		protected Dictionary<String, Symbol> symbols = new Dictionary<String, Symbol>();
		public int VariableStackSize { get; protected set; } = 0;

		public LocalScope(Identifier name)
		{
			Name = name;
			Stack = new Stack();
		}

		public LocalScope(Stack stack) => Stack = stack;

		/// <summary>
		/// Declares a new variable on the stack.
		/// </summary>
		public void DeclareVariable(ValueType type, Identifier name)
		{
			if(name.HasTemplateParams)
				throw new VariableException($"Attempted to use template parameters in the name of local {name}.");
			if(symbols.ContainsKey(name.name))
				throw new VariableException($"Attempted to redefine variable or constant {name}.");
			VariableStackSize += type.Size;
			Stack.Pointer -= type.Size;
			symbols[name.name] = new StackSymbol(Stack, Stack.Pointer, type);
		}

		/// <summary>
		/// Declares a variable that already exists on the stack.
		/// </summary>
		/// <param name="offset">Offset relative to the current stack pointer.</param>
		public void DeclareVariable(ValueType type, Identifier name, int offset)
		{
			if(name.HasTemplateParams)
				throw new VariableException($"Attempted to use template parameters in the name of local {name}.");
			if(symbols.ContainsKey(name.name))
				throw new VariableException($"Attempted to redefine variable or constant {name}.");
			symbols[name.name] = new StackSymbol(Stack, Stack.Pointer + offset, type);
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

		public bool SymbolExists(Identifier name)
		{
			if(name.HasTemplateParams)
				return false;
			return symbols.ContainsKey(name.name);
		}

		public Symbol GetSymbol(params Identifier[] path)
		{
			if(path.Length != 1) // named local scopes cannot be nested
				return null;
			if(path[0].HasTemplateParams)
				return null;
			return symbols.GetValueOrDefault(path[0].name);
		}

		public void DeclareSymbol(Identifier name, Symbol symbol)
		{
			if(name.HasTemplateParams)
				throw new VariableException($"Attempted to use template parameters in the name of local {name}.");
			if(symbols.ContainsKey(name.name))
				throw new VariableException($"Attempted to redefine variable or constant {name}.");
			symbols[name.name] = symbol;
		}

		protected override Scope CreateAnonymousScope() => new LocalScope(Stack);
	}
}
