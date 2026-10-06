using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Client;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// What falls on each world: nothing at all without air, what condenses there with it, and each
	/// kind drawn as what it is.
	/// </summary>
	/// <remarks>
	/// Rain and snow fell on Seli Waste, a scene on the airless, waterless moon Helis. The field
	/// itself was right — it has always returned a clear frame for no air — but the World Sim bed
	/// dressed into the generated scene started on the HOME world at 25°, because it loads no
	/// addressables, so the scene's atlas entry (its body, its place, its biome map) never resolved
	/// in play. These pin both halves: the real sampling path on an airless body, and the bed
	/// standing on the scene's own body.
	/// </remarks>
	[TestFixture]
	public class WorldPrecipitationTests
	{
		private readonly List<Object> created = new List<Object>();
		private readonly List<WeatherSubstance> cached = new List<WeatherSubstance>();

		[TearDown]
		public void TearDown()
		{
			foreach (WeatherSubstance substance in cached)
			{
				substance.RemoveFromCache();
			}
			cached.Clear();
			foreach (Object o in created)
			{
				if (o != null)
				{
					Object.DestroyImmediate(o);
				}
			}
			created.Clear();
			PlanetAir.ClearCache();
		}

		private WorldBody Body(AtmosphereKind atmosphere, float water)
		{
			// Round a star of its own, so whatever solar system the editor has loaded can place it.
			var star = ScriptableObject.CreateInstance<StarBody>();
			star.Luminosity = 1f;
			star.SkyRadiusKm = 696000f;
			created.Add(star);
			var body = ScriptableObject.CreateInstance<WorldBody>();
			body.name = "Test " + atmosphere + " " + created.Count;
			body.Parent = star;
			body.Orbit = new OrbitSettings { Distance = 1f, PeriodDays = 365f };
			body.Atmosphere = atmosphere;
			body.Water = water;
			body.SkyRadiusKm = 1800f;
			body.RotationHours = 24f;
			created.Add(body);
			return body;
		}

		private WeatherSubstance Substance(string name, WeatherCoverKind cover, bool condenses, Condensate condensate, bool frozen)
		{
			var substance = ScriptableObject.CreateInstance<WeatherSubstance>();
			substance.name = "WorldPrecipitationTests " + name;
			substance.Cover = cover;
			substance.Condenses = condenses;
			substance.Condensate = condensate;
			substance.Frozen = frozen;
			created.Add(substance);
			substance.AddToCache(substance.name);
			cached.Add(substance);
			return substance;
		}

		/// <summary>
		/// A timeline that asks for the worst weather it can: soaking, unstable, low-pressure air
		/// added on top, a thunderstorm standing on the point and an eruption beside it.
		/// </summary>
		private static WeatherTimeline Stormy(WorldBody body, uint tick)
		{
			var timeline = new WeatherTimeline
			{
				SceneName = "WorldPrecipitationTests",
				SceneMode = WeatherSceneMode.Own,
				TickDelta = 1.0 / 30.0,
				LatitudeDegrees = 30f,
				BodyOverride = body,
			};
			var wet = new AirOffsets { Humidity = 1f, Instability = 1f, Pressure = -1f };
			timeline.Air = new AirOffsetEntry { From = wet, To = wet, StartSeconds = 0, EndSeconds = 0 };
			timeline.Cells.Add(Cell(1, StormKind.Thunderstorm, timeline, tick));
			timeline.Cells.Add(Cell(2, StormKind.Eruption, timeline, tick));
			return timeline;
		}

		private static StormCell Cell(ushort id, StormKind kind, WeatherTimeline timeline, uint tick)
		{
			// Its life in the world seconds the timeline reads at those ticks.
			double At(long t) => timeline.WorldSecondsAt((double)t);
			return new StormCell
			{
				ID = id,
				Kind = kind,
				Shape = StormPhysics.ShapeOf(kind),
				RadiusMeters = 5000f,
				ExtentMeters = 5000f,
				PeakIntensity = 1f,
				BirthSeconds = At(0),
				MatureSeconds = At(1),
				DecaySeconds = At(tick + 100000L),
				DeathSeconds = At(tick + 200000L),
				MotionSeconds = At(tick),
			};
		}

		private static bool IsClear(in WeatherFrame frame)
		{
			for (int i = 0; i < WeatherChannels.Count; i++)
			{
				if (frame[i] != 0f)
				{
					return false;
				}
			}
			return true;
		}

		// ── No air: no weather at all ──────────────────────────────────

		[Test]
		public void AnAirlessWorld_HasNoWeather_WhateverTheAirOffsetsAndStormsAskFor()
		{
			const uint Tick = 3000;
			WorldBody helis = Body(AtmosphereKind.None, 0f);
			WeatherSample sample = WeatherField.Sample(Stormy(helis, Tick), null, default, Vector3.zero, Tick);

			LogAssert.IsTrue(IsClear(sample.Frame), "no rain, snow, ash, cloud, fog, wind or lightning where there is no air");
			LogAssert.IsTrue(IsClear(sample.Background), "and no background weather either");
			LogAssert.IsNull(sample.Substance, "nothing is falling, so nothing is falling as anything");
			LogAssert.IsFalse(sample.Planet.HasAir, "the sample says there is no air");
			LogAssert.IsFalse(StormPhysics.CanForm(sample.Planet), "so no storm of any kind can form or be spawned there");
			foreach (StormKind kind in StormPhysics.Kinds)
			{
				LogAssert.IsTrue(IsClear(StormPhysics.PeakFrame(kind, sample, out WeatherSubstance substance)), $"a {kind} makes nothing there");
				LogAssert.IsNull(substance, $"a {kind} drops nothing there");
			}
		}

		[Test]
		public void TheSameCall_OnAWorldWithAir_MakesWeather()
		{
			// The control: the test above is not vacuous — this exact call, with air, is not clear.
			const uint Tick = 3000;
			WorldBody wet = Body(AtmosphereKind.Standard, 0.7f);
			WeatherSample sample = WeatherField.Sample(Stormy(wet, Tick), null, default, Vector3.zero, Tick);

			LogAssert.IsTrue(sample.Planet.HasAir, "a world with air has air");
			LogAssert.IsTrue(StormPhysics.CanForm(sample.Planet), "and storms can form in it");
			LogAssert.IsFalse(IsClear(sample.Frame), "and the same timeline makes weather there");
		}

		[Test]
		public void NoStormFormsWithoutAirOrGravity()
		{
			LogAssert.IsFalse(StormPhysics.CanForm(default), "no air");
			PlanetAir earth = PlanetAir.Earthlike;
			LogAssert.IsTrue(StormPhysics.CanForm(earth), "our own air");
			earth.Gravity = 0f;
			LogAssert.IsFalse(StormPhysics.CanForm(earth), "air with nothing to settle it");
		}

		[Test]
		public void AStormAskedForByName_IsRefusedOnAnAirlessWorld_InTheGameAndInTheBed()
		{
			string host = CodeOnly(Read("Scripts/Server/Implementation/World/SceneServer/Weather/WeatherHost.cs"));
			string spawn = MethodBody(host, "public ushort SpawnCell(Scene scene, StormKind kind");
			LogAssert.IsTrue(spawn.Contains("StormPhysics.CanForm(sample.Planet)"), "WeatherHost.SpawnCell (the admin command and the ECA action) refuses where no storm can form");
			string bed = CodeOnly(Read("TestHarness/World/WorldSimController.cs"));
			LogAssert.IsTrue(MethodBody(bed, "public void SpawnCell(StormKind kind").Contains("StormPhysics.CanForm(here.Planet)"), "and so does the bed");
		}

		// ── The bed stands on the scene's own body ─────────────────────

		[Test]
		public void APlacedScene_StandsOnItsOwnBody_NotTheHomeWorld()
		{
			WorldBody helis = Body(AtmosphereKind.None, 0f);
			var entry = ScriptableObject.CreateInstance<WorldAtlasScene>();
			entry.SceneName = "WorldPrecipitationTests Waste";
			entry.Placed = true;
			entry.Body = helis;
			created.Add(entry);
			LogAssert.AreSame(helis, ScenePlacementClimate.ResolveBody(null, entry), "the entry's body");
		}

		[Test]
		public void TheWorldSimBed_StartsOnTheScenesBodyAndPlace_AndCachesItsAtlasEntry()
		{
			string bed = CodeOnly(Read("TestHarness/World/WorldSimController.cs"));
			string awake = MethodBody(bed, "private void Awake()");
			LogAssert.IsTrue(awake.Contains("ScenePlacementClimate.ResolveBody(SolarSystemProfile.Active, placed)"),
				"the bed starts on the scene's own body (home world only when the scene has none)");
			LogAssert.IsFalse(awake.Contains(".HomeWorld"), "never on the home world by default — that rained on an airless moon");
			LogAssert.IsTrue(awake.Contains("placed.EffectiveSunLatitude") && awake.Contains("placed.TimeLongitude"),
				"and at the scene's own place, which the climate, the sun and the wind belts all read");
			string cache = MethodBody(bed, "private void Cache()");
			LogAssert.IsTrue(cache.Contains("WorldAtlasScene.EditorLookup.Find(") && cache.Contains("entry.AddToCache("),
				"the atlas entry is cached as the game's loader would, or Settings.Body is null in play");
		}

		// ── What condenses ─────────────────────────────────────────────

		[Test]
		public void ACondensateBelowItsFreezingPoint_FallsAsSnow_AndAboveItAsRain_HailKept()
		{
			var frame = new WeatherFrame();
			frame[WeatherChannel.Precipitation] = 0.6f;
			frame[WeatherChannel.RainWeight] = 0.7f;
			frame[WeatherChannel.HailWeight] = 0.3f;

			WeatherFrame frozen = frame;
			WeatherPhysics.TypeCondensate(ref frozen, true);
			LogAssert.AreEqual(0f, frozen[WeatherChannel.RainWeight], "nothing liquid falls below the freezing point");
			LogAssert.AreEqual(0.7f, frozen[WeatherChannel.SnowWeight], "it all falls as snow");
			LogAssert.AreEqual(0.3f, frozen[WeatherChannel.HailWeight], "hail is the updraught's, not the ground's");

			WeatherFrame liquid = frozen;
			WeatherPhysics.TypeCondensate(ref liquid, false);
			LogAssert.AreEqual(0.7f, liquid[WeatherChannel.RainWeight], "above it, back to rain");
			LogAssert.AreEqual(0f, liquid[WeatherChannel.SnowWeight], "and no snow");
		}

		[Test]
		public void TheBackground_IsTypedByItsOwnCondensate_BeforeTheGroundJoinsIt()
		{
			string physics = CodeOnly(Read("Scripts/Shared/Implementation/Weather/WeatherPhysics.cs"));
			string frame = MethodBody(physics, "public static WeatherFrame Frame(");
			int typed = frame.IndexOf("TypeCondensate(ref weather, column.SurfaceKelvin < AirPhysics.FreezingKelvin(planet.Condensate))");
			int added = frame.IndexOf("accumulator.Add(weather, 1f, PrecipitateOf(planet, column.SurfaceKelvin))");
			LogAssert.IsTrue(typed >= 0 && added > typed,
				"a non-water condensate is typed by its own freezing point on the background alone — typing the whole frame would melt a cryovolcano's tephra");
			LogAssert.IsTrue(frame.Contains("if (planet.Condensate != Condensate.Water)"), "water keeps its scale-band typing");
		}

		[Test]
		public void WhatFalls_IsWhatCondensesOnThatWorld()
		{
			WeatherSubstance methane = Substance("Methane Drizzle", WeatherCoverKind.Wet, true, Condensate.Methane, false);
			WeatherSubstance nitrogen = Substance("Nitrogen Snow", WeatherCoverKind.Snow, true, Condensate.Nitrogen, true);
			Substance("Volcanic Ash", WeatherCoverKind.Ash, false, Condensate.Water, false);

			PlanetAir titan = PlanetAir.Earthlike;
			titan.Condensate = Condensate.Methane;
			WeatherSubstance onTitan = WeatherPhysics.PrecipitateOf(titan, 94f);
			LogAssert.IsTrue(onTitan != null && onTitan.Condenses && onTitan.Condensate == Condensate.Methane && !onTitan.Frozen,
				"Titan at 94 K rains liquid methane");

			PlanetAir triton = PlanetAir.Earthlike;
			triton.Condensate = Condensate.Nitrogen;
			WeatherSubstance onTriton = WeatherPhysics.PrecipitateOf(triton, 38f);
			LogAssert.IsTrue(onTriton != null && onTriton.Condensate == Condensate.Nitrogen && onTriton.Frozen,
				"a 38 K nitrogen world snows nitrogen");

			LogAssert.IsNull(WeatherPhysics.PrecipitateOf(PlanetAir.Earthlike, 280f), "water is the kinds' own default, typed by temperature");
			LogAssert.IsTrue(methane != null && nitrogen != null);
		}

		// ── Each kind drawn as what it is ──────────────────────────────

		[Test]
		public void ASubstance_FallsInTheChannelOfWhatItLeaves()
		{
			LogAssert.AreEqual(WeatherChannel.AshWeight, WeatherPhysics.ChannelOf(Substance("Ash", WeatherCoverKind.Ash, false, Condensate.Water, false)));
			LogAssert.AreEqual(WeatherChannel.SandWeight, WeatherPhysics.ChannelOf(Substance("Sand", WeatherCoverKind.Sand, false, Condensate.Water, false)));
			LogAssert.AreEqual(WeatherChannel.SnowWeight, WeatherPhysics.ChannelOf(Substance("Snow", WeatherCoverKind.Snow, true, Condensate.Nitrogen, true)));
			LogAssert.AreEqual(WeatherChannel.RainWeight, WeatherPhysics.ChannelOf(Substance("Drizzle", WeatherCoverKind.Wet, true, Condensate.Methane, false)));
			LogAssert.AreEqual(WeatherChannel.RainWeight, WeatherPhysics.ChannelOf(null), "water, or nothing known");
			WeatherSubstance ash = Substance("Ash 2", WeatherCoverKind.Ash, false, Condensate.Water, false);
			LogAssert.AreEqual(1f, WeatherPhysics.Falling(ash, 0.5f)[WeatherChannel.AshWeight], "Falling puts it in that channel");
		}

		[Test]
		public void ASubstance_DressesOnlyItsOwnKind()
		{
			WeatherSubstance ash = Substance("Ash", WeatherCoverKind.Ash, false, Condensate.Water, false);
			WeatherSubstance methane = Substance("Drizzle", WeatherCoverKind.Wet, true, Condensate.Methane, false);
			LogAssert.AreSame(ash, PrecipitationField.SubstanceOf(WeatherChannel.AshWeight, ash), "ash looks like ash");
			LogAssert.IsNull(PrecipitationField.SubstanceOf(WeatherChannel.RainWeight, ash), "rain beside a volcano is not grey");
			LogAssert.AreSame(methane, PrecipitationField.SubstanceOf(WeatherChannel.RainWeight, methane), "methane rain is methane's colour and pace");
			LogAssert.IsNull(PrecipitationField.SubstanceOf(WeatherChannel.SnowWeight, methane), "a liquid substance does not dress snow");
			LogAssert.IsNull(PrecipitationField.SubstanceOf(WeatherChannel.AshWeight, null));
		}

		[Test]
		public void OnlyRain_GrowsWithHowHardItFalls()
		{
			LogAssert.IsTrue(PrecipitationField.GrowthOf(WeatherChannel.RainWeight, 1f, 1f) > 4f, "a storm's drops are several times a drizzle's");
			foreach (WeatherChannel grain in new[] { WeatherChannel.AshWeight, WeatherChannel.SandWeight, WeatherChannel.SnowWeight, WeatherChannel.HailWeight })
			{
				LogAssert.AreEqual(1f, PrecipitationField.GrowthOf(grain, 1f, 1f), $"{grain} is the size it is, however hard it falls");
			}
		}

		[Test]
		public void AshAndSand_AreDrawnGrainSized_AndAshFallsSlowAndGrey()
		{
			// The ash sprite's blob fills ~0.44 of its quad, the grit's specks up to 0.16.
			PrecipitationLook ash = PrecipitationLook.Ash();
			LogAssert.IsTrue(ash.Size.y * 0.44f <= 0.005f, $"ash is drawn as mm flakes and aggregates, not {ash.Size.y * 0.44f * 100f:0.#} cm blobs");
			LogAssert.IsTrue(ash.FallSpeed.y <= 1f, "a 0.1 mm shard settles at well under a metre a second");
			LogAssert.IsTrue(Mathf.Abs(ash.Tint.r - ash.Tint.b) < 0.1f && ash.Tint.r < 0.7f, "grey");
			LogAssert.IsTrue(PrecipitationField.TraitsOf(WeatherChannel.AshWeight).Fine, "held up by the air's viscosity: it drifts");
			LogAssert.IsFalse(PrecipitationField.TraitsOf(WeatherChannel.AshWeight).Clear, "an opaque grain");
			PrecipitationLook sand = PrecipitationLook.Sand();
			LogAssert.IsTrue(sand.Size.y * 0.16f <= 0.006f, "sand is grit, not pebbles");

			// And the shipped profile, which is what is actually drawn.
			string profile = File.ReadAllText(Path.Combine(Application.dataPath, "Prefabs/Client/Weather/Weather Render Profile.asset")).Replace("\r\n", "\n");
			LogAssert.IsTrue(LookSize(profile, "Ash").y * 0.44f <= 0.005f, "the profile's ash is grain-sized");
			LogAssert.IsTrue(LookSize(profile, "Sand").y * 0.16f <= 0.006f, "the profile's sand is grain-sized");
		}

		// ── Helpers ────────────────────────────────────────────────────

		private static Vector2 LookSize(string yaml, string look)
		{
			Match m = Regex.Match(yaml, "\n  " + look + ":\n    AtlasRow: \\d+\n    Size: \\{x: ([0-9.eE-]+), y: ([0-9.eE-]+)\\}");
			LogAssert.IsTrue(m.Success, $"the profile has a {look} look");
			return new Vector2(float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
				float.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture));
		}

		private static string Read(string relative)
		{
			return File.ReadAllText(Path.Combine(Application.dataPath, relative)).Replace("\r\n", "\n");
		}

		private static string CodeOnly(string source)
		{
			var lines = new List<string>();
			foreach (string line in source.Split('\n'))
			{
				string t = line.TrimStart();
				if (t.StartsWith("//") || t.StartsWith("/*") || t.StartsWith("*"))
				{
					continue;
				}
				lines.Add(line);
			}
			return string.Join("\n", lines);
		}

		/// <summary>The brace-matched body after the first <c>{</c> following the signature; empty when the signature is not there.</summary>
		private static string MethodBody(string source, string signature)
		{
			int at = source.IndexOf(signature);
			LogAssert.IsTrue(at >= 0, $"found {signature}");
			int open = source.IndexOf('{', at);
			int depth = 0;
			for (int i = open; i < source.Length; i++)
			{
				if (source[i] == '{')
				{
					depth++;
				}
				else if (source[i] == '}' && --depth == 0)
				{
					return source.Substring(open, i - open + 1);
				}
			}
			return string.Empty;
		}
	}
}
