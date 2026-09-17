using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Weather;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// The weather model both sides evaluate: how layers blend, how a storm re-types with the
	/// temperature, how cover builds and clears, and how the timeline moves between revisions.
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
				if (asset is WeatherLayerTemplate template)
				{
					template.RemoveFromCache();
				}
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

		private WeatherLayerTemplate CachedTemplate(string name, WeatherLayerKind kind, WeatherChannel channel)
		{
			var template = ScriptableObject.CreateInstance<WeatherLayerTemplate>();
			template.name = name;
			template.Kind = kind;
			template.Channels.Add(new WeatherChannelCurve { Channel = channel, Curve = AnimationCurve.Linear(0f, 0f, 1f, 1f), Scale = 1f });
			template.AddToCache(name);
			created.Add(template);
			return template;
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
		public void ALayerEasesBetweenItsTicks()
		{
			var entry = new WeatherLayerEntry { From = 0f, To = 1f, StartTick = 100, EndTick = 200 };
			LogAssert.AreEqual(0f, entry.IntensityAt(50));
			LogAssert.AreEqual(0f, entry.IntensityAt(100));
			Assert.That(entry.IntensityAt(150), Is.EqualTo(0.5f).Within(Tolerance));
			LogAssert.AreEqual(1f, entry.IntensityAt(200));
			LogAssert.AreEqual(1f, entry.IntensityAt(5000));

			float last = 0f;
			for (uint t = 100; t <= 200; t++)
			{
				float value = entry.IntensityAt(t);
				LogAssert.IsTrue(value >= last, $"intensity must not fall on the way up (tick {t})");
				last = value;
			}
		}

		[Test]
		public void AnInstantLayerJumps()
		{
			var entry = new WeatherLayerEntry { From = 0.2f, To = 0.9f, StartTick = 100, EndTick = 100 };
			LogAssert.AreEqual(0.2f, entry.IntensityAt(99));
			LogAssert.AreEqual(0.9f, entry.IntensityAt(100));
		}

		[Test]
		public void PruningForgetsFinishedRemovalsAndDeadCells()
		{
			var timeline = new WeatherTimeline();
			timeline.Layers.Add(new WeatherLayerEntry { Handle = 1, From = 1f, To = 0f, StartTick = 0, EndTick = 100, RemoveWhenDone = true });
			timeline.Layers.Add(new WeatherLayerEntry { Handle = 2, From = 0f, To = 1f, StartTick = 0, EndTick = 100 });
			timeline.Cells.Add(new StormCell { ID = 7, BirthTick = 0, MatureTick = 10, DecayTick = 20, DeathTick = 30 });

			timeline.Prune(50);
			LogAssert.AreEqual(2, timeline.Layers.Count, "a removal is kept until it has faded out");
			LogAssert.AreEqual(0, timeline.Cells.Count);

			timeline.Prune(100);
			LogAssert.AreEqual(1, timeline.Layers.Count);
			LogAssert.AreEqual((ushort)2, timeline.Layers[0].Handle, "a finished fade-in is kept");
		}

		[Test]
		public void TheLeadCoversOneAndAHalfSeconds()
		{
			var timeline = new WeatherTimeline { TickDelta = TickDelta };
			LogAssert.AreEqual(45u, timeline.LeadTicks);
			LogAssert.AreEqual(300u, timeline.SecondsToTicks(10f));
			LogAssert.AreEqual(0u, timeline.SecondsToTicks(0f));
		}

		[Test]
		public void SceneLayersAreEvaluatedThroughTheirTemplates()
		{
			WeatherLayerTemplate clouds = CachedTemplate("Test Clouds", WeatherLayerKind.Clouds, WeatherChannel.CloudCover);
			WeatherLayerTemplate warmth = CachedTemplate("Test Warmth", WeatherLayerKind.Clouds, WeatherChannel.TemperatureOffset);
			LogAssert.AreNotEqual(0, clouds.ID, "the template must be registered for the timeline to find it");

			var timeline = new WeatherTimeline { TickDelta = TickDelta };
			timeline.Layers.Add(new WeatherLayerEntry { Handle = 1, TemplateID = clouds.ID, From = 0f, To = 1f, StartTick = 0, EndTick = 100 });
			timeline.Layers.Add(new WeatherLayerEntry { Handle = 2, TemplateID = warmth.ID, From = 0.25f, To = 0.25f, StartTick = 0, EndTick = 0 });
			timeline.Layers.Add(new WeatherLayerEntry { Handle = 3, TemplateID = 12345, From = 1f, To = 1f });

			var acc = new WeatherAccumulator();
			timeline.AccumulateSceneLayers(50, ref acc);
			WeatherFrame frame = acc.Resolve();
			Assert.That(frame[WeatherChannel.CloudCover], Is.EqualTo(0.5f).Within(Tolerance));

			timeline.Climate = new WeatherClimateEntry { FromTemperature = 0.1f, ToTemperature = 0.1f, FromHumidity = -0.2f, ToHumidity = -0.2f };
			timeline.ClimateAt(50, out float temperature, out float humidity);
			Assert.That(temperature, Is.EqualTo(0.35f).Within(Tolerance), "the climate shift and the layers' offsets add");
			Assert.That(humidity, Is.EqualTo(-0.2f).Within(Tolerance));
		}

		[Test]
		public void APrecipitatingLayerFallsAsItsOwnKind()
		{
			WeatherLayerTemplate snow = CachedTemplate("Test Snow", WeatherLayerKind.Snow, WeatherChannel.Precipitation);
			WeatherFrame frame = snow.Evaluate(0.6f);
			Assert.That(frame[WeatherChannel.Precipitation], Is.EqualTo(0.6f).Within(Tolerance));
			LogAssert.AreEqual(1f, frame[WeatherChannel.SnowWeight]);
		}

		// ── Deltas ────────────────────────────────────────────────────

		private static WeatherTimeline TimelineAt(uint revision)
		{
			var timeline = new WeatherTimeline { SceneName = "Test", Revision = revision, TickDelta = TickDelta };
			timeline.Layers.Add(new WeatherLayerEntry { Handle = 1, TemplateID = 10, To = 0.5f });
			timeline.Cells.Add(new StormCell { ID = 1, PresetID = 20, DeathTick = 1000 });
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
				Layers = new List<WeatherLayerEntry> { new WeatherLayerEntry { Handle = 1, TemplateID = 10, To = 0.9f }, new WeatherLayerEntry { Handle = 2, TemplateID = 11, To = 1f } },
				RemovedCells = new List<ushort> { 1 },
				Cells = new List<StormCell> { new StormCell { ID = 2, PresetID = 21, DeathTick = 1000 } },
				HasClimate = true,
				Climate = new WeatherClimateEntry { ToTemperature = 0.4f },
			};
			LogAssert.IsTrue(timeline.TryApply(delta));
			LogAssert.AreEqual(5u, timeline.Revision);
			LogAssert.AreEqual(2, timeline.Layers.Count);
			LogAssert.AreEqual(0.9f, timeline.Layers[0].To, "an existing handle is edited in place");
			LogAssert.AreEqual(1, timeline.Cells.Count);
			LogAssert.AreEqual((ushort)2, timeline.Cells[0].ID);
			LogAssert.AreEqual(0.4f, timeline.Climate.ToTemperature);
		}

		[Test]
		public void ADuplicateDeltaIsHarmless()
		{
			WeatherTimeline timeline = TimelineAt(5);
			var stale = new WeatherDeltaBroadcast { SceneName = "Test", Revision = 5, RemovedLayers = new List<ushort> { 1 } };
			LogAssert.IsTrue(timeline.TryApply(stale), "an old delta needs no resync");
			LogAssert.AreEqual(1, timeline.Layers.Count, "an old delta changes nothing");
			LogAssert.AreEqual(5u, timeline.Revision);
		}

		[Test]
		public void AGapChangesNothingAndAsksForTheWholeTimeline()
		{
			WeatherTimeline timeline = TimelineAt(5);
			var skipped = new WeatherDeltaBroadcast { SceneName = "Test", Revision = 7, RemovedLayers = new List<ushort> { 1 }, HasCover = true, Cover = new WeatherCover { Snow = 1f } };
			LogAssert.IsTrue(timeline.IsGap(skipped));
			LogAssert.IsFalse(timeline.TryApply(skipped));
			LogAssert.AreEqual(5u, timeline.Revision);
			LogAssert.AreEqual(1, timeline.Layers.Count);
			LogAssert.AreEqual(0f, timeline.Cover.Snow);
		}

		[Test]
		public void AFullTimelineReplacesEverything()
		{
			WeatherTimeline source = TimelineAt(9);
			source.Seed = 42;
			source.SceneMode = WeatherSceneMode.Fixed;
			source.FixedPresetID = 77;
			source.FixedIntensity = 0.3f;
			source.Cover = new WeatherCover { Snow = 0.2f, Wet = 0.4f };
			source.CoverTick = 1234;

			WeatherTimeline target = TimelineAt(2);
			target.Layers.Add(new WeatherLayerEntry { Handle = 99 });
			target.Apply(source.ToBroadcast());

			LogAssert.AreEqual(9u, target.Revision);
			LogAssert.AreEqual(42u, target.Seed);
			LogAssert.AreEqual(WeatherSceneMode.Fixed, target.SceneMode);
			LogAssert.AreEqual(77, target.FixedPresetID);
			LogAssert.AreEqual(0.3f, target.FixedIntensity);
			LogAssert.AreEqual(1, target.Layers.Count);
			LogAssert.AreEqual(0.2f, target.Cover.Snow);
			LogAssert.AreEqual(1234u, target.CoverTick);

			WeatherTimelineBroadcast copy = source.ToBroadcast();
			copy.Layers.Clear();
			LogAssert.AreEqual(1, source.Layers.Count, "the broadcast must not share the timeline's lists");
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
				MotionTick = 300,
				BirthTick = 300,
				MatureTick = 600,
				DecayTick = 3000,
				DeathTick = 3300,
			};
		}

		[Test]
		public void ACellGrowsHoldsAndFades()
		{
			StormCell cell = Cell();
			LogAssert.AreEqual(0f, cell.EnvelopeAt(0));
			LogAssert.AreEqual(0f, cell.EnvelopeAt(300));
			Assert.That(cell.EnvelopeAt(450), Is.EqualTo(0.5f).Within(Tolerance));
			LogAssert.AreEqual(1f, cell.EnvelopeAt(600));
			LogAssert.AreEqual(1f, cell.EnvelopeAt(3000));
			Assert.That(cell.EnvelopeAt(3150), Is.EqualTo(0.5f).Within(Tolerance));
			LogAssert.AreEqual(0f, cell.EnvelopeAt(3300));
			LogAssert.IsTrue(cell.IsDead(3300));
			LogAssert.IsFalse(cell.IsDead(3299));

			float previous = -1f;
			for (uint t = 300; t <= 600; t++)
			{
				float value = cell.EnvelopeAt(t);
				LogAssert.IsTrue(value >= previous, $"growth must not dip (tick {t})");
				previous = value;
			}
		}

		[Test]
		public void ACellDriftsWithItsVelocity()
		{
			StormCell cell = Cell();
			Vector2 start = cell.CentreAt(300, TickDelta);
			LogAssert.AreEqual(new Vector2(100f, -50f), start);

			Vector2 later = cell.CentreAt(300 + 30 * 60, TickDelta);
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
				LogAssert.AreEqual(a.CentreAt(t, TickDelta), b.CentreAt(t, TickDelta));
				Vector2 straight = new Vector2(100f + 2f * (float)((t - 300) * TickDelta), -50f + (float)((t - 300) * TickDelta));
				LogAssert.IsTrue(Vector2.Distance(a.CentreAt(t, TickDelta), straight) <= 150f * Mathf.Sqrt(2f) + 0.01f,
					"the wander stays within its amplitude");
			}
		}

		[Test]
		public void ACellIsStrongestAtItsCentreAndAbsentBeyondItsRadius()
		{
			StormCell cell = Cell();
			const uint tick = 1000;
			Vector2 centre = cell.CentreAt(tick, TickDelta);
			var at = new Vector3(centre.x, 0f, centre.y);
			Assert.That(cell.InfluenceAt(at, tick, TickDelta), Is.EqualTo(0.8f).Within(Tolerance));
			Assert.That(cell.InfluenceAt(at + new Vector3(200f, 0f, 0f), tick, TickDelta), Is.EqualTo(0.8f).Within(Tolerance), "full strength inside 55% of the radius");
			float edge = cell.InfluenceAt(at + new Vector3(300f, 0f, 0f), tick, TickDelta);
			LogAssert.IsTrue(edge > 0f && edge < 0.8f, $"fading toward the edge, got {edge}");
			LogAssert.AreEqual(0f, cell.InfluenceAt(at + new Vector3(0f, 0f, 401f), tick, TickDelta));
			LogAssert.AreEqual(0f, cell.InfluenceAt(at, 3400, TickDelta), "a dead cell has no influence");
		}
	}
}
