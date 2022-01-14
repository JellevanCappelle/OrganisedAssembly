using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;

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

	interface ICompiler // TODO: comment
	{
		/// <summary>
		/// Scope specific metadata.
		/// </summary>
		public Dictionary<String, object> PersistentData { get; }

		public CompilationStep CurrentPass { get; }
		
		public String CurrentFile { get; }

		void Compile();

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

	class Compiler : ICompiler
	{
		protected IEnumerable<CompilerAction> program;
		protected Dictionary<String, Section> sections;

		protected Stack<Scope> scopeStack = new Stack<Scope>();
		protected Stack<List<Scope>> usingStack = new Stack<List<Scope>>();
		protected Stack<String> fileStack = new Stack<String>();
		public String CurrentFile => fileStack.Peek();
		protected GlobalScope rootScope;
		protected long uid = 0;
		protected CompilationStep currentPass = CompilationStep.None;
		public CompilationStep CurrentPass => currentPass;
		protected int currentLine = 0;
		protected int currentColumn = 0;

		protected List<Scope> UsingScopes => usingStack.Peek();
		protected Scope CurrentScope => scopeStack.Peek();
		public bool IsLocal => CurrentScope is LocalScope;
		protected LocalScope Local => CurrentScope as LocalScope;
		protected GlobalScope Global => CurrentScope as GlobalScope;
		public bool IsAnonymous => CurrentScope.IsAnonymous;
		public Dictionary<String, object> PersistentData => CurrentScope.PersistentData;

		protected TopologicalSort<PlaceholderSymbol> placeholders = new TopologicalSort<PlaceholderSymbol>();

		public Compiler(IEnumerable<CompilerAction> program, Dictionary<String, StreamWriter> sections)
		{
			this.program = program;
			this.sections = new Dictionary<String, Section>();
			foreach(KeyValuePair<String, StreamWriter> section in sections)
				this.sections[section.Key] = new Section(section.Value);
			scopeStack.Push(rootScope = new GlobalScope());
		}

		public void Compile()
		{
			if(program == null)
				throw new LanguageException("Attempted to compile a non-existent program.");
			try
			{
				CompilationStep[] passes = {
					CompilationStep.DeclareGlobalSymbols,
					CompilationStep.SolveGlobalSymbolDependencies,
					CompilationStep.GenerateCode
				};
				foreach(CompilationStep pass in passes)
				{
					currentPass = pass;
					Console.Write($"Running compilation step: {currentPass}... ");
					foreach(CompilerAction action in program)
						action(this, pass);

					// resolve placeholders during the appropriate pass
					if(pass == CompilationStep.SolveGlobalSymbolDependencies)
						foreach(PlaceholderSymbol placeholder in placeholders.Sort())
							placeholder.Resolve();

					Console.WriteLine("Done!");
				}
			}
			catch(LanguageException e)
			{
				e.AddLineInfo(CurrentFile, currentLine, currentColumn);
				ExceptionDispatchInfo.Throw(e); // rethrow with original stack trace
			}
			foreach(Section section in sections.Values)
				section.Close();
			program = null;

			// do post-compilation sanity checks, ensure everything that had to be exited was exited
			if(usingStack.Count > 0)
				throw new InvalidOperationException("Compilation ended but not all files were exited.");
			if(scopeStack.Count != 1) // only the root scope should be left
				throw new InvalidOperationException("Compilation ended but not all scopes were exited.");
		}

		public void EnterFile(String file)
		{
			fileStack.Push(file);
			usingStack.Push(new List<Scope>());
		}

		public void ExitFile()
		{
			if(usingStack.Count == 0)
				throw new InvalidOperationException("Attempted to exit file when not in a file.");
			fileStack.Pop();
			usingStack.Pop();
		}

		public void UsingScope(params Identifier[] path)
		{
			if(usingStack.Count == 0)
				throw new InvalidOperationException("Attempted to import a scope while not in a file.");
			GlobalScope scope = rootScope.GetSubScope(path) ?? throw new LanguageException($"Attmepted to use non-existent global scope {String.Join<Identifier>('.', path)}.");
			UsingScopes.Add(scope);
		}

		public void EnterGlobal(params Identifier[] path)
		{
			if(IsLocal)
				throw new LanguageException("Cannot enter global scope while in a local scope.");

			// when a scope path exists of more than one scope, all of the parent scopes should be created if necessary, but none of them should be pushed on the stack
			GlobalScope scope = (GlobalScope)CurrentScope;
			foreach(Identifier segment in path)
				scope = scope.GetSubScope(segment) ?? new GlobalScope(segment, scope);

			// 'scope' now contains the last scope in the path, this is the one to actually enter
			scopeStack.Push(scope);
		}

		public void ExitGlobal()
		{
			if(IsLocal || scopeStack.Count == 1) // make sure the root scope is never exited
				throw new InvalidOperationException("Attempted to exit global scope while not in a global scope.");

			CurrentScope.ResetAnonymous();
			scopeStack.Pop();
		}

		public Identifier[] GetCurrentPath() => (from scope in scopeStack.Reverse() where scope.Name != null select scope.Name).ToArray();

		public TypeSymbol GetCurrentAssociatedType() => !IsLocal ? Global.AssociatedType : throw new InvalidOperationException("Attempted to obtain associated type of a local scope.");

		public void EnterLocal(Identifier name)
		{
			if(IsLocal)
				throw new InvalidOperationException($"Attempted to enter local scope {name} while already in local scope {String.Join<Identifier>('.', GetCurrentPath())}.");

			LocalScope scope = Global.GetLocalScope(name);
			if(scope == null)
				if(currentPass == CompilationStep.DeclareGlobalSymbols)
					Global.AddLocalScope(scope = new LocalScope(name));
				else
					throw new InvalidOperationException($"Attempted to enter non-existent local scope in pass {currentPass}.");

			scopeStack.Push(scope);
		}

		public void ExitLocal()
		{
			if(!IsLocal)
				throw new InvalidOperationException("Attempted to exit local scope while not in a local scope.");

			if(Local.Stack.Pointer != -Local.VariableStackSize)
				throw new LanguageException("Attempted to exit local scope before clearing the stack.");

			Local.ResetAnonymous();
			scopeStack.Pop();
		}

		public void EnterAnonymous() => scopeStack.Push(CurrentScope.GetNextAnonymous());

		public void ExitAnonymous()
		{
			if(!IsAnonymous)
				throw new InvalidOperationException("Attempted to exit anonymous scope while not in one.");

			if(IsLocal) // do stack housekeeping for local scopes
				Local.Stack.Pointer += Local.VariableStackSize;

			CurrentScope.ResetAnonymous();
			scopeStack.Pop();
		}

		public long GetUID() => uid++;

		public void DeclarePosition(int line, int column)
		{
			currentLine = line;
			currentColumn = column;
		}

		public void DeclareVariable(ValueType type, Identifier name)
		{
			if(!IsLocal)
				throw new VariableException("Attempted to declare a local variable in a nonlocal scope.");
			Local.DeclareVariable(type, name);
		}

		public void AllocateDummyVariable(int size)
		{
			if(!IsLocal)
				throw new VariableException("Attempted to allocate stack space for a variable in a nonlocal scope.");
			Local.AllocateDummyVariable(size);
		}

		public void DeclareExistingStackVariable(ValueType type, Identifier name, int offset)
		{
			if(!IsLocal)
				throw new VariableException("Attempted to declare a local variable in a nonlocal scope.");
			Local.DeclareVariable(type, name, offset);
		}

		public void DeclareConstant(Identifier name, String nasmRepresentation, ValueType type = null)
			=> CurrentScope.DeclareSymbol(name, new ConstantSymbol(nasmRepresentation, type));

		public void DeclareFunction(Identifier name, String label, FunctionMetadata metadata)
		{
			if(IsLocal)
				throw new LanguageException("Attempted to declare function in local scope.");
			Global.Declare(name, new FunctionSymbol(label, metadata), new LocalScope(name));
		}

		public PlaceholderSymbol DeclarePlaceholder(Identifier name, Func<Symbol> resolve) => DeclarePlaceholder(name, resolve, null);
		public PlaceholderSymbol DeclareFunctionPlaceholder(Identifier name, Func<FunctionSymbol> resolve) => DeclarePlaceholder(name, resolve, new LocalScope(name));
		protected PlaceholderSymbol DeclarePlaceholder(Identifier name, Func<Symbol> resolve, LocalScope functionScope)
		{
			if(IsLocal)
				throw new InvalidOperationException("Attempted to declare a placeholder symbol in a local scope.");
			if(currentPass != CompilationStep.DeclareGlobalSymbols)
				throw new InvalidOperationException($"Attempted to declare a placeholder symbol during pass {currentPass}.");
			PlaceholderSymbol placeholder = new PlaceholderSymbol(name.name, Global, resolve);
			placeholders.AddNode(placeholder);
			Global.Declare(name, placeholder, functionScope);
			return placeholder;
		}

		public void AddAnonymousPlaceholder(PlaceholderSymbol placeholder)
		{
			if(currentPass != CompilationStep.DeclareGlobalSymbols)
				throw new InvalidOperationException($"Attempted to declare a placeholder symbol during pass {currentPass}.");
			placeholders.AddNode(placeholder);
		}

		public void DeclareDependency(PlaceholderSymbol dependency, PlaceholderSymbol dependent)
		{
			if(currentPass != CompilationStep.SolveGlobalSymbolDependencies)
				throw new InvalidOperationException($"Attempted to declare a symbol dependency during pass {currentPass}.");
			placeholders.AddEdge(dependency, dependent);
		}

		public void DeclareType(Identifier name, TypeSymbol type)
		{
			if(currentPass != CompilationStep.DeclareGlobalSymbols)
				throw new InvalidOperationException($"Attempted to declare a type during pass {currentPass}.");
			if(IsLocal)
				throw new LanguageException($"Attempted to declare a type in a local scope.");

			GlobalScope scope = new GlobalScope(name, Global, type);
			Global.Declare(name, type, scope);
			type.InitMemberScope(scope);
		}

		public Operand SetRegisterAlias(Identifier name, String register)
		{
			if(!IsLocal)
				throw new LanguageException("Attempted to set register alias in a global scope.");
			if(currentPass != CompilationStep.GenerateCode)
				throw new InvalidOperationException($"Attempted to set register alias during pass {currentPass}.");

			if(CurrentScope.GetSymbol(name) is AliasSymbol existing)
			{
				existing.Assign(register);
				return existing.Register;
			}
			else
			{
				AliasSymbol newAlias = new AliasSymbol(register);
				CurrentScope.DeclareSymbol(name, newAlias);
				return newAlias.Register;
			}
		}

		public bool IsStackVariable(Identifier name) => IsLocal && Local.SymbolExists(name);

		public Symbol ResolveSymbol(UnresolvedPath path) => ResolveSymbol(path.Resolve(this));
		public Symbol ResolveSymbol(params Identifier[] path)
		{
			if(currentPass != CompilationStep.GenerateCode && currentPass != CompilationStep.SolveGlobalSymbolDependencies)
				throw new InvalidOperationException($"Attempted to resolve a name ({String.Join<Identifier>('.', path)}) during  pass {currentPass}.");
			foreach(Scope scope in scopeStack)
			{
				Symbol var = scope.GetSymbol(path);
				if(var != null)
					return var;
			}
			if(usingStack.Count > 0)
				foreach(Scope scope in UsingScopes)
				{
					Symbol var = scope.GetSymbol(path);
					if(var != null)
						return var;
				}
			throw new LanguageException($"Attempted to reference non-existent variable, constant or function '{String.Join<Identifier>('.', path)}'.");
		}

		public int GetStackSize()
		{
			if(currentPass != CompilationStep.GenerateCode)
				throw new InvalidOperationException($"Attempted to obtain the stack size during pass {currentPass}.");
			if(!IsLocal)
				throw new LanguageException("Attempted to obtain total stack size while in a global scope.");
			return Local.Stack.Size;
		}

		public void SetStackSize(int stackSize)
		{
			if(currentPass != CompilationStep.GenerateCode)
				throw new InvalidOperationException($"Attempted to set the stack size during pass {currentPass}.");
			if(!IsLocal)
				throw new LanguageException("Attempted to set total stack size while in a global scope.");
			Local.Stack.Size = stackSize;

			// finally, fire the event
			foreach(Section section in sections.Values)
				section.FireEvent(CompilerEvent.StackSizeSet);
		}

		public int GetMaxStackSize()
		{
			if(!IsLocal)
				throw new LanguageException("Attempted to obtain max stack size while in a global scope.");
			return Local.Stack.MaxSize;
		}

		public void MoveStackPointer(int offset)
		{
			if(!IsLocal)
				throw new LanguageException("Attempted to move stack pointer while in a global scope.");
			if(currentPass != CompilationStep.GenerateCode)
				throw new InvalidOperationException($"Attempted to move the stack pointer during pass {currentPass}.");
			Local.Stack.Pointer += offset;
		}

		public void DeclareCall()
		{
			if(!IsLocal)
				return; // ignore calls in global scopes
			Local.Stack.IsLeaf = false;
		}

		public bool IsLeaf()
		{
			if(!IsLocal)
				throw new LanguageException("Attempted to test whether a global scope is a leaf function.");
			return Local.Stack.IsLeaf;
		}

		public void Generate(SymbolString line, String section)
		{
			if(!sections.ContainsKey(section)) throw new LanguageException($"Attempted to write to non-existent section {section}.");
			line.ResolveAliases(); // ensure that any aliased registers are correct, even in the future when they might have been changed
			if(!line.IsStackReference)
				sections[section].Generate(line.ToString());
			else
				sections[section].Generate(line, CompilerEvent.StackSizeSet);
		}

		public void Generate(SymbolString futureLine, CompilerEvent compilerEvent, String section)
		{
			if(!sections.ContainsKey(section)) throw new LanguageException($"Attempted to write to non-existent section {section}.");
			sections[section].Generate(futureLine, compilerEvent);
		}
	}
}
