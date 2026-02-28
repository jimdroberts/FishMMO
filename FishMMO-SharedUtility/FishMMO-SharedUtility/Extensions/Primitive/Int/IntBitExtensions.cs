using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace FishMMO.Shared
{
	/// <summary>
	/// Extension methods for high-performance bitwise flag manipulation on integers.
	/// Supports both raw bit indices and generic Enum-based bit positions.
	/// </summary>
	public static class IntBitExtensions
	{
		/// <summary>
		/// Checks if the specified bit position is set (1) in the integer value.
		/// </summary>
		/// <param name="flag">The bitmask integer.</param>
		/// <param name="bitPosition">The zero-based index of the bit (0-31).</param>
		/// <returns>True if the bit is set; otherwise, false.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool IsFlagged(this int flag, int bitPosition)
		{
			return (flag & (1 << bitPosition)) != 0;
		}

		/// <summary>
		/// Checks if the specified generic bit position is set. 
		/// Use this for Enums representing bit indices.
		/// </summary>
		/// <typeparam name="T">A struct type (ideally an Enum).</typeparam>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool IsFlagged<T>(this int flag, T bitPosition) where T : struct
		{
			// Safety check: ensure we handle different enum underlying types correctly
			int pos = EqualityComparer<T>.Default.GetHashCode(bitPosition);
			// Note: In performance-critical netcode, we assume T is an int-sized enum.
			return (flag & (1 << Unsafe.As<T, int>(ref bitPosition))) != 0;
		}

		/// <summary>
		/// Sets the specified bit to 0 (off) using a bitwise NOT and AND operation.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void DisableBit(this ref int flag, int bitPosition)
		{
			flag &= ~(1 << bitPosition);
		}

		/// <summary>
		/// Sets the specified generic bit position to 0 (off).
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void DisableBit<T>(this ref int flag, T bitPosition) where T : struct
		{
			flag &= ~(1 << Unsafe.As<T, int>(ref bitPosition));
		}

		/// <summary>
		/// Sets the specified bit to 1 (on) using a bitwise OR operation.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void EnableBit(this ref int flag, int bitPosition)
		{
			flag |= (1 << bitPosition);
		}

		/// <summary>
		/// Sets the specified generic bit position to 1 (on).
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void EnableBit<T>(this ref int flag, T bitPosition) where T : struct
		{
			flag |= (1 << Unsafe.As<T, int>(ref bitPosition));
		}

		/// <summary>
		/// Reverses the state of the specified bit (1 becomes 0, 0 becomes 1).
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void ToggleBit(this ref int flag, int bitPosition)
		{
			flag ^= (1 << bitPosition);
		}
	}
}