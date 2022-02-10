using System;
using System.Text.Json;

namespace OrganisedAssembly
{
	class ValueType
	{
		protected SizeSpecifier sizeSpec = SizeSpecifier.NONE;
		protected int size = 0;
		protected TemplateParameter typeParam = null;
		protected TypeSymbol type = null;
		protected Placeholder dependency = null;
		protected bool defined = false;

		public bool Defined => defined;
		public bool DefinedOrDependent => defined || dependency != null;
		public bool WellDefined => defined && sizeSpec != SizeSpecifier.NONE;
		public int Size => defined ? size : throw new InvalidOperationException("Value type undefined.");
		public SizeSpecifier SizeSpec => defined ? sizeSpec : throw new InvalidOperationException("Value type undefined.");
		public TypeSymbol Type => defined ? type : throw new InvalidOperationException("Value type undefined.");

		public ValueType(ValueType original) // copy constructor
		{
			sizeSpec = original.sizeSpec;
			size = original.size;
			typeParam = original.typeParam;
			type = original.type;
			defined = original.defined;
		}

		public ValueType(TypeSymbol type) => Solve(type);

		public ValueType(JsonProperty sizeOrType)
		{
			if(sizeOrType.Name == "sizeOrType") // use the child node if the nonterminal isn't already a sizeSpecifier or typeName
				sizeOrType = sizeOrType.GetChildNonterminal() ?? throw new LanguageException($"Malformed size or type specifier: {sizeOrType.Flatten()}");

			typeParam = TemplateParameter.Parse(sizeOrType);
		}

		public static implicit operator ValueType(SizeSpecifier size) => new ValueType(size);
		public ValueType(SizeSpecifier size)
		{
			this.size = (int)(sizeSpec = size);
			defined = true;
		}

		public ValueType(int arraySize)
		{
			size = arraySize;
			defined = true;
		}

		public void DeclareDependency(Placeholder dependent, ICompiler compiler)
		{
			if(typeParam != null && !defined)
			{
				if(dependency == null)
				{
					Symbol symbol = typeParam.ToSymbol(compiler);
					if(symbol is TypeSymbol type)
						Solve(type);
					else if(symbol is Placeholder dependency)
						this.dependency = dependency;
					else
						throw new LanguageException($"Not a type: {typeParam}.");
				}
				else if(!defined)
					compiler.DeclareDependency(dependency, dependent);
			}
		}

		public void ResolveDependency()
		{
			if(dependency != null)
			{
				Symbol symbol = dependency.Result;
				if(symbol is TypeSymbol type)
					Solve(type);
				else
					throw new LanguageException($"Not a type: {typeParam}.");
			}
		}

		// should only be called during code generation in a local scope
		public void Solve(ICompiler compiler)
		{
			if(!compiler.IsLocal || compiler.CurrentPass != CompilationStep.GenerateCode)
				throw new InvalidOperationException("Solve() can only be called in a local scope during code generation.");
			if(typeParam == null)
				return;

			Symbol type = typeParam.ToSymbol(compiler);
			if(type is TypeSymbol)
				Solve((TypeSymbol)type);
			else
				throw new LanguageException($"Not a type: {typeParam}.");
		}

		protected void Solve(TypeSymbol type)
		{
			if(defined)
				throw new InvalidOperationException($"Attempted to resolve the same type '{typeParam}' twice.");

			this.type = type;
			sizeSpec = type.Size;
			size = type.SizeOf;
			defined = true;
		}
	}
}
