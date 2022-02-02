using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace OrganisedAssembly.Kernel
{
	class KernelCompiler : ProjectCompiler
	{
		protected readonly ulong virtualBase = 0xffff800000000000;

		protected static readonly String compilerDir = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
		protected readonly String templatePath = compilerDir + "\\Kernel\\template.asm";
		protected readonly String compileScript = compilerDir + "\\Kernel\\compile.bat";

		public KernelCompiler(ProjectSettings project, ActionConverter converter) : base(project, converter)
		{
			if(project.runtime != "none")
				throw new NotImplementedException("Attempted to use a runtime when compiling a kernel.");
		}

		public override void Compile()
		{
			// create directories in case they didn't exist yet
			Directory.CreateDirectory(tempFolder);
			Directory.CreateDirectory(cacheFolder);

			// initialise .text, .data and .bss sections
			Dictionary<String, StreamWriter> sections = new Dictionary<String, StreamWriter>()
			{
				{ "entry", new StreamWriter($"{tempFolder}/entry.asm") },
				{ "program", new StreamWriter($"{tempFolder}/code.asm") },
				{ "data", new StreamWriter($"{tempFolder}/data.asm") },
				{ "uninitialised", new StreamWriter($"{tempFolder}/bss.asm") }
			};

			// copy template
			sections["entry"].Write(new StreamReader(templatePath).ReadToEnd());
			sections["entry"].WriteLine($"org {virtualBase}");

			// add some compiler defined bits
			List<CompilerAction> program = new List<CompilerAction> {
				// add entry point generation
				(compiler, pass) =>
				{
					if(pass == CompilationStep.GenerateCode)
					{
						String entry = compiler.ResolveSymbol("main").Nasm; // find main label/function in root scope
						compiler.Generate($"jmp {entry}", "entry"); // generate entry point
					}
				},
				
				// declare built-in types and constants
				(compiler, pass) =>
				{
					if(pass == CompilationStep.DeclareGlobalSymbols)
					{
						// declare kernel development specific constants
						compiler.EnterGlobal("CompilerConstants");
						compiler.DeclareConstant("KernelVirtualBase", $"0x{virtualBase:x}");
						compiler.DeclareConstant("EndOfKernelImage", "end_of_kernel");
						compiler.ExitGlobal();
					}
				},

				// declare common builtins
				Builtins.BuiltinTypes()
		};

			// add the actual source code
			ParseAndConvertSource(program);

			// compile the program
			Compiler compiler = new Compiler(sections);
			compiler.Compile(program);

			// include all sections in single .asm file
			StreamWriter output = new StreamWriter($"{tempFolder}/output.asm");
			output.WriteLine($"%include \"{tempFolder}/entry.asm\"");
			output.WriteLine($"%include \"{tempFolder}/code.asm\"");
			output.WriteLine($"%include \"{tempFolder}/data.asm\"");
			output.WriteLine($"%include \"{tempFolder}/bss.asm\"");
			output.WriteLine($"end_of_kernel:"); // mark end of kernel image
			output.Flush();
			output.Close();
			Process.Start(compileScript, $"\"{tempFolder}/output.asm\" \"{outputFile}\"").WaitForExit();
		}
	}
}
