using System;
using System.Collections.Generic;

namespace OrganisedAssembly
{
	enum CompilationStep
	{
		None,
		DeclareGlobalSymbols,
		SolveGlobalSymbolDependencies,
		GenerateCode,
	}

	delegate void CompilerAction(ICompiler compiler, CompilationStep pass);

	interface ICompilerState // must only capture scope/file/symbol information, but not e.g. the current pass or current line
	{
		ICompiler Instantiate();
	}

	interface ICompiler // TODO: comment
	{
		/// <summary>
		/// Scope specific metadata.
		/// </summary>
		Dictionary<String, object> PersistentData { get; }

		CompilationStep CurrentPass { get; }
		
		String CurrentFile { get; }

		ICompilerState GetState(); // must return a deep copy of the current compiler state
		void Compile(IEnumerable<CompilerAction> program, CompilationStep upTo = CompilationStep.GenerateCode);

		void EnterFile(String file);
		void ExitFile();
		void UsingScope(params Identifier[] path);

		void EnterGlobal(params Identifier[] path);
		void ExitGlobal();
		Identifier[] GetCurrentPath();
		TypeSymbol GetCurrentAssociatedType();

		void EnterLocal(Identifier name);
		void ExitLocal();
		bool IsLocal { get; }

		void EnterAnonymous();
		void ExitAnonymous();
		
		long GetUID();

		void DeclarePosition(int line, int column);

		void DeclareVariable(ValueType type, Identifier name);
		void AllocateDummyVariable(int size);
		void DeclareExistingStackVariable(ValueType type, Identifier name, int offset);
		void DeclareConstant(Identifier name, String nasmRepresentation, ValueType type = null);
		void DeclareFunction(Identifier name, String label, FunctionMetadata metadata);
		PlaceholderSymbol DeclarePlaceholder(Identifier name, Func<Symbol> resolve);
		PlaceholderSymbol DeclareFunctionPlaceholder(Identifier name, Func<FunctionSymbol> resolve);
		void AddAnonymousPlaceholder(PlaceholderSymbol placeholder);
		void DeclareDependency(PlaceholderSymbol dependency, PlaceholderSymbol dependent);
		void DeclareType(Identifier name, TypeSymbol type);
		Operand SetRegisterAlias(Identifier name, String register);
		bool IsStackVariable(Identifier name);
		Symbol ResolveSymbol(UnresolvedPath path);
		Symbol ResolveSymbol(params Identifier[] path);

		int GetStackSize();
		void SetStackSize(int size);
		int GetMaxStackSize();
		void MoveStackPointer(int offset);
		void DeclareCall(); // declare that this local scope is not a leaf function
		bool IsLeaf();

		void Generate(SymbolString line, String section);
		void Generate(SymbolString futureLine, CompilerEvent compilerEvent, String section);
	}
}
