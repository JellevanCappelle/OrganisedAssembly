namespace OrganisedAssembly
{
	static class Builtins
	{
		public static void GenerateBuiltinTypes(ICompiler compiler)
		{
			// String type
			compiler.DeclareType("String", new TypeSymbol((int)SizeSpecifier.QWORD, (int)CompilerSettings.StringLengthSize, true)); // declare it as a pointer type for a 4-byte struct containing only the string length
			compiler.EnterGlobal("String");
			compiler.DeclareConstant("length", "0", new ValueType(CompilerSettings.StringLengthSize));
			compiler.DeclareConstant("c_str", ((int)CompilerSettings.StringLengthSize).ToString());
			compiler.ExitGlobal();

			// Array template
			//compiler.DeclareTemplate("Array", new Template(new TemplateName("Array", "T"), new CompilerAction[] { ArrayAction }));
		}

		static void ArrayAction(ICompiler compiler, CompilationStep pass)
		{
			int instanceSize = (compiler.ResolveSymbol("T") as TypeSymbol).SizeOfInstance; // TODO: declare a placeholder dependent on the parameter

			// compute the alignment of the data buffer, must be a power of two, preferably >= instanceSize, must be <= CompilerSettings.MaxArrayAlignment
			int offset = (int)CompilerSettings.ArrayLengthSize;
			if(instanceSize > offset)
				while(offset < instanceSize && offset < CompilerSettings.MaxArrayAlignment)
					offset <<= 1;

			// declare fields
			compiler.DeclareConstant("count", "0", new ValueType(CompilerSettings.ArrayLengthSize));
			compiler.DeclareConstant("data", offset.ToString());
		}
	}
}
