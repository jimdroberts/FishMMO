using System.Runtime.CompilerServices;

namespace FishMMO.Shared
{
	/// <summary>
	/// Extension methods for bitwise flag manipulation on integers, supporting both int and generic struct bit positions.
	/// </summary>
	public static class IntBitExtensions
	{
		/// <summary>
		/// Checks if the specified bit position is flagged (set to 1) in the integer value.
		/// </summary>
		/// <param name="flag">Integer value representing flags.</param>
		/// <param name="bitPosition">Bit position to check.</param>
		/// <returns>True if the bit is set, otherwise false.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool IsFlagged(this int flag, int bitPosition)
		{
			return (flag & (1 << bitPosition)) != 0;
		}

		/// <summary>
		/// Checks if the specified generic bit position is flagged (set to 1) in the integer value.
		/// </summary>
		/// <typeparam name="T">Struct type representing the bit position.</typeparam>
		/// <param name="flag">Integer value representing flags.</param>
		/// <param name="bitPosition">Bit position to check.</param>
		/// <returns>True if the bit is set, otherwise false.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool IsFlagged<T>(this int flag, T bitPosition) where T : struct
		{
			return (flag & (1 << Unsafe.As<T, int>(ref bitPosition))) != 0;
		}

		/// <summary>
		/// Disables (clears) the specified generic bit position in the integer value.
		/// </summary>
		/// <typeparam name="T">Struct type representing the bit position.</typeparam>
		/// <param name="flag">Reference to the integer value representing flags.</param>
		/// <param name="bitPosition">Bit position to disable.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void DisableBit<T>(this ref int flag, T bitPosition) where T : struct
		{
			flag &= ~(1 << Unsafe.As<T, int>(ref bitPosition));
		}

		/// <summary>
		/// Disables (clears) the specified bit position in the integer value.
		/// </summary>
		/// <param name="flag">Reference to the integer value representing flags.</param>
		/// <param name="bitPosition">Bit position to disable.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void DisableBit(this ref int flag, int bitPosition)
		{
			flag &= ~(1 << bitPosition);
		}

		/// <summary>
		/// Enables (sets) the specified generic bit position in the integer value.
		/// </summary>
		/// <typeparam name="T">Struct type representing the bit position.</typeparam>
		/// <param name="flag">Reference to the integer value representing flags.</param>
		/// <param name="bitPosition">Bit position to enable.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void EnableBit<T>(this ref int flag, T bitPosition) where T : struct
		{
			flag |= 1 << Unsafe.As<T, int>(ref bitPosition);
		}

		/// <summary>
		/// Enables (sets) the specified bit position in the integer value.
		/// </summary>
		/// <param name="flag">Reference to the integer value representing flags.</param>
		/// <param name="bitPosition">Bit position to enable.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void EnableBit(this ref int flag, int bitPosition)
		{
			flag |= 1 << bitPosition;
		}
	}
}