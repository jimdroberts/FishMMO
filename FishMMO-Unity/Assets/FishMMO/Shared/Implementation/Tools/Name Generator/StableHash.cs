using System;
using System.Text;

namespace FishMMO.Shared.NameGeneration
{
	/// <summary>
	/// Cross-process-stable hashing used for deterministic seed derivation.
	/// <para>
	/// <c>string.GetHashCode()</c> is randomized per AppDomain in modern .NET
	/// (a HashDoS mitigation). That is fatal for an MMO content pipeline where
	/// two servers MUST generate the same content from the same seed.
	/// These helpers produce the same value across processes, runtimes, and
	/// platforms for the same input bytes.
	/// </para>
	/// <para>Algorithm: FNV-1a over UTF-8 bytes.</para>
	/// </summary>
	public static class StableHash
	{
		private const ulong FNV64_OFFSET = 14695981039346656037UL;
		private const ulong FNV64_PRIME  = 1099511628211UL;
		private const uint  FNV32_OFFSET = 2166136261U;
		private const uint  FNV32_PRIME  = 16777619U;

		public static ulong FNV64(string value)
		{
			if (string.IsNullOrEmpty(value)) return FNV64_OFFSET;
			ulong h = FNV64_OFFSET;
			int len = Encoding.UTF8.GetByteCount(value);
			var buf = len <= 256 ? stackalloc byte[256] : new byte[len];
			int written = Encoding.UTF8.GetBytes(value, buf);
			for (int i = 0; i < written; i++)
			{
				h ^= buf[i];
				h *= FNV64_PRIME;
			}
			return h;
		}

		public static uint FNV32(string value)
		{
			if (string.IsNullOrEmpty(value)) return FNV32_OFFSET;
			uint h = FNV32_OFFSET;
			int len = Encoding.UTF8.GetByteCount(value);
			var buf = len <= 256 ? stackalloc byte[256] : new byte[len];
			int written = Encoding.UTF8.GetBytes(value, buf);
			for (int i = 0; i < written; i++)
			{
				h ^= buf[i];
				h *= FNV32_PRIME;
			}
			return h;
		}

		/// <summary>Mix an accumulator with a follow-up string deterministically.</summary>
		public static ulong Combine(ulong acc, string value)
		{
			if (string.IsNullOrEmpty(value))
			{
				acc ^= 0x9E3779B97F4A7C15UL;
				acc *= FNV64_PRIME;
				return acc;
			}
			int len = Encoding.UTF8.GetByteCount(value);
			var buf = len <= 256 ? stackalloc byte[256] : new byte[len];
			int written = Encoding.UTF8.GetBytes(value, buf);
			// separator so Combine("ab","c") != Combine("a","bc")
			acc ^= 0x2Fu;
			acc *= FNV64_PRIME;
			for (int i = 0; i < written; i++)
			{
				acc ^= buf[i];
				acc *= FNV64_PRIME;
			}
			return acc;
		}

		public static ulong Combine(ulong acc, long value)
		{
			unchecked
			{
				for (int i = 0; i < 8; i++)
				{
					acc ^= (byte)(value >> (i * 8));
					acc *= FNV64_PRIME;
				}
			}
			return acc;
		}

		public static ulong Combine(ulong acc, int value) => Combine(acc, (long)value);

		/// <summary>Build a deterministic 64-bit seed by hashing each string in order.</summary>
		public static ulong Seed(params string[] parts)
		{
			ulong acc = FNV64_OFFSET;
			if (parts == null) return acc;
			for (int i = 0; i < parts.Length; i++)
				acc = Combine(acc, parts[i]);
			return acc;
		}

		/// <summary>
		/// Produce a <see cref="DeterministicRNG"/> from the given parts. The
		/// 64-bit hash is folded to 32 bits with xor so every input bit reaches
		/// the seed; a fold to zero is remapped to one so accidental collisions
		/// on zero do not all share one stream.
		/// </summary>
		public static DeterministicRNG DeriveRng(params string[] parts)
		{
			return new DeterministicRNG(FoldSeed(Seed(parts)));
		}

		/// <summary>Produce a <see cref="DeterministicRNG"/> from a parent seed plus an index, for batch variation.</summary>
		public static DeterministicRNG DeriveRng(ulong parentSeed, int index)
		{
			return new DeterministicRNG(FoldSeed(Combine(parentSeed, index)));
		}

		/// <summary>Folds a 64-bit hash to the 32-bit seed <see cref="DeterministicRNG"/> takes.</summary>
		public static int FoldSeed(ulong hash)
		{
			int seed = unchecked((int)(hash ^ (hash >> 32)));
			return seed == 0 ? 1 : seed;
		}
	}
}
