using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FishMMO.Client;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Phase I2n-b "sea halls" (2026-10-26, decision 83): the SeaHall cave form laid on the cave machinery (dome, ledge, melt shaft and
	/// its stair, dived passages, floor basins, stored smokers), its link gated by its interior's world, its spots and the client's
	/// presentation (pure functions of the shared clock), and the defaults (only the four hosts name it; nothing else moves).
	/// </summary>
	public class SeaHallTests
	{
		/// <summary>The sea halls' hosts and their weights (decision 83).</summary>
		public static readonly (string Biome, float Weight)[] Hosts =
		{
			("Ice Shelf", 0.4f), ("Tidal Fracture", 0.6f), ("Ice Geyser Field", 0.5f), ("Cryovolcanic Plain", 0.3f),
		};

		private const string Shapers = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/PointsOfInterest/Shapers/";
		private const string ShaderPath = "Assets/Prefabs/Client/Weather/Shaders/FishSeaHallVents.shader";

		/// <summary>An ice shelf's ground: flat at the freeboard with a low ridge, 1 km square round the origin.</summary>
		private static CaveGround Shelf(float ridge = 1.2f)
		{
			const float spacing = 4f;
			const int n = 251;
			var heights = new float[n * n];
			for (int k = 0; k < n; k++)
			{
				for (int i = 0; i < n; i++)
				{
					float x = -500f + i * spacing, z = -500f + k * spacing;
					heights[k * n + i] = 1f + ridge * Mathf.Max(0f, Mathf.Sin(x * 0.011f + 0.3f) * Mathf.Cos(z * 0.007f));
				}
			}
			return new CaveGround(-500f, -500f, spacing, n, n, heights);
		}

		private static CaveSystem PlanHall(int seed, int size, out string problem)
			=> CaveSystem.Plan(CaveForm.SeaHall, size, new Vector3(0f, 1f, 0f), 30f, seed, 77, Shelf(), 0f, out problem);

		[Test]
		public void TheForms_AreAppended_BySameOrdinalsEverywhere()
		{
			Assert.That((int)CaveForm.SeaHall, Is.EqualTo(6));
			Assert.That((int)BiomeCaveForm.SeaHall, Is.EqualTo(6));
			Assert.That((int)SceneCaveForm.SeaHall, Is.EqualTo(6));
			Assert.That(CaveLifeBake.FormOf(CaveForm.SeaHall), Is.EqualTo(SceneCaveForm.SeaHall));
			Assert.That((int)SceneCaveLifeKind.Smoker, Is.EqualTo(7));
			Assert.That((int)SceneCaveLifeKind.Hall, Is.EqualTo(8));
			Assert.That((int)SceneCaveLifeKind.Brine, Is.EqualTo(9));
			Assert.That((int)SceneCaveLifeFlags.Great, Is.EqualTo(4));
			Assert.That(CaveShaper.TryForm(POIType.SeaHall, out CaveForm form) && form == CaveForm.SeaHall, Is.True);
			Assert.That((int)AmbientCaveLife.FormOf(SceneCaveForm.SeaHall), Is.EqualTo((int)AmbientCaveForms.SeaHall));
		}

		[Test]
		public void APlannedHall_HasItsDomeLedgeShaftStairAndSmokers_UnderEnoughIce()
		{
			int planned = 0;
			var problems = new List<string>();
			for (int seed = 1; seed <= 16; seed++)
			{
				for (int size = 0; size <= 1; size++)
				{
					CaveSystem hall = PlanHall(seed, size, out string problem);
					if (hall == null)
					{
						problems.Add($"seed {seed} size {size}: {problem}");
						continue;
					}
					planned++;
					string at = $"seed {seed} size {size}";
					Assert.That(hall.Form, Is.EqualTo(CaveForm.SeaHall), at);
					Assert.That(hall.TryHallDome(out CaveChamber dome), Is.True, at);
					Assert.That(2f * dome.Radii.x, Is.InRange(89f, 201f), at + ": 90–200 m across");
					float water = hall.WaterLevel - hall.Origin.y;
					Assert.That(float.IsNaN(hall.WaterLevel), Is.False, at);
					Assert.That(water - dome.Floor, Is.InRange(25f, 60f), at + ": 25–60 m of water over the vent floor");
					float crown = dome.Centre.y + dome.Radii.y;
					Assert.That(crown - dome.Floor, Is.InRange(35f, 76f), at + ": the dome 35–70 m high");
					Assert.That(crown, Is.GreaterThan(water + 8f), at + ": an air pocket under the crown");
					// The ledge (the Merehall's floor) 2–6 m over the water, under 30–90 m of ice.
					CaveChamber ledge = hall.Chambers.First(c => c.Kind == CaveChamberKind.Room);
					Assert.That(ledge.Floor - water, Is.InRange(1.99f, 6.01f), at);
					Assert.That(-ledge.Floor, Is.InRange(CaveSystem.SeaHallShaftMin - 3f, CaveSystem.SeaHallShaftMax + 3f), at);
					// The way in: a stair's head on the lip, the shaft and its spiral down to the ledge's level.
					Assert.That(hall.Mouths[0].Kind, Is.EqualTo(CaveMouthKind.Ground), at);
					Assert.That(hall.Mouths[0].Floor, Is.EqualTo(Vector3.zero), at);
					Assert.That(hall.Shafts.Count, Is.EqualTo(1), at);
					CaveSolid stair = hall.Solids.First(s => s.Kind == CaveSolidKind.Spiral);
					Assert.That(stair.A.y, Is.EqualTo(ledge.Floor).Within(0.01f), at);
					Assert.That(stair.B.y, Is.EqualTo(0f), at);
					Assert.That(Mathf.Abs(stair.Arch) / (stair.Depth - stair.Width), Is.LessThanOrEqualTo(CaveSystem.LedgeGrade + 1e-4f), at + ": the stair's inner lip walks");
					Assert.That(hall.Shafts[0].Top, Is.GreaterThan(0f), at + ": the shaft opens through the ice");
					// The vault under MinCover of ice over its middle.
					Assert.That(1f - crown, Is.GreaterThanOrEqualTo(CaveSystem.MinCover), at);
					// Smokers on the floor, at most one Great one, its crown out of the water.
					Assert.That(hall.Smokers.Count, Is.InRange(3, 21), at);
					Assert.That(hall.Smokers.Count(s => s.Great), Is.LessThanOrEqualTo(1), at);
					foreach (CaveSmoker s in hall.Smokers)
					{
						Assert.That(s.Base.y, Is.EqualTo(dome.Floor).Within(0.01f), at);
						Assert.That(s.Orifice.y, Is.LessThan(crown), at);
						if (s.Great)
						{
							Assert.That(s.Orifice.y, Is.GreaterThan(water), at + ": the Great Smoker breaks the waterline");
						}
						else
						{
							Assert.That(s.Orifice.y, Is.LessThan(water), at);
						}
					}
					foreach (CaveHallPool p in hall.HallPools)
					{
						Assert.That(p.Mirror, Is.LessThan(p.Rim.y), at + ": a basin's mirror under its rim");
					}
				}
			}
			Console.WriteLine($"planned {planned}/32; refused: " + string.Join("; ", problems));
			Assert.That(planned, Is.GreaterThanOrEqualTo(24), "most halls plan on an ice shelf: " + string.Join("; ", problems));
		}

		[Test]
		public void APlan_IsDeterministic_AndItsValues_RoundTrip()
		{
			CaveSystem a = PlanHall(5, 1, out _), b = PlanHall(5, 1, out _);
			Assert.That(a, Is.Not.Null);
			float[] va = a.ToValues();
			Assert.That(va, Is.EqualTo(b.ToValues()), "the same inputs plan the same hall");
			CaveSystem back = CaveSystem.FromValues(va, a.Origin, a.Seed);
			Assert.That(back, Is.Not.Null);
			Assert.That(back.Form, Is.EqualTo(CaveForm.SeaHall));
			Assert.That(back.Smokers.Count, Is.EqualTo(a.Smokers.Count));
			Assert.That(back.HallPools.Count, Is.EqualTo(a.HallPools.Count));
			Assert.That(back.ToValues(), Is.EqualTo(va), "stored and read back to the bit");
			Assert.That(CaveSystem.FromValues(va.Take(va.Length - 1).ToArray(), a.Origin, a.Seed), Is.Null, "a short sea-hall section is no system");
		}

		[Test]
		public void OtherCaves_StoreNoSeaHallSection_AndAreUntouched()
		{
			string halls = File.ReadAllText(Shapers + "CaveSystem.SeaHall.cs");
			Assert.That(halls, Does.Contain("if (Form != CaveForm.SeaHall)\n\t\t\t{\n\t\t\t\treturn;").Or.Contain("if (Form != CaveForm.SeaHall)\r\n"), "only a sea hall writes its section");
			Assert.That(halls, Does.Contain("v[o] != SeaHallTag"), "a section is read only where its tag stands");
			string system = File.ReadAllText(Shapers + "CaveSystem.cs");
			Assert.That(system, Does.Contain("if (form == CaveForm.SeaHall)\n\t\t\t{\n\t\t\t\t// A dome under the ice").Or.Contain("if (form == CaveForm.SeaHall)\r\n"));
			Assert.That(system, Does.Contain("public const int Format = 7;"), "no format change: every stored cave stays as it was");
		}

		[Test]
		public void TheFloorsHeat_IsTheSmokersAndTheBrines()
		{
			CaveSystem hall = PlanHall(3, 1, out _);
			Assert.That(hall, Is.Not.Null);
			CaveSmoker s = hall.Smokers[0];
			Assert.That(hall.HeatAt(s.Base), Is.GreaterThan(0.45f), "hot at a chimney's foot");
			Assert.That(hall.HeatAt(s.Base + new Vector3(400f, 0f, 0f)), Is.EqualTo(0f), "cold far off");
			float near = hall.HeatAt(s.Base + new Vector3(1f, 0f, 0f)), far = hall.HeatAt(s.Base + new Vector3(4f, 0f, 0f));
			Assert.That(near, Is.GreaterThanOrEqualTo(far - 1e-4f));
			CaveSystem.SmokerHeat(new CaveSmoker { Great = true }, out float peak, out float reach);
			Assert.That((peak, reach), Is.EqualTo((1f, 40f)));
		}

		[Test]
		public void TheLink_IsGatedByTheInteriorsWorld_AndKeepsItsHabitatWhole()
		{
			Assert.That(BiomeCaveInteriors.GatedByWorld(BiomeCaveForm.SeaHall), Is.True);
			foreach (BiomeCaveForm f in (BiomeCaveForm[])Enum.GetValues(typeof(BiomeCaveForm)))
			{
				Assert.That(BiomeCaveInteriors.GatedByWorld(f), Is.EqualTo(f == BiomeCaveForm.SeaHall), f.ToString());
				Assert.That(BiomeCaveInteriors.InEveryList(f), Is.EqualTo(f != BiomeCaveForm.SeaHall), f.ToString());
			}
			const int barren = (int)BiomeHabitat.Barren, sea = (int)(BiomeHabitat.Sea | BiomeHabitat.Cave);
			Assert.That(BiomeCaveInteriors.LinkedHabitat(BiomeCaveForm.SeaHall, barren, sea, barren), Is.EqualTo(sea), "no host Barren bit (Q7)");
			Assert.That(BiomeCaveInteriors.LinkedHabitat(BiomeCaveForm.Cave, barren, sea, barren), Is.EqualTo(BiomeCaveInteriors.LinkedHabitat(barren, sea, barren)));
			Assert.That(BiomeCaveInteriors.DefaultInterior(BiomeCaveForm.SeaHall), Is.Null, "no default link: only its hosts name it");
			Assert.That(BiomeCaveInteriors.DefaultFormations().Any(f => f.Form == BiomeCaveForm.SeaHall), Is.False);
			Assert.That(BiomeCaveInteriors.DefaultGlows().Any(f => f.Form == BiomeCaveForm.SeaHall), Is.False);
			Assert.That(BiomeArtSpec.DefaultCaveShell(CaveForm.SeaHall), Is.EqualTo(CliffRocks.Ice));
			Assert.That(PointOfInterestCaveDressing.StandInRock(CaveForm.SeaHall), Is.EqualTo(RockTypes.WaterIce.Name));
			// The world the Subsurface Ocean Vent needs: frozen through AND cryovolcanic (its Requires).
			BiomeWorldConditions earth = BiomeWorldConditions.Earthlike;
			Assert.That(earth.Meets(BiomeWorldRequirement.Cryovolcanic), Is.False, "never on an Earth");
			string art = File.ReadAllText("Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/Terrain/CaveInteriorArt.cs");
			Assert.That(art, Does.Contain("BiomeCaveInteriors.GatedByWorld(f) && !(world ?? BiomeWorldConditions.Earthlike).Meets(interior.Requires)"));
			string stage = File.ReadAllText("Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/PointsOfInterest/PointOfInterestStage.cs");
			Assert.That(stage, Does.Contain("CaveInteriorArt.GateSeaHalls(biome,"), "the planner weighs a hall 0 where it would not hold");
			string shaper = File.ReadAllText(Shapers + "CaveShaper.cs");
			Assert.That(shaper, Does.Contain("if (form == CaveForm.SeaHall && !planned.Linked)"), "and the shaper refuses one with no sea");
		}

		[Test]
		public void OnlyTheFourHosts_NameSeaHalls_AndTheVentIsAnInteriorOnly()
		{
			PointOfInterestRules rules = PointOfInterestRules.Default();
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				(string Biome, float Weight) host = Hosts.FirstOrDefault(h => h.Biome == entry.Biome);
				BiomeSiteKind site = entry.SiteKinds().FirstOrDefault(k => k.Kind == POIType.SeaHall);
				bool links = BiomeCaveInteriors.TryLink(entry.CaveInteriors(), BiomeCaveForm.SeaHall, out string interior);
				float row = rules.KindWeightsFor(entry.Biome).Where(k => k.Kind == POIType.SeaHall).Select(k => k.Weight).DefaultIfEmpty(0f).First();
				if (host.Biome == null)
				{
					Assert.That(site.Kind, Is.Not.EqualTo(POIType.SeaHall), entry.Biome + " hosts no sea hall");
					Assert.That(links, Is.False, entry.Biome);
					Assert.That(row, Is.EqualTo(0f), entry.Biome);
					continue;
				}
				Assert.That(site.Kind, Is.EqualTo(POIType.SeaHall), entry.Biome);
				Assert.That(site.Weight, Is.EqualTo(host.Weight), entry.Biome);
				Assert.That(row, Is.EqualTo(host.Weight), entry.Biome + ": the catalogue's row agrees");
				Assert.That(links && interior == BiomeCaveInteriors.SubsurfaceOceanVent, Is.True, entry.Biome);
			}
			BiomeSpecTable.Entry vent = BiomeSpecTable.Entries.First(e => e.Name == "Subsurface Ocean Vent");
			Assert.That(vent.SelectionWeight, Is.EqualTo(0f), "the table never selects it");
			Assert.That(File.ReadAllText("Assets/Templates/Entity/Biomes/Subsurface Ocean Vent.asset"), Does.Contain("\n  SelectionWeight: 0\n"));
			Assert.That(BiomeArtSpec.For("Subsurface Ocean Vent").Habitat, Is.EqualTo(BiomeHabitat.Sea | BiomeHabitat.Cave));
			PointOfInterestKindRule hallRule = rules.RuleFor(POIType.SeaHall);
			Assert.That(hallRule.Face, Is.False, "a melt shaft needs no face");
			Assert.That(hallRule.DefaultBiomeWeight, Is.EqualTo(0f));
		}

		[Test]
		public void TheHallsSpots_CarryItsDomeSeaSmokersAndBrine_AndTheClientReadsThemBack()
		{
			CaveSystem hall = PlanHall(7, 1, out _);
			Assert.That(hall, Is.Not.Null);
			List<SceneCaveLifeSpot> spots = PointOfInterestCaveDressing.HallSpotsOf(hall, 77, 1234);
			Assert.That(spots.Count(s => s.Kind == SceneCaveLifeKind.Hall), Is.EqualTo(1));
			Assert.That(spots.Count(s => s.Kind == SceneCaveLifeKind.Pool), Is.EqualTo(1));
			Assert.That(spots.Count(s => s.Kind == SceneCaveLifeKind.Smoker), Is.EqualTo(hall.Smokers.Count));
			Assert.That(spots.Count(s => s.Kind == SceneCaveLifeKind.Brine), Is.EqualTo(hall.HallPools.Count(p => p.Kind == CaveHallPoolKind.Brine)));
			Assert.That(spots.All(s => s.Form == SceneCaveForm.SeaHall && s.SiteId == 77 && s.Biome == 1234), Is.True);
			SceneCaveLifeSpot sea = spots.First(s => s.Kind == SceneCaveLifeKind.Pool);
			Assert.That(sea.Position.y, Is.EqualTo(hall.WaterLevel).Within(1e-3f));
			Assert.That(SeaLifeSystem.IsCaveSea(in sea), Is.True, "its sea is wide and deep enough for its own sea life");
			List<SeaHallVents.Hall> halls = SeaHallVents.HallsOf(spots);
			Assert.That(halls.Count, Is.EqualTo(1));
			Assert.That(halls[0].Smokers.Count, Is.EqualTo(hall.Smokers.Count));
			Assert.That(halls[0].Water, Is.EqualTo(hall.WaterLevel).Within(1e-3f));
			foreach (SeaHallVents.Smoker s in halls[0].Smokers)
			{
				float rise = SeaHallVents.RiseOf(in s, halls[0].Water);
				Assert.That(rise, Is.GreaterThan(0f));
				if (s.Kind != SceneSeaHallVents.Great)
				{
					Assert.That(s.Orifice.y + rise, Is.LessThanOrEqualTo(halls[0].Water), "an underwater plume stops under the surface");
				}
			}
			Assert.That(halls[0].Smokers.Count(s => s.Kind == SceneSeaHallVents.Great), Is.EqualTo(hall.Smokers.Count(s => s.Great)));
			// A cave sea's floor: water within its pool, none beyond.
			var ground = new SeaLifeSystem.CavePoolGround(sea.Position, sea.Extent.x, sea.Extent.y, null);
			Assert.That(ground.TryGround(sea.Position.x, sea.Position.z, out float bed) && Mathf.Abs(bed - (sea.Position.y - sea.Extent.y)) < 1e-3f, Is.True);
			Assert.That(ground.TryGround(sea.Position.x + 2f * sea.Extent.x, sea.Position.z, out float none) && float.IsPositiveInfinity(none), Is.True);
		}

		[Test]
		public void ThePlumes_AreTheTidesAndTheFlickers_PureAndInRange()
		{
			for (uint seed = 1; seed < 400; seed += 7)
			{
				for (float t = 0f; t < SceneSeaHallVents.Wrap; t += 97.3f)
				{
					float s = SceneSeaHallVents.Strength(0.65f, t, seed, false), g = SceneSeaHallVents.Strength(0.3f, t, seed, true);
					Assert.That(s, Is.InRange(0f, 1f));
					Assert.That(g, Is.InRange(SceneSeaHallVents.GreatFloor, 1f), "the Great Smoker never under 0.85");
					Assert.That(SceneSeaHallVents.Strength(0.65f, t, seed, false), Is.EqualTo(s), "pure");
				}
			}
			Assert.That(SceneSeaHallVents.Tide(0.3f), Is.EqualTo(0.75f).Within(1e-6f));
			Assert.That(SceneSeaHallVents.Tide(1f), Is.EqualTo(1f).Within(1e-6f));
			// Barely faltering: on over most of the time.
			double sum = 0;
			int n = 0;
			for (float t = 0f; t < SceneSeaHallVents.Wrap; t += 3.1f, n++)
			{
				sum += SceneSeaHallVents.Flicker(t, 12345u);
			}
			Assert.That(sum / n, Is.GreaterThan(0.75), "a smoker is on most of the time");
			// Continuous across the wrap and smooth (30 s ramps): never more than a ramp's step in a tenth of a second.
			foreach (uint seed in new uint[] { 3u, 99u, 4242u })
			{
				Assert.That(Mathf.Abs(SceneSeaHallVents.Flicker(SceneSeaHallVents.Wrap - 0.01f, seed) - SceneSeaHallVents.Flicker(0f, seed)), Is.LessThan(0.02f));
				for (float t = 0f; t < 4000f; t += 0.1f)
				{
					Assert.That(Mathf.Abs(SceneSeaHallVents.Flicker(t + 0.1f, seed) - SceneSeaHallVents.Flicker(t, seed)), Is.LessThan(0.02f));
				}
			}
			foreach (float slot in SceneSeaHallVents.FlickerSlots)
			{
				Assert.That(SceneSeaHallVents.Wrap % slot, Is.EqualTo(0f), $"{slot} divides the wrap");
			}
		}

		[Test]
		public void NothingScrubs_EveryPhaseRunsOnAWholePeriod_AndBriniclesGrowAndSnap()
		{
			foreach (float s in new[] { 7.3f, 33f, 120f, 412f, 9999f })
			{
				float p = SceneSeaHallVents.WholePeriod(s, SceneSeaHallVents.Wrap);
				Assert.That(SceneSeaHallVents.Wrap / p, Is.EqualTo(Mathf.Round(SceneSeaHallVents.Wrap / p)).Within(1e-3f));
			}
			foreach (float hours in SceneSeaHallVents.BriniclePeriodsHours)
			{
				Assert.That(SceneSeaHallVents.BrinicleWrap % (3600f * hours), Is.EqualTo(0f), $"{hours} h divides 36 h");
			}
			const uint seed = 777u;
			float longest = SceneSeaHallVents.BrinicleLongestOver(seed, 20f);
			Assert.That(longest, Is.InRange(SceneSeaHallVents.BrinicleShortest, SceneSeaHallVents.BrinicleLongest));
			Assert.That(SceneSeaHallVents.BrinicleLongestOver(seed, 1f), Is.LessThanOrEqualTo(0.5f), "never into the water");
			float period = SceneSeaHallVents.BriniclePeriod(seed);
			float last = SceneSeaHallVents.BrinicleLength(0f, seed, longest, out _);
			int snaps = 0;
			for (float t = 60f; t <= SceneSeaHallVents.BrinicleWrap; t += 60f)
			{
				float l = SceneSeaHallVents.BrinicleLength(t, seed, longest, out float since);
				Assert.That(l, Is.InRange(0f, longest));
				Assert.That(since, Is.InRange(0f, period));
				if (l < last)
				{
					snaps++;
				}
				else
				{
					Assert.That(l - last, Is.LessThan(longest * 60f / period + 1e-3f), "it grows steadily");
				}
				last = l;
			}
			Assert.That(snaps, Is.EqualTo(Mathf.RoundToInt(SceneSeaHallVents.BrinicleWrap / period)).Within(1), "one snap a period");
			Assert.That(SceneSeaHallVents.BrinicleSeconds(SceneSeaHallVents.BrinicleWrap * 3.0 + 12.5), Is.EqualTo(12.5f).Within(0.01f));
		}

		[Test]
		public void TheShader_MirrorsTheNumbers_AndCompilesTheWebGpuWay()
		{
			string shader = File.ReadAllText(ShaderPath);
			Assert.That(shader, Does.Contain("#pragma use_dxc vulkan"));
			Assert.That(shader, Does.Not.Contain("#include \"FishIndirectInstancing"));
			Assert.That(shader, Does.Contain("#define HALL_SMOKERS " + SceneSeaHallVents.MaxSmokers));
			Assert.That(shader, Does.Contain("#define HALL_WRAP 10000.0"));
			Assert.That(shader, Does.Contain("#define HALL_BRINICLE_WRAP 129600.0"));
			Assert.That(shader, Does.Contain("#define HALL_PER_GROVE " + SceneSeaHallVents.BriniclesPerGrove));
			Assert.That(shader, Does.Contain("k == 0u ? 6.0 : k == 1u ? 9.0 : k == 2u ? 12.0 : 18.0"), "the brinicle periods, a select chain");
			Assert.That(shader, Does.Contain("HallHash(seed, 3u, 1u)"), "the rise speed's hash");
			Assert.That(shader, Does.Contain("HallHash(seed, 32u, 0u)"), "the brinicle phase's hash");
			Assert.That(shader, Does.Not.Contain("[unroll]"), "no forced-unroll loop (FXC's WebGPU path)");
			string profile = File.ReadAllText("Assets/Scripts/Client/World/Weather/Presentation/WeatherRenderProfile.cs");
			Assert.That(profile, Does.Contain("public Shader SeaHallVentsShader;"));
			string assets = File.ReadAllText("Assets/Scripts/Client/World/Weather/Editor/WeatherRenderAssets.cs");
			Assert.That(assets, Does.Contain("profile.SeaHallVentsShader = AssetDatabase.LoadAssetAtPath<Shader>(SeaHallVentsShaderPath);"));
		}
	}
}
