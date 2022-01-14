using System;
using System.Linq;
using System.Text.Json;

namespace OrganisedAssembly
{
	class ValueType
	{
		protected SizeSpecifier sizeSpec = SizeSpecifier.NONE;
		protected int size = 0;
		protected UnresolvedPath typeName = null;
		protected PlaceholderSymbol dependency = null;
		protected TypeSymbol type;
		protected bool defined = false;

		public bool Defined => defined;
		public bool WellDefined => defined && sizeSpec != SizeSpecifier.NONE;
		public int Size => defined ? size : throw new InvalidOperationException("Value type undefined.");
		public SizeSpecifier SizeSpec => defined ? sizeSpec : throw new InvalidOperationException("Value type undefined.");
		public TypeSymbol Type => defined ? type : throw new InvalidOperationException("Value type undefined.");

		public ValueType(TypeSymbol type) => Solve(type);

		public ValueType(JsonProperty sizeOrType)
		{
			if(sizeOrType.Name == "sizeOrType") // use the child node if the nonterminal isn't already a sizeSpecifier or typeName
				sizeOrType = sizeOrType.GetChildNonterminal() ?? throw new LanguageException($"Malformed size specifier: {sizeOrType.Flatten()}");

			if(sizeOrType.Name == "sizeSpecifier")
			{
				size = (int)(sizeSpec = BaseConverter.ParseSize(sizeOrType.Flatten()));
				defined = true;
			}
			else if(sizeOrType.Name == "identifierPath")
				typeName = new UnresolvedPath(sizeOrType);
			else
				throw new LanguageException($"Unexpected sub-rule name in sizeOrType: {sizeOrType.Name}.");
		}

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

		public void DeclareDependency(PlaceholderSymbol dependent, ICompiler compiler)
		{
			if(typeName != null)
			{
				Symbol type = compiler.ResolveSymbol(typeName);
				if(type is TypeSymbol)
					Solve((TypeSymbol)type);
				else if(type is PlaceholderSymbol)
				{
					dependency = (PlaceholderSymbol)type;
					compiler.DeclareDependency(dependency, dependent);
				}
				else
					throw new LanguageException($"Not a type: {typeName}.");
			}
		}

		public void ResolveDependency()
		{
			if(dependency != null)
			{
				Symbol type = dependency.Result;
				if(type is TypeSymbol)
					Solve((TypeSymbol)type);
				else
					throw new LanguageException($"Not a type: {typeName}.");
			}
		}

		// should only be called during code generation in a local scope
		public void Solve(ICompiler compiler)
		{
			if(!compiler.IsLocal || compiler.CurrentPass != CompilationStep.GenerateCode)
				throw new InvalidOperationException("Solve() can only be called in a local scope during code generation.");
			if(typeName == null)
				return;

			Symbol type = compiler.ResolveSymbol(typeName.Resolve(compiler));
			if(type is TypeSymbol)
				Solve((TypeSymbol)type);
			else
				throw new LanguageException($"Not a type: {typeName}.");
		}

		protected void Solve(TypeSymbol type)
		{
			this.type = type;
			sizeSpec = type.Size;
			size = type.sizeOfValue;
			defined = true;
		}
	}
}
