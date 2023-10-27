using System;
using System.Runtime.CompilerServices;

namespace FishMMO.Shared
{
	public static class IntBitExtensions
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool IsFlagged(this int flag, int bitPosition)
		{
			return ((flag >> bitPosition) & 1) == 1;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool IsFlagged<T>(this int flag, T bitPosition)
		{
			int pos = Convert.ToInt32(bitPosition);
			return ((flag >> pos) & 1) == 1;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void DisableBit<T>(this ref int flag, T bitPosition)
		{
			int pos = Convert.ToInt32(bitPosition);
			flag &= ~(1 << pos);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void DisableBit(this ref int flag, int bitPosition)
		{
			flag &= ~(1 << bitPosition);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void EnableBit<T>(this ref int flag, T bitPosition)
		{
			flag |= 1 << Convert.ToInt32(bitPosition);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void EnableBit(this ref int flag, int bitPosition)
		{
			flag |= 1 << bitPosition;
		}
	}
}