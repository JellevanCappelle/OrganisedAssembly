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

		public static JsonProperty Parse(String file, String cache = null)
		{
			// parse the input file
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
			
			JsonDocumentOptions options = new JsonDocumentOptions { MaxDepth = int.MaxValue };
			JsonDocument parseTree = JsonDocument.Parse(programJSON, options);
			return parseTree.RootElement.GetFirstProperty();
		}

		public static JsonProperty Load(String file)
		{
			using(FileStream fileStream = new FileStream(file, FileMode.Open))
			{
				JsonDocumentOptions options = new JsonDocumentOptions { MaxDepth = int.MaxValue };
				JsonDocument parseTree = JsonDocument.Parse(fileStream, options);
				return parseTree.RootElement.GetFirstProperty();
			}
		}
	}
}
