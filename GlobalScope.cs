using System;
using System.Collections.Generic;

namespace OrganisedAssembly
{
	class GlobalScope : BaseScope, Scope
	{
		public String Name { get; protected set; }
		public bool IsAnonymous { get; protected set; }
		public TypeSymbol AssociatedType { get; protected set; } = null;
		public String AbsoluteName => parent?.AbsoluteName != null ? parent.AbsoluteName + '.' + Name : Name;
		protected readonly GlobalScope parent = null;
		protected readonly Dictionary<String, GlobalScope> subScopes = new Dictionary<String, GlobalScope>();
		protected readonly Dictionary<String, LocalScope> localScopes = new Dictionary<String, LocalScope>();
		protected readonly Dictionary<String, Symbol> constants = new Dictionary<String, Symbol>();

		internal GlobalScope(bool anonymous = false) // root / anonymous scope constructor
		{
			Name = null;
			IsAnonymous = anonymous;
		}

		internal GlobalScope(String name, GlobalScope parent)
		{
			Name = name;
			parent.AddSubScope(this);
			this.parent = parent;
		}

		internal GlobalScope(String name, GlobalScope parent, TypeSymbol type) : this(name, parent) => AssociatedType = type;

		protected void AddSubScope(GlobalScope scope)
		{
			if(subScopes.ContainsKey(scope.Name))
				throw new LanguageException($"Attempted to redefine sub scope {scope.Name} in {AbsoluteName}");
			subScopes[scope.Name] = scope;
		}

		internal GlobalScope GetSubScope(params String[] path)
			=> subScopes.ContainsKey(path[0])
				? path.Length == 1 ? subScopes[path[0]] : subScopes[path[0]].GetSubScope(path[1..])
				: null;

		public void AddLocalScope(LocalScope local)
		{
			if(localScopes.ContainsKey(local.Name))
				throw new LanguageException($"Attempted to redefine local sub scope {local.Name} in {AbsoluteName}");
			localScopes[local.Name] = local;
		}
		
		public LocalScope GetLocalScope(String name) => localScopes.GetValueOrDefault(name, null);

		public void DeclareSymbol(String name, Symbol symbol)
		{
			if(constants.ContainsKey(name))
				throw new VariableException($"Attempted to redefine variable or constant {name} in {AbsoluteName}.");
			constants[name] = symbol;
		}

		public void ReplacePlaceholder(String name, Symbol symbol)
		{
			if(!constants.ContainsKey(name))
				throw new InvalidOperationException($"Attempted to replace a nonexistent placeholder {name} in {AbsoluteName}.");
			if(!(constants[name] is PlaceholderSymbol))
				throw new InvalidOperationException($"Attempted to replace a non-placeholder symbol {name} in {AbsoluteName}.");
			constants[name] = symbol;
		}

		public bool SymbolExists(String[] path)
			=> path.Length == 1
				? constants.ContainsKey(path[0]) : GetSubScope(path[0..1])?.SymbolExists(path[1..]) == true;

		public Symbol GetSymbol(String[] path)
			=> path.Length == 1
				? constants.ContainsKey(path[0]) ? constants[path[0]] : null
				: GetSubScope(path[0])?.GetSymbol(path[1..]);

		protected override Scope CreateAnonymousScope() => new GlobalScope(true);
	}
}
