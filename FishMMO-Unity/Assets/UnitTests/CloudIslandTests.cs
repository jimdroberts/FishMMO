using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// A scene on a gas giant is a cloud world (2026-10-14: "Cutting a scene on a gas giant shouldn't generate terrain…
	/// It should just be a cloud system… small floating islands, flying ships, and maybe flying cities"): its islands,
	/// their bridges and landing stages (CloudIslands), and the habitable layer its climate is asked about (CloudWorld).
	/// </summary>
	public class CloudIslandTests
	{
		private static CloudIslands.Plan Lay(float size, uint seed = 12345u) => CloudIslands.Lay(size, size, seed);

		[Test]
		public void TheSameSceneLaysTheSameIslands()
		{
			CloudIslands.Plan a = Lay(1500f), b = Lay(1500f);
			Assert.AreEqual(a.Islands.Count, b.Islands.Count);
			for (int i = 0; i < a.Islands.Count; i++)
			{
				Assert.AreEqual(a.Islands[i].Centre, b.Islands[i].Centre);
				Assert.AreEqual(a.Islands[i].Height, b.Islands[i].Height);
			}
			Assert.AreEqual(a.Bridges.Count, b.Bridges.Count);
			Assert.AreEqual(a.Docks.Count, b.Docks.Count);
		}

		[TestCase(500f)]
		[TestCase(1500f)]
		[TestCase(4000f)]
		public void ThereIsACityIslandAndAnArchipelagoAroundIt(float size)
		{
			CloudIslands.Plan plan = Lay(size);
			Assert.AreEqual(SceneCloudWorld.IslandKind.City, plan.Islands[0].Kind, "the city's island is laid first");
			Assert.Greater(plan.Islands.Count, 3, "more than the city");
			Assert.Greater(plan.Bridges.Count, 0, "some islands are a rope bridge apart");
		}

		[TestCase(500f)]
		[TestCase(1500f)]
		[TestCase(4000f)]
		public void IslandsKeepApartAndInsideTheScene(float size)
		{
			CloudIslands.Plan plan = Lay(size);
			for (int i = 0; i < plan.Islands.Count; i++)
			{
				CloudIslands.Island a = plan.Islands[i];
				Assert.LessOrEqual(Mathf.Abs(a.Centre.x) + a.Reach, size * 0.5f, $"island {i} reaches past the scene's edge");
				Assert.LessOrEqual(Mathf.Abs(a.Centre.y) + a.Reach, size * 0.5f, $"island {i} reaches past the scene's edge");
				Assert.That(a.Height, Is.InRange(plan.Floor, plan.Ceiling), $"island {i} floats outside the band");
				Assert.Greater(a.Height, CloudIslands.DeckTop + 100f, "well above the deck");
				for (int j = i + 1; j < plan.Islands.Count; j++)
				{
					CloudIslands.Island b = plan.Islands[j];
					Assert.GreaterOrEqual(Vector2.Distance(a.Centre, b.Centre), a.Reach + b.Reach + CloudIslands.ShortestGap - 1e-3f, $"islands {i} and {j} touch");
				}
			}
		}

		[Test]
		public void BridgesSpanOnlyWhatARopeWalkCanAndClimbGently()
		{
			CloudIslands.Plan plan = Lay(2500f);
			foreach (CloudIslands.Bridge bridge in plan.Bridges)
			{
				float span = Vector2.Distance(bridge.FromAt, bridge.ToAt);
				Assert.LessOrEqual(span, CloudIslands.LongestBridge + 1e-3f);
				CloudIslands.Island a = plan.Islands[bridge.A], b = plan.Islands[bridge.B];
				float rise = Mathf.Abs(a.GroundAt(bridge.FromAt.x, bridge.FromAt.y) - b.GroundAt(bridge.ToAt.x, bridge.ToAt.y));
				Assert.LessOrEqual(rise, span * CloudIslands.BridgeGrade + 1e-3f, "too steep to walk");
				// Each end stands on its own island.
				Assert.Less(a.Inside(bridge.FromAt.x, bridge.FromAt.y), 1f);
				Assert.Less(b.Inside(bridge.ToAt.x, bridge.ToAt.y), 1f);
			}
		}

		[Test]
		public void LandingStagesFaceOpenSky()
		{
			CloudIslands.Plan plan = Lay(2500f);
			Assert.Greater(plan.Docks.Count, 0, "the city at least has a landing stage");
			foreach (CloudIslands.Dock dock in plan.Docks)
			{
				Vector2 out30 = dock.Edge + dock.Outward * 30f;
				Assert.AreEqual(-1, plan.IslandAt(out30.x, out30.y), "a stage reaching into another island");
				Assert.AreEqual(1f, dock.Outward.magnitude, 1e-4f);
			}
		}

		[Test]
		public void OnlyTheIslandsAreSolidAndTheVoidTakesTheirRimsHeight()
		{
			var tiles = new TerrainTilePlan { CountX = 1, CountZ = 1, TileMetres = 500f, Resolution = 129 };
			CloudIslands.Plan plan = CloudIslands.Lay(tiles.WidthMetres, tiles.DepthMetres, 777u);
			int per = tiles.Resolution - 1;
			SceneHeightField field = SceneHeightField.FromMetres(tiles, new float[(per + 1) * (per + 1)]);
			bool[] solid = CloudIslands.Shape(plan, field);
			Assert.AreEqual(per * per, solid.Length);
			int island = 0;
			for (int z = 0; z < per; z++)
			{
				for (int x = 0; x < per; x++)
				{
					float east = 0.5f * (field.EastOf(x) + field.EastOf(x + 1)), north = 0.5f * (field.NorthOf(z) + field.NorthOf(z + 1));
					if (solid[z * per + x])
					{
						island++;
						Assert.GreaterOrEqual(plan.IslandAt(east, north), 0, "a solid cell over the void");
					}
				}
			}
			Assert.Greater(island, per * per / 10, "a fair share of the sky is island");
			Assert.Less(island, per * per * 6 / 10, "most of it is sky");
			field.Range(out float lowest, out float highest);
			Assert.GreaterOrEqual(lowest, plan.Floor - 5f, "no sample drops toward the deck: the void holds its rims' height");
		}

		/// <summary>The ground of a plan's islands at a point, NaN over the void: what the cut reads off the holed terrain.</summary>
		private static System.Func<float, float, float> GroundOf(CloudIslands.Plan plan)
			=> (east, north) =>
			{
				int i = plan.IslandAt(east, north);
				return i < 0 ? float.NaN : plan.Islands[i].GroundAt(east, north);
			};

		[TestCase(500f)]
		[TestCase(1500f)]
		[TestCase(4000f)]
		public void AirshipsFindLanesAmongTheIslands(float size)
		{
			int scenes = 0, served = 0, lanes = 0, stages = 0;
			for (uint seed = 1; seed <= 12; seed++)
			{
				CloudIslands.Plan plan = Lay(size, seed * 7919u);
				System.Func<float, float, float> ground = GroundOf(plan);
				SceneCloudWorld.Island[] islands = CloudWorldCut.IslandRecords(plan, out float lowest, out float highest);
				SceneCloudWorld.Dock[] docks = CloudWorldCut.DockRecords(plan, ground);
				var planner = new SkyLanePlanner(PointOfInterestSkyTraffic.Islands(islands, ground), size, size);
				var kinds = new System.Collections.Generic.List<string>();
				var planned = PointOfInterestSkyTraffic.PlanLanes(planner, docks, CloudIslands.DeckTop, lowest, highest, size, size,
					new DeterministicRNG((int)seed), kinds);
				scenes++;
				stages += docks.Length;
				lanes += planned.Count;
				served += planned.Count > 0 ? 1 : 0;
			}
			System.Console.WriteLine($"{size} m: {served}/{scenes} scenes have lanes; {lanes} lanes over {stages} stages.");
			Assert.GreaterOrEqual(served, scenes * 3 / 4, $"{size} m: only {served} of {scenes} scenes have an airship lane ({lanes} lanes over {stages} stages)");
		}

		[Test]
		public void AGasGiantsScenesAreInATemperateWetLayer()
		{
			var giant = ScriptableObject.CreateInstance<WorldBody>();
			try
			{
				giant.Kind = WorldBodyKind.GasGiant;
				giant.Atmosphere = AtmosphereKind.Thick;
				giant.Water = 0f;
				Assert.IsTrue(CloudWorld.Is(giant));
				Assert.AreEqual(CloudWorld.LayerKelvin, ClimateModel.MeanSurfaceKelvin(null, giant), 1e-6);
				Assert.AreEqual(AtmosphereKind.Standard, CloudWorld.AtmosphereOf(giant), "the layer's air is breathable");
				Assert.Greater(CloudWorld.WaterOf(giant), 0.4f, "and wet: it is cloud all round");
				BiomeWorldConditions conditions = BiomeWorldConditions.For(null, giant);
				Assert.AreEqual(AtmosphereKind.Standard, conditions.Atmosphere);
				Assert.IsTrue(conditions.HasLiquidWater, "water condenses in the layer");
			}
			finally
			{
				Object.DestroyImmediate(giant);
			}
		}
	}
}
