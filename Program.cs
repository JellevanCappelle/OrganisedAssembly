using System;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.IO;
using OrganisedAssembly.Kernel;
using OrganisedAssembly.Win64;

namespace OrganisedAssembly
{
	class Program
	{
		static void Main(string[] args)
		{
			if(args.Length == 0)
			{
				Console.WriteLine("Please specify a project file.");
				return;
			}
			String projectFile = args[0];
			String projectDir = Path.GetDirectoryName(Path.GetFullPath(projectFile));
			if(!File.Exists(projectFile))
			{
				Console.WriteLine($"This file doesn't exist: {projectFile}");
				return;
			}

			// read the project settings
			ProjectSettings project;
			try
			{
				project = JsonSerializer.Deserialize<ProjectSettings>(File.ReadAllText(projectFile));
			}
			catch(JsonException)
			{
				Console.WriteLine("Invalid project file.");
				return;
			}
			// fix paths
			project.outputFile = Path.GetFullPath(Path.Combine(projectDir, project.outputFile));
			project.sourceDir = Path.GetFullPath(Path.Combine(projectDir, project.sourceDir));
			project.parserCacheDir = Path.GetFullPath(Path.Combine(projectDir, project.parserCacheDir));
			project.tempDir = Path.GetFullPath(Path.Combine(projectDir, project.tempDir));
			Console.WriteLine(project);

			// compile
			ActionConverter converter =
				project.abi == "kernel" ? new KernelConverter() :
				project.abi == "win64" ? new Win64Converter() :
				throw new NotImplementedException("ABI not implemented: " + project.abi);
			ProjectCompiler compiler =
				project.platform == "kernel" && project.format == "bin" ? new KernelCompiler(project, converter) :
				project.platform == "win64" && project.format == "win64" ? new Win64Compiler(project, converter) :
				throw new NotImplementedException($"Combination of platform '{project.platform}' and format '{project.format}' not implemented.");
			compiler.Compile();
		}

		public static void PrintTree(JsonProperty node, int depth = 0)
		{
			// print current rule
			String depthStr = "";
			for(int i = 1; i < depth; i++) depthStr += "  ";
			if(node.Value.GetArrayLength() == 1 && node.Value[0].ValueKind == JsonValueKind.String)
			{
				String value = node.Value[0].GetString();
				if(Regex.Match(value, "\\s*").Length != value.Length)
					Console.WriteLine(depthStr + node.Name + ": " + node.Value[0].GetString()); // handle lone terminals
				return;
			}
			else
				Console.WriteLine(depthStr + node.Name);

			// print children
			depthStr += "  ";
			foreach(JsonElement child in node.Value.EnumerateArray())
				if(child.ValueKind == JsonValueKind.String)
				{
					String value = child.GetString();
					if(Regex.Match(value, "\\s*").Length == value.Length)
						continue;
					Console.WriteLine(depthStr + "Terminal: " + value);
				}
				else if(child.ValueKind == JsonValueKind.Object)
					PrintTree(child.GetFirstProperty(), depth + 1);
		}
	}
}
