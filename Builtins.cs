using System;

namespace OrganisedAssembly
{
	static class Builtins
	{
		public static CompilerAction BuiltinTypes()
		{
			// Array template
			Template arrayTemplate = new Template(new TemplateName("Array", "T"), new CompilerAction[] { ArrayAction });

			return (compiler, pass) =>
			{
				if(pass == CompilationStep.DeclareGlobalSymbols)
				{
					// String type
					compiler.DeclareType("String", new ReferenceType((int)CompilerSettings.StringLengthSize)); // declare it as a pointer type for a n-byte struct containing only the string length
					compiler.EnterGlobal("String");
					compiler.DeclareConstant("length", "0", CompilerSettings.StringLengthSize);
					compiler.DeclareConstant("c_str", ((int)CompilerSettings.StringLengthSize).ToString());
					compiler.ExitGlobal();
				}

				arrayTemplate.Action(compiler, pass);
			};
		}

		static void ArrayAction(ICompiler compiler, CompilationStep pass)
		{
			switch(pass)
			{
				case CompilationStep.DeclareGlobalSymbols:
					{
						// declare type
						ReferenceType refType = new ReferenceType(layout =>
						{
							// obtain value size of the stored type
							int valueSize;
							if(layout.dependencies == null)
								valueSize = layout.size;
							else if(layout.dependencies[0].Result is TypeSymbol type)
								valueSize = type.SizeOf;
							else
								throw new LanguageException("Template parameter for Array<T> is not a type.");

							// compute the alignment of the data buffer, must be a power of two, preferably >= instanceSize, must be <= CompilerSettings.MaxArrayAlignment
							int offset = (int)CompilerSettings.ArrayLengthSize;
							if(valueSize > offset)
								while(offset < valueSize && offset < CompilerSettings.MaxArrayAlignment)
									offset <<= 1;

							return offset;
						});
						compiler.DeclareType("Array", refType);
						compiler.AddAnonymousPlaceholder(refType.Struct.layoutPlaceholder);

						// declare fields
						compiler.EnterGlobal("Array");
						compiler.DeclareConstant("count", "0", CompilerSettings.ArrayLengthSize);
						compiler.DeclarePlaceholder("data", _ => new ConstantSymbol(refType.Struct.SizeOf.ToString()));
						compiler.ExitGlobal();
					}
					break;
				case CompilationStep.SolveGlobalSymbolDependencies:
					{
						// resolve parameter and declare dependency if needed
						StructLayoutSymbol layout = ((ReferenceType)compiler.ResolveSymbol("Array")).Struct.layoutPlaceholder;
						Symbol parameter = compiler.ResolveSymbol("T");
						if(parameter is Placeholder dependency)
						{
							layout.dependencies = new Placeholder[] { dependency };
							compiler.DeclareDependency(dependency, layout);
						}
						else if(parameter is TypeSymbol type)
							layout.size = type.SizeOf;

						// declare dependency of the 'data' field of the struct layout
						compiler.EnterGlobal("Array");
						compiler.DeclareDependency(layout, (Placeholder)compiler.ResolveSymbol("data"));
						compiler.ExitGlobal();
					}
					break;
			}
		}
	}
}
