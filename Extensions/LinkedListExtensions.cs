using System.Collections.Generic;

namespace OrganisedAssembly
{
	static class LinkedListExtensions // TODO: just write a custom linked list implementation that supports O(1) concatenation
	{
		public static void Concat<T>(this LinkedList<T> first, LinkedList<T> second)
		{
			foreach(T e in second)
				first.AddLast(e);
		}
	}
}
