using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;

namespace OrganisedAssembly
{
	class SharedReference<T> where T : class
	{
		public T Value { get; set; } = default(T);
		
		public static implicit operator T (SharedReference<T> reference) => reference.Value;
	}

	partial class Compiler : ICompiler
	{
		public String CurrentFile => fileStack.Peek().file;
		public CompilationStep CurrentPass => currentPass;
		public bool IsLocal => CurrentScope is LocalScope;
		public bool IsAnonymous => CurrentScope.IsAnonymous;
		public Dictionary<String, object> PersistentData => CurrentScope.PersistentData;

		protected List<Scope> UsingScopes => fileStack.Peek().usings;
		protected Scope CurrentScope => scopeStack.Peek();
		protected LocalScope Local => CurrentScope as LocalScope;
		protected GlobalScope Global => CurrentScope as GlobalScope;

		public Compiler(Dictionary<String, StreamWriter> sections)
		{
			this.verbose = CompilerSettings.Verbose;
			this.sections = new Dictionary<String, Section>();
			foreach(KeyValuePair<String, StreamWriter> section in sections)
				this.sections[section.Key] = new Section(section.Value);
			scopeStack.Push(rootScope = new GlobalScope());
		}

		public void Compile(IEnumerable<CompilerAction> program, CompilationStep upTo = CompilationStep.GenerateCode)
		{
			int fileCount = fileStack.Count;
			int scopeCount = scopeStack.Count;

			if(program == null)
				throw new LanguageException("Attempted to compile a non-existent program.");
			
			try
			{
				if(currentPass == CompilationStep.None) // skip the 'None' step
					currentPass++;
				for(; currentPass <= upTo; currentPass++)
				{
					if(verbose) Console.WriteLine($"Running compilation step: {currentPass}... ");

					// create a new TopologicalSort if necessary
					if(currentPass == CompilationStep.DeclareGlobalSymbols)
						if(placeholdersOwner = placeholders.Value == null)
							placeholders.Value = new TopologicalSort<PlaceholderSymbol>();

					// run through each compiler action in the program
					foreach(CompilerAction action in program)
						action(this, currentPass);

					// resolve placeholders during the right pass, only if this is the owner of the TopologicalSort object
					if(placeholdersOwner && currentPass == CompilationStep.SolveGlobalSymbolDependencies)
					{
						foreach(PlaceholderSymbol placeholder in placeholders.Value.Sort())
							placeholder.Resolve();
						placeholders.Value = null;
					}

					// close files
					if(currentPass == CompilationStep.GenerateCode)
						foreach(Section section in sections.Values)
							section.Close();
				}
			}
			catch(LanguageException e)
			{
				e.AddLineInfo(CurrentFile, currentLine, currentColumn);
				ExceptionDispatchInfo.Throw(e); // rethrow with original stack trace
			}

			// do post-compilation sanity checks, ensure everything that had to be exited was exited
			if(fileStack.Count != fileCount)
				throw new InvalidOperationException("Compilation ended but not all / too many files were exited.");
			if(scopeStack.Count != scopeCount) // only the root scope should be left
				throw new InvalidOperationException("Compilation ended but not all / too many scopes were exited.");
		}

		// filenames must be unique
		public void EnterFile(String file)
		{
			if(currentPass == CompilationStep.DeclareGlobalSymbols)
				if(usingsDict.ContainsKey(file))
					throw new InvalidOperationException($"Attempted to enter file '{file}' multiple times during pass {currentPass}.");
				else
					fileStack.Push((file, usingsDict[file] = new List<Scope>()));
			else
				fileStack.Push((file, usingsDict[file]));
		}

		public void ExitFile()
		{
			if(fileStack.Count == 0)
				throw new InvalidOperationException("Attempted to exit file when not in a file.");
			fileStack.Pop();
		}

		public void UsingScope(params Identifier[] path)
		{
			if(currentPass == CompilationStep.DeclareGlobalSymbols)
				throw new InvalidOperationException($"Attempted to import a scope during pass {CurrentPass}.");
			if(fileStack.Count == 0)
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
			placeholders.Value.AddNode(placeholder);
			Global.Declare(name, placeholder, functionScope);
			return placeholder;
		}

		public void AddAnonymousPlaceholder(PlaceholderSymbol placeholder)
		{
			if(currentPass != CompilationStep.DeclareGlobalSymbols)
				throw new InvalidOperationException($"Attempted to declare a placeholder symbol during pass {currentPass}.");
			placeholders.Value.AddNode(placeholder);
		}

		public void DeclareDependency(PlaceholderSymbol dependency, PlaceholderSymbol dependent)
		{
			if(currentPass != CompilationStep.SolveGlobalSymbolDependencies)
				throw new InvalidOperationException($"Attempted to declare a symbol dependency during pass {currentPass}.");
			placeholders.Value.AddEdge(dependency, dependent);
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

		public void DeclareTemplate(Identifier name, Template template)
		{
			if(currentPass != CompilationStep.DeclareGlobalSymbols)
				throw new InvalidOperationException($"Attempted to declare a template during pass {currentPass}.");
			if(IsLocal)
				throw new LanguageException($"Attempted to declare a template in a local scope.");

			Global.Declare(name, template);
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
			if(currentPass == CompilationStep.DeclareGlobalSymbols)
				throw new InvalidOperationException($"Attempted to resolve a name ({String.Join<Identifier>('.', path)}) during  pass {currentPass}.");
			
			return ResolveSymbolInternal(path) ?? throw new LanguageException($"Attempted to reference non-existent variable, constant or function '{String.Join<Identifier>('.', path)}'.");
		}

		protected Symbol ResolveSymbolInternal(Identifier[] path)
		{
			foreach(Scope scope in scopeStack)
			{
				Symbol var = scope.GetSymbol(path);
				if(var != null)
					return var;
			}
			if(fileStack.Count > 0)
				foreach(Scope scope in UsingScopes)
				{
					Symbol var = scope.GetSymbol(path);
					if(var != null)
						return var;
				}
			return null;
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
