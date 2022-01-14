using System;
using System.Linq;
using System.Text.Json;

namespace OrganisedAssembly
{
	class Identifier // TODO: make record
	{
		public readonly String name;
		public readonly Symbol[] templateParams;

		public bool HasTemplateParams => templateParams.Length != 0;

		public Identifier(String name, params Symbol[] templateParams)
		{
			this.name = name;
			this.templateParams = templateParams;
		}

		public static implicit operator Identifier(String name) => new Identifier(name);

		public override String ToString() => name;
	}

	class UnresolvedIdentifier
	{
		protected readonly String name;
		protected readonly UnresolvedPath[] templateParams;
		public bool HasTemplateParams => templateParams.Length != 0;

		public UnresolvedIdentifier(JsonProperty identifier)
		{
			name = identifier.GetNonterminal("identifierName")?.Flatten() ?? throw new LanguageException("Malformed identifier.");
			
			if(identifier.GetNonterminal("templateParameters") is JsonProperty templateParams)
				this.templateParams = (from path in templateParams.GetNonterminals("identifierPath") select new UnresolvedPath(path)).ToArray();
			else
				this.templateParams = new UnresolvedPath[0];
		}

		public Identifier Resolve(ICompiler compiler) => new Identifier(name, (from path in templateParams select compiler.ResolveSymbol(path)).ToArray());

		public Identifier ResolveParameterless()
		{
			if(HasTemplateParams)
				throw new InvalidOperationException("Attempted parameterless resolution on identifier with template parameters.");
			return name;
		}

		public override String ToString() => templateParams.Length == 0
				? name
				: $"{name}<{String.Join<UnresolvedPath>(',', templateParams)}>";
	}

	class UnresolvedPath
	{
		protected readonly UnresolvedIdentifier[] segments;
		public bool IsNull => segments.Length == 0;

		public UnresolvedPath(JsonProperty? identifierPath) => segments = identifierPath?.GetNonterminals("identifier").Select(x => new UnresolvedIdentifier(x)).ToArray()
																		  ?? new UnresolvedIdentifier[0];

		public Identifier[] Resolve(ICompiler compiler) => !IsNull
			? (from x in segments select x.Resolve(compiler)).ToArray()
			: throw new InvalidOperationException("Attempted to resolve a null path.");

		public Identifier[] ResolveParameterless()
		{
			foreach(UnresolvedIdentifier segment in segments)
				if(segment.HasTemplateParams)
					return null;
			return (from x in segments select x.ResolveParameterless()).ToArray();
		}

		public override String ToString() => String.Join<UnresolvedIdentifier>('.', segments);
	}
}
