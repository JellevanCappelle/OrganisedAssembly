using System;
using System.Collections.Generic;
using System.Text.Json;
using System.IO;

namespace OrganisedAssembly
{
	abstract partial class BaseConverter : ActionConverter
	{
		void ConvertDeclaration(JsonProperty node, LinkedList<CompilerAction> program)
		{
			JsonProperty declaration = node.GetChildNonterminal() ?? throw new LanguageException("Encountered empty declaration nonterminal.");

			switch(declaration.Name)
			{
				case "variableDecl":
					ConvertVariable(declaration, program);
					break;
				case "dataStringDecl":
					ConvertDataString(declaration, program);
					break;
				case "textStringDecl":
					ConvertTextString(declaration, program);
					break;
				case "cStringDecl":
					ConvertCString(declaration, program);
					break;
				case "constantDecl":
					ConvertConstant(declaration, program);
					break;
				case "arrayDecl":
					ConvertArray(declaration, program);
					break;
				case "aliasDecl":
					ConvertAlias(declaration, program);
					break;
				case "fileDecl":
					ConvertInclude(declaration, program);
					break;
				default:
					throw new LanguageException($"Encountered malformed declaration: {declaration.Flatten()}.");
			}
		}

		void ConvertVariable(JsonProperty declaration, LinkedList<CompilerAction> program)
		{
			String name = declaration.GetNonterminal("name")?.Flatten()
						  ?? throw new LanguageException($"Encountered malformed declaration {declaration.Flatten()}.");
			ValueType type = new ValueType(declaration.GetNonterminal("sizeOrType")
							 ?? throw new LanguageException($"Encountered malformed declaration: {declaration.Flatten()}."));
			JsonProperty? value = declaration.GetNonterminal("varAssignment")?.GetNonterminal("expr");

			Placeholder placeholder = null;
			program.AddLast((compiler, pass) =>
			{
				if(!compiler.IsLocal && value == null)
					throw new LanguageException("Attempted to declare uninitialised variable in global scope.");

				if(pass == CompilationStep.DeclareGlobalSymbols && !compiler.IsLocal)
					if(type.Defined)
						compiler.DeclareConstant(name, GetLabelString(compiler, name), type);
					else
						placeholder = compiler.DeclarePlaceholder(name, _ =>
						{
							type.ResolveDependency();
							return new ConstantSymbol(GetLabelString(compiler, name), type);
						});
				else if(pass == CompilationStep.SolveGlobalSymbolDependencies && placeholder != null)
					type.DeclareDependency(placeholder, compiler);
				else if(pass == CompilationStep.GenerateCode)
				{
					if(compiler.IsLocal)
					{
						type.Solve(compiler);
						compiler.DeclareVariable(type, name);
						if(value != null)
							compiler.Generate($"mov {Operand.SizeToNasm(type.SizeSpec)} [" + compiler.ResolveSymbol(name) + "]," + Resolve((JsonProperty)value, compiler), "program");
					}
					else
					{
						String mnemonic;
						switch(type.SizeSpec)
						{
							case SizeSpecifier.BYTE:
								mnemonic = " db ";
								break;
							case SizeSpecifier.WORD:
								mnemonic = " dw ";
								break;
							case SizeSpecifier.DWORD:
								mnemonic = " dd ";
								break;
							case SizeSpecifier.QWORD:
								mnemonic = " dq ";
								break;
							default:
								throw new LanguageException($"Encountered malformed declaration: {declaration.Flatten()}.");
						}
						compiler.Generate(compiler.ResolveSymbol(name) + mnemonic + Resolve((JsonProperty)value, compiler), "data");
					}
				}
			});
		}

		void ConvertDataString(JsonProperty declaration, LinkedList<CompilerAction> program)
		{
			String name = declaration.GetNonterminal("name")?.Flatten()
						  ?? throw new LanguageException($"Encountered malformed declaration: {declaration.Flatten()}.");
			String type = declaration.GetNonterminal("dataStringType")?.Flatten()
						  ?? throw new LanguageException($"Encountered malformed declaration: {declaration.Flatten()}.");
			JsonProperty value = declaration.GetNonterminal("exprList")
								 ?? throw new LanguageException($"Encountered malformed declaration: {declaration.Flatten()}.");

			program.AddLast((compiler, pass) =>
			{
				if(compiler.IsLocal)
					throw new LanguageException("Attempted to declare string in local scope.");

				if(pass == CompilationStep.DeclareGlobalSymbols)
					compiler.DeclareConstant(name, GetLabelString(compiler, name));
				else if(pass == CompilationStep.GenerateCode)
					switch(type)
					{
						case "bytes":
							compiler.Generate(compiler.ResolveSymbol(name) + "db" + Resolve(value, compiler), "data");
							break;
						case "words":
							compiler.Generate(compiler.ResolveSymbol(name) + "dw" + Resolve(value, compiler), "data");
							break;
						case "dwords":
							compiler.Generate(compiler.ResolveSymbol(name) + "dd" + Resolve(value, compiler), "data");
							break;
						case "qwords":
							compiler.Generate(compiler.ResolveSymbol(name) + "dq" + Resolve(value, compiler), "data");
							break;
						default:
							throw new LanguageException($"Unknown string type: {type}.");
					}
			});
		}

		void ConvertTextString(JsonProperty declaration, LinkedList<CompilerAction> program)
		{
			String name = declaration.GetNonterminal("name")?.Flatten()
						  ?? throw new LanguageException($"Encountered malformed declaration: {declaration.Flatten()}.");
			JsonProperty value = declaration.GetNonterminal("doubleQuotedString")
								 ?? throw new LanguageException($"Encountered malformed declaration: {declaration.Flatten()}.");

			program.AddLast((compiler, pass) =>
			{
				if(compiler.IsLocal)
					throw new LanguageException("Attempted to declare string in local scope.");

				if(pass == CompilationStep.DeclareGlobalSymbols)
					compiler.DeclareConstant(name, GetLabelString(compiler, name));
				else if(pass == CompilationStep.GenerateCode)
					GenerateString(compiler.ResolveSymbol(name).Nasm, value.Flatten(), compiler);
			});
		}

		void ConvertCString(JsonProperty declaration, LinkedList<CompilerAction> program)
		{
			String name = declaration.GetNonterminal("name")?.Flatten()
						  ?? throw new LanguageException($"Encountered malformed declaration: {declaration.Flatten()}.");
			JsonProperty value = declaration.GetNonterminal("singleQuotedString")
								 ?? throw new LanguageException($"Encountered malformed declaration: {declaration.Flatten()}.");

			program.AddLast((compiler, pass) =>
			{
				if(compiler.IsLocal)
					throw new LanguageException("Attempted to declare string in local scope.");

				if(pass == CompilationStep.DeclareGlobalSymbols)
					compiler.DeclareConstant(name, GetLabelString(compiler, name));
				else if(pass == CompilationStep.GenerateCode)
					GenerateCString(compiler.ResolveSymbol(name).Nasm, value.Flatten(), compiler);
			});
		}

		void ConvertConstant(JsonProperty declaration, LinkedList<CompilerAction> program)
		{
			String name = declaration.GetNonterminal("name")?.Flatten()
						  ?? throw new LanguageException($"Encountered malformed declaration {declaration.Flatten()}.");
			JsonProperty value = declaration.GetNonterminal("expr")
								 ?? throw new LanguageException($"Encountered malformed declaration: {declaration.Flatten()}.");

			program.AddLast((compiler, pass) =>
			{
				if(compiler.IsLocal)
				{
					if(pass == CompilationStep.GenerateCode)
						compiler.DeclareConstant(name, Resolve(value, compiler).ToString());
				}
				else if(pass == CompilationStep.DeclareGlobalSymbols)
					compiler.DeclareConstant(name, Resolve(value, compiler).ToString()); // TODO: it should be possible for a constant to depend on a constant that will be declared later on
			});
		}

		void ConvertArray(JsonProperty declaration, LinkedList<CompilerAction> program)
		{
			String name = declaration.GetNonterminal("name")?.Flatten()
						  ?? throw new LanguageException($"Encountered malformed declaration {declaration.Flatten()}.");
			JsonProperty size = declaration.GetNonterminal("expr")
								?? throw new LanguageException($"Encountered malformed declaration {declaration.Flatten()}.");

			program.AddLast((compiler, pass) =>
			{
				if(compiler.IsLocal)
				{
					if(pass == CompilationStep.GenerateCode)
					{
						int arraySize = int.Parse(size.Flatten()); // TODO: write a proper expression-to-int method that works for compile-time constants
						compiler.DeclareVariable(new ValueType(arraySize), name);
					}
				}
				else
					if(pass == CompilationStep.DeclareGlobalSymbols)
						compiler.DeclareConstant(name, GetLabelString(compiler, name));
					else if(pass == CompilationStep.GenerateCode)
						compiler.Generate(compiler.ResolveSymbol(name) + ": resb" + Resolve(size, compiler), "uninitialised");
			});
		}

		void ConvertAlias(JsonProperty declaration, LinkedList<CompilerAction> program)
		{
			program.AddLast((compiler, pass) =>
			{
				if(pass == CompilationStep.GenerateCode)
					GenerateAlias(declaration, compiler);
			});
		}

		Operand GenerateAlias(JsonProperty declaration, ICompiler compiler)
		{
			String name = declaration.GetNonterminal("name")?.Flatten()
						  ?? throw new LanguageException($"Encountered malformed declaration {declaration.Flatten()}.");
			String register = declaration.GetNonterminal("gpRegister")?.Flatten()
							  ?? throw new LanguageException($"Encountered malformed declaration {declaration.Flatten()}.");

			return compiler.SetRegisterAlias(name, register);
		}

		void ConvertInclude(JsonProperty declaration, LinkedList<CompilerAction> program)
		{
			String name = declaration.GetNonterminal("name")?.Flatten()
						  ?? throw new LanguageException($"Encountered malformed declaration {declaration.Flatten()}.");
			String filePath = declaration.GetNonterminal("doubleQuotedString")?.Flatten()
							  ?? throw new LanguageException($"Encountered malformed declaration {declaration.Flatten()}.");

			program.AddLast((compiler, pass) =>
			{
				if(compiler.IsLocal)
					throw new LanguageException("Attempted to include binary file in a local scope.");

				if(pass == CompilationStep.DeclareGlobalSymbols)
					compiler.DeclareConstant(name, GetLabelString(compiler, name));
				else if(pass == CompilationStep.GenerateCode)
					compiler.Generate(compiler.ResolveSymbol(name) + ": incbin" + ('"' + Path.GetFullPath(Path.Combine(Path.GetDirectoryName(compiler.CurrentFile), filePath.Substring(1, filePath.Length - 2))) + '"'), "data");
			});
		}
	}
}
