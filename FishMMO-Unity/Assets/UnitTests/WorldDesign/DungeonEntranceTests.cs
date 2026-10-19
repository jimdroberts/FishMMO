using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.NameGeneration;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Dungeon entrances (2026-10-18: "boring and misaligned"): the door is set at its face's foot, square to the face, the
	/// ground cut to a level forecourt once and only once, its interaction box in its doorway, its doorway open into its
	/// passage, and its way coming in head-on.
	/// </summary>
	[TestFixture]
	public class DungeonEntranceTests
	{
		/// <summary>A face whose foot runs through <paramref name="foot"/> facing <paramref name="yaw"/>: flat before it, 54° up behind it to 30 m.</summary>
		private static Func<float, float, float> Cliff(Vector2 foot, float yaw)
		{
			var o = new Vector2(Mathf.Sin(yaw * Mathf.Deg2Rad), Mathf.Cos(yaw * Mathf.Deg2Rad));
			return (x, z) =>
			{
				float u = Vector2.Dot(new Vector2(x, z) - foot, o);
				return u >= 0f ? 10f - 0.04f * u : 10f + Mathf.Min(30f, 1.4f * -u);
			};
		}

		private static Vector2 Dir(float yaw) => new Vector2(Mathf.Sin(yaw * Mathf.Deg2Rad), Mathf.Cos(yaw * Mathf.Deg2Rad));

		[TestCase(33f, 22.5f)]
		[TestCase(200f, 180f)]
		[TestCase(301f, 292.5f)]
		public void TheDoorFindsItsFacesFootAndTurnsSquareToIt(float faceYaw, float plannerYaw)
		{
			var foot = new Vector2(40f, -25f);
			Func<float, float, float> ground = Cliff(foot, faceYaw);
			// The planner's cell middle: out in front of the face and off to one side of where the door will be.
			Vector2 cell = foot + Dir(faceYaw) * 6.5f + new Vector2(Dir(faceYaw).y, -Dir(faceYaw).x) * 3f;
			Assert.That(DungeonEntranceShaper.FindFace(ground, new Vector3(cell.x, ground(cell.x, cell.y), cell.y), plannerYaw, out DungeonEntranceShaper.Plan plan, out string problem), Is.True, problem);
			Assert.That(Mathf.Abs(Mathf.DeltaAngle(plan.Yaw, faceYaw)), Is.LessThan(1.5f), $"square to the face, not the planner's {plannerYaw}° step");
			float u = Vector2.Dot(new Vector2(plan.Threshold.x, plan.Threshold.z) - foot, Dir(faceYaw));
			Assert.That(u, Is.EqualTo(-DungeonEntranceShaper.Recess).Within(0.35f), "the threshold stands its recess back into the face from the foot");
			Assert.That(plan.Floor, Is.InRange(9.6f, 10.01f), "the floor is the foot's, no deeper than the forecourt's fall");
			Assert.That(plan.Threshold.y, Is.EqualTo(plan.Floor));
			Assert.That(plan.Rise, Is.GreaterThan(DungeonEntranceShaper.MinFaceRise));
		}

		[Test]
		public void GroundWithNoFaceTakesNoDoor()
		{
			Func<float, float, float> slope = (x, z) => 10f + 0.2f * z;
			Assert.That(DungeonEntranceShaper.FindFace(slope, new Vector3(0f, 10f, 0f), 180f, out _, out string problem), Is.False);
			Assert.That(problem, Is.Not.Empty);
		}

		[Test]
		public void TheCutLevelsTheForecourtAndTheNotchOnceAndForAll()
		{
			const float yaw = 33f;
			var foot = new Vector2(0f, 0f);
			Func<float, float, float> ground = Cliff(foot, yaw);
			Vector2 cell = foot + Dir(yaw) * 6f;
			Assert.That(DungeonEntranceShaper.FindFace(ground, new Vector3(cell.x, 10f, cell.y), 22.5f, out DungeonEntranceShaper.Plan plan, out string problem), Is.True, problem);
			Vector2 o = Dir(plan.Yaw), side = new Vector2(o.y, -o.x), t = new Vector2(plan.Threshold.x, plan.Threshold.z);
			Rect area = DungeonEntranceShaper.CutArea(plan);
			int raisedBehind = 0;
			for (float x = area.xMin; x <= area.xMax; x += 0.7f)
			{
				for (float z = area.yMin; z <= area.yMax; z += 0.7f)
				{
					float h = ground(x, z);
					float once = DungeonEntranceShaper.CutHeight(plan, x, z, h);
					float twice = DungeonEntranceShaper.CutHeight(plan, x, z, once);
					Assert.That(twice, Is.EqualTo(once), $"cut again at ({x:0.0}, {z:0.0})");
					var p = new Vector2(x, z) - t;
					float u = Vector2.Dot(p, o), v = Mathf.Abs(Vector2.Dot(p, side));
					if (u >= 0f && u <= DungeonEntranceShaper.PadDepth && v <= DungeonEntranceShaper.PadHalf)
					{
						Assert.That(once, Is.EqualTo(plan.Floor).Within(1e-4f), $"the forecourt at u {u:0.0}, v {v:0.0}");
					}
					if (u < -DungeonEntranceShaper.NotchDepth - 0.01f || u < 0f && v > DungeonEntranceShaper.NotchHalf + 0.01f)
					{
						raisedBehind += once > h + 1e-4f ? 1 : 0;
					}
					if (u >= -DungeonEntranceShaper.NotchDepth && u < 0f && v <= DungeonEntranceShaper.NotchHalf)
					{
						Assert.That(once, Is.LessThanOrEqualTo(plan.Floor + 1e-4f), "the notch is cut down to the floor");
					}
				}
			}
			Assert.That(raisedBehind, Is.Zero, "nothing behind the threshold is ever filled: it is the face");
		}

		[Test]
		public void EveryDungeonDoorHasADoorwayInsideItsFrame()
		{
			foreach (string id in new[] { "DungeonPortal", "DungeonBarrow", "DungeonGate", "CryptEntrance" })
			{
				StructurePiece piece = StructurePieces.Find(id);
				Assert.That(piece, Is.Not.Null, id);
				for (int v = 0; v < piece.Variants; v++)
				{
					Assert.That(StructurePieces.TryDoorwayOf(piece, v, out StructureDoorway door), Is.True, $"{id} v{v}");
					Assert.That(door.Width, Is.GreaterThanOrEqualTo(1.3f), $"{id} v{v}: a person passes");
					Assert.That(door.Height, Is.GreaterThanOrEqualTo(2.25f), $"{id} v{v}: a door is at least 2.25 m (fishmmo-doors-interiors)");
					float passageStart = door.PassageCentre.z - door.PassageHalfSize.z;
					Assert.That(passageStart, Is.GreaterThan(door.Sill.z), $"{id} v{v}: the passage is behind the sill");
					Assert.That(door.PassageCentre.y - door.PassageHalfSize.y, Is.LessThan(door.Sill.y - 1f), $"{id} v{v}: the passage goes down");
					Assert.That(door.PassageCentre.y - door.PassageHalfSize.y, Is.GreaterThanOrEqualTo(-1.3f), $"{id} v{v}: no deeper than the kit lets a piece reach");
					if (id != "CryptEntrance")
					{
						Assert.That(door.Sill.z, Is.EqualTo(0f), $"{id} v{v}: anchored at its threshold");
						Assert.That(door.PassageCentre.z + door.PassageHalfSize.z, Is.LessThan(StructurePieces.DungeonRearReach), $"{id} v{v}: inside its frame");
					}
				}
			}
			Assert.That(StructurePieces.Find("Gatehouse") != null && !StructurePieces.TryDoorwayOf(StructurePieces.Find("Gatehouse"), 0, out _), Is.True, "only the dungeon doors answer");
		}

		[Test]
		public void TurnMatchesTheWayAPieceIsTurned()
		{
			foreach (float yaw in new[] { 0f, 37f, 90f, 215f, -60f })
			{
				Vector3 ahead = DungeonEntranceFeature.Turn(yaw, Vector3.forward), right = DungeonEntranceFeature.Turn(yaw, Vector3.right);
				// Quaternion.Euler(0, yaw, 0) sends +z to (sin, 0, cos) and +x to (cos, 0, −sin).
				Assert.That(Vector3.Distance(ahead, new Vector3(Mathf.Sin(yaw * Mathf.Deg2Rad), 0f, Mathf.Cos(yaw * Mathf.Deg2Rad))), Is.LessThan(1e-5f));
				Assert.That(Vector3.Distance(right, new Vector3(Mathf.Cos(yaw * Mathf.Deg2Rad), 0f, -Mathf.Sin(yaw * Mathf.Deg2Rad))), Is.LessThan(1e-5f));
			}
		}

		[TestCase(0f, 1f)]
		[TestCase(127f, 1f)]
		[TestCase(250f, 2.5f)]
		public void TheInteractionBoxFillsTheDoorwayAtTheThreshold(float yaw, float scale)
		{
			StructurePiece piece = StructurePieces.Find("DungeonPortal");
			Assert.That(StructurePieces.TryDoorwayOf(piece, 0, out StructureDoorway door), Is.True);
			var origin = new Vector3(120f, 33f, -48f);
			DungeonEntranceFeature.DoorwayBox(origin, yaw, scale, door, out Vector3 centre, out Vector3 size);
			Assert.That(size.x, Is.EqualTo(door.Width * scale).Within(1e-4f), "as wide as the doorway");
			Assert.That(size.y, Is.EqualTo(door.Height * scale).Within(1e-4f), "as tall as the doorway");
			Vector3 inward = DungeonEntranceFeature.Turn(yaw, Vector3.forward);
			// Its outer face on the threshold's line, its foot on the sill, its depth the wall's.
			Vector3 front = centre - inward * (0.5f * size.z) - Vector3.up * (0.5f * size.y);
			Vector3 sill = origin + DungeonEntranceFeature.Turn(yaw, door.Sill * scale);
			Assert.That(Vector3.Distance(front, sill), Is.LessThan(1e-3f), "the box's front foot is the sill");
			float reveal = door.PassageCentre.z - door.PassageHalfSize.z - door.Sill.z;
			Assert.That(size.z, Is.EqualTo(reveal * scale).Within(1e-4f), "through the wall to the passage");
		}

		/// <summary>
		/// Each door's doorway is open: no part of its collider (the last level) stands in the way along the door's middle,
		/// from outside it to the passage's back wall, at a stride, the doorway's middle and under its head. Needs the kit's
		/// solids (Quaternion maths): run in Unity, or under nativerun.
		/// </summary>
		[Test]
		public void EveryDungeonDoorIsOpenIntoItsPassage()
		{
			foreach (string id in new[] { "DungeonPortal", "DungeonBarrow", "DungeonGate", "CryptEntrance" })
			{
				StructurePiece piece = StructurePieces.Find(id);
				for (int v = 0; v < piece.Variants; v++)
				{
					StructurePieces.TryDoorwayOf(piece, v, out StructureDoorway door);
					List<StructureSolid> solids = StructurePieces.BuildSolids(piece, v, StructurePieces.LevelCount - 1, StructurePieces.DefaultSeed);
					float passage = door.PassageCentre.z - door.PassageHalfSize.z;
					var probes = new List<Vector3>();
					// Through the doorway and over the landing inside it, at a stride, the doorway's middle and under its head.
					for (float z = door.Sill.z - 0.8f; z <= passage + 1f; z += 0.2f)
					{
						foreach (float h in new[] { 0.4f, 0.5f * door.Height, door.Height - 0.9f })
						{
							probes.Add(new Vector3(0f, door.Sill.y + h, z));
						}
					}
					// And down at the stair's foot, before the black back wall.
					probes.Add(new Vector3(0f, door.PassageCentre.y - door.PassageHalfSize.y + 0.4f, door.PassageCentre.z + door.PassageHalfSize.z - 0.3f));
					foreach (Vector3 p in probes)
					{
						foreach (StructureSolid s in solids)
						{
							Bounds b = s.Bounds;
							Vector3 lo = b.min + Vector3.one * 0.02f, hi = b.max - Vector3.one * 0.02f;
							bool inside = p.x > lo.x && p.x < hi.x && p.y > lo.y && p.y < hi.y && p.z > lo.z && p.z < hi.z;
							Assert.That(inside, Is.False, $"{id} v{v}: a {s.Material} part ({b.min} … {b.max}) stands in the doorway at {p}");
						}
					}
				}
			}
		}

		/// <summary>A dungeon entrance's way comes in along its door's heading to the forecourt's flagstones, from whichever side it starts.</summary>
		[Test]
		public void TheWayComesToTheDoorHeadOn()
		{
			const float yaw = 128f;
			var door = new PointOfInterestRecord { Id = 2, Kind = POIType.DungeonEntrance, Position = new Vector3(0f, 10f, 0f), Radius = 15f, Yaw = yaw };
			var records = new List<PointOfInterestRecord>
			{
				new PointOfInterestRecord { Id = 1, Kind = POIType.Town, Position = new Vector3(-700f, 10f, 300f), Radius = 40f },
				door,
				new PointOfInterestRecord { Id = 3, Kind = POIType.Town, Position = new Vector3(700f, 10f, -400f), Radius = 40f },
			};
			var keepOuts = new List<PointOfInterestKeepOut>();
			foreach (PointOfInterestRecord r in records)
			{
				keepOuts.Add(new PointOfInterestKeepOut { Id = r.Id, X = r.Position.x, Z = r.Position.z, Radius = r.Radius });
			}
			var plan = new DungeonEntranceShaper.Plan { Threshold = door.Position, Yaw = yaw, Floor = 10f, Rise = 12f };
			var input = new PathPlanInput
			{
				WidthMetres = 3200f,
				DepthMetres = 3200f,
				Ground = (x, z) => 10f,
				WaterDepth = (x, z) => 0f,
				Records = records,
				KeepOuts = keepOuts,
				HasStreets = r => PathNetworkPlanner.RankOf(r.Kind) >= 3,
				Seed = 11,
				Shapes = new List<PointOfInterestShape>
				{
					new PointOfInterestShape { SiteId = 2, Shaper = DungeonEntranceShaper.ShaperName, Position = door.Position, Yaw = yaw, Values = plan.ToValues() },
				},
			};
			PathPlan paths = PathNetworkPlanner.Plan(input);
			List<ScenePath> ways = paths.Paths.FindAll(p => p.FromId == 2 || p.ToId == 2);
			Assert.That(ways, Is.Not.Empty, string.Join("\n", paths.Notes));
			Vector2 o = Dir(yaw);
			foreach (ScenePath way in ways)
			{
				Vector3 last = way.FromId == 2 ? way.Points[0] : way.Points[way.Count - 1];
				var end = new Vector2(last.x, last.z);
				Assert.That(Vector2.Distance(end, o * StructurePieces.DungeonFrontReach), Is.LessThan(0.6f), $"way {way.Id} ends on the forecourt's flagstones, at {end}");
				for (int i = 0; i < way.Count; i++)
				{
					var p = new Vector2(way.Points[i].x, way.Points[i].z);
					float along = Vector2.Dot(p, o);
					if (p.magnitude <= DungeonEntranceShaper.PadDepth + 3.5f)
					{
						Assert.That(along, Is.GreaterThan(0f), $"way {way.Id} point {i} at {p} comes round behind the door");
						Assert.That(Mathf.Abs(p.x * o.y - p.y * o.x), Is.LessThan(0.6f), $"way {way.Id} point {i} at {p} is off the door's axis");
					}
				}
			}
		}
	}
}
