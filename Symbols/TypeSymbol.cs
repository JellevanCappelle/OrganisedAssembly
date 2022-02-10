using System;

namespace OrganisedAssembly
{
	class TypeSymbol : Symbol
	{
		public override String Nasm => throw new LanguageException("Cannot use a type as a constant, variable or function.");

		protected readonly SizeSpecifier sizeSpecifier;
		public override SizeSpecifier Size => sizeSpecifier;

		public bool SizeOfDefined { get; protected set; } = false;
		protected int sizeOf;
		public int SizeOf // size of actual instances of this type
		{
			get => SizeOfDefined ? sizeOf : throw new InvalidOperationException("Attempted to obtain SizeOf of a type for which this is not yet defined.");
			protected set
			{
				sizeOf = value;
				SizeOfDefined = true;
			}
		}

		protected GlobalScope memberScope = null; // scope containing all member symbols of this type
		public GlobalScope MemberScope
		{
			get => memberScope ?? throw new InvalidOperationException("Attempted to access member scope of a type before it is defined.");
			set
			{
				if(memberScope != null)
					throw new InvalidOperationException("Attempted to redefine member scope of a type.");
				memberScope = value;
			}
		}

		protected TypeSymbol()
		{
			sizeSpecifier = SizeSpecifier.NONE;
		}

		public TypeSymbol(SizeSpecifier size)
		{
			if(size == SizeSpecifier.NONE)
				throw new InvalidOperationException("Attempted to construct a type of unspecified size.");
			sizeSpecifier = size;
			SizeOf = (int)size;
		}
	}

	class StructType : TypeSymbol
	{
		public readonly StructLayoutSymbol layoutPlaceholder = null;
		
		public StructType(int sizeOf) : base() => SizeOf = sizeOf;

		// constructor for structs with a yet to be defined size
		public StructType(Func<StructLayoutSymbol, int> sizeOfInstance) : base() => layoutPlaceholder = new StructLayoutSymbol(x => SizeOf = sizeOfInstance(x));
	}

	class ReferenceType : TypeSymbol
	{
		protected readonly TypeSymbol valueType; // represents the underlying type, without any level of referenceness
		protected readonly int referenceness; // 1 for a pointer, 2 for a pointer-to-a-pointer, etc...
		public readonly TypeSymbol dereferenced; // represents the outcome of dereferencing this type once
		public StructType Struct => (StructType)dereferenced;

		public ReferenceType(int sizeOf) : this(new StructType(sizeOf)) { }
		public ReferenceType(Func<StructLayoutSymbol, int> sizeOfInstance) : this(new StructType(sizeOfInstance)) { }

		// referencing constructor
		public ReferenceType(TypeSymbol type) : base(SizeSpecifier.QWORD)
		{
			dereferenced = type;
			if(type is ReferenceType reference)
			{
				valueType = reference.valueType;
				referenceness = reference.referenceness + 1;
			}
			else
			{
				valueType = type;
				referenceness = 1;
			}
		}
	}
}
