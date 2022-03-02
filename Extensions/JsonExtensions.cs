using System;
using System.Collections.Generic;
using System.Text.Json;

namespace OrganisedAssembly
{
	static class JsonExtensions
	{
		public static JsonProperty GetFirstProperty(this JsonElement element)
		{
			foreach(JsonProperty property in element.EnumerateObject())
				return property;
			throw new InvalidOperationException("Attempted to get first property of empty JsonElement.");
		}

		public static (int line, int column) GetLineInfo(this JsonProperty node)
		{
			int length = node.Value.GetArrayLength();
			return (node.Value[length - 2].GetInt32(), node.Value[length - 1].GetInt32());
		}

		/// <summary>
		/// Returns all non-terminal productions of a given rule.
		/// </summary>
		public static IEnumerable<JsonProperty> GetNonterminals(this JsonProperty parent, String ruleName)
		{
			foreach(JsonElement child in parent.Value.EnumerateArray())
				if(child.ValueKind == JsonValueKind.Object)
					if(child.GetFirstProperty().Name == ruleName)
						yield return child.GetFirstProperty();
		}

		/// <summary>
		/// Recursively finds all non-terminal productions of a given rule.
		/// </summary>
		public static IEnumerable<JsonProperty> GetNonterminalsRecursive(this JsonProperty parent, String ruleName)
		{
			foreach(JsonElement child in parent.Value.EnumerateArray())
				if(child.ValueKind == JsonValueKind.Object)
					if(child.GetFirstProperty().Name == ruleName)
						yield return child.GetFirstProperty();
					else
						foreach(JsonProperty result in child.GetFirstProperty().GetNonterminalsRecursive(ruleName))
							yield return result;
		}

		/// <summary>
		/// Returns the only non-terminal production of a given rule, or null if there is not exactly one such non-terminal.
		/// </summary>
		public static JsonProperty? GetNonterminal(this JsonProperty parent, String ruleName)
		{
			JsonProperty? result = null;
			foreach(JsonElement child in parent.Value.EnumerateArray())
				if(child.ValueKind == JsonValueKind.Object)
					if(child.GetFirstProperty().Name == ruleName)
						if(result == null)
							result = child.GetFirstProperty();
						else
							return null;
			return result;
		}

		/// <summary>
		/// Returns the first non-terminal child, if there is one
		/// </summary>
		public static JsonProperty? GetChildNonterminal(this JsonProperty parent)
		{
			foreach(JsonElement child in parent.Value.EnumerateArray())
				if(child.ValueKind == JsonValueKind.Object)
					return child.GetFirstProperty();
			return null;
		}

		/// <summary>
		/// Converts a parse tree back to a space separated flat string.
		/// </summary>
		public static String Flatten(this JsonProperty node)
		{
			String[] result = new String[node.Value.GetArrayLength()];
			int i = 0;
			foreach(JsonElement child in node.Value.EnumerateArray())
				if(child.ValueKind == JsonValueKind.Object)
					result[i++] = child.GetFirstProperty().Flatten();
				else if(child.ValueKind == JsonValueKind.String)
					result[i++] = child.GetString();
			return String.Join(' ', result, 0, i);
		}

		/// <summary>
		/// Converts a parse tree back to a space separated flat string, using a function to replace all productions of a certain rule.
		/// </summary>
		public static String FlattenReplace(this JsonProperty node, (String ruleName, Func<JsonProperty, String> replaceFunc)[] replacementRules)
		{
			foreach((String ruleName, Func<JsonProperty, String> replaceFunc) in replacementRules)
				if(node.Name == ruleName)
					return replaceFunc(node);

			String[] result = new String[node.Value.GetArrayLength()];
			int i = 0;
			foreach(JsonElement child in node.Value.EnumerateArray())
				if(child.ValueKind == JsonValueKind.Object)
					result[i++] = child.GetFirstProperty().FlattenReplace(replacementRules);
				else if(child.ValueKind == JsonValueKind.String)
					result[i++] = child.GetString();
			return String.Join(' ', result, 0, i);
		}

		/// <summary>
		/// Converts a parse tree back to a space separated flat string, using a function to replace all productions of a certain rule.
		/// </summary>
		public static SymbolString Resolve(this JsonProperty node, (String ruleName, Func<JsonProperty, Symbol> replaceFunc)[] replacementRules)
		{
			foreach((String ruleName, Func<JsonProperty, Symbol> replaceFunc) in replacementRules)
				if(node.Name == ruleName)
					return replaceFunc(node);

			SymbolString result = new SymbolString();
			foreach(JsonElement child in node.Value.EnumerateArray())
				if(child.ValueKind == JsonValueKind.Object)
					result += child.GetFirstProperty().Resolve(replacementRules);
				else if(child.ValueKind == JsonValueKind.String)
					result += child.GetString();
			return result;
		}
	}
}
