using System;
using System.Linq;

namespace OrganisedAssembly
{
	class FunctionMetadata // TODO: return size?
	{
		public readonly (ValueType type, String name)[] parameters;

		public FunctionMetadata() => parameters = new (ValueType type, String name)[0];

		public FunctionMetadata((ValueType type, String name)[] parameters)
		{
			if(parameters == null)
				throw new ArgumentNullException();
			foreach((ValueType type, String name) in parameters)
				if(!type.WellDefined)
					throw new LanguageException($"Bad parameter type for parameter {name}.");
			this.parameters = parameters;
		}

		public FunctionMetadata((SizeSpecifier size, String name)[] parameters) => this.parameters = parameters.Select(x => (new ValueType(x.size), x.name)).ToArray();
	}
}
