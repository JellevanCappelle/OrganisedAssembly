
using System;

namespace OrganisedAssembly
{
	class Stack
	{
		protected int pointer = 0;
		protected int size;
		protected bool sizeSet = false;
		protected bool isLeaf = true;

		public int MaxSize { get; protected set; }

		public int Pointer
		{
			get => pointer;
			set
			{
				if(sizeSet)
					throw new InvalidOperationException("Attempted to manipulate stack pointer after setting the stack size.");
				pointer = value;
				if(-value > MaxSize)
					MaxSize = -value;
			}
		}

		public int Size
		{
			get
			{
				if(!sizeSet)
					throw new InvalidOperationException("Attempted to get stack size before it was set.");
				return size;
			}
			set
			{
				if(sizeSet)
					throw new InvalidOperationException("Attempted to set stack size on the same local scope twice.");
				size = value;
				sizeSet = true;
			}
		}

		public bool IsLeaf
		{
			get => isLeaf;
			set
			{
				if(!isLeaf && value)
					throw new InvalidOperationException("Attempted to declare a function as leaf when it was already declared a non-leaf.");
				isLeaf = value;
			}
		}
	}
}
