using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using FishMMO.Shared;
using FishNet.Serializing;
using KinematicCharacterController;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The climbing movement mode (2026-10-13: caves and mines get ladders and ropes as a real climbing interaction):
	/// its rules run with no motor, a tick at a time, the feet moved by the velocity each tick returns, as the KCC moves
	/// them when nothing is in the way. Taking hold, climbing along the line, stepping off and on at the top, letting go at
	/// the bottom and by a jump, and the same inputs giving the same climb (what prediction and replay need).
	/// </summary>
	[TestFixture]
	public class ClimbingTests
	{
		private const float Dt = 1f / 30f;
		private const float Hang = 0.45f;

		/// <summary>A 6 m ladder against a wall at z = 10 (the upper floor is beyond it, at y = 6): its climber hangs at −z.</summary>
		private static SceneClimbable WallLadder() => new SceneClimbable
		{
			Kind = SceneClimbableKind.Ladder,
			Bottom = new Vector3(10f, 0f, 10f),
			Top = new Vector3(10f, 6f, 10f),
			Out = new Vector3(0f, 0f, -1f),
			TopExit = new Vector3(0f, 0f, 1f),
			HalfWidth = 0.3f,
		};

		/// <summary>A free rope hanging 8 m down a shaft from its edge; the upper floor is toward +x.</summary>
		private static SceneClimbable ShaftRope() => new SceneClimbable
		{
			Kind = SceneClimbableKind.Rope,
			Bottom = new Vector3(0f, -8f, 0f),
			Top = new Vector3(0f, 0f, 0f),
			Out = Vector3.zero,
			TopExit = new Vector3(1f, 0f, 0f),
			HalfWidth = 0.1f,
		};

		private static ClimbIntent Climbing(float up, float side = 0f) => new ClimbIntent { Up = up, Side = side };

		/// <summary>Runs <paramref name="ticks"/> ticks (or until it lets go), moving the feet by each tick's velocity.</summary>
		private static Vector3 Run(in SceneClimbable c, ref ClimbState state, Vector3 feet, ClimbIntent intent, int ticks, out bool released, Func<Vector3, bool> floorBelow = null)
		{
			released = false;
			for (int i = 0; i < ticks; i++)
			{
				ClimbIntent tick = intent;
				tick.FloorBelow = floorBelow != null && floorBelow(feet);
				ClimbStep step = ClimbingRules.Step(in c, ref state, feet, in tick, Hang, 1f, Dt);
				feet += step.Velocity * Dt;
				if (step.Released)
				{
					released = true;
					break;
				}
			}
			return feet;
		}

		private static ClimbState HoldingAt(int index = 0)
		{
			ClimbState state = ClimbState.Free;
			ClimbingRules.Attach(ref state, index, ClimbPhase.Holding, Dt);
			return state;
		}

		// ── Taking hold ──────────────────────────────────────────────────────

		[Test]
		public void WalkingIntoALadderTakesHoldOfIt()
		{
			SceneClimbable ladder = WallLadder();
			Assert.That(ClimbingRules.TryAttach(in ladder, new Vector3(10f, 0f, 9.2f), Vector3.forward, true, Hang, out ClimbPhase phase, out _), Is.True);
			Assert.That(phase, Is.EqualTo(ClimbPhase.Holding));
		}

		[Test]
		public void ALadderIsNotTakenWalkingPastItAwayFromItOrOutOfReach()
		{
			SceneClimbable ladder = WallLadder();
			Vector3 front = new Vector3(10f, 0f, 9.2f);
			Assert.That(ClimbingRules.TryAttach(in ladder, front, Vector3.back, true, Hang, out _, out _), Is.False, "walking away");
			Assert.That(ClimbingRules.TryAttach(in ladder, front, Vector3.right, true, Hang, out _, out _), Is.False, "walking past");
			Assert.That(ClimbingRules.TryAttach(in ladder, front, Vector3.zero, true, Hang, out _, out _), Is.False, "standing still");
			Assert.That(ClimbingRules.TryAttach(in ladder, new Vector3(10f, 0f, 8.8f), Vector3.forward, true, Hang, out _, out _), Is.False, "1.2 m off: out of reach");
			Assert.That(ClimbingRules.TryAttach(in ladder, new Vector3(11.2f, 0f, 9.5f), Vector3.forward, true, Hang, out _, out _), Is.False, "beside it, past its width and reach");
			Assert.That(ClimbingRules.TryAttach(in ladder, new Vector3(10f, 5.5f, 9.5f), Vector3.forward, false, Hang, out _, out _), Is.False, "above the top hold");
		}

		[Test]
		public void ALadderCanBeCaughtInTheAirPartWayUp()
		{
			SceneClimbable ladder = WallLadder();
			Assert.That(ClimbingRules.TryAttach(in ladder, new Vector3(10.2f, 2.5f, 9.4f), Vector3.forward, false, Hang, out ClimbPhase phase, out _), Is.True);
			Assert.That(phase, Is.EqualTo(ClimbPhase.Holding));
		}

		[Test]
		public void WalkingOffTheTopOntoALadderStepsOnToIt()
		{
			SceneClimbable ladder = WallLadder();
			Vector3 landing = new Vector3(10f, 6f, 10.5f);
			Assert.That(ClimbingRules.TryAttach(in ladder, landing, Vector3.back, true, Hang, out ClimbPhase phase, out _), Is.True);
			Assert.That(phase, Is.EqualTo(ClimbPhase.SteppingOn));
			Assert.That(ClimbingRules.TryAttach(in ladder, landing, Vector3.forward, true, Hang, out _, out _), Is.False, "walking away from the edge");
			Assert.That(ClimbingRules.TryAttach(in ladder, landing, Vector3.back, false, Hang, out _, out _), Is.False, "not standing on the upper floor");
			Assert.That(ClimbingRules.TryAttach(in ladder, new Vector3(10f, 6f, 12f), Vector3.back, true, Hang, out _, out _), Is.False, "too far back from the edge");
		}

		[Test]
		public void ARopeIsTakenFromWhicheverSideItIsReached()
		{
			SceneClimbable rope = ShaftRope();
			Assert.That(ClimbingRules.TryAttach(in rope, new Vector3(-0.7f, -8f, 0f), Vector3.right, true, Hang, out ClimbPhase phase, out _), Is.True, "from -x");
			Assert.That(phase, Is.EqualTo(ClimbPhase.Holding));
			Assert.That(ClimbingRules.TryAttach(in rope, new Vector3(0f, -8f, 0.7f), Vector3.back, true, Hang, out _, out _), Is.True, "from +z");
			Assert.That(ClimbingRules.TryAttach(in rope, new Vector3(0f, -8f, 0.7f), Vector3.right, true, Hang, out _, out _), Is.False, "moving past it");
		}

		[Test]
		public void AClimbableThatDoesNotRiseIsNeverTaken()
		{
			SceneClimbable flat = WallLadder();
			flat.Top = flat.Bottom + new Vector3(0f, 0.2f, 3f);
			Assert.That(ClimbingRules.IsValid(in flat), Is.False);
			Assert.That(ClimbingRules.TryAttach(in flat, new Vector3(10f, 0f, 9.2f), Vector3.forward, true, Hang, out _, out _), Is.False);
		}

		// ── On it ────────────────────────────────────────────────────────────

		[Test]
		public void ForwardClimbsUpTheLineAtClimbingSpeed()
		{
			SceneClimbable ladder = WallLadder();
			ClimbState state = HoldingAt();
			// From where the grab left it, a little off: a few ticks to settle onto the hold, then a second's climb.
			Vector3 feet = Run(in ladder, ref state, new Vector3(10.1f, 0f, 9.3f), Climbing(0f), 15, out _);
			Assert.That(feet.z, Is.EqualTo(10f - Hang).Within(0.01f), "hangs off the line by its hang");
			float startY = feet.y;
			feet = Run(in ladder, ref state, feet, Climbing(1f), 30, out bool released);
			Assert.That(released, Is.False);
			Assert.That(feet.y - startY, Is.EqualTo(Constants.Character.ClimbSpeed).Within(0.02f), "a second climbs ClimbSpeed metres");
			Assert.That(feet.z, Is.EqualTo(10f - Hang).Within(0.01f));
			Assert.That(state.Phase, Is.EqualTo(ClimbPhase.Holding));
		}

		[Test]
		public void ARopeIsClimbedSlowerThanALadder()
		{
			SceneClimbable rope = ShaftRope();
			ClimbState state = HoldingAt();
			Vector3 feet = Run(in rope, ref state, new Vector3(-Hang, -7f, 0f), Climbing(1f), 30, out _);
			Assert.That(feet.y - -7f, Is.EqualTo(Constants.Character.ClimbSpeed * ClimbingRules.KindShare(SceneClimbableKind.Rope)).Within(0.02f));
			Assert.That(new Vector2(feet.x, feet.z).magnitude, Is.EqualTo(Hang).Within(0.01f), "kept at the hang off the rope, on its side");
		}

		[Test]
		public void SidewaysShufflesAcrossALadderButNoFurtherThanItsWidth()
		{
			SceneClimbable ladder = WallLadder();
			ClimbState state = HoldingAt();
			Vector3 feet = Run(in ladder, ref state, new Vector3(10f, 2f, 10f - Hang), Climbing(0f, 1f), 90, out _);
			// The climber faces +z (into the wall), so its right is −x... and the shuffle stops at the half width either way.
			Assert.That(Mathf.Abs(feet.x - 10f), Is.EqualTo(ladder.HalfWidth).Within(0.01f));
			Assert.That(feet.y, Is.EqualTo(2f).Within(0.01f), "shuffling does not climb");
		}

		[Test]
		public void ClimbingPastTheTopStepsOffOntoTheLanding()
		{
			SceneClimbable ladder = WallLadder();
			ClimbState state = HoldingAt();
			Vector3 feet = Run(in ladder, ref state, new Vector3(10f, 3f, 10f - Hang), Climbing(1f), 300, out bool released);
			Assert.That(released, Is.True, "the climb ends on the upper floor");
			Assert.That(state.IsClimbing, Is.False);
			Vector3 landing = ClimbingRules.Landing(in ladder);
			Assert.That(landing, Is.EqualTo(new Vector3(10f, 6f, 10f + Constants.Character.ClimbLandingMetres)));
			Assert.That(new Vector2(feet.x - landing.x, feet.z - landing.z).magnitude, Is.LessThanOrEqualTo(ClimbingRules.ArriveMetres + 1e-4f));
			Assert.That(feet.y, Is.EqualTo(6f + Constants.Character.ClimbStepClearance).Within(0.03f), "just clear of the floor, which the ground then takes");
			Assert.That(state.Ticks, Is.GreaterThan(0), "a short wait before it takes hold again");
		}

		[Test]
		public void TheStepOffRisesClearOfTheEdgeBeforeGoingOverIt()
		{
			SceneClimbable ladder = WallLadder();
			ClimbState state = HoldingAt();
			Vector3 feet = new Vector3(10f, 4.9f, 10f - Hang);
			for (int i = 0; i < 300; i++)
			{
				ClimbIntent up = Climbing(1f);
				ClimbStep step = ClimbingRules.Step(in ladder, ref state, feet, in up, Hang, 1f, Dt);
				feet += step.Velocity * Dt;
				if (feet.z > 10f - 0.05f)
				{
					// Over the wall's line: only ever with the feet above the upper floor.
					Assert.That(feet.y, Is.GreaterThan(6f), $"tick {i}: crossed the edge below the floor");
				}
				if (step.Released)
				{
					return;
				}
			}
			Assert.Fail("never stepped off");
		}

		[Test]
		public void GoingDownToTheFootLetsGoOntoTheFloor()
		{
			SceneClimbable ladder = WallLadder();
			ClimbState state = HoldingAt();
			Vector3 feet = Run(in ladder, ref state, new Vector3(10f, 1f, 10f - Hang), Climbing(-1f), 200, out bool released);
			Assert.That(released, Is.True);
			Assert.That(state.IsClimbing, Is.False);
			Assert.That(feet.y, Is.EqualTo(0f).Within(ClimbingRules.SettleMetres + 0.07f));
		}

		[Test]
		public void AFootSetBelowTheFloorStillLetsGoWhereTheFloorIs()
		{
			// The climb's foot buried 0.4 m: the feet meet the floor (y = 0) first, which the caller's probe reports.
			SceneClimbable ladder = WallLadder();
			ladder.Bottom = new Vector3(10f, -0.4f, 10f);
			ClimbState state = HoldingAt();
			Vector3 feet = Run(in ladder, ref state, new Vector3(10f, 1.5f, 10f - Hang), Climbing(-1f), 200, out bool released, at => at.y <= 0.05f);
			Assert.That(released, Is.True);
			Assert.That(feet.y, Is.GreaterThanOrEqualTo(-0.01f));
		}

		[Test]
		public void HoldingStillStaysPut()
		{
			SceneClimbable ladder = WallLadder();
			ClimbState state = HoldingAt();
			Vector3 start = new Vector3(10f, 2f, 10f - Hang);
			Vector3 feet = Run(in ladder, ref state, start, Climbing(0f), 120, out bool released);
			Assert.That(released, Is.False);
			Assert.That((feet - start).magnitude, Is.LessThan(1e-4f), "no gravity on a ladder");
		}

		[Test]
		public void CrouchSlidesDownFasterThanClimbing()
		{
			SceneClimbable ladder = WallLadder();
			ClimbState state = HoldingAt();
			Vector3 feet = Run(in ladder, ref state, new Vector3(10f, 5f, 10f - Hang), new ClimbIntent { Slide = true }, 15, out _);
			Assert.That(5f - feet.y, Is.EqualTo(Constants.Character.ClimbSlideSpeed * 0.5f).Within(0.05f));
		}

		[Test]
		public void AJumpLetsGoAndPushesAwayFromTheLadder()
		{
			SceneClimbable ladder = WallLadder();
			ClimbState state = HoldingAt();
			ClimbIntent jump = new ClimbIntent { Jump = true, Up = 1f };
			ClimbStep step = ClimbingRules.Step(in ladder, ref state, new Vector3(10f, 3f, 10f - Hang), in jump, Hang, 1f, Dt);
			Assert.That(step.Released && step.Jumped, Is.True);
			Assert.That(state.IsClimbing, Is.False);
			Assert.That(step.Velocity.z, Is.LessThan(0f), "away from the wall, along Out");
			Assert.That(step.Velocity.y, Is.GreaterThan(0f));
			Assert.That(state.Ticks, Is.EqualTo(ClimbingRules.TicksFor(Constants.Character.ClimbJumpRegrabSeconds, Dt)), "not caught again at once");
		}

		[Test]
		public void SteppingOnFromTheTopEndsHoldingAtTheTopHold()
		{
			SceneClimbable ladder = WallLadder();
			ClimbState state = ClimbState.Free;
			Vector3 feet = new Vector3(10f, 6f, 10.5f);
			Assert.That(ClimbingRules.TryAttach(in ladder, feet, Vector3.back, true, Hang, out ClimbPhase phase, out _), Is.True);
			ClimbingRules.Attach(ref state, 0, phase, Dt);
			for (int i = 0; i < 200 && state.Phase == ClimbPhase.SteppingOn; i++)
			{
				ClimbIntent none = default;
				ClimbStep step = ClimbingRules.Step(in ladder, ref state, feet, in none, Hang, 1f, Dt);
				Assert.That(step.Released, Is.False, $"tick {i}");
				feet += step.Velocity * Dt;
				if (feet.y < 6f - 0.05f)
				{
					Assert.That(feet.z, Is.LessThan(10f - 0.3f), $"tick {i}: went down through the edge");
				}
			}
			Assert.That(state.Phase, Is.EqualTo(ClimbPhase.Holding));
			Assert.That(feet.y, Is.EqualTo(6f - Constants.Character.ClimbTopHoldMetres).Within(0.06f));
			// And on down to the floor.
			Run(in ladder, ref state, feet, Climbing(-1f), 400, out bool released);
			Assert.That(released, Is.True);
		}

		[Test]
		public void FacingIsIntoTheLadderThenTheWayOffOverTheTop()
		{
			SceneClimbable ladder = WallLadder();
			Assert.That((ClimbingRules.Facing(in ladder, ClimbPhase.Holding, new Vector3(10f, 2f, 9.55f)) - Vector3.forward).magnitude, Is.LessThan(1e-5f), "into the wall");
			Assert.That((ClimbingRules.Facing(in ladder, ClimbPhase.SteppingOff, new Vector3(10f, 6.1f, 9.8f)) - Vector3.forward).magnitude, Is.LessThan(1e-5f), "over the top, the way off");
			SceneClimbable rope = ShaftRope();
			Assert.That((ClimbingRules.Facing(in rope, ClimbPhase.Holding, new Vector3(0f, -4f, -0.45f)) - Vector3.forward).magnitude, Is.LessThan(1e-5f), "into the rope from its -z side");
			Assert.That((ClimbingRules.Facing(in rope, ClimbPhase.SteppingOn, new Vector3(0.5f, 0f, 0f)) - Vector3.right).magnitude, Is.LessThan(1e-5f), "stepping onto a rope from the top: facing back to the floor it came from");
		}

		[Test]
		public void MovedFarOffItTheHoldIsLost()
		{
			SceneClimbable ladder = WallLadder();
			ClimbState state = HoldingAt();
			ClimbIntent none = default;
			ClimbStep step = ClimbingRules.Step(in ladder, ref state, new Vector3(30f, 2f, 30f), in none, Hang, 1f, Dt);
			Assert.That(step.Released, Is.True);
			Assert.That(step.Velocity, Is.EqualTo(Vector3.zero), "no glide back from a teleport");
		}

		[Test]
		public void TheWaitAfterLettingGoRunsDown()
		{
			ClimbState state = ClimbState.Free;
			ClimbingRules.Release(ref state, Constants.Character.ClimbRegrabSeconds, Dt);
			int ticks = state.Ticks;
			Assert.That(ticks, Is.EqualTo(Mathf.CeilToInt(Constants.Character.ClimbRegrabSeconds / Dt)));
			for (int i = 0; i < ticks; i++)
			{
				ClimbingRules.TickCooldown(ref state);
			}
			Assert.That(state.Ticks, Is.EqualTo(0));
		}

		[Test]
		public void PushingIntoTheLadderClimbsAndAwayGoesDownWhicheverWayTheCameraLooks()
		{
			SceneClimbable ladder = WallLadder();
			Vector3 feet = new Vector3(10f, 2f, 10f - Hang);
			ClimbingRules.Axes(in ladder, ClimbPhase.Holding, feet, Vector3.forward, out float up, out float side);
			Assert.That(up, Is.EqualTo(1f).Within(1e-5f), "into the wall");
			Assert.That(side, Is.EqualTo(0f).Within(1e-5f));
			ClimbingRules.Axes(in ladder, ClimbPhase.Holding, feet, Vector3.back, out up, out _);
			Assert.That(up, Is.EqualTo(-1f).Within(1e-5f), "away from it");
			ClimbingRules.Axes(in ladder, ClimbPhase.Holding, feet, Vector3.left, out up, out side);
			Assert.That(up, Is.EqualTo(0f).Within(1e-5f));
			Assert.That(Mathf.Abs(side), Is.EqualTo(1f).Within(1e-5f), "along it: across the ladder");
		}

		[Test]
		public void WalkingOnOffTheTopCarriesOnDownWithTheSamePush()
		{
			// Walked forward off the edge (the push is away from the wall, toward the pit): once on, the same push goes down,
			// so a character does not climb straight back up and off again.
			SceneClimbable ladder = WallLadder();
			Vector3 feet = new Vector3(10f, 6f, 10.5f);
			Assert.That(ClimbingRules.TryAttach(in ladder, feet, Vector3.back, true, Hang, out ClimbPhase phase, out _), Is.True);
			ClimbState state = ClimbState.Free;
			ClimbingRules.Attach(ref state, 0, phase, Dt);
			for (int i = 0; i < 400; i++)
			{
				ClimbingRules.Axes(in ladder, state.Phase, feet, Vector3.back, out float up, out float side);
				ClimbIntent intent = new ClimbIntent { Up = up, Side = side, Move = Vector3.back };
				ClimbStep step = ClimbingRules.Step(in ladder, ref state, feet, in intent, Hang, 1f, Dt);
				feet += step.Velocity * Dt;
				Assert.That(state.Phase != ClimbPhase.SteppingOff, Is.True, $"tick {i}: went back up");
				if (step.Released)
				{
					Assert.That(feet.y, Is.EqualTo(0f).Within(0.1f), "down at the foot");
					return;
				}
			}
			Assert.Fail("never reached the foot");
		}

		// ── Determinism ──────────────────────────────────────────────────────

		private static List<(Vector3 feet, ClimbState state)> Script(in SceneClimbable c, Vector3 feet, ClimbState state, int from, int to)
		{
			var trace = new List<(Vector3, ClimbState)>();
			for (int i = from; i < to; i++)
			{
				// A scripted mix: climb, shuffle, pause, slide, climb on over the top.
				ClimbIntent intent = new ClimbIntent
				{
					Up = i < 40 ? 1f : i < 60 ? 0f : i < 70 ? -1f : 1f,
					Side = i >= 20 && i < 35 ? 0.6f : 0f,
					Slide = i >= 62 && i < 66,
				};
				ClimbStep step = ClimbingRules.Step(in c, ref state, feet, in intent, Hang, 0.9f, Dt);
				feet += step.Velocity * Dt;
				trace.Add((feet, state));
				if (step.Released)
				{
					break;
				}
			}
			return trace;
		}

		[Test]
		public void TheSameInputsGiveTheSameClimb()
		{
			SceneClimbable ladder = WallLadder();
			List<(Vector3 feet, ClimbState state)> a = Script(in ladder, new Vector3(10.2f, 0f, 9.3f), HoldingAt(), 0, 400);
			List<(Vector3 feet, ClimbState state)> b = Script(in ladder, new Vector3(10.2f, 0f, 9.3f), HoldingAt(), 0, 400);
			Assert.That(a.Count, Is.EqualTo(b.Count));
			for (int i = 0; i < a.Count; i++)
			{
				Assert.That(a[i].feet == b[i].feet && a[i].state.Phase == b[i].state.Phase && a[i].state.Ticks == b[i].state.Ticks && a[i].state.Index == b[i].state.Index, Is.True, $"tick {i}");
			}
			Assert.That(a[a.Count - 1].state.IsClimbing, Is.False, "the script ends over the top");
		}

		[Test]
		public void AReplayFromAReconciledTickRejoinsTheSameClimb()
		{
			// What the owner does after a reconcile: restore the feet and the climb at one tick, re-run the inputs from there.
			SceneClimbable ladder = WallLadder();
			List<(Vector3 feet, ClimbState state)> live = Script(in ladder, new Vector3(10.2f, 0f, 9.3f), HoldingAt(), 0, 400);
			const int at = 37;
			List<(Vector3 feet, ClimbState state)> replay = Script(in ladder, live[at].feet, live[at].state, at + 1, 400);
			for (int i = 0; i < replay.Count; i++)
			{
				(Vector3 feet, ClimbState state) expected = live[at + 1 + i];
				Assert.That(replay[i].feet == expected.feet && replay[i].state.Phase == expected.state.Phase && replay[i].state.Ticks == expected.state.Ticks, Is.True, $"replayed tick {at + 1 + i}");
			}
		}

		// ── The scene's climbables ───────────────────────────────────────────

		[Test]
		public void TheIndexFindsOnlyTheClimbablesNearAPlaceInAscendingOrder()
		{
			var list = new List<SceneClimbable> { WallLadder(), ShaftRope(), WallLadder() };
			SceneClimbable far = WallLadder();
			far.Bottom += new Vector3(200f, 0f, 0f);
			far.Top += new Vector3(200f, 0f, 0f);
			list[2] = far;
			var index = new ClimbableIndex(list);
			var found = new List<int>();
			Assert.That(index.Query(new Vector3(10f, 0f, 9f), 2.5f, found), Is.True);
			Assert.That(found, Is.EqualTo(new[] { 0, 1 }), "the ladder and the rope share a cell; the far ladder is elsewhere");
			Assert.That(index.Query(new Vector3(210f, 0f, 9f), 2.5f, found), Is.True);
			Assert.That(found, Is.EqualTo(new[] { 2 }));
			Assert.That(index.Query(new Vector3(-500f, 0f, 9f), 2.5f, found), Is.False);
		}

		[Test]
		public void AScenesClimbablesAreFoundByIndexWhileRegistered()
		{
			Scene scene = default;
			object owner = new object();
			var list = new List<SceneClimbable> { WallLadder(), ShaftRope() };
			try
			{
				SceneClimbables.Register(scene, owner, list);
				Assert.That(SceneClimbables.TryGet(scene, 1, out SceneClimbable rope), Is.True);
				Assert.That(rope.Kind, Is.EqualTo(SceneClimbableKind.Rope));
				Assert.That(SceneClimbables.TryGet(scene, 2, out _), Is.False);
				Assert.That(SceneClimbables.TryGet(scene, -1, out _), Is.False);
				var near = new List<int>();
				Assert.That(SceneClimbables.Near(scene, new Vector3(9.5f, 0f, 9.5f), ClimbingRules.QueryReach, near), Is.True);
				SceneClimbables.Unregister(scene, new object());
				Assert.That(SceneClimbables.Has(scene), Is.True, "only its own owner withdraws a scene's climbables");
			}
			finally
			{
				SceneClimbables.Unregister(scene, owner);
			}
			Assert.That(SceneClimbables.Has(scene), Is.False);
		}

		[Test]
		public void ClimbablesAreSerializedWithTheScenesPointsOfInterest()
		{
			FieldInfo field = typeof(ScenePointsOfInterest).GetField(nameof(ScenePointsOfInterest.Climbables));
			Assert.That(field, Is.Not.Null);
			Assert.That(field.IsPublic && field.FieldType == typeof(List<SceneClimbable>), Is.True);
			Assert.That(typeof(SceneClimbable).IsSerializable, Is.True, "Unity serializes only [Serializable] structs");
			foreach (string name in new[] { "Kind", "Bottom", "Top", "Out", "TopExit", "HalfWidth" })
			{
				FieldInfo member = typeof(SceneClimbable).GetField(name);
				Assert.That(member != null && member.IsPublic && !member.IsStatic && !member.IsNotSerialized, Is.True, name);
			}
			Assert.That(Enum.GetUnderlyingType(typeof(SceneClimbableKind)), Is.EqualTo(typeof(byte)));
			Assert.That((byte)SceneClimbableKind.Ladder == 0 && (byte)SceneClimbableKind.RopeLadder == 1 && (byte)SceneClimbableKind.Rope == 2, Is.True, "stored ordinals never move");
		}

		/// <summary>Native (a ScriptableObject and Unity's serializer): runs in the editor's test runner, not without it.</summary>
		[Test]
		public void AStoredScenePointsOfInterestRoundTripsItsClimbables()
		{
			var asset = ScriptableObject.CreateInstance<ScenePointsOfInterest>();
			var copy = ScriptableObject.CreateInstance<ScenePointsOfInterest>();
			try
			{
				asset.Climbables.Add(WallLadder());
				asset.Climbables.Add(ShaftRope());
				JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(asset), copy);
				Assert.That(copy.Climbables.Count, Is.EqualTo(2));
				for (int i = 0; i < 2; i++)
				{
					SceneClimbable a = asset.Climbables[i], b = copy.Climbables[i];
					Assert.That(b.Kind == a.Kind && b.Bottom == a.Bottom && b.Top == a.Top && b.Out == a.Out && b.TopExit == a.TopExit && b.HalfWidth == a.HalfWidth, Is.True, $"climbable {i}");
				}
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(asset);
				UnityEngine.Object.DestroyImmediate(copy);
			}
		}

		// ── The reconcile ────────────────────────────────────────────────────

		private static void RegisterSerializers()
		{
			foreach (Type t in new[] { typeof(CharacterTransientGroundingReportDeltaSerializer), typeof(KinematicCharacterMotorStateDeltaSerializer) })
			{
				t.GetMethod("RegisterSerializers", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).Invoke(null, null);
			}
		}

		private static KinematicCharacterMotorState Climber(ushort climbable, ClimbPhase phase, byte ticks)
		{
			KinematicCharacterMotorState state = default;
			state.Rotation = Quaternion.identity;
			state.Position = new Vector3(10f, 2f, 9.55f);
			state.Climbable = climbable;
			state.ClimbPhase = (byte)phase;
			state.ClimbTicks = ticks;
			return state;
		}

		[Test]
		public void TheClimbRidesTheReconcileFullAndDelta()
		{
			RegisterSerializers();
			KinematicCharacterMotorState climbing = Climber(43, ClimbPhase.SteppingOff, 61);

			Writer full = new Writer();
			full.Write(climbing);
			KinematicCharacterMotorState fullBack = new Reader(full.GetArraySegment(), null).Read<KinematicCharacterMotorState>();
			Assert.That(fullBack.Climbable == 43 && fullBack.ClimbPhase == (byte)ClimbPhase.SteppingOff && fullBack.ClimbTicks == 61, Is.True, "full");

			KinematicCharacterMotorState prev = Climber(0, ClimbPhase.None, 0);
			Writer delta = new Writer();
			delta.WriteDelta(prev, climbing, DeltaSerializerOption.RootSerialize);
			KinematicCharacterMotorState deltaBack = new Reader(delta.GetArraySegment(), null).ReadDelta(prev);
			Assert.That(deltaBack.Climbable == 43 && deltaBack.ClimbPhase == (byte)ClimbPhase.SteppingOff && deltaBack.ClimbTicks == 61, Is.True, "delta");
			Assert.That(deltaBack.Position, Is.EqualTo(climbing.Position));

			// Only the ticks change: the extension carries that one field and nothing else is disturbed.
			KinematicCharacterMotorState next = climbing;
			next.ClimbTicks = 60;
			Writer tick = new Writer();
			tick.WriteDelta(climbing, next, DeltaSerializerOption.RootSerialize);
			KinematicCharacterMotorState tickBack = new Reader(tick.GetArraySegment(), null).ReadDelta(climbing);
			Assert.That(tickBack.ClimbTicks == 60 && tickBack.Climbable == 43 && tickBack.ClimbPhase == (byte)ClimbPhase.SteppingOff, Is.True, "ticks only");
			Assert.That(tick.Length, Is.EqualTo(2 + 1 + 1), "the mask, the extension's flags and the one byte");
		}

		[Test]
		public void AStateWithNoClimbChangeSendsNoExtension()
		{
			RegisterSerializers();
			KinematicCharacterMotorState a = Climber(5, ClimbPhase.Holding, 0);
			KinematicCharacterMotorState b = a;
			b.IsCrouching = true;
			Writer delta = new Writer();
			delta.WriteDelta(a, b, DeltaSerializerOption.RootSerialize);
			Assert.That(delta.Length, Is.EqualTo(2 + 1), "the mask and the crouch flag: the climb costs nothing on a tick it does not change");
			KinematicCharacterMotorState back = new Reader(delta.GetArraySegment(), null).ReadDelta(a);
			Assert.That(back.IsCrouching && back.Climbable == 5 && back.ClimbPhase == (byte)ClimbPhase.Holding, Is.True);
		}
	}
}
