using System;
using System.Collections.Generic;

namespace OrganisedAssembly
{
	interface Scope
	{
		Identifier Name { get; }
		bool IsAnonymous { get; }
		Dictionary<String, object> PersistentData { get; }

		/// <summary>
		/// Enters the next anonymous sub scope, or creates one if at the end of the list.
		/// </summary>
		Scope GetNextAnonymous();

		/// <summary>
		/// Restarts at the beginning of the list of anonymous sub scopes. Only possible at the end of the list.
		/// </summary>
		void ResetAnonymous();

		/// <summary>
		/// Returns a variable/constant/function, or null if the symbol does not exist.
		/// </summary>
		Symbol GetSymbol(params Identifier[] path);

		/// <summary>
		/// Declares a constant/function.
		/// </summary>
		void DeclareSymbol(Identifier name, Symbol symbol);
	}

	abstract class BaseScope
	{
		public Dictionary<String, object> PersistentData { get; private set; } = new Dictionary<String, object>();
		private List<Scope> anonymous = new List<Scope>();
		private int index = 0;
		private bool canCreate = true;

		protected abstract Scope CreateAnonymousScope();

		public Scope GetNextAnonymous()
		{
			if(anonymous.Count <= index)
				if(canCreate)
					anonymous.Add(CreateAnonymousScope());
				else
					throw new InvalidOperationException("Attemted to get a new anonymous scope after calling ResetAnonymous().");
			return anonymous[index++];
		}

		public void ResetAnonymous()
		{
			if(index != anonymous.Count)
				throw new InvalidOperationException("Attempted to call ResetAnonymous() while not at the end of the list.");
			index = 0;
			canCreate = false;
		}
	}
}
