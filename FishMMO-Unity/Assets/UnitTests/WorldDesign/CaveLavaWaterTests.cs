using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Weather;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Inland lava (2026-10-20, a lava tube's rivers and pits): the cave's lava written into its scene's hydrology as lakes and
	/// rivers flagged Lava (<see cref="CaveLavaWater"/>), answered by the simulation's water as <see cref="WaterBody.Lava"/>,
	/// baked into the NavMesh's "Lava" area, and drawn by the inland lava shader. Pure: nothing here needs the engine's
	/// native side (the scene-object half is <see cref="InlandLavaSceneTests"/>).
	/// </summary>
	[TestFixture]
	public class CaveLavaWaterTests
	{
		private const string WaterFolder = "Assets/Plugins/FishMMO Water/";

		[TearDown]
		public void TearDown() => WaterQuery.Clear();

		// ── Ids ───────────────────────────────────────────────────

		[Test]
		public void LavaIdsLieInTheGrottoRangeApartFromPoolsPaddiesAndMoats()
		{
			foreach (int site in new[] { 0, 1, 0x00ABCDEF, unchecked((int)0xDEADBEEF), int.MaxValue, -7 })
			{
				var seen = new HashSet<int>();
				for (int body = 0; body < CaveLavaWater.MaxBodies; body++)
				{
					int id = CaveLavaWater.IdOf(site, body);
					Assert.That(id, Is.GreaterThanOrEqualTo(CaveShaper.PoolIdBase).And.LessThan(MoatShaper.LakeIdBase), $"site {site} body {body}");
					Assert.That(CaveLavaWater.IsCaveLava(id) && CaveLavaWater.OfSite(id, site), Is.True);
					Assert.That(CaveLavaWater.OfSite(id, site + 1), Is.False);
					Assert.That(FloatingPlants.IsGrottoPool(id), Is.True, "no floating plants on lava");
					Assert.That(PointOfInterestPaddies.IsPaddy(id) || FloatingPlants.IsMoat(id), Is.False);
					Assert.That(seen.Add(id), Is.True, "two bodies of one site share an id");
				}
				// The site's grotto pool keeps its own id.
				int pool = CaveShaper.PoolIdBase | (site & 0x00FFFFFF);
				Assert.That(CaveLavaWater.IsCaveLava(pool), Is.False);
				Assert.That(seen.Contains(pool), Is.False);
			}
		}

		// ── Lakes and rivers ──────────────────────────────────────

		[Test]
		public void ALavaLakeCoversItsDiscWhereTheCaveIsOpenUnderItsSurface()
		{
			var centre = new Vector3(10f, -30f, 20f);
			// A wall at x = 12: rock east of it.
			SceneHydrology.Lake lake = CaveLavaWater.Lake(42, centre, 4f, p => p.x < 12f);
			Assert.That(lake, Is.Not.Null);
			Assert.That(lake.Lava, Is.True);
			Assert.That(lake.Id, Is.EqualTo(42));
			Assert.That(lake.Level, Is.EqualTo(-30f));
			Assert.That(lake.MaskCell, Is.EqualTo(CaveLavaWater.LakeMaskCell));
			Assert.That(lake.Covers(10f, 20f), Is.True, "its centre");
			Assert.That(lake.Covers(8f, 17.5f), Is.True, "inside the disc");
			Assert.That(lake.Covers(13f, 20f), Is.False, "past the wall");
			Assert.That(lake.Covers(10f, 25f), Is.False, "past its radius");
			Assert.That(lake.Seeds.Length, Is.EqualTo(1));
			Assert.That(lake.Covers(lake.Seeds[0].x, lake.Seeds[0].y), Is.True, "its seed is in its lava");
			Assert.That(CaveLavaWater.Lake(1, centre, 4f, p => false), Is.Null, "no open cell, no lake");
			// The cave must be open just under the surface: a floor standing over the level holds no lava.
			Assert.That(CaveLavaWater.Lake(1, new Vector3(0f, 5f, 0f), 3f, p => p.y > 5.5f), Is.Null);
		}

		[Test]
		public void ALavaRiverRunsDownItsSlopeNeverRisingAndFallsWhereItIsSteep()
		{
			var points = new[]
			{
				new Vector3(0f, 10f, 0f), new Vector3(10f, 9.8f, 0f), new Vector3(20f, 10.5f, 0f), new Vector3(22f, 6f, 0f), new Vector3(40f, 5.95f, 0f),
				new Vector3(60f, 5.9f, 0f),
			};
			SceneHydrology.River river = CaveLavaWater.River(7, points, 3f, false);
			Assert.That(river.Lava && river.Perennial, Is.True);
			Assert.That(river.Start, Is.EqualTo(SceneHydrology.End.Source));
			Assert.That(river.Finish, Is.EqualTo(SceneHydrology.End.Sink));
			for (int i = 1; i < river.Points.Length; i++)
			{
				Assert.That(river.Points[i].y, Is.LessThanOrEqualTo(river.Points[i - 1].y), $"point {i} rises");
			}
			Assert.That(river.Points[2].y, Is.EqualTo(9.8f), "a rise is held at the level before it");
			Assert.That(river.Reach[0], Is.EqualTo(0), "2% is a run");
			Assert.That(river.Reach[2], Is.EqualTo(4));
			Assert.That(river.Reach[3], Is.EqualTo(4));
			Assert.That(river.Reach[5], Is.EqualTo(0));
			for (int i = 0; i < river.Points.Length; i++)
			{
				Assert.That(river.Speed[i], Is.InRange(CaveLavaWater.MinSpeed, CaveLavaWater.MaxSpeed));
				Assert.That(river.Width[i], Is.EqualTo(3f));
				Assert.That(river.Bed[i], Is.EqualTo(river.Points[i].y - CaveLavaWater.RiverDepth).Within(1e-5f));
			}
			Assert.That(river.Speed[3], Is.GreaterThan(river.Speed[5]), "faster down the fall than on the flat");

			SceneHydrology.River fall = CaveLavaWater.River(8, new[] { new Vector3(0f, 4f, 0f), new Vector3(2f, 3.9f, 0f) }, 2f, true);
			Assert.That(fall.Reach, Is.All.EqualTo(4), "a fall falls at every point");
			Assert.That(CaveLavaWater.River(9, new[] { Vector3.zero }, 2f, false), Is.Null);
		}

		[Test]
		public void ARiverRunsIntoTheLakeItsEndLiesInAndOutOfTheOneItsStartLiesIn()
		{
			SceneHydrology.Lake lake = CaveLavaWater.Lake(100, new Vector3(30f, 5f, 0f), 5f, null);
			SceneHydrology.River into = CaveLavaWater.River(9, new[] { new Vector3(0f, 9f, 0f), new Vector3(28f, 5.6f, 0f) }, 2f, false, new[] { lake });
			Assert.That(into.Finish, Is.EqualTo(SceneHydrology.End.Lake));
			Assert.That(into.EndLake, Is.EqualTo(100));
			Assert.That(into.Points[1].y, Is.EqualTo(5f), "it ends at the lake's level");
			SceneHydrology.River outOf = CaveLavaWater.River(10, new[] { new Vector3(31f, 5.2f, 1f), new Vector3(50f, 4f, 0f) }, 2f, false, new[] { lake });
			Assert.That(outOf.Start, Is.EqualTo(SceneHydrology.End.Lake));
			Assert.That(outOf.StartLake, Is.EqualTo(100));
			Assert.That(outOf.Points[0].y, Is.EqualTo(5f), "it leaves at the lake's level");
			Assert.That(outOf.Finish, Is.EqualTo(SceneHydrology.End.Sink));
		}

		// ── The writer, on a real lava tube ───────────────────────

		/// <summary>A foot at 70 m, a face rising 60 m over 30 m, a plateau behind (CaveLifeBakeTests' escarpment).</summary>
		private static Func<float, float, float> Escarpment()
			=> (x, z) =>
			{
				float t = Mathf.Clamp01(z / 30f);
				return 70f + 60f * (t * t * (3f - 2f * t)) - 0.04f * Mathf.Max(0f, -z);
			};

		private static CaveSystem LavaTube(int seed)
		{
			CaveGround ground = CaveGround.Sample(Escarpment(), -560f, -560f, 560f, 560f, 2f);
			CaveSystem system = CaveSystem.Plan(CaveForm.LavaTube, 1, new Vector3(0f, 0f, -1f), 180f, seed * 7919, seed, ground, 0f, out string problem, float.NaN);
			Assert.That(system, Is.Not.Null, $"the lava tube was refused: {problem}");
			return system;
		}

		/// <summary>A plan by hand: a lake at the trunk's sixth node, a river down its nodes before it into the lake, and a fall.</summary>
		private static CaveLavaPlan PlanFor(CaveSystem system)
		{
			List<CaveNode> nodes = system.Passages[0].Nodes;
			Assert.That(nodes.Count, Is.GreaterThan(6));
			var plan = new CaveLavaPlan { Active = true };
			CaveNode pit = nodes[6];
			plan.Bodies.Add(new CaveLavaBody
			{
				Kind = CaveLavaKind.Lake,
				Path = new[] { new Vector3(pit.Centre.x, pit.Floor + 0.5f, pit.Centre.z) },
				Width = Mathf.Max(1.5f, pit.Radius * 0.8f),
			});
			var run = new Vector3[6];
			for (int i = 0; i < 6; i++)
			{
				run[i] = new Vector3(nodes[i + 1].Centre.x, nodes[i + 1].Floor + 0.4f, nodes[i + 1].Centre.z);
			}
			run[5] = new Vector3(pit.Centre.x, pit.Floor + 0.5f, pit.Centre.z);
			plan.Bodies.Add(new CaveLavaBody { Kind = CaveLavaKind.River, Path = run, Width = 1.5f });
			plan.Bodies.Add(new CaveLavaBody
			{
				Kind = CaveLavaKind.Fall,
				Path = new[] { run[0] + new Vector3(0f, 4f, 0f), run[0] + new Vector3(1.5f, 0f, 0f) },
				Width = 1f,
			});
			return plan;
		}

		[Test]
		public void TheWriterReplacesASitesLavaAndLeavesEveryOtherBodyOfWater()
		{
			CaveSystem system = LavaTube(4);
			CaveLavaPlan plan = PlanFor(system);
			const int site = 0x12345678;
			var lakes = new List<SceneHydrology.Lake>
			{
				new SceneHydrology.Lake { Id = 5 },
				new SceneHydrology.Lake { Id = CaveShaper.PoolIdBase | (site & 0x00FFFFFF) },
				new SceneHydrology.Lake { Id = CaveLavaWater.IdOf(site + 1, 0), Lava = true },
			};
			var rivers = new List<SceneHydrology.River> { new SceneHydrology.River { Id = 3 }, new SceneHydrology.River { Id = CaveLavaWater.IdOf(site + 1, 1), Lava = true } };

			int written = CaveLavaWater.Write(lakes, rivers, site, system, plan);
			Assert.That(written, Is.EqualTo(3), "the lake, the river and the fall");
			List<SceneHydrology.Lake> mine = lakes.FindAll(l => CaveLavaWater.OfSite(l.Id, site));
			List<SceneHydrology.River> mineRivers = rivers.FindAll(r => CaveLavaWater.OfSite(r.Id, site));
			Assert.That(mine.Count, Is.EqualTo(1));
			Assert.That(mineRivers.Count, Is.EqualTo(2));
			Assert.That(mine[0].Lava && mineRivers.TrueForAll(r => r.Lava), Is.True);
			Assert.That(mine[0].Id, Is.EqualTo(CaveLavaWater.IdOf(site, 0)), "a body's id is its index in the plan");
			Assert.That(mine[0].Level, Is.EqualTo(system.Origin.y + plan.Bodies[0].Path[0].y).Within(1e-4f), "scene metres: the system moved to its origin");
			SceneHydrology.River river = rivers.Find(r => r.Id == CaveLavaWater.IdOf(site, 1));
			Assert.That(river.Finish, Is.EqualTo(SceneHydrology.End.Lake), "the river runs into the pit's lava");
			Assert.That(river.EndLake, Is.EqualTo(mine[0].Id));
			Assert.That(rivers.Find(r => r.Id == CaveLavaWater.IdOf(site, 2)).Reach, Is.All.EqualTo(4));
			Assert.That(lakes.Count, Is.EqualTo(4), "everyone else's water stays");
			Assert.That(rivers.Count, Is.EqualTo(4));

			// Again: replaced, never doubled; the same bodies, the same numbers.
			Assert.That(CaveLavaWater.Write(lakes, rivers, site, system, plan), Is.EqualTo(3));
			Assert.That(lakes.Count, Is.EqualTo(4));
			Assert.That(rivers.Count, Is.EqualTo(4));
			Assert.That(lakes.Find(l => l.Id == CaveLavaWater.IdOf(site, 0)).Mask, Is.EqualTo(mine[0].Mask));

			// Cooled: the site's lava goes, and nothing else.
			Assert.That(CaveLavaWater.Write(lakes, rivers, site, system, CaveLavaPlan.Cooled), Is.EqualTo(0));
			Assert.That(lakes.FindAll(l => CaveLavaWater.OfSite(l.Id, site)).Count + rivers.FindAll(r => CaveLavaWater.OfSite(r.Id, site)).Count, Is.EqualTo(0));
			Assert.That(lakes.Count, Is.EqualTo(3));
			Assert.That(rivers.Count, Is.EqualTo(2));
		}

		[Test]
		public void APitsLavaStaysInTheCave()
		{
			CaveSystem system = LavaTube(4);
			CaveLavaPlan plan = PlanFor(system);
			var lakes = new List<SceneHydrology.Lake>();
			CaveLavaWater.Write(lakes, new List<SceneHydrology.River>(), 77, system, plan);
			SceneHydrology.Lake lake = lakes[0];
			int cells = 0;
			for (int z = 0; z < lake.MaskHeight; z++)
			{
				for (int x = 0; x < lake.MaskWidth; x++)
				{
					float east = lake.MaskOrigin.x + (x + 0.5f) * lake.MaskCell, north = lake.MaskOrigin.y + (z + 0.5f) * lake.MaskCell;
					if (!lake.Covers(east, north))
					{
						continue;
					}
					cells++;
					bool open = false;
					for (float drop = 0.05f; drop <= 1.001f && !open; drop += 0.475f)
					{
						open = system.Void(new Vector3(east, lake.Level - drop, north) - system.Origin) < 0f;
					}
					Assert.That(open, Is.True, $"lava in the rock at ({east}, {north})");
				}
			}
			Assert.That(cells, Is.GreaterThan(0));
		}

		// ── The water the simulation reads ────────────────────────

		/// <summary>A rectangle of lava or water standing at one height.</summary>
		private sealed class Pit : SurfaceWater.ILavaAreaSource
		{
			public Rect Area;
			public float Height;
			public bool Lava;

			public float Level => float.NegativeInfinity;
			public float WaveHeight => 0f;
			public bool IsUnder(Vector3 point) => TryGetSurface(point.x, point.z, out float level) && point.y < level;
			public bool TryGetSurface(float x, float z, out float level) => TryGetSurface(x, z, out level, out _);
			public bool TryGetSurface(float x, float z, out float level, out bool lava)
			{
				bool here = Area.Contains(new Vector2(x, z));
				level = here ? Height : float.NegativeInfinity;
				lava = here && Lava;
				return here;
			}
			public Vector2 CurrentAt(float x, float z) => Area.Contains(new Vector2(x, z)) ? new Vector2(0.5f, 0f) : Vector2.zero;
		}

		[Test]
		public void InlandLavaIsTheLavaLiquidToTheSimulation()
		{
			Scene scene = default;
			var pit = new Pit { Area = new Rect(0f, 0f, 10f, 10f), Height = -30f, Lava = true };
			WaterQuery.RegisterInland(scene, pit);
			WaterSample sample = WaterQuery.Sample(scene, 5f, 5f, 0u);
			Assert.That(sample.Body, Is.EqualTo(WaterBody.Lava), "flowing, it is still lava, not a river");
			Assert.That(sample.Surface, Is.EqualTo(-30f));
			Assert.That(sample.DepthOf(-31f), Is.EqualTo(1f));
			Assert.That(WaterQuery.Sample(scene, 15f, 5f, 0u).Body, Is.EqualTo(WaterBody.None));

			// The highest surface over a place answers: a pond over the tube is the pond.
			var pond = new Pit { Area = new Rect(0f, 0f, 10f, 10f), Height = 12f, Lava = false };
			WaterQuery.RegisterInland(scene, pond);
			Assert.That(WaterQuery.Sample(scene, 5f, 5f, 0u).Body, Is.EqualTo(WaterBody.River));
			pond.Height = -40f;
			Assert.That(WaterQuery.Sample(scene, 5f, 5f, 0u).Body, Is.EqualTo(WaterBody.Lava));
		}

		[Test]
		public void TheWeathersViewTellsLavaFromWater()
		{
			var pit = new Pit { Area = new Rect(0f, 0f, 10f, 10f), Height = -30f, Lava = true };
			SurfaceWater.Register(pit);
			try
			{
				Assert.That(SurfaceWater.TryGetInlandSurfaceAt(5f, 5f, out float level, out bool lava), Is.True);
				Assert.That(level, Is.EqualTo(-30f));
				Assert.That(lava, Is.True);
				Assert.That(SurfaceWater.TryGetInlandSurfaceAt(50f, 5f, out _, out lava), Is.False);
				Assert.That(lava, Is.False);
			}
			finally
			{
				SurfaceWater.Unregister(pit);
			}
		}

		// ── The NavMesh ───────────────────────────────────────────

		[Test]
		public void TheNavMeshMarksTheBedUnderTheLavaAndNothingStandingOverIt()
		{
			SceneHydrology.Lake lake = CaveLavaWater.Lake(1, new Vector3(0f, -20f, 0f), 3f, null);
			SceneHydrology.River river = CaveLavaWater.River(2, new[] { new Vector3(10f, -18f, 0f), new Vector3(20f, -19f, 3f) }, 2f, false);
			var water = new SceneHydrology.Lake { Id = 3, Level = 0f, Mask = new byte[] { 1 }, MaskWidth = 1, MaskHeight = 1, MaskCell = 4f };
			var into = new List<NavMeshBuildSource>();
			int boxes = SceneNavMeshBaker.LavaModifiers(new[] { lake, water }, new[] { river }, 7, into);
			Assert.That(boxes, Is.EqualTo(into.Count).And.GreaterThan(1));
			foreach (NavMeshBuildSource box in into)
			{
				Assert.That(box.shape, Is.EqualTo(NavMeshBuildSourceShape.ModifierBox));
				Assert.That(box.area, Is.EqualTo(7));
			}
			// The lake's: from under its deepest bed to just under its surface.
			NavMeshBuildSource first = into[0];
			Vector3 at = first.transform.GetColumn(3);
			Assert.That(at.y + first.size.y * 0.5f, Is.EqualTo(-20f - SceneNavMeshBaker.BoxTopUnderSurfaceMetres).Within(1e-4f));
			Assert.That(at.y - first.size.y * 0.5f, Is.EqualTo(-20f - CaveLavaWater.MaxBedDepth - 1f).Within(1e-4f));
			// The river's: its bed half way along inside, a man standing on its surface not.
			NavMeshBuildSource last = into[into.Count - 1];
			Matrix4x4 toBox = last.transform.inverse;
			Vector3 middle = (river.Points[0] + river.Points[1]) * 0.5f;
			Assert.That(Inside(toBox.MultiplyPoint3x4(middle - new Vector3(0f, CaveLavaWater.RiverDepth, 0f)), last.size), Is.True, "the bed");
			Assert.That(Inside(toBox.MultiplyPoint3x4(middle + new Vector3(0f, 0.2f, 0f)), last.size), Is.False, "over the surface");
			// No water is marked lava.
			Assert.That(SceneNavMeshBaker.LavaModifiers(new[] { water }, new SceneHydrology.River[0], 7, new List<NavMeshBuildSource>()), Is.EqualTo(0));
		}

		private static bool Inside(Vector3 local, Vector3 size)
			=> Mathf.Abs(local.x) <= size.x * 0.5f && Mathf.Abs(local.y) <= size.y * 0.5f && Mathf.Abs(local.z) <= size.z * 0.5f;

		// ── The look ──────────────────────────────────────────────

		private static HashSet<string> Properties(string shader)
		{
			int start = shader.IndexOf("Properties", StringComparison.Ordinal);
			int open = shader.IndexOf('{', start), depth = 0, end = open;
			for (; end < shader.Length; end++)
			{
				if (shader[end] == '{')
				{
					depth++;
				}
				else if (shader[end] == '}' && --depth == 0)
				{
					break;
				}
			}
			var names = new HashSet<string>();
			foreach (Match match in Regex.Matches(shader.Substring(open, end - open), @"^\s*(?:\[[^\]]*\]\s*)*(_\w+)\s*\(", RegexOptions.Multiline))
			{
				names.Add(match.Groups[1].Value);
			}
			return names;
		}

		[Test]
		public void TheInlandLavaIsTheLavasSurfaceWithItsOwnSwitch()
		{
			string lava = File.ReadAllText(WaterFolder + "Shaders/FishLava.shader");
			string inland = File.ReadAllText(WaterFolder + "Shaders/FishInlandLava.shader");
			Assert.That(Properties(inland), Is.EquivalentTo(Properties(lava)), "one UnityPerMaterial: the same properties, one for one");
			Assert.That(inland, Does.Contain("Shader \"FishMMO/Water/Inland Lava\""));
			MatchCollection passes = Regex.Matches(inland, @"HLSLPROGRAM(.*?)ENDHLSL", RegexOptions.Singleline);
			Assert.That(passes.Count, Is.EqualTo(Regex.Matches(lava, "HLSLPROGRAM").Count));
			foreach (Match pass in passes)
			{
				string body = pass.Groups[1].Value;
				int define = body.IndexOf("#define FISH_LAVA_INLAND", StringComparison.Ordinal);
				int include = body.IndexOf("#include \"FishLavaPasses.hlsl\"", StringComparison.Ordinal);
				Assert.That(define >= 0 && include > define, Is.True, "every pass switches to the inland path before the passes are included");
				Assert.That(body, Does.Contain("#pragma use_dxc vulkan"));
			}
		}

		[Test]
		public void TheInlandLavaMaterialIsTheInlandShadersWithTheBakedCrust()
		{
			string material = File.ReadAllText(WaterFolder + "Materials/InlandLava.mat");
			string shaderGuid = Regex.Match(File.ReadAllText(WaterFolder + "Shaders/FishInlandLava.shader.meta"), @"guid: (\w+)").Groups[1].Value;
			Assert.That(material, Does.Contain("guid: " + shaderGuid));
			foreach (string map in new[] { "LavaCrustNormal", "LavaCrustMask" })
			{
				string guid = Regex.Match(File.ReadAllText(WaterFolder + "Textures/" + map + ".png.meta"), @"guid: (\w+)").Groups[1].Value;
				Assert.That(material, Does.Contain("guid: " + guid), map);
			}
			Assert.That(File.ReadAllText("Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/SceneGenerator.cs"),
				Does.Contain("Materials/InlandLava.mat"), "the scene's Inland Water object is given it");
		}

		[Test]
		public void TheSkyReachesTheLavaOnlyNearTheGround()
		{
			Assert.That(FishMMO.Water.InlandWaterRenderer.OpenSkyUnder(0f), Is.EqualTo(1f));
			Assert.That(FishMMO.Water.InlandWaterRenderer.OpenSkyUnder(6f), Is.EqualTo(1f));
			Assert.That(FishMMO.Water.InlandWaterRenderer.OpenSkyUnder(13f), Is.InRange(0.4f, 0.6f));
			Assert.That(FishMMO.Water.InlandWaterRenderer.OpenSkyUnder(20f), Is.EqualTo(0f));
			Assert.That(FishMMO.Water.InlandWaterRenderer.OpenSkyUnder(80f), Is.EqualTo(0f));
		}

		[Test]
		public void AFallIsPouredAndTheRiverAroundItKeepsItsRibbon()
		{
			var points = new[]
			{
				new Vector3(0f, 10f, 0f), new Vector3(8f, 9.9f, 0f), new Vector3(9.5f, 5f, 0f), new Vector3(20f, 4.9f, 0f), new Vector3(30f, 4.8f, 0f),
			};
			SceneHydrology.River river = CaveLavaWater.River(11, points, 2f, false);
			List<Vector2Int> spans = FishMMO.Water.InlandWaterRenderer.PourSpans(river);
			Assert.That(spans.Count, Is.EqualTo(1));
			Assert.That(river.Points[spans[0].x].y - river.Points[spans[0].y].y, Is.GreaterThan(4f), "the span runs from above the lip to below the fall");
			var positions = new List<Vector3>();
			var frames = new List<Vector2>();
			var kinds = new List<Vector2>();
			var scales = new List<float>();
			var colours = new List<Color32>();
			var indices = new List<int>();
			FishMMO.Water.InlandWaterRenderer.BuildPour(river, spans[0].x, spans[0].y, 8f, positions, frames, kinds, scales, colours, indices);
			Assert.That(indices.Count, Is.GreaterThan(0));
			Assert.That(indices.Count % 3, Is.EqualTo(0));
			Assert.That(positions.Count, Is.EqualTo(frames.Count).And.EqualTo(kinds.Count).And.EqualTo(scales.Count).And.EqualTo(colours.Count));
			foreach (int i in indices)
			{
				Assert.That(i, Is.InRange(0, positions.Count - 1));
			}
			foreach (Vector2 kind in kinds)
			{
				Assert.That(kind.x, Is.EqualTo(FishMMO.Water.InlandWaterRenderer.PourKind));
			}
			// From the lip's height to the pool's, never above the lip by more than its rounded front, never under the pool.
			float highest = float.NegativeInfinity, lowest = float.PositiveInfinity;
			foreach (Vector3 p in positions)
			{
				highest = Mathf.Max(highest, p.y);
				lowest = Mathf.Min(lowest, p.y);
			}
			Assert.That(highest, Is.InRange(river.Points[spans[0].x].y - 0.01f, river.Points[spans[0].x].y + 0.5f));
			Assert.That(lowest, Is.InRange(river.Points[spans[0].y].y - 0.01f, river.Points[spans[0].y].y + 0.2f));
			// Down the run, its frame only grows.
			Assert.That(frames[frames.Count - 1].x, Is.GreaterThan(frames[0].x));
			// A gentle river has no pour.
			Assert.That(FishMMO.Water.InlandWaterRenderer.PourSpans(CaveLavaWater.River(12, new[] { Vector3.zero, new Vector3(10f, -0.1f, 0f) }, 2f, false)), Is.Empty);
			// A fall body is one pour from its lip to its landing.
			SceneHydrology.River fall = CaveLavaWater.River(13, new[] { new Vector3(0f, 6f, 0f), new Vector3(1.5f, 1f, 0f) }, 1.5f, true);
			Assert.That(FishMMO.Water.InlandWaterRenderer.PourSpans(fall), Is.EqualTo(new List<Vector2Int> { new Vector2Int(0, 1) }));
		}

		[Test]
		public void ANarrowChannelsPlatesAreSmaller()
		{
			Assert.That(FishMMO.Water.InlandWaterRenderer.RiverPlateScale(2f), Is.LessThan(FishMMO.Water.InlandWaterRenderer.RiverPlateScale(4f)));
			Assert.That(FishMMO.Water.InlandWaterRenderer.RiverPlateScale(40f), Is.EqualTo(1f));
			Assert.That(FishMMO.Water.InlandWaterRenderer.RiverPlateScale(0.5f), Is.EqualTo(0.3f));
			SceneHydrology.Lake pit = CaveLavaWater.Lake(1, Vector3.zero, 2f, null), hall = CaveLavaWater.Lake(2, Vector3.zero, 15f, null);
			Assert.That(FishMMO.Water.InlandWaterRenderer.LakePlateScale(pit), Is.LessThan(FishMMO.Water.InlandWaterRenderer.LakePlateScale(hall)));
		}

		[Test]
		public void OldBodiesAreWaterUntilFlagged()
		{
			Assert.That(new SceneHydrology.Lake().Lava, Is.False);
			Assert.That(new SceneHydrology.River().Lava, Is.False);
			// Serialized fields: public, and not kept out of the asset.
			foreach (Type type in new[] { typeof(SceneHydrology.Lake), typeof(SceneHydrology.River) })
			{
				System.Reflection.FieldInfo field = type.GetField("Lava");
				Assert.That(field != null && field.FieldType == typeof(bool) && !field.IsNotSerialized, Is.True, type.Name);
			}
		}
	}

	/// <summary>
	/// The scene-object half of inland lava: a scene's <see cref="SceneWaterBodies"/> over a hydrology with lava in it, and
	/// the flag through Unity's serializer. Needs the engine (a ScriptableObject, a GameObject): run in the editor.
	/// </summary>
	[TestFixture]
	public class InlandLavaSceneTests
	{
		private GameObject host;
		private SceneHydrology hydrology;

		[TearDown]
		public void TearDown()
		{
			WaterQuery.Clear();
			if (host != null)
			{
				UnityEngine.Object.DestroyImmediate(host);
			}
			if (hydrology != null)
			{
				UnityEngine.Object.DestroyImmediate(hydrology);
			}
		}

		[Test]
		public void TheLavaFlagSurvivesTheSerializerAndOldDataReadsAsWater()
		{
			var river = new SceneHydrology.River { Id = 4, Lava = true };
			Assert.That(JsonUtility.FromJson<SceneHydrology.River>(JsonUtility.ToJson(river)).Lava, Is.True);
			var lake = new SceneHydrology.Lake { Id = 5, Lava = true };
			Assert.That(JsonUtility.FromJson<SceneHydrology.Lake>(JsonUtility.ToJson(lake)).Lava, Is.True);
			// Written before the flag: no Lava field at all.
			Assert.That(JsonUtility.FromJson<SceneHydrology.Lake>("{\"Id\":5,\"Level\":3.0}").Lava, Is.False);
		}

		[Test]
		public void ASceneAnswersLavaWhereItsLavaStands()
		{
			hydrology = ScriptableObject.CreateInstance<SceneHydrology>();
			hydrology.Lakes.Add(CaveLavaWater.Lake(CaveLavaWater.IdOf(9, 0), new Vector3(0f, -30f, 0f), 4f, null));
			hydrology.Rivers.Add(CaveLavaWater.River(CaveLavaWater.IdOf(9, 1), new[] { new Vector3(10f, -28f, 0f), new Vector3(40f, -29f, 0f) }, 2f, false));
			host = new GameObject("Inland Lava Test");
			SceneWaterBodies bodies = host.AddComponent<SceneWaterBodies>();
			bodies.Hydrology = hydrology;
			bodies.Build();
			Assert.That(bodies.HasLava, Is.True);
			Assert.That(bodies.TryGetSurface(0f, 0f, out float level, out bool lava) && lava, Is.True);
			Assert.That(level, Is.EqualTo(-30f));
			Assert.That(bodies.TryGetSurface(25f, 0f, out level, out lava) && lava, Is.True);
			Assert.That(bodies.Reaches(new Rect(-2f, -2f, 4f, 4f)), Is.True);
			Assert.That(bodies.Reaches(new Rect(-2f, -2f, 4f, 4f), false), Is.False, "lava is not water to a sealed view");
			WaterQuery.RegisterInland(host.scene, bodies);
			Assert.That(WaterQuery.Sample(host.scene, 25f, 0f, 0u).Body, Is.EqualTo(WaterBody.Lava));
			Assert.That(WaterQuery.Sample(host.scene, 0f, 0f, 0u).Body, Is.EqualTo(WaterBody.Lava));
		}

		[Test]
		public void ARiverOnTheHillIsWaterOverTheTubesLavaUnderIt()
		{
			hydrology = ScriptableObject.CreateInstance<SceneHydrology>();
			// The tube's lava runs east 40 m under the hill; a river on the hill crosses over it running north, its centre farther off.
			hydrology.Rivers.Add(CaveLavaWater.River(CaveLavaWater.IdOf(9, 0), new[] { new Vector3(-20f, -28f, 0f), new Vector3(20f, -29f, 0f) }, 4f, false));
			hydrology.Rivers.Add(new SceneHydrology.River
			{
				Id = 1,
				Points = new[] { new Vector3(1.5f, 12f, -30f), new Vector3(1.5f, 11.5f, 30f) },
				Width = new[] { 6f, 6f },
				Speed = new[] { 1f, 1f },
			});
			host = new GameObject("Inland Lava Test");
			SceneWaterBodies bodies = host.AddComponent<SceneWaterBodies>();
			bodies.Hydrology = hydrology;
			bodies.Build();
			// At (0, 0) the lava's line is nearer than the river's, and still the river on the hill, the higher, answers.
			Assert.That(bodies.TryGetSurface(0f, 0f, out float level, out bool lava), Is.True);
			Assert.That(lava, Is.False);
			Assert.That(level, Is.GreaterThan(11f));
			// Each liquid's current is its own: the river runs north, the lava east.
			Assert.That(bodies.FlowAt(0f, 0f, out _, out _).y, Is.GreaterThan(0.5f));
			Assert.That(bodies.FlowAt(0f, 0f, out _, out _).x, Is.EqualTo(0f).Within(1e-4f));
			Assert.That(bodies.FlowAt(0f, 0f, out _, out _, true).x, Is.GreaterThan(0.1f));
			// Past the river's banks the tube's lava is what stands there.
			Assert.That(bodies.TryGetSurface(15f, 0f, out level, out lava) && lava, Is.True);
		}
	}
}
