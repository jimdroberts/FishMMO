using System.Collections.Generic;
using FishMMO.Shared;
using NUnit.Framework;
using AuthTestTrace = FishMMO.UnitTests.Harness.AuthTestTrace;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Covers the pure decision functions behind per-observer streaming: relevance scoring,
	/// LOD intervals, density-scaled range, phase-spread send gating and config parsing.
	/// </summary>
	[TestFixture]
	public class ObserverStreamingPolicyTests
	{
		private int savedCap;
		private float savedCombat, savedParty, savedGuild, savedDistance;
		private float savedDensityRadius, savedScale, savedMinRange;
		private int savedLow, savedHigh;
		private List<ObserverStreamingPolicy.LodBand> savedBands;
		private byte savedMaxSendInterval, savedEngagedOverflow;

		[SetUp]
		public void SaveDefaults()
		{
			savedCap = ObserverStreamingPolicy.FullRateObserverCap;
			savedCombat = ObserverStreamingPolicy.CombatWeight;
			savedParty = ObserverStreamingPolicy.PartyWeight;
			savedGuild = ObserverStreamingPolicy.GuildWeight;
			savedDistance = ObserverStreamingPolicy.DistanceWeight;
			savedDensityRadius = ObserverStreamingPolicy.DensityRadius;
			savedScale = ObserverStreamingPolicy.RangeScaleAtHighDensity;
			savedMinRange = ObserverStreamingPolicy.MinimumRange;
			savedLow = ObserverStreamingPolicy.LowDensity;
			savedHigh = ObserverStreamingPolicy.HighDensity;
			savedBands = new List<ObserverStreamingPolicy.LodBand>(ObserverStreamingPolicy.LodBands);
			savedMaxSendInterval = ObserverStreamingPolicy.MaxSendInterval;
			savedEngagedOverflow = ObserverStreamingPolicy.EngagedOverflowInterval;
		}

		[TearDown]
		public void RestoreDefaults()
		{
			ObserverStreamingPolicy.FullRateObserverCap = savedCap;
			ObserverStreamingPolicy.CombatWeight = savedCombat;
			ObserverStreamingPolicy.PartyWeight = savedParty;
			ObserverStreamingPolicy.GuildWeight = savedGuild;
			ObserverStreamingPolicy.DistanceWeight = savedDistance;
			ObserverStreamingPolicy.DensityRadius = savedDensityRadius;
			ObserverStreamingPolicy.RangeScaleAtHighDensity = savedScale;
			ObserverStreamingPolicy.MinimumRange = savedMinRange;
			ObserverStreamingPolicy.LowDensity = savedLow;
			ObserverStreamingPolicy.HighDensity = savedHigh;
			ObserverStreamingPolicy.SetLodBands(savedBands);
			ObserverStreamingPolicy.MaxSendInterval = savedMaxSendInterval;
			ObserverStreamingPolicy.EngagedOverflowInterval = savedEngagedOverflow;
		}

		[Test]
		public void Score_CombatOutranksPartyOutranksGuildOutranksProximity()
		{
			AuthTestTrace.LogTestStart(nameof(Score_CombatOutranksPartyOutranksGuildOutranksProximity),
				"With default weights a distant fighter outranks a nearby party member, who outranks a guild member, who outranks a stranger.")
				.GetAwaiter().GetResult();

			float fighterFar = ObserverStreamingPolicy.Score(true, false, false, 90f, 100f);
			float partyNear = ObserverStreamingPolicy.Score(false, true, false, 5f, 100f);
			float guildNear = ObserverStreamingPolicy.Score(false, false, true, 5f, 100f);
			float strangerNear = ObserverStreamingPolicy.Score(false, false, false, 5f, 100f);

			LogAssert.IsTrue(fighterFar > partyNear, $"combat ({fighterFar}) must outrank party ({partyNear}).");
			LogAssert.IsTrue(partyNear > guildNear, $"party ({partyNear}) must outrank guild ({guildNear}).");
			LogAssert.IsTrue(guildNear > strangerNear, $"guild ({guildNear}) must outrank stranger ({strangerNear}).");
		}

		[Test]
		public void Score_CloserIsHigher_AndBeyondRangeContributesNothing()
		{
			AuthTestTrace.LogTestStart(nameof(Score_CloserIsHigher_AndBeyondRangeContributesNothing),
				"Proximity decreases monotonically with distance and is zero at or past the range.")
				.GetAwaiter().GetResult();

			float at0 = ObserverStreamingPolicy.Score(false, false, false, 0f, 100f);
			float at50 = ObserverStreamingPolicy.Score(false, false, false, 50f, 100f);
			float at100 = ObserverStreamingPolicy.Score(false, false, false, 100f, 100f);
			float at150 = ObserverStreamingPolicy.Score(false, false, false, 150f, 100f);

			LogAssert.IsTrue(at0 > at50 && at50 > at100, $"{at0} > {at50} > {at100} expected.");
			LogAssert.AreEqual(0f, at100, "At full range proximity is 0.");
			LogAssert.AreEqual(0f, at150, "Beyond full range proximity stays 0.");
		}

		[Test]
		public void LodInterval_UsesBandsAscending_AndFallsBackToFullRate()
		{
			AuthTestTrace.LogTestStart(nameof(LodInterval_UsesBandsAscending_AndFallsBackToFullRate),
				"Default band: every 2nd tick at any distance beyond the cap; bands ascend; nothing above MaxSendInterval; no band -> 1.")
				.GetAwaiter().GetResult();

			LogAssert.AreEqual((byte)2, ObserverStreamingPolicy.LodInterval(10f), "Beyond the cap, near: every 2nd tick.");
			LogAssert.AreEqual((byte)2, ObserverStreamingPolicy.LodInterval(500f),
				"Beyond the cap, far: still every 2nd tick. The 4- and 8-tick far bands this table used to carry " +
				"are what rendered as far-field NPCs teleporting.");

			/* The pre-2026-09-02 table, to show both that bands still ascend and that the ceiling
			 * now applies to whatever is configured. */
			ObserverStreamingPolicy.SetLodBands(new[]
			{
				new ObserverStreamingPolicy.LodBand(20f, 2),
				new ObserverStreamingPolicy.LodBand(45f, 4),
				new ObserverStreamingPolicy.LodBand(float.PositiveInfinity, 8),
			});
			LogAssert.AreEqual((byte)2, ObserverStreamingPolicy.LodInterval(10f), "10 m is in the first band.");
			LogAssert.AreEqual((byte)2, ObserverStreamingPolicy.LodInterval(20f), "Band edges are inclusive.");
			LogAssert.AreEqual(ObserverStreamingPolicy.MaxSendInterval, ObserverStreamingPolicy.LodInterval(30f),
				"A configured 4 is clamped to the interpolation ceiling.");
			LogAssert.AreEqual(ObserverStreamingPolicy.MaxSendInterval, ObserverStreamingPolicy.LodInterval(500f),
				"A configured 8 is clamped to the interpolation ceiling.");

			ObserverStreamingPolicy.MaxSendInterval = 8;
			LogAssert.AreEqual((byte)4, ObserverStreamingPolicy.LodInterval(30f), "Raising the ceiling releases the configured band.");
			LogAssert.AreEqual((byte)8, ObserverStreamingPolicy.LodInterval(500f), "Raising the ceiling releases the configured band.");

			ObserverStreamingPolicy.SetLodBands(null);
			LogAssert.AreEqual((byte)1, ObserverStreamingPolicy.LodInterval(500f), "No bands means never limited.");
		}

		[Test]
		public void SetLodBands_SortsByDistance()
		{
			AuthTestTrace.LogTestStart(nameof(SetLodBands_SortsByDistance),
				"Bands supplied out of order are sorted so the first match is the nearest band.")
				.GetAwaiter().GetResult();

			ObserverStreamingPolicy.SetLodBands(new[]
			{
				new ObserverStreamingPolicy.LodBand(float.PositiveInfinity, 8),
				new ObserverStreamingPolicy.LodBand(10f, 2),
				new ObserverStreamingPolicy.LodBand(30f, 4),
			});

			LogAssert.AreEqual(10f, ObserverStreamingPolicy.LodBands[0].MaxDistance, "Nearest band first after sort.");
			LogAssert.AreEqual(30f, ObserverStreamingPolicy.LodBands[1].MaxDistance, "Middle band.");
			LogAssert.IsTrue(float.IsPositiveInfinity(ObserverStreamingPolicy.LodBands[2].MaxDistance), "Infinite band last.");

			// Lift the ceiling so the intervals themselves prove which band answered.
			ObserverStreamingPolicy.MaxSendInterval = 8;
			LogAssert.AreEqual((byte)2, ObserverStreamingPolicy.LodInterval(5f), "Nearest band first after sort.");
			LogAssert.AreEqual((byte)4, ObserverStreamingPolicy.LodInterval(20f), "Middle band.");
			LogAssert.AreEqual((byte)8, ObserverStreamingPolicy.LodInterval(50f), "Infinite band last.");
		}

		[Test]
		public void EveryIntervalThePolicyHandsOut_IsClampedToMaxSendInterval()
		{
			AuthTestTrace.LogTestStart(nameof(EveryIntervalThePolicyHandsOut_IsClampedToMaxSendInterval),
				"Cap bands and the engaged overflow are read through the interpolation ceiling; the ceiling itself is configurable.")
				.GetAwaiter().GetResult();

			ObserverStreamingPolicy.MaxSendInterval = 2;
			ObserverStreamingPolicy.EngagedOverflowInterval = 6;
			LogAssert.AreEqual((byte)2, ObserverStreamingPolicy.EngagedOverflowInterval, "Engaged overflow is clamped on read.");
			LogAssert.AreEqual((byte)1, ObserverStreamingPolicy.ClampInterval(0), "Zero never means 'never send'.");
			LogAssert.AreEqual((byte)2, ObserverStreamingPolicy.ClampInterval(255), "Anything above the ceiling is the ceiling.");

			LogAssert.IsTrue(ObserverStreamingPolicy.ApplySetting("ObserverMaxSendInterval", "3"), "Ceiling key parses.");
			LogAssert.AreEqual((byte)3, ObserverStreamingPolicy.MaxSendInterval, "Ceiling applied.");
			LogAssert.AreEqual((byte)3, ObserverStreamingPolicy.EngagedOverflowInterval, "The stored 6 is now clamped to 3, not lost.");
			LogAssert.IsTrue(ObserverStreamingPolicy.ApplySetting("ObserverMaxSendInterval", "0"), "Out-of-range ceiling parses...");
			LogAssert.AreEqual((byte)1, ObserverStreamingPolicy.MaxSendInterval, "...and is clamped to at least 1.");
		}

		[Test]
		public void ScaledRange_FullBelowLowDensity_ScaledAtHigh_FlooredAtMinimum()
		{
			AuthTestTrace.LogTestStart(nameof(ScaledRange_FullBelowLowDensity_ScaledAtHigh_FlooredAtMinimum),
				"Sparse keeps 100 m; dense halves it; a small base range never drops below the floor.")
				.GetAwaiter().GetResult();

			ObserverStreamingPolicy.LowDensity = 8;
			ObserverStreamingPolicy.HighDensity = 40;
			ObserverStreamingPolicy.RangeScaleAtHighDensity = 0.5f;
			ObserverStreamingPolicy.MinimumRange = 25f;

			LogAssert.AreEqual(100f, ObserverStreamingPolicy.ScaledRange(100f, 0), "Empty area keeps full range.");
			LogAssert.AreEqual(100f, ObserverStreamingPolicy.ScaledRange(100f, 8), "At LowDensity keeps full range.");
			float mid = ObserverStreamingPolicy.ScaledRange(100f, 24);
			LogAssert.IsTrue(mid < 100f && mid > 50f, $"Midway density scales between full and half ({mid}).");
			LogAssert.AreEqual(50f, ObserverStreamingPolicy.ScaledRange(100f, 40), "At HighDensity applies the full scale.");
			LogAssert.AreEqual(50f, ObserverStreamingPolicy.ScaledRange(100f, 400), "Beyond HighDensity clamps.");
			LogAssert.AreEqual(25f, ObserverStreamingPolicy.ScaledRange(30f, 400), "Floor holds at MinimumRange.");
			LogAssert.AreEqual(15f, ObserverStreamingPolicy.ScaledRange(15f, 400), "A base range below the floor is never raised.");
		}

		[Test]
		public void ShouldSendThisTick_IntervalOneAlwaysSends()
		{
			AuthTestTrace.LogTestStart(nameof(ShouldSendThisTick_IntervalOneAlwaysSends),
				"Full rate sends on every tick regardless of phase.")
				.GetAwaiter().GetResult();

			for (uint tick = 0; tick < 20; ++tick)
			{
				LogAssert.IsTrue(ObserverStreamingPolicy.ShouldSendThisTick(tick, 1, 7), $"tick {tick} must send at interval 1.");
			}
		}

		[Test]
		public void ShouldSendThisTick_SendsExactlyOncePerInterval_AndPhaseSpreadsObservers()
		{
			AuthTestTrace.LogTestStart(nameof(ShouldSendThisTick_SendsExactlyOncePerInterval_AndPhaseSpreadsObservers),
				"Interval 4 sends 1 of every 4 ticks; two observers with different phases send on different ticks.")
				.GetAwaiter().GetResult();

			int sentA = 0, sentB = 0, sameTick = 0;
			for (uint tick = 0; tick < 400; ++tick)
			{
				bool a = ObserverStreamingPolicy.ShouldSendThisTick(tick, 4, 0);
				bool b = ObserverStreamingPolicy.ShouldSendThisTick(tick, 4, 1);
				if (a) sentA++;
				if (b) sentB++;
				if (a && b) sameTick++;
			}

			LogAssert.AreEqual(100, sentA, "Observer A sends 1 in 4.");
			LogAssert.AreEqual(100, sentB, "Observer B sends 1 in 4.");
			LogAssert.AreEqual(0, sameTick, "Adjacent phases never share a send tick.");
		}

		[Test]
		public void ApplySetting_ParsesKnownKeys_RejectsBadValues_IgnoresUnknown()
		{
			AuthTestTrace.LogTestStart(nameof(ApplySetting_ParsesKnownKeys_RejectsBadValues_IgnoresUnknown),
				"Server config keys map onto the policy; malformed values and unknown keys are refused.")
				.GetAwaiter().GetResult();

			LogAssert.IsTrue(ObserverStreamingPolicy.ApplySetting("ObserverFullRateCap", "12"), "Cap parses.");
			LogAssert.AreEqual(12, ObserverStreamingPolicy.FullRateObserverCap, "Cap applied.");
			LogAssert.IsTrue(ObserverStreamingPolicy.ApplySetting("ObserverCombatWeight", "250.5"), "Float parses.");
			LogAssert.AreEqual(250.5f, ObserverStreamingPolicy.CombatWeight, "Float applied.");
			LogAssert.IsTrue(ObserverStreamingPolicy.ApplySetting("ObserverLodBands", "15:2, 30:3, inf:6"), "Bands parse.");
			ObserverStreamingPolicy.MaxSendInterval = 8;
			LogAssert.AreEqual((byte)3, ObserverStreamingPolicy.LodInterval(20f), "Parsed bands are in effect.");
			LogAssert.AreEqual((byte)6, ObserverStreamingPolicy.LodInterval(1000f), "Infinite band parsed.");
			ObserverStreamingPolicy.MaxSendInterval = 2;
			LogAssert.AreEqual((byte)2, ObserverStreamingPolicy.LodInterval(1000f), "Configured bands still obey the ceiling.");

			LogAssert.IsFalse(ObserverStreamingPolicy.ApplySetting("ObserverFullRateCap", "twelve"), "Bad int refused.");
			LogAssert.AreEqual(12, ObserverStreamingPolicy.FullRateObserverCap, "Bad value leaves the cap unchanged.");
			LogAssert.IsFalse(ObserverStreamingPolicy.ApplySetting("ObserverLodBands", "15-2"), "Bad band syntax refused.");
			LogAssert.IsFalse(ObserverStreamingPolicy.ApplySetting("NotAKey", "1"), "Unknown key ignored.");
		}
	}
}
