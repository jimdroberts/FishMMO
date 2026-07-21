using System;
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
			if (bitPosition < 0 || bitPosition > 63)
				throw new ArgumentOutOfRangeException(nameof(bitPosition), "Bit position must be between 0 and 63.");
			return (flag & (1L << bitPosition)) != 0;
		}

		/// <summary>
		/// Checks if the specified generic bit position is set.
		/// Useful for large flag sets defined in Enums.
		/// </summary>
		/// <typeparam name="T">An enum type backed by long.</typeparam>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool IsFlagged<T>(this long flag, T bitPosition) where T : unmanaged, Enum
		{
			if (System.Runtime.CompilerServices.Unsafe.SizeOf<T>() != sizeof(long))
				throw new ArgumentException($"Enum type {typeof(T).Name} is not long-backed. Only long-backed enums are supported.");
			long pos = Unsafe.As<T, long>(ref bitPosition);
			if (pos < 0 || pos > 63)
				throw new ArgumentOutOfRangeException(nameof(bitPosition), "Bit position must be between 0 and 63.");
			return (flag & (1L << (int)pos)) != 0;
		}

		/// <summary>
		/// Sets the specified bit to 0 (off) using a bitwise NOT and AND operation.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void DisableBit(this ref long flag, int bitPosition)
		{
			if (bitPosition < 0 || bitPosition > 63)
				throw new ArgumentOutOfRangeException(nameof(bitPosition), "Bit position must be between 0 and 63.");
			flag &= ~(1L << bitPosition);
		}

		/// <summary>
		/// Sets the specified generic bit position to 0 (off).
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void DisableBit<T>(this ref long flag, T bitPosition) where T : unmanaged, Enum
		{
			if (System.Runtime.CompilerServices.Unsafe.SizeOf<T>() != sizeof(long))
				throw new ArgumentException($"Enum type {typeof(T).Name} is not long-backed. Only long-backed enums are supported.");
			long pos = Unsafe.As<T, long>(ref bitPosition);
			if (pos < 0 || pos > 63)
				throw new ArgumentOutOfRangeException(nameof(bitPosition), "Bit position must be between 0 and 63.");
			flag &= ~(1L << (int)pos);
		}

		/// <summary>
		/// Sets the specified bit to 1 (on) using a bitwise OR operation.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void EnableBit(this ref long flag, int bitPosition)
		{
			if (bitPosition < 0 || bitPosition > 63)
				throw new ArgumentOutOfRangeException(nameof(bitPosition), "Bit position must be between 0 and 63.");
			flag |= (1L << bitPosition);
		}

		/// <summary>
		/// Sets the specified generic bit position to 1 (on).
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void EnableBit<T>(this ref long flag, T bitPosition) where T : unmanaged, Enum
		{
			if (System.Runtime.CompilerServices.Unsafe.SizeOf<T>() != sizeof(long))
				throw new ArgumentException($"Enum type {typeof(T).Name} is not long-backed. Only long-backed enums are supported.");
			long pos = Unsafe.As<T, long>(ref bitPosition);
			if (pos < 0 || pos > 63)
				throw new ArgumentOutOfRangeException(nameof(bitPosition), "Bit position must be between 0 and 63.");
			flag |= (1L << (int)pos);
		}

		/// <summary>
		/// Reverses the state of the specified bit (1 becomes 0, 0 becomes 1).
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void ToggleBit(this ref long flag, int bitPosition)
		{
			if (bitPosition < 0 || bitPosition > 63)
				throw new ArgumentOutOfRangeException(nameof(bitPosition), "Bit position must be between 0 and 63.");
			flag ^= (1L << bitPosition);
		}

		/// <summary>
		/// Reverses the state of the specified generic bit position (1 becomes 0, 0 becomes 1).
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void ToggleBit<T>(this ref long flag, T bitPosition) where T : unmanaged, Enum
		{
			if (System.Runtime.CompilerServices.Unsafe.SizeOf<T>() != sizeof(long))
				throw new ArgumentException($"Enum type {typeof(T).Name} is not long-backed. Only long-backed enums are supported.");
			long pos = Unsafe.As<T, long>(ref bitPosition);
			if (pos < 0 || pos > 63)
				throw new ArgumentOutOfRangeException(nameof(bitPosition), "Bit position must be between 0 and 63.");
			flag ^= (1L << (int)pos);
		}
	}
}
