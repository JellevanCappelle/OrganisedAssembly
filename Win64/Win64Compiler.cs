using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace OrganisedAssembly.Win64
{
	class Win64Compiler : ProjectCompiler
	{
		protected static readonly String compilerDir = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
		protected readonly String templatePath = compilerDir + "\\Win64\\template.asm";
		protected readonly String compileScript = compilerDir + "\\Win64\\compile.bat";
		protected readonly String stdioLib = compilerDir + "\\Win64\\Win64StdIO.oasm";
		protected readonly String libPath;
		
		public Win64Compiler(ProjectSettings project, ActionConverter converter, String libPath = null) : base(project, converter)
		{
			if(libPath != null)
				this.libPath = libPath;
			else // TODO: find a cleaner way to do this
				using(RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Wow6432Node\Microsoft\Microsoft SDKs\Windows\v10.0"))
					this.libPath = Path.Combine(key.GetValue("InstallationFolder") as String, "Lib", key.GetValue("ProductVersion") as String + ".0", "um\\x64");
		}

		public override void Compile()
		{
			// initialise .text, .data and .bss sections
			Dictionary<String, StreamWriter> sections = new Dictionary<String, StreamWriter>()
			{
				{ "program", new StreamWriter($"{tempFolder}/code.asm") },
				{ "data", new StreamWriter($"{tempFolder}/data.asm") },
				{ "uninitialised", new StreamWriter($"{tempFolder}/bss.asm") }
			};

			// copy template
			sections["program"].Write(new StreamReader(templatePath).ReadToEnd());

			// declare sections
			sections["program"].WriteLine("section .text");
			sections["data"].WriteLine("section .data");
			sections["uninitialised"].WriteLine("section .bss");

			// add compiler defined bits
			List<CompilerAction> program = new List<CompilerAction> {
				// add entry point generation
				(compiler, pass) =>
				{
					if(pass == CompilationStep.GenerateCode)
					{
						String init = compiler.ResolveSymbol("Runtime", "init").Nasm;
						String entry = compiler.ResolveSymbol("main").Nasm; // find main label/function in root scope
						compiler.Generate( // generate entry point
							"main:" + Environment.NewLine +
							"sub rsp, 40" + Environment.NewLine + // shadow space + stack alignmnet 
							$"call {init}" + Environment.NewLine +
							$"call {entry}" + Environment.NewLine +
							"xor ecx, ecx" + Environment.NewLine +
							"jmp ExitProcess"
							, "program");
					}
				},

				// declare builtins
				(compiler, pass) =>
				{
					if(pass == CompilationStep.DeclareGlobalSymbols)
						Builtins.GenerateBuiltinTypes(compiler);
				},

				// declare functions from Kernel32.dll
				(compiler, pass) =>
				{
					if(pass == CompilationStep.DeclareGlobalSymbols)
					{
						compiler.EnterFile("Kernel32.dll");
						compiler.EnterGlobal("Kernel32");
						compiler.DeclareFunction("ExitProcess", "ExitProcess", new FunctionMetadata(new (SizeSpecifier size, String name)[]{
							(SizeSpecifier.DWORD, "exitCode"),
						}));
						compiler.DeclareFunction("GetStdHandle", "GetStdHandle", new FunctionMetadata(new (SizeSpecifier size, String name)[]{
							(SizeSpecifier.DWORD, "stdHandle"),
						}));
						compiler.DeclareFunction("WriteFile", "WriteFile", new FunctionMetadata(new (SizeSpecifier size, String name)[]{
							(SizeSpecifier.DWORD, "file"),
							(SizeSpecifier.QWORD, "buffer"),
							(SizeSpecifier.DWORD, "numberOfBytesToWrite"),
							(SizeSpecifier.QWORD, "numberOfBytesWritten"),
							(SizeSpecifier.QWORD, "overlapped"),
						}));
						compiler.DeclareFunction("ReadFile", "ReadFile", new FunctionMetadata(new (SizeSpecifier size, String name)[]{
							(SizeSpecifier.DWORD, "file"),
							(SizeSpecifier.QWORD, "buffer"),
							(SizeSpecifier.DWORD, "numberOfBytesToRead"),
							(SizeSpecifier.QWORD, "numberOfBytesRead"),
							(SizeSpecifier.QWORD, "overlapped"),
						}));
						compiler.DeclareFunction("WriteConsole", "WriteConsoleA", new FunctionMetadata(new (SizeSpecifier size, string name)[] {
							(SizeSpecifier.DWORD, "console"),
							(SizeSpecifier.QWORD, "buffer"),
							(SizeSpecifier.DWORD, "numberOfBytesToWrite"),
							(SizeSpecifier.QWORD, "numberOfBytesWritten"),
							(SizeSpecifier.QWORD, "reserved"),
						}));
						compiler.DeclareFunction("ReadConsole", "ReadConsoleA", new FunctionMetadata(new (SizeSpecifier size, String name)[]{
							(SizeSpecifier.DWORD, "console"),
							(SizeSpecifier.QWORD, "buffer"),
							(SizeSpecifier.DWORD, "numberOfBytesToRead"),
							(SizeSpecifier.QWORD, "numberOfBytesRead"),
							(SizeSpecifier.QWORD, "inputControl"),
						}));
						compiler.DeclareFunction("SetConsoleOutputCP", "SetConsoleOutputCP", new FunctionMetadata(new (SizeSpecifier size, String name)[]{
							(SizeSpecifier.DWORD, "codepage"),
						}));
						compiler.ExitGlobal();
						compiler.ExitFile();
					}
				}
			};

			// convert to compiler actions
			if(project.runtime == "stdio")
				program.AddRange(converter.ConvertTree(Parser.Parse(stdioLib), stdioLib));
			else if(project.runtime != "none")
				throw new NotImplementedException($"Unsupported runtime: '{project.runtime}'.");
			ParseAndConvertSource(program);

			// compile
			Compiler compiler = new Compiler(program, sections);
			compiler.Compile();

			// include all sections in single .asm file
			StreamWriter output = new StreamWriter($"{tempFolder}/output.asm");
			output.WriteLine($"%include \"{tempFolder}/code.asm\"");
			output.WriteLine($"%include \"{tempFolder}/data.asm\"");
			output.WriteLine($"%include \"{tempFolder}/bss.asm\"");
			output.Flush();
			output.Close();

			// find the folder contatining Kernel32.lib
			Process.Start(compileScript, $"\"{tempFolder}/output.asm\" \"{outputFile}\" \"{libPath}\"").WaitForExit();
		}
	}
}
