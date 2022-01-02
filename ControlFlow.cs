using System;
using System.Collections.Generic;
using System.Text.Json;

namespace OrganisedAssembly
{
	abstract partial class BaseConverter : ActionConverter
	{
		protected String InvertCondition(String condition) => condition.StartsWith('n') ? condition.Substring(1) : 'n' + condition;

		void ConvertControlFlow(JsonProperty node, LinkedList<CompilerAction> program)
		{
			JsonProperty child = node.GetChildNonterminal() ??
								 throw new LanguageException("Encountered malformed control flow statement.");
			switch(child.Name)
			{
				case "ifStatement":
					ConvertIf(child, program);
					break;
				case "whileLoop":
					ConvertWhile(child, program);
					break;
				case "doWhileLoop":
					ConvertDoWhile(child, program);
					break;
				case "forLoop":
					ConvertFor(child, program);
					break;
				case "doForLoop":
					ConvertDoFor(child, program);
					break;
				default:
					throw new NotImplementedException();
			}
		}

		void ConvertIf(JsonProperty node, LinkedList<CompilerAction> program)
		{
			String conditionType = node.GetNonterminal("ifKeyword")?.Flatten().Substring(2)
						  ?? throw new LanguageException("Encountered malformed if statement.");
			JsonProperty? condition = node.GetNonterminal("condition");
			JsonProperty body = node.GetNonterminal("ifBody")
								?? throw new LanguageException("Encountered malformed if statement.");
			JsonProperty? elseBody = node.GetNonterminal("elseStatement")?.GetNonterminal("ifBody");

			// condition
			if(condition != null)
				ConvertNode((JsonProperty)condition, program);
			program.AddLast((compiler, pass) =>
			{
				if(pass == CompilationStep.GenerateCode)
				{
					compiler.PersistentData["end_of_if"] = GetLabelString(compiler, "end_of_if");
					compiler.Generate($"j{InvertCondition(conditionType)} {compiler.PersistentData["end_of_if"]}", "program");
				}
				compiler.EnterAnonymous();
			});

			// body
			ConvertNode(body, program);

			// generate end-of-if label and skip else if it exists
			program.AddLast((compiler, pass) =>
			{
				compiler.ExitAnonymous();
				if(pass == CompilationStep.GenerateCode)
				{
					if(elseBody != null)
					{
						compiler.PersistentData["end_of_else"] = GetLabelString(compiler, "end_of_else");
						compiler.Generate($"jmp {compiler.PersistentData["end_of_else"]}", "program");
					}
					compiler.Generate(compiler.PersistentData["end_of_if"] + ":", "program");
				}
			});

			if(elseBody != null)
			{
				// else body
				program.AddLast((compiler, pass) => compiler.EnterAnonymous());
				ConvertNode((JsonProperty)elseBody, program);

				// enf-of-else label
				program.AddLast((compiler, pass) =>
				{
					compiler.ExitAnonymous();
					if(pass == CompilationStep.GenerateCode)
						compiler.Generate(compiler.PersistentData["end_of_else"] + ":", "program");
				});
			}
		}

		void ConvertWhile(JsonProperty node, LinkedList<CompilerAction> program)
		{
			String conditionType = node.GetNonterminal("whileKeyword")?.Flatten().Substring(5)
						  ?? throw new LanguageException("Encountered malformed while loop.");
			JsonProperty? condition = node.GetNonterminal("condition");
			JsonProperty body = node.GetNonterminal("loopBody")
								?? throw new LanguageException("Encountered malformed while loop.");

			ConvertLoop(null, condition, conditionType, null, body, program);
		}

		void ConvertFor(JsonProperty node, LinkedList<CompilerAction> program)
		{
			String conditionType = node.GetNonterminal("forKeyword")?.Flatten().Substring(3)
						  ?? throw new LanguageException("Encountered malformed for loop.");
			JsonProperty? init = node.GetNonterminal("initialiser");
			JsonProperty? condition = node.GetNonterminal("condition");
			JsonProperty? advance = node.GetNonterminal("repeatable");
			JsonProperty body = node.GetNonterminal("loopBody")
								?? throw new LanguageException("Encountered malformed for loop.");

			ConvertLoop(init, condition, conditionType, advance, body, program);
		}

		void ConvertDoWhile(JsonProperty node, LinkedList<CompilerAction> program)
		{
			String conditionType = node.GetNonterminal("whileKeyword")?.Flatten().Substring(5)
						  ?? throw new LanguageException("Encountered malformed while loop.");
			JsonProperty? condition = node.GetNonterminal("condition");
			JsonProperty body = node.GetNonterminal("loopBody")
								?? throw new LanguageException("Encountered malformed while loop.");

			ConvertDoLoop(null, body, condition, conditionType, null, program);
		}

		void ConvertDoFor(JsonProperty node, LinkedList<CompilerAction> program)
		{
			String conditionType = node.GetNonterminal("forKeyword")?.Flatten().Substring(3)
						  ?? throw new LanguageException("Encountered malformed for loop.");
			JsonProperty? init = node.GetNonterminal("initialiser");
			JsonProperty? condition = node.GetNonterminal("condition");
			JsonProperty? advance = node.GetNonterminal("repeatable");
			JsonProperty body = node.GetNonterminal("loopBody")
								?? throw new LanguageException("Encountered malformed for loop.");

			ConvertDoLoop(init, body, condition, conditionType, advance, program);
		}

		void ConvertLoop(JsonProperty? init, JsonProperty? condition, String conditionType, JsonProperty? advance, JsonProperty body, LinkedList<CompilerAction> program)
		{
			program.AddLast((compiler, pass) => compiler.EnterAnonymous());
			if(init != null)
				ConvertNode((JsonProperty)init, program);
			if(condition != null)
				ConvertNode((JsonProperty)condition, program);

			// skip loop if condition is not met
			program.AddLast((compiler, pass) =>
			{
				if(pass == CompilationStep.GenerateCode)
				{
					String endLabel =  GetLabelString(compiler, "end_of_loop");
					compiler.PersistentData["end_of_loop"] = endLabel;
					compiler.Generate($"j{InvertCondition(conditionType)} {endLabel}", "program");
				}
			});

			// do the loop
			ConvertDoLoop(null, body, condition, conditionType, advance, program);

			// add label
			program.AddLast((compiler, pass) =>
			{
				if(pass == CompilationStep.GenerateCode)
					compiler.Generate(compiler.PersistentData["end_of_loop"] + ":", "program");
				compiler.ExitAnonymous();
			});
		}

		void ConvertDoLoop(JsonProperty? init, JsonProperty body, JsonProperty? condition, String conditionType, JsonProperty? advance, LinkedList<CompilerAction> program)
		{
			program.AddLast((compile, pass) => compile.EnterAnonymous());
			if(init != null)
				ConvertNode((JsonProperty)init, program);

			// mark start of loop
			program.AddLast((compiler, pass) =>
			{
				if(pass == CompilationStep.GenerateCode)
				{
					String startLabel = GetLabelString(compiler, "start_of_loop");
					compiler.PersistentData["start_of_loop"] = startLabel;
					compiler.Generate(startLabel + ":", "program");
				}
			});

			// loop body + condition
			ConvertNode(body, program);
			if(advance != null)
				ConvertNode((JsonProperty)advance, program);
			if(condition != null)
				ConvertNode((JsonProperty)condition, program);

			// repeat if condition is met
			program.AddLast((compiler, pass) =>
			{
				if(pass == CompilationStep.GenerateCode)
					compiler.Generate($"j{conditionType} {compiler.PersistentData["start_of_loop"]}", "program");
				compiler.ExitAnonymous();
			});
		}
	}
}
