using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace OrganisedAssembly
{
	class Identifier // TODO: make record
	{
		public readonly String name;
		public readonly Symbol[] templateParams;

		public bool HasTemplateParams => templateParams?.Length != 0;

		// regular constructor
		public Identifier(String name, params Symbol[] templateParams)
		{
			this.name = name;
			this.templateParams = templateParams;
		}

		public static implicit operator Identifier(String name) => new Identifier(name);

		public override String ToString() => HasTemplateParams ? $"{name}<...>" : name;
	}

	class UnresolvedIdentifier
	{
		protected readonly String name;
		protected readonly TemplateParameter[] templateParams;
		public bool HasTemplateParams => templateParams.Length != 0;

		public UnresolvedIdentifier(JsonProperty identifier)
		{
			name = identifier.GetNonterminal("name")?.Flatten()
				   ?? throw new LanguageException("Malformed identifier.");
			
			if(identifier.GetNonterminal("templateParamList") is JsonProperty templateParams)
				this.templateParams = (from param in templateParams.GetNonterminals("templateParam") select TemplateParameter.Parse(param)).ToArray();
			else
				this.templateParams = new UnresolvedPath[0];
		}

		public UnresolvedIdentifier(String name)
		{
			this.name = name;
			templateParams = new UnresolvedPath[0];
		}

		public Identifier Resolve(ICompiler compiler) => new Identifier(name, (from param in templateParams select param.ToSymbol(compiler)).ToArray());

		public Identifier ResolveParameterless()
		{
			if(HasTemplateParams)
				throw new InvalidOperationException("Attempted parameterless resolution on identifier with template parameters.");
			return name;
		}

		public override String ToString() => templateParams.Length == 0
				? name
				: $"{name}<{String.Join<object>(',', templateParams)}>";
	}

	abstract class TemplateParameter // TODO combine this with ValueType.cs, allow declaring dependencies etc...
	{
		public abstract Symbol ToSymbol(ICompiler compiler); // might return a placeholder

		public static TemplateParameter Parse(JsonProperty param)
		{
			if(param.Name == "templateParam" || param.Name == "sizeOrType")
				param = param.GetChildNonterminal()
						?? throw new LanguageException("Malformed template parameter.");

			switch(param.Name)
			{
				case "identifierPath":
					return new UnresolvedPath(param);
				case "sizeSpecifier":
					return new SizeParam(ProgramConverter.ParseSize(param.Flatten()));
				case "refType":
				case "valueType":
					return new RefOrValuePath(param);
				default:
					throw new LanguageException("Malformed template parameter.");
			}
		}
	}

	class UnresolvedPath : TemplateParameter
	{
		protected readonly UnresolvedIdentifier[] segments;
		public bool IsNull => segments.Length == 0;

		public UnresolvedPath(JsonProperty? path)
			=> segments = path?.Name == "identifierPath" // otherwise assume namePath or null
			? path?.GetNonterminals("identifier").Select(x => new UnresolvedIdentifier(x)).ToArray()
			: path?.GetNonterminals("name").Select(x => new UnresolvedIdentifier(x.Flatten())).ToArray()
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

		public override Symbol ToSymbol(ICompiler compiler) => compiler.ResolveSymbol(Resolve(compiler));

		public override String ToString() => String.Join<UnresolvedIdentifier>('.', segments);
	}

	class RefOrValuePath : TemplateParameter
	{
		protected readonly bool reference; // if true, this is a referencing type, if false, a dereferencing type
		protected TemplateParameter param;

		public RefOrValuePath(JsonProperty refOrValue)
		{
			reference = refOrValue.Name == "refType"; // other option is "valueType"
			param = Parse(refOrValue.GetNonterminal("sizeOrType")
					?? throw new LanguageException("Malformed template parameter."));
			if(param is SizeParam && !reference)
				throw new LanguageException($"'{this}' is not a reference-type.");
		}

		public override Symbol ToSymbol(ICompiler compiler)
		{
			Symbol symbol = param.ToSymbol(compiler);
			if(symbol is TypeSymbol type)
				return ApplyRefOrValue(type);
			else if(symbol is Placeholder dependency)
			{
				Placeholder placeholder = new Placeholder(
					_ => ApplyRefOrValue(dependency.Result as TypeSymbol
										 ?? throw new LanguageException($"'{this}' is not a type.")));
				compiler.AddAnonymousPlaceholder(placeholder);
				compiler.DeclareDependency(dependency, placeholder);
				return placeholder;
			}
			else
				throw new LanguageException($"'{this}' is not a type or a placeholder.");
		}

		protected TypeSymbol ApplyRefOrValue(TypeSymbol type)
		{
			if(reference)
				return new ReferenceType(type);
			else if(type is ReferenceType refType)
				return refType.dereferenced;
			else
				throw new LanguageException($"'{this}' is not a reference-type.");
		}

		public override String ToString() => param.ToString();
	}

	class SizeParam : TemplateParameter
	{
		protected static readonly Dictionary<SizeSpecifier, TypeSymbol> symbols = new Dictionary<SizeSpecifier, TypeSymbol> {
				{ SizeSpecifier.BYTE, new TypeSymbol(SizeSpecifier.BYTE) },
				{ SizeSpecifier.WORD, new TypeSymbol(SizeSpecifier.WORD) },
				{ SizeSpecifier.DWORD, new TypeSymbol(SizeSpecifier.DWORD) },
				{ SizeSpecifier.QWORD, new TypeSymbol(SizeSpecifier.QWORD) },
			};

		protected readonly SizeSpecifier size;
		public SizeParam(SizeSpecifier size) => this.size = size != SizeSpecifier.NONE ? size : throw new InvalidOperationException("Attempted to use unspecified size as a template parameter.");

		public override Symbol ToSymbol(ICompiler compiler) => symbols[size];

		public override String ToString() => size.ToString();
	}
}
