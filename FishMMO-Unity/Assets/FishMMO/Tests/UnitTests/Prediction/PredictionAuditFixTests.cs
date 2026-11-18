using FishNet.Object.Prediction;
using FishMMO.Shared;
using NUnit.Framework;
using UnityEngine;
using FishMMO.UnitTests.Harness;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The pure rules added by the eighth prediction audit (PREDICTION_AUDIT_REPORT.md): each is the smallest statement
	/// of a fix, tested without a server.
	/// </summary>
	public class PredictionAuditFixTests
	{
		// ── The tick an owner stamps on its input ─────────────────────────

		private const uint TickRate = 30;
		private const uint Window = TickRate * ReplicateTickGuard.WindowSeconds;
		private const uint Settle = TickRate * ReplicateTickGuard.SettleSeconds;

		private static bool Accept(ref ReplicateTickGuard guard, uint inputTick, uint serverTick)
		{
			return guard.Accept(inputTick, serverTick, Window, ReplicateTickGuard.CreepIntervalTicks, Settle);
		}

		/// <summary>
		/// The live defect: a long frame at world entry. The client catches up its ticks at once and the first input read is
		/// the oldest of the burst; anchored there, every input after it read as too far ahead and the character stood at
		/// the spawn point, pulled back to it by every correction.
		/// </summary>
		[TestCase(6)]
		[TestCase(10)]
		[TestCase(30)]
		public void ALongFrameAtWorldEntry_NeverLocksTheOwnerOut(int frameSeconds)
		{
			uint burst = (uint)(frameSeconds * TickRate);
			ReplicateTickGuard guard = default;
			const uint Spawned = 1000;
			guard.Begin(Spawned);
			// The client processed the spawn at the start of the long frame and caught up its ticks at its end.
			uint firstInput = 5000;
			uint read = Spawned + burst + 3;
			// The burst arrives over a few server ticks (pacing), oldest first.
			for (uint i = 0; i <= burst; ++i)
			{
				uint at = read + i * 4 / burst;
				LogAssert.IsTrue(Accept(ref guard, firstInput + i, at), $"{frameSeconds} s frame: burst input {i} of {burst} was refused");
			}
			// Then the steady stream: one input a tick, a few seconds of it, and the settling period passed.
			uint input = firstInput + burst + 1;
			for (uint s = read + 5; s < read + 5 + 30 * 20; ++s)
			{
				LogAssert.IsTrue(Accept(ref guard, input++, s), $"{frameSeconds} s frame: the steady input read on server tick {s} was refused");
			}
		}

		[Test]
		public void WithoutABegin_AWholeBurstReadAtOnceIsAccepted()
		{
			ReplicateTickGuard guard = default;
			for (uint i = 0; i < 400; ++i)
			{
				LogAssert.IsTrue(Accept(ref guard, 5000u + i, 2000u), "every input read on the first server tick is one burst");
			}
			uint input = 5400;
			for (uint s = 2001; s < 2600; ++s)
			{
				LogAssert.IsTrue(Accept(ref guard, input++, s));
			}
		}

		[Test]
		public void AnHonestOwner_IsNeverRefused_ThroughFreezesLatencyAndLateReads()
		{
			ReplicateTickGuard guard = default;
			guard.Begin(990);
			uint input = 5000;
			uint server = 1000;
			for (; server < 1600; ++server)
			{
				LogAssert.IsTrue(Accept(ref guard, input++, server), "steady");
			}
			// The client freezes for eight seconds (a hidden tab), then catches its ticks up in one burst.
			server += 240;
			for (int i = 0; i < 240; ++i)
			{
				LogAssert.IsTrue(Accept(ref guard, input++, server), "a burst after a freeze reads low, never high");
			}
			// Two minutes of three seconds' extra latency, then it recovers.
			for (int i = 0; i < 3600; ++i, ++server)
			{
				LogAssert.IsTrue(Accept(ref guard, input++, server + 90), "late reads");
			}
			for (int i = 0; i < 600; ++i, ++server)
			{
				LogAssert.IsTrue(Accept(ref guard, input++, server), "a reference that followed the latency down refused the owner when it recovered");
			}
			// A counter that falls behind for good (a process suspended without catching up) is never refused.
			input -= 900;
			for (int i = 0; i < 300; ++i, ++server)
			{
				LogAssert.IsTrue(Accept(ref guard, input++, server), "slow is never refused");
			}
		}

		[Test]
		public void ATickFarAhead_IsRefused_AndDoesNotBuyTheNextOnes()
		{
			ReplicateTickGuard guard = default;
			guard.Begin(990);
			uint input = 5000;
			uint server = 1000;
			for (; server < 1100; ++server)
			{
				LogAssert.IsTrue(Accept(ref guard, input++, server));
			}
			// A minute ahead: every cooldown and timed debuff on this character would have ended on the spot.
			LogAssert.IsFalse(Accept(ref guard, input + 1800u, server), "a tick the owner's clock cannot have reached is refused");
			LogAssert.IsTrue(Accept(ref guard, input++, server++), "and the honest input for that tick is still good: the forgery moved nothing");
			int accepted = 0;
			for (uint i = 0; i < 300; ++i, ++server)
			{
				if (Accept(ref guard, input + i + 1800u, server))
				{
					accepted++;
				}
			}
			LogAssert.AreEqual(0, accepted, "ten seconds of asking does not make a minute's jump acceptable");
		}

		[Test]
		public void ACounterRunFast_GainsTheWindowOnce_ThenTwoPercent()
		{
			ReplicateTickGuard guard = default;
			guard.Begin(999);
			uint input = 5000;
			LogAssert.IsTrue(Accept(ref guard, input, 1000u));
			uint lastAccepted = input;
			const uint Span = 30 * 600;
			for (uint server = 1001; server <= 1000 + Span; ++server)
			{
				input += 2;
				if (Accept(ref guard, input, server))
				{
					lastAccepted = input;
				}
				else
				{
					input = lastAccepted;
				}
			}
			long gained = (long)lastAccepted - 5000 - Span;
			long allowed = Window + Settle + Span / ReplicateTickGuard.CreepIntervalTicks + 2;
			LogAssert.IsTrue(gained <= allowed, $"ten minutes of running the counter at double speed gained {gained} ticks; the bound is {allowed}");
		}

		// ── Body heading over the shoulder ────────────────────────────────

		[Test]
		public void TheBodysTurnOverTheShoulder_IsTheSameSmallAngle_AtAnyPitch()
		{
			Vector3 drawn = new Vector3(0f, 1.5f, 0f);
			foreach (float pitch in new[] { 0f, 30f, 60f, 75f, 85f, 89f })
			{
				Quaternion view = Quaternion.Euler(pitch, 0f, 0f);
				Vector3 forward = view * Vector3.forward;
				// Over the shoulder: 0.55 m right, 0.2 m up, 2.5 m back.
				Vector3 camera = drawn + view * new Vector3(0.55f, 0.2f, -2.5f);
				// The crosshair rests on something very near (the ground at the character's feet).
				Vector3 body = AimConvergence.BodyPoint(camera, forward, drawn, 0.5f, drawn);
				Vector3 heading = AimConvergence.BodyHeading(drawn, body, forward);
				float turn = Mathf.Abs(Vector3.SignedAngle(Vector3.forward, heading, Vector3.up));
				// 5.2 degrees wherever the floor applies; at the very limit of the pitch the far end of the ray is all there is (7.2).
				LogAssert.IsTrue(turn < 8f, $"looking {pitch} degrees down the body turns {turn:0.0} degrees off the view; it was 19 at 75 and 45 at 85");
			}
		}

		/// <summary>
		/// The aim is never bent to fit the body's heading: the offset covers the whole circle, so the shot stays on the
		/// crosshair and the body still faces where it should, even when the aim's own heading points backwards.
		/// </summary>
		[Test]
		public void AnAimWhoseHeadingPointsBackwards_StillGivesTheBodyItsOwn_AndIsLeftOnTheCrosshair()
		{
			Vector3 bodyHeading = Vector3.forward;
			// Looking almost straight down, with the eye a few centimetres ahead of where the body is drawn: the aim's
			// ground-plane part points BACKWARDS.
			Vector3 aim = AimDirectionCompression.Quantize(new Vector3(0.01f, -1f, -0.03f).normalized);
			float needed = AimConvergence.HeadingOffsetDegrees(aim, bodyHeading);
			LogAssert.IsTrue(Mathf.Abs(needed) > 135f, $"Sanity: the body is {needed:0} degrees from the aim's heading.");

			short sent = AimConvergence.EncodeHeadingOffset(needed);
			Vector3 forward = AimConvergence.MovementRotation(AimDirectionCompression.ToRotation(aim), sent) * Vector3.forward;
			// The motor takes the heading from the rotation's forward on the ground plane (KCCController.SetInputs).
			float rebuilt = Vector3.SignedAngle(Vector3.forward, Vector3.ProjectOnPlane(forward, Vector3.up), Vector3.up);
			LogAssert.IsTrue(Mathf.Abs(rebuilt) < 0.5f, $"The server rebuilds the body's heading from the aim and the offset ({rebuilt:0.00} degrees off).");

			string player = ProjectFiles.ReadAllText("Assets/FishMMO/Shared/Implementation/Entity/Prediction/KCC/KCCPlayer.cs");
			StringAssert.Contains("aimDirection = AimDirectionCompression.Quantize(aim);", player,
				"The aim sent is the crosshair's, never turned to make the body's heading fit: that moved shots 15 to 20 degrees.");
		}

		[Test]
		public void LookingStraightDown_TheAimStillHasItsHeading()
		{
			foreach (float yaw in new[] { 0f, 90f, 200f, 315f })
			{
				foreach (float pitch in new[] { 85f, 88.5f, 89.5f, -88.5f, -89.5f })
				{
					Quaternion view = Quaternion.Euler(pitch, yaw, 0f);
					Quaternion rebuilt = AimDirectionCompression.ToRotation(view * Vector3.forward);
					float off = Vector3.Angle(view * Vector3.right, rebuilt * Vector3.right);
					LogAssert.IsTrue(off < 2f,
						$"pitch {pitch} yaw {yaw}: right is {off:0.0} degrees from the camera's; above 88.2 degrees it was a fixed world axis whichever way the camera faced");
				}
			}
		}

		// ── Observed cast animations ──────────────────────────────────────

		[Test]
		public void AnObservedCastWaitsOnlyForAClockItBelongsTo()
		{
			LogAssert.IsTrue(AbilityController.ObservedCastIsNear(1005u, 1000.4d, 30), "A cast written a few ticks ahead of the playback clock waits for it.");
			LogAssert.IsTrue(AbilityController.ObservedCastIsNear(900u, 1000d, 30), "One already behind it does not wait at all (it is played).");
			LogAssert.IsFalse(AbilityController.ObservedCastIsNear(2000000u, 300000d, 30),
				"A tick from another server's counter is not this clock's to reach: waiting on it held every later cast behind it.");
			LogAssert.IsFalse(AbilityController.ObservedCastIsNear(1000u + 61u, 1000d, 30), "Two seconds is the limit.");
		}

		[Test]
		public void TheObservedCastQueue_IsClearedWithTheConnection_AndNothingWaitsWithoutAClock()
		{
			string source = ProjectFiles.ReadAllText("Assets/FishMMO/Shared/Implementation/Entity/Prediction/Ability/AbilityController.Activation.cs");
			StringAssert.Contains("networkManager.ClientManager.OnClientConnectionState += OnObservedCastConnectionState;", source,
				"The queue outlives the connection it was filled on.");
			StringAssert.Contains("if (headTick > drawTick && ObservedCastIsNear(headTick, drawTick, observedCastClock.TickRate))", source,
				"A stale head must not hold the queue.");
		}

		// ── What a correction must leave alone, and put back ──────────────

		[Test]
		public void ResourceValues_AreComparedExactly_NotWithinAThousandth()
		{
			string source = ProjectFiles.ReadAllText(
				"Assets/FishMMO/Shared/Implementation/Entity/Prediction/CharacterAttribute/CharacterAttributeResourceStateSerializer.cs");
			StringAssert.DoesNotContain("FLOAT_EPSILON", source,
				"A value too close to the baseline to send is rebuilt as the baseline: health 0.0008 against the server's 0.");
			StringAssert.Contains("!prev.Health.Equals(next.Health)", source);
		}

		[Test]
		public void TheFixesAreWiredWhereTheAuditFoundTheGaps()
		{
			string controller = ProjectFiles.ReadAllText("Assets/FishMMO/Shared/Implementation/Entity/Prediction/Ability/AbilityController.cs");
			StringAssert.Contains("endedAbility.DestroyAbilityObjectsBetween(reconcileTick, castEnd);", controller,
				"A cast the server ended early leaves the owner's later projectile flying as a ghost.");
			StringAssert.Contains("predictionController.RequestReconcileSends();", controller,
				"A cast cancelled for missing input is not told to the owner until its input returns.");
			StringAssert.Contains("localInputFlags.DisableBit(AbilityActivationFlags.IsConsumable);", controller,
				"The consumable flag outlives the id it describes, and every later ability press goes out as a consumable.");

			string ability = ProjectFiles.ReadAllText("Assets/FishMMO/Shared/Implementation/Entity/Prediction/Ability/AbilityObject.cs");
			StringAssert.Contains("TruncateLastSweep(hitPoint);", ability, "A recorded step runs on past the wall that ended the object.");
			StringAssert.Contains("if (SceneryBetween(self.scene.GetPhysicsScene(), ClosestPointOnSegment(sweep.From, sweep.To, point), point,", ability,
				"A claim is granted on distance alone, through cover.");
			StringAssert.Contains("DamageMitigation.TryDeflect(victim, headingTransform.forward, normal, mutate: true, out _)", ability,
				"A claim against an ended object skips the victim's parry and shield.");
			StringAssert.Contains("FastForward((uint)(behind - 1));", ability, "A copy first seen in mid-flight steps through every tick it missed.");

			string player = ProjectFiles.ReadAllText("Assets/FishMMO/Shared/Implementation/Entity/Prediction/KCC/KCCPlayer.cs");
			StringAssert.Contains("CarryOnBlankTick(kccInput.GetTick());", player, "A rider is left behind by the deck on a tick with no input.");

			string fishnet = ProjectFiles.ReadAllText("Assets/Plugins/FishNet/Runtime/Object/NetworkBehaviour/NetworkBehaviour.Prediction.cs");
			StringAssert.Contains("if (!(isServerStarted && TryStartHolding(requireRepeat: true)))", fishnet,
				"The server holds on any empty queue, and runs late input a second time.");
			StringAssert.Contains("if (!_replicateTickGuard.Accept(tick, guardClock.LocalTick,", fishnet, "The owner's tick is taken on trust.");
		}
	}
}
