using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text.Json;

namespace OrganisedAssembly
{
	abstract partial class BaseConverter : ActionConverter
	{
		private static Dictionary<String, SizeSpecifier> sizeLookup = new Dictionary<String, SizeSpecifier>
		{
			{ "byte", SizeSpecifier.BYTE },
			{ "word", SizeSpecifier.WORD },
			{ "dword", SizeSpecifier.DWORD },
			{ "qword", SizeSpecifier.QWORD }
		};

		protected static Dictionary<SizeSpecifier, char> sizeToChar = new Dictionary<SizeSpecifier, char>
		{
			{ SizeSpecifier.BYTE, 'b' },
			{ SizeSpecifier.WORD, 'w' },
			{ SizeSpecifier.DWORD, 'd' },
			{ SizeSpecifier.QWORD, 'q' }
		};

		protected abstract void GenerateFunction(String name, (ValueType type, String name)[] parameters, JsonProperty body, LinkedList<CompilerAction> program);
		protected abstract void GenerateABICall(Operand function, FunctionMetadata metadata, Operand[] arguments, Operand[] returnTargets, ICompiler compiler);
		protected abstract void ConvertABIReturn(JsonProperty ret, LinkedList<CompilerAction> program);

		public IEnumerable<CompilerAction> ConvertTree(JsonProperty parseTree, String file)
		{
			LinkedList<CompilerAction> program = new LinkedList<CompilerAction>();
			program.AddLast((compiler, pass) => compiler.EnterFile(file));
			ConvertNode(parseTree, program, file);
			program.AddLast((compiler, pass) => compiler.ExitFile());
			return program;
		}

		protected bool NodeDependsOnStack(JsonProperty node, ICompiler compiler)
		{
			if(!compiler.IsLocal) // can't be stack dependent in a global scope
				return false;
			foreach(JsonProperty reference in node.GetNonterminalsRecursive("identifierPath"))
			{
				String[] path = reference.GetNonterminals("identifier").Select(x => x.Flatten()).ToArray();
				if(path.Length == 1) // stack variables can only consist of a single path segment
					if(compiler.IsStackVariable(path[0]))
						return true;
			}
			return false;
		}

		protected bool NodesDependOnStack(JsonProperty[] nodes, ICompiler compiler)
		{
			if(!compiler.IsLocal)
				return false;
			foreach(JsonProperty node in nodes)
				if(NodeDependsOnStack(node, compiler))
					return true;
			return false;
		}

		/// <summary>
		/// Resolves the node to its NASM representation. Does not set aliases if alias declarations are encountered.
		/// </summary>
		protected SymbolString Resolve(JsonProperty node, ICompiler compiler) => node.Resolve(new(String, Func<JsonProperty, Symbol>)[]
		{
			("singleQuoteString", str => StringToNasm(str.Flatten())),
			("sizeof", sizeOf => SizeOf(sizeOf, compiler).ToString()),
			("identifierPath", id => compiler.ResolveSymbol(new UnresolvedPath(id))),
			("aliasDecl", alias => compiler.ResolveSymbol(alias.GetNonterminal("identifier")?.Flatten() ?? throw new LanguageException($"Encountered malformed alias declaration: '{alias.Flatten()}'.")))
		});

		protected String GetLabelString(ICompiler compiler, String name)
		{
			return "L" + compiler.GetUID() + "_" + String.Join<Identifier>('_', compiler.GetCurrentPath()) + "_" + name;
		}

		public static SizeSpecifier ParseSize(String str)
		{
			if(!sizeLookup.ContainsKey(str))
				throw new LanguageException($"Bad size specifier: '{str}'.");
			return sizeLookup[str];
		}

		/// <summary>
		/// Returns the path if only a single symbol is referenced, or null otherwise.
		/// </summary>
		protected Identifier[] MemoryReferenceToPath(JsonProperty address, ICompiler compiler)
		{
			// verify that the address is a single identifier path
			if(address.GetNonterminal("segAddress") == null) return null;
			address = (JsonProperty)address.GetNonterminal("segAddress");
			if(address.GetNonterminal("segRegister") != null) return null;
			address = (JsonProperty)address.GetNonterminal("address");
			if(address.GetNonterminal("offsetMultiplier") != null) return null;
			address = (JsonProperty)address.GetNonterminal("baseOrOffset");
			if(address.GetNonterminal("gpRegister") != null) return null;
			address = (JsonProperty)address.GetNonterminal("expr");
			if(address.GetNonterminals("exprTerm").Count() > 1) return null;
			address = (JsonProperty)address.GetNonterminal("exprTerm");
			if(address.GetNonterminal("unaryOperator") != null) return null;
			if(address.GetNonterminal("exprValue") == null) return null;
			address = (JsonProperty)address.GetNonterminal("exprValue");
			if(address.GetNonterminal("identifierPath") == null) return null;
			
			address = (JsonProperty)address.GetChildNonterminal();
			return new UnresolvedPath(address).Resolve(compiler);
		}

		/// <summary>
		/// Checks if this expression is just a single path, and returns it if that's the case. Returns null otherwise.
		/// </summary>
		protected Identifier[] ExpressionToPath(JsonProperty expr, ICompiler compiler)
		{
			if(expr.GetNonterminal("exprTerm") == null) return null;
			expr = (JsonProperty)expr.GetNonterminal("exprTerm");
			if(expr.GetNonterminal("unaryOperator") != null || expr.GetNonterminal("exprValue") == null) return null;
			expr = (JsonProperty)expr.GetNonterminal("exprValue");
			if(expr.GetNonterminal("identifierPath") == null) return null;

			expr = (JsonProperty)expr.GetChildNonterminal();
			return new UnresolvedPath(expr).Resolve(compiler);
		}

		protected String StringToNasm(String literal)
		{
			String str = literal[1..^1]; // strip enclosing quotes
			List<String> result = new List<String>();
			while(str.Length > 0)
			{
				int i = str.IndexOf('\\');
				if(i == -1)
				{
					result.Add($"\"{str}\"");
					str = "";
				}
				else
				{
					if(str.Length == i + 1)
						throw new InvalidOperationException($"Failed to parse string '{literal}'.");
					char special = str[i + 1];
					String pre = str.Substring(0, i);
					str = str.Substring(i + 2);
					if(pre.Length > 0)
						result.Add($"\"{pre}\"");
					if(special == '\\') result.Add("'\\'");
					else if(special == 'n') result.Add("0ah");
					else if(special == 'r') result.Add("0dh");
					else if(special == 't') result.Add("09h");
					else if(special == 'u')
					{	// format: \u(<codepoint in hex>)
						LanguageException exception = new LanguageException($"Bad unicode escape character sequence.");
						if(str.Length < 3) throw exception;
						if(str[0] != '(') throw exception;
						int j = str.IndexOf(')');
						if(j < 2 || j > 9) throw exception;
						String codepoint = str[1..j];
						if(!codepoint.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))) throw exception;
						codepoint.PadLeft(8, '0');
						result.Add($"`\\U{codepoint}`");
						str = str.Substring(j + 1);
					}
					else
						throw new LanguageException($"Unknown escape character in '{literal}'.");
				}
			}
			return String.Join(", ", result);
		}

		protected void GenerateString(String label, String literal, ICompiler compiler)
		{
			String end = GetLabelString(compiler, "end_of_string");
			compiler.Generate($"{label}:", "data");
			compiler.Generate($"\td{sizeToChar[CompilerSettings.StringLengthSize]} {end} - {label} - {(int)CompilerSettings.StringLengthSize + 1}", "data"); // subtract additional bytes for the length field and the terminating 0.
			compiler.Generate($"\tdb {StringToNasm(literal)}, 0", "data");
			compiler.Generate($"{end}:", "data");
		}

		protected void GenerateCString(String label, String literal, ICompiler compiler)
		{
			compiler.Generate($"{label}:", "data");
			compiler.Generate($"\tdb {StringToNasm(literal)}, 0", "data");
		}

		protected int SizeOf(JsonProperty sizeOf, ICompiler compiler)
		{
			UnresolvedPath path = new UnresolvedPath(sizeOf.GetNonterminal("identifierPath")
								  ?? throw new LanguageException($"Malformed sizeof operator: '{sizeOf.Flatten()}'"));

			Symbol type = compiler.ResolveSymbol(path);
			if(type is TypeSymbol)
				return (type as TypeSymbol).SizeOfInstance;
			else
				throw new LanguageException($"'{path}' is not a type.");
		}

		/// <summary>
		/// Convert an argument parse tree to an operand.
		/// </summary>
		/// <param name="generate">Whether or not this call is allowed to generate code in the data section. This is useful for e.g. string literals and register aliases.</param>
		protected Operand ArgumentToOperand(JsonProperty arg, String name, ICompiler compiler, ValueType expectedType = null, bool generate = false) // TODO: check not only for the correct size, but also the correct type of the argument
		{
			if(expectedType != null)
				if(!expectedType.WellDefined)
					throw new InvalidOperationException($"Bad expected type for '{name}'.");

			arg = (JsonProperty)arg.GetChildNonterminal();
			if(arg.Name == "gpRegister")
			{
				Operand result = new Operand(arg.Flatten());
				if(expectedType != null && result.size != expectedType?.SizeSpec)
					throw new LanguageException($"Operand size mismatch for '{name}'.");
				return result;
			}
			else if(arg.Name == "refArgument")
			{
				if(expectedType != null && expectedType?.SizeSpec != SizeSpecifier.QWORD) // the argument is a pointer (QWORD)
					throw new LanguageException($"Operand size mismatch for '{name}'.");

				// memory address
				if(arg.GetNonterminal("segAddress") is JsonProperty address)
					return new Operand(SizeSpecifier.NONE, "[" + Resolve(address, compiler) + "]", OperandType.Reference);

				// single quoted string literal
				else if(arg.GetNonterminal("singleQuotedString") is JsonProperty cstr)
				{
					if(!generate)
						throw new InvalidOperationException("Attempted to parse string literal without enabling data generation.");
					String label = GetLabelString(compiler, "anonymous_c_string");
					GenerateCString(label, cstr.Flatten(), compiler);
					return new Operand(SizeSpecifier.NONE, $"[{label}]", OperandType.Reference);
				}

				// double quoted string literal
				else if(arg.GetNonterminal("doubleQuotedString") is JsonProperty str)
				{
					if(!generate)
						throw new InvalidOperationException("Attempted to parse string literal without enabling data generation.");
					String label = GetLabelString(compiler, "anonymous_string");
					GenerateString(label, str.Flatten(), compiler);
					return new Operand(SizeSpecifier.NONE, $"[{label}]", OperandType.Reference);
				}
				else
					throw new LanguageException($"Malformed reference argument: '{arg.Flatten()}'.");
			}
			else if(arg.Name == "memArgument")
			{
				// try explicit or expected size first
				SizeSpecifier? size = expectedType?.SizeSpec;
				if(arg.GetNonterminal("sizeOrType") is JsonProperty sizeOrType)
				{
					ValueType argType = new ValueType(sizeOrType);
					argType.Solve(compiler);
					size = argType.SizeSpec;
				}
				if(size != null)
					return new Operand((SizeSpecifier)size,
						"[" + Resolve((JsonProperty)arg.GetNonterminal("segAddress"), compiler) + "]",
						OperandType.Memory);

				// try to find implicit size
				if(MemoryReferenceToPath(arg, compiler) is Identifier[] path)
				{
					Symbol var = compiler.ResolveSymbol(path);
					SymbolString nasmRep = "[" + var + "]";
					if(var.Size == SizeSpecifier.NONE)
						throw new LanguageException($"No size specified for '{name}'.");
					return new Operand(var.Size, nasmRep, OperandType.Memory);
				}

				// no size specified at all
				throw new LanguageException($"No size specified for '{name}'.");
			}
			else if(arg.Name == "immArgument")
			{
				// check if it's an aliased register
				if(ExpressionToPath((JsonProperty)arg.GetNonterminal("expr"), compiler) is Identifier[] path)
					if(compiler.ResolveSymbol(path) is AliasSymbol alias)
						return alias.Register;

				// use explicit or expected size
				SizeSpecifier size = expectedType?.SizeSpec ?? SizeSpecifier.NONE;
				if(arg.GetNonterminal("sizeSpecifier") is JsonProperty sizeSpec)
					size = ParseSize(sizeSpec.Flatten());
				if(size == SizeSpecifier.NONE)
					throw new LanguageException($"No size specified for '{name}'.");

				return new Operand(size, Resolve((JsonProperty)arg.GetNonterminal("expr"), compiler), OperandType.Immediate);
			}
			else if(arg.Name == "aliasDecl")
			{
				if(!generate)
					throw new InvalidOperationException("Attempted to set alias without enabling code generation.");
				return GenerateAlias(arg, compiler);
			}
			else if(arg.Name == "identifier") // only used to support aliased registers
			{
				if(compiler.ResolveSymbol(arg.Flatten()) is AliasSymbol alias)
					return alias.Register;
				else
					throw new LanguageException($"Argument '{name}': expected '{arg.Flatten()}' to be an aliased register, but it wasn't.");
			}
			else
				throw new LanguageException($"Unexpected non-terminal rule '{arg.Name}' for argument '{name}'.");
		}

		protected Operand ReturnTargetToOperand(JsonProperty returnTarget, ICompiler compiler)
		{
			JsonProperty? mem = returnTarget.GetNonterminal("memArgument");
			JsonProperty? retSize = mem?.GetNonterminal("sizeOrType");
			
			// check if the return target needs to be declared first
			if(mem != null && retSize != null)
			{
				Identifier[] path = MemoryReferenceToPath((JsonProperty)mem, compiler);
				if(path?.Length == 1)
				{
					// the return target is a variable in the current scope of which the size is known -> declare it if it doesn't exist yet (local scopes only)
					ValueType type = new ValueType((JsonProperty)retSize);
					Identifier name = path[0];
					if(compiler.IsLocal && !compiler.IsStackVariable(name))
					{
						type.Solve(compiler);
						compiler.DeclareVariable(type, name);
					}
				}
			}

			// convert to operand
			return ArgumentToOperand(returnTarget, "return target", compiler, generate: true);
		}

		protected void ConvertNode(JsonProperty node, LinkedList<CompilerAction> program, String file = null)
		{
			(int line, int column) = node.GetLineInfo();
			program.AddLast((compiler, pass) => compiler.DeclarePosition(line, column));

			try
			{
				String[] statementRules = new String[] { "statement", "oneliner", "initialiser", "condition", "repeatable" };
				if(statementRules.Contains(node.Name))
					ConvertStatement(node, program);
				else if(node.Name == "function")
					ConvertFunction(node, program);
				else if(node.Name == "namespace")
					ConverNamespace(node, program);
				else if(node.Name == "enum")
					ConvertEnum(node, program);
				else if(node.Name == "struct")
					ConvertStruct(node, program);
				else if(node.Name == "using")
					ConvertUsing(node, program);
				else
					foreach(JsonElement child in node.Value.EnumerateArray())
						if(child.ValueKind == JsonValueKind.Object)
							ConvertNode(child.GetFirstProperty(), program, file);
			}
			catch(LanguageException e)
			{
				e.AddLineInfo(file, line, column);
				ExceptionDispatchInfo.Throw(e);
			}
		}

		protected void ConvertFunction(JsonProperty function, LinkedList<CompilerAction> program)
		{
			// obtain function name and parameter list
			JsonProperty declaration = function.GetNonterminal("functionDeclaration")
									   ?? throw new LanguageException("Malformed function.");
			String name = declaration.GetNonterminal("identifier")?.Flatten()
						  ?? throw new LanguageException("Malformed function declaration.");
			JsonProperty body = function.GetNonterminal("localBody")
								?? throw new LanguageException("Missing function body.");
			(ValueType size, String name)[] parameters = ParseParameterList(declaration.GetNonterminal("parameterList"))?.ToArray();

			// generate function
			GenerateFunction(name, parameters, body, program);
		}

		protected void ConvertABICall(JsonProperty call, LinkedList<CompilerAction> program)
		{
			JsonProperty target = call.GetNonterminal("identifierPath")
								  ?? throw new LanguageException("Encountered malformed ABI call.");
			UnresolvedPath targetPath = new UnresolvedPath(target);
			JsonProperty[] argumentList = call.GetNonterminal("argumentList")?.GetNonterminals("argument").ToArray();
			JsonProperty[] returnTargetList = call.GetNonterminal("abiAssignment")?.GetNonterminal("returnTargetList")?.GetNonterminals("returnTarget").ToArray();

			program.AddLast((compiler, pass) =>
			{
				if(pass == CompilationStep.GenerateCode)
				{
					// obtain function symbol and metadata if available
					Symbol function = compiler.ResolveSymbol(targetPath);
					FunctionMetadata metadata = function is FunctionSymbol ? (function as FunctionSymbol).Metadata : null;
					if(metadata != null)
						if((argumentList != null ? argumentList.Length : 0) != metadata.parameters.Length)
							throw new LanguageException($"Parameter count mismatch: '{String.Join('.', targetPath)}()' expects {metadata.parameters.Length} parameters, got {argumentList.Length}.");

					// convert arguments / return targets to operands
					Operand[] arguments = argumentList != null ? new Operand[argumentList.Length] : new Operand[0];
					Operand[] returnTargets = returnTargetList != null ? new Operand[returnTargetList.Length] : new Operand[0];
					for(int i = 0; i < arguments.Length; i++)
						arguments[i] = ArgumentToOperand(argumentList[i], metadata?.parameters[i].name ?? "unnamed parameter", compiler, metadata?.parameters[i].type, true);
					for(int i = 0; i < returnTargets.Length; i++)
						returnTargets[i] = ReturnTargetToOperand(returnTargetList[i], compiler);

					// generate the call
					Operand functionOp = new Operand(SizeSpecifier.QWORD, function, OperandType.Immediate);
					GenerateABICall(functionOp, metadata, arguments, returnTargets, compiler);
				}
			});
		}

		protected void ConvertMethodCall(JsonProperty call, LinkedList<CompilerAction> program)
		{
			LanguageException malformed = new LanguageException("Malformed non-static method call.");
			JsonProperty reference = call.GetNonterminal("structReference") ?? throw malformed;
			UnresolvedPath structPath = new UnresolvedPath(reference.GetNonterminal("directPointer")?.GetNonterminal("identifierPath"));
			UnresolvedPath typePath = structPath.IsNull ? new UnresolvedPath(reference.GetChildNonterminal()?.GetNonterminal("identifierPath")
																			 ?? throw malformed) : null;
			UnresolvedPath methodPath = new UnresolvedPath(call.GetNonterminal("identifierPath"));
			JsonProperty[] argumentList = call.GetNonterminal("argumentList")?.GetNonterminals("argument").ToArray();
			JsonProperty[] returnTargetList = call.GetNonterminal("abiAssignment")?.GetNonterminal("returnTargetList")?.GetNonterminals("returnTarget").ToArray();

			program.AddLast((compiler, pass) =>
			{
				if(pass == CompilationStep.GenerateCode)
				{
					// resolve struct, its type and the specified method
					TypeSymbol structType;
					LanguageException invalidMethod;
					Operand structOperand;
					if(!structPath.IsNull) // direct pointer to a struct
					{
						Symbol structSymbol = compiler.ResolveSymbol(structPath);
						structType = (structSymbol as TypedSymbol)?.Type.Type ?? throw new LanguageException($"Type unknown for '{structPath}'.");
						invalidMethod = new LanguageException($"'{methodPath}()' is not a valid non-static method of struct '[{structPath}]'.");
						structOperand = new Operand(structType.Size, "[" + structSymbol + "]", OperandType.Memory);
					}
					else // casted pointer to a struct
					{
						structType = (compiler.ResolveSymbol(typePath) as TypeSymbol) ?? throw new LanguageException($"Type unknown: '{typePath}'.");
						invalidMethod = new LanguageException($"'{methodPath}()' is not a valid non-static method of type '[{typePath}]'.");
						JsonProperty cast = reference.GetChildNonterminal() ?? throw malformed;
						if(cast.Name == "regPointerCast")
							structOperand = new Operand(cast.GetNonterminal("gpRegister")?.Flatten() ?? throw malformed);
						else
							structOperand = new Operand(structType.Size, "[" + Resolve(cast.GetNonterminal("segAddress") ?? throw malformed, compiler) + "]", OperandType.Memory);
					}
					Symbol method = structType.MemberScope.GetSymbol(methodPath.Resolve(compiler));

					// verify that this is a valid non-static method
					FunctionMetadata metadata = (method as FunctionSymbol)?.Metadata ?? throw invalidMethod;
					if(metadata.parameters.Length == 0) throw invalidMethod;
					if(metadata.parameters[0].type.Type != structType || metadata.parameters[0].name != "this") throw invalidMethod;
					int argumentCount = argumentList != null ? argumentList.Length : 0;
					if(argumentCount != metadata.parameters.Length - 1)
						throw new LanguageException($"Parameter count mismatch: '{methodPath}()' expects {metadata.parameters.Length - 1} parameters, got {argumentCount}.");

					// convert everything to operands
					Operand[] arguments = argumentList != null ? new Operand[argumentList.Length + 1] : new Operand[1];
					Operand[] returnTargets = returnTargetList != null ? new Operand[returnTargetList.Length] : new Operand[0];
					arguments[0] = structOperand;
					for(int i = 1; i < arguments.Length; i++)
						arguments[i] = ArgumentToOperand(argumentList[i - 1], metadata.parameters[i].name ?? "unnamed parameter", compiler, metadata?.parameters[i].type, true);
					for(int i = 0; i < returnTargets.Length; i++)
						returnTargets[i] = ReturnTargetToOperand(returnTargetList[i], compiler);

					// generate the call
					Operand methodOp = new Operand(SizeSpecifier.QWORD, method, OperandType.Immediate);
					GenerateABICall(methodOp, metadata, arguments, returnTargets, compiler);
				}
			});
		}

		protected IEnumerable<(ValueType size, String name)> ParseParameterList(JsonProperty? parameterList) => parameterList?.GetNonterminals("parameter").Select(par =>
			{
				ValueType size = new ValueType(par.GetNonterminal("sizeOrType")
								 ?? throw new LanguageException($"Malformed parameter: {par.Flatten()}."));
				String name = par.GetNonterminal("identifier")?.Flatten()
							  ?? throw new LanguageException($"Malformed parameter: {par.Flatten()}.");
				return (size, name);
			});

		void ConverNamespace(JsonProperty node, LinkedList<CompilerAction> program)
		{
			Identifier[] path = new UnresolvedPath(node.GetNonterminal("namespaceDeclaration")?.GetNonterminal("identifierPath")
												   ?? throw new LanguageException("Encountered malformed namespace.")
								).ResolveParameterless()
								?? throw new LanguageException("Encountered namespace with template parameters in its name.");
			JsonProperty body = node.GetNonterminal("namespaceBody")
								?? throw new LanguageException("Encountered namespace without body.");

			program.AddLast((compiler, pass) => compiler.EnterGlobal(path));
			ConvertNode(body, program);
			program.AddLast((compiler, pass) => compiler.ExitGlobal());
		}

		void ConvertUsing(JsonProperty node, LinkedList<CompilerAction> program)
		{
			Identifier[] path = new UnresolvedPath(node.GetNonterminal("identifierPath")
												   ?? throw new LanguageException($"Malformed using statement: '{node.Flatten()}'."))
								.ResolveParameterless()
								?? throw new LanguageException($"Attempted to use template parameters in using statement: '{node.Flatten()}'.");

			program.AddLast((compiler, pass) =>
			{
				if(pass != CompilationStep.DeclareGlobalSymbols)
					compiler.UsingScope(path);
			});
		}

		void ConvertEnum(JsonProperty node, LinkedList<CompilerAction> program)
		{
			String enumName = node.GetNonterminal("identifier")?.Flatten()
						  ?? throw new LanguageException("Malformed enum declaration.");
			JsonProperty[] statements = node.GetNonterminal("enumBody")?.GetNonterminals("enumStatement").ToArray()
										?? throw new LanguageException("Enum declaration missing body.");

			program.AddLast((compiler, pass) => compiler.EnterGlobal(enumName));
			foreach(JsonProperty statement in statements)
			{
				JsonProperty? assignment = statement.GetNonterminal("enumAssignment");
				if(assignment == null) continue;

				String name = assignment?.GetNonterminal("identifier")?.Flatten()
							  ?? throw new LanguageException("Malformed enum assignment.");
				JsonProperty value = assignment?.GetNonterminal("expr")
									 ?? throw new LanguageException("Enum assignment missing value.");

				program.AddLast((compiler, pass) =>
				{
					if(pass == CompilationStep.DeclareGlobalSymbols)
						compiler.DeclareConstant(name, Resolve(value, compiler).ToString());
				});
			}
			program.AddLast((compiler, pass) => compiler.ExitGlobal());
		}

		void ConvertStruct(JsonProperty node, LinkedList<CompilerAction> program)
		{
			String structName = node.GetNonterminal("identifier")?.Flatten()
								?? throw new LanguageException("Malformed struct declaration.");
			JsonProperty[] statements = node.GetNonterminal("structBody")?.GetNonterminals("structStatement").ToArray()
										?? throw new LanguageException("Struct declaration missing body.");

			//define a TypeSymbol for the struct
			int structSize = 0; // to be filled in later
			PlaceholderSymbol structSizePlaceholder = null;
			program.AddLast((compiler, pass) =>
			{
				if(pass == CompilationStep.DeclareGlobalSymbols)
				{
					TypeSymbol structType = new TypeSymbol((int)SizeSpecifier.QWORD, true, () => structSize);
					structSizePlaceholder = structType.sizeOfInstancePlaceholder;
					compiler.DeclareType(structName, structType);
					compiler.AddAnonymousPlaceholder(structSizePlaceholder);
				}
			});

			// enter the structs namesapce
			program.AddLast((compiler, pass) => compiler.EnterGlobal(structName));

			// define (placeholders for) all fields and define all constants
			List<(ValueType type, String name)> fields = new List<(ValueType type, String name)>();
			PlaceholderSymbol lastPlaceholder = null;
			foreach(JsonProperty statement in statements)
			{
				JsonProperty? field = statement.GetNonterminal("structField");
				if(field == null) continue;
				field = field?.GetChildNonterminal()
						?? throw new LanguageException($"Encountered struct field declaration with no child non-terminal.");

				String fieldRule = field?.Name;
				if(fieldRule == "constantDecl")
					ConvertConstant((JsonProperty)field, program);
				else
				{
					// determine the name and type of the field
					String name;
					ValueType type;
					if(fieldRule == "structVariableDecl")
					{
						name = field?.GetNonterminal("identifier")?.Flatten()
									  ?? throw new LanguageException($"Malformed struct field declaration: {field?.Flatten()}");
						type = new ValueType(field?.GetNonterminal("sizeOrType")
										 ?? throw new LanguageException($"Malformed struct field declaration: {field?.Flatten()}"));
					}
					else if(fieldRule == "arrayDecl")
					{
						name = field?.GetNonterminal("identifier")?.Flatten()
									  ?? throw new LanguageException($"Malformed struct field declaration: {field?.Flatten()}");
						type = new ValueType(int.Parse(field?.GetNonterminal("expr")?.Flatten() // TODO: write a proper expression-to-int method that works for compile-time constants
								   ?? throw new LanguageException($"Malformed struct field declaration: {field?.Flatten()}")));
					}
					else
						throw new LanguageException($"Unknown struct field rule: {fieldRule} in field {field?.Flatten()}.");

					// declare the field. if it is dependent on a type or if a previous field was, declare it as placeholder (both dependent on the type and the previous placeholder field if applicable).
					PlaceholderSymbol placeholder = null;
					program.AddLast((compiler, pass) =>
					{
						if(pass == CompilationStep.DeclareGlobalSymbols)
						{
							if(lastPlaceholder == null && type.Defined)
							{
								compiler.DeclareConstant(name, structSize.ToString(), type);
								structSize += type.Size;
							}
							else
							{
								placeholder = compiler.DeclarePlaceholder(name, () =>
								{
									type.ResolveDependency();
									Symbol result = new ConstantSymbol(structSize.ToString(), type);
									structSize += type.Size;
									return result;
								});
							}
						}
						else if(placeholder != null && pass == CompilationStep.SolveGlobalSymbolDependencies)
						{
							type.DeclareDependency(placeholder, compiler);
							if(lastPlaceholder != null)
							{
								compiler.DeclareDependency(lastPlaceholder, placeholder);
								lastPlaceholder = placeholder;
							}
						}
					});
				}
			}

			// declare that the struct size depends on the fields (if applicable)
			program.AddLast((compiler, pass) =>
			{
				if(lastPlaceholder != null && pass == CompilationStep.SolveGlobalSymbolDependencies)
					compiler.DeclareDependency(lastPlaceholder, structSizePlaceholder);
			});

			// handle all methods
			foreach(JsonProperty statement in statements)
			{
				JsonProperty? method = statement.GetNonterminal("structMethod");
				if(method == null)
					continue;
				ConvertStructMethod((JsonProperty)method, program);
			}

			// exit the structs namesapce
			program.AddLast((compiler, pass) => compiler.ExitGlobal());
		}

		void ConvertStructMethod(JsonProperty method, LinkedList<CompilerAction> program)
		{
			// obtain method name and body list
			JsonProperty declaration = method.GetNonterminal("structMethodDecl")
									   ?? throw new LanguageException("Malformed struct method.");
			bool staticMethod = declaration.GetNonterminal("staticKeyword") != null;
			String name = declaration.GetNonterminal("identifier")?.Flatten()
						  ?? throw new LanguageException("Malformed function declaration.");
			JsonProperty body = method.GetNonterminal("localBody")
								?? throw new LanguageException("Missing function body.");

			// construct the full parameter list (including the implicit 'this' parameter if the method is non-static)
			IEnumerable<(ValueType type, String name)> explicitParameters = ParseParameterList(declaration.GetNonterminal("parameterList")) ?? new (ValueType type, String name)[0];
			(ValueType type, String name)[] parameters = (staticMethod ? explicitParameters : explicitParameters.Prepend((null, "this"))).ToArray(); // insert temporary 'this' parameter without type, to be replaced later

			// ensure that during compilation the 'this' parameter has the right type
			if(!staticMethod)
				program.AddLast((compiler, pass) =>
				{
					if(pass == CompilationStep.DeclareGlobalSymbols)
						parameters[0].type = new ValueType(compiler.GetCurrentAssociatedType());
				});

			// generate function
			GenerateFunction(name, parameters, body, program);
		}

		void ConvertStatement(JsonProperty node, LinkedList<CompilerAction> program)
		{
			foreach(JsonElement child in node.Value.EnumerateArray())
				if(child.ValueKind == JsonValueKind.Object)
					switch(child.GetFirstProperty().Name)
					{
						case "label":
							ConvertLabel(child.GetFirstProperty(), program);
							break;
						case "instruction":
							ConvertInstruction(child.GetFirstProperty(), program);
							break;
						case "sseInstruction":
							ConvertSSEInstruction(child.GetFirstProperty(), program);
							break;
						case "declaration":
							ConvertDeclaration(child.GetFirstProperty(), program);
							break;
						case "abiReturn":
							ConvertABIReturn(child.GetFirstProperty(), program);
							break;
						case "abiCall":
							ConvertABICall(child.GetFirstProperty(), program);
							break;
						case "methodCall":
							ConvertMethodCall(child.GetFirstProperty(), program);
							break;
						case "controlFlow":
							ConvertControlFlow(child.GetFirstProperty(), program);
							break;
						case "comment": break;
						default:
							throw new LanguageException($"Unknown statement rule: {child.GetFirstProperty().Name}");
					}
		}

		void ConvertLabel(JsonProperty node, LinkedList<CompilerAction> program)
		{
			String label = node.GetNonterminal("identifier")?.Flatten()
						   ?? throw new LanguageException("Encountered malformed label statement.");
			program.AddLast((compiler, pass) =>
			{
				if(pass == CompilationStep.DeclareGlobalSymbols)
					compiler.DeclareConstant(label, GetLabelString(compiler, label));
				else if(pass == CompilationStep.GenerateCode) compiler.Generate(compiler.ResolveSymbol(label) + ":", "program");
			});
		}
	}
}
