using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Decision 27 of the biome suite (2026-10-18, phase I2i): plants floating on still fresh water (duckweed, water-lilies, water
	/// hyacinth, frogbit), boardwalks raised along a biome's ways and between its houses, and flooded paddy fields behind earth
	/// bunds. Every default is exactly what was cut before: no biome floats anything, raises any way or floods any field.
	/// </summary>
	public class BiomeStillWaterBoardwalkPaddyTests
	{
		private const string BiomeFolder = "Assets/Templates/Entity/Biomes";
		private const string World = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World";
		private const string GeneratorSource = World + "/SceneGenerator.cs";
		private const string RepaintSource = World + "/Terrain/BiomeRepaintTool.cs";
		private const string StageSource = World + "/PointsOfInterest/PointOfInterestStage.cs";
		private const string FieldsSource = World + "/PointsOfInterest/Features/PointOfInterestFields.cs";
		private const string StreetsSource = World + "/PointsOfInterest/PropsFeature.cs";
		private const string SceneWaterSource = World + "/Terrain/Hydrology/SceneWater.cs";
		private const string PathFieldSource = "Assets/Scripts/Shared/Implementation/Entity/WorldSceneDetails/PointsOfInterest/Paths/ScenePathField.cs";
		private const string PathGroundSource = World + "/PointsOfInterest/Paths/PathGround.cs";
		private const string PlannerSource = World + "/PointsOfInterest/Paths/PathNetworkPlanner.cs";
		private const string ArtGeneratorSource = World + "/ProceduralArt/BiomeArtGenerator.cs";

		// ── Defaults: nothing changes ─────────────────────────────────

		/// <summary>Every biome's spec floats nothing and raises no way; every template asset holds none (or the field not yet).</summary>
		[Test]
		public void Defaults_FloatNothing_RaiseNothing_ForEveryBiome()
		{
			Assert.That(BiomeArtSpec.Entries.Length, Is.EqualTo(83));
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				Assert.That(entry.FloatingPlants(), Is.Empty, $"{entry.Biome} floats nothing yet");
				Assert.That(entry.Boardwalks, Is.EqualTo(BiomeBoardwalks.None), $"{entry.Biome} raises no way yet");
				foreach ((string prefab, float _) in entry.FieldCrops())
				{
					Assert.That(entry.FieldSurface(prefab), Is.Not.EqualTo(BiomeFieldSurface.Paddy), $"{entry.Biome}: {prefab} is not a paddy yet");
				}
			}
			int templates = 0;
			foreach (string path in Directory.GetFiles(BiomeFolder, "*.asset"))
			{
				string text = File.ReadAllText(path);
				if (!Regex.IsMatch(text, "^  MinTemperature:", RegexOptions.Multiline))
				{
					continue;
				}
				templates++;
				Assert.That(Regex.IsMatch(text, @"^  floatingPlants:\s*\n  - ", RegexOptions.Multiline), Is.False, $"{Path.GetFileName(path)} floats something");
				Match walks = Regex.Match(text, @"^  boardwalks: (\d+)", RegexOptions.Multiline);
				Assert.That(!walks.Success || walks.Groups[1].Value == "0", Is.True, $"{Path.GetFileName(path)} raises ways");
				Assert.That(Regex.IsMatch(text, @"^\s*Surface: 3\s*$", RegexOptions.Multiline), Is.False, $"{Path.GetFileName(path)} floods a field");
			}
			Assert.That(templates, Is.EqualTo(83));
			var template = new BiomeTemplateFields();
			Assert.That(template.DefaultBoardwalks, Is.EqualTo(BiomeBoardwalks.None));
		}

		/// <summary>The template's defaults as the field initialisers give them, read without making a ScriptableObject.</summary>
		private sealed class BiomeTemplateFields
		{
			public BiomeBoardwalks DefaultBoardwalks
			{
				get
				{
					string source = File.ReadAllText("Assets/Scripts/Shared/Implementation/Tools/Biomes/BiomeTemplate.cs");
					StringAssert.Contains("private List<BiomeFloatingPlant> floatingPlants = new List<BiomeFloatingPlant>();", source);
					return source.Contains("private BiomeBoardwalks boardwalks = BiomeBoardwalks.None;") ? BiomeBoardwalks.None : (BiomeBoardwalks)255;
				}
			}
		}

		/// <summary>The new enums are append-only: their numbers are what assets store.</summary>
		[Test]
		public void NewValues_AreAppended()
		{
			Assert.That((byte)BiomeFloatingPlantKind.Duckweed, Is.EqualTo(0));
			Assert.That((byte)BiomeFloatingPlantKind.Frogbit, Is.EqualTo(3));
			Assert.That((byte)BiomeFieldSurface.Paddy, Is.EqualTo(3), "after RidgeAndFurrow");
			Assert.That((byte)ScenePathPointFlags.Boardwalk, Is.EqualTo(1 << 5), "the first free flag bit");
			Assert.That((byte)BiomeBoardwalks.None, Is.EqualTo(0));
			Assert.That((int)DetailKind.Duckweed, Is.EqualTo((int)DetailKind.SlimeMat + 1), "after every earlier kind");
			Assert.That(new BiomeFloatingPlant(BiomeFloatingPlantKind.WaterLily, 2.5f).ToString(), Is.EqualTo("WaterLily×2.5"));
			Assert.That(new BiomeFieldCrop(null, 1f, BiomeFieldSurface.Paddy).ToString(), Is.EqualTo("<none>×1|Paddy"));
		}

		// ── Floating plants: the cover model ──────────────────────────

		[Test]
		public void Cover_FollowsDepthShoreAndShelter()
		{
			foreach (BiomeFloatingPlantKind kind in (BiomeFloatingPlantKind[])Enum.GetValues(typeof(BiomeFloatingPlantKind)))
			{
				Assert.That(BiomeFloatingPlants.Cover(kind, 0.05f, 2f, 1f), Is.Zero, $"{kind}: no plant on a wet bank");
				for (float shore = 0f; shore < 60f; shore += 3f)
				{
					float c = BiomeFloatingPlants.Cover(kind, 1f, shore, 1f);
					Assert.That(c, Is.InRange(0f, 1f), $"{kind} at {shore} m");
					Assert.That(BiomeFloatingPlants.Cover(kind, 1f, shore, 0.3f), Is.LessThanOrEqualTo(c + 1e-6f), $"{kind}: less where less sheltered");
				}
			}
			// Free floaters thin out over open water; lilies hold to their depth.
			Assert.That(BiomeFloatingPlants.Cover(BiomeFloatingPlantKind.Duckweed, 1f, 50f, 1f), Is.Zero);
			Assert.That(BiomeFloatingPlants.Cover(BiomeFloatingPlantKind.Duckweed, 1f, 3f, 1f), Is.GreaterThan(0.8f));
			Assert.That(BiomeFloatingPlants.Cover(BiomeFloatingPlantKind.WaterLily, 1f, 30f, 1f), Is.GreaterThan(0.7f));
			Assert.That(BiomeFloatingPlants.Cover(BiomeFloatingPlantKind.WaterLily, 3f, 30f, 1f), Is.Zero, "too deep for a lily's stalk");
			Assert.That(BiomeFloatingPlants.Cover(BiomeFloatingPlantKind.WaterLily, 0.2f, 30f, 1f), Is.Zero, "too shallow");
			Assert.That(BiomeFloatingPlants.Cover(BiomeFloatingPlantKind.Frogbit, 2f, 1f, 1f), Is.Zero, "frogbit keeps to the shallow margins");
			// Shelter: a pond whole, a big lake its margins.
			Assert.That(BiomeFloatingPlants.LakeShelter(500f), Is.EqualTo(1f));
			Assert.That(BiomeFloatingPlants.LakeShelter(500000f), Is.EqualTo(BiomeFloatingPlants.OpenLakeShelter).Within(1e-5f));
			float previous = 1f;
			for (float area = 1000f; area < 1e6f; area *= 1.7f)
			{
				float s = BiomeFloatingPlants.LakeShelter(area);
				Assert.That(s, Is.LessThanOrEqualTo(previous + 1e-6f));
				previous = s;
			}
			Assert.That(BiomeFloatingPlants.Shelter(0.3f, 0f), Is.EqualTo(1f).Within(1e-5f), "full at the shore");
			Assert.That(BiomeFloatingPlants.Shelter(0.3f, 200f), Is.EqualTo(0.3f).Within(1e-3f), "the body's own far out");
		}

		[Test]
		public void Pick_IsByWeight_AndEmptyPicksNothing()
		{
			Assert.That(BiomeFloatingPlants.TryPick(null, 0.5f, out _), Is.False);
			Assert.That(BiomeFloatingPlants.TryPick(new List<BiomeFloatingPlant>(), 0.5f, out _), Is.False);
			Assert.That(BiomeFloatingPlants.TryPick(new List<BiomeFloatingPlant> { new BiomeFloatingPlant(BiomeFloatingPlantKind.Frogbit, 0f) }, 0.5f, out _), Is.False);
			var list = new List<BiomeFloatingPlant>
			{
				new BiomeFloatingPlant(BiomeFloatingPlantKind.Duckweed, 3f),
				new BiomeFloatingPlant(BiomeFloatingPlantKind.WaterLily, 1f),
			};
			int lilies = 0;
			for (int k = 0; k < 1000; k++)
			{
				Assert.That(BiomeFloatingPlants.TryPick(list, (k + 0.5f) / 1000f, out BiomeFloatingPlantKind kind), Is.True);
				lilies += kind == BiomeFloatingPlantKind.WaterLily ? 1 : 0;
			}
			Assert.That(lilies, Is.EqualTo(250));
		}

		// ── Floating plants: still-water masks and determinism ────────

		private static FloatingPlants.StillWater Pond(float radius, float spacing, Vector2 origin, int size)
		{
			var body = new FloatingPlants.StillWater { Id = 7, Level = 10f, Spacing = spacing, Origin = origin, Width = size, Depth = size };
			body.Wet = new bool[size * size];
			for (int j = 0; j < size; j++)
			{
				for (int i = 0; i < size; i++)
				{
					float x = origin.x + i * spacing, z = origin.y + j * spacing;
					body.Wet[j * size + i] = x * x + z * z < radius * radius;
				}
			}
			return body;
		}

		private static readonly List<BiomeFloatingPlant> Mixed = new List<BiomeFloatingPlant>
		{
			new BiomeFloatingPlant(BiomeFloatingPlantKind.Duckweed, 2f),
			new BiomeFloatingPlant(BiomeFloatingPlantKind.WaterLily, 1f),
			new BiomeFloatingPlant(BiomeFloatingPlantKind.Frogbit, 1f),
		};

		[Test]
		public void ShoreDistance_GrowsInward_AndDryIsZero()
		{
			FloatingPlants.StillWater pond = Pond(30f, 4f, new Vector2(-40f, -40f), 21);
			float[] d = FloatingPlants.ShoreDistances(pond.Wet, pond.Width, pond.Depth, pond.Spacing);
			int centre = 10 * 21 + 10;
			Assert.That(d[centre], Is.GreaterThan(20f).And.LessThan(32f), "about the radius in from the shore");
			for (int k = 0; k < d.Length; k++)
			{
				if (!pond.Wet[k])
				{
					Assert.That(d[k], Is.Zero);
				}
				else
				{
					Assert.That(d[k], Is.GreaterThan(0f));
				}
			}
			Assert.That(d[10 * 21 + 18], Is.LessThan(d[10 * 21 + 14]), "nearer the shore, less");
		}

		[Test]
		public void PlanBody_LaysOnlyOnTheWater_AtItsLevel_AndTheSameEveryTime()
		{
			FloatingPlants.StillWater pond = Pond(30f, 4f, new Vector2(-40f, -40f), 21);
			pond.Shelter = BiomeFloatingPlants.LakeShelter(pond.Area);
			float Bed(float x, float z) => 10f - Mathf.Lerp(1.6f, 0.2f, Mathf.Sqrt(x * x + z * z) / 30f);
			var first = new List<FloatingPlants.Plant>();
			int laid = FloatingPlants.PlanBody(pond, Bed, (x, z) => Mixed, null, 1234u, first);
			Assert.That(laid, Is.GreaterThan(50), "a sheltered pond carries plants");
			foreach (FloatingPlants.Plant p in first)
			{
				int k = pond.PointAt(p.Position.x, p.Position.z);
				Assert.That(k >= 0 && pond.Wet[k], Is.True, $"on the pond's water at {p.Position}");
				Assert.That(p.Position.y, Is.EqualTo(10f), "at the water's level");
				Vector2 range = BiomeFloatingPlants.ScaleRange(p.Kind);
				Assert.That(p.Scale, Is.InRange(range.x, range.y));
			}
			var again = new List<FloatingPlants.Plant>();
			FloatingPlants.PlanBody(pond, Bed, (x, z) => Mixed, null, 1234u, again);
			Assert.That(again, Is.EqualTo(first), "deterministic");
			var other = new List<FloatingPlants.Plant>();
			FloatingPlants.PlanBody(pond, Bed, (x, z) => Mixed, null, 99u, other);
			Assert.That(other, Is.Not.EqualTo(first), "another seed lays another pond");
			// The same water seen through a larger mask (another extent and origin) lays the same plants: the lattice is the scene's.
			FloatingPlants.StillWater wider = Pond(30f, 4f, new Vector2(-48f, -48f), 25);
			wider.Shelter = pond.Shelter;
			var widerPlants = new List<FloatingPlants.Plant>();
			FloatingPlants.PlanBody(wider, Bed, (x, z) => Mixed, null, 1234u, widerPlants);
			Assert.That(widerPlants.Count, Is.EqualTo(first.Count));
			// Nothing where the biome floats nothing, and nothing where kept clear.
			Assert.That(FloatingPlants.PlanBody(pond, Bed, (x, z) => null, null, 1234u, new List<FloatingPlants.Plant>()), Is.Zero);
			var half = new List<FloatingPlants.Plant>();
			FloatingPlants.PlanBody(pond, Bed, (x, z) => Mixed, (x, z) => x > 0f, 1234u, half);
			Assert.That(half.TrueForAll(p => p.Position.x <= 0f), Is.True);
			Assert.That(half.Count, Is.LessThan(first.Count));
		}

		[Test]
		public void PlanPools_OnlySlackPools_BetweenTheBanks()
		{
			const int n = 40;
			var xs = new float[n];
			var zs = new float[n];
			var widths = new float[n];
			var surfaces = new float[n];
			var reaches = new RiverReach[n];
			for (int i = 0; i < n; i++)
			{
				xs[i] = i * 3f;
				zs[i] = 0f;
				widths[i] = 10f;
				surfaces[i] = 5f;
				reaches[i] = i >= 10 && i < 25 ? RiverReach.Pool : i < 10 ? RiverReach.Run : RiverReach.Riffle;
			}
			var plants = new List<FloatingPlants.Plant>();
			FloatingPlants.PlanPools(3, xs, zs, widths, surfaces, reaches, (x, z) => 4f, (x, z) => Mixed, null, null, 77u, plants);
			Assert.That(plants.Count, Is.GreaterThan(5));
			foreach (FloatingPlants.Plant p in plants)
			{
				Assert.That(p.Position.x, Is.InRange(30f, 72f), "within the pool's points");
				Assert.That(Mathf.Abs(p.Position.z), Is.LessThan(5f), "between the banks");
				Assert.That(p.Position.y, Is.EqualTo(5f));
			}
			var all = (RiverReach[])reaches.Clone();
			for (int i = 0; i < n; i++)
			{
				all[i] = RiverReach.Run;
			}
			Assert.That(FloatingPlants.PlanPools(3, xs, zs, widths, surfaces, all, (x, z) => 4f, (x, z) => Mixed, null, null, 77u, new List<FloatingPlants.Plant>()), Is.Zero,
				"a running river carries none");
		}

		/// <summary>A scene's water with one square lake on its grid (the grid set as the water's marking would).</summary>
		private static SceneWater WaterWithLake(int id, float level, bool frozen, float seaLevel)
		{
			var water = new SceneWater { SeaLevel = seaLevel };
			var grid = new SceneWaterGrid(40, 40, 2f, -40f, -40f);
			for (int z = 10; z < 30; z++)
			{
				for (int x = 10; x < 30; x++)
				{
					int i = z * 40 + x;
					grid.Kind[i] = WaterKind.Lake;
					grid.Body[i] = id;
					grid.Level[i] = level;
				}
			}
			typeof(SceneWater).GetProperty(nameof(SceneWater.Grid), BindingFlags.Public | BindingFlags.Instance).SetValue(water, grid);
			water.Lakes.Add(new SceneLake { Id = id, Level = level, SpillLevel = level, Frozen = frozen, Bounds = Rect.MinMaxRect(-20f, -20f, 20f, 20f) });
			return water;
		}

		[Test]
		public void Plan_TakesStillOpenWaterOverTheSea_Only()
		{
			float Bed(float x, float z) => 9f;
			IReadOnlyList<BiomeFloatingPlant> Floats(int id) => Mixed;
			IReadOnlyList<BiomeFloatingPlant> None(int id) => null;
			Assert.That(FloatingPlants.Plan(WaterWithLake(5, 10f, false, float.NegativeInfinity), Bed, Floats, None, null, 3u).Count, Is.GreaterThan(10));
			Assert.That(FloatingPlants.Plan(WaterWithLake(5, 10f, true, float.NegativeInfinity), Bed, Floats, None, null, 3u), Is.Empty, "frozen");
			Assert.That(FloatingPlants.Plan(WaterWithLake(5, 10f, false, 10.02f), Bed, Floats, None, null, 3u), Is.Empty,
				"under the sea's level the vegetation shader would fold a land plant away (FishSeaCeiling)");
			Assert.That(FloatingPlants.Plan(WaterWithLake(CaveShaper.PoolIdBase | 9, 10f, false, float.NegativeInfinity), Bed, Floats, None, null, 3u), Is.Empty, "a grotto's pool");
			Assert.That(FloatingPlants.Plan(WaterWithLake(5, 10f, false, float.NegativeInfinity), Bed, None, None, null, 3u), Is.Empty, "a biome floating nothing");
			Assert.That(FloatingPlants.Plan(WaterWithLake(MoatShaper.LakeIdBase | 9, 10f, false, float.NegativeInfinity), Bed, Floats, None, null, 3u).Count, Is.GreaterThan(10),
				"a moat is still water");
			List<FloatingPlants.Plant> a = FloatingPlants.Plan(WaterWithLake(5, 10f, false, float.NegativeInfinity), Bed, Floats, None, null, 3u);
			List<FloatingPlants.Plant> b = FloatingPlants.Plan(WaterWithLake(5, 10f, false, float.NegativeInfinity), Bed, Floats, None, null, 3u);
			Assert.That(b, Is.EqualTo(a), "deterministic");
		}

		[Test]
		public void Paddies_AreNeitherStillWaterPlantsNorTheCutsWater()
		{
			int id = PointOfInterestPaddies.LakeId(0x12345678, 3);
			Assert.That(PointOfInterestPaddies.IsPaddy(id), Is.True);
			Assert.That(FloatingPlants.IsGrottoPool(id) || FloatingPlants.IsMoat(id), Is.False);
			Assert.That(id, Is.LessThan(CaveShaper.PoolIdBase));
			Assert.That(PointOfInterestPaddies.IsPaddy(MoatShaper.LakeIdBase | 3) || PointOfInterestPaddies.IsPaddy(CaveShaper.PoolIdBase | 3) || PointOfInterestPaddies.IsPaddy(17), Is.False);
			Assert.That(PointOfInterestPaddies.LakeId(0x12345678, 4), Is.Not.EqualTo(id));
			StringAssert.Contains("PointOfInterestPaddies.IsPaddy(lake.Id)", Read(SceneWaterSource));
		}

		// ── Boardwalks ────────────────────────────────────────────────

		private static ScenePath Way(int n, ScenePathClass kind = ScenePathClass.Road, float spacing = 2f)
		{
			var path = new ScenePath
			{
				Id = 4,
				Class = kind,
				Points = new Vector3[n],
				HalfWidth = new float[n],
				Wear = new float[n],
				Surface = new byte[n],
				Flags = new byte[n],
			};
			for (int i = 0; i < n; i++)
			{
				path.Points[i] = new Vector3(i * spacing, 0f, 0f);
				path.HalfWidth[i] = 1.2f;
				path.Wear[i] = 1f;
			}
			return path;
		}

		[Test]
		public void Runs_MergeShortGaps_DropShortRuns_AndNeverCrossABridgeFordOrSite()
		{
			ScenePath way = Way(41);
			Assert.That(PathBoardwalks.Runs(way, i => true), Is.EqualTo(new List<(int, int)> { (0, 40) }));
			// A 4 m gap (two points) is carried over; a 10 m one is not.
			Assert.That(PathBoardwalks.Runs(way, i => i != 15 && i != 16), Is.EqualTo(new List<(int, int)> { (0, 40) }));
			Assert.That(PathBoardwalks.Runs(way, i => i < 12 || i > 16), Is.EqualTo(new List<(int, int)> { (0, 11), (17, 40) }));
			// A run under 10 m stays on the ground.
			Assert.That(PathBoardwalks.Runs(way, i => i >= 20 && i <= 23), Is.Empty);
			// A bridge's deck, a ford and a site's ground break a run and are never closed over.
			foreach (ScenePathPointFlags flag in new[] { ScenePathPointFlags.Bridge, ScenePathPointFlags.Ford, ScenePathPointFlags.InSite })
			{
				ScenePath crossing = Way(41);
				crossing.Flags[20] = (byte)flag;
				List<(int From, int To)> runs = PathBoardwalks.Runs(crossing, i => true);
				Assert.That(runs, Is.EqualTo(new List<(int, int)> { (0, 19), (21, 40) }), flag.ToString());
			}
			Assert.That(PathBoardwalks.Runs(way, null), Is.Empty);
		}

		[Test]
		public void Mark_FlagsTheRunsAlone_AndKeepsTheirOtherFlags()
		{
			ScenePath way = Way(30);
			way.Flags[5] = (byte)ScenePathPointFlags.Squared;
			int marked = PathBoardwalks.Mark(way, new List<(int, int)> { (3, 12) });
			Assert.That(marked, Is.EqualTo(10));
			for (int i = 0; i < way.Count; i++)
			{
				bool on = (way.FlagsAt(i) & ScenePathPointFlags.Boardwalk) != 0;
				Assert.That(on, Is.EqualTo(i >= 3 && i <= 12), $"point {i}");
			}
			Assert.That((way.FlagsAt(5) & ScenePathPointFlags.Squared) != 0, Is.True);
			Assert.That(PathBoardwalks.FlaggedRuns(way), Is.EqualTo(new List<(int, int)> { (3, 12) }));
		}

		[Test]
		public void MarkWays_RaisesOnlyTheClassesItsBiomeRaises_OnLevelDryGround()
		{
			ScenePath road = Way(30, ScenePathClass.Road), path = Way(30, ScenePathClass.Footpath), street = Way(30, ScenePathClass.Street), yard = Way(30, ScenePathClass.Yard);
			var ways = new List<ScenePath> { road, path, street, yard };
			Assert.That(PathBoardwalks.MarkWays(ways, (x, z) => BiomeBoardwalks.None, null, null), Is.Zero, "no biome raises anything: nothing changes");
			Assert.That(PathBoardwalks.MarkWays(ways, null, null, null), Is.Zero);
			int marked = PathBoardwalks.MarkWays(ways, (x, z) => BiomeBoardwalks.Roads | BiomeBoardwalks.Streets, (x, z) => x > 30f ? 20f : 2f, (x, z) => true);
			Assert.That(marked, Is.GreaterThan(0));
			Assert.That(PathBoardwalks.FlaggedRuns(road), Is.EqualTo(new List<(int, int)> { (0, 15) }), "the level part of the road");
			Assert.That(PathBoardwalks.FlaggedRuns(path), Is.Empty, "footpaths are not the biome's to raise");
			Assert.That(PathBoardwalks.FlaggedRuns(street), Is.Empty, "a settlement raises its own streets");
			Assert.That(PathBoardwalks.FlaggedRuns(yard), Is.Empty);
			Assert.That(BiomeBoardwalkRules.Raises(BiomeBoardwalks.Paths, ScenePathClass.Trail), Is.True);
			Assert.That(BiomeBoardwalkRules.Raises(BiomeBoardwalks.Paths | BiomeBoardwalks.Roads | BiomeBoardwalks.Streets, ScenePathClass.AncientRoad), Is.False);
			foreach (ScenePathClass kind in (ScenePathClass[])Enum.GetValues(typeof(ScenePathClass)))
			{
				Assert.That(BiomeBoardwalkRules.Raises(BiomeBoardwalks.None, kind), Is.False);
			}
		}

		[Test]
		public void FlaggedSegments_AreNeitherDrawnNorCarvedNorClimbed()
		{
			ScenePath plain = Way(41, ScenePathClass.Road);
			ScenePath raised = Way(41, ScenePathClass.Road);
			for (int i = 0; i < raised.Count; i++)
			{
				raised.Flags[i] = (byte)ScenePathPointFlags.Boardwalk;
				// A steep climb the deck carries at its own grade.
				plain.Points[i].y = raised.Points[i].y = i * 3f;
			}
			Assert.That(ScenePathField.Build(new[] { plain }).PageCount, Is.GreaterThan(0));
			Assert.That(ScenePathField.Build(new[] { raised }).PageCount, Is.Zero, "nothing drawn under the deck");
			Assert.That(PathNetworkPlanner.Excess(plain, 0, 40, PathRouter.WalkableGrade), Is.GreaterThan(0f));
			Assert.That(PathNetworkPlanner.Excess(raised, 0, 40, PathRouter.WalkableGrade), Is.Zero, "the deck is walked, not the ground");
			float Ground(float x, float z) => 3f;
			foreach ((ScenePath way, bool carved) in new[] { (plain, true), (raised, false) })
			{
				var paths = new List<ScenePath> { way };
				float[][] targets = PathCarver.Targets(paths, Ground, null);
				var index = new PathSegmentIndex(paths, p => p.MaxHalfWidth + ScenePathStyle.For(p.Class).Shoulder);
				PathCarver.Blend(index, targets, 40f, 0f, out float weight);
				Assert.That(weight > 0f, Is.EqualTo(carved), carved ? "a way is carved" : "nothing carved under the deck");
			}
			StringAssert.Contains("ScenePathPointFlags.Boardwalk", Read(PathFieldSource));
			StringAssert.Contains("ScenePathPointFlags.Bridge | ScenePathPointFlags.Boardwalk", Read(PathGroundSource));
			StringAssert.Contains("ScenePathPointFlags.Bridge | ScenePathPointFlags.Boardwalk", Read(PlannerSource));
		}

		[Test]
		public void Deck_StandsOverTheGround_RampsDownAtItsEnds_AndRunsGently()
		{
			ScenePath way = Way(41);
			float Ground(float x, float z) => 2f + 0.4f * Mathf.Sin(x * 0.3f) + 0.02f * x;
			List<PathBoardwalks.Station> stations = PathBoardwalks.Stations(way, 0, 40, Ground);
			Assert.That(stations.Count, Is.EqualTo(21), "80 m in 4 m modules");
			Assert.That(stations[0].At, Is.EqualTo(new Vector2(0f, 0f)));
			Assert.That(stations[stations.Count - 1].At.x, Is.EqualTo(80f).Within(1e-3f));
			Assert.That(stations[0].Deck, Is.EqualTo(stations[0].Ground + PathBoardwalks.MinClearMetres).Within(1e-4f), "down to the ground at its start");
			Assert.That(stations[20].Deck, Is.EqualTo(stations[20].Ground + PathBoardwalks.MinClearMetres).Within(1e-4f), "and at its end");
			for (int k = 0; k < stations.Count; k++)
			{
				Assert.That(stations[k].Deck, Is.GreaterThanOrEqualTo(stations[k].Ground + PathBoardwalks.MinClearMetres - 1e-5f));
				if (k >= 2 && k <= stations.Count - 3)
				{
					Assert.That(stations[k].Deck - stations[k].Ground, Is.GreaterThanOrEqualTo(PathBoardwalks.DeckRiseMetres - 1e-4f), "clear of the ground between the ramps");
				}
				if (k > 0)
				{
					float grade = Mathf.Abs(stations[k].Deck - stations[k - 1].Deck) / (stations[k].Along - stations[k - 1].Along);
					bool ramp = k <= 2 || k >= stations.Count - 2;
					Assert.That(grade, Is.LessThanOrEqualTo(ramp ? 0.35f : PathBoardwalks.DeckGrade + 1e-4f), $"station {k}");
				}
			}
			// An end that meets another deck keeps its height into it.
			List<PathBoardwalks.Station> joined = PathBoardwalks.Stations(way, 0, 40, Ground, StructurePieces.BoardwalkModule, true, false);
			Assert.That(joined[20].Deck - joined[20].Ground, Is.GreaterThan(0.5f));
			// The piles reach the ground, and only where the deck clears it.
			Assert.That(PathBoardwalks.BentScale(new PathBoardwalks.Station { Deck = 3f, Ground = 2.1f }),
				Is.EqualTo((0.9f + StructurePieces.BoardwalkPileSink) / StructurePieces.BoardwalkBentDepth).Within(1e-5f));
			Assert.That(PathBoardwalks.BentScale(new PathBoardwalks.Station { Deck = 2.2f, Ground = 2.1f }), Is.Zero);
			Assert.That(PathBoardwalks.DeckHalfWidth(0.2f), Is.EqualTo(0.5f * PathBoardwalks.MinWidthMetres));
			Assert.That(PathBoardwalks.DeckHalfWidth(9f), Is.EqualTo(0.5f * PathBoardwalks.MaxWidthMetres));
		}

		[Test]
		public void BoardwalkPieces_AreTheLastPieces_AndNoTemplateDrawsThem()
		{
			IReadOnlyList<StructurePiece> all = StructurePieces.All;
			StructurePiece span = StructurePieces.Find("BoardwalkSpan"), bent = StructurePieces.Find("BoardwalkBent");
			Assert.That(span, Is.Not.Null);
			Assert.That(bent, Is.Not.Null);
			// Appended, so no earlier piece moved in the table; only the families appended after them (dungeon entrances, the linear
			// features of 2026-10-18, then the town's other buildings of 2026-10-19, after the last linear piece) follow them.
			int at = -1, lastLinear = -1;
			for (int i = 0; i < all.Count; i++)
			{
				at = all[i] == span ? i : at;
				lastLinear = all[i].HasTag("linear") ? i : lastLinear;
			}
			Assert.That(all[at + 1], Is.SameAs(bent));
			for (int i = at + 2; i < all.Count; i++)
			{
				Assert.That(all[i].HasTag("dungeon") || all[i].HasTag("linear") || i > lastLinear, Is.True, $"{all[i].Id} was appended after the boardwalks by a later family");
				Assert.That(all[i].HasTag("boardwalk"), Is.False, $"{all[i].Id}");
			}
			Assert.That(span.Tags, Is.EqualTo(new[] { "boardwalk" }));
			Assert.That(bent.Tags, Is.EqualTo(new[] { "boardwalk" }));
			Assert.That(span.Anchor, Is.EqualTo(StructureAnchor.Deck));
			Assert.That(span.ModuleLength, Is.EqualTo(StructurePieces.BoardwalkModule));
			Assert.That(Array.IndexOf(StructurePieces.Tags, "boardwalk"), Is.GreaterThanOrEqualTo(0));
			foreach (string path in Directory.GetFiles("Assets/Templates/World/PointsOfInterest", "*.asset", SearchOption.AllDirectories))
			{
				Assert.That(File.ReadAllText(path).IndexOf("boardwalk", StringComparison.OrdinalIgnoreCase), Is.LessThan(0), $"{path} queries the boardwalk pieces");
			}
			Assert.That(ProceduralArtFamilies.FamiliesOfFile("StructurePieces.Boardwalk.cs"), Is.EqualTo(new[] { ArtFamily.Structures }));
			Assert.That(ProceduralArtPayload.SourceFiles, Does.Contain("StructurePieces.Boardwalk.cs"));
		}

		// ── Paddies ───────────────────────────────────────────────────

		[Test]
		public void PaddyGround_IsALevelFloorInsideABund()
		{
			const float water = 10f;
			float floor = water - BiomeFieldSurfaces.PaddyWaterDepth, crest = water + BiomeFieldSurfaces.PaddyBundRise;
			foreach (float ground in new[] { 9.5f, 10f, 10.6f })
			{
				Assert.That(BiomeFieldSurfaces.PaddyGround(5f, ground, water), Is.EqualTo(floor), "the floor, dug or filled level");
				Assert.That(BiomeFieldSurfaces.PaddyGround(BiomeFieldSurfaces.PaddyFloorFrom, ground, water), Is.EqualTo(floor).Within(1e-5f));
				Assert.That(BiomeFieldSurfaces.PaddyGround(0.3f, ground, water), Is.EqualTo(Mathf.Max(ground, crest)), "the crest, never lowering higher ground");
				Assert.That(BiomeFieldSurfaces.PaddyGround(-3f, ground, water), Is.EqualTo(ground), "the ground beyond the bund as it was");
				for (float inside = -2f; inside < 3f; inside += 0.05f)
				{
					float a = BiomeFieldSurfaces.PaddyGround(inside, ground, water), b = BiomeFieldSurfaces.PaddyGround(inside + 0.05f, ground, water);
					Assert.That(Mathf.Abs(b - a), Is.LessThan(0.2f), $"no step at {inside:0.00} m in");
					if (inside < 0f)
					{
						Assert.That(a, Is.GreaterThanOrEqualTo(ground), "the outer face is raised only");
					}
					if (inside <= BiomeFieldSurfaces.PaddyBundCrest)
					{
						Assert.That(a, Is.GreaterThanOrEqualTo(Mathf.Min(ground, crest) - 1e-5f));
					}
				}
			}
		}

		[Test]
		public void PaddyLevel_StandsUnderTheGroundRoundIt_OrRefuses()
		{
			Assert.That(BiomeFieldSurfaces.PaddyLevel(new[] { 10f, 10.2f }, new[] { 10.1f, 10.3f, 10.05f }, out float water), Is.True);
			Assert.That(water, Is.EqualTo(10.05f - BiomeFieldSurfaces.PaddyFreeboard).Within(1e-5f));
			Assert.That(BiomeFieldSurfaces.PaddyLevel(new[] { 10f, 12f }, new[] { 10f }, out _), Is.False, "too deep a cut on a slope");
			Assert.That(BiomeFieldSurfaces.PaddyLevel(new[] { 8f, 8.1f }, new[] { 10f }, out _), Is.False, "too deep a fill");
			Assert.That(BiomeFieldSurfaces.PaddyLevel(new[] { 10f }, new[] { float.NaN }, out _), Is.False);
			Assert.That(BiomeFieldSurfaces.PaddyLevel(new float[0], new[] { 10f }, out _), Is.False);
			Assert.That(BiomeFieldSurfaces.Flooded(BiomeFieldSurface.Paddy), Is.True);
			Assert.That(BiomeFieldSurfaces.Ridged(BiomeFieldSurface.Paddy) || BiomeFieldSurfaces.Carved(BiomeFieldSurface.Paddy), Is.False, "neither ridged nor ridge-carved");
		}

		private static readonly Vector2 PaddyCentre = new Vector2(61.3f, 47.9f);
		private static readonly Vector2 PaddySize = new Vector2(40f, 24f);
		private const float PaddyYaw = 23f;

		[Test]
		public void Inside_IsTheDistanceInFromTheFieldsEdge()
		{
			Assert.That(PointOfInterestPaddies.Inside(PaddyCentre, PaddyYaw, PaddySize, PaddyCentre.x, PaddyCentre.y), Is.EqualTo(12f).Within(1e-4f));
			float r = PaddyYaw * Mathf.Deg2Rad;
			var axisX = new Vector2(Mathf.Cos(r), -Mathf.Sin(r));
			var axisZ = new Vector2(Mathf.Sin(r), Mathf.Cos(r));
			Vector2 edge = PaddyCentre + axisX * 20f;
			Assert.That(PointOfInterestPaddies.Inside(PaddyCentre, PaddyYaw, PaddySize, edge.x, edge.y), Is.EqualTo(0f).Within(1e-4f));
			Vector2 out3 = PaddyCentre + axisZ * 15f;
			Assert.That(PointOfInterestPaddies.Inside(PaddyCentre, PaddyYaw, PaddySize, out3.x, out3.y), Is.EqualTo(-3f).Within(1e-4f));
			Vector2 corner = PaddyCentre + axisX * 23f + axisZ * 16f;
			Assert.That(PointOfInterestPaddies.Inside(PaddyCentre, PaddyYaw, PaddySize, corner.x, corner.y), Is.EqualTo(-5f).Within(1e-4f));
		}

		[Test]
		public void PaddyWater_CoversTheFloor_AndItsSheetStaysUnderTheGroundRoundIt()
		{
			// Gently sloping ground; the level from the ground round the field, the ground carved to the paddy.
			float Natural(float x, float z) => 20f + 0.006f * x - 0.004f * z;
			Assert.That(PointOfInterestPaddies.Level(PaddyCentre, PaddyYaw, PaddySize, Natural, out float water), Is.True);
			float Final(float x, float z) => Natural(x, z) + PointOfInterestPaddies.Offset(PaddyCentre, PaddyYaw, PaddySize, water, x, z, Natural(x, z));
			SceneHydrology.Lake lake = PointOfInterestPaddies.Lake(PointOfInterestPaddies.LakeId(5, 0), PaddyCentre, PaddyYaw, PaddySize, water);
			Assert.That(lake, Is.Not.Null);
			Assert.That(lake.MaskOrigin.x % lake.MaskCell, Is.EqualTo(0f).Within(1e-4f), "on the scene's own 4 m lattice");
			Assert.That(lake.Covers(lake.Seeds[0].x, lake.Seeds[0].y), Is.True);
			int covered = 0;
			for (int z = -1; z <= lake.MaskHeight; z++)
			{
				for (int x = -1; x <= lake.MaskWidth; x++)
				{
					float cx = lake.MaskOrigin.x + (x + 0.5f) * lake.MaskCell, cz = lake.MaskOrigin.y + (z + 0.5f) * lake.MaskCell;
					if (lake.Covers(cx, cz))
					{
						covered++;
						Assert.That(PointOfInterestPaddies.Inside(PaddyCentre, PaddyYaw, PaddySize, cx, cz), Is.GreaterThanOrEqualTo(BiomeFieldSurfaces.PaddyFloorFrom));
					}
					// The renderer draws a mask a cell larger: wherever its sheet can reach, off the floor, the ground stands over it.
					bool near = false;
					for (int dz = -1; dz <= 1 && !near; dz++)
					{
						for (int dx = -1; dx <= 1 && !near; dx++)
						{
							near = lake.Covers(cx + dx * lake.MaskCell, cz + dz * lake.MaskCell);
						}
					}
					if (!near)
					{
						continue;
					}
					for (int sz = 0; sz < 4; sz++)
					{
						for (int sx = 0; sx < 4; sx++)
						{
							float px = cx + (sx - 1.5f) * lake.MaskCell / 4f, pz = cz + (sz - 1.5f) * lake.MaskCell / 4f;
							float inside = PointOfInterestPaddies.Inside(PaddyCentre, PaddyYaw, PaddySize, px, pz);
							float ground = Final(px, pz);
							if (inside >= BiomeFieldSurfaces.PaddyFloorFrom)
							{
								Assert.That(ground, Is.EqualTo(water - BiomeFieldSurfaces.PaddyWaterDepth).Within(1e-4f), "the floor under the water");
							}
							else if (inside <= BiomeFieldSurfaces.PaddyBundCrest)
							{
								Assert.That(ground, Is.GreaterThanOrEqualTo(water), $"the water's sheet shows nowhere off the field ({px:0.0}, {pz:0.0}, {inside:0.00} m in)");
							}
						}
					}
				}
			}
			Assert.That(covered, Is.GreaterThan(20));
			Assert.That(PointOfInterestPaddies.Lake(1, PaddyCentre, 0f, new Vector2(3f, 3f), water), Is.Null, "too narrow for a cell of water");
		}

		[Test]
		public void PaddyCarve_RestoreCarve_IsBitForBit()
		{
			const int res = 129;
			var tileSize = new Vector2(256f, 256f);
			const float heightScale = 600f;
			foreach (float quantum in new[] { 65535f, 32766f })
			{
				float Q(float h) => Mathf.Round(Mathf.Clamp01(h) * quantum) / quantum;
				var ground = new float[res, res];
				for (int j = 0; j < res; j++)
				{
					for (int i = 0; i < res; i++)
					{
						ground[j, i] = Q(0.05f + 0.0002f * Mathf.Sin(i * 0.21f) * Mathf.Cos(j * 0.17f) + 0.000002f * i);
					}
				}
				float stepX = tileSize.x / (res - 1);
				float Natural(float x, float z)
				{
					int i = Mathf.Clamp(Mathf.RoundToInt(x / stepX), 0, res - 1), j = Mathf.Clamp(Mathf.RoundToInt(z / stepX), 0, res - 1);
					return ground[j, i] * heightScale;
				}
				Assert.That(PointOfInterestPaddies.Level(PaddyCentre, PaddyYaw, PaddySize, Natural, out float water), Is.True);
				var samples = new List<int>();
				var metres = new List<float>();
				Rect area = PointOfInterestPaddies.CarveArea(PaddyCentre, PaddyYaw, PaddySize);
				Func<float, float, float, float> offset = (x, z, g) => PointOfInterestPaddies.Offset(PaddyCentre, PaddyYaw, PaddySize, water, x, z, g);
				Assert.That(PointOfInterestFieldCarve.PlanTo(area, Vector2.zero, tileSize, res, (i, j) => ground[j, i] * heightScale, offset, samples, metres), Is.GreaterThan(50));
				Assert.That(metres.Exists(m => m < 0f) && metres.Exists(m => m > 0f), Is.True, "dug and banked");
				PointOfInterestFieldCarve.Bounds(samples, res, out int x0, out int z0, out int w, out int d);
				float[,] Block(float[,] all)
				{
					var block = new float[d, w];
					for (int j = 0; j < d; j++)
					{
						for (int i = 0; i < w; i++)
						{
							block[j, i] = all[z0 + j, x0 + i];
						}
					}
					return block;
				}
				float[,] Stored(float[,] block)
				{
					var stored = (float[,])block.Clone();
					for (int j = 0; j < d; j++)
					{
						for (int i = 0; i < w; i++)
						{
							stored[j, i] = Q(stored[j, i]);
						}
					}
					return stored;
				}
				float[,] before = Block(ground);
				float[,] carved = (float[,])before.Clone();
				PointOfInterestFieldCarve.Raise(carved, x0, z0, res, samples, metres, heightScale);
				float[,] after = Stored(carved);
				List<float> deltas = PointOfInterestFieldCarve.Deltas(before, after, x0, z0, res, samples);
				float[,] restored = (float[,])after.Clone();
				PointOfInterestFieldCarve.Lower(restored, x0, z0, res, samples, deltas);
				restored = Stored(restored);
				Assert.That(restored, Is.EqualTo(before), $"restored bit for bit ({quantum})");
				// Planned again from the restored ground, the same carve.
				var samples2 = new List<int>();
				var metres2 = new List<float>();
				var whole = (float[,])ground.Clone();
				for (int j = 0; j < d; j++)
				{
					for (int i = 0; i < w; i++)
					{
						whole[z0 + j, x0 + i] = restored[j, i];
					}
				}
				PointOfInterestFieldCarve.PlanTo(area, Vector2.zero, tileSize, res, (i, j) => whole[j, i] * heightScale, offset, samples2, metres2);
				Assert.That(samples2, Is.EqualTo(samples));
				Assert.That(metres2, Is.EqualTo(metres));
				// Control: planned from the carved ground the carve would move the floor again by nothing, but the record is what
				// makes the restore exact; carving on top of itself without it changes the stored ground.
				float[,] piled = (float[,])after.Clone();
				PointOfInterestFieldCarve.Raise(piled, x0, z0, res, samples, metres, heightScale);
				Assert.That(Stored(piled), Is.Not.EqualTo(after), "without the restore, a second carve moves the ground again");
			}
		}

		// ── Wiring ────────────────────────────────────────────────────

		[Test]
		public void Wiring_FloatsAfterTheWaterIsSaved_MarksBeforeTheCarve_ClearsPaddiesBeforeTheSites()
		{
			string cut = CodeOnly(Read(GeneratorSource));
			int save = cut.IndexOf("string hydrologyPath = SaveWater(request, terrainFolder, water, tileGround, result.Notes);", StringComparison.Ordinal);
			int lay = cut.IndexOf("FloatingPlants.Lay(scene, water,", StringComparison.Ordinal);
			int place = cut.IndexOf("poi.Place(scene, plan, terrains, water, result.Notes, result.Wrote);", StringComparison.Ordinal);
			Assert.That(save, Is.GreaterThan(0));
			Assert.That(lay, Is.GreaterThan(save).And.LessThan(place), "the cut floats its plants on the water as saved, before the sites");

			string repaint = CodeOnly(Read(RepaintSource));
			int rsave = repaint.IndexOf("SceneGenerator.SaveWater(", StringComparison.Ordinal);
			int rlay = repaint.IndexOf("FloatingPlants.Lay(scene, water,", StringComparison.Ordinal);
			Assert.That(rlay, Is.GreaterThan(rsave).And.LessThan(repaint.IndexOf("poi.Place(", StringComparison.Ordinal)), "the repaint lays them again");

			string stage = CodeOnly(Read(StageSource));
			int plan = stage.IndexOf("PathPlan paths = PathNetworkPlanner.Plan(pathInput);", StringComparison.Ordinal);
			int mark = stage.IndexOf("MarkBoardwalks(paths.Paths,", StringComparison.Ordinal);
			int carve = stage.IndexOf("PathCarver.Carve(", StringComparison.Ordinal);
			Assert.That(mark, Is.GreaterThan(plan).And.LessThan(carve), "flagged before the ways are carved");
			int ways = stage.IndexOf("PathBoardwalks.LayWays(asset.Paths, sink,", StringComparison.Ordinal);
			Assert.That(ways, Is.GreaterThan(0).And.LessThan(stage.IndexOf("ScenePathBake.Write(", StringComparison.Ordinal)));
			int restore = stage.IndexOf("PointOfInterestFieldCarve.Restore(scene, terrains, notes);", StringComparison.Ordinal);
			int clear = stage.IndexOf("PointOfInterestPaddies.ClearWater(TerrainFolder, Request.SceneName);", StringComparison.Ordinal);
			Assert.That(clear, Is.GreaterThan(restore).And.LessThan(stage.IndexOf("GameObject root = EnsureRoot(scene, asset);", StringComparison.Ordinal)));

			string fields = CodeOnly(Read(FieldsSource));
			int paddy = fields.IndexOf("flooded = LayPaddy(context, field, f, ref carved);", StringComparison.Ordinal);
			Assert.That(paddy, Is.GreaterThan(0).And.LessThan(fields.IndexOf("PaintDetails(terrains, field, crop, biome, surface);", StringComparison.Ordinal)), "dug before it is sown");
			StringAssert.Contains("if (BiomeFieldSurfaces.Flooded(surface))", fields);

			string streets = CodeOnly(Read(StreetsSource));
			StringAssert.Contains("BiomeBoardwalkRules.Raises(BiomeTemplateArt.Boardwalks(", streets);
			StringAssert.Contains("LayStreetBoardwalks(context, streetPaths);", streets);

			string art = CodeOnly(Read(ArtGeneratorSource));
			int floating = art.IndexOf("if (FloatingPlantMeshes.IsFloating(spec.Plant.Kind))", StringComparison.Ordinal);
			Assert.That(floating, Is.GreaterThan(0));
			string held = art.Substring(floating, 600);
			StringAssert.Contains("m.SetFloat(\"_WindSway\", 0f);", held);
			StringAssert.Contains("m.SetFloat(\"_Clinging\", 1f);", held);
			StringAssert.Contains("m.SetFloat(\"_VaryLean\", 0f);", held);
			Assert.That(held.Contains("_Aquatic\", 1"), Is.False);
		}

		[Test]
		public void FloatingDetails_AreCataloguedAndRegisteredAsPlants()
		{
			var kinds = new Dictionary<string, DetailKind>();
			foreach (DetailSpec spec in ProceduralArtCatalogue.Details)
			{
				kinds[ProceduralArtCatalogue.DetailPrefab(spec.Name)] = spec.Plant.Kind;
			}
			var expect = new Dictionary<BiomeFloatingPlantKind, DetailKind>
			{
				{ BiomeFloatingPlantKind.Duckweed, DetailKind.Duckweed },
				{ BiomeFloatingPlantKind.WaterLily, DetailKind.WaterLily },
				{ BiomeFloatingPlantKind.WaterHyacinth, DetailKind.WaterHyacinth },
				{ BiomeFloatingPlantKind.Frogbit, DetailKind.Frogbit },
			};
			foreach (KeyValuePair<BiomeFloatingPlantKind, DetailKind> kv in expect)
			{
				string prefab = ProceduralArtCatalogue.FloatingPrefab(kv.Key);
				Assert.That(kinds.TryGetValue(prefab, out DetailKind kind), Is.True, $"{kv.Key}: {prefab} is a detail");
				Assert.That(kind, Is.EqualTo(kv.Value));
				Assert.That(FloatingPlantMeshes.IsFloating(kind), Is.True);
				Assert.That(SeaFloorMeshes.IsAquatic(kind), Is.False, "on the water, not under it");
				foreach (string prefix in new[] { "Detail_Grass", "Detail_Reeds", "Detail_Flowers", "Detail_Debris" })
				{
					Assert.That(prefab.StartsWith(prefix, StringComparison.Ordinal), Is.False, $"{prefab} must not read as a {prefix} kind");
				}
			}
			foreach (string file in new[] { "FloatingPlantMeshes.cs", "ProceduralArtCatalogue.Floating.cs" })
			{
				Assert.That(ProceduralArtPayload.SourceFiles, Does.Contain(file));
				Assert.That(ProceduralArtFamilies.FamiliesOfFile(file), Is.EqualTo(new[] { ArtFamily.Plants }));
			}
		}

		private static string Read(string relative) => File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), relative)).Replace("\r\n", "\n");

		private static string CodeOnly(string source)
		{
			var code = new System.Text.StringBuilder(source.Length);
			foreach (string line in source.Replace("\r\n", "\n").Split('\n'))
			{
				string trimmed = line.TrimStart();
				if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith("/*", StringComparison.Ordinal) || trimmed.StartsWith("*", StringComparison.Ordinal))
				{
					continue;
				}
				code.Append(line).Append('\n');
			}
			return code.ToString();
		}
	}
}
