using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using NUnit.Framework;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;
using FishMMO.Server.Implementation.World.SceneServer.Spawner;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Wildlife placement (WildlifePlanner), the procedural body (WildlifeBodyMesh) and the gait (WildlifeGait): pure, so each
	/// is tested without a scene.
	/// </summary>
	public class WildlifePlacementTests
	{
		private readonly List<Object> made = new List<Object>();

		[TearDown]
		public void TearDown()
		{
			foreach (Object o in made)
			{
				if (o != null)
				{
					Object.DestroyImmediate(o);
				}
			}
			made.Clear();
		}

		private WildlifeTemplate Species(WildlifeRole role, BiomeHabitat habitats, float density, params POIType[] dens)
		{
			var t = ScriptableObject.CreateInstance<WildlifeTemplate>();
			t.name = role.ToString();
			t.Role = role;
			t.Habitats = habitats;
			t.GroupsPerKm2 = density;
			t.GroupMin = 2;
			t.GroupMax = 4;
			t.DenKinds = new List<POIType>(dens);
			made.Add(t);
			return t;
		}

		private WildlifeCatalogue Limits(int max = 1000)
		{
			var c = ScriptableObject.CreateInstance<WildlifeCatalogue>();
			c.MaxSpawnersPerScene = max;
			made.Add(c);
			return c;
		}

		/// <summary>Forest on the west half, sea on the east; ground at 10 m.</summary>
		private static bool HalfForest(float x, float z, out BiomeHabitat habitat, out float altitude)
		{
			altitude = 10f;
			habitat = x < 2048f ? BiomeHabitat.Forest : BiomeHabitat.Sea;
			return true;
		}

		/// <summary>Forest on the west half, swamp (wetland) on the east.</summary>
		private static bool ForestAndSwamp(float x, float z, out BiomeHabitat habitat, out float altitude)
		{
			altitude = x * 0.01f;
			habitat = x < 2048f ? BiomeHabitat.Forest : BiomeHabitat.Wetland;
			return true;
		}

		private static readonly Rect World = new Rect(0f, 0f, 4096f, 4096f);

		private static bool Holds(WildlifeArea area, WildlifeTemplate species) => area.Species.Exists(w => w.Species.Template == species);

		[Test]
		public void AreasCoverOnlyGroundTheirSpeciesLivesOn()
		{
			WildlifeTemplate deer = Species(WildlifeRole.Prey, BiomeHabitat.Forest, 4f);
			var species = new List<WildlifePlanner.Species> { new WildlifePlanner.Species(null, deer) };
			WildlifeCatalogue limits = Limits();
			List<WildlifeArea> plan = WildlifePlanner.Plan("Test", World, HalfForest, null, null, null, species, limits);
			LogAssert.IsTrue(plan.Count > 0, "a dense forest gets areas");
			foreach (WildlifeArea a in plan)
			{
				LogAssert.IsTrue(a.Center.x - a.Size.x * 0.5f < 2048f, $"{a.Name} lies wholly at sea");
				LogAssert.IsTrue(a.Count >= 2 && a.Count <= 6, "every spawner keeps the catalogue's 2 to 6 animals");
				LogAssert.IsTrue(a.Center.x - a.Size.x * 0.5f >= World.xMin && a.Center.x + a.Size.x * 0.5f <= World.xMax &&
					a.Center.z - a.Size.z * 0.5f >= World.yMin && a.Center.z + a.Size.z * 0.5f <= World.yMax,
					$"{a.Name}'s box stays inside the placement bounds");
				LogAssert.IsTrue(a.Size.x >= 32f, "a clipped area keeps a usable size");
			}
		}

		[Test]
		public void EachAreaHoldsTheSpeciesItsBiomesSupport()
		{
			WildlifeTemplate wolf = Species(WildlifeRole.Predator, BiomeHabitat.Forest, 4f);
			WildlifeTemplate gator = Species(WildlifeRole.Predator, BiomeHabitat.Wetland, 4f);
			var species = new List<WildlifePlanner.Species> { new WildlifePlanner.Species(null, wolf), new WildlifePlanner.Species(null, gator) };
			List<WildlifeArea> plan = WildlifePlanner.Plan("Test", World, ForestAndSwamp, null, null, null, species, Limits());
			bool sawMixed = false;
			foreach (WildlifeArea a in plan)
			{
				float west = a.Center.x - a.Size.x * 0.5f, east = a.Center.x + a.Size.x * 0.5f;
				if (east <= 2048f)
				{
					LogAssert.IsTrue(Holds(a, wolf) && !Holds(a, gator), "an all-forest area: wolves, no alligators");
				}
				else if (west >= 2048f)
				{
					LogAssert.IsTrue(Holds(a, gator) && !Holds(a, wolf), "an all-swamp area: alligators, no wolves");
				}
				else if (Holds(a, wolf) && Holds(a, gator))
				{
					sawMixed = true;
				}
			}
			LogAssert.IsTrue(sawMixed, "an area across the edge holds both, each in its share");
			SpawnerDefinition d = WildlifePlanner.ToDefinition(plan.Find(a => Holds(a, wolf) && Holds(a, gator)));
			LogAssert.AreEqual(2, d.Spawnables.Count, "one entry per species");
			LogAssert.IsTrue(d.SpawnType == ObjectSpawnType.Weighted, "picked by weight");
			float total = 0f;
			foreach (SpawnableSettings e in d.Spawnables)
			{
				total += e.SpawnChance;
			}
			LogAssert.IsTrue(Mathf.Abs(total - 1f) < 1e-4f, "the weights are shares");
			LogAssert.IsTrue(d.Pack.Enabled, "packs are on (one per species at run time)");
			LogAssert.IsTrue(d.BoundingBoxSize.y >= 20f, "the box spans the area's relief");
		}

		[Test]
		public void TheSameSceneAlwaysGetsTheSamePlacement()
		{
			var species = new List<WildlifePlanner.Species> { new WildlifePlanner.Species(null, Species(WildlifeRole.Prey, BiomeHabitat.Forest, 2f)) };
			List<WildlifeArea> a = WildlifePlanner.Plan("Moron 2", World, HalfForest, null, null, null, species, Limits());
			List<WildlifeArea> b = WildlifePlanner.Plan("Moron 2", World, HalfForest, null, null, null, species, Limits());
			LogAssert.AreEqual(a.Count, b.Count, "same count");
			for (int i = 0; i < a.Count; ++i)
			{
				LogAssert.IsTrue(a[i].Center == b[i].Center && a[i].Count == b[i].Count, "same areas, in the same order");
			}
		}

		[Test]
		public void KeptOutGroundCountsForNothing()
		{
			var species = new List<WildlifePlanner.Species> { new WildlifePlanner.Species(null, Species(WildlifeRole.Prey, BiomeHabitat.Forest, 8f)) };
			// Everything west of 1500 m is kept out.
			List<WildlifeArea> plan = WildlifePlanner.Plan("Test", World, HalfForest, null, (x, z) => x < 1500f, null, species, Limits());
			foreach (WildlifeArea a in plan)
			{
				LogAssert.IsTrue(a.Center.x + a.Size.x * 0.5f > 1500f, $"{a.Name} lies wholly in a keep-out");
			}
		}

		/// <summary>
		/// No animal spawns inside a town. An area's box is placed clear of settlements but is hundreds of metres wide and its
		/// animals spawn anywhere in it, so each area carries the settlement circles it reaches into and the spawner refuses
		/// points inside them.
		/// </summary>
		[Test]
		public void TownsAreKeptOutOfEverySpawnPoint()
		{
			var circles = new List<Vector3> { new Vector3(500f, 100f, 500f), new Vector3(3000f, 50f, 3000f) };
			List<Vector3> near = WildlifePlanner.KeepOutsFor(circles, new Vector3(800f, 0f, 500f), new Vector3(512f, 40f, 512f));
			LogAssert.AreEqual(1, near.Count, "only the town that reaches into the box is kept");
			LogAssert.IsTrue(near[0] == circles[0], "the near town");

			LogAssert.IsTrue(WildlifePlanner.InsideKeepOut(near, new Vector3(560f, 7f, 520f)), "a point in the town square is refused");
			LogAssert.IsFalse(WildlifePlanner.InsideKeepOut(near, new Vector3(650f, 7f, 500f)), "a point outside the town is fine");
			LogAssert.IsFalse(WildlifePlanner.InsideKeepOut(null, Vector3.zero), "a spawner with no circles refuses nothing");

			WildlifeTemplate wolf = Species(WildlifeRole.Predator, BiomeHabitat.Forest, 50f);
			var species = new List<WildlifePlanner.Species> { new WildlifePlanner.Species(null, wolf) };
			var pois = new List<PointOfInterestRecord> { new PointOfInterestRecord { Kind = POIType.Village, Position = new Vector3(600f, 5f, 600f), Radius = 40f } };
			List<WildlifeArea> plan = WildlifePlanner.Plan("Test", World, HalfForest, pois, null, null, species, Limits());
			bool carried = false;
			foreach (WildlifeArea area in plan)
			{
				// The village's keep-clear circle: its radius plus the catalogue's settlement clearance.
				float radius = 40f + Limits().SettlementClearance;
				float dx = Mathf.Max(Mathf.Abs(600f - area.Center.x) - area.Size.x * 0.5f, 0f);
				float dz = Mathf.Max(Mathf.Abs(600f - area.Center.z) - area.Size.z * 0.5f, 0f);
				bool reaches = dx * dx + dz * dz < radius * radius;
				if (reaches)
				{
					LogAssert.IsTrue(area.KeepOuts.Count == 1 && WildlifePlanner.InsideKeepOut(area.KeepOuts, new Vector3(600f, 5f, 600f)),
						$"{area.Name} reaches the village and keeps it out");
					carried = true;
				}
			}
			LogAssert.IsTrue(carried, "at least one area reaches the village");
		}

		[Test]
		public void DensGetAGroupAndComeBeforeTheCap()
		{
			WildlifeTemplate wolf = Species(WildlifeRole.Predator, BiomeHabitat.Forest, 50f, POIType.WolfDen);
			var species = new List<WildlifePlanner.Species> { new WildlifePlanner.Species(null, wolf) };
			var pois = new List<PointOfInterestRecord> { new PointOfInterestRecord { Kind = POIType.WolfDen, Position = new Vector3(300f, 5f, 300f), Radius = 12f } };
			List<WildlifeArea> plan = WildlifePlanner.Plan("Test", World, HalfForest, pois, null, null, species, Limits(max: 3));
			LogAssert.AreEqual(3, plan.Count, "the cap holds");
			LogAssert.IsTrue(plan[0].Den && plan[0].Center == pois[0].Position, "the den is placed first, at the den");
			LogAssert.IsTrue(plan[0].Count >= 2 && plan[0].Count <= 6, "a den keeps the catalogue's 2 to 6");
		}

		/// <summary>The runtime's per-spawn ground check: one of its habitats, never a settlement or the sea.</summary>
		[Test]
		public void ASpeciesLivesOnlyOnItsOwnGround()
		{
			WildlifeTemplate wolf = Species(WildlifeRole.Predator, BiomeHabitat.Forest | BiomeHabitat.Tundra, 1f);
			LogAssert.IsTrue(WildlifePlanner.Lives(wolf, BiomeHabitat.Forest), "forest");
			LogAssert.IsTrue(WildlifePlanner.Lives(wolf, BiomeHabitat.Tundra | BiomeHabitat.Mountain), "any one of its habitats");
			LogAssert.IsFalse(WildlifePlanner.Lives(wolf, BiomeHabitat.Wetland), "not a swamp");
			LogAssert.IsFalse(WildlifePlanner.Lives(wolf, BiomeHabitat.Forest | BiomeHabitat.Settlement), "never in a settlement");
			LogAssert.IsFalse(WildlifePlanner.Lives(wolf, BiomeHabitat.Forest | BiomeHabitat.Sea), "never at sea");
		}

		[Test]
		public void TheBodyHasEveryPartAndRoomToMove()
		{
			Mesh mesh = WildlifeBodyMesh.Build(WildlifeBodySpec.Deer);
			made.Add(mesh);
			var parts = new List<Vector4>();
			mesh.GetUVs(1, parts);
			var seen = new HashSet<int>();
			foreach (Vector4 p in parts)
			{
				seen.Add(Mathf.RoundToInt(p.w));
			}
			for (int part = 0; part <= 6; ++part)
			{
				LogAssert.IsTrue(seen.Contains(part), $"part {(WildlifeBodyMesh.Part)part} is in the mesh");
			}
			LogAssert.AreEqual(mesh.vertexCount, parts.Count, "every vertex names its part");
			LogAssert.IsTrue(mesh.bounds.min.y < 0f, "the bounds leave room for the fall onto its side");
			LogAssert.IsTrue(mesh.vertexCount < 3000, "low poly");
		}

		[Test]
		public void TheGaitQuickensAndGallops()
		{
			WildlifeBodySpec deer = WildlifeBodySpec.Deer;
			WildlifeGait still = WildlifeGait.Evaluate(deer, 10f, 0f, 1f);
			WildlifeGait walk = WildlifeGait.Evaluate(deer, 10f, 1.4f, 1f);
			WildlifeGait run = WildlifeGait.Evaluate(deer, 10f, 10f, 1f);
			WildlifeGait dead = WildlifeGait.Evaluate(deer, 10f, 10f, 0f);
			LogAssert.AreEqual(0f, still.LegSwing, "standing still, the legs do not move");
			LogAssert.IsTrue(walk.Gallop < 0.01f && walk.StepsPerSecond > 0f, "a walk is a trot-free walk");
			LogAssert.IsTrue(run.Gallop > 0.99f, "at full speed it gallops");
			LogAssert.IsTrue(run.StepsPerSecond > walk.StepsPerSecond && run.LegSwing > walk.LegSwing, "faster, wider steps");
			LogAssert.AreEqual(0f, dead.StepsPerSecond, "the dead do not walk");
		}
		// ── Ground: caves and painted textures (2026-11-21: deer and elk spawned in caves) ──────────────

		private static GroundReading Ground(bool underground, SurfaceKind? surface)
		{
			return new GroundReading { Underground = underground, SurfaceKnown = surface.HasValue, Surface = surface ?? SurfaceKind.Dirt };
		}

		[Test]
		public void EachShelterKeepsItsSpeciesInOrOutOfCaves()
		{
			WildlifeTemplate deer = Species(WildlifeRole.Prey, BiomeHabitat.Forest, 1f);
			WildlifeTemplate bat = Species(WildlifeRole.Predator, BiomeHabitat.None, 0f);
			bat.Shelter = WildlifeShelter.Cave;
			WildlifeTemplate bear = Species(WildlifeRole.Predator, BiomeHabitat.Forest, 1f);
			bear.Shelter = WildlifeShelter.Anywhere;
			LogAssert.IsTrue(WildlifeRules.LivesOn(deer, Ground(false, SurfaceKind.Grass)), "a deer in a field");
			LogAssert.IsFalse(WildlifeRules.LivesOn(deer, Ground(true, SurfaceKind.Stone)), "never a deer in a cave");
			LogAssert.IsTrue(WildlifeRules.LivesOn(bat, Ground(true, SurfaceKind.Stone)), "a bat in a cave");
			LogAssert.IsFalse(WildlifeRules.LivesOn(bat, Ground(false, SurfaceKind.Grass)), "never a bat out in a field");
			LogAssert.IsTrue(WildlifeRules.LivesOn(bear, Ground(true, SurfaceKind.Stone)) && WildlifeRules.LivesOn(bear, Ground(false, SurfaceKind.Leaves)), "a bear either");
		}

		[Test]
		public void ASpeciesKeepsToItsGrounds()
		{
			WildlifeTemplate deer = Species(WildlifeRole.Prey, BiomeHabitat.Forest, 1f);
			deer.Surfaces = SurfaceMask.Grass | SurfaceMask.Leaves;
			LogAssert.IsTrue(WildlifeRules.LivesOn(deer, Ground(false, SurfaceKind.Leaves)), "forest floor");
			LogAssert.IsFalse(WildlifeRules.LivesOn(deer, Ground(false, SurfaceKind.Stone)), "not bare rock");
			LogAssert.IsFalse(WildlifeRules.LivesOn(deer, Ground(false, SurfaceKind.Sand)), "not a beach");
			LogAssert.IsTrue(WildlifeRules.LivesOn(deer, Ground(false, null)), "an unknown ground passes (a tile with no map)");
			deer.Surfaces = SurfaceMask.Any;
			LogAssert.IsTrue(WildlifeRules.LivesOn(deer, Ground(false, SurfaceKind.Stone)), "no grounds set is any ground");
			LogAssert.AreEqual((int)SurfaceMask.Snow, (int)WildlifeRules.MaskOf(SurfaceKind.Snow), "one bit per kind");
		}

		[Test]
		public void ACaveDwellerAsksNothingOfTheBiome()
		{
			WildlifeTemplate bat = Species(WildlifeRole.Predator, BiomeHabitat.None, 0f);
			bat.Shelter = WildlifeShelter.Cave;
			LogAssert.IsTrue(WildlifePlanner.Lives(bat, BiomeHabitat.Sea), "a sea cave under the coast's sea cell");
			LogAssert.IsTrue(WildlifePlanner.Lives(bat, BiomeHabitat.Forest), "a cave under a forest");
		}

		private static SceneCaveLifeSpot Floor(int site, Vector3 at, float dark, float headroom, SceneCaveForm form = SceneCaveForm.Cave)
		{
			return new SceneCaveLifeSpot
			{
				Kind = SceneCaveLifeKind.Floor, Form = form, Position = at, Normal = Vector3.up, Extent = new Vector3(5f, headroom, 0f),
				Dark = dark, SiteId = site,
			};
		}

		[Test]
		public void ColoniesLiveDeepInTheCavesAndNowhereElse()
		{
			WildlifeTemplate bat = Species(WildlifeRole.Predator, BiomeHabitat.Forest, 50f);
			bat.Shelter = WildlifeShelter.Cave;
			bat.ColoniesInCaves = true;
			WildlifeCatalogue limits = Limits();
			limits.CaveColonyChance = 1f;
			limits.ColoniesPerCave = 2;
			limits.ColonySpacing = 60f;
			var caves = new List<SceneCaveLifeSpot>
			{
				Floor(7, new Vector3(500f, -40f, 500f), 0.9f, 8f),
				Floor(7, new Vector3(520f, -42f, 500f), 0.95f, 8f),  // the darkest, but within 60 m of the next
				Floor(7, new Vector3(600f, -60f, 500f), 0.8f, 8f),
				Floor(7, new Vector3(700f, -70f, 500f), 0.2f, 8f),   // in the mouth's light
				Floor(7, new Vector3(800f, -70f, 500f), 0.9f, 2f),   // no room to fly
				Floor(9, new Vector3(900f, -30f, 500f), 0.9f, 8f, SceneCaveForm.IceCave),
			};
			var species = new List<WildlifePlanner.Species> { new WildlifePlanner.Species(null, bat) };
			List<WildlifeArea> plan = WildlifePlanner.Plan("Test", World, HalfForest, null, null, caves, species, limits);
			LogAssert.AreEqual(2, plan.Count, "two colonies in the one cave, none out in the forest, none in the ice");
			LogAssert.IsTrue(plan[0].Den && Mathf.Approximately(plan[0].Center.x, 520f), "the darkest floor first");
			LogAssert.IsTrue(Mathf.Approximately(plan[1].Center.x, 600f), "the next one far enough from it");
			foreach (WildlifeArea colony in plan)
			{
				float floorY = colony.Center.x > 550f ? -60f : -42f;
				float top = colony.Center.y + colony.Size.y * 0.5f, bottom = colony.Center.y - colony.Size.y * 0.5f;
				LogAssert.IsTrue(top > floorY && top < floorY + 8f, "its cast starts in the cave's air, under the roof");
				LogAssert.IsTrue(bottom < floorY, "and reaches the floor");
				LogAssert.IsTrue(colony.Count >= bat.GroupMin && colony.Count <= bat.GroupMax, "a colony is the species' group, not the catalogue's");
			}
		}

		[Test]
		public void OpenSpeciesAreNotColonies()
		{
			WildlifeTemplate deer = Species(WildlifeRole.Prey, BiomeHabitat.Forest, 1f);
			WildlifeCatalogue limits = Limits();
			limits.CaveColonyChance = 1f;
			var caves = new List<SceneCaveLifeSpot> { Floor(3, new Vector3(500f, -40f, 500f), 0.9f, 8f) };
			var species = new List<WildlifePlanner.Species> { new WildlifePlanner.Species(null, deer) };
			List<WildlifeArea> plan = WildlifePlanner.Plan("Test", World, HalfForest, null, null, caves, species, limits);
			LogAssert.IsFalse(plan.Exists(a => a.Den), "no colony of deer");
			LogAssert.IsTrue(plan.Count > 0, "deer still roam the forest");
		}

		[Test]
		public void TheGroundMapKeepsEachCellsStrongestKind()
		{
			// 4x4 splat texels, two layers (grass, rock): rock covers the right half, grass the left.
			var maps = new float[4, 4, 2];
			for (int y = 0; y < 4; y++)
			{
				for (int x = 0; x < 4; x++)
				{
					maps[y, x, 0] = x < 2 ? 0.8f : 0.1f;
					maps[y, x, 1] = 1f - maps[y, x, 0];
				}
			}
			byte[] grid = TerrainGroundKinds.Bake(maps, new[] { SurfaceKind.Grass, SurfaceKind.Stone }, 2);
			LogAssert.AreEqual(4, grid.Length, "2x2 cells");
			LogAssert.AreEqual((byte)SurfaceKind.Grass, grid[0], "left cell grass");
			LogAssert.AreEqual((byte)SurfaceKind.Stone, grid[1], "right cell rock");
			LogAssert.AreEqual((byte)SurfaceKind.Stone, grid[3], "the upper row the same");
			LogAssert.AreEqual(SurfaceKind.Leaves, SurfaceNames.OfName("Ground_ForestFloor", SurfaceKind.Dirt), "layers named as footsteps read them");
		}

		[Test]
		public void AWingedBodyHasWingsInPlaceOfForelegs()
		{
			WildlifeBodySpec bat = WildlifeBodySpec.Deer;
			bat.WingSpan = 2.6f;
			bat.HoverHeight = 1.8f;
			Mesh mesh = WildlifeBodyMesh.Build(bat);
			made.Add(mesh);
			var parts = new List<Vector4>();
			mesh.GetUVs(1, parts);
			var seen = new HashSet<int>();
			float widest = 0f;
			var positions = new List<Vector3>();
			mesh.GetVertices(positions);
			for (int i = 0; i < parts.Count; ++i)
			{
				seen.Add(Mathf.RoundToInt(parts[i].w));
				widest = Mathf.Max(widest, Mathf.Abs(positions[i].x));
			}
			LogAssert.IsTrue(seen.Contains((int)WildlifeBodyMesh.Part.LeftWing) && seen.Contains((int)WildlifeBodyMesh.Part.RightWing), "both wings");
			LogAssert.IsFalse(seen.Contains((int)WildlifeBodyMesh.Part.FrontLeft) || seen.Contains((int)WildlifeBodyMesh.Part.FrontRight), "no forelegs");
			LogAssert.IsTrue(seen.Contains((int)WildlifeBodyMesh.Part.HindLeft), "hind legs kept");
			LogAssert.IsTrue(widest > 1.1f && widest <= 1.4f, $"the wings span it ({widest * 2f:0.00} m)");
		}

		[Test]
		public void AFlyerHoversSwoopsToBiteAndFallsWhenDead()
		{
			WildlifeBodySpec bat = WildlifeBodySpec.Deer;
			bat.WingSpan = 2.6f;
			bat.HoverHeight = 1.8f;
			WildlifeFlight idle = WildlifeFlight.Evaluate(bat, 8f, 0f, 1.2f, 0f, 0f);
			WildlifeFlight biting = WildlifeFlight.Evaluate(bat, 8f, 0f, 1.2f, 1f, 0f);
			WildlifeFlight fast = WildlifeFlight.Evaluate(bat, 8f, 8f, 1.2f, 0f, 0f);
			WildlifeFlight dead = WildlifeFlight.Evaluate(bat, 8f, 0f, 1.2f, 0f, 1f);
			WildlifeFlight deer = WildlifeFlight.Evaluate(WildlifeBodySpec.Deer, 10f, 5f, 1.2f, 0f, 0f);
			LogAssert.IsTrue(Mathf.Abs(idle.Height - 1.8f) < 0.2f, "hovers at its height");
			LogAssert.IsTrue(biting.Height < idle.Height - 0.8f, "swoops down to bite");
			LogAssert.IsTrue(fast.BeatsPerSecond > idle.BeatsPerSecond && idle.BeatsPerSecond > 0f, "beats faster at speed");
			LogAssert.IsTrue(dead.Height == 0f && dead.WingAngle == 0f && dead.BeatsPerSecond == 0f, "dead on the floor, wings flat");
			LogAssert.IsTrue(deer.Height == 0f && deer.WingAngle == 0f, "nothing for a wingless animal");
		}
	}
}
