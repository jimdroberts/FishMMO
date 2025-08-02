using System.Runtime.CompilerServices;

namespace FishMMO.Shared
{
	/// <summary>
	/// Extension methods for bitwise flag manipulation on long integers, supporting both int and generic struct bit positions.
	/// </summary>
	public static class LongBitExtensions
	{
		/// <summary>
		/// Checks if the specified bit position is flagged (set to 1) in the long value.
		/// </summary>
		/// <param name="flag">Long value representing flags.</param>
		/// <param name="bitPosition">Bit position to check.</param>
		/// <returns>True if the bit is set, otherwise false.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool IsFlagged(this long flag, int bitPosition)
		{
			return (flag & (1L << bitPosition)) != 0;
		}

		/// <summary>
		/// Checks if the specified generic bit position is flagged (set to 1) in the long value.
		/// </summary>
		/// <typeparam name="T">Struct type representing the bit position.</typeparam>
		/// <param name="flag">Long value representing flags.</param>
		/// <param name="bitPosition">Bit position to check.</param>
		/// <returns>True if the bit is set, otherwise false.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool IsFlagged<T>(this long flag, T bitPosition) where T : struct
		{
			return (flag & (1L << Unsafe.As<T, int>(ref bitPosition))) != 0;
		}

		/// <summary>
		/// Disables (clears) the specified generic bit position in the long value.
		/// </summary>
		/// <typeparam name="T">Struct type representing the bit position.</typeparam>
		/// <param name="flag">Reference to the long value representing flags.</param>
		/// <param name="bitPosition">Bit position to disable.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void DisableBit<T>(this ref long flag, T bitPosition) where T : struct
		{
			flag &= ~(1L << Unsafe.As<T, int>(ref bitPosition));
		}

		/// <summary>
		/// Disables (clears) the specified bit position in the long value.
		/// </summary>
		/// <param name="flag">Reference to the long value representing flags.</param>
		/// <param name="bitPosition">Bit position to disable.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void DisableBit(this ref long flag, int bitPosition)
		{
			flag &= ~(1L << bitPosition);
		}

		/// <summary>
		/// Enables (sets) the specified generic bit position in the long value.
		/// </summary>
		/// <typeparam name="T">Struct type representing the bit position.</typeparam>
		/// <param name="flag">Reference to the long value representing flags.</param>
		/// <param name="bitPosition">Bit position to enable.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void EnableBit<T>(this ref long flag, T bitPosition) where T : struct
		{
			flag |= 1L << Unsafe.As<T, int>(ref bitPosition);
		}

		/// <summary>
		/// Enables (sets) the specified bit position in the long value.
		/// </summary>
		/// <param name="flag">Reference to the long value representing flags.</param>
		/// <param name="bitPosition">Bit position to enable.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void EnableBit(this ref long flag, int bitPosition)
		{
			flag |= 1L << bitPosition;
		}
	}
}