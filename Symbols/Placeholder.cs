using System;
using System.Linq;

namespace OrganisedAssembly
{
	class Placeholder : Symbol
	{
		public override String Nasm => throw new InvalidOperationException("Attempted to convert placeholder symbol to its NASM equivalent.");

		protected readonly Identifier name;
		protected readonly GlobalScope scope;
		protected readonly Func<Placeholder, Symbol> resolve;

		public Identifier Name => name;

		protected bool resolved = false;
		protected Symbol result = null;
		public Symbol Result => resolved ? result : throw new InvalidOperationException("Attempted to obtain the result of an unresolved placeholder symbol.");

		public Placeholder(Identifier name, GlobalScope scope, Func<Placeholder, Symbol> resolve)
		{
			if(name == null || scope == null || resolve == null)
				throw new ArgumentNullException();
			this.name = name;
			this.scope = scope;
			this.resolve = resolve;
		}

		public Placeholder(Func<Placeholder, Symbol> resolve)
		{
			name = null;
			scope = null;
			this.resolve = resolve;
		}

		public void Resolve()
		{
			if(resolved)
				throw new InvalidOperationException("Attempted to resolve placeholder symbol twice.");
			result = resolve(this);
			resolved = true;
			scope?.ReplacePlaceholder(name, result);
		}
	}

	class FunctionPlaceholder : Placeholder
	{
		public readonly (ValueType type, String name)[] parameters;

		public FunctionPlaceholder(Identifier name, (ValueType type, String name)[] parameters, GlobalScope scope, Func<Placeholder, Symbol> resolve)
			: base(name, scope, resolve)
			=> this.parameters = parameters.Select(x => (new ValueType(x.type), x.name)).ToArray(); // deep-copy the parameter types
	}

	class StructLayoutSymbol : Placeholder
	{
		public int size = 0;
		public Placeholder[] dependencies = null;
		public ValueType[] fieldTypes = null;

		public StructLayoutSymbol(Action<StructLayoutSymbol> action) : base(x =>
		{
			action((StructLayoutSymbol)x);
			return null;
		})
		{ }
	}
}
