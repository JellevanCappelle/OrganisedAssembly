using System;
using System.Collections.Generic;

namespace OrganisedAssembly
{
	class SymbolAndOrScope // TODO: make record
	{
		public readonly Symbol symbol;
		public readonly Scope scope;

		public SymbolAndOrScope(Symbol symbol, Scope scope = null)
		{
			this.symbol = symbol;
			this.scope = scope;
		}

		public SymbolAndOrScope(Scope scope)
		{
			symbol = null;
			this.scope = scope;
		}

		public static implicit operator SymbolAndOrScope(Symbol symbol) => new SymbolAndOrScope(symbol);
	}

	class GlobalScope : BaseScope, Scope
	{
		public Identifier Name { get; protected set; }
		public bool IsAnonymous { get; protected set; }
		public TypeSymbol AssociatedType { get; protected set; } = null;
		public String AbsoluteName => parent?.AbsoluteName != null ? parent.AbsoluteName + '.' + Name.ToString() : Name?.ToString();
		protected readonly GlobalScope parent = null;
		protected readonly Dictionary<String, SymbolAndOrScope> members = new Dictionary<String, SymbolAndOrScope>();
		protected readonly Dictionary<String, Template> templateMembers = new Dictionary<String, Template>();
		
		internal GlobalScope(bool anonymous = false, TypeSymbol type = null) // root / anonymous scope constructor
		{
			Name = null;
			IsAnonymous = anonymous;
			AssociatedType = type;
		}

		internal GlobalScope(Identifier name, GlobalScope parent)
		{
			if(name.HasTemplateParams)
				throw new LanguageException($"Attempted to use template parameters in the name of {name.name}.");
			Name = name.name;
			parent.AddSubScope(this);
			this.parent = parent;
		}

		internal GlobalScope(Identifier name, GlobalScope parent, TypeSymbol type)
		{
			if(name.HasTemplateParams)
				throw new LanguageException($"Attempted to use template parameters in the name of {name.name}.");
			Name = name.name;
			this.parent = parent;
			AssociatedType = type;
		}

		public SymbolAndOrScope this[Identifier identifier]
			=> templateMembers.GetValueOrDefault(identifier.name)?[identifier.templateParams]
			   ?? (identifier.HasTemplateParams ? null : members.GetValueOrDefault(identifier.name));

		protected void AddSubScope(GlobalScope scope)
		{
			if(scope.Name.HasTemplateParams)
				throw new InvalidOperationException("Attempted to add a sub scope with template parameters in its name.");
			if(members.ContainsKey(scope.Name.name))
				throw new LanguageException($"Attempted to redefine sub scope {scope.Name} in {AbsoluteName}");
			members[scope.Name.name] = new SymbolAndOrScope(scope);
		}

		internal GlobalScope GetSubScope(params Identifier[] path) => path.Length == 1
				? this[path[0]]?.scope as GlobalScope
				: (this[path[0]]?.scope as GlobalScope)?.GetSubScope(path[1..]);

		public void AddLocalScope(LocalScope local)
		{
			if(local.Name.HasTemplateParams)
				throw new InvalidOperationException("Attempted to add a local sub scope with template parameters in its name.");
			if(members.ContainsKey(local.Name.name))
				throw new LanguageException($"Attempted to redefine local sub scope {local.Name} in {AbsoluteName}");
			members[local.Name.name] = new SymbolAndOrScope(local);
		}

		public LocalScope GetLocalScope(Identifier name) => this[name]?.scope as LocalScope;

		public void DeclareSymbol(Identifier name, Symbol symbol)
		{
			if(name.HasTemplateParams)
				throw new VariableException($"Attempted to define global {name} with template parameters in its name.");
			if(members.ContainsKey(name.name))
				throw new VariableException($"Attempted to redefine variable or constant {name} in {AbsoluteName}.");
			members[name.name] = symbol;
		}

		public void Declare(Identifier name, Symbol symbol, Scope scope = null)
		{
			if(name.HasTemplateParams)
				throw new VariableException($"Attempted to define global '{name}' with template parameters in its name.");
			if(members.ContainsKey(name.name) || templateMembers.ContainsKey(name.name))
				throw new VariableException($"Attempted to redefine '{name}' in {AbsoluteName}.");
			members[name.name] = new SymbolAndOrScope(symbol, scope);
		}

		public void Declare(Identifier name, Template template)
		{
			if(name.HasTemplateParams)
				throw new VariableException($"Attempted to define global '{name}' with template parameters in its name.");
			if(members.ContainsKey(name.name) || templateMembers.ContainsKey(name.name))
				throw new VariableException($"Attempted to redefine '{name}' in {AbsoluteName}.");
			templateMembers[name.name] = template;
		}

		public void ReplacePlaceholder(Identifier name, Symbol symbol)
		{
			if(name.HasTemplateParams)
				throw new InvalidOperationException("Attempted to replace a placeholder symbol using a name with template arguments.");
			if(!members.ContainsKey(name.name))
				throw new InvalidOperationException($"Attempted to replace a non-existent placeholder {name} in {AbsoluteName}.");
			if(!(members[name.name].symbol is PlaceholderSymbol))
				throw new InvalidOperationException($"Attempted to replace a non-placeholder symbol {name} in {AbsoluteName}.");
			members[name.name] = new SymbolAndOrScope(symbol, members[name.name].scope);
		}

		public Symbol GetSymbol(params Identifier[] path)
			=> path.Length == 1
				? this[path[0]]?.symbol
				: GetSubScope(path[0])?.GetSymbol(path[1..]);

		protected override Scope CreateAnonymousScope() => new GlobalScope(true);
	}
}
