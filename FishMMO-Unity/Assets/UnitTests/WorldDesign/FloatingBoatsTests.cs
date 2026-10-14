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

		[Test]
		public void ARidersFacingTurnsWithTheDeck_KeepingItsBearingThroughTheBendTheSwingAndTheRoundsEnd()
		{
			FloatingBoats.Lane lane = PortToSea();
			FloatingBoats.Sailing ship = Cog();
			Round(lane, ship, out double leg, out double period);
			float dt = (float)TickDelta;
			// Under way through the bend; then the swing at anchor in port and on into the next round, where the heading's
			// sum of two swings drops back a whole turn.
			(double from, double to)[] spans =
			{
				(leg * 0.3, leg * 0.7),
				(2.0 * leg + ship.DwellEnd - 5.0, period + 20.0),
			};
			bool wrapped = false;
			foreach ((double from, double to) in spans)
			{
				double first = Math.Floor(from / TickDelta), last = Math.Floor(to / TickDelta);
				var local = new Vector3(-6f, 1.4f, 2.1f);
				// Facing across the deck, to port: a bearing the swing turns furthest from any world direction.
				Vector3 bearing = new Vector3(0.6f, 0f, -0.8f);
				FloatingBoats.ShipPose start = At(lane, ship, first);
				Vector3 rider = start.ToWorld(local);
				Vector3 facing = FloatingBoats.Turned(bearing, start.Yaw);
				float turned = 0f;
				for (double tick = first + 1; tick <= last; tick++)
				{
					FloatingBoats.ShipPose before = At(lane, ship, tick - 1), now = At(lane, ship, tick);
					wrapped |= Mathf.Abs(now.Yaw - before.Yaw) > 180f;
					Assert.IsTrue(FloatingBoats.TryCarry(before, now, ship.Deck, rider, dt, out Vector3 carry, out float turn), $"aboard at tick {tick}");
					Assert.LessOrEqual(Mathf.Abs(turn), 2f, $"tick {tick}: a deck turns a little each tick, never a whole turn");
					turned += Mathf.Abs(turn);
					rider += carry * dt;
					facing = FloatingBoats.Turned(facing, turn);
					// Its place and its bearing on the deck both held: the deck turned it, its feet and its face alike.
					Assert.Less((now.ToLocal(rider) - local).magnitude, 0.01f, $"the rider kept its place on the deck at tick {tick}");
					Vector3 onDeck = FloatingBoats.Turned(facing, -now.Yaw);
					Assert.Less((onDeck - bearing).magnitude, 5e-3f, $"the rider kept its bearing on the deck at tick {tick}");
				}
				Assert.Greater(turned, 20f, "the span must turn the ship, or the test proves nothing");
				if (from > leg)
				{
					Assert.Greater(turned, 170f, "the span must take the ship through its swing");
				}
			}
			Assert.IsTrue(wrapped, "the spans must cross a heading's wrap (the round's end), or the short way round is untested");
		}

		[Test]
		public void ATurnIsTakenTheShortWayRound()
		{
			FloatingBoats.ShipPose Yaw(float yaw) => new FloatingBoats.ShipPose { Yaw = yaw, Shown = true };
			Assert.AreEqual(2f, FloatingBoats.Turn(Yaw(179f), Yaw(-179f)), 1e-4f, "across the arctangent's wrap");
			Assert.AreEqual(1f, FloatingBoats.Turn(Yaw(359.5f), Yaw(0.5f)), 1e-4f);
			Assert.AreEqual(0f, FloatingBoats.Turn(Yaw(540f), Yaw(180f)), 1e-4f, "a round's two swings, dropped at its end");
			Assert.AreEqual(-0.25f, FloatingBoats.Turn(Yaw(10f), Yaw(9.75f)), 1e-4f);
		}

		[Test]
		public void ARiderOffTheDeckIsNotTurned()
		{
			FloatingBoats.Lane lane = PortToSea();
			FloatingBoats.Sailing ship = Cog();
			Round(lane, ship, out double leg, out _);
			// Mid-swing in port, where the deck turns fastest.
			double tick = Math.Floor((2.0 * leg + ship.DwellEnd + ship.DwellStart * 0.5) / TickDelta);
			FloatingBoats.ShipPose before = At(lane, ship, tick - 1), now = At(lane, ship, tick);
			Assert.Greater(Mathf.Abs(FloatingBoats.Turn(before, now)), 0.01f, "the control: the deck is turning");
			Assert.IsFalse(FloatingBoats.TryCarry(before, now, ship.Deck, before.ToWorld(new Vector3(0f, 0.5f, 6f)), (float)TickDelta, out _, out float turn));
			Assert.AreEqual(0f, turn, "a swimmer alongside keeps its own facing");
		}

		/// <summary>
		/// SOURCE: the turn reaches the rider by the platform seam on every peer (the replicate hands it to the motor, which
		/// turns the facing), and the owner's camera turns with the drawn hull, since a character facing the camera takes its
		/// facing from the camera in its input.
		/// </summary>
		[Test]
		public void TheTurnReachesTheMotorAndTheOwnersCamera()
		{
			string motor = CodeOnly(ReadSource("Assets/Plugins/KinematicCharacterController/Core/KinematicCharacterMotor.cs"));
			Assert.IsTrue(motor.Contains("public void SetPlatformVelocity(Vector3 velocity, float turn)"), "the motor takes a platform's turn");
			Assert.IsTrue(motor.Contains("float platformTurn = GroundingStatus.IsStableOnGround ? _platformTurn : 0f;"),
				"the turn is conveyed only to a stably grounded rider, as a horizontal carry is");
			Assert.IsTrue(motor.Contains("Quaternion.AngleAxis(platformTurn, _cachedWorldUp) * _characterForward") &&
				motor.Contains("TransientRotation = Quaternion.LookRotation(turnedForward.normalized, _characterUp);"),
				"the turn goes on the motor's facing, as an attached PhysicsMover's rotation does");
			string player = CodeOnly(ReadSource("Assets/Scripts/Shared/Implementation/Entity/Prediction/KCC/KCCPlayer.cs"));
			Assert.IsTrue(player.Contains("out platformVelocity, out platformTurn, out ship);") &&
				player.Contains("Motor.SetPlatformVelocity(platformVelocity, platformTurn);"),
				"the replicate hands the ship's turn at the synced tick to the motor, live and in every replay");
			string input = CodeOnly(ReadSource("Assets/Scripts/Client/Input/PlayerInputController.cs"));
			Assert.IsTrue(input.Contains("FloatingBoats.TryGetDrawnYaw(ship, out float shipYaw)") &&
				input.Contains("camera.PlanarDirection = planar.normalized;"),
				"the owner's camera turns with the drawn ship");
			string controller = CodeOnly(ReadSource("Assets/Scripts/Shared/Implementation/Entity/Prediction/KCC/KCCController.cs"));
			string suppressed = controller.Substring(controller.IndexOf("if (controlsSuppressed)", StringComparison.Ordinal));
			Assert.IsTrue(suppressed.Substring(0, suppressed.IndexOf("return;", StringComparison.Ordinal)).Contains("lookInputVector = Vector3.zero;"),
				"with the controls taken away no look target is kept, or it turns a stunned rider back against the deck");
		}

		private static string ReadSource(string relativePath)
		{
			string path = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), relativePath);
			Assert.IsTrue(System.IO.File.Exists(path), $"{relativePath} not found at {path}.");
			return System.IO.File.ReadAllText(path).Replace("\r\n", "\n");
		}

		/// <summary>The source without its comment lines, so prose about a construct does not stand in for it.</summary>
		private static string CodeOnly(string source)
		{
			var kept = new System.Text.StringBuilder(source.Length);
			foreach (string line in source.Split('\n'))
			{
				string trimmed = line.TrimStart();
				if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith("/*", StringComparison.Ordinal) || trimmed.StartsWith("*", StringComparison.Ordinal))
				{
					continue;
				}
				kept.Append(line).Append('\n');
			}
			return kept.ToString();
		}

		/// <summary>Native (Unity's quaternion): a turn is the rotation between two poses' frames, the motor's included.</summary>
		[Test]
		public void ATurnIsTheRotationBetweenTwoPosesFrames()
		{
			var before = new FloatingBoats.ShipPose { Position = new Vector3(4f, 0f, 9f), Yaw = 171f, Shown = true };
			var now = new FloatingBoats.ShipPose { Position = new Vector3(5f, 0f, 8f), Yaw = -172.5f, Shown = true };
			float turn = FloatingBoats.Turn(before, now);
			Vector3 direction = new Vector3(0.6f, 0f, -0.8f);
			Quaternion frames = Quaternion.Euler(0f, now.Yaw, 0f) * Quaternion.Inverse(Quaternion.Euler(0f, before.Yaw, 0f));
			Assert.Less((FloatingBoats.Turned(direction, turn) - frames * direction).magnitude, 1e-4f);
			// The motor turns the facing about world up by the same angle (KinematicCharacterMotor's platform turn).
			Assert.Less((FloatingBoats.Turned(direction, turn) - Quaternion.AngleAxis(turn, Vector3.up) * direction).magnitude, 1e-4f);
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

		// ── The tick driver's savings: never at the cost of a carry ───────────────────────────────────────────────

		/// <summary>
		/// The lane's extent rejects a rider before any pose is worked out (TryCarryAt), and only one no hull on that lane
		/// could carry at any tick: it changes no answer, it only skips the arithmetic.
		/// </summary>
		[Test]
		public void TheLanesExtentRejectsOnlyRidersNoHullCouldCarry()
		{
			FloatingBoats.Lane lane = PortToSea();
			FloatingBoats.Sailing ship = Cog();
			Round(lane, ship, out _, out double period);
			Rect area = FloatingBoats.LaneArea(lane);
			Assert.AreEqual(Rect.MinMaxRect(0f, 0f, 400f, 400f), area);
			float reach = Mathf.Max(ship.Length, ship.Deck.extents.magnitude) + 2f;
			float dt = (float)TickDelta;
			int rejected = 0, carried = 0;
			for (double tick = 1; tick < period / TickDelta; tick += 97)
			{
				FloatingBoats.ShipPose before = At(lane, ship, tick - 1), now = At(lane, ship, tick);
				// Riders on and round the deck, and out across the sea in every direction.
				for (int k = 0; k < 64; k++)
				{
					float angle = k * 0.61803f * Mathf.PI * 2f;
					float distance = (k % 8) * (k % 8) * 6f;
					Vector3 rider = now.ToWorld(new Vector3(Mathf.Cos(angle) * distance, 1.5f, Mathf.Sin(angle) * distance * 0.25f));
					bool carry = FloatingBoats.TryCarry(before, now, ship.Deck, rider, dt, out _);
					carried += carry ? 1 : 0;
					if (!FloatingBoats.MayReach(area, reach, rider))
					{
						rejected++;
						Assert.IsFalse(carry, $"tick {tick}: a rider the lane's extent rejects was carried");
						Assert.Greater((now.Position - rider).magnitude, reach, $"tick {tick}: the old rejection would have let it through");
					}
				}
			}
			Assert.Greater(rejected, 0, "the control: some riders must be out of reach, or the test proves nothing");
			Assert.Greater(carried, 0, "the control: some riders must be carried, or the test proves nothing");
		}

		/// <summary>
		/// A hull's collider stands down only beyond SolidReach past its length from every character, and nothing that
		/// stands on its deck (anything it carries) is ever that far from its root: so a rider always finds it placed.
		/// </summary>
		[Test]
		public void EveryRiderAShipCanCarryIsWithinItsSolidReach()
		{
			FloatingBoats.Lane lane = PortToSea();
			FloatingBoats.Sailing ship = Cog();
			Round(lane, ship, out _, out double period);
			float solid = FloatingBoats.SolidReach + Mathf.Max(ship.Length, ship.Deck.extents.magnitude);
			float dt = (float)TickDelta;
			Vector3 min = ship.Deck.min, max = ship.Deck.max;
			for (double tick = 1; tick < period / TickDelta; tick += 211)
			{
				FloatingBoats.ShipPose before = At(lane, ship, tick - 1), now = At(lane, ship, tick);
				// The deck's corners, past its margin: the furthest a carried rider can stand.
				foreach (float x in new[] { min.x - FloatingBoats.DeckMargin, max.x + FloatingBoats.DeckMargin })
				{
					foreach (float z in new[] { min.z - FloatingBoats.DeckMargin, max.z + FloatingBoats.DeckMargin })
					{
						Vector3 rider = before.ToWorld(new Vector3(x, 1.5f, z));
						if (!FloatingBoats.TryCarry(before, now, ship.Deck, rider, dt, out _))
						{
							continue;
						}
						float across = new Vector2(rider.x - now.Position.x, rider.z - now.Position.z).magnitude;
						Assert.Less(across, solid - 50f, $"tick {tick}: a rider at the deck's corner is near the edge of the solid reach");
					}
				}
			}
		}
	}
}
