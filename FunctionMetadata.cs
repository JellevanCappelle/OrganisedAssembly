using System;
using System.Linq;

namespace OrganisedAssembly
{
	struct Parameter // TODO: change to readonly record struct
	{
		public readonly ValueType type;
		public readonly String name;
		public readonly bool isParams;

		public Parameter(ValueType type, String name) : this(type, name, false) { }
		public Parameter(SizeSpecifier size, String name) : this(new ValueType(size), name) { }
		public Parameter(TypeSymbol type, String name) : this(new ValueType(type), name) { }
		public Parameter(ValueType type, String name, bool isParams)
		{
			this.type = type;
			this.name = name;
			this.isParams = isParams;
		}
	}

	class FunctionMetadata // TODO: return size?
	{
		public readonly Parameter[] parameters;
		public readonly bool hasParams = false;

		public FunctionMetadata() => parameters = new Parameter[0];

		public FunctionMetadata(Parameter[] parameters)
		{
			if(parameters == null)
				throw new ArgumentNullException();
			
			foreach(Parameter p in parameters)
				if(!p.type.WellDefined)
					throw new LanguageException($"Bad parameter type for '{p.name}'.");
			
			hasParams = parameters[^1].isParams;
			foreach(Parameter p in parameters[..^1])
				if(p.isParams)
					throw new LanguageException($"'{p.name}' is not allowed to be a params parameter, because it isn't last.");
			
			this.parameters = parameters;
		}

		public FunctionMetadata((SizeSpecifier size, String name)[] parameters) => this.parameters = parameters.Select(x => new Parameter(x.size, x.name)).ToArray();
	}
}
