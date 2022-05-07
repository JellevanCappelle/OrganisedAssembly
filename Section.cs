using System;
using System.Collections.Generic;
using System.IO;

namespace OrganisedAssembly
{
	enum CompilerEvent
	{
		None,
		StackSizeSet
	}

	sealed class Section
	{
		private sealed class BackLogItem
		{
			public readonly CompilerEvent dependency;
			private readonly SymbolString line;
			private readonly LinkedList<String> backlog;

			public BackLogItem(SymbolString line, CompilerEvent dependency)
			{
				this.dependency = dependency;
				this.line = line;
				backlog = new LinkedList<String>();
			}

			public void Append(String line) => backlog.AddLast(line);

			public void Merge(BackLogItem next)
			{
				String result = next.line.ToString();
				if(result != null)
					backlog.AddLast(result);
				backlog.Concat(next.backlog);
			}

			public void Write(TextWriter target)
			{
				String result = line.ToString();
				if(result != null) target.WriteLine(result);
				foreach(String s in backlog)
					target.WriteLine(s);
			}
		}

		public readonly bool indent = false;
		private readonly bool owner = false;
		private readonly StreamWriter target;
		private readonly StringWriter tmpTarget = new StringWriter();
		private LinkedList<BackLogItem> backlog = new LinkedList<BackLogItem>();

		public Section(StreamWriter target, bool indent = false)
		{
			this.target = target;
			this.indent = indent;
			owner = true;
		}

		public Section(Section section)
		{
			target = section.target;
			indent = section.indent;
		}

		public void Generate(String line)
		{
			if(line == null) return;
			if(backlog.Count == 0)
				tmpTarget.WriteLine(line);
			else
				backlog.Last.Value.Append(line.ToString());
		}

		public void Generate(SymbolString line, CompilerEvent dependency)
		{
			if(dependency == CompilerEvent.None)
				Generate(line.ToString());
			else
				backlog.AddLast(new BackLogItem(line, dependency));
		}

		public void FireEvent(CompilerEvent compilerEvent)
		{
			if(backlog.Count == 0)
				return; // nothing to do

			LinkedList<BackLogItem> newBacklog = new LinkedList<BackLogItem>();
			BackLogItem dependency = null;
			foreach(BackLogItem item in backlog)
			{
				if(item.dependency == compilerEvent)
					if(dependency == null)
						item.Write(tmpTarget);
					else
						dependency.Merge(item);
				else
					newBacklog.AddLast(dependency = item);
			}
			backlog = newBacklog;
		}

		public void Close()
		{
			target.Write(tmpTarget.ToString());
			tmpTarget.Close();
			target.Flush();
			if(owner)
				target.Close();
		}
	}
}
