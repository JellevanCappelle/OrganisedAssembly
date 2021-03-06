using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace OrganisedAssembly
{
	class ProjectSettings
	{
		// mandatory properties
		public String platform { get; set; }
		public String abi { get; set; }
		public String format { get; set; }
		public String sourceDir { get; set; }
		public String parserCacheDir { get; set; }
		public String tempDir { get; set; }
		public String outputFile { get; set; }

		// optional properties
		public String runtime { get; set; } = "none";

		public override string ToString()
		{
			String result = "";
			foreach(PropertyInfo p in GetType().GetProperties())
			{
				if(result != "") result += '\n';
				result += $"{p.Name}: '{p.GetValue(this)}'";
			}
			return result;
		}
	}

	abstract class ProjectCompiler
	{
		protected readonly ProjectSettings project;
		protected readonly ProgramConverter converter;
		protected readonly String inputFolder;
		protected readonly String tempFolder;
		protected readonly String outputFile;
		protected readonly String cacheFolder;

		public ProjectCompiler(ProjectSettings project, ProgramConverter converter)
		{
			this.project = project;
			this.converter = converter;
			inputFolder = project.sourceDir;
			outputFile = project.outputFile;
			tempFolder = project.tempDir;
			cacheFolder = project.parserCacheDir;
		}

		public abstract void Compile();

		protected void ParseAndConvertSource(List<CompilerAction> program)
		{
			// parse and convert all input files
			foreach(String inputFile in Directory.EnumerateFiles(inputFolder, "*.oasm", SearchOption.AllDirectories))
			{
				String cacheFile = Path.ChangeExtension(Path.Combine(cacheFolder, Path.GetRelativePath(inputFolder, inputFile)), "json");
				JsonProperty parseTree = Parser.Parse(inputFile, cacheFile);

				if(CompilerSettings.Verbose) Console.Write("Converting parse tree... ");
				program.AddRange(converter.ConvertTree(parseTree, inputFile));
				if(CompilerSettings.Verbose) Console.WriteLine("Done!");
			}
		}
	}
}
