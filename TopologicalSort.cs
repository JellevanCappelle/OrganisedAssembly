using System;
using System.Collections.Generic;

namespace OrganisedAssembly
{
	class TopologicalSort<T>
	{
		protected Dictionary<T, List<T>> inboundEdges = new Dictionary<T, List<T>>();
		protected Dictionary<T, List<T>> outboundEdges = new Dictionary<T, List<T>>();
		protected bool sorted = false;
		public bool Sorted => sorted;

		public void AddNode(T node)
		{
			if(sorted)
				throw new InvalidOperationException("Attempted to add to an already sorted dependency graph.");
			if(node == null)
				throw new ArgumentNullException("null is not allowed as a node value");

			// register as a node without inbound edges
			if(!inboundEdges.ContainsKey(node))
				inboundEdges[node] = new List<T>();
		}

		public void AddEdge(T source, T destination)
		{
			if(sorted)
				throw new InvalidOperationException("Attempted to add to an already sorted dependency graph.");
			if(source == null || destination == null)
				throw new ArgumentNullException("null is not allowed as a node value");

			// add inbound & outbound edges to the graph
			if(!inboundEdges.ContainsKey(destination))
				inboundEdges[destination] = new List<T> { source };
			else
				inboundEdges[destination].Add(source);

			if(!outboundEdges.ContainsKey(source))
				outboundEdges[source] = new List<T> { destination };
			else
				outboundEdges[source].Add(destination);

			// ensure the source is registered as a node without inbound edges, the equivalent is not necessary for destination and outboundEdges
			if(!inboundEdges.ContainsKey(source))
				inboundEdges[source] = new List<T>();
		}

		public IEnumerable<T> Sort()
		{
			// find all source nodes
			Stack<T> backlog = new Stack<T>();
			foreach((T node, List<T> dependencies) in inboundEdges)
				if(dependencies.Count == 0)
					backlog.Push(node);
			
			while(backlog.Count != 0)
			{
				T node = backlog.Pop();
				
				// remove node from the dependency graph and add any new source nodes to the backlog
				if(outboundEdges.ContainsKey(node))
					foreach(T dependent in outboundEdges[node])
					{
						inboundEdges[dependent].Remove(node);
						if(inboundEdges[dependent].Count == 0)
							backlog.Push(dependent);
					}
				inboundEdges.Remove(node);
				
				yield return node;
			}

			// cleanup
			sorted = true;
			if(inboundEdges.Count != 0)
				throw new InvalidOperationException("Encountered a cycle in the dependency graph.");
			inboundEdges = outboundEdges = null;
		}
	}
}
