using System;

namespace OrganisedAssembly
{
	static class ToNasmExtensions
	{
		public static String ToNasm(this BaseRegister register) => register.ToString().ToLower();
		public static String ToNasm(this SizeSpecifier size) => size != SizeSpecifier.NONE
			? size.ToString().ToLower()
			: throw new InvalidOperationException("Attempted to convert undefined size specifier to its nasm representation.");
		public static String ToHumanReadable(this SizeSpecifier size) => size != SizeSpecifier.NONE
			? size.ToString().ToLower()
			: "undefined size";
	}
}
