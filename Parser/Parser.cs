using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace OrganisedAssembly
{
	static class Parser
	{
		private static readonly String parserPath = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + "\\Parser\\language.py";
		private static readonly DateTime parserDate = File.GetLastWriteTime(parserPath);

		public static JsonProperty Parse(String file, String cache = null)
		{
			JsonDocumentOptions options = new JsonDocumentOptions { MaxDepth = int.MaxValue };
			JsonProperty result;

			// attempt to load cache if it exists and is valid
			if(cache != null && File.Exists(cache) && new FileInfo(cache).Length > 0)
			{
				DateTime cacheDate = File.GetLastWriteTime(cache);
				if(cacheDate >= File.GetLastWriteTime(file) && cacheDate >= parserDate)
				{
					if(CompilerSettings.Verbose) Console.Write($"Loading cached parse tree: {cache}... ");

					using(FileStream fileStream = new FileStream(cache, FileMode.Open))
						result = JsonDocument.Parse(fileStream, options).RootElement.GetFirstProperty(); // return root node

					if(CompilerSettings.Verbose) Console.WriteLine("Done!");
					return result;
				}
			}

			// parse the input file
			if(CompilerSettings.Verbose) Console.Write($"Parsing: {file}... ");
			Process parser = Process.Start(new ProcessStartInfo("py", '"' + parserPath + '"')
			{
				RedirectStandardInput = true,
				RedirectStandardOutput = true,
				StandardOutputEncoding = Encoding.UTF8, // force usage of UTF-8, even when running in a console that doesn't support it
				WorkingDirectory = Path.GetDirectoryName(parserPath),
			});
			parser.StandardInput.WriteLine(Path.GetFullPath(file));
			String programJSON = parser.StandardOutput.ReadLine();

			// save the result if requested
			if(cache != null)
				using(StreamWriter save = new StreamWriter(cache, false))
					save.Write(programJSON);
			
			// return the root node
			result = JsonDocument.Parse(programJSON, options).RootElement.GetFirstProperty();
			if(CompilerSettings.Verbose) Console.WriteLine("Done!");
			return result;

		}
	}
}
