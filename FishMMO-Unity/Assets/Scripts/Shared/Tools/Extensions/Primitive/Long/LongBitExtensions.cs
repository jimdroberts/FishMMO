using System.Runtime.CompilerServices;

namespace FishMMO.Shared
{
	public static class LongBitExtensions
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool IsFlagged(this long flag, int bitPosition)
		{
			return (flag & (1L << bitPosition)) != 0;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool IsFlagged<T>(this long flag, T bitPosition) where T : struct
		{
			return (flag & (1L << Unsafe.As<T, int>(ref bitPosition))) != 0;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void DisableBit<T>(this ref long flag, T bitPosition) where T : struct
		{
			flag &= ~(1L << Unsafe.As<T, int>(ref bitPosition));
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void DisableBit(this ref long flag, int bitPosition)
		{
			flag &= ~(1L << bitPosition);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void EnableBit<T>(this ref long flag, T bitPosition) where T : struct
		{
			flag |= 1L << Unsafe.As<T, int>(ref bitPosition);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void EnableBit(this ref long flag, int bitPosition)
		{
			flag |= 1L << bitPosition;
		}
	}
}