using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// A cloud world's airships (PointOfInterestSkyTraffic, <see cref="SkyLanePlanner"/>, <see cref="FloatingBoats"/> airborne):
	/// lanes that start and end at the stages and keep out of every island's rock, a ship that lies still at its stage
	/// until it swings, a rider carried from the deck up, and pieces whose collider is a deck to stand on and a rail to
	/// keep riders aboard.
	/// </summary>
	public class SkyTrafficTests
	{
		private const float Width = 4000f, Depth = 4000f;

		/// <summary>Margins the test holds a lane to: a little inside the planner's own, for the rounding of its corners.</summary>
		private const float SideRoom = 30f, OverRoom = 30f, UnderRoom = 20f;

		/// <summary>Two islands with stages facing each other across a third, which stands in the way at their height.</summary>
		private static SkyLanePlanner.Island[] Islands() => new[]
		{
			new SkyLanePlanner.Island { Centre = new Vector2(-1100f, 0f), Radius = 250f, Top = 380f, Keel = 60f },
			new SkyLanePlanner.Island { Centre = new Vector2(1100f, 0f), Radius = 250f, Top = 360f, Keel = 40f },
			new SkyLanePlanner.Island { Centre = new Vector2(0f, 0f), Radius = 320f, Top = 430f, Keel = 150f },
			// High overhead, far above the band: a lane flies under it, never round it.
			new SkyLanePlanner.Island { Centre = new Vector2(-400f, 700f), Radius = 200f, Top = 1300f, Keel = 1000f },
		};

		private static SkyLanePlanner.Dock DockA => new SkyLanePlanner.Dock { Island = 0, Position = new Vector3(-838f, 350.1f, 0f), Outward = new Vector2(1f, 0f) };
		private static SkyLanePlanner.Dock DockB => new SkyLanePlanner.Dock { Island = 1, Position = new Vector3(838f, 330.1f, 0f), Outward = new Vector2(-1f, 0f) };

		private static float BowReach => 26f * 0.5f + StructurePieces.AirshipBowPlank;

		[Test]
		public void ALaneBetweenStagesStartsAndEndsAtTheirBerths()
		{
			var planner = new SkyLanePlanner(Islands(), Width, Depth);
			Assert.IsTrue(planner.TryPort(DockA, BowReach, out SkyLanePlanner.End a));
			Assert.IsTrue(planner.TryPort(DockB, BowReach, out SkyLanePlanner.End b));
			FloatingBoats.Lane lane = planner.Plan(a, b, 400f);
			Assert.IsNotNull(lane, "a way round the middle island");
			Assert.IsTrue(lane.PortAtStart && lane.PortAtEnd);
			Assert.Greater(lane.BerthSwing, 0f, "a ship holds still at a stage before it swings");

			// The ship's root at the berth: its deck level with the stage's, its plank's tip just off the stage's end.
			Vector3 first = lane.Points[0], last = lane.Points[lane.Points.Length - 1];
			Assert.AreEqual(DockA.Position.y, first.y, 1e-4f);
			Assert.AreEqual(DockB.Position.y, last.y, 1e-4f);
			Assert.AreEqual(DockA.Position.x + BowReach + SkyLanePlanner.BerthGap, first.x, 1e-3f);
			Assert.AreEqual(DockB.Position.x - BowReach - SkyLanePlanner.BerthGap, last.x, 1e-3f);
			Assert.AreEqual(0f, first.z, 1e-3f);

			// It comes in straight along the stage's line, level: the last leg points back at the stage.
			Vector3 inbound = last - lane.Points[lane.Points.Length - 2];
			Assert.AreEqual(0f, inbound.y, 1e-3f);
			Assert.Greater(Vector3.Dot(new Vector3(inbound.x, 0f, inbound.z).normalized, new Vector3(1f, 0f, 0f)), 0.999f);
			Assert.GreaterOrEqual(inbound.magnitude, SkyLanePlanner.MinApproach - 1e-3f);

			AssertClear(lane, Islands(), new[] { DockA, DockB });
			AssertMeasured(lane);
		}

		[Test]
		public void ALaneRoutesRoundAnIslandInItsWayAndUnderOneAboveIt()
		{
			var planner = new SkyLanePlanner(Islands(), Width, Depth);
			planner.TryPort(DockA, BowReach, out SkyLanePlanner.End a);
			planner.TryPort(DockB, BowReach, out SkyLanePlanner.End b);
			FloatingBoats.Lane lane = planner.Plan(a, b, 400f);
			Assert.IsNotNull(lane);
			// Somewhere it passes the middle island's centre line off to one side, past its reach.
			bool passed = false;
			foreach (Vector3 p in lane.Points)
			{
				if (Mathf.Abs(p.x) < 50f)
				{
					Assert.Greater(Mathf.Abs(p.z), 320f + SideRoom, "round the middle island, not through it");
					passed = true;
				}
			}
			Assert.IsTrue(passed, "the lane crosses from one side of the scene to the other");
			Assert.IsTrue(planner.Blocks(2, 330f, 450f), "the middle island's column reaches the lane's band");
			Assert.IsFalse(planner.Blocks(3, 330f, 450f), "the high island is far over the band");
		}

		[Test]
		public void ALaneToTheEdgeEndsPastItAtItsCruise()
		{
			var planner = new SkyLanePlanner(Islands(), Width, Depth);
			Assert.IsTrue(planner.TryPort(DockB, BowReach, out SkyLanePlanner.End a));
			float cruise = a.Berth.y + 40f;
			Assert.IsTrue(planner.TryEdge(new Vector2(a.Lead.x, a.Lead.z), new Vector2(1f, 0f), a.Berth.y, cruise, out SkyLanePlanner.End b));
			Assert.IsFalse(b.Port);
			FloatingBoats.Lane lane = planner.Plan(a, b, cruise);
			Assert.IsNotNull(lane);
			Assert.IsTrue(lane.PortAtStart);
			Assert.IsFalse(lane.PortAtEnd);
			Vector3 last = lane.Points[lane.Points.Length - 1];
			Assert.Greater(last.x, Width * 0.5f, "it waits past the scene's edge, out of sight");
			AssertClear(lane, Islands(), new[] { DockB });
			AssertMeasured(lane);
			// Climbing no steeper than the planner allows, eased.
			for (int i = 2; i < lane.Points.Length - 1; i++)
			{
				Vector3 run = lane.Points[i] - lane.Points[i - 1];
				float flat = new Vector2(run.x, run.z).magnitude;
				if (flat > 1f)
				{
					Assert.LessOrEqual(Mathf.Abs(run.y) / flat, SkyLanePlanner.Climb * 1.6f, $"leg {i} climbs too steeply");
				}
			}
		}

		[Test]
		public void ASecondShipsHigherLaneKeepsTheSameBerthsAndStaysClear()
		{
			var planner = new SkyLanePlanner(Islands(), Width, Depth);
			planner.TryPort(DockA, BowReach, out SkyLanePlanner.End a);
			planner.TryPort(DockB, BowReach, out SkyLanePlanner.End b);
			FloatingBoats.Lane lane = planner.Plan(a, b, 400f);
			FloatingBoats.Lane higher = planner.Raised(lane, 34f);
			Assert.IsNotNull(higher);
			Assert.AreEqual(lane.Points[0], higher.Points[0]);
			Assert.AreEqual(lane.Points[lane.Points.Length - 1], higher.Points[higher.Points.Length - 1]);
			Assert.AreEqual(lane.Points[1], higher.Points[1], "the same level run out of the stage");
			int middle = lane.Points.Length / 2;
			Assert.AreEqual(lane.Points[middle].y + 34f, higher.Points[middle].y, 0.5f, "over the open air it flies higher");
			AssertClear(higher, Islands(), new[] { DockA, DockB });
		}

		/// <summary>
		/// A lane's two ships, half a round apart, the second on the higher copy at its scaled speed (as the stage sails them):
		/// wherever they come near each other across the ground, one is well over the other, and they never share a berth.
		/// </summary>
		[Test]
		public void ALanesTwoShipsPassOneOverTheOther()
		{
			var planner = new SkyLanePlanner(Islands(), Width, Depth);
			planner.TryPort(DockA, BowReach, out SkyLanePlanner.End a);
			planner.TryPort(DockB, BowReach, out SkyLanePlanner.End b);
			FloatingBoats.Lane lane = planner.Plan(a, b, 400f);
			FloatingBoats.Lane higher = planner.Raised(lane, 34f);
			var first = new FloatingBoats.Sailing { Speed = 5f, DwellStart = 300f, DwellEnd = 300f };
			double period = FloatingBoats.RoundSeconds(lane, first);
			var second = new FloatingBoats.Sailing { Speed = 5f * higher.Length / lane.Length, DwellStart = 300f, DwellEnd = 300f, Offset = (float)(0.5 * period) };
			Assert.AreEqual(period, FloatingBoats.RoundSeconds(higher, second), 1e-2, "the same round, so they never drift");
			for (double t = 0.0; t < 2.0 * period; t += 2.0)
			{
				FloatingBoats.ShipPose p = FloatingBoats.CourseAt(lane, first, t), q = FloatingBoats.CourseAt(higher, second, t);
				float across = new Vector2(p.Position.x - q.Position.x, p.Position.z - q.Position.z).magnitude;
				if (across < 40f)
				{
					Assert.Greater(Mathf.Abs(p.Position.y - q.Position.y), 25f, $"at {t:0} s they meet at one height");
				}
			}
		}

		[Test]
		public void AStageFacingAnotherIslandCloseByGetsNoLane()
		{
			SkyLanePlanner.Island[] islands =
			{
				new SkyLanePlanner.Island { Centre = new Vector2(0f, 0f), Radius = 200f, Top = 300f, Keel = 50f },
				new SkyLanePlanner.Island { Centre = new Vector2(420f, 0f), Radius = 150f, Top = 300f, Keel = 50f },
			};
			var planner = new SkyLanePlanner(islands, Width, Depth);
			var dock = new SkyLanePlanner.Dock { Island = 0, Position = new Vector3(212f, 280f, 0f), Outward = new Vector2(1f, 0f) };
			Assert.IsFalse(planner.TryPort(dock, BowReach, out _), "its way out runs into the neighbour");
			dock.Outward = new Vector2(0f, 1f);
			dock.Position = new Vector3(0f, 280f, 212f);
			Assert.IsTrue(planner.TryPort(dock, BowReach, out _), "facing open air it has one");
		}

		// ── The ship at its stage ──────────────────────────────────────────────────────────────────────────────

		[Test]
		public void AtABerthTheShipLiesStillUntilItsSwing()
		{
			// No berth swing: the sea's whole-wait swing, unchanged.
			Assert.AreEqual(Mathf.SmoothStep(0f, 1f, 100f / 300f), FloatingBoats.SwingShare(100f, 300f, 0f));
			Assert.AreEqual(0f, FloatingBoats.SwingShare(0f, 300f, 40f));
			Assert.AreEqual(0f, FloatingBoats.SwingShare(259f, 300f, 40f), "still until the last 40 s");
			Assert.Greater(FloatingBoats.SwingShare(280f, 300f, 40f), 0f);
			Assert.Less(FloatingBoats.SwingShare(280f, 300f, 40f), 1f);
			Assert.AreEqual(1f, FloatingBoats.SwingShare(300f, 300f, 40f), 1e-6f);
			// A wait shorter than the swing swings through all of it.
			Assert.AreEqual(Mathf.SmoothStep(0f, 1f, 10f / 30f), FloatingBoats.SwingShare(10f, 30f, 40f), 1e-6f);

			var lane = new FloatingBoats.Lane
			{
				Points = new[] { new Vector3(0f, 300f, 0f), new Vector3(1000f, 320f, 0f) },
				Along = new[] { 0f, Vector3.Distance(new Vector3(0f, 300f, 0f), new Vector3(1000f, 320f, 0f)) },
				PortAtStart = true,
				PortAtEnd = true,
				BerthSwing = 40f,
			};
			var ship = new FloatingBoats.Sailing { Speed = 5f, DwellStart = 300f, DwellEnd = 300f };
			// The leg out: its time at full speed less the port ramps (Covered's share).
			double leg = lane.Length / ship.Speed / (1f - 0.12f);
			FloatingBoats.ShipPose arrived = FloatingBoats.CourseAt(lane, ship, leg + 1.0);
			FloatingBoats.ShipPose waiting = FloatingBoats.CourseAt(lane, ship, leg + 250.0);
			FloatingBoats.ShipPose swung = FloatingBoats.CourseAt(lane, ship, leg + 299.9);
			Assert.AreEqual(lane.Points[1], arrived.Position);
			Assert.AreEqual(lane.Points[1], waiting.Position);
			Assert.IsFalse(waiting.UnderWay);
			Assert.AreEqual(arrived.Yaw, waiting.Yaw, 1e-3f, "lying still alongside the stage");
			Assert.AreEqual(180f, Mathf.Abs(Mathf.DeltaAngle(arrived.Yaw, swung.Yaw)), 0.5f, "and turned end for end by the time it leaves");
		}

		[Test]
		public void AnAirshipCarriesARiderStandingOnItsDeck()
		{
			var before = new FloatingBoats.ShipPose { Position = new Vector3(0f, 300f, 0f), Yaw = 0f, Shown = true, UnderWay = true };
			var now = new FloatingBoats.ShipPose { Position = new Vector3(0.2f, 300.05f, 0f), Yaw = 0f, Shown = true, UnderWay = true };
			var deck = new Bounds(new Vector3(0f, 4f, 0f), new Vector3(28f, 16f, 8f));
			var rider = new Vector3(2f, 300.0f, 1f);
			// The deck is the airship's y 0: on the water's rule a rider there would be under the waterline's lowest feet.
			Assert.IsFalse(FloatingBoats.TryCarry(before, now, deck, rider, 1f / 30f, out _, out _));
			Assert.IsTrue(FloatingBoats.TryCarry(before, now, deck, rider, 1f / 30f, float.NegativeInfinity, out Vector3 velocity, out _));
			Assert.AreEqual(6f, velocity.x, 1e-3f);
			Assert.AreEqual(1.5f, velocity.y, 1e-3f, "carried up as the ship climbs");
		}

		// ── The pieces ───────────────────────────────────────────────────────────────────────────────────────

		[Test]
		public void AirshipsReachTheirPlankAndStagesAreTheirLength()
		{
			Assert.IsTrue(StructurePieces.TryAirship("SkySkiff", out float skiff, out _, out float skiffReach));
			Assert.IsTrue(StructurePieces.TryAirship("SkyCog", out float cog, out _, out float cogReach));
			Assert.AreEqual(14f, skiff);
			Assert.AreEqual(26f, cog);
			Assert.AreEqual(skiff * 0.5f + StructurePieces.AirshipBowPlank, skiffReach);
			Assert.AreEqual(cog * 0.5f + StructurePieces.AirshipBowPlank, cogReach);
			Assert.IsFalse(StructurePieces.TryAirship("Cog", out _, out _, out _));
			Assert.AreEqual(StructureAnchor.Deck, StructureKit.Get("SkyDock").Anchor);
			Assert.AreEqual(StructureAnchor.Deck, StructureKit.Get("SkyCog").Anchor);
		}

		/// <summary>
		/// The collider (the coarsest level, under way): a deck at y = 0 from the stern to the plank's tip, a rail along both
		/// sides at hand height, and the way open over the plank's tip; and the stage's walkway at y = 0 its whole length.
		/// </summary>
		[Test]
		public void TheCollidersAreADeckToStandOnAndARailToKeepRidersAboard()
		{
			foreach (string id in new[] { "SkySkiff", "SkyCog" })
			{
				StructurePieces.TryAirship(id, out float length, out float beam, out float reach);
				MeshBuilder mesh = StructurePieces.Build(StructureKit.Get(id), 0, StructurePieces.LevelCount - 1, StructurePieces.DefaultSeed).Mesh;
				for (float x = -length * 0.5f + 1.5f; x <= reach - 0.3f; x += 0.5f)
				{
					// Down from 1.5 m over the deck (inside the cog's aftcastle, whose floor is the deck).
					Assert.IsTrue(FirstHit(mesh, new Vector3(x, 1.5f, 0.3f), Vector3.down, 3f, out float t), $"{id}: nothing under x {x}");
					float y = 1.5f - t;
					if (y > 0.05f)
					{
						// The aftcastle's roof over the stern: walked under, not on.
						Assert.Less(x, -length * 0.5f + 5f, $"{id}: something at {y:0.00} m over the deck at x {x}");
						continue;
					}
					Assert.AreEqual(0f, y, 0.05f, $"{id}: the deck at x {x}");
				}
				for (int s = -1; s <= 1; s += 2)
				{
					Assert.IsTrue(FirstHit(mesh, new Vector3(-1f, StructurePieces.AirshipRail, 0f), new Vector3(0f, 0f, s), beam, out _), $"{id}: no rail on side {s}");
				}
				Assert.IsFalse(FirstHit(mesh, new Vector3(length * 0.5f - 1f, 0.8f, 0f), Vector3.right, reach, out _), $"{id}: the way over the plank's tip is blocked");
			}
			MeshBuilder stage = StructurePieces.Build(StructureKit.Get("SkyDock"), 0, StructurePieces.LevelCount - 1, StructurePieces.DefaultSeed).Mesh;
			for (float z = -StructurePieces.SkyDockLength * 0.5f + 0.2f; z <= StructurePieces.SkyDockLength * 0.5f - 0.2f; z += 0.5f)
			{
				Assert.IsTrue(FirstHit(stage, new Vector3(0.3f, 1.5f, z), Vector3.down, 3f, out float t));
				Assert.AreEqual(0f, 1.5f - t, 0.05f, $"the stage's walkway at z {z}");
			}
			Assert.IsFalse(FirstHit(stage, new Vector3(0f, 0.8f, 0f), Vector3.forward, 10f, out _), "the stage's end is open");
			Assert.IsTrue(FirstHit(stage, new Vector3(0f, 0.99f, 2f), Vector3.right, 4f, out _), "a rail along its side");
		}

		// ── Helpers ──────────────────────────────────────────────────────────────────────────────────────────

		/// <summary>Every 2 m of the lane outside every island's column, but a port's own island along its level run out.</summary>
		private static void AssertClear(FloatingBoats.Lane lane, SkyLanePlanner.Island[] islands, SkyLanePlanner.Dock[] docks)
		{
			for (float s = 0f; s <= lane.Length; s += 2f)
			{
				Vector3 p = FloatingBoats.PointAt(lane, s);
				for (int i = 0; i < islands.Length; i++)
				{
					if (OnRunOut(p, i, docks))
					{
						continue;
					}
					SkyLanePlanner.Island island = islands[i];
					bool beside = (new Vector2(p.x, p.z) - island.Centre).magnitude >= island.Radius + SideRoom;
					bool over = p.y >= island.Top + OverRoom, under = p.y <= island.Keel - UnderRoom;
					Assert.IsTrue(beside || over || under, $"at {s:0} m ({p}) the lane is in island {i}'s rock");
				}
			}
		}

		/// <summary>Whether a point is on a stage's own line straight out over the void, at its deck's height.</summary>
		private static bool OnRunOut(Vector3 p, int island, SkyLanePlanner.Dock[] docks)
		{
			foreach (SkyLanePlanner.Dock dock in docks)
			{
				if (dock.Island != island)
				{
					continue;
				}
				Vector2 from = new Vector2(p.x - dock.Position.x, p.z - dock.Position.z);
				Vector2 outward = dock.Outward.normalized;
				float along = Vector2.Dot(from, outward);
				float aside = Mathf.Abs(from.x * outward.y - from.y * outward.x);
				if (along >= 0f && aside < 0.5f && Mathf.Abs(p.y - dock.Position.y) < 0.5f)
				{
					return true;
				}
			}
			return false;
		}

		private static void AssertMeasured(FloatingBoats.Lane lane)
		{
			Assert.AreEqual(lane.Points.Length, lane.Along.Length);
			Assert.AreEqual(0f, lane.Along[0]);
			for (int i = 1; i < lane.Points.Length; i++)
			{
				Assert.AreEqual(lane.Along[i - 1] + Vector3.Distance(lane.Points[i - 1], lane.Points[i]), lane.Along[i], 0.01f);
			}
		}

		/// <summary>The nearest of a mesh's triangles a ray meets within <paramref name="reach"/>, by its distance along it.</summary>
		private static bool FirstHit(MeshBuilder mesh, Vector3 origin, Vector3 direction, float reach, out float distance)
		{
			distance = float.PositiveInfinity;
			List<Vector3> p = mesh.Positions;
			foreach (List<int> triangles in mesh.Submeshes)
			{
				for (int t = 0; t + 2 < triangles.Count; t += 3)
				{
					Vector3 a = p[triangles[t]], e1 = p[triangles[t + 1]] - a, e2 = p[triangles[t + 2]] - a;
					Vector3 h = Vector3.Cross(direction, e2);
					float det = Vector3.Dot(e1, h);
					if (Mathf.Abs(det) < 1e-9f)
					{
						continue;
					}
					float inv = 1f / det;
					Vector3 o = origin - a;
					float u = Vector3.Dot(o, h) * inv;
					if (u < 0f || u > 1f)
					{
						continue;
					}
					Vector3 q = Vector3.Cross(o, e1);
					float v = Vector3.Dot(direction, q) * inv;
					if (v < 0f || u + v > 1f)
					{
						continue;
					}
					float d = Vector3.Dot(e2, q) * inv;
					if (d >= 0f && d <= reach && d < distance)
					{
						distance = d;
					}
				}
			}
			return distance <= reach;
		}
	}
}
