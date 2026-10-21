using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Weather;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Phase I2q "hot ground" (decision 47, 2026-10-18): the Steam emission authored for the geyser basin, mud pots as a fourth
	/// geothermal kind on a per-biome split, the cut finding the client's very sites and digging pools, cones, rims and runoff
	/// under them (idempotent), the heat mask and its bands, the Great Spout and its timing, and hot water. Pure: no Unity object
	/// is made (templates and maps are native), so what needs one is read from the sources or the assets' YAML.
	/// </summary>
	public class GeothermalGroundTests
	{
		private const string WorldFolder = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World";
		private const string BiomeFolder = "Assets/Templates/Entity/Biomes";

		private static string Code(string path) => File.ReadAllText(path);

		private static readonly BiomeGeothermalShares Basin = new BiomeGeothermalShares(0.25f, 0.40f, 0.20f, 0.15f);

		/// <summary>The old comparison, verbatim, before the split was on the template.</summary>
		private static GeothermalKind OldKind(float k)
		{
			return k < GeothermalVents.GeyserShare ? GeothermalKind.Geyser : k < GeothermalVents.GeyserShare + GeothermalVents.HotSpringShare ? GeothermalKind.HotSpring : GeothermalKind.Fumarole;
		}

		/// <summary>A steaming ground over the west half of a scene, plain ground east of x = 0.</summary>
		private static GeothermalGround SteamWest(Vector2 p, BiomeGeothermalShares shares, float chance = 0.7f)
		{
			return p.x < 0f ? new GeothermalGround { Chance = chance, AllKinds = true, Shares = shares } : GeothermalGround.None;
		}

		// ── Kinds ──

		[Test]
		public void KindOf_TodaysSplit_IsTheOldComparisonToTheBit()
		{
			var rng = new System.Random(5);
			for (int i = 0; i <= 200000; i++)
			{
				float k = i < 100001 ? i / 100000f : (float)rng.NextDouble();
				Assert.That(GeothermalVents.KindOf(k, BiomeGeothermalShares.Today), Is.EqualTo(OldKind(k)), $"k {k:R}");
				Assert.That(GeothermalVents.KindOf(k, default), Is.EqualTo(OldKind(k)), "an unfilled split reads as today's");
			}
			// The edges themselves.
			foreach (float k in new[] { 0.25f, 0.65f, 0.6499999f, 0.2499999f, BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(0.65f) - 1) })
			{
				Assert.That(GeothermalVents.KindOf(k, BiomeGeothermalShares.Today), Is.EqualTo(OldKind(k)));
			}
		}

		[Test]
		public void KindOf_TheBasinsSplit_GivesItsShares_AndMudPotsOnlyWhereNamed()
		{
			var counts = new int[4];
			const int n = 100000;
			for (int i = 0; i < n; i++)
			{
				counts[(int)GeothermalVents.KindOf((i + 0.5f) / n, Basin)]++;
			}
			Assert.That(counts[(int)GeothermalKind.Geyser] / (float)n, Is.EqualTo(0.25f).Within(0.001f));
			Assert.That(counts[(int)GeothermalKind.HotSpring] / (float)n, Is.EqualTo(0.40f).Within(0.001f));
			Assert.That(counts[(int)GeothermalKind.Fumarole] / (float)n, Is.EqualTo(0.20f).Within(0.001f));
			Assert.That(counts[(int)GeothermalKind.MudPot] / (float)n, Is.EqualTo(0.15f).Within(0.001f));
			for (int i = 0; i < n; i++)
			{
				Assert.That(GeothermalVents.KindOf((i + 0.5f) / n, BiomeGeothermalShares.Today), Is.Not.EqualTo(GeothermalKind.MudPot));
			}
			Assert.That((int)GeothermalKind.MudPot, Is.EqualTo(3), "append-only");
		}

		// ── The search: the cut's whole scene and the client's corner of it ──

		[Test]
		public void TheScenesSites_HoldEveryCornerSearch_AndACameraSearchIsNeverCutShort()
		{
			var origin = new Vector2(-2048f, -2048f);
			var scene = new Rect(origin, new Vector2(4096f, 4096f));
			const uint seed = 0xC0FFEEu;
			// A whole scene of steaming ground at the highest chance: the worst a camera can be in.
			Func<Vector2, GeothermalGround> all = p => new GeothermalGround { Chance = 1f, AllKinds = true, Shares = Basin };
			var whole = new List<GeothermalSite>();
			GeothermalVents.Find(origin, scene, seed, all, whole, GeothermalVents.CellMetres, int.MaxValue);
			Assert.That(whole.Count, Is.GreaterThan(2048), "more than any capped search could hold");
			var keys = new HashSet<(float, float, uint, GeothermalKind)>(whole.Select(s => (s.Position.x, s.Position.y, s.Seed, s.Kind)));
			var near = new List<GeothermalSite>();
			foreach (Vector2 camera in new[] { new Vector2(0f, 0f), new Vector2(1500f, 1700f), new Vector2(-1900f, 300f) })
			{
				// GeothermalVents.Near's rectangle: the camera ± (Reach + Refind), clipped to the scene.
				float r = 1500f + 60f;
				Rect area = Rect.MinMaxRect(Mathf.Max(scene.xMin, camera.x - r), Mathf.Max(scene.yMin, camera.y - r), Mathf.Min(scene.xMax, camera.x + r), Mathf.Min(scene.yMax, camera.y + r));
				GeothermalVents.Find(origin, area, seed, all, near, GeothermalVents.CellMetres, GeothermalVents.MaxSites);
				int inside = whole.Count(s => s.Position.x >= area.xMin && s.Position.x <= area.xMax && s.Position.y >= area.yMin && s.Position.y <= area.yMax);
				Assert.That(near.Count, Is.EqualTo(inside), "the camera's search finds every site the cut dug round it");
				Assert.That(near.Count, Is.LessThan(GeothermalVents.MaxSites));
				foreach (GeothermalSite s in near)
				{
					Assert.That(keys.Contains((s.Position.x, s.Position.y, s.Seed, s.Kind)), Is.True);
				}
			}
		}

		[Test]
		public void TheGroundOverload_IsTheTemplatesSearch()
		{
			string vents = Code("Assets/Scripts/Shared/Implementation/Weather/GeothermalVents.cs");
			// The template overload (Near, the client) reads the ground through GeothermalGround.Of, as SceneSites (the cut) does.
			StringAssert.Contains("Find(origin, area, seed, p => GeothermalGround.Of(biomeAt(p)), into, cellMetres, MaxSites);", vents);
			StringAssert.Contains("Find(map.WorldOrigin, new Rect(map.WorldOrigin, map.WorldSize), SeedOf(map), p => GeothermalGround.Of(map.Sample(new Vector3(p.x, 0f, p.y)))", vents);
			StringAssert.Contains("Find(map.WorldOrigin, near, SeedOf(map), p => map.Sample(new Vector3(p.x, 0f, p.y)), into);", vents);
			StringAssert.Contains("return map != null ? SeedOf(map.name) : 0u;", vents);
			// The cut names its map as the biome stage writes it.
			string generator = Code(WorldFolder + "/SceneGenerator.cs");
			StringAssert.Contains("$\"{WorldEditorAssets.Sanitize(request.SceneName)} Biome Map\"", generator);
			StringAssert.Contains("string mapPath = $\"{terrainFolder}/{WorldEditorAssets.Sanitize(request.SceneName)} Biome Map.asset\";", generator);
			string carve = Code(WorldFolder + "/Terrain/GeothermalGroundCarve.cs");
			StringAssert.Contains("map.name = mapName;", carve);
			StringAssert.Contains("field.WriteTo(map);", carve);
			StringAssert.Contains("GeothermalVents.SceneSites(map, sites);", carve);
			Assert.That(GeothermalVents.SeedOf("Moron 1 Biome Map"), Is.EqualTo(unchecked((uint)"Moron 1 Biome Map".GetDeterministicHashCode() ^ 0x5BD1E995u)));
		}

		[Test]
		public void TodaysSplit_FindsTheSameSitesAsBefore()
		{
			// The old search, by its own arithmetic, against the new one with today's split: identical lists.
			var origin = new Vector2(-1000f, -1000f);
			var area = new Rect(-1000f, -1000f, 2000f, 2000f);
			var sites = new List<GeothermalSite>();
			GeothermalVents.Find(origin, area, 99u, p => SteamWest(p, BiomeGeothermalShares.Today), sites, GeothermalVents.CellMetres, GeothermalVents.MaxSites);
			var basin = new List<GeothermalSite>();
			GeothermalVents.Find(origin, area, 99u, p => SteamWest(p, Basin), basin, GeothermalVents.CellMetres, GeothermalVents.MaxSites);
			Assert.That(sites.Count, Is.EqualTo(basin.Count), "the split moves no site, it only names them");
			for (int i = 0; i < sites.Count; i++)
			{
				Assert.That(sites[i].Position, Is.EqualTo(basin[i].Position));
				Assert.That(sites[i].Seed, Is.EqualTo(basin[i].Seed));
				Assert.That(sites[i].Kind, Is.Not.EqualTo(GeothermalKind.MudPot));
			}
			Assert.That(basin.Exists(s => s.Kind == GeothermalKind.MudPot), Is.True);
		}

		// ── Mud pots and the Great Spout on the shared clock ──

		[Test]
		public void APlop_IsAPureFunctionOfTheClock_AndEndsInsideItsTick()
		{
			var pot = new GeothermalSite { Position = new Vector2(3f, 4f), Kind = GeothermalKind.MudPot, Size = 0.7f, Seed = 424242u };
			int plops = 0, slots = 0;
			double from = 1.7e9;
			for (int lane = 0; lane < GeothermalVents.PlopLanes; lane++)
			{
				long last = long.MinValue;
				for (double t = from; t < from + 600.0; t += 0.05)
				{
					MudPlop a = GeothermalVents.Plop(pot, t, lane);
					MudPlop b = GeothermalVents.Plop(pot, t, lane);
					Assert.That(a.Since, Is.EqualTo(b.Since));
					Assert.That(a.Offset, Is.EqualTo(b.Offset));
					if (a.Slot != last)
					{
						last = a.Slot;
						slots++;
						if (a.Duration > 0f)
						{
							plops++;
							Assert.That(a.Offset.magnitude, Is.LessThanOrEqualTo(0.7f * GeothermalVents.SourceRadius(pot) + 1e-4f));
							Assert.That(a.Height, Is.InRange(0.05f, 0.61f));
						}
					}
				}
			}
			Assert.That(plops / (float)slots, Is.InRange(0.5f, 0.7f), "three ticks in five plop");
		}

		[Test]
		public void APlop_FitsInsideItsTick()
		{
			for (uint seed = 1; seed < 400; seed++)
			{
				var pot = new GeothermalSite { Kind = GeothermalKind.MudPot, Size = (seed % 10) / 9f, Seed = seed * 2654435761u };
				for (double t = 1e8; t < 1e8 + 30.0; t += 0.1)
				{
					MudPlop p = GeothermalVents.Plop(pot, t, (int)(seed & 1));
					if (p.Duration <= 0f)
					{
						continue;
					}
					double start = t - p.Since;
					// The tick holding t holds the whole plop: its start and its last drop.
					MudPlop atStart = GeothermalVents.Plop(pot, start + 1e-4, (int)(seed & 1));
					MudPlop atEnd = GeothermalVents.Plop(pot, start + p.Duration + p.Flight - 1e-4, (int)(seed & 1));
					Assert.That(atStart.Slot, Is.EqualTo(p.Slot));
					Assert.That(atEnd.Slot, Is.EqualTo(p.Slot));
				}
			}
		}

		[Test]
		public void TheGreatSpout_IsTheLargestGeyser_AndItsNextEruptionIsTheMomentsOwn()
		{
			var sites = new List<GeothermalSite>
			{
				new GeothermalSite { Kind = GeothermalKind.HotSpring, Size = 1f, Seed = 1 },
				new GeothermalSite { Kind = GeothermalKind.Geyser, Size = 0.6f, Seed = 9 },
				new GeothermalSite { Kind = GeothermalKind.Geyser, Size = 0.9f, Seed = 7 },
				new GeothermalSite { Kind = GeothermalKind.Geyser, Size = 0.9f, Seed = 3 },
			};
			Assert.That(GeothermalVents.GreatSpoutIndex(sites), Is.EqualTo(3), "the largest, the smaller seed on a tie");
			Assert.That(GeothermalVents.GreatSpoutIndex(sites.Take(1).ToList()), Is.EqualTo(-1));
			GeothermalSite spout = sites[3];
			double interval = GeothermalVents.IntervalSeconds(spout);
			for (double t = 1.6e9; t < 1.6e9 + 5 * interval; t += interval / 7.3)
			{
				GeyserMoment m = GeothermalVents.Moment(spout, t);
				Assert.That(GeothermalVents.EruptionStart(spout, m.Eruption), Is.EqualTo(t - m.Since).Within(1e-3), "the same arithmetic");
				double next = GeothermalVents.SecondsToNextEruption(spout, t);
				Assert.That(next, Is.GreaterThan(0.0));
				GeyserMoment then = GeothermalVents.Moment(spout, t + next + 0.01);
				Assert.That(then.Since, Is.InRange(0f, 0.02f), "an eruption begins exactly then");
			}
		}

		// ── The carve ──

		/// <summary>A rolling heightfield sampled on a 2 m lattice and quantised as a 16-bit terrain stores it (range 200 m).</summary>
		private sealed class Field
		{
			public const int N = 257;
			public const float Step = 2f, Origin = -256f, Range = 200f;
			public float[] H = new float[N * N];

			public static Field Hills()
			{
				var f = new Field();
				for (int j = 0; j < N; j++)
				{
					for (int i = 0; i < N; i++)
					{
						float x = Origin + i * Step, z = Origin + j * Step;
						f.H[j * N + i] = Q(40f + 6f * Mathf.Sin(x * 0.013f) + 4f * Mathf.Cos(z * 0.021f) + 0.02f * x);
					}
				}
				return f;
			}

			public static float Q(float metres) => Mathf.Round(Mathf.Clamp01(metres / Range) * 65535f) / 65535f * Range;

			public float At(float x, float z)
			{
				float gx = Mathf.Clamp((x - Origin) / Step, 0f, N - 1.001f), gz = Mathf.Clamp((z - Origin) / Step, 0f, N - 1.001f);
				int i = (int)gx, j = (int)gz;
				float fx = gx - i, fz = gz - j;
				return Mathf.Lerp(Mathf.Lerp(H[j * N + i], H[j * N + i + 1], fx), Mathf.Lerp(H[(j + 1) * N + i], H[(j + 1) * N + i + 1], fx), fz);
			}

			/// <summary>Carves a plan, returning the deltas as stored (read back after quantising).</summary>
			public float[] Carve(GeothermalGroundCarve.Plan plan)
			{
				var deltas = new float[H.Length];
				for (int j = 0; j < N; j++)
				{
					for (int i = 0; i < N; i++)
					{
						int k = j * N + i;
						float move = GeothermalGroundCarve.PlanOffset(plan, Origin + i * Step, Origin + j * Step, H[k]);
						if (Mathf.Abs(move) > 1e-5f)
						{
							float before = H[k];
							H[k] = Q(H[k] + move);
							deltas[k] = H[k] - before;
						}
					}
				}
				return deltas;
			}

			public void Restore(float[] deltas)
			{
				for (int k = 0; k < H.Length; k++)
				{
					H[k] -= deltas[k];
				}
			}
		}

		private static List<GeothermalSite> SitesFor(Rect scene)
		{
			var sites = new List<GeothermalSite>();
			GeothermalVents.Find(scene.min, scene, 77u, p => new GeothermalGround { Chance = 1f, AllKinds = true, Shares = Basin }, sites, 40f, int.MaxValue);
			return sites;
		}

		[Test]
		public void TheCarve_IsDeterministic_AndRestoredToTheBit_AndTheSameAgain()
		{
			var scene = new Rect(Field.Origin, Field.Origin, (Field.N - 1) * Field.Step, (Field.N - 1) * Field.Step);
			Field field = Field.Hills();
			float[] original = (float[])field.H.Clone();
			List<GeothermalSite> sites = SitesFor(scene);
			Assert.That(sites.Select(s => s.Kind).Distinct().Count(), Is.EqualTo(4), "every kind stands in the test field");
			GeothermalGroundCarve.Plan a = GeothermalGroundCarve.Make(sites, field.At, null, null, scene);
			GeothermalGroundCarve.Plan b = GeothermalGroundCarve.Make(sites, field.At, null, null, scene);
			Assert.That(a.Dug.Select(d => d.Base), Is.EqualTo(b.Dug.Select(d => d.Base)));
			Assert.That(a.Runoff.Count, Is.GreaterThan(0), "springs and geysers run off downhill");
			Assert.That(a.Runoff.SelectMany(l => l), Is.EqualTo(b.Runoff.SelectMany(l => l)));
			Assert.That(a.Heat.Texels, Is.EqualTo(b.Heat.Texels));

			float[] deltas = field.Carve(a);
			float[] carved = (float[])field.H.Clone();
			Assert.That(deltas.Count(d => d != 0f), Is.GreaterThan(100));
			field.Restore(deltas);
			Assert.That(field.H, Is.EqualTo(original), "taken out exactly");
			GeothermalGroundCarve.Plan again = GeothermalGroundCarve.Make(sites, field.At, null, null, scene);
			Assert.That(again.Dug.Select(d => d.Base), Is.EqualTo(a.Dug.Select(d => d.Base)));
			field.Carve(again);
			Assert.That(field.H, Is.EqualTo(carved), "carved again from the same ground, the same ground");

			// CONTROL: carving the carved ground without restoring piles up.
			field.Carve(GeothermalGroundCarve.Make(sites, field.At, null, null, scene));
			Assert.That(field.H, Is.Not.EqualTo(carved));
		}

		[Test]
		public void APool_StandsOverItsOwnFunnel_WhereTheClientLooksForIt()
		{
			var scene = new Rect(Field.Origin, Field.Origin, (Field.N - 1) * Field.Step, (Field.N - 1) * Field.Step);
			Field field = Field.Hills();
			List<GeothermalSite> sites = SitesFor(scene);
			GeothermalGroundCarve.Plan plan = GeothermalGroundCarve.Make(sites, field.At, null, null, scene);
			field.Carve(plan);
			int pools = 0;
			foreach (GeothermalGroundCarve.Dug dug in plan.Dug)
			{
				SceneHydrology.Lake lake = GeothermalGroundCarve.PoolLake(dug);
				if (dug.Site.Kind != GeothermalKind.HotSpring)
				{
					Assert.That(lake, Is.Null);
					continue;
				}
				pools++;
				Assert.That(lake.Frozen, Is.False);
				Assert.That(GeothermalGroundCarve.IsPool(lake.Id), Is.True);
				Assert.That(PointOfInterestPaddies.IsPaddy(lake.Id), Is.True, "cleared with the paddies' water each place");
				Assert.That(PointOfInterestLinearFeatures.IsChannel(lake.Id), Is.False);
				Assert.That(lake.Covers(dug.Site.Position.x, dug.Site.Position.y), Is.True);
				float ground = field.At(dug.Site.Position.x, dug.Site.Position.y);
				// The client's own-pool rule (GeothermalSteamPresenter): water over the ground by more than 0.3 m, and no deeper than its funnel + 0.5 m.
				Assert.That(lake.Level, Is.GreaterThan(ground + 0.3f), $"pool {dug.Index}: water stands in it");
				Assert.That(lake.Level, Is.LessThanOrEqualTo(ground + GeothermalVents.PoolDepth(dug.Site) + 0.5f), $"pool {dug.Index}: the client takes it for the spring's own");
			}
			Assert.That(pools, Is.GreaterThan(0));
			// Ids apart from every paddy's and every terrace kind's.
			Assert.That(GeothermalGroundCarve.IsPool(PointOfInterestPaddies.LakeId(12, 3)), Is.False);
			Assert.That(GeothermalGroundCarve.IsPool(PointOfInterestFieldTerraces.LakeId(PointOfInterestFieldTerraces.TreadLakeKind, 5, 1, 2)), Is.False);
			Assert.That(GeothermalGroundCarve.IsPool(PointOfInterestLinearFeatures.ChannelLakeId(3, 4)), Is.False);
		}

		[Test]
		public void TheCarve_ShapesEachKind()
		{
			var geyser = new GeothermalGroundCarve.Dug { Site = new GeothermalSite { Kind = GeothermalKind.Geyser, Size = 0.5f }, Base = 10f };
			Assert.That(GeothermalGroundCarve.Offset(geyser, 1.2f, 0f, 10f), Is.GreaterThan(0f), "a cone");
			Assert.That(GeothermalGroundCarve.Offset(geyser, 0f, 0f, 10f), Is.LessThan(GeothermalGroundCarve.Offset(geyser, 1.2f, 0f, 10f)), "with its orifice");
			Assert.That(GeothermalGroundCarve.Offset(geyser, 20f, 0f, 10f), Is.EqualTo(0f));
			var spring = new GeothermalGroundCarve.Dug { Site = new GeothermalSite { Kind = GeothermalKind.HotSpring, Size = 0.5f }, Base = 10f };
			float r = GeothermalGroundCarve.CarveRadius(spring.Site);
			Assert.That(GeothermalGroundCarve.Offset(spring, 0f, 0f, 10f), Is.EqualTo(-(GeothermalVents.PoolDepth(spring.Site) + GeothermalGroundCarve.PoolFreeboard)).Within(1e-5f));
			Assert.That(GeothermalGroundCarve.Offset(spring, r + 0.1f, 0f, 9.5f), Is.GreaterThan(0.5f), "the rim dams the downhill side");
			var pot = new GeothermalGroundCarve.Dug { Site = new GeothermalSite { Kind = GeothermalKind.MudPot, Size = 0.5f }, Base = 10f };
			Assert.That(GeothermalGroundCarve.Offset(pot, 0f, 0f, 10f), Is.EqualTo(-GeothermalVents.DishDepth(pot.Site)).Within(1e-5f));
			Assert.That(GeothermalGroundCarve.RunoffOffset(0f), Is.EqualTo(-GeothermalGroundCarve.RunoffDepth));
			Assert.That(GeothermalGroundCarve.RunoffOffset(GeothermalGroundCarve.RunoffHalfWidth), Is.EqualTo(0f));
		}

		[Test]
		public void WetSitesAreNeitherDugNorHeated_AndFootprintsAreHeatedNotDug()
		{
			var scene = new Rect(Field.Origin, Field.Origin, (Field.N - 1) * Field.Step, (Field.N - 1) * Field.Step);
			Field field = Field.Hills();
			List<GeothermalSite> sites = SitesFor(scene);
			GeothermalGroundCarve.Plan all = GeothermalGroundCarve.Make(sites, field.At, null, null, scene);
			GeothermalGroundCarve.Plan wet = GeothermalGroundCarve.Make(sites, field.At, (x, z) => x < 0f, null, scene);
			GeothermalGroundCarve.Plan kept = GeothermalGroundCarve.Make(sites, field.At, null, (x, z) => z < 0f, scene);
			Assert.That(wet.Dug.All(d => d.Site.Position.x >= 0f), Is.True);
			Assert.That(wet.HeatSources.Count(s => s.Shape == BiomeHeatSourceShape.Point), Is.EqualTo(sites.Count(s => s.Position.x >= 0f)));
			Assert.That(kept.Dug.All(d => d.Site.Position.y >= 0f), Is.True);
			Assert.That(kept.HeatSources.Count(s => s.Shape == BiomeHeatSourceShape.Point), Is.EqualTo(sites.Count));
			Assert.That(all.Dug.Count, Is.EqualTo(sites.Count));
			Assert.That(all.GreatSpout, Is.EqualTo(GeothermalVents.GreatSpoutIndex(sites)));
		}

		// ── Heat ──

		[Test]
		public void TheHeatMask_IsDeterministic_AndFallsAwayFromItsSites()
		{
			var geyser = new GeothermalSite { Position = new Vector2(10f, 10f), Kind = GeothermalKind.Geyser, Size = 1f };
			float reach = GeothermalVents.HeatReach(geyser);
			var sources = new List<BiomeHeatSource>
			{
				BiomeHeatSource.Point(geyser.Position, GeothermalVents.HeatPeak(geyser), reach),
				BiomeHeatSource.Line(new Vector2(40f, 10f), new Vector2(80f, 10f), 0.75f, 0.15f, 3f),
			};
			var scene = new Rect(-200f, -200f, 400f, 400f);
			BiomeHeatMask a = BiomeHeatMask.Bake(sources, scene);
			BiomeHeatMask b = BiomeHeatMask.Bake(sources, scene);
			Assert.That(a.Texels, Is.EqualTo(b.Texels));
			Assert.That(a.At(10f, 10f), Is.GreaterThan(0.9f));
			Assert.That(a.At(10f + 0.18f * reach, 10f), Is.GreaterThanOrEqualTo(0.6f), "bare hot ground near the vent");
			Assert.That(a.At(10f + 0.5f * reach, 10f), Is.InRange(0.15f, 0.35f), "a warm halo further out");
			Assert.That(a.At(10f + reach + 3f, 10f + reach), Is.EqualTo(0f));
			Assert.That(a.At(41f, 10f), Is.GreaterThan(a.At(78f, 10f)), "runoff cools downstream");
			Assert.That(a.At(60f, 14f), Is.EqualTo(0f).Within(0.01f));
			Assert.That(BiomeHeatMask.Bake(new List<BiomeHeatSource>(), scene).Any, Is.False);
			Assert.That(BiomeHeatMask.None.At(0f, 0f), Is.EqualTo(0f));
			// The geyser itself, by GeothermalVents.Heat.
			Assert.That(GeothermalVents.Heat(geyser, 0f), Is.EqualTo(1f));
			Assert.That(GeothermalVents.Heat(geyser, reach), Is.EqualTo(0f));
		}

		[Test]
		public void TheHeatBand_IsOpenAtItsEnds_AndPassesUnknownHeat()
		{
			Assert.That(BiomeHeatBand.Closes(new Vector2(0f, 1f)), Is.False);
			Assert.That(BiomeHeatBand.Weight(new Vector2(0f, 1f), 0.1f, 0f), Is.EqualTo(1f));
			Assert.That(BiomeHeatBand.Weight(new Vector2(0.65f, 1f), 0.1f, float.NaN), Is.EqualTo(1f));
			Assert.That(BiomeHeatBand.Weight(new Vector2(0.65f, 1f), 0.1f, 0.65f), Is.EqualTo(0.5f).Within(1e-5f), "half at the end itself");
			Assert.That(BiomeHeatBand.Weight(new Vector2(0.65f, 1f), 0.1f, 0.5f), Is.EqualTo(0f));
			Assert.That(BiomeHeatBand.Weight(new Vector2(0.65f, 1f), 0.1f, 1f), Is.EqualTo(1f));
			Assert.That(BiomeHeatBand.Weight(new Vector2(0.1f, 0.4f), 0f, 0.4f), Is.EqualTo(1f));
			Assert.That(BiomeHeatBand.Weight(new Vector2(0.1f, 0.4f), 0f, 0.41f), Is.EqualTo(0f));
			var rule = new PrefabSpawnRule();
			Assert.That(rule.useHeatBand, Is.False);
			Assert.That(rule.HeatWeight(0f), Is.EqualTo(1f));
		}

		[Test]
		public void HeatBands_AreAuthoredOnlyWhereNamed_AndMarksHold()
		{
			// A rule without a heat band hashes as before; its inert range does not count.
			var plain = new PrefabSpawnRule { ruleName = "Moss" };
			string before = BiomeArtAuthoring.Fingerprint(plain);
			plain.heatRange = new Vector2(0.3f, 0.7f);
			Assert.That(BiomeArtAuthoring.Fingerprint(plain), Is.EqualTo(before));
			plain.useHeatBand = true;
			Assert.That(BiomeArtAuthoring.Fingerprint(plain), Is.Not.EqualTo(before));
			// A spec layer's mark: unchanged without a heat band, the climate part and "|q" with one.
			var layer = new BiomeArtSpec.Layer { Family = "Sinter" };
			Assert.That(BiomeArtAuthoring.ClimateMark(layer), Is.EqualTo(string.Empty));
			layer.Heat(0.65f, 1f);
			Assert.That(BiomeArtAuthoring.ClimateMark(layer), Is.EqualTo("t-|h-|q6500,10000,1000"));
			layer.Heat(0f, 1f);
			Assert.That(layer.HeatBand, Is.Null, "both ends open is no band");
		}

		// ── Authoring and defaults ──

		[Test]
		public void TheGeyserBasin_EmitsSteam_WithMudPots_AndHotWater_AndNoOtherBiomeChanges()
		{
			Assert.That(BiomeArtSpec.Entries.Length, Is.EqualTo(83));
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				bool basin = entry.Biome == "Geyser Basin";
				Assert.That(entry.GeothermalShares.HasValue, Is.EqualTo(basin), entry.Biome);
				Assert.That(entry.WaterLook().Hot, Is.EqualTo(basin), entry.Biome);
				if (!basin)
				{
					Assert.That(entry.Emission.HasValue && entry.Emission.Value.Substance == "Steam", Is.False, entry.Biome);
				}
			}
			BiomeArtSpec.Entry geyser = BiomeArtSpec.For("Geyser Basin");
			Assert.That(geyser.Emission, Is.EqualTo(("Steam", 0.5f)));
			Assert.That(geyser.GeothermalShares.Value.Signature(), Is.EqualTo(Basin.Signature()));
			// Its colours stay today's: only the flag is new until its round tints it.
			BiomeWaterLook look = geyser.WaterLook();
			look.Hot = false;
			Assert.That(look.Signature(), Is.EqualTo(BiomeWaterLooks.Today.Signature()));
			// The weather content generator ships Steam (a vapour) and the authoring makes it when missing.
			string content = Code(WorldFolder + "/WeatherContentGenerator.cs");
			StringAssert.Contains("With(Vapour(\"Steam\")", content);
			StringAssert.Contains("public static WeatherSubstance EnsureShipped(string name)", content);
			StringAssert.Contains("WeatherContentGenerator.EnsureShipped(substance)", Code(WorldFolder + "/ProceduralArt/BiomeArtAuthoring.cs"));
		}

		[Test]
		public void Defaults_AreTodays_OnEveryTemplate()
		{
			Assert.That(BiomeGeothermalShares.Today.IsToday, Is.True);
			Assert.That(new BiomeGeothermalShares(0.25f, 0.4f, 0.2f, 0.15f).IsToday, Is.False);
			StringAssert.Contains("private BiomeGeothermalShares geothermal = BiomeGeothermalShares.Today;", Code("Assets/Scripts/Shared/Implementation/Tools/Biomes/BiomeTemplate.cs"));
			// Signatures marked before the flag existed hold.
			Assert.That(BiomeWaterLooks.Today.Signature().Contains("hot"), Is.False);
			BiomeWaterLook hot = BiomeWaterLooks.Today;
			hot.Hot = true;
			Assert.That(hot.Approximately(BiomeWaterLooks.Today), Is.False);
			Assert.That(BiomeWaterLook.Lerp(BiomeWaterLooks.Today, hot, 0.25f).Hot, Is.False);
			Assert.That(BiomeWaterLook.Lerp(BiomeWaterLooks.Today, hot, 0.75f).Hot, Is.True);
			foreach (string path in Directory.GetFiles(BiomeFolder, "*.asset"))
			{
				string text = File.ReadAllText(path);
				if (!text.Contains("m_Script: {fileID: 11500000, guid: b10de7e3a4c14e1f8a9d5c2b6f7e8a01"))
				{
					continue;
				}
				// Nothing authored yet: no split, no hot water, no heat band anywhere (Author biome art writes the basin's).
				Assert.That(text.Contains("\n  geothermal:"), Is.False, path);
				Assert.That(Regex.IsMatch(text, @"\n    Hot: 1"), Is.False, path);
				Assert.That(Regex.IsMatch(text, @"useHeatBand: 1"), Is.False, path);
			}
		}

		[Test]
		public void HotWater_NeverFreezes_AndSteams_AndColdWaterIsAsBefore()
		{
			var cold = new WaterTemperature.Reading { InlandC = 0f, InlandFrozen = true, SeaC = -1f, SeaOpen = 0.5f };
			WaterTemperature.Reading same = WaterTemperature.WithHot(cold, false);
			Assert.That(same.InlandC, Is.EqualTo(cold.InlandC));
			Assert.That(same.InlandFrozen, Is.True);
			Assert.That(same.InlandHot, Is.False);
			WaterTemperature.Reading hot = WaterTemperature.WithHot(cold, true);
			Assert.That(hot.InlandFrozen, Is.False);
			Assert.That(hot.InlandC, Is.GreaterThanOrEqualTo(WaterTemperature.HotWaterC));
			Assert.That(hot.SeaC, Is.EqualTo(cold.SeaC), "the sea is not a spring");
			Assert.That(SteamPhysics.SteamFogExcess(hot.InlandC, -5f, -8f, false), Is.GreaterThan(0f), "hot water smokes in cold air");
			Assert.That(WaterTemperature.HotShore(null, Vector2.zero), Is.False);
			StringAssert.Contains("bool frozen = moat == BiomeMoatWater.Frozen && !(biome != null && biome.WaterLook.Hot);",
				Code(WorldFolder + "/PointsOfInterest/Shapers/MoatShaper.cs"));
		}

		// ── Wiring ──

		[Test]
		public void TheStages_RunInOrder()
		{
			string generator = Code(WorldFolder + "/SceneGenerator.cs");
			int paint = generator.IndexOf("internal static bool PaintBiomes(", StringComparison.Ordinal);
			int restore = generator.IndexOf("GeothermalGroundCarve.Restore(scene, terrains, result.Notes);", paint, StringComparison.Ordinal);
			int field = generator.IndexOf("field ??= SceneBiomeField.Build(", paint, StringComparison.Ordinal);
			int plan = generator.IndexOf("poi?.PlanSites(scene, plan, terrains, field, water, result.Notes);", paint, StringComparison.Ordinal);
			int lay = generator.IndexOf("GeothermalGroundCarve.Lay(", paint, StringComparison.Ordinal);
			int splat = generator.IndexOf("BiomeSplatPainter.Paint(tiles, palette, field, options);", paint, StringComparison.Ordinal);
			int scatter = generator.IndexOf("TerrainScatter.Scatter(", paint, StringComparison.Ordinal);
			int write = generator.IndexOf("field.WriteTo(map);", lay, StringComparison.Ordinal);
			int heat = generator.IndexOf("map.SetHeat(heat);", paint, StringComparison.Ordinal);
			Assert.That(restore, Is.GreaterThan(paint).And.LessThan(field), "restored before the field reads the ground");
			Assert.That(lay, Is.GreaterThan(plan).And.LessThan(splat), "dug after the pads, before the splat");
			Assert.That(scatter, Is.GreaterThan(splat));
			Assert.That(heat, Is.GreaterThan(write), "the heat stored after the grid (which clears it)");
			StringAssert.Contains("HeatAt = (east, north) => heat.At(east, north),", generator);
			StringAssert.Contains("HeatAt = options.HeatAt }", generator);

			string repaint = Code(WorldFolder + "/Terrain/BiomeRepaintTool.cs");
			Assert.That(repaint.IndexOf("GeothermalGroundCarve.Restore(", StringComparison.Ordinal),
				Is.GreaterThan(repaint.IndexOf("CliffPlacer.Clear(scene);", StringComparison.Ordinal)).And.LessThan(repaint.IndexOf("SceneGenerator.LoadWater(", StringComparison.Ordinal)));
			string stage = Code(WorldFolder + "/PointsOfInterest/PointOfInterestStage.cs");
			Assert.That(stage.IndexOf("GeothermalGroundCarve.WriteWater(", StringComparison.Ordinal),
				Is.GreaterThan(stage.IndexOf("PointOfInterestPaddies.ClearWater(TerrainFolder, Request.SceneName);", StringComparison.Ordinal)));

			string presenter = Code("Assets/Scripts/Client/World/Weather/Sky/GeothermalSteamPresenter.cs");
			StringAssert.Contains("lake > ground + GeothermalVents.PoolDepth(site) + OwnPoolSlack", presenter);
			StringAssert.Contains("GeothermalVents.Plop(site, motionSeconds, lane)", presenter);
			StringAssert.Contains("WithHot(reading, HotShore(", Code("Assets/Scripts/Shared/Implementation/Weather/WaterTemperature.cs"));
			string painter = Code(WorldFolder + "/Terrain/BiomeSplatPainter.cs");
			StringAssert.Contains("strength *= source.HeatWeight(heat);", painter);
		}

		/// <summary>Which scenes hold the geyser basin (whose sites appear on the next re-author + repaint): printed for the report.</summary>
		[Test]
		public void TheScenesWithAGeyserBasin_AreListed()
		{
			int id = (nameof(BiomeTemplate) + "Geyser Basin").GetDeterministicHashCode();
			string hex = string.Concat(BitConverter.GetBytes(id).Select(b => b.ToString("x2")));
			var found = new List<string>();
			foreach (string map in Directory.GetFiles("Assets/Scenes/WorldScene", "* Biome Map.asset", SearchOption.AllDirectories))
			{
				Match ids = Regex.Match(File.ReadAllText(map), @"\n  BiomeIDs: ([0-9a-f]+)");
				string all = ids.Groups[1].Value;
				int cells = 0;
				for (int i = 0; i + 8 <= all.Length; i += 8)
				{
					if (string.CompareOrdinal(all, i, hex, 0, 8) == 0)
					{
						cells++;
					}
				}
				if (cells > 0)
				{
					found.Add($"{Path.GetFileNameWithoutExtension(map)} ({cells} cell(s) of {all.Length / 8})");
				}
			}
			Console.WriteLine("Geyser Basin in: " + (found.Count > 0 ? string.Join("; ", found) : "no scene"));
			Assert.Pass();
		}
	}
}
