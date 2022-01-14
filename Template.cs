using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace OrganisedAssembly
{
	class Template
	{
		protected readonly int parameters; // number of template parameters to expect
		protected readonly Dictionary<Symbol[], SymbolAndOrScope> instances = new Dictionary<Symbol[], SymbolAndOrScope>(new ArrayEqualityComparer<Symbol>());
		protected readonly Func<Symbol[], SymbolAndOrScope> instantiate;

		public Template(int parameters, Func<Symbol[], SymbolAndOrScope> instantiate)
		{
			this.parameters = parameters;
			this.instantiate = instantiate;
		}

		public SymbolAndOrScope this[params Symbol[] parameters]
		{
			get
			{
				if(parameters.Length != this.parameters)
					throw new LanguageException("Attempted to instantiate template with incorrent number of parameters.");

				if(instances.GetValueOrDefault(parameters) is SymbolAndOrScope instance)
					return instance;
				else
					return instances[parameters] = instantiate(parameters);
			}
		}
	}

	class ArrayEqualityComparer<T> : IEqualityComparer<T[]>
	{
		public bool Equals(T[] x, T[] y) => x?.SequenceEqual(y) ?? false;

		public int GetHashCode([DisallowNull] T[] array)
		{
			int hash = 0;
			foreach(T e in array) hash ^= e.GetHashCode();
			return hash;
		}
	}
}
