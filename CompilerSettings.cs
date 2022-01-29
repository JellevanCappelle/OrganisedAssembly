namespace OrganisedAssembly
{
	class CompilerSettings
	{
		// settings related to code generation
		public const SizeSpecifier StringLengthSize = SizeSpecifier.DWORD;
		public const SizeSpecifier ArrayLengthSize = SizeSpecifier.QWORD;
		public const int MaxArrayAlignment = 64;

		// other settings
		public static bool Verbose = false;
	}
}
