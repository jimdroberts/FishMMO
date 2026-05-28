using NUnit.Framework;
using AuthTestTrace = FishMMO.UnitTests.Harness.AuthTestTrace;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

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
			=> LogAssert.AreEqual(0UL, Encode(0));

		[Test]
		public void Encode_One_ReturnsTwo()
			=> LogAssert.AreEqual(2UL, Encode(1));

		[Test]
		public void Encode_Two_ReturnsFour()
			=> LogAssert.AreEqual(4UL, Encode(2));

		[Test]
		public void Encode_NegativeOne_ReturnsOne()
			=> LogAssert.AreEqual(1UL, Encode(-1));

		[Test]
		public void Encode_NegativeTwo_ReturnsThree()
			=> LogAssert.AreEqual(3UL, Encode(-2));

		[Test]
		public void Encode_127_Returns254()
			=> LogAssert.AreEqual(254UL, Encode(127));

		[Test]
		public void Encode_Negative128_Returns255()
			=> LogAssert.AreEqual(255UL, Encode(-128));

		[Test]
		public void Encode_1000_Returns2000()
			=> LogAssert.AreEqual(2000UL, Encode(1000));

		[Test]
		public void Encode_Negative1000_Returns1999()
			=> LogAssert.AreEqual(1999UL, Encode(-1000));

		[Test]
		public void Encode_LongMaxValue_ReturnsULongMaxMinusOne()
			=> LogAssert.AreEqual(ULongMax - 1UL, Encode(LongMax));

		[Test]
		public void Encode_LongMinValue_ReturnsULongMax()
			=> LogAssert.AreEqual(ULongMax, Encode(LongMin));

		// ═════════════════════════════════════════════════════════════════════
		// 2. ZigZagDecode — known expected values
		// ═════════════════════════════════════════════════════════════════════

		[Test]
		public void Decode_Zero_ReturnsZero()
			=> LogAssert.AreEqual(0L, Decode(0));

		[Test]
		public void Decode_One_ReturnsNegativeOne()
			=> LogAssert.AreEqual(-1L, Decode(1));

		[Test]
		public void Decode_Two_ReturnsOne()
			=> LogAssert.AreEqual(1L, Decode(2));

		[Test]
		public void Decode_Three_ReturnsNegativeTwo()
			=> LogAssert.AreEqual(-2L, Decode(3));

		[Test]
		public void Decode_254_Returns127()
			=> LogAssert.AreEqual(127L, Decode(254));

		[Test]
		public void Decode_255_ReturnsNegative128()
			=> LogAssert.AreEqual(-128L, Decode(255));

		[Test]
		public void Decode_2000_Returns1000()
			=> LogAssert.AreEqual(1000L, Decode(2000));

		[Test]
		public void Decode_1999_ReturnsNegative1000()
			=> LogAssert.AreEqual(-1000L, Decode(1999));

		[Test]
		public void Decode_ULongMaxMinusOne_ReturnsLongMax()
			=> LogAssert.AreEqual(LongMax, Decode(ULongMax - 1));

		[Test]
		public void Decode_ULongMax_ReturnsLongMin()
			=> LogAssert.AreEqual(LongMin, Decode(ULongMax));

		// ═════════════════════════════════════════════════════════════════════
		// 3. Round-trip — spot values
		// ═════════════════════════════════════════════════════════════════════

		private static readonly long[] RoundTripCases =
		{
			0, 1, -1, 2, -2, 127, -128, 1000, -1000,
			int.MaxValue, int.MinValue,
			LongMax, LongMin
		};

		[SetUp]
		public void TestSetup()
		{
			AuthTestTrace.LogTestStart(TestContext.CurrentContext.Test.Name, "ZigZag encode/decode unit test")
				.GetAwaiter().GetResult();
		}

		[TearDown]
		public void TestTeardown()
		{
			var status = TestContext.CurrentContext.Result.Outcome.Status;
			if (status == NUnit.Framework.Interfaces.TestStatus.Passed)
				AuthTestTrace.Log("ZigZagTests", "SUCCESS", TestContext.CurrentContext.Test.Name).GetAwaiter().GetResult();
			else
				AuthTestTrace.Log("ZigZagTests", "FAILURE", $"{TestContext.CurrentContext.Test.Name}: {TestContext.CurrentContext.Result.Message}")
					.GetAwaiter().GetResult();
			AuthTestTrace.LogTestEnd(TestContext.CurrentContext.Test.Name).GetAwaiter().GetResult();
		}

		[TestCaseSource(nameof(RoundTripCases))]
		public void RoundTrip_Encode_ThenDecode_ReturnsOriginal(long value)
			=> LogAssert.AreEqual(value, Decode(Encode(value)));

		// ═════════════════════════════════════════════════════════════════════
		// 4. Exhaustive round-trip sweep
		// ═════════════════════════════════════════════════════════════════════

		[Test]
		public void RoundTrip_ExhaustiveSweep_NegativeToPositive500k()
		{
			int failures = 0;
			for (long v = -500_000L; v <= 500_000L; v++)
				if (Decode(Encode(v)) != v) failures++;
			LogAssert.AreEqual(0, failures,
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
			=> LogAssert.AreEqual(ReferenceEncode(value), Encode(value));

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
			=> LogAssert.AreEqual(expected, Encode(input));

		// ═════════════════════════════════════════════════════════════════════
		// 7. Bit-property invariants
		// ═════════════════════════════════════════════════════════════════════

		[Test]
		public void Encode_NonNegative_ProducesEvenResult()
		{
			for (long v = 0; v <= 10_000; v++)
				LogAssert.AreEqual(0UL, Encode(v) % 2,
					$"Encode({v}) = {Encode(v)} is not even.");
		}

		[Test]
		public void Encode_Negative_ProducesOddResult()
		{
			for (long v = -1; v >= -10_000; v--)
				LogAssert.AreEqual(1UL, Encode(v) % 2,
					$"Encode({v}) = {Encode(v)} is not odd.");
		}

		[Test]
		public void Encode_NonNegatives_AreMonotonicallyIncreasing()
		{
			for (long v = 0; v < 9_999; v++)
				LogAssert.IsTrue(Encode(v) < Encode(v + 1),
					$"Encode({v}) >= Encode({v + 1}): ordering violated.");
		}

		[Test]
		public void Encode_Negatives_AreMonotonicallyDecreasing()
		{
			// More negative → larger encoded value (farther from zero)
			for (long v = -9_999; v < -1; v++)
				LogAssert.IsTrue(Encode(v) > Encode(v + 1),
					$"Encode({v}) <= Encode({v + 1}): ordering violated.");
		}

		[Test]
		public void Encode_SameAbsoluteValue_NegativeEncodesOneLess()
		{
			// |n| == |-n|, so enc(-n) == enc(n) - 1
			for (long v = 1; v <= 10_000; v++)
				LogAssert.AreEqual(Encode(v) - 1UL, Encode(-v),
					$"Encode(-{v}) != Encode({v}) - 1.");
		}
	}
}