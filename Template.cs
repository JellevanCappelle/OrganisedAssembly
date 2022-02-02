using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;

namespace OrganisedAssembly
{
	class Template
	{
		protected readonly TemplateName name;
		protected readonly Func<IEnumerable<CompilerAction>> generateCode;
		protected readonly Dictionary<Symbol[], TemplateInstance> instances = new Dictionary<Symbol[], TemplateInstance>(new ArrayEqualityComparer<Symbol>());

		protected ICompilerState state;
		protected CompilationStep currentPass = CompilationStep.None;
		protected IEnumerable<CompilerAction> code = null;
		protected IEnumerable<CompilerAction> Code => code = code ?? generateCode();

		public Template(TemplateName name, IEnumerable<CompilerAction> code)
		{
			this.name = name;
			this.code = code;
		}

		public Template(TemplateName name, Func<IEnumerable<CompilerAction>> generateCode)
		{
			this.name = name;
			this.generateCode = generateCode;
		}

		public void Action(ICompiler compiler, CompilationStep pass)
		{
			if((currentPass = pass) == CompilationStep.DeclareGlobalSymbols)
			{
				state = compiler.GetState();
				compiler.DeclareTemplate(name.name, this);
			}
			else // can't have any instances yet during DeclareGlobalSymbols
				foreach(TemplateInstance instance in instances.Values)
					instance.Compile(pass);
		}

		public SymbolAndOrScope this[params Symbol[] parameters]
		{
			get
			{
				if(parameters.Length != name.parameterNames.Length)
					throw new LanguageException("Attempted to instantiate template with incorrent number of parameters.");

				if(instances.GetValueOrDefault(parameters) is TemplateInstance existing)
					return existing;
				else
				{
					TemplateInstance instance = new TemplateInstance(name, parameters, Code, state);
					instance.Compile(currentPass);
					return instances[parameters] = instance;
				}
			}
		}

		public override String ToString() => name.ToString();
	}

	class TemplateInstance
	{
		protected readonly TemplateName name; // name of the symbol/scope being templated
		protected readonly ICompiler compiler;
		protected readonly GlobalScope lookup;
		protected readonly IEnumerable<CompilerAction> code;

		public TemplateInstance(TemplateName name, Symbol[] parameters, IEnumerable<CompilerAction> code, ICompilerState state)
		{
			this.name = name;
			this.code = code;
			(compiler, lookup) = state.Instantiate();

			// initialise the scope with the template parameters
			foreach((String param, Symbol value) in name.parameterNames.Zip(parameters))
				lookup.DeclareSymbol(param, value);

			if(CompilerSettings.Verbose)
			{
				String path = String.Join<Identifier>('.', compiler.GetCurrentPath());
				if(path.Length > 0) path += '.';
				Console.WriteLine($"Instantiating template: '{path}{name}'");
			}
		}

		public void Compile(CompilationStep upTo) => compiler.Compile(code, upTo);

		public static implicit operator SymbolAndOrScope(TemplateInstance template) => template.lookup[template.name.name];

		public override String ToString() => name.ToString();
	}

	class TemplateName
	{
		public bool HasTemplateParams => parameterNames.Length != 0;
		
		public readonly String name;
		public readonly String[] parameterNames;

		public TemplateName(JsonProperty name)
		{
			this.name = name.GetNonterminal("name")?.Flatten()
						?? throw new LanguageException($"Malformed template name '{name.Flatten()}'.");
			parameterNames = name.GetNonterminal("templateDeclarationParameters") is JsonProperty parameters
				? (from param in parameters.GetNonterminals("name") select param.Flatten()).ToArray()
				: new String[0];
		}

		public TemplateName(String name, params String[] parameterNames)
		{
			this.name = name;
			this.parameterNames = parameterNames;
		}

		public override String ToString() => HasTemplateParams ? $"{name}<{String.Join(", ", parameterNames)}>" : name;
	}

	class ArrayEqualityComparer<T> : IEqualityComparer<T[]>
	{
		public bool Equals(T[] x, T[] y) => x?.SequenceEqual(y) ?? false;

		public int GetHashCode([DisallowNull] T[] array)
		{
			int hash = 0;
			foreach(T e in array) hash ^= e.GetHashCode();
			return hash;
		}
	}
}
