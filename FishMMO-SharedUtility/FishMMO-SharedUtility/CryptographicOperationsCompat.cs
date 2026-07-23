using System;
using System.Runtime.CompilerServices;

namespace FishMMO.Shared
{
    /// <summary>
    /// Cross-platform replacement for <see cref="System.Security.Cryptography.CryptographicOperations.ZeroMemory"/>
    /// that works on Unity IL2CPP, Mono, and .NET Core/.NET 8+.
    /// </summary>
    /// <remarks>
    /// <see cref="System.Security.Cryptography.CryptographicOperations.ZeroMemory"/> was added in .NET Core 3.0
    /// and is NOT available in Unity's IL2CPP runtime (which targets .NET Standard 2.0).
    /// This polyfill uses <c>Span&lt;T&gt;.Clear()</c> on IL2CPP and delegates to the BCL method elsewhere.
    /// </remarks>
    public static class CryptographicOperationsCompat
    {
        /// <summary>
        /// Fills the provided buffer with zeros.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ZeroMemory(byte[] buffer)
        {
            if (buffer == null) return;
#if ENABLE_IL2CPP || NETSTANDARD2_0 || NETSTANDARD2_1
            buffer.AsSpan().Clear();
#else
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(buffer);
#endif
        }

        /// <summary>
        /// Fills the provided span with zeros.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ZeroMemory(Span<byte> span)
        {
#if ENABLE_IL2CPP || NETSTANDARD2_0 || NETSTANDARD2_1
            span.Clear();
#else
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(span);
#endif
        }

        /// <summary>
        /// Fills a segment of the provided buffer with zeros.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ZeroMemory(byte[] buffer, int offset, int length)
        {
            if (buffer == null) return;
            ZeroMemory(buffer.AsSpan(offset, length));
        }

        /// <summary>
        /// Determines the equality of two byte sequences in constant time.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool FixedTimeEquals(byte[] left, byte[] right)
        {
            if (left == null || right == null) return false;
            if (left.Length != right.Length) return false;

#if ENABLE_IL2CPP || NETSTANDARD2_0 || NETSTANDARD2_1
            // Constant-time comparison: OR accumulator of XOR differences
            int result = 0;
            for (int i = 0; i < left.Length; i++)
            {
                result |= left[i] ^ right[i];
            }
            return result == 0;
#else
            return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(left, right);
#endif
        }

        /// <summary>
        /// Determines the equality of two read-only byte spans in constant time.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool FixedTimeEquals(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
        {
            if (left.Length != right.Length) return false;

#if ENABLE_IL2CPP || NETSTANDARD2_0 || NETSTANDARD2_1
            int result = 0;
            for (int i = 0; i < left.Length; i++)
            {
                result |= left[i] ^ right[i];
            }
            return result == 0;
#else
            return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(left, right);
#endif
        }
    }
}
