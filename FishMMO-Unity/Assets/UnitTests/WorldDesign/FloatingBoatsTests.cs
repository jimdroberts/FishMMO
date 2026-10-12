using System;
using FishMMO.Shared;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>The sailing ships' arithmetic (<see cref="FloatingBoats"/>): how far along its lane a ship is, and where that is.</summary>
	public class FloatingBoatsTests
	{
		[Test]
		public void ALegWithNoPortEndsRunsAtAnEvenSpeed()
		{
			for (int i = 0; i <= 10; i++)
			{
				Assert.AreEqual(i / 10f, FloatingBoats.Covered(i / 10f, 0f, 0f), 1e-5f);
			}
		}

		[Test]
		public void AShipGathersWayOutOfPortAndLosesItComingIn()
		{
			const float k = 0.12f;
			float share = 1f - k;
			// Its whole leg covered at the end, by the share the ramps leave.
			Assert.AreEqual(share, FloatingBoats.Covered(1f, k, k), 1e-5f);
			// Slow off the mark and slow at the end: less than a full speed's distance in the first and last moments.
			Assert.Less(FloatingBoats.Covered(0.05f, k, k), 0.05f * 0.6f);
			Assert.Less(share - FloatingBoats.Covered(0.95f, k, k), 0.05f * 0.6f);
			// Never going backwards.
			float last = -1f;
			for (int i = 0; i <= 200; i++)
			{
				float s = FloatingBoats.Covered(i / 200f, k, k);
				Assert.GreaterOrEqual(s, last - 1e-6f);
				last = s;
			}
		}

		[Test]
		public void ALanesPointIsFoundByItsDistanceAlong()
		{
			var lane = new FloatingBoats.Lane
			{
				Points = new[] { new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f), new Vector3(10f, 0f, 20f) },
				Along = new[] { 0f, 10f, 30f },
			};
			Assert.AreEqual(30f, lane.Length);
			Assert.AreEqual(new Vector3(5f, 0f, 0f), FloatingBoats.PointAt(lane, 5f));
			Assert.AreEqual(new Vector3(10f, 0f, 10f), FloatingBoats.PointAt(lane, 20f));
			Assert.AreEqual(new Vector3(0f, 0f, 0f), FloatingBoats.PointAt(lane, -3f));
			Assert.AreEqual(new Vector3(10f, 0f, 20f), FloatingBoats.PointAt(lane, 99f));
		}

		// ── Solid ships (2026-10-12: "Solid sailing ships needs to be done.") ──────────────────────────────────────

		private const double TickDelta = 1.0 / 30.0;

		/// <summary>A lane with a right-angle bend, from a port (where it waits at anchor) to the open sea (where it waits out of sight).</summary>
		private static FloatingBoats.Lane PortToSea()
		{
			return new FloatingBoats.Lane
			{
				Points = new[] { new Vector3(0f, 0f, 0f), new Vector3(400f, 0f, 0f), new Vector3(400f, 0f, 400f) },
				Along = new[] { 0f, 400f, 800f },
				PortAtStart = true,
				PortAtEnd = false,
			};
		}

		private static FloatingBoats.Sailing Cog(float offset = 0f)
		{
			return new FloatingBoats.Sailing
			{
				Lane = 0,
				Speed = 3.5f,
				DwellStart = 240f,
				DwellEnd = 300f,
				Offset = offset,
				Length = 24f,
				// A cog's hull in its own frame: waterline y 0, bow +x, its deck some way above the water.
				Deck = new Bounds(new Vector3(0f, 1f, 0f), new Vector3(24f, 6f, 7f)),
			};
		}

		/// <summary>The out-leg's time, the far-end dwell's start and the start dwell's start, seconds into a round.</summary>
		private static void Round(FloatingBoats.Lane lane, FloatingBoats.Sailing ship, out double leg, out double period)
		{
			float share = 1f - 0.5f * ((lane.PortAtStart ? 0.12f : 0f) + (lane.PortAtEnd ? 0.12f : 0f));
			leg = lane.Length / ship.Speed / share;
			period = 2.0 * leg + ship.DwellStart + ship.DwellEnd;
		}

		private static FloatingBoats.ShipPose At(FloatingBoats.Lane lane, FloatingBoats.Sailing ship, double tick)
			=> FloatingBoats.CourseAt(lane, ship, FloatingBoats.SecondsAt(tick, TickDelta));

		private static void AssertSamePose(FloatingBoats.ShipPose expected, FloatingBoats.ShipPose actual, string what)
		{
			Assert.AreEqual(expected.Position, actual.Position, what);
			Assert.AreEqual(expected.Yaw, actual.Yaw, what);
			Assert.AreEqual(expected.Shown, actual.Shown, what);
			Assert.AreEqual(expected.UnderWay, actual.UnderWay, what);
		}

		[Test]
		public void AShipsPoseIsAFunctionOfTheTickAlone()
		{
			FloatingBoats.Lane lane = PortToSea();
			FloatingBoats.Sailing ship = Cog(37f);
			// Asked twice, in any order, on any peer: the same answer, to the bit. Nothing about a ship is ever sent.
			for (uint tick = 1_000_000u; tick < 1_000_000u + 90_000u; tick += 997u)
			{
				FloatingBoats.ShipPose first = At(lane, ship, tick);
				At(lane, ship, tick + 12345u);
				AssertSamePose(first, At(lane, ship, tick), $"tick {tick}");
			}
		}

		[Test]
		public void ReplayingATickStandsTheHullWhereTheServerHadItOnThatTick()
		{
			FloatingBoats.Lane lane = PortToSea();
			FloatingBoats.Sailing ship = Cog();
			Round(lane, ship, out double leg, out double period);
			// The last reconcile paired the client's tick 1000 with a server tick hours into the session, the ship under way
			// then: input 1000 ran on the server on that tick.
			const uint clientState = 1000u;
			uint serverState = (uint)Math.Floor((period * 120.0 + leg * 0.4) / TickDelta);
			for (uint replayed = clientState + 1u; replayed < clientState + 40u; replayed++)
			{
				uint server = serverState + (replayed - clientState);
				uint synced = FloatingBoats.SyncTick(false, 0u, 123u, clientState, serverState, replayed);
				Assert.AreEqual(server, synced, $"client tick {replayed} maps to the server tick its input runs on");
				// The server posed the hull at its own tick; the client's replay of the same input poses it identically.
				uint onServer = FloatingBoats.SyncTick(true, server, 0u, 0u, 0u, 777u);
				Assert.AreEqual(server, onServer, "the server poses at its own tick, whatever the input's label");
				AssertSamePose(At(lane, ship, onServer), At(lane, ship, synced), $"replayed tick {replayed}");
			}
			// Control: the hull at the live tick is somewhere else, so a replay that probed it would meet the wrong deck.
			FloatingBoats.ShipPose replay = At(lane, ship, serverState + 1u), live = At(lane, ship, serverState + 40u);
			Assert.Greater((replay.Position - live.Position).magnitude, 0.5f, "the control must move, or the test proves nothing");
		}

		[Test]
		public void BeforeAnyReconcileAClientPosesAtItsSyncedEstimate()
		{
			Assert.AreEqual(5000u, FloatingBoats.SyncTick(false, 0u, 5000u, 0u, 0u, 17u));
		}

		[Test]
		public void AShipWaitingBeyondTheSceneEdgeIsHiddenAndCarriesNobody()
		{
			FloatingBoats.Lane lane = PortToSea();
			FloatingBoats.Sailing ship = Cog();
			Round(lane, ship, out double leg, out double period);
			// Mid-wait at the far end, which is open sea: out of sight, and so out of the physics.
			double tick = (leg + ship.DwellEnd * 0.5) / TickDelta;
			FloatingBoats.ShipPose before = At(lane, ship, tick - 1), now = At(lane, ship, tick);
			Assert.IsFalse(now.Shown, "a ship beyond the scene's edge is hidden (its collider off)");
			Vector3 onDeck = now.ToWorld(new Vector3(0f, 2f, 0f));
			Assert.IsFalse(FloatingBoats.TryCarry(before, now, ship.Deck, onDeck, (float)TickDelta, out Vector3 carry));
			Assert.AreEqual(Vector3.zero, carry);
			// At anchor in port it is there, solid, swinging to its cable.
			double inPort = (2.0 * leg + ship.DwellEnd + ship.DwellStart * 0.5) / TickDelta;
			FloatingBoats.ShipPose anchored = At(lane, ship, inPort);
			Assert.IsTrue(anchored.Shown);
			Assert.IsFalse(anchored.UnderWay);
			// And under way it is there too.
			Assert.IsTrue(At(lane, ship, leg * 0.5 / TickDelta).Shown);
		}

		[Test]
		public void ARiderStandingStillIsCarriedWithTheDeck_UnderWayAndThroughTheSwingAtAnchor()
		{
			FloatingBoats.Lane lane = PortToSea();
			FloatingBoats.Sailing ship = Cog();
			Round(lane, ship, out double leg, out double period);
			float dt = (float)TickDelta;
			// Under way, through the bend; then the whole wait in port, swinging 180°, then gathering way again.
			(double from, double to)[] spans =
			{
				(leg * 0.3, leg * 0.7),
				(2.0 * leg + ship.DwellEnd - 5.0, period + 20.0),
			};
			foreach ((double from, double to) in spans)
			{
				double first = Math.Floor(from / TickDelta), last = Math.Floor(to / TickDelta);
				var local = new Vector3(-6f, 1.4f, 2.1f);   // aft of midships, to starboard: the swing moves it furthest
				Vector3 rider = At(lane, ship, first).ToWorld(local);
				float turned = 0f;
				for (double tick = first + 1; tick <= last; tick++)
				{
					FloatingBoats.ShipPose before = At(lane, ship, tick - 1), now = At(lane, ship, tick);
					turned += Mathf.Abs(Mathf.DeltaAngle(before.Yaw, now.Yaw));
					Assert.IsTrue(FloatingBoats.TryCarry(before, now, ship.Deck, rider, dt, out Vector3 carry), $"aboard at tick {tick}");
					rider += carry * dt;
					Vector3 stood = now.ToLocal(rider);
					Assert.Less((stood - local).magnitude, 0.01f, $"the rider kept its place on the deck at tick {tick}");
				}
				if (from > leg)
				{
					Assert.Greater(turned, 170f, "the span must take the ship through its swing, or the test proves nothing");
				}
			}
		}

		[Test]
		public void ASwimmerAlongsideOrUnderTheHullIsNotAboard()
		{
			FloatingBoats.Lane lane = PortToSea();
			FloatingBoats.Sailing ship = Cog();
			Round(lane, ship, out double leg, out _);
			// A quarter of the way out: under way at full speed, on the straight.
			double tick = Math.Floor(leg * 0.25 / TickDelta);
			FloatingBoats.ShipPose before = At(lane, ship, tick - 1), now = At(lane, ship, tick);
			Assert.IsTrue(now.Shown && now.UnderWay);
			float dt = (float)TickDelta;
			Assert.IsFalse(FloatingBoats.TryCarry(before, now, ship.Deck, before.ToWorld(new Vector3(0f, -0.6f, 0f)), dt, out _), "feet below the waterline: swimming");
			Assert.IsFalse(FloatingBoats.TryCarry(before, now, ship.Deck, before.ToWorld(new Vector3(0f, 0.5f, 5f)), dt, out _), "beyond the beam: alongside");
			Assert.IsFalse(FloatingBoats.TryCarry(before, now, ship.Deck, before.ToWorld(new Vector3(20f, 1f, 0f)), dt, out _), "past the bow");
			Assert.IsFalse(FloatingBoats.TryCarry(before, now, ship.Deck, before.ToWorld(new Vector3(0f, 9f, 0f)), dt, out _), "far over the masthead");
			Assert.IsTrue(FloatingBoats.TryCarry(before, now, ship.Deck, before.ToWorld(new Vector3(0f, 1.5f, 0f)), dt, out Vector3 carry), "on the deck");
			Assert.AreEqual(ship.Speed, carry.magnitude, 0.05f, "carried at the ship's speed");
		}

		[Test]
		public void AShipNeverJumpsFromOneTickToTheNext()
		{
			FloatingBoats.Lane lane = PortToSea();
			FloatingBoats.Sailing ship = Cog(11f);
			Round(lane, ship, out _, out double period);
			// A whole round and a bit, so the wrap from the end of the round to the start of the next is crossed.
			long ticks = (long)(period / TickDelta) + 600;
			FloatingBoats.ShipPose last = At(lane, ship, 0);
			int compared = 0;
			for (long tick = 1; tick <= ticks; tick++)
			{
				FloatingBoats.ShipPose now = At(lane, ship, tick);
				if (now.Shown && last.Shown)
				{
					float moved = (now.Position - last.Position).magnitude;
					Assert.LessOrEqual(moved, ship.Speed * (float)TickDelta * 1.05f + 1e-3f, $"tick {tick} moved {moved} m");
					Assert.LessOrEqual(Mathf.Abs(Mathf.DeltaAngle(last.Yaw, now.Yaw)), 2f, $"tick {tick} turned too sharply");
					compared++;
				}
				last = now;
			}
			Assert.Greater(compared, ticks / 2, "most of a round is in sight");
		}

		[Test]
		public void TheDrawnShipLagsTheHullByTheRidersOwnInterpolation()
		{
			// A rider and the deck under it must be drawn at the same moment, or the deck slides under them (issue #228's
			// smoothing lesson). The rider's lag is the playable prefabs' _ownerInterpolation, pinned there.
			Assert.AreEqual((int)FishMMO.UnitTests.PlatformRiderSmoothingTests.InterpolationTicks, FloatingBoats.VisualInterpolationTicks);
		}

		/// <summary>Native (Unity's quaternion): the pose's own yaw arithmetic agrees with the rotation put on the root.</summary>
		[Test]
		public void ThePoseFrameIsTheRootsRotation()
		{
			var pose = new FloatingBoats.ShipPose { Position = new Vector3(10f, 2f, -4f), Yaw = 63f, Shown = true };
			var local = new Vector3(3f, 1.5f, -2f);
			Vector3 unity = pose.Position + Quaternion.Euler(0f, pose.Yaw, 0f) * local;
			Assert.Less((pose.ToWorld(local) - unity).magnitude, 1e-4f);
			Assert.Less((pose.ToLocal(unity) - local).magnitude, 1e-4f);
		}
	}
}
