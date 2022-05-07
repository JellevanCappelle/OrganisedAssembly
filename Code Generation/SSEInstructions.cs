using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace OrganisedAssembly
{
	abstract partial class ProgramConverter
	{
		void ConvertSSEInstruction(JsonProperty node, LinkedList<CompilerAction> program)
		{
			String opcode = node.GetNonterminal("sseOpcode")?.Flatten().ToLower()
							?? throw new LanguageException($"Malformed SSE instruction: {node.Flatten()}.");

			program.AddLast((compiler, pass) =>
			{
				if(pass == CompilationStep.GenerateCode)
					compiler.Generate(opcode + node.GetNonterminals("sseOperand").Select(x => Resolve(x, compiler)).Aggregate((x, y) => x + ", " + y), "program");
			});
		}
	}
}
