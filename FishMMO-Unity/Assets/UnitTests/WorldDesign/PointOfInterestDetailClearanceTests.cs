using System.Collections.Generic;
using System.IO;
using FishMMO.Shared;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Every detail layer emptied under the buildings at the cut (2026-10-27, PointOfInterestDetailClearance): Moron 2's houses
	/// had grass, flowers, ferns and bushes standing inside them, because the buildings were kept clear only by the path field
	/// and the CPU chunk renderer, which draws most of a biome-suite scene's detail kinds, never reads it.
	/// </summary>
	[TestFixture]
	public class PointOfInterestDetailClearanceTests
	{
		private const float Cell = 0.5f;
		private const int Side = 48;

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

		private static ScenePath Yard(Vector3 a, Vector3 b, float half, ScenePathPointFlags shape)
		{
			byte flags = (byte)(ScenePathPointFlags.InSite | shape);
			return new ScenePath
			{
				Class = ScenePathClass.Yard,
				Points = new[] { a, b },
				HalfWidth = new[] { half, half },
				Wear = new[] { 1f, 1f },
				Surface = new[] { (byte)ScenePathSurface.Earth, (byte)ScenePathSurface.Earth },
				Flags = new[] { flags, flags },
			};
		}

		/// <summary>A 10 × 6 m house in the middle of a 24 m square: its floor runs end midpoint to end midpoint along x.</summary>
		private static List<PointOfInterestTreeClearance.Floor> House()
		{
			var paths = new List<ScenePath>
			{
				// Its apron (no floor: the grass round it is the path field's to fray) and its floor.
				Yard(new Vector3(6.4f, 0f, 12f), new Vector3(17.6f, 0f, 12f), 3.6f, ScenePathPointFlags.Squared),
				Yard(new Vector3(7f, 0f, 12f), new Vector3(17f, 0f, 12f), 3f, ScenePathPointFlags.Squared | ScenePathPointFlags.Floor),
			};
			return PointOfInterestTreeClearance.FloorsOf(paths);
		}

		private static int[,] Full(int value)
		{
			var map = new int[Side, Side];
			for (int j = 0; j < Side; j++)
			{
				for (int i = 0; i < Side; i++)
				{
					map[j, i] = value;
				}
			}
			return map;
		}

		private static float HalfDiagonal => 0.5f * Mathf.Sqrt(2f) * Cell;

		/// <summary>
		/// The regression: whichever renderer draws a kind (blades, the GPU scatter, the chunk renderer), its layer holds nothing
		/// under a floor once cleared. A blade, a fern, a mushroom and a bush, each with its own reach.
		/// </summary>
		[Test]
		public void EveryKindIsEmptiedUnderAFloor()
		{
			List<PointOfInterestTreeClearance.Floor> floors = House();
			float[,] edge = PointOfInterestDetailClearance.EdgeField(floors, Vector2.zero, Side, Side, Cell, Cell, 4f);
			var kinds = new (float reach, int block)[]
			{
				(PointOfInterestDetailClearance.Reach(0.05f, false), 1),
				(PointOfInterestDetailClearance.Reach(0.5f, false), 1),
				(PointOfInterestDetailClearance.Reach(0f, false), 1),
				(PointOfInterestDetailClearance.Reach(1.1f, true), 4),
			};
			foreach ((float reach, int block) in kinds)
			{
				int[,] map = Full(5);
				int emptied = PointOfInterestDetailClearance.ClearLayer(map, edge, reach, HalfDiagonal, block);
				Assert.That(emptied, Is.GreaterThan(0));
				for (int j = 0; j < Side; j++)
				{
					for (int i = 0; i < Side; i++)
					{
						var centre = new Vector2((i + 0.5f) * Cell, (j + 0.5f) * Cell);
						float d = Mathf.Infinity;
						foreach (PointOfInterestTreeClearance.Floor floor in floors)
						{
							d = Mathf.Min(d, PointOfInterestTreeClearance.Edge(floor, centre));
						}
						if (d < reach)
						{
							Assert.That(map[j, i], Is.Zero, $"reach {reach}: cell ({i}, {j}), {d:0.00} m from the walls, still grows");
						}
						else if (d > reach + HalfDiagonal + block * Cell * 1.5f)
						{
							Assert.That(map[j, i], Is.EqualTo(5), $"reach {reach}: cell ({i}, {j}), {d:0.00} m out, was cleared");
						}
					}
				}
			}
		}

		[Test]
		public void APlantBesideTheWallIsKeptOffIt()
		{
			List<PointOfInterestTreeClearance.Floor> floors = House();
			float[,] edge = PointOfInterestDetailClearance.EdgeField(floors, Vector2.zero, Side, Side, Cell, Cell, 4f);
			int[,] fern = Full(3);
			PointOfInterestDetailClearance.ClearLayer(fern, edge, 0.6f, HalfDiagonal, 1);
			// The wall at x = 17: the cell 0.25 m out (centre 17.25) leans in, the one 2.25 m out does not.
			int row = 24;
			Assert.That(fern[row, 34], Is.Zero, "rooted against the wall");
			Assert.That(fern[row, 38], Is.EqualTo(3), "two paces out");
		}

		[Test]
		public void ABushIsClearedByWholeBlocks()
		{
			List<PointOfInterestTreeClearance.Floor> floors = House();
			float[,] edge = PointOfInterestDetailClearance.EdgeField(floors, Vector2.zero, Side, Side, Cell, Cell, 6f);
			int[,] bush = Full(1);
			PointOfInterestDetailClearance.ClearLayer(bush, edge, 1f, HalfDiagonal, 4);
			for (int bj = 0; bj < Side; bj += 4)
			{
				for (int bi = 0; bi < Side; bi += 4)
				{
					int sum = 0;
					for (int j = bj; j < bj + 4; j++)
					{
						for (int i = bi; i < bi + 4; i++)
						{
							sum += bush[j, i];
						}
					}
					Assert.That(sum == 0 || sum == 16, Is.True, $"block ({bi}, {bj}) part cleared: the scatter would stand its bushes anywhere in it");
				}
			}
		}

		[Test]
		public void EmptyLayersAndFarCellsAreLeftAlone()
		{
			List<PointOfInterestTreeClearance.Floor> floors = House();
			float[,] edge = PointOfInterestDetailClearance.EdgeField(floors, new Vector2(500f, 500f), Side, Side, Cell, Cell, 4f);
			int[,] map = Full(2);
			Assert.That(PointOfInterestDetailClearance.ClearLayer(map, edge, 0.5f, HalfDiagonal, 1), Is.Zero, "no floor near this tile");
			Assert.That(PointOfInterestDetailClearance.ClearLayer(new int[Side, Side], PointOfInterestDetailClearance.EdgeField(floors, Vector2.zero, Side, Side, Cell, Cell, 4f), 0.5f, HalfDiagonal, 1), Is.Zero);
		}

		[Test]
		public void ReachIsTheFootprintHeldToItsBounds()
		{
			Assert.That(PointOfInterestDetailClearance.Reach(float.NaN, false), Is.Zero);
			Assert.That(PointOfInterestDetailClearance.Reach(0.3f, false), Is.EqualTo(0.3f));
			Assert.That(PointOfInterestDetailClearance.Reach(4f, false), Is.EqualTo(PointOfInterestDetailClearance.MaxPlantReachMetres));
			Assert.That(PointOfInterestDetailClearance.Reach(0f, true), Is.EqualTo(PointOfInterestDetailClearance.MinBushReachMetres));
			Assert.That(PointOfInterestDetailClearance.Reach(1.4f, true), Is.EqualTo(1.4f));
			Assert.That(PointOfInterestDetailClearance.Reach(9f, true), Is.EqualTo(PointOfInterestDetailClearance.MaxBushReachMetres));
			Assert.That(PointOfInterestDetailClearance.IsBush("Detail_Bush_HollyForest"), Is.True);
			Assert.That(PointOfInterestDetailClearance.IsBush("Detail_FernShuttlecock"), Is.False);
		}

		/// <summary>A keep's or a market hall's ground-floor rooms get a floor of their own; an upper storey, a stilt deck and a crypt do not.</summary>
		[Test]
		public void RoomsNoYardFlooredGetAFloor()
		{
			var paths = new List<ScenePath>
			{
				Yard(new Vector3(-4f, 0f, 0f), new Vector3(4f, 0f, 0f), 3f, ScenePathPointFlags.Squared | ScenePathPointFlags.Floor),
			};
			var rooms = new List<SceneRoom>
			{
				// On the house's floor already.
				new SceneRoom { Centre = new Vector3(0f, 1.4f, 0f), HalfSize = new Vector3(3f, 1.3f, 2f) },
				// A keep's hall, turned a quarter, on the ground.
				new SceneRoom { Centre = new Vector3(40f, 2f, 0f), Yaw = 90f, HalfSize = new Vector3(5f, 1.9f, 2f) },
				// A stilt house's room, its floor 3 m up; and a crypt below the ground.
				new SceneRoom { Centre = new Vector3(80f, 4.3f, 0f), HalfSize = new Vector3(2f, 1.2f, 2f) },
				new SceneRoom { Centre = new Vector3(120f, -3f, 0f), HalfSize = new Vector3(2f, 1.2f, 2f) },
			};
			int added = PointOfInterestDetailClearance.AddRoomFloors(paths, rooms, (x, z) => 0f);
			Assert.That(added, Is.EqualTo(1));
			ScenePath hall = paths[1];
			Assert.That(hall.Class, Is.EqualTo(ScenePathClass.Yard));
			Assert.That((ScenePathPointFlags)hall.Flags[0] & (ScenePathPointFlags.Floor | ScenePathPointFlags.Squared),
				Is.EqualTo(ScenePathPointFlags.Floor | ScenePathPointFlags.Squared));
			Assert.That(hall.Wear[0], Is.EqualTo(1f), "worn bare, as a house's floor");
			List<PointOfInterestTreeClearance.Floor> floors = PointOfInterestTreeClearance.FloorsOf(paths);
			Assert.That(floors.Count, Is.EqualTo(2));
			// Turned a quarter: its long side runs along z, out to the walls past the box.
			float far = 5f + PointOfInterestDetailClearance.RoomWallMetres - 0.05f;
			Assert.That(PointOfInterestTreeClearance.Edge(floors[1], new Vector2(40f, far)), Is.LessThan(0f));
			Assert.That(PointOfInterestTreeClearance.Edge(floors[1], new Vector2(40f + 3f, 0f)), Is.GreaterThan(0f));
			Assert.That(PointOfInterestDetailClearance.AddRoomFloors(paths, rooms, (x, z) => 0f), Is.Zero, "floored once");
		}

		/// <summary>The cut's order: room floors, then the trees and the details off every floor, before the path field is baked.</summary>
		[Test]
		public void PlacingClearsEveryDetailLayerOffTheFloors()
		{
			string stage = File.ReadAllText("Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/PointsOfInterest/PointOfInterestStage.cs");
			int linear = stage.IndexOf("PointOfInterestLinearFeatures.LayScene(", System.StringComparison.Ordinal);
			int rooms = stage.IndexOf("PointOfInterestDetailClearance.AddRoomFloors(asset.Paths, asset.Rooms", System.StringComparison.Ordinal);
			int trees = stage.IndexOf("PointOfInterestTreeClearance.Clear(terrains, asset.Paths", System.StringComparison.Ordinal);
			int details = stage.IndexOf("PointOfInterestDetailClearance.Clear(terrains, asset.Paths", System.StringComparison.Ordinal);
			int bake = stage.IndexOf("ScenePathBake.Write(", System.StringComparison.Ordinal);
			Assert.That(linear, Is.GreaterThan(0));
			Assert.That(rooms, Is.GreaterThan(linear), "rooms floored after every site and linear feature has written its yards");
			Assert.That(trees, Is.GreaterThan(rooms), "the trees kept off the room floors too");
			Assert.That(details, Is.GreaterThan(rooms), "every detail layer emptied under every floor");
			Assert.That(bake, Is.GreaterThan(details), "before the path field (and the floors in it) is baked");
		}

		/// <summary>
		/// On a real tile (Unity only): a fern no GPU renderer takes and a bush past the scatter's channels are both emptied under the
		/// house, and the meadow two paces out keeps its fern.
		/// </summary>
		[Test]
		public void ClearEmptiesEveryLayerOfATile()
		{
			// Quads (a mesh detail must have a mesh): half a metre across their middle, so the fern leans 0.7 m.
			GameObject fern = GameObject.CreatePrimitive(PrimitiveType.Quad);
			fern.name = "Detail_FernShuttlecock";
			fern.hideFlags = HideFlags.HideAndDontSave;
			made.Add(fern);
			GameObject bush = GameObject.CreatePrimitive(PrimitiveType.Quad);
			bush.name = "Detail_Bush_Elder";
			bush.hideFlags = HideFlags.HideAndDontSave;
			made.Add(bush);
			var data = new TerrainData { heightmapResolution = 33, size = new Vector3(24f, 10f, 24f) };
			made.Add(data);
			data.SetDetailResolution(Side, 8);
			data.detailPrototypes = new[]
			{
				new DetailPrototype { prototype = fern, usePrototypeMesh = true, renderMode = DetailRenderMode.VertexLit, useInstancing = true, minWidth = 1f, maxWidth = 1f },
				new DetailPrototype { prototype = bush, usePrototypeMesh = true, renderMode = DetailRenderMode.VertexLit, useInstancing = true, minWidth = 1f, maxWidth = 1f },
			};
			data.SetDetailLayer(0, 0, 0, Full(4));
			data.SetDetailLayer(0, 0, 1, Full(1));
			GameObject go = Terrain.CreateTerrainGameObject(data);
			go.hideFlags = HideFlags.HideAndDontSave;
			made.Add(go);
			var paths = new List<ScenePath>
			{
				Yard(new Vector3(7f, 0f, 12f), new Vector3(17f, 0f, 12f), 3f, ScenePathPointFlags.Squared | ScenePathPointFlags.Floor),
			};
			int cleared = PointOfInterestDetailClearance.Clear(new[,] { { go.GetComponent<Terrain>() } }, paths, null);
			Assert.That(cleared, Is.GreaterThan(0));
			int[,] ferns = data.GetDetailLayer(0, 0, Side, Side, 0), bushes = data.GetDetailLayer(0, 0, Side, Side, 1);
			// The middle of the floor (12, 12) and a corner just inside it (16.75, 9.25).
			Assert.That(ferns[24, 24], Is.Zero);
			Assert.That(ferns[18, 33], Is.Zero);
			Assert.That(bushes[24, 24], Is.Zero);
			Assert.That(bushes[18, 33], Is.Zero);
			Assert.That(ferns[24, 40], Is.EqualTo(4), "the meadow 3 m off the wall keeps its fern");
		}
	}
}
