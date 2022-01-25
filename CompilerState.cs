using System;
using System.Collections.Generic;
using System.Linq;

namespace OrganisedAssembly
{
	partial class Compiler : ICompiler
	{
		protected static long uid = 0;

		// fields defining the state of the compiler as captured by CompilerState
		protected readonly Dictionary<String, Section> sections; // captured without deep-copy
		protected readonly Stack<Scope> scopeStack = new Stack<Scope>();
		protected readonly Stack<List<Scope>> usingStack = new Stack<List<Scope>>();
		protected readonly Stack<String> fileStack = new Stack<String>();
		protected readonly GlobalScope rootScope;

		// non-captured state
		protected readonly bool verbose = false;
		protected CompilationStep currentPass = CompilationStep.None;
		protected int currentLine = 0;
		protected int currentColumn = 0;

		protected TopologicalSort<PlaceholderSymbol> placeholders = new TopologicalSort<PlaceholderSymbol>();

		public ICompilerState GetState() => new CompilerState(this);

		protected Compiler(CompilerState state)
		{
			sections = state.sections;
			scopeStack = state.scopeStack;
			usingStack = state.usingStack;
			fileStack = state.fileStack;
			rootScope = state.rootScope;
		}

		protected class CompilerState : ICompilerState
		{
			public readonly Dictionary<String, Section> sections;

			public readonly Stack<Scope> scopeStack;
			public readonly Stack<List<Scope>> usingStack;
			public readonly Stack<String> fileStack;
			public readonly GlobalScope rootScope;

			public CompilerState(Compiler compiler)
			{
				// deep-copy the compiler state
				sections = compiler.sections;
				rootScope = compiler.rootScope;
				scopeStack = new Stack<Scope>(compiler.scopeStack);
				fileStack = new Stack<String>(compiler.fileStack);
				usingStack = new Stack<List<Scope>>(from list in compiler.usingStack select new List<Scope>(list));
			}

			public ICompiler Instantiate() => new Compiler(this);
		}
	}
}
