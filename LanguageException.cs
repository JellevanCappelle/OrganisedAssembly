using System;

namespace OrganisedAssembly
{
	internal class VariableException : LanguageException { internal VariableException(String message) : base(message) { } }
	internal class LanguageException : Exception
	{
		private bool lineInfoSet = false;
		private String file = null;
		private int line = 0;
		private int column = 0;

		public override string Message => (lineInfoSet ? $"In '{file}' at ({line}, {column}): " : "") + base.Message;

		internal LanguageException(String message) : base(message) { }

		public void AddLineInfo(String file, int line, int column)
		{
			if(this.file == null)
				this.file = file;
			if(!lineInfoSet)
			{
				this.line = line;
				this.column = column;
				lineInfoSet = true;
			}
		}
	}
}
