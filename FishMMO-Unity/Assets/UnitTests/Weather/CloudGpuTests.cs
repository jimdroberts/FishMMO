using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Client;
using FishMMO.Shared.Weather;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// What moving the clouds' work onto the GPU must not change: the rebuilt buffer's resolution on
	/// each tier, the storms the weather-map kernel is handed and what it makes of them, and the pace
	/// the air map is rebuilt at.
	/// </summary>
	/// <remarks>
	/// The kernels themselves cannot run here. What can be held is everything either side of them: the
	/// packed storms carry everything the reference raster reads (<see cref="WeatherMap.RasterPacked"/>,
	/// the kernel's twin, agrees with <see cref="WeatherMap.SampleTexel"/> over each storm), a storm is
	/// never dropped that could have drawn anything, and the tiers' numbers keep what Jim judged.
	/// </remarks>
	[TestFixture]
	public class CloudGpuTests
	{
		private const double TickDelta = 1.0 / 30.0;

		/// <summary>Stormy air: humid, unstable, in a weak low, the wind from the south-west.</summary>
		private static WeatherSample StormyAir()
		{
			PlanetAir planet = PlanetAir.Earthlike;
			var air = new WeatherDriver.Synoptic { Humidity = 0.75f, Pressure = -0.4f, Instability = 0.5f, Wind = new Vector2(4f, 3f), LocalTime01 = 0.6f };
			AirColumn column = AirColumn.Of(planet, 293f, air.Humidity, air.Pressure, air.Instability);
			return new WeatherSample { Planet = planet, OpenAir = air, Air = air, OpenColumn = column, Column = column, Temperature = 0.6f };
		}

		private static StormCell Cell(ushort id, StormKind kind, Vector2 at, float radius, float extent, Vector2 velocity)
		{
			return new StormCell
			{
				ID = id, Kind = kind, Shape = StormPhysics.ShapeOf(kind), OriginX = at.x, OriginZ = at.y,
				VelocityX = velocity.x, VelocityZ = velocity.y, MotionSeconds = 10 * TickDelta,
				RadiusMeters = radius, ExtentMeters = extent, PeakIntensity = 1f,
				BirthSeconds = 0 * TickDelta, MatureSeconds = 0 * TickDelta, DecaySeconds = 1000000 * TickDelta, DeathSeconds = 1000001 * TickDelta,
			};
		}

		private static WeatherTimeline Timeline(params StormCell[] cells)
		{
			var timeline = new WeatherTimeline { SceneMode = WeatherSceneMode.Own, TickDelta = TickDelta, LatitudeDegrees = 35f };
			timeline.Cells.AddRange(cells);
			return timeline;
		}

		private static Rect Square(Vector2 centre, float size) => new Rect(centre.x - 0.5f * size, centre.y - 0.5f * size, size, size);

		// ── The rebuilt buffer's resolution ─────────────────────────────────

		[Test]
		public void TheRebuiltCloudsAreNeverCoarserThanTheMarchNorFinerThanTheScreen()
		{
			for (float march = 0.15f; march <= 1.001f; march += 0.05f)
			{
				float auto = CloudTierSettings.HistoryScaleFor(0f, march);
				LogAssert.IsTrue(auto >= march - 1e-6f && auto <= 1f, $"auto at a march of {march:0.00}: {auto:0.000}");
				LogAssert.IsTrue(auto / march <= CloudTierSettings.AutoPixelsPerTexel + 1e-4f,
					$"no more than {CloudTierSettings.AutoPixelsPerTexel} pixels a texel across, or a pixel waits more than {CloudTierSettings.AutoPixelsPerTexel * CloudTierSettings.AutoPixelsPerTexel:0.##} frames for a sample: {auto / march:0.00} at {march:0.00}");
				// Asked for too little or too much, it is held to the march and the screen.
				LogAssert.IsTrue(Mathf.Approximately(CloudTierSettings.HistoryScaleFor(march * 0.5f, march), march), "never coarser than the march");
				LogAssert.IsTrue(Mathf.Approximately(CloudTierSettings.HistoryScaleFor(3f, march), 1f), "never finer than the screen");
			}
			float previous = 0f;
			for (float march = 0.15f; march <= 1.001f; march += 0.01f)
			{
				float auto = CloudTierSettings.HistoryScaleFor(0f, march);
				LogAssert.IsTrue(auto >= previous - 1e-6f, "a finer march is never rebuilt coarser");
				previous = auto;
			}
		}

		[Test]
		public void EveryTierRebuildsAtFourTimesItsMarchAndTheCompositeScalesItUp()
		{
			// 2026-10-07: the marches came down to about one ray in 200-400 pixels a frame (shipped skies march
			// about one in 256), 10.7 ms of clouds becoming about 2 at 2560x1440. Each rebuilds at four times its
			// march, every pixel re-marched once in sixteen frames, and the composite scales that up by depth.
			foreach (WeatherTierSettings tier in new[] { WeatherTierSettings.Performant(), WeatherTierSettings.Balanced(), WeatherTierSettings.High() })
			{
				float rebuilt = CloudTierSettings.HistoryScaleFor(tier.CloudHistoryScale, tier.CloudResolution);
				LogAssert.IsTrue(Mathf.Abs(rebuilt - 4f * tier.CloudResolution) < 1e-5f, $"rebuilt at four times a march of {tier.CloudResolution}: {rebuilt:0.000}");
				LogAssert.IsTrue(rebuilt < 0.5f, $"below the screen's size, so the composite's upscale is what draws it: {rebuilt:0.000}");
				LogAssert.IsTrue(tier.CloudResolution * tier.CloudResolution < 1f / 150f, $"a ray for every 150 pixels or more: {1f / (tier.CloudResolution * tier.CloudResolution):0}");
			}
		}

		[Test]
		public void AProfileSavedBeforeTheFieldExistedGetsTheSameNumbersAsTheDefaults()
		{
			// Unity fills a field a saved asset does not have with its initializer: 0, the renderer's
			// own choice. That choice must be what each tier's defaults say explicitly, or the shipped
			// profile and a fresh one would draw different skies.
			LogAssert.AreEqual(0f, new WeatherTierSettings().CloudHistoryScale, "absent from an asset, the field is 0");
			foreach (WeatherTierSettings tier in new[] { WeatherTierSettings.Performant(), WeatherTierSettings.Balanced(), WeatherTierSettings.High() })
			{
				float saved = CloudTierSettings.HistoryScaleFor(0f, tier.CloudResolution);
				float fresh = CloudTierSettings.HistoryScaleFor(tier.CloudHistoryScale, tier.CloudResolution);
				LogAssert.IsTrue(Mathf.Abs(saved - fresh) < 1e-5f, $"at a march of {tier.CloudResolution}: {saved:0.000} from an old asset, {fresh:0.000} from the defaults");
			}
			var settings = new CloudTierSettings { Resolution = 0.4f, HistoryScale = 0f };
			LogAssert.AreEqual(1f, settings.EffectiveHistoryScale, "the tier struct answers the same");
		}

		[Test]
		public void TheCamerasHeightIsTheSameAtPlanetaryRadiusToTheCentimetre()
		{
			// The bullseye this project paid for: |p − c| − R in single precision at 6371 km steps in
			// half metres. The form the resolve's skip uses must resolve a centimetre.
			const double earth = 6371000.0;
			double a = FishCloudsFeature.AltitudeOver(new Vector3(0f, 0.74f, 0f), earth);
			double b = FishCloudsFeature.AltitudeOver(new Vector3(0f, 0.75f, 0f), earth);
			LogAssert.IsTrue(System.Math.Abs(a - 0.74) < 1e-4 && System.Math.Abs(b - 0.75) < 1e-4, $"0.74 m and 0.75 m read {a:0.0000} and {b:0.0000}");
			// Over a curved world the sea falls away from a flat plane: a point at y = 0 ten kilometres
			// off stands above the sea there by the sagitta, d²/2R.
			double far = FishCloudsFeature.AltitudeOver(new Vector3(10000f, 0f, 0f), earth);
			double sagitta = 10000.0 * 10000.0 / (2.0 * earth);
			LogAssert.IsTrue(System.Math.Abs(far - sagitta) < 0.01, $"10 km out at y = 0 stands {far:0.000} m above the sea (the sagitta is {sagitta:0.000} m)");
		}

		// ── The storms the kernel is handed ─────────────────────────────────

		[Test]
		public void AStormIsSentToAMapByItsFootprintNotItsCentre()
		{
			Rect map = Square(Vector2.zero, WeatherMap.DefaultSizeMeters);
			float half = 0.5f * WeatherMap.DefaultSizeMeters;
			LogAssert.IsTrue(WeatherMap.Overlaps(Vector2.zero, 10f, map), "a storm in the middle");
			LogAssert.IsTrue(WeatherMap.Overlaps(new Vector2(half + 900f, 0f), 1000f, map), "a centre off the map, a reach that is on it");
			LogAssert.IsFalse(WeatherMap.Overlaps(new Vector2(half + 1100f, 0f), 1000f, map), "a reach that stops short");
			// Off a corner, what matters is the distance to the corner, not to either edge.
			var diagonal = new Vector2(half + 3000f, half + 3000f);
			float toCorner = Mathf.Sqrt(2f) * 3000f;
			LogAssert.IsFalse(WeatherMap.Overlaps(diagonal, toCorner - 10f, map), "just short of the corner");
			LogAssert.IsTrue(WeatherMap.Overlaps(diagonal, toCorner + 10f, map), "just over it");
			// A tropical cyclone at its real size: the eye 200 km off, the rain bands 300 km out. Its
			// centre is on neither map; it covers both, and no radius is clamped.
			LogAssert.IsTrue(WeatherMap.Overlaps(new Vector2(200000f, 0f), 300000f, map), "a hurricane centred 200 km off reaches the fine map");
			LogAssert.IsTrue(WeatherMap.Overlaps(new Vector2(200000f, 0f), 300000f, Square(Vector2.zero, WeatherMap.FarSizeMeters)), "and the coarse one");
			LogAssert.IsFalse(WeatherMap.Overlaps(Vector2.zero, 0f, map), "nothing reaches nowhere");
			LogAssert.IsFalse(WeatherMap.Overlaps(Vector2.zero, float.NaN, map), "a bad reach draws nothing");
		}

		[Test]
		public void AStormThatIsNotSentPutsNothingOnTheMap()
		{
			// Culling may only drop what would have drawn nothing: every texel of a map a storm is not
			// sent to must be empty of it in the reference raster. Walked outward until it is no longer
			// sent, so the test holds whatever size the storm's anvil comes out at.
			WeatherSample air = StormyAir();
			var storms = new StormFrames(air);
			Rect map = Square(Vector2.zero, WeatherMap.DefaultSizeMeters);
			var packed = new List<Vector4>();
			Vector2 away = new Vector2(3f, -2f).normalized;
			bool sentNear = false, droppedFar = false;
			foreach (float distance in new[] { 5000f, 20000f, 40000f, 80000f, 160000f, 320000f, 640000f })
			{
				WeatherTimeline timeline = Timeline(Cell(1, StormKind.Thunderstorm, away * distance, 1500f, 0f, Vector2.zero));
				int sent = WeatherMap.PackFor(timeline, storms, 10, map, packed);
				LogAssert.AreEqual(sent * WeatherMap.StormFloat4s, packed.Count, "packed whole or not at all");
				if (sent > 0)
				{
					sentNear |= distance <= 5000f;
					continue;
				}
				droppedFar |= distance >= 640000f;
				for (int y = 0; y <= 8; y++)
				{
					for (int x = 0; x <= 8; x++)
					{
						var p = new Vector3(map.xMin + map.width * x / 8f, 0f, map.yMin + map.height * y / 8f);
						WeatherMap.Texel texel = WeatherMap.SampleTexel(timeline, storms, p, 10);
						LogAssert.IsTrue(texel.Cover == 0f && texel.Anvil == 0f && texel.Clearing == 0f,
							$"a storm {distance / 1000f:0} km off is not sent, and draws nothing there: {texel.Cover}/{texel.Anvil} at {p}");
					}
				}
			}
			LogAssert.IsTrue(sentNear, "a storm 5 km off is sent to the fine map");
			LogAssert.IsTrue(droppedFar, "and one 640 km off is not");
		}

		[Test]
		public void APackedStormSaysWhatKindAndShapeItIs()
		{
			WeatherSample air = StormyAir();
			var storms = new StormFrames(air);
			Rect map = Square(Vector2.zero, WeatherMap.FarSizeMeters);
			var packed = new List<Vector4>();
			var motion = new Vector2(8f, 4f);
			foreach (StormKind kind in new[] { StormKind.Thunderstorm, StormKind.Supercell, StormKind.SquallLine, StormKind.TropicalCyclone, StormKind.Haboob, StormKind.DustDevil })
			{
				WeatherTimeline timeline = Timeline(Cell(1, kind, Vector2.zero, kind == StormKind.TropicalCyclone ? 20000f : 1500f, kind == StormKind.TropicalCyclone ? 3000f : 400f, motion));
				if (WeatherMap.PackFor(timeline, storms, 10, map, packed) == 0)
				{
					continue;
				}
				int flags = Mathf.RoundToInt(packed[0].w);
				LogAssert.AreEqual((int)StormPhysics.ShapeOf(kind), (flags >> 1) & 7, $"{kind}'s shape");
				LogAssert.AreEqual(kind == StormKind.Supercell, (flags & 16) != 0, $"{kind}: only a supercell's updraught stands over its cell");
				bool anatomy = StormAnatomy.Of(kind, air, motion, timeline.LatitudeDegrees, timeline.Cells[0].RadiusMeters).Valid;
				LogAssert.AreEqual(anatomy, (flags & 1) != 0, $"{kind}: whether it has cloud of its own");
				LogAssert.IsTrue(packed[0].z >= timeline.Cells[0].ReachMeters, $"{kind}: its reach is at least its cell's ({packed[0].z:0} m)");
			}
		}

		[Test]
		public void TheKernelsArithmeticIsTheReferenceRastersForEveryKindOfStorm()
		{
			// RasterPacked reads only what the kernel is handed and does what the kernel does. Held to
			// SampleTexel — the CPU raster's own Contribute — over each storm's whole footprint, it proves
			// the packing carries everything that decides a texel and the arithmetic has not drifted.
			WeatherSample air = StormyAir();
			var storms = new StormFrames(air);
			var motion = new Vector2(9f, 3f);
			StormPhysics.Dimensions(StormKind.SquallLine, air.OpenColumn, 0.5f, 0.5f, out float lineDepth, out float lineHalf, out _);
			var cases = new[]
			{
				Cell(1, StormKind.Thunderstorm, Vector2.zero, 1500f, 0f, Vector2.zero),
				Cell(2, StormKind.Supercell, Vector2.zero, 150f, 400f, motion),
				Cell(3, StormKind.SquallLine, Vector2.zero, lineDepth, lineHalf, motion),
				Cell(4, StormKind.TropicalCyclone, Vector2.zero, 20000f, 3000f, motion),
				Cell(5, StormKind.Haboob, Vector2.zero, 3000f, 8000f, motion),
				Cell(6, StormKind.DustDevil, Vector2.zero, 40f, 300f, motion),
			};
			var packed = new List<Vector4>();
			int compared = 0, cloudy = 0;
			foreach (StormCell cell in cases)
			{
				WeatherTimeline timeline = Timeline(cell);
				float reach = Mathf.Max(cell.ReachMeters, 30000f);
				if (WeatherMap.PackFor(timeline, storms, 10, Square(Vector2.zero, 4f * reach), packed) == 0)
				{
					continue;
				}
				float extent = packed[0].z;
				for (int y = -20; y <= 20; y++)
				{
					for (int x = -20; x <= 20; x++)
					{
						var p = new Vector2(extent * x / 20f, extent * y / 20f);
						WeatherMap.Texel reference = WeatherMap.SampleTexel(timeline, storms, new Vector3(p.x, 0f, p.y), 10);
						WeatherMap.Texel twin = WeatherMap.RasterPacked(packed, p, 0f);
						Compare(reference.A, twin.A, 1e-4f, $"{cell.Kind} A at {p}");
						Compare(reference.B, twin.B, 0.05f, $"{cell.Kind} B at {p}");
						compared++;
						if (reference.Cover > 0.5f || reference.Anvil > 0.5f)
						{
							cloudy++;
						}
					}
				}
			}
			LogAssert.IsTrue(compared > 3000, $"compared across the storms' footprints: {compared} points");
			LogAssert.IsTrue(cloudy > 200, $"and a good share of them under a storm's own cloud or anvil, not empty air: {cloudy}");
		}

		[Test]
		public void OverlappingStormsAreBlendedAsTheReferenceBlendsThem()
		{
			WeatherSample air = StormyAir();
			var storms = new StormFrames(air);
			WeatherTimeline timeline = Timeline(
				Cell(1, StormKind.Thunderstorm, new Vector2(-1500f, 0f), 1500f, 0f, Vector2.zero),
				Cell(2, StormKind.Thunderstorm, new Vector2(1500f, 400f), 1800f, 0f, Vector2.zero),
				Cell(3, StormKind.TropicalCyclone, new Vector2(40000f, -20000f), 60000f, 12000f, new Vector2(5f, 2f)));
			var packed = new List<Vector4>();
			Rect map = Square(Vector2.zero, WeatherMap.DefaultSizeMeters);
			LogAssert.AreEqual(3, WeatherMap.PackFor(timeline, storms, 10, map, packed), "all three reach the fine map, the hurricane from 45 km off");
			float texel = WeatherMap.DefaultSizeMeters / WeatherMap.DefaultResolution;
			for (int y = 0; y < 32; y++)
			{
				for (int x = 0; x < 32; x++)
				{
					var p = new Vector2(map.xMin + (x * 4 + 0.5f) * texel, map.yMin + (y * 4 + 0.5f) * texel);
					WeatherMap.Texel reference = WeatherMap.SampleTexel(timeline, storms, new Vector3(p.x, 0f, p.y), 10);
					WeatherMap.Texel twin = WeatherMap.RasterPacked(packed, p, 0f);
					Compare(reference.A, twin.A, 1e-4f, $"A at {p}");
					Compare(reference.B, twin.B, 0.05f, $"B at {p}");
				}
			}
		}

		private static void Compare(Color reference, Color twin, float absolute, string what)
		{
			for (int c = 0; c < 4; c++)
			{
				float tolerance = absolute + 1e-4f * Mathf.Abs(reference[c]);
				LogAssert.IsTrue(Mathf.Abs(reference[c] - twin[c]) <= tolerance, $"{what}[{c}]: reference {reference[c]}, kernel's twin {twin[c]}");
			}
		}

		// ── The air map's pace ──────────────────────────────────────────────

		[Test]
		public void TheAirMapIsBuiltAtThePaceTheAirChanges()
		{
			int total = CloudAirMap.Resolution * CloudAirMap.Resolution;
			const float frame = 1f / 60f;
			// The game's clock, four world seconds to one: a few texels a frame, not ninety-six.
			int game = CloudAirMap.SliceFor(4.0 * frame, frame);
			LogAssert.IsTrue(game >= 1 && game <= CloudAirMap.TexelsPerFrame / 8, $"at 4x a frame probes {game} texels");
			LogAssert.IsTrue(game * (CloudAirMap.RealSecondsPerMap / frame) >= total, "and still finishes a map within the real-time bound");
			// The World Sim bed's clock, 180 to one: as fast as it ever was.
			LogAssert.AreEqual(CloudAirMap.TexelsPerFrame, CloudAirMap.SliceFor(180.0 * frame, frame), "at 180x it probes as many as it ever did");
			// A paused clock still lets an offset through, within the real-time bound.
			int paused = CloudAirMap.SliceFor(0.0, frame);
			LogAssert.IsTrue(paused >= 1 && paused * (CloudAirMap.RealSecondsPerMap / frame) >= total, $"paused, {paused} a frame still finishes a map in {CloudAirMap.RealSecondsPerMap} s");
			// Faster clocks never probe fewer.
			int previous = 0;
			for (double rate = 0.0; rate <= 400.0; rate += 5.0)
			{
				int slice = CloudAirMap.SliceFor(rate * frame, frame);
				LogAssert.IsTrue(slice >= previous && slice <= CloudAirMap.TexelsPerFrame, $"at {rate}x: {slice}");
				previous = slice;
			}
			// A clock set backwards is a change of the same size; nonsense is the least there is.
			LogAssert.AreEqual(CloudAirMap.SliceFor(3.0, frame), CloudAirMap.SliceFor(-3.0, frame), "backwards as forwards");
			LogAssert.IsTrue(CloudAirMap.SliceFor(double.NaN, float.NaN) >= 1, "a bad frame still probes one");
		}
	}
}
