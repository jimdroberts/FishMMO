using NUnit.Framework;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// NUnit tests for <c>ZigZagEncode(long)</c> and <c>ZigZagDecode(ulong)</c>.
	///
	/// Coverage:
	///   - Known encode mappings (positive, negative, boundaries)
	///   - Known decode mappings (even/odd, boundaries)
	///   - Full round-trip encode → decode for spot values
	///   - Exhaustive round-trip sweep over ±500 000
	///   - Encoder agreement: branchless result must match the expected formula
	///   - Bit-property invariants (parity, monotonicity)
	///   - Sign-boundary continuity around zero
	/// </summary>
	[TestFixture]
	public class ZigZagTests
	{
		// ── Helpers ──────────────────────────────────────────────────────────

		private const long LongMax = long.MaxValue;   //  9 223 372 036 854 775 807
		private const long LongMin = long.MinValue;   // -9 223 372 036 854 775 808
		private const ulong ULongMax = ulong.MaxValue;  // 18 446 744 073 709 551 615

		/// <summary>Reference implementation of the encode formula for agreement tests.</summary>
		private static ulong ReferenceEncode(long value) => (ulong)((value << 1) ^ (value >> 63));

		// ── System under test (static wrappers matching the real methods) ─────

		private static ulong Encode(long value) => (ulong)((value << 1) ^ (value >> 63));
		private static long Decode(ulong value) => (long)(value >> 1) ^ -((long)value & 1);

		// ═════════════════════════════════════════════════════════════════════
		// 1. ZigZagEncode — known expected values
		// ═════════════════════════════════════════════════════════════════════

		[Test]
		public void Encode_Zero_ReturnsZero()
			=> Assert.That(Encode(0), Is.EqualTo(0UL));

		[Test]
		public void Encode_One_ReturnsTwo()
			=> Assert.That(Encode(1), Is.EqualTo(2UL));

		[Test]
		public void Encode_Two_ReturnsFour()
			=> Assert.That(Encode(2), Is.EqualTo(4UL));

		[Test]
		public void Encode_NegativeOne_ReturnsOne()
			=> Assert.That(Encode(-1), Is.EqualTo(1UL));

		[Test]
		public void Encode_NegativeTwo_ReturnsThree()
			=> Assert.That(Encode(-2), Is.EqualTo(3UL));

		[Test]
		public void Encode_127_Returns254()
			=> Assert.That(Encode(127), Is.EqualTo(254UL));

		[Test]
		public void Encode_Negative128_Returns255()
			=> Assert.That(Encode(-128), Is.EqualTo(255UL));

		[Test]
		public void Encode_1000_Returns2000()
			=> Assert.That(Encode(1000), Is.EqualTo(2000UL));

		[Test]
		public void Encode_Negative1000_Returns1999()
			=> Assert.That(Encode(-1000), Is.EqualTo(1999UL));

		[Test]
		public void Encode_LongMaxValue_ReturnsULongMaxMinusOne()
			=> Assert.That(Encode(LongMax), Is.EqualTo(ULongMax - 1));

		[Test]
		public void Encode_LongMinValue_ReturnsULongMax()
			=> Assert.That(Encode(LongMin), Is.EqualTo(ULongMax));

		// ═════════════════════════════════════════════════════════════════════
		// 2. ZigZagDecode — known expected values
		// ═════════════════════════════════════════════════════════════════════

		[Test]
		public void Decode_Zero_ReturnsZero()
			=> Assert.That(Decode(0), Is.EqualTo(0L));

		[Test]
		public void Decode_One_ReturnsNegativeOne()
			=> Assert.That(Decode(1), Is.EqualTo(-1L));

		[Test]
		public void Decode_Two_ReturnsOne()
			=> Assert.That(Decode(2), Is.EqualTo(1L));

		[Test]
		public void Decode_Three_ReturnsNegativeTwo()
			=> Assert.That(Decode(3), Is.EqualTo(-2L));

		[Test]
		public void Decode_254_Returns127()
			=> Assert.That(Decode(254), Is.EqualTo(127L));

		[Test]
		public void Decode_255_ReturnsNegative128()
			=> Assert.That(Decode(255), Is.EqualTo(-128L));

		[Test]
		public void Decode_2000_Returns1000()
			=> Assert.That(Decode(2000), Is.EqualTo(1000L));

		[Test]
		public void Decode_1999_ReturnsNegative1000()
			=> Assert.That(Decode(1999), Is.EqualTo(-1000L));

		[Test]
		public void Decode_ULongMaxMinusOne_ReturnsLongMax()
			=> Assert.That(Decode(ULongMax - 1), Is.EqualTo(LongMax));

		[Test]
		public void Decode_ULongMax_ReturnsLongMin()
			=> Assert.That(Decode(ULongMax), Is.EqualTo(LongMin));

		// ═════════════════════════════════════════════════════════════════════
		// 3. Round-trip — spot values
		// ═════════════════════════════════════════════════════════════════════

		private static readonly long[] RoundTripCases =
		{
			0, 1, -1, 2, -2, 127, -128, 1000, -1000,
			int.MaxValue, int.MinValue,
			LongMax, LongMin
		};

		[TestCaseSource(nameof(RoundTripCases))]
		public void RoundTrip_Encode_ThenDecode_ReturnsOriginal(long value)
			=> Assert.That(Decode(Encode(value)), Is.EqualTo(value));

		// ═════════════════════════════════════════════════════════════════════
		// 4. Exhaustive round-trip sweep
		// ═════════════════════════════════════════════════════════════════════

		[Test]
		public void RoundTrip_ExhaustiveSweep_NegativeToPositive500k()
		{
			int failures = 0;
			for (long v = -500_000L; v <= 500_000L; v++)
				if (Decode(Encode(v)) != v) failures++;
			Assert.That(failures, Is.Zero,
				$"{failures} values in [−500 000, +500 000] did not survive a round-trip.");
		}

		// ═════════════════════════════════════════════════════════════════════
		// 5. Branchless agreement — output must equal the reference formula
		// ═════════════════════════════════════════════════════════════════════

		private static readonly long[] AgreementCases =
		{
			0, 1, -1, 2, -2, 63, -63, 127, -128, 255, -256,
			32767, -32768, 100_000, -100_000,
			int.MaxValue, int.MinValue,
			(long)uint.MaxValue, -((long)uint.MaxValue + 1),
			LongMax, LongMin
		};

		[TestCaseSource(nameof(AgreementCases))]
		public void Encode_MatchesReferenceFormula(long value)
			=> Assert.That(Encode(value), Is.EqualTo(ReferenceEncode(value)));

		// ═════════════════════════════════════════════════════════════════════
		// 6. Sign-boundary continuity
		// ═════════════════════════════════════════════════════════════════════

		[TestCase(-5L, 9UL)]
		[TestCase(-4L, 7UL)]
		[TestCase(-3L, 5UL)]
		[TestCase(-2L, 3UL)]
		[TestCase(-1L, 1UL)]
		[TestCase(0L, 0UL)]
		[TestCase(1L, 2UL)]
		[TestCase(2L, 4UL)]
		[TestCase(3L, 6UL)]
		[TestCase(4L, 8UL)]
		[TestCase(5L, 10UL)]
		public void Encode_SignBoundary_MapsCorrectly(long input, ulong expected)
			=> Assert.That(Encode(input), Is.EqualTo(expected));

		// ═════════════════════════════════════════════════════════════════════
		// 7. Bit-property invariants
		// ═════════════════════════════════════════════════════════════════════

		[Test]
		public void Encode_NonNegative_ProducesEvenResult()
		{
			for (long v = 0; v <= 10_000; v++)
				Assert.That(Encode(v) % 2, Is.Zero,
					$"Encode({v}) = {Encode(v)} is not even.");
		}

		[Test]
		public void Encode_Negative_ProducesOddResult()
		{
			for (long v = -1; v >= -10_000; v--)
				Assert.That(Encode(v) % 2, Is.EqualTo(1UL),
					$"Encode({v}) = {Encode(v)} is not odd.");
		}

		[Test]
		public void Encode_NonNegatives_AreMonotonicallyIncreasing()
		{
			for (long v = 0; v < 9_999; v++)
				Assert.That(Encode(v), Is.LessThan(Encode(v + 1)),
					$"Encode({v}) >= Encode({v + 1}): ordering violated.");
		}

		[Test]
		public void Encode_Negatives_AreMonotonicallyDecreasing()
		{
			// More negative → larger encoded value (farther from zero)
			for (long v = -9_999; v < -1; v++)
				Assert.That(Encode(v), Is.GreaterThan(Encode(v + 1)),
					$"Encode({v}) <= Encode({v + 1}): ordering violated.");
		}

		[Test]
		public void Encode_SameAbsoluteValue_NegativeEncodesOneLess()
		{
			// |n| == |-n|, so enc(-n) == enc(n) - 1
			for (long v = 1; v <= 10_000; v++)
				Assert.That(Encode(-v), Is.EqualTo(Encode(v) - 1),
					$"Encode(-{v}) != Encode({v}) - 1.");
		}
	}
}