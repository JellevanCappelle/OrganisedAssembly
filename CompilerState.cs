using System;
using System.Collections.Generic;
using System.Linq;

namespace OrganisedAssembly
{
	partial class Compiler : ICompiler
	{
		protected static long uid = 0;

		// fields defining the state of the compiler as captured by CompilerState
		protected readonly Dictionary<String, Section> sections; // captured by constructing new sections from the original ones
		protected readonly Dictionary<String, List<Scope>> usingsDict = new Dictionary<String, List<Scope>>(); // captured without deep-copy
		protected readonly Stack<Scope> scopeStack = new Stack<Scope>();
		protected readonly Stack<(String file, List<Scope> usings)> fileStack = new Stack<(String, List<Scope>)>(); // stack is deep-copied, contained lists are not
		protected readonly GlobalScope rootScope;
		protected readonly SharedReference<TopologicalSort<Placeholder>> placeholders = new SharedReference<TopologicalSort<Placeholder>>(); // captured without deep-copy

		// non-captured state
		protected readonly bool verbose = false;
		protected bool placeholdersOwner = false;
		protected CompilationStep currentPass = CompilationStep.None;
		protected int currentLine = 0;
		protected int currentColumn = 0;

		public ICompilerState GetState() => new CompilerState(this);

		protected Compiler(CompilerState state)
		{
			// deep-copy the compiler state
			sections = new Dictionary<string, Section>(
				from original in state.sections select new KeyValuePair<String, Section>(original.Key, new Section(original.Value))
				);
			usingsDict = state.usingsDict;
			rootScope = state.rootScope;
			placeholders = state.placeholders;
			scopeStack = new Stack<Scope>(state.scopeStack);
			fileStack = new Stack<(String, List<Scope>)>(state.fileStack);
		}

		protected class CompilerState : ICompilerState
		{
			public readonly Dictionary<String, Section> sections;
			public readonly Dictionary<String, List<Scope>> usingsDict;
			public readonly Scope[] scopeStack;
			public readonly (String, List<Scope>)[] fileStack;
			public readonly GlobalScope rootScope;
			public readonly SharedReference<TopologicalSort<Placeholder>> placeholders;

			public CompilerState(Compiler compiler)
			{
				if(compiler.IsLocal)
					throw new InvalidOperationException("Attempted to save compiler state while in a local scope.");

				// deep-copy the compiler state
				sections = compiler.sections;
				usingsDict = compiler.usingsDict;
				rootScope = compiler.rootScope;
				placeholders = compiler.placeholders;
				scopeStack = compiler.scopeStack.Reverse().ToArray(); // Stack<T>.ToArray() gives the elements in the opposite order in which they were pushed
				fileStack = compiler.fileStack.Reverse().ToArray();
			}

			public (ICompiler, GlobalScope) Instantiate()
			{
				Compiler compiler = new Compiler(this);
				GlobalScope scope = new GlobalScope(true, compiler.GetCurrentAssociatedType());
				compiler.scopeStack.Push(scope); // push an anonymous scope, so that any symbols defined in this compiler are invisible outside of it
				return (compiler, scope);
			}
		}
	}
}
