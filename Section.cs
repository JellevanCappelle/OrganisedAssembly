﻿using System;
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

			public void Merge(BackLogItem next) // TODO: this should be O(1)!!!
			{
				String result = next.line.ToString();
				if(result != null) backlog.AddLast(result);
				foreach(String s in next.backlog)
					backlog.AddLast(s);
			}

			public void Write(StreamWriter target)
			{
				String result = line.ToString();
				if(result != null) target.WriteLine(result);
				foreach(String s in backlog)
					target.WriteLine(s);
			}
		}

		private StreamWriter target;
		private LinkedList<BackLogItem> backlog = new LinkedList<BackLogItem>();

		public Section(StreamWriter target) => this.target = target;

		public void Generate(String line)
		{
			if(line == null) return;
			if(backlog.Count == 0)
				target.WriteLine(line);
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
						item.Write(target);
					else
						dependency.Merge(item);
				else
					newBacklog.AddLast(dependency = item);
			}
			backlog = newBacklog;
		}

		public void Close()
		{
			target.Flush();
			target.Close();
		}
	}
}
