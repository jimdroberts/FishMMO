using System.Runtime.CompilerServices;

namespace FishMMO.Shared
{
	/// <summary>
	/// Extension methods for high-performance bitwise flag manipulation on 64-bit integers (long).
	/// Supports both raw bit indices and generic Enum-based bit positions (0-63).
	/// </summary>
	public static class LongBitExtensions
	{
		/// <summary>
		/// Checks if the specified bit position is set (1) in the long value.
		/// </summary>
		/// <param name="flag">The 64-bit bitmask.</param>
		/// <param name="bitPosition">The zero-based index of the bit (0-63).</param>
		/// <returns>True if the bit is set; otherwise, false.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool IsFlagged(this long flag, int bitPosition)
		{
			return (flag & (1L << bitPosition)) != 0;
		}

		/// <summary>
		/// Checks if the specified generic bit position is set.
		/// Useful for large flag sets defined in Enums.
		/// </summary>
		/// <typeparam name="T">A struct type (ideally an int-based Enum).</typeparam>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool IsFlagged<T>(this long flag, T bitPosition) where T : struct
		{
			return (flag & (1L << Unsafe.As<T, int>(ref bitPosition))) != 0;
		}

		/// <summary>
		/// Sets the specified bit to 0 (off) using a bitwise NOT and AND operation.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void DisableBit(this ref long flag, int bitPosition)
		{
			flag &= ~(1L << bitPosition);
		}

		/// <summary>
		/// Sets the specified generic bit position to 0 (off).
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void DisableBit<T>(this ref long flag, T bitPosition) where T : struct
		{
			flag &= ~(1L << Unsafe.As<T, int>(ref bitPosition));
		}

		/// <summary>
		/// Sets the specified bit to 1 (on) using a bitwise OR operation.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void EnableBit(this ref long flag, int bitPosition)
		{
			flag |= (1L << bitPosition);
		}

		/// <summary>
		/// Sets the specified generic bit position to 1 (on).
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void EnableBit<T>(this ref long flag, T bitPosition) where T : struct
		{
			flag |= (1L << Unsafe.As<T, int>(ref bitPosition));
		}

		/// <summary>
		/// Reverses the state of the specified bit (1 becomes 0, 0 becomes 1).
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void ToggleBit(this ref long flag, int bitPosition)
		{
			flag ^= (1L << bitPosition);
		}
	}
}