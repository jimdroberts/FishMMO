using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Weather;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// The weather model both sides evaluate: how frames blend, how a storm re-types with the
	/// temperature, how cover builds and clears, what is added to the air, and how the timeline
	/// moves between revisions.
	/// </summary>
	/// <remarks>
	/// Server and client run this code over the same data and the same tick. Anything here that is
	/// not a pure function of its inputs would put the two out of step without a single message
	/// being lost.
	/// </remarks>
	[TestFixture]
	public class WeatherModelTests
	{
		private const double TickDelta = 1.0 / 30.0;
		private const float Tolerance = 1e-4f;

		private readonly List<ScriptableObject> created = new List<ScriptableObject>();

		[TearDown]
		public void TearDown()
		{
			foreach (ScriptableObject asset in created)
			{
				Object.DestroyImmediate(asset);
			}
			created.Clear();
		}

		private static WeatherFrame Frame(params (WeatherChannel channel, float value)[] values)
		{
			var frame = new WeatherFrame();
			foreach (var (channel, value) in values)
			{
				frame[channel] = value;
			}
			return frame;
		}

		// ── Blending ──────────────────────────────────────────────────

		[Test]
		public void TheChannelTableCoversEveryChannel()
		{
			LogAssert.AreEqual(WeatherChannels.Count, System.Enum.GetValues(typeof(WeatherChannel)).Length,
				"WeatherChannels.Count must match the enum, or a channel is never blended or sent");
			var frame = new WeatherFrame();
			for (int i = 0; i < WeatherChannels.Count; i++)
			{
				frame[i] = i + 1;
			}
			for (int i = 0; i < WeatherChannels.Count; i++)
			{
				LogAssert.AreEqual((float)(i + 1), frame[i], $"channel {(WeatherChannel)i} must have its own storage");
			}
		}

		[Test]
		public void TwoCloudyLayersAreNotTwiceAsCloudy()
		{
			var acc = new WeatherAccumulator();
			acc.Add(Frame((WeatherChannel.CloudCover, 0.6f)), 1f);
			acc.Add(Frame((WeatherChannel.CloudCover, 0.4f)), 1f);
			Assert.That(acc.Resolve()[WeatherChannel.CloudCover], Is.EqualTo(0.6f).Within(Tolerance));
		}

		[Test]
		public void PrecipitationCombinesAsASoftSum()
		{
			var acc = new WeatherAccumulator();
			acc.Add(Frame((WeatherChannel.Precipitation, 0.5f), (WeatherChannel.RainWeight, 1f)), 1f);
			acc.Add(Frame((WeatherChannel.Precipitation, 0.5f), (WeatherChannel.RainWeight, 1f)), 1f);
			WeatherFrame result = acc.Resolve();
			Assert.That(result[WeatherChannel.Precipitation], Is.EqualTo(0.75f).Within(Tolerance));
			LogAssert.IsTrue(result[WeatherChannel.Precipitation] <= 1f, "a soft sum never exceeds 1");
		}

		[Test]
		public void WhatFallsIsWeightedByHowMuchFalls()
		{
			var acc = new WeatherAccumulator();
			acc.Add(Frame((WeatherChannel.Precipitation, 1f), (WeatherChannel.RainWeight, 1f)), 1f);
			acc.Add(Frame((WeatherChannel.Precipitation, 0.5f), (WeatherChannel.SnowWeight, 1f)), 1f);
			WeatherFrame result = acc.Resolve();
			Assert.That(result[WeatherChannel.RainWeight], Is.EqualTo(2f / 3f).Within(Tolerance));
			Assert.That(result[WeatherChannel.SnowWeight], Is.EqualTo(1f / 3f).Within(Tolerance));
			LogAssert.AreEqual(PrecipitationKind.Sleet, result.DominantPrecipitation, "rain and snow together read as sleet");
		}

		[Test]
		public void PrecipitationWithNoTypeStops()
		{
			var acc = new WeatherAccumulator();
			acc.Add(Frame((WeatherChannel.Precipitation, 1f)), 1f);
			LogAssert.AreEqual(0f, acc.Resolve()[WeatherChannel.Precipitation],
				"precipitation that names nothing falling cannot be drawn, so it must not be reported");
		}

		[Test]
		public void OpposingWindsCancel()
		{
			var acc = new WeatherAccumulator();
			acc.Add(Frame((WeatherChannel.WindSpeed, 0.5f), (WeatherChannel.WindHeading, 90f)), 1f);
			acc.Add(Frame((WeatherChannel.WindSpeed, 0.5f), (WeatherChannel.WindHeading, 270f)), 1f);
			Assert.That(acc.Resolve()[WeatherChannel.WindSpeed], Is.EqualTo(0f).Within(Tolerance));
		}

		[Test]
		public void AlignedWindsAddAndKeepTheirHeading()
		{
			var acc = new WeatherAccumulator();
			acc.Add(Frame((WeatherChannel.WindSpeed, 0.3f), (WeatherChannel.WindHeading, 45f)), 1f);
			acc.Add(Frame((WeatherChannel.WindSpeed, 0.2f), (WeatherChannel.WindHeading, 45f)), 1f);
			WeatherFrame result = acc.Resolve();
			Assert.That(result[WeatherChannel.WindSpeed], Is.EqualTo(0.5f).Within(Tolerance));
			Assert.That(result[WeatherChannel.WindHeading], Is.EqualTo(45f).Within(0.01f));
		}

		[Test]
		public void ClimateOffsetsAddAndClamp()
		{
			var acc = new WeatherAccumulator();
			acc.Add(Frame((WeatherChannel.TemperatureOffset, 0.3f)), 1f);
			acc.Add(Frame((WeatherChannel.TemperatureOffset, 0.4f)), 1f);
			Assert.That(acc.Resolve()[WeatherChannel.TemperatureOffset], Is.EqualTo(0.7f).Within(Tolerance));

			acc.Add(Frame((WeatherChannel.TemperatureOffset, 0.9f)), 1f);
			LogAssert.AreEqual(1f, acc.Resolve()[WeatherChannel.TemperatureOffset]);
		}

		[Test]
		public void AWeightScalesALayer()
		{
			var acc = new WeatherAccumulator();
			acc.Add(Frame((WeatherChannel.CloudCover, 1f)), 0.25f);
			Assert.That(acc.Resolve()[WeatherChannel.CloudCover], Is.EqualTo(0.25f).Within(Tolerance));

			var none = new WeatherAccumulator();
			none.Add(Frame((WeatherChannel.CloudCover, 1f)), 0f);
			LogAssert.IsFalse(none.HasAny, "a zero-weight layer adds nothing");
		}

		[Test]
		public void SnowfallFillsTheSnowCoverRate()
		{
			var acc = new WeatherAccumulator();
			acc.Add(Frame((WeatherChannel.Precipitation, 0.8f), (WeatherChannel.SnowWeight, 1f)), 1f);
			WeatherFrame result = acc.Resolve();
			Assert.That(result[WeatherChannel.SnowCoverRate], Is.EqualTo(0.8f).Within(Tolerance));
			LogAssert.AreEqual(0f, result[WeatherChannel.WetnessTarget], "dry snow does not wet the ground");
		}

		[Test]
		public void SuppressingRainLeavesTheRestNormalised()
		{
			WeatherFrame frame = Frame((WeatherChannel.Precipitation, 1f), (WeatherChannel.RainWeight, 0.5f), (WeatherChannel.SnowWeight, 0.5f));
			frame.Suppress(WeatherKindMask.Rain);
			LogAssert.AreEqual(0f, frame[WeatherChannel.RainWeight]);
			Assert.That(frame[WeatherChannel.SnowWeight], Is.EqualTo(1f).Within(Tolerance));
			LogAssert.AreEqual(1f, frame[WeatherChannel.Precipitation]);

			frame.Suppress(WeatherKindMask.Snow);
			LogAssert.AreEqual(0f, frame[WeatherChannel.Precipitation], "suppressing everything that falls stops the fall");
		}

		// ── Temperature ───────────────────────────────────────────────

		[Test]
		public void TheSameStormRainsWhenWarmAndSnowsWhenCold()
		{
			WeatherFrame warm = Frame((WeatherChannel.RainWeight, 1f));
			warm.RetypeForTemperature(0.5f);
			LogAssert.AreEqual(1f, warm[WeatherChannel.RainWeight]);
			LogAssert.AreEqual(0f, warm[WeatherChannel.SnowWeight]);

			WeatherFrame cold = Frame((WeatherChannel.RainWeight, 1f));
			cold.RetypeForTemperature(-0.5f);
			LogAssert.AreEqual(0f, cold[WeatherChannel.RainWeight]);
			LogAssert.AreEqual(1f, cold[WeatherChannel.SnowWeight]);

			WeatherFrame sleet = Frame((WeatherChannel.SnowWeight, 1f));
			sleet.RetypeForTemperature(-0.05f);
			Assert.That(sleet[WeatherChannel.RainWeight], Is.EqualTo(0.5f).Within(Tolerance));
			Assert.That(sleet[WeatherChannel.SnowWeight], Is.EqualTo(0.5f).Within(Tolerance));
		}

		[Test]
		public void RetypingLeavesHailAshAndSandAlone()
		{
			WeatherFrame frame = Frame((WeatherChannel.HailWeight, 0.4f), (WeatherChannel.AshWeight, 0.3f), (WeatherChannel.SandWeight, 0.3f));
			frame.RetypeForTemperature(-1f);
			LogAssert.AreEqual(0.4f, frame[WeatherChannel.HailWeight]);
			LogAssert.AreEqual(0.3f, frame[WeatherChannel.AshWeight]);
			LogAssert.AreEqual(0.3f, frame[WeatherChannel.SandWeight]);
			LogAssert.AreEqual(0f, frame[WeatherChannel.SnowWeight]);
		}

		// ── Cover ─────────────────────────────────────────────────────

		[Test]
		public void SnowBuildsInTheColdAndMeltsIntoWetGround()
		{
			var cover = new WeatherCover();
			WeatherFrame snowing = Frame((WeatherChannel.SnowCoverRate, 1f));
			cover.Integrate(snowing, -0.5f, WeatherCover.SnowFillSeconds / 2f);
			Assert.That(cover.Snow, Is.EqualTo(0.5f).Within(Tolerance));
			cover.Integrate(snowing, -0.5f, WeatherCover.SnowFillSeconds);
			LogAssert.AreEqual(1f, cover.Snow, "cover is clamped");

			var clear = new WeatherFrame();
			cover.Integrate(clear, -0.5f, 600f);
			LogAssert.AreEqual(1f, cover.Snow, "snow does not melt below freezing");

			cover.Integrate(clear, 0.5f, 60f);
			LogAssert.IsTrue(cover.Snow < 1f, "snow melts above freezing");
			LogAssert.IsTrue(cover.Wet > 0f, "melting snow wets the ground");

			for (int i = 0; i < 100; i++)
			{
				cover.Integrate(clear, 0.5f, 60f);
			}
			LogAssert.AreEqual(0f, cover.Snow);
			LogAssert.AreEqual(0f, cover.Wet, "ground dries once the snow is gone");
		}

		[Test]
		public void RainWetsQuicklyAndDriesSlowly()
		{
			var cover = new WeatherCover();
			cover.Integrate(Frame((WeatherChannel.WetnessTarget, 1f)), 0.3f, WeatherCover.WetFillSeconds);
			LogAssert.AreEqual(1f, cover.Wet);

			cover.Integrate(new WeatherFrame(), 0f, WeatherCover.WetFillSeconds);
			LogAssert.IsTrue(cover.Wet > 0.5f, $"drying must be slower than wetting, got {cover.Wet}");
		}

		[Test]
		public void CoverIntegratesTheSameInOneStepOrMany()
		{
			/* The client catches cover up from the server's snapshot in one step; the server advances
			 * it every second. A linear rate makes the two agree away from the clamps. */
			WeatherFrame frame = Frame((WeatherChannel.SnowCoverRate, 0.5f), (WeatherChannel.AshCoverRate, 0.25f));
			var once = new WeatherCover();
			once.Integrate(frame, -0.5f, 120f);
			var many = new WeatherCover();
			for (int i = 0; i < 120; i++)
			{
				many.Integrate(frame, -0.5f, 1f);
			}
			Assert.That(many.Snow, Is.EqualTo(once.Snow).Within(Tolerance));
			Assert.That(many.Ash, Is.EqualTo(once.Ash).Within(Tolerance));
		}

		// ── Timeline ──────────────────────────────────────────────────

		[Test]
		public void AddedAirEasesBetweenItsTicks()
		{
			var entry = new AirOffsetEntry { From = default, To = new AirOffsets { Humidity = 0.4f, Temperature = -10f }, StartSeconds = 100, EndSeconds = 200 };
			LogAssert.AreEqual(0f, entry.AtSeconds(50).Humidity);
			LogAssert.AreEqual(0f, entry.AtSeconds(100).Humidity);
			Assert.That(entry.AtSeconds(150).Humidity, Is.EqualTo(0.2f).Within(Tolerance));
			Assert.That(entry.AtSeconds(150).Temperature, Is.EqualTo(-5f).Within(Tolerance));
			LogAssert.AreEqual(0.4f, entry.AtSeconds(200).Humidity);
			LogAssert.AreEqual(0.4f, entry.AtSeconds(5000).Humidity);

			float last = 0f;
			for (uint t = 100; t <= 200; t++)
			{
				float value = entry.AtSeconds(t).Humidity;
				LogAssert.IsTrue(value >= last, $"what is added must not fall on the way up (tick {t})");
				last = value;
			}
		}

		[Test]
		public void AnInstantChangeToTheAirJumps()
		{
			var entry = new AirOffsetEntry { From = new AirOffsets { Pressure = 0.2f }, To = new AirOffsets { Pressure = 0.9f }, StartSeconds = 100, EndSeconds = 100 };
			LogAssert.AreEqual(0.2f, entry.AtSeconds(99).Pressure);
			LogAssert.AreEqual(0.9f, entry.AtSeconds(100).Pressure);
		}

		[Test]
		public void AdditionsToTheAirAddScaleAndStayAdditions()
		{
			var a = new AirOffsets { Temperature = 5f, Humidity = 0.1f, Wind = 2f };
			var b = new AirOffsets { Temperature = -3f, Pressure = 0.4f, Gravity = 1f };
			AirOffsets sum = a + b;
			LogAssert.AreEqual(2f, sum.Temperature);
			LogAssert.AreEqual(0.1f, sum.Humidity);
			LogAssert.AreEqual(0.4f, sum.Pressure);
			LogAssert.AreEqual(2f, sum.Wind);
			LogAssert.AreEqual(1f, sum.Gravity);
			LogAssert.IsTrue(default(AirOffsets).IsZero, "nothing added is zero");
			LogAssert.IsFalse(sum.IsZero);
			Assert.That(new AirOffsets { Temperature = 33.1f }.TemperatureScale, Is.EqualTo(1f).Within(1e-3f), "the temperature is kelvin, the scale's unit is 33.1 of them");
		}

		[Test]
		public void AdditionsChangeTheAirAndNeverReplaceIt()
		{
			var air = new WeatherDriver.Synoptic { Humidity = 0.5f, Pressure = 0.1f, Instability = 0.3f, Wind = new Vector2(3f, 4f) };
			WeatherDriver.Synoptic changed = new AirOffsets { Humidity = 0.2f, Pressure = -0.5f, Instability = 0.1f, Wind = 5f }.Apply(air);
			Assert.That(changed.Humidity, Is.EqualTo(0.7f).Within(Tolerance));
			Assert.That(changed.Pressure, Is.EqualTo(-0.4f).Within(Tolerance));
			Assert.That(changed.Instability, Is.EqualTo(0.4f).Within(Tolerance));
			Assert.That(changed.Wind.magnitude, Is.EqualTo(10f).Within(1e-3f), "wind is added along the way it already blows");
			Assert.That(Vector2.Angle(changed.Wind, air.Wind), Is.LessThan(0.01f));
			WeatherDriver.Synoptic calmed = new AirOffsets { Wind = -20f }.Apply(air);
			LogAssert.AreEqual(0f, calmed.Wind.magnitude, "a wind taken below nothing is calm, not blowing backwards");
			WeatherDriver.Synoptic same = default(AirOffsets).Apply(air);
			LogAssert.AreEqual(air.Humidity, same.Humidity);
		}

		[Test]
		public void PruningForgetsDeadCells()
		{
			var timeline = new WeatherTimeline();
			timeline.Cells.Add(new StormCell { ID = 7, BirthSeconds = 0 * TickDelta, MatureSeconds = 10 * TickDelta, DecaySeconds = 20 * TickDelta, DeathSeconds = 30 * TickDelta });
			timeline.Cells.Add(new StormCell { ID = 8, BirthSeconds = 0 * TickDelta, MatureSeconds = 10 * TickDelta, DecaySeconds = 200 * TickDelta, DeathSeconds = 300 * TickDelta });
			timeline.Prune(50);
			LogAssert.AreEqual(1, timeline.Cells.Count);
			LogAssert.AreEqual((ushort)8, timeline.Cells[0].ID);
		}

		[Test]
		public void TheLeadCoversOneAndAHalfSeconds()
		{
			var timeline = new WeatherTimeline { TickDelta = TickDelta };
			// In world time at the world's pace: real time, with no world clock anchored.
			Assert.That(timeline.LeadWorldSeconds, Is.EqualTo(1.5).Within(1e-9));
		}

		[Test]
		public void WhatTheGroundGivesUpFallsAsItsOwnKind()
		{
			var sand = ScriptableObject.CreateInstance<WeatherSubstance>();
			created.Add(sand);
			sand.Cover = WeatherCoverKind.Sand;
			WeatherFrame frame = WeatherPhysics.Falling(sand, 0.6f);
			Assert.That(frame[WeatherChannel.Precipitation], Is.EqualTo(0.6f).Within(Tolerance));
			LogAssert.AreEqual(1f, frame[WeatherChannel.SandWeight]);
			sand.Cover = WeatherCoverKind.Ash;
			LogAssert.AreEqual(1f, WeatherPhysics.Falling(sand, 0.6f)[WeatherChannel.AshWeight]);
		}

		// ── Deltas ────────────────────────────────────────────────────

		private static WeatherTimeline TimelineAt(uint revision)
		{
			var timeline = new WeatherTimeline { SceneName = "Test", Revision = revision, TickDelta = TickDelta };
			timeline.Cells.Add(new StormCell { ID = 1, Kind = StormKind.Thunderstorm, DeathSeconds = 1000 * TickDelta });
			return timeline;
		}

		[Test]
		public void TheNextDeltaApplies()
		{
			WeatherTimeline timeline = TimelineAt(4);
			var delta = new WeatherDeltaBroadcast
			{
				SceneName = "Test",
				Revision = 5,
				RemovedCells = new List<ushort> { 1 },
				Cells = new List<StormCell> { new StormCell { ID = 2, Kind = StormKind.Haboob, DeathSeconds = 1000 * TickDelta } },
				HasAir = true,
				Air = new AirOffsetEntry { To = new AirOffsets { Temperature = -4f } },
			};
			LogAssert.IsTrue(timeline.TryApply(delta));
			LogAssert.AreEqual(5u, timeline.Revision);
			LogAssert.AreEqual(1, timeline.Cells.Count);
			LogAssert.AreEqual((ushort)2, timeline.Cells[0].ID);
			LogAssert.AreEqual(StormKind.Haboob, timeline.Cells[0].Kind);
			LogAssert.AreEqual(-4f, timeline.Air.To.Temperature);
		}

		[Test]
		public void ADuplicateDeltaIsHarmless()
		{
			WeatherTimeline timeline = TimelineAt(5);
			var stale = new WeatherDeltaBroadcast { SceneName = "Test", Revision = 5, RemovedCells = new List<ushort> { 1 } };
			LogAssert.IsTrue(timeline.TryApply(stale), "an old delta needs no resync");
			LogAssert.AreEqual(1, timeline.Cells.Count, "an old delta changes nothing");
			LogAssert.AreEqual(5u, timeline.Revision);
		}

		[Test]
		public void AGapChangesNothingAndAsksForTheWholeTimeline()
		{
			WeatherTimeline timeline = TimelineAt(5);
			var skipped = new WeatherDeltaBroadcast { SceneName = "Test", Revision = 7, RemovedCells = new List<ushort> { 1 }, HasCover = true, Cover = new WeatherCover { Snow = 1f } };
			LogAssert.IsTrue(timeline.IsGap(skipped));
			LogAssert.IsFalse(timeline.TryApply(skipped));
			LogAssert.AreEqual(5u, timeline.Revision);
			LogAssert.AreEqual(1, timeline.Cells.Count);
			LogAssert.AreEqual(0f, timeline.Cover.Snow);
		}

		[Test]
		public void AFullTimelineReplacesEverything()
		{
			WeatherTimeline source = TimelineAt(9);
			source.Seed = 42;
			source.SceneMode = WeatherSceneMode.None;
			source.Air = new AirOffsetEntry { From = new AirOffsets { Humidity = 0.1f }, To = new AirOffsets { Humidity = 0.3f }, StartSeconds = 10, EndSeconds = 20 };
			source.Cover = new WeatherCover { Snow = 0.2f, Wet = 0.4f };
			source.CoverSeconds = 1234.5;

			WeatherTimeline target = TimelineAt(2);
			target.Cells.Add(new StormCell { ID = 99 });
			target.Apply(source.ToBroadcast());

			LogAssert.AreEqual(9u, target.Revision);
			LogAssert.AreEqual(42u, target.Seed);
			LogAssert.AreEqual(WeatherSceneMode.None, target.SceneMode);
			LogAssert.AreEqual(0.3f, target.Air.To.Humidity);
			LogAssert.AreEqual(20.0, target.Air.EndSeconds);
			LogAssert.AreEqual(1, target.Cells.Count);
			LogAssert.AreEqual(0.2f, target.Cover.Snow);
			LogAssert.AreEqual(1234.5, target.CoverSeconds);

			WeatherTimelineBroadcast copy = source.ToBroadcast();
			copy.Cells.Clear();
			LogAssert.AreEqual(1, source.Cells.Count, "the broadcast must not share the timeline's lists");
		}

		// ── Storm cells ───────────────────────────────────────────────

		private static StormCell Cell()
		{
			return new StormCell
			{
				ID = 1,
				Seed = 99,
				OriginX = 100f,
				OriginZ = -50f,
				VelocityX = 2f,
				VelocityZ = 1f,
				RadiusMeters = 400f,
				PeakIntensity = 0.8f,
				MotionSeconds = 300 * TickDelta,
				BirthSeconds = 300 * TickDelta,
				MatureSeconds = 600 * TickDelta,
				DecaySeconds = 3000 * TickDelta,
				DeathSeconds = 3300 * TickDelta,
			};
		}

		[Test]
		public void ACellGrowsHoldsAndFades()
		{
			StormCell cell = Cell();
			LogAssert.AreEqual(0f, cell.EnvelopeAtSeconds((0) * TickDelta));
			LogAssert.AreEqual(0f, cell.EnvelopeAtSeconds((300) * TickDelta));
			Assert.That(cell.EnvelopeAtSeconds((450) * TickDelta), Is.EqualTo(0.5f).Within(Tolerance));
			LogAssert.AreEqual(1f, cell.EnvelopeAtSeconds((600) * TickDelta));
			LogAssert.AreEqual(1f, cell.EnvelopeAtSeconds((3000) * TickDelta));
			Assert.That(cell.EnvelopeAtSeconds((3150) * TickDelta), Is.EqualTo(0.5f).Within(Tolerance));
			LogAssert.AreEqual(0f, cell.EnvelopeAtSeconds((3300) * TickDelta));
			LogAssert.IsTrue(cell.IsDeadAtSeconds((3300) * TickDelta));
			LogAssert.IsFalse(cell.IsDeadAtSeconds((3299) * TickDelta));

			float previous = -1f;
			for (uint t = 300; t <= 600; t++)
			{
				float value = cell.EnvelopeAtSeconds((t) * TickDelta);
				LogAssert.IsTrue(value >= previous, $"growth must not dip (tick {t})");
				previous = value;
			}
		}

		[Test]
		public void ACellDriftsWithItsVelocity()
		{
			StormCell cell = Cell();
			Vector2 start = cell.CentreAtSeconds((300) * TickDelta);
			LogAssert.AreEqual(new Vector2(100f, -50f), start);

			Vector2 later = cell.CentreAtSeconds((300 + 30 * 60) * TickDelta);
			Assert.That(later.x, Is.EqualTo(100f + 2f * 60f).Within(0.01f));
			Assert.That(later.y, Is.EqualTo(-50f + 1f * 60f).Within(0.01f));
		}

		[Test]
		public void ACellMeandersTheSameWayEverywhere()
		{
			StormCell a = Cell();
			a.MeanderMeters = 150f;
			StormCell b = a;
			for (uint t = 300; t < 3300; t += 97)
			{
				LogAssert.AreEqual(a.CentreAtSeconds((t) * TickDelta), b.CentreAtSeconds((t) * TickDelta));
				Vector2 straight = new Vector2(100f + 2f * (float)((t - 300) * TickDelta), -50f + (float)((t - 300) * TickDelta));
				LogAssert.IsTrue(Vector2.Distance(a.CentreAtSeconds((t) * TickDelta), straight) <= 150f * Mathf.Sqrt(2f) + 0.01f,
					"the wander stays within its amplitude");
			}
		}

		[Test]
		public void ACellIsStrongestAtItsCentreAndAbsentBeyondItsRadius()
		{
			StormCell cell = Cell();
			const uint tick = 1000;
			Vector2 centre = cell.CentreAtSeconds((tick) * TickDelta);
			var at = new Vector3(centre.x, 0f, centre.y);
			Assert.That(cell.InfluenceAtSeconds(at, (tick) * TickDelta), Is.EqualTo(0.8f).Within(Tolerance));
			Assert.That(cell.InfluenceAtSeconds(at + new Vector3(200f, 0f, 0f), (tick) * TickDelta), Is.EqualTo(0.8f).Within(Tolerance), "full strength inside 55% of the radius");
			float edge = cell.InfluenceAtSeconds(at + new Vector3(300f, 0f, 0f), (tick) * TickDelta);
			LogAssert.IsTrue(edge > 0f && edge < 0.8f, $"fading toward the edge, got {edge}");
			LogAssert.AreEqual(0f, cell.InfluenceAtSeconds(at + new Vector3(0f, 0f, 401f), (tick) * TickDelta));
			LogAssert.AreEqual(0f, cell.InfluenceAtSeconds(at, (3400) * TickDelta), "a dead cell has no influence");
		}
	}
}
