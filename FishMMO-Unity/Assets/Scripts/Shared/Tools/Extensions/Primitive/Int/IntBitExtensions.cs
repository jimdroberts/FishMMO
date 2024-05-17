using System.Runtime.CompilerServices;

namespace FishMMO.Shared
{
	public static class IntBitExtensions
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool IsFlagged(this int flag, int bitPosition)
		{
			return (flag & (1 << bitPosition)) != 0;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool IsFlagged<T>(this int flag, T bitPosition) where T : struct
		{
			return (flag & (1 << Unsafe.As<T, int>(ref bitPosition))) != 0;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void DisableBit<T>(this ref int flag, T bitPosition) where T : struct
		{
			flag &= ~(1 << Unsafe.As<T, int>(ref bitPosition));
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void DisableBit(this ref int flag, int bitPosition)
		{
			flag &= ~(1 << bitPosition);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void EnableBit<T>(this ref int flag, T bitPosition) where T : struct
		{
			flag |= 1 << Unsafe.As<T, int>(ref bitPosition);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void EnableBit(this ref int flag, int bitPosition)
		{
			flag |= 1 << bitPosition;
		}
	}
}