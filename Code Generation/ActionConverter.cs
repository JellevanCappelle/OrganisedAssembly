using System.Collections.Generic;
using System.Text.Json;
using System;

namespace OrganisedAssembly
{
	interface ActionConverter // TODO: come up with a better name
	{
		IEnumerable<CompilerAction> ConvertTree(JsonProperty parseTree, String file);
	}
}
