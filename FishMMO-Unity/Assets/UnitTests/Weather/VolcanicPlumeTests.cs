using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Client;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// Volcanic plumes: the buoyant column and umbrella ash falls out of where there is air, the
	/// ballistic fountain where there is none, where vents stand, and that the ash the weather drops
	/// is the drawn plume's.
	/// </summary>
	[TestFixture]
	public class VolcanicPlumeTests
	{
		private const float G = SurfacePhysics.EarthGravity;
		private readonly List<Object> created = new List<Object>();

		[TearDown]
		public void TearDown()
		{
			foreach (Object o in created)
			{
				if (o != null)
				{
					Object.DestroyImmediate(o);
				}
			}
			created.Clear();
		}

		private WeatherSubstance Ash(bool vented = true)
		{
			var ash = ScriptableObject.CreateInstance<WeatherSubstance>();
			ash.name = "VolcanicPlumeTests Ash";
			ash.Cover = WeatherCoverKind.Ash;
			ash.Vented = vented;
			ash.GrainMetres = 1e-4f;
			ash.GrainDensity = 2400f;
			created.Add(ash);
			return ash;
		}

		private BiomeTemplate Biome(WeatherSubstance emits, float rate)
		{
			var biome = ScriptableObject.CreateInstance<BiomeTemplate>();
			biome.Emits = emits;
			biome.EmissionRate = rate;
			created.Add(biome);
			return biome;
		}

		// ── The buoyant column ─────────────────────────────────────────

		[Test]
		public void TheColumnRises_WithTheFourthRootOfTheEruptionRate()
		{
			float earth = VolcanicPlume.TopMetres(1e6f, G, 1f);
			LogAssert.IsTrue(Mathf.Abs(earth - 7463f) < 20f, $"Wilson and Walker: 0.236 km × Q^¼, 7.46 km at 10⁶ kg/s (was {earth:0})");
			LogAssert.IsTrue(Mathf.Abs(VolcanicPlume.TopMetres(16e6f, G, 1f) / earth - 2f) < 1e-3f, "sixteen times the rate, twice the height");
			LogAssert.IsTrue(Mathf.Abs(VolcanicPlume.TopMetres(1e6f, G / 4f, 1f) / earth - 2f) < 1e-3f, "a quarter the gravity, twice the height (H ∝ g^−½)");
			LogAssert.IsTrue(VolcanicPlume.TopMetres(1e6f, G, 0.1f) > earth, "thinner air, a taller column");
			LogAssert.AreEqual(1000f, Mathf.Round(VolcanicPlume.MassRate(0f)), "a degassing vent: 10³ kg/s");
			LogAssert.IsTrue(VolcanicPlume.MassRate(1f) > 3e7f && VolcanicPlume.MassRate(0.5f) > VolcanicPlume.MassRate(0.2f), "a sub-Plinian eruption at full strength, rising with the emission");
		}

		[Test]
		public void TheColumnLeansDownwind_AndTheUmbrellaSpreadsAtNeutralBuoyancy()
		{
			PlanetAir air = PlanetAir.Earthlike;
			VolcanicPlume.Plume calm = VolcanicPlume.Of(Vector2.zero, 0.5f, null, air, Vector2.zero);
			LogAssert.IsTrue(calm.Valid, "a plume where there is air");
			LogAssert.AreEqual(0.75f * calm.Top, calm.Umbrella, "the umbrella at three quarters of the top (Sparks)");
			LogAssert.IsTrue(calm.TopOffset == Vector2.zero, "upright in still air");

			VolcanicPlume.Plume breezy = VolcanicPlume.Of(Vector2.zero, 0.5f, null, air, new Vector2(5f, 0f));
			VolcanicPlume.Plume windy = VolcanicPlume.Of(Vector2.zero, 0.5f, null, air, new Vector2(20f, 0f));
			LogAssert.IsTrue(breezy.TopOffset.x > 0f && Mathf.Abs(breezy.TopOffset.y) < 1e-3f, "leaning downwind");
			LogAssert.IsTrue(windy.TopOffset.x > breezy.TopOffset.x, "further in a stronger wind");
			LogAssert.IsTrue(windy.TopOffset.magnitude <= 2f * windy.Top + 1e-3f, "bent over, never laid flat past twice its height");
		}

		[Test]
		public void AshFallsBeneathAndDownwindOfTheUmbrella_NotUpwind()
		{
			VolcanicPlume.Plume plume = VolcanicPlume.Of(Vector2.zero, 0.5f, null, PlanetAir.Earthlike, new Vector2(8f, 0f));
			Vector2 underTop = plume.TopOffset;
			float under = VolcanicPlume.FalloutAt(plume, underTop);
			float downwind = VolcanicPlume.FalloutAt(plume, underTop + new Vector2(5000f, 0f));
			float further = VolcanicPlume.FalloutAt(plume, underTop + new Vector2(50000f, 0f));
			float upwind = VolcanicPlume.FalloutAt(plume, underTop - new Vector2(4f * plume.Radius, 0f));
			float aside = VolcanicPlume.FalloutAt(plume, underTop + new Vector2(5000f, 6f * (plume.Radius + 1000f)));
			LogAssert.IsTrue(under > 0.3f, $"heavy under the umbrella ({under:0.###})");
			LogAssert.IsTrue(downwind > 0f && downwind <= under, "falling downwind");
			LogAssert.IsTrue(further < downwind, "thinning exponentially further down");
			LogAssert.IsTrue(upwind < 0.01f, "none upwind past the umbrella's edge");
			LogAssert.IsTrue(aside < downwind * 0.05f, "and none well off to the side");

			float length = VolcanicPlume.FalloutLength(plume);
			float expected = 8f * VolcanicPlume.WindAloft * plume.Umbrella / plume.FallSpeed;
			LogAssert.IsTrue(Mathf.Abs(length - Mathf.Max(plume.Radius, expected)) < 1f, "thinning over a grain's drift while it falls from the umbrella");

			VolcanicPlume.Plume still = VolcanicPlume.Of(Vector2.zero, 0.5f, null, PlanetAir.Earthlike, Vector2.zero);
			LogAssert.IsTrue(Mathf.Abs(VolcanicPlume.FalloutAt(still, new Vector2(300f, 0f)) - VolcanicPlume.FalloutAt(still, new Vector2(0f, -300f))) < 1e-5f,
				"in still air, a disc round the vent");
		}

		[Test]
		public void AGrainSettles_AtItsStokesSpeed_SlowerOnALighterWorld()
		{
			float ash = VolcanicPlume.GrainFallSpeed(1e-4f, 2400f, G, SurfacePhysics.EarthAirDensity);
			LogAssert.IsTrue(Mathf.Abs(ash - 0.727f) < 0.01f, $"a tenth-millimetre shard settles at ~0.73 m/s (was {ash:0.###})");
			LogAssert.IsTrue(VolcanicPlume.GrainFallSpeed(1e-4f, 2400f, 1.9f, SurfacePhysics.EarthAirDensity) < ash, "slower under weaker gravity");
			float lapillus = VolcanicPlume.GrainFallSpeed(1e-3f, 2400f, G, SurfacePhysics.EarthAirDensity);
			LogAssert.IsTrue(lapillus > 5f && lapillus < 10f, $"a millimetre grain is drag-limited at ~7.7 m/s, not Stokes' 73 (was {lapillus:0.#})");
		}

		// ── Air gates the plume; no air makes a fountain ───────────────

		[Test]
		public void NoAir_NoBuoyantPlume_NoAshWeather_AFountainInstead()
		{
			PlanetAir none = default;
			LogAssert.IsFalse(VolcanicPlume.CanRise(none), "nothing to be buoyant in");
			LogAssert.IsFalse(VolcanicPlume.Of(Vector2.zero, 1f, null, none, new Vector2(10f, 0f)).Valid, "so no column");
			LogAssert.AreEqual(0f, VolcanicPlume.FalloutAt(default, Vector2.zero), "and no fallout from one");
			var plumes = new List<VolcanicPlume.Plume>();
			VolcanicVents.Plumes(null, null, 0, none, Vector2.right, Vector2.zero, plumes);
			LogAssert.AreEqual(0, plumes.Count, "no plumes at all over an airless world");
			LogAssert.IsTrue(VolcanicPlume.IsFountain(none, 1.9f), "a vent there is a fountain");
			LogAssert.IsFalse(VolcanicPlume.IsFountain(PlanetAir.Earthlike, G), "and not where there is air");

			// The weather frame: the plumes' fallout falls only with air.
			PlanetAir earth = PlanetAir.Earthlike;
			AirColumn column = AirColumn.Of(earth, 288f, 0.3f, 0.5f, 0.2f);
			var air = new WeatherDriver.Synoptic { Humidity = 0.3f, Pressure = 0.5f, Instability = 0.2f };
			WeatherFrame withAir = WeatherPhysics.Frame(air, column, earth, default, 0.3f, 1f, 0.5f, Ash(), out _);
			LogAssert.IsTrue(withAir[WeatherChannel.AshWeight] > 0f && withAir[WeatherChannel.Precipitation] > 0f, "ash falls out of a plume overhead");
			PlanetAir airless = earth;
			airless.HasAir = false;
			WeatherFrame without = WeatherPhysics.Frame(air, column, airless, default, 0.3f, 1f, 0.5f, Ash(), out _);
			LogAssert.AreEqual(0f, without[WeatherChannel.AshWeight], "and never without air to hold it up");
		}

		[Test]
		public void AVentedGround_DropsNothingWhereItStands_ItsAshFallsFromThePlume()
		{
			PlanetAir earth = PlanetAir.Earthlike;
			AirColumn column = AirColumn.Of(earth, 288f, 0.3f, 0.5f, 0.2f);
			var air = new WeatherDriver.Synoptic { Humidity = 0.3f, Pressure = 0.5f, Instability = 0.2f };
			GroundTraits vented = GroundTraits.Of(Biome(Ash(true), 0.15f), false);
			GroundTraits diffuse = GroundTraits.Of(Biome(Ash(false), 0.15f), false);
			LogAssert.IsTrue(vented.Vented && !diffuse.Vented, "the substance says which");
			LogAssert.AreEqual(0f, WeatherPhysics.Frame(air, column, earth, vented, 0.3f, 1f, out _)[WeatherChannel.AshWeight], "a vent's ash does not fall everywhere over its biome");
			LogAssert.IsTrue(WeatherPhysics.Frame(air, column, earth, diffuse, 0.3f, 1f, out _)[WeatherChannel.AshWeight] > 0f, "a diffuse haze still settles where it is made");
			LogAssert.IsTrue(WeatherPhysics.FallingFrom(null, 0.4f)[WeatherChannel.AshWeight] > 0f, "a plume of nothing in particular drops plain ash");
		}

		// ── The fountain ───────────────────────────────────────────────

		[Test]
		public void AFountainClimbsVSquaredOverTwoG_AndLandsInARing()
		{
			// Io: g 1.8, Prometheus-class half a kilometre a second — some 70 km up.
			float io = VolcanicPlume.FountainHeight(500f, 1.8f);
			LogAssert.IsTrue(io > 60000f && io < 120000f, $"a Prometheus-class plume stands ~70–100 km on Io (was {io:0})");
			LogAssert.IsTrue(Mathf.Abs(VolcanicPlume.FountainHeight(100f, 2f) - 2500f) < 1e-2f, "v²/2g");

			float v = 60f, g = 1.93f;
			LogAssert.IsTrue(Mathf.Abs(VolcanicPlume.BallisticRange(v, 45f, g) - v * v / g) < 1e-2f, "farthest at 45°: v²/g");
			float alpha = 25f;
			float t = VolcanicPlume.FlightSeconds(v, alpha, g);
			float landed = v * Mathf.Sin(alpha * Mathf.Deg2Rad) * t;
			LogAssert.IsTrue(Mathf.Abs(landed - VolcanicPlume.BallisticRange(v, alpha, g)) < 0.05f, "where the arc lands is where the range says");
			float apex = v * Mathf.Cos(alpha * Mathf.Deg2Rad) * (0.5f * t) - 0.5f * g * (0.25f * t * t);
			float straightUp = VolcanicPlume.FountainHeight(v * Mathf.Cos(alpha * Mathf.Deg2Rad), g);
			LogAssert.IsTrue(Mathf.Abs(apex - straightUp) < 0.05f, "at half its flight it is at the top of its parabola: no drag");

			VolcanicPlume.FalloutRing(v, g, out float inner, out float outer);
			LogAssert.IsTrue(inner > 0f && inner < outer, "a ring, not a disc");
			LogAssert.IsTrue(Mathf.Abs(outer - VolcanicPlume.BallisticRange(v, VolcanicPlume.FountainConeDegrees, g)) < 1e-3f, "its edge where the widest launch lands");
		}

		[Test]
		public void AFountainIsScaledToAScene_ButStillFallsByTheWorldsGravity()
		{
			float g = 9.81f * 1254f / 6371f; // Helis
			float quiet = VolcanicPlume.LaunchSpeed(0.08f), loud = VolcanicPlume.LaunchSpeed(1f);
			LogAssert.IsTrue(quiet / VolcanicPlume.PlayableScale >= 500f && quiet / VolcanicPlume.PlayableScale < 700f && Mathf.Abs(loud / VolcanicPlume.PlayableScale - 1000f) < 1f,
				"Prometheus-class to Pele-class, at the playable scale");
			float height = VolcanicPlume.FountainHeight(quiet, g);
			VolcanicPlume.FalloutRing(quiet, g, out _, out float outer);
			LogAssert.IsTrue(height > 200f && height < 3000f, $"hundreds of metres to a few km on Helis (was {height:0})");
			LogAssert.IsTrue(outer < 3000f, "landing inside a scene a few kilometres across");
			LogAssert.IsTrue(VolcanicPlume.FountainHeight(quiet, 2f * g) < height, "lower under stronger gravity");
		}

		// ── Vents ──────────────────────────────────────────────────────

		[Test]
		public void VentsStandOnlyInVentedBiomes_TheSameEveryTime()
		{
			BiomeTemplate volcano = Biome(Ash(true), 0.25f);
			BiomeTemplate haze = Biome(Ash(false), 0.25f);
			var area = new Rect(0f, 0f, 3000f, 3000f);
			var a = new List<VentSite>();
			var b = new List<VentSite>();
			VolcanicVents.Find(area, 42u, p => volcano, a);
			VolcanicVents.Find(area, 42u, p => volcano, b);
			LogAssert.IsTrue(a.Count > 0 && a.Count <= VolcanicVents.MaxVents, "a volcanic field has vents");
			LogAssert.AreEqual(a.Count, b.Count, "the same vents every time");
			for (int i = 0; i < a.Count; i++)
			{
				LogAssert.IsTrue(a[i].Position == b[i].Position && area.Contains(a[i].Position), "the same places, in the area");
			}
			VolcanicVents.Find(area, 42u, p => haze, a);
			LogAssert.AreEqual(0, a.Count, "a diffuse emitter has none");
			VolcanicVents.Find(area, 42u, p => p.x < 1500f ? volcano : null, a);
			foreach (VentSite v in a)
			{
				LogAssert.IsTrue(v.Position.x < 1500f, "only where the vented biome is");
			}
		}

		[Test]
		public void LavaLakes_AreFoundAtTheirDeepest_AndOnlyBelowTheLava()
		{
			var area = new Rect(0f, 0f, 1000f, 1000f);
			var lakes = new List<Vector2>();
			// A bowl whose floor is 20 m under the lava at (600, 400).
			System.Func<Vector2, float> bowl = p => -20f + 0.0005f * ((p - new Vector2(600f, 400f)).sqrMagnitude);
			VolcanicVents.LavaLakes(area, 0f, bowl, lakes);
			LogAssert.AreEqual(1, lakes.Count, "one lake in the cell");
			LogAssert.IsTrue((lakes[0] - new Vector2(600f, 400f)).magnitude < 60f, "at its deepest");
			VolcanicVents.LavaLakes(area, -30f, bowl, lakes);
			LogAssert.AreEqual(0, lakes.Count, "none where the ground stands above the lava");
		}

		// ── The ash the weather drops is the drawn plume's ─────────────

		[Test]
		public void TheDrawnPlume_IsThePlumeTheAshFallsFrom()
		{
			string field = CodeOnly(Read("Scripts/Shared/Implementation/Weather/WeatherField.cs"));
			LogAssert.IsTrue(field.Contains("VolcanicVents.Plumes(timeline, settings, tick, planet, air.Wind, position2, plumes)"), "the weather asks for the plumes in the open air's wind");
			LogAssert.IsTrue(field.Contains("steadyFallout, steadySubstance") && field.Contains("emission, fallout, falloutSubstance"), "and drops their fallout in the background and the storm frame");
			string presenter = CodeOnly(Read("Scripts/Client/World/Weather/Sky/VolcanicPlumePresenter.cs"));
			LogAssert.IsTrue(presenter.Contains("VolcanicVents.Plumes(context.Timeline, context.Settings, (uint)context.Tick, sample.Planet, sample.OpenAir.Wind,"),
				"the presenter draws exactly those plumes: the same planet and the same wind");
			string presentation = CodeOnly(Read("Scripts/Client/World/Weather/Presentation/WeatherPresentation.cs"));
			LogAssert.IsTrue(presentation.Contains("go.AddComponent<VolcanicPlumePresenter>()"), "and it is made with the weather's presentation");
			LogAssert.IsTrue(PrecipitationField.TraitsOf(WeatherChannel.AshWeight).FromCloud && PrecipitationField.TraitsOf(WeatherChannel.AshWeight).FromPlume,
				"ash falls from its cloud, and its cloud is the plume");
			string precipitation = CodeOnly(Read("Scripts/Client/World/Weather/Presentation/PrecipitationField.cs"));
			LogAssert.IsTrue(precipitation.Contains("traits.FromCloud && !traits.FromPlume ? 1f : 0f"), "so the water cloud overhead does not blank it");
			string host = CodeOnly(Read("Scripts/Server/Implementation/World/SceneServer/Weather/WeatherHost.cs"));
			LogAssert.IsTrue(host.Contains("if (kind == StormKind.Eruption)"), "and an eruption stays on its vent");
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
	}
}
