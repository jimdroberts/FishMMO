using System.Collections.Generic;
using FishMMO.Server.Implementation.World.SceneServer.Spawner;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using NUnit.Framework;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

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
	}
}
