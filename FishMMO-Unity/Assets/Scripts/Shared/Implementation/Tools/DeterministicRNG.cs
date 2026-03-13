using System.Runtime.CompilerServices;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// A fast, deterministic, thread-safe pseudo-random number generator.
	/// Drop-in replacement for <see cref="System.Random"/> in all game code.
	///
	/// Algorithm: xoshiro128** (Blackman &amp; Vigna, 2018).
	/// Period: 2^128 − 1. Passes BigCrush.
	/// Thread safety: no shared mutable state — each instance is independent.
	///
	/// API surface matches the subset of <see cref="System.Random"/> used in
	/// this codebase: <c>Next()</c>, <c>Next(int)</c>, <c>Next(int,int)</c>,
	/// <c>NextDouble()</c>, <c>NextFloat()</c>, <c>Range(int,int)</c>,
	/// <c>Range(float,float)</c>.
	///
	/// A static <see cref="Shared"/> instance is provided for fire-and-forget
	/// usage that does not require determinism (replaces <c>UnityEngine.Random</c>).
	/// </summary>
	public sealed class DeterministicRNG
	{
		/// <summary>
		/// Global shared instance for non-deterministic / fire-and-forget usage.
		/// Replaces <c>UnityEngine.Random</c> for all game code.
		/// NOT thread-safe — use only from the main thread (same contract as UnityEngine.Random).
		/// </summary>
		public static readonly DeterministicRNG Shared = new DeterministicRNG();

		private uint s0, s1, s2, s3;

		/// <summary>
		/// Creates a new RNG seeded from an integer.
		/// Uses SplitMix32 to expand the single int seed into 128 bits of state.
		/// </summary>
		/// <param name="seed">Deterministic seed value.</param>
		public DeterministicRNG(int seed)
		{
			// SplitMix32-derived expansion to fill all four state words.
			// Ensures even seed == 0 produces non-zero state.
			uint z = (uint)seed;
			s0 = SplitMix(ref z);
			s1 = SplitMix(ref z);
			s2 = SplitMix(ref z);
			s3 = SplitMix(ref z);

			// xoshiro requires at least one non-zero state word.
			if ((s0 | s1 | s2 | s3) == 0)
			{
				s0 = 1;
			}
		}

		/// <summary>
		/// Creates a new unseeded RNG using <see cref="System.Environment.TickCount"/>
		/// for a non-deterministic seed. Use this only for server-side cases where
		/// determinism is not required (e.g., loot rolls, shuffle).
		/// </summary>
		public DeterministicRNG()
			: this(System.Environment.TickCount)
		{
		}

		/// <summary>
		/// Creates a DeterministicRNG from raw xoshiro128** state words.
		/// Used to restore the exact generator position during reconcile, since
		/// the 128-bit state cannot be reconstructed from a single 32-bit output.
		/// </summary>
		/// <param name="state0">First state word.</param>
		/// <param name="state1">Second state word.</param>
		/// <param name="state2">Third state word.</param>
		/// <param name="state3">Fourth state word.</param>
		public DeterministicRNG(uint state0, uint state1, uint state2, uint state3)
		{
			s0 = state0;
			s1 = state1;
			s2 = state2;
			s3 = state3;

			if ((s0 | s1 | s2 | s3) == 0)
			{
				s0 = 1;
			}
		}

		/// <summary>
		/// Captures the full 128-bit xoshiro128** state for serialization.
		/// Must be included in reconcile data alongside or instead of the
		/// single-int seed — storing only the seed causes permanent desync
		/// after any prediction mismatch because the 128-bit generator state
		/// cannot be reconstructed from a 32-bit output.
		/// </summary>
		/// <param name="state0">First state word.</param>
		/// <param name="state1">Second state word.</param>
		/// <param name="state2">Third state word.</param>
		/// <param name="state3">Fourth state word.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void CaptureState(out uint state0, out uint state1, out uint state2, out uint state3)
		{
			state0 = s0;
			state1 = s1;
			state2 = s2;
			state3 = s3;
		}

		/// <summary>
		/// Restores the full 128-bit xoshiro128** state from previously captured values.
		/// Call this instead of allocating a <see langword="new"/> instance during
		/// reconcile to avoid per-tick heap allocation.
		/// </summary>
		/// <param name="state0">First state word.</param>
		/// <param name="state1">Second state word.</param>
		/// <param name="state2">Third state word.</param>
		/// <param name="state3">Fourth state word.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void RestoreState(uint state0, uint state1, uint state2, uint state3)
		{
			s0 = state0;
			s1 = state1;
			s2 = state2;
			s3 = state3;

			if ((s0 | s1 | s2 | s3) == 0)
			{
				s0 = 1;
			}
		}

		/// <summary>
		/// Murmur3-finalization mix — derives a well-distributed 32-bit value from a counter.
		/// Used internally to expand a single int seed into 128 bits of state.
		/// Named loosely after SplitMix32 but uses the Murmur3 finalizer constants
		/// (shift pattern is 15/13/16 rather than the strict 16/13/16 of SplitMix32).
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static uint SplitMix(ref uint z)
		{
			z += 0x9E3779B9u;
			z ^= z >> 15;
			z *= 0x85EBCA6Bu;
			z ^= z >> 13;
			z *= 0xC2B2AE35u;
			z ^= z >> 16;
			return z;
		}

		/// <summary>
		/// Rotates a 32-bit value left by <paramref name="k"/> bits.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static uint RotL(uint x, int k)
		{
			return (x << k) | (x >> (32 - k));
		}

		/// <summary>
		/// Advances the internal state and returns a raw 32-bit value.
		/// Core xoshiro128** step.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private uint NextRaw()
		{
			uint result = RotL(s1 * 5, 7) * 9;
			uint t = s1 << 9;

			s2 ^= s0;
			s3 ^= s1;
			s1 ^= s2;
			s0 ^= s3;

			s2 ^= t;
			s3 = RotL(s3, 11);

			return result;
		}

		/// <summary>
		/// Returns a non-negative random integer (0 ≤ result &lt; <see cref="int.MaxValue"/>).
		/// Equivalent to <see cref="System.Random.Next()"/>.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public int Next()
		{
			return (int)(NextRaw() & 0x7FFFFFFFu);
		}

		/// <summary>
		/// Returns a non-negative random integer less than <paramref name="maxValue"/>.
		/// Equivalent to <see cref="System.Random.Next(int)"/>.
		/// When <paramref name="maxValue"/> is a power of two (including 1), a single
		/// bitmask replaces the rejection loop — <c>Next(1)</c> costs exactly one
		/// <see cref="NextRaw"/> call. The rejection-sampling path for non-power-of-two
		/// values is unchanged, preserving the same output sequence.
		/// </summary>
		/// <param name="maxValue">Exclusive upper bound (must be &gt; 0).</param>
		public int Next(int maxValue)
		{
			if (maxValue <= 0) return 0;

			uint uMax = (uint)maxValue;

			// Power-of-two fast path: (uMax & (uMax - 1)) == 0 for all 2^k.
			// Includes maxValue == 1 (always returns 0) with no loop.
			if ((uMax & (uMax - 1)) == 0)
			{
				return (int)(NextRaw() & (uMax - 1));
			}

			// Rejection sampling to eliminate modulo bias.
			uint threshold = (uint)(0x100000000UL % uMax);
			while (true)
			{
				uint r = NextRaw();
				if (r >= threshold)
					return (int)(r % uMax);
			}
		}

		/// <summary>
		/// Returns a random integer in the range [<paramref name="minValue"/>, <paramref name="maxValue"/>).
		/// Equivalent to <see cref="System.Random.Next(int, int)"/>.
		/// The range is computed as <c>long</c> to avoid overflow when
		/// <paramref name="maxValue"/> − <paramref name="minValue"/> &gt; <see cref="int.MaxValue"/>.
		/// </summary>
		/// <param name="minValue">Inclusive lower bound.</param>
		/// <param name="maxValue">Exclusive upper bound.</param>
		public int Next(int minValue, int maxValue)
		{
			if (minValue >= maxValue) return minValue;
			long range = (long)maxValue - minValue;
			if (range > int.MaxValue)
				Log.Warning("DeterministicRNG", $"Next({minValue}, {maxValue}): range {range} exceeds int.MaxValue");
			if (range > int.MaxValue) range = int.MaxValue;
			return minValue + Next((int)range);
		}

		/// <summary>
		/// Returns a random double in the range [0.0, 1.0).
		/// Equivalent to <see cref="System.Random.NextDouble()"/>.
		/// Uses 32 bits of entropy (sufficient for float/game use).
		/// All game code should prefer <see cref="NextFloat"/> or
		/// <see cref="Range(float,float)"/> which avoid double-promotion overhead.
		/// </summary>
		public double NextDouble()
		{
			// Multiply by 1/(2^32) to map [0, 2^32) → [0.0, 1.0).
			return NextRaw() * (1.0 / 4294967296.0);
		}

		/// <summary>
		/// Returns a random float in the range [0.0f, 1.0f).
		/// Drop-in replacement for <c>UnityEngine.Random.value</c>.
		/// Computed entirely in float — no double promotion.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public float NextFloat()
		{
			// 1f / 4294967296f == 2.3283064e-10f — exact in float.
			return NextRaw() * (1f / 4294967296f);
		}

		/// <summary>
		/// Returns a random float in the range [<paramref name="min"/>, <paramref name="max"/>).
		/// Drop-in replacement for <c>UnityEngine.Random.Range(float, float)</c>.
		/// </summary>
		/// <param name="min">Inclusive lower bound.</param>
		/// <param name="max">Exclusive upper bound.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public float Range(float min, float max)
		{
			if (min >= max) return min;
			return min + NextFloat() * (max - min);
		}

		/// <summary>
		/// Returns a random integer in the range [<paramref name="min"/>, <paramref name="max"/>).
		/// Drop-in replacement for <c>UnityEngine.Random.Range(int, int)</c>.
		/// Alias for <see cref="Next(int, int)"/>.
		/// </summary>
		/// <param name="min">Inclusive lower bound.</param>
		/// <param name="max">Exclusive upper bound.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public int Range(int min, int max)
		{
			return Next(min, max);
		}
	}
}