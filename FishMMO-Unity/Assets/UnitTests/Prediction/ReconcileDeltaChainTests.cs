using System;
using System.Reflection;
using NUnit.Framework;
using FishMMO.Shared;
using FishNet.Serializing;
using KinematicCharacterController;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// End-to-end coverage for the delta reconcile chain now that it is live on the wire.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A delta reconcile is only decodable by a peer holding the same baseline the writer used, so
	/// the correctness of the feature is a property of the <em>sequence</em>, not of any single
	/// payload. These tests model the two halves of that sequence exactly as
	/// <c>NetworkBehaviour.Reconcile_Send</c> and <c>NetworkBehaviour.Reconcile_Reader</c> perform
	/// them, and then attack the sequence in the three ways it can realistically break: a peer that
	/// starts observing late, a state dropped for being old, and a baseline that has drifted for
	/// any reason at all.
	/// </para>
	/// <para>
	/// Play mode cannot be driven in this environment, so this fixture is the closest available
	/// substitute for a live session. It exercises the real production serializers and the real
	/// baseline-advance rule — it does not reimplement either.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class ReconcileDeltaChainTests
	{
		/// <summary>Server tick rate, matching <c>TimeManager._tickRate</c> on the scene server.</summary>
		private const int ServerTickRate = 30;

		[OneTimeSetUp]
		public void RegisterProductionSerializers()
		{
			Type[] serializerTypes =
			{
				typeof(CharacterReconcileDataDeltaSerializer),
				typeof(CharacterReplicateDataDeltaSerializer),
				typeof(CharacterTransientGroundingReportDeltaSerializer),
				typeof(KinematicCharacterMotorStateDeltaSerializer),
				typeof(CharacterAttributeResourceStateSerializer),
			};

			foreach (Type serializerType in serializerTypes)
			{
				MethodInfo register = serializerType.GetMethod("RegisterSerializers",
					BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
				LogAssert.IsNotNull(register, $"{serializerType.Name} must expose a RegisterSerializers hook.");
				register.Invoke(null, null);
			}
		}

		/// <summary>
		/// The option <c>NetworkBehaviour.GetDeltaSerializeOption</c> returns for a given tick:
		/// FullSerialize on the tick an observer is added and once per second, RootSerialize otherwise.
		/// </summary>
		private static DeltaSerializerOption OptionForTick(uint localTick, bool observerAddedThisTick = false)
		{
			if (observerAddedThisTick)
			{
				return DeltaSerializerOption.FullSerialize;
			}
			return localTick % ServerTickRate == 0
				? DeltaSerializerOption.FullSerialize
				: DeltaSerializerOption.RootSerialize;
		}

		/// <summary>
		/// Models <c>Reconcile_Send</c>: write the delta against the server's baseline, then advance
		/// that baseline unconditionally.
		/// </summary>
		private static ArraySegment<byte> ServerSend(
			ref CharacterReconcileData serverBaseline,
			CharacterReconcileData next,
			DeltaSerializerOption option)
		{
			Writer writer = new Writer();
			writer.WriteDelta(serverBaseline, next, option);
			serverBaseline = next;
			return writer.GetArraySegment();
		}

		/// <summary>
		/// Models <c>Reconcile_Reader</c>: decode against the client's baseline, then advance that
		/// baseline unconditionally — including when the state is dropped for being old, which is
		/// the FishMMO edit this fixture exists to protect.
		/// </summary>
		private static CharacterReconcileData ClientReceive(
			ref CharacterReconcileData clientBaseline,
			ArraySegment<byte> payload)
		{
			Reader reader = new Reader(payload, null);
			CharacterReconcileData newData = reader.ReadDelta(clientBaseline);
			LogAssert.AreEqual(0, reader.Remaining, "A reconcile payload must be consumed exactly.");
			clientBaseline = newData;
			return newData;
		}

		[Test]
		public void Chain_SixtyTicks_ClientTracksServerExactly()
		{
			CharacterReconcileData serverBaseline = default;
			CharacterReconcileData clientBaseline = default;
			CharacterReconcileData authoritative = MakeReconcileData();

			int totalBytes = 0;

			for (uint tick = 1; tick <= ServerTickRate * 2; tick++)
			{
				authoritative = Advance(authoritative, tick);

				ArraySegment<byte> payload = ServerSend(ref serverBaseline, authoritative, OptionForTick(tick));
				totalBytes += payload.Count;

				CharacterReconcileData received = ClientReceive(ref clientBaseline, payload);

				AssertReconcileEquals(authoritative, received, $"tick {tick}");
				AssertReconcileEquals(serverBaseline, clientBaseline, $"baselines after tick {tick}");
			}

			TestContext.WriteLine(
				$"MEASURE chain of {ServerTickRate * 2} reconciles: {totalBytes}B total, " +
				$"{totalBytes / (double)(ServerTickRate * 2):F1}B/tick average (includes two absolute resyncs)");
		}

		[Test]
		public void LateObserver_ReceivesAbsoluteSnapshot_AndJoinsTheChain()
		{
			CharacterReconcileData serverBaseline = default;
			CharacterReconcileData existingClient = default;
			CharacterReconcileData authoritative = MakeReconcileData();

			// The object has been alive a while; the server's baseline has moved well away from default.
			for (uint tick = 1; tick <= 12; tick++)
			{
				authoritative = Advance(authoritative, tick);
				ArraySegment<byte> payload = ServerSend(ref serverBaseline, authoritative, OptionForTick(tick));
				ClientReceive(ref existingClient, payload);
			}

			/* A new observer starts here holding nothing. GetDeltaSerializeOption returns
			 * FullSerialize on the tick an observer is added — but FishNet's scalar delta writers
			 * are difference-based, so "every field present" is not the same as "decodable from no
			 * baseline". This is the case that made the upstream delta path unusable, and the
			 * absolute-snapshot mode is what fixes it. */
			CharacterReconcileData lateObserver = default;
			authoritative = Advance(authoritative, 13);
			ArraySegment<byte> spawnPayload = ServerSend(ref serverBaseline, authoritative,
				OptionForTick(13, observerAddedThisTick: true));

			CharacterReconcileData bootstrapped = ClientReceive(ref lateObserver, spawnPayload);

			AssertReconcileEquals(authoritative, bootstrapped,
				"a late observer must decode the absolute snapshot exactly, from an empty baseline");

			// And it must then track the chain like any other peer.
			for (uint tick = 14; tick <= 20; tick++)
			{
				authoritative = Advance(authoritative, tick);
				ArraySegment<byte> payload = ServerSend(ref serverBaseline, authoritative, OptionForTick(tick));
				CharacterReconcileData received = ClientReceive(ref lateObserver, payload);
				AssertReconcileEquals(authoritative, received, $"late observer at tick {tick}");
			}
		}

		[Test]
		public void DroppedOldState_StillAdvancesBaseline_SoTheChainSurvives()
		{
			CharacterReconcileData serverBaseline = default;
			CharacterReconcileData clientBaseline = default;
			CharacterReconcileData authoritative = MakeReconcileData();

			for (uint tick = 1; tick <= 5; tick++)
			{
				authoritative = Advance(authoritative, tick);
				ClientReceive(ref clientBaseline, ServerSend(ref serverBaseline, authoritative, OptionForTick(tick)));
			}

			/* Tick 6 arrives but the reader treats it as an old state and does not APPLY it.
			 * Reconcile_Reader still advances the baseline, because Reconcile_Send advanced its own
			 * unconditionally. Modelled here by discarding the returned value but keeping the
			 * baseline move that ClientReceive performs. */
			authoritative = Advance(authoritative, 6);
			ArraySegment<byte> dropped = ServerSend(ref serverBaseline, authoritative, OptionForTick(6));
			ClientReceive(ref clientBaseline, dropped);

			AssertReconcileEquals(serverBaseline, clientBaseline,
				"baselines must stay in lock-step across a dropped state");

			// The next delta must still decode against that baseline.
			authoritative = Advance(authoritative, 7);
			CharacterReconcileData received = ClientReceive(ref clientBaseline,
				ServerSend(ref serverBaseline, authoritative, OptionForTick(7)));

			AssertReconcileEquals(authoritative, received,
				"the delta after a dropped state must still decode correctly");
		}

		[Test]
		public void DroppedOldState_WithoutBaselineAdvance_BreaksTheChain()
		{
			/* The negative control for the edit above. This models what upstream FishNet did —
			 * `if (tick < _lastReadReconcileRemoteTick) return;` placed BEFORE the baseline
			 * assignment, so a dropped state left the reader's baseline behind the writer's. If
			 * this test ever starts passing its final assertion, the baseline-advance edit in
			 * NetworkBehaviour.Reconcile_Reader has been reverted and every dropped reconcile is
			 * silently corrupting the chain again. */
			CharacterReconcileData serverBaseline = default;
			CharacterReconcileData clientBaseline = default;
			CharacterReconcileData authoritative = MakeReconcileData();

			for (uint tick = 1; tick <= 5; tick++)
			{
				authoritative = Advance(authoritative, tick);
				ClientReceive(ref clientBaseline, ServerSend(ref serverBaseline, authoritative, OptionForTick(tick)));
			}

			// Tick 6: server advances, reader decodes but does NOT advance its baseline.
			authoritative = Advance(authoritative, 6);
			ArraySegment<byte> dropped = ServerSend(ref serverBaseline, authoritative, OptionForTick(6));
			CharacterReconcileData staleBaseline = clientBaseline;
			ClientReceive(ref clientBaseline, dropped);
			clientBaseline = staleBaseline; // upstream behaviour: baseline left behind

			// Tick 7 is a plain delta, so decoding it against the stale baseline must be wrong.
			authoritative = Advance(authoritative, 7);
			CharacterReconcileData received = ClientReceive(ref clientBaseline,
				ServerSend(ref serverBaseline, authoritative, OptionForTick(7)));

			LogAssert.IsFalse(ReconcileEquals(authoritative, received),
				"Decoding a delta against a baseline the writer has already moved past must produce the " +
				"wrong result. If it does not, this scenario is not exercising the delta path and the " +
				"positive test alongside it proves nothing.");
		}

		[Test]
		public void StaleBaseline_IsRepairedByTheNextAbsoluteSnapshot()
		{
			CharacterReconcileData serverBaseline = default;
			CharacterReconcileData clientBaseline = default;
			CharacterReconcileData authoritative = MakeReconcileData();

			for (uint tick = 1; tick <= 10; tick++)
			{
				authoritative = Advance(authoritative, tick);
				ClientReceive(ref clientBaseline, ServerSend(ref serverBaseline, authoritative, OptionForTick(tick)));
			}

			/* Corrupt the client's baseline outright — stand-in for any way the chain could break
			 * that these tests have not thought of. A delta decoded against it is now wrong, which
			 * is the point: the guarantee being asserted is recovery, not immunity. */
			clientBaseline.MotorState.Position += new Vector3(999f, -999f, 999f);
			clientBaseline.ResourceState.Health = -12345f;

			authoritative = Advance(authoritative, 11);
			CharacterReconcileData corrupt = ClientReceive(ref clientBaseline,
				ServerSend(ref serverBaseline, authoritative, OptionForTick(11)));
			LogAssert.IsFalse(ReconcileEquals(authoritative, corrupt),
				"a delta decoded against a corrupted baseline is expected to be wrong — if this passes, " +
				"the payload is not actually delta-encoded and the test proves nothing.");

			// Tick 30 is a periodic full serialize: absolute, and independent of the bad baseline.
			authoritative = Advance(authoritative, ServerTickRate);
			CharacterReconcileData repaired = ClientReceive(ref clientBaseline,
				ServerSend(ref serverBaseline, authoritative, OptionForTick(ServerTickRate)));

			AssertReconcileEquals(authoritative, repaired,
				"the once-per-second absolute snapshot must repair a drifted baseline");
			AssertReconcileEquals(serverBaseline, clientBaseline, "baselines after the resync");
		}

		[TearDown]
		public void ClearGuard()
		{
			// A rejected read parks a flag for NetworkBehaviour.Reconcile_Reader to consume; tests
			// that reject on purpose must not leak it into the next test.
			FishNet.Object.ReconcileDeltaGuard.ConsumeRejection();
		}

		/// <summary>
		/// A lost StateUpdate must not corrupt the owner: every delta after the gap is rejected,
		/// the baseline is left alone, and FishNet is told not to reconcile from it.
		/// </summary>
		/// <remarks>
		/// Before the sequence guard this scenario decoded tick 7 against the tick-5 baseline and
		/// handed the owner a wrong position — a teleport-and-jitter burst that lasted until the
		/// next absolute snapshot. Now the loss costs "no correction until the snapshot", which is
		/// what a lost packet meant before delta encoding existed.
		/// </remarks>
		[Test]
		public void LostPacket_RejectsEveryLaterDelta_UntilTheAbsoluteSnapshot()
		{
			CharacterReconcileData serverBaseline = default;
			CharacterReconcileData clientBaseline = default;
			CharacterReconcileData authoritative = MakeReconcileData();

			for (uint tick = 1; tick <= 5; tick++)
			{
				authoritative = Advance(authoritative, tick);
				ClientReceive(ref clientBaseline, ServerSend(ref serverBaseline, authoritative, OptionForTick(tick)));
				LogAssert.IsFalse(FishNet.Object.ReconcileDeltaGuard.ConsumeRejection(), $"in-order tick {tick} must be accepted");
			}
			CharacterReconcileData baselineBeforeLoss = clientBaseline;

			// Tick 6 is LOST: the server advances its baseline, the client never sees the bytes.
			authoritative = Advance(authoritative, 6);
			ServerSend(ref serverBaseline, authoritative, OptionForTick(6));

			int rejected = 0;
			for (uint tick = 7; tick < ServerTickRate; tick++)
			{
				authoritative = Advance(authoritative, tick);
				ArraySegment<byte> payload = ServerSend(ref serverBaseline, authoritative, OptionForTick(tick));
				CharacterReconcileData returned = ClientReceive(ref clientBaseline, payload);

				LogAssert.IsTrue(FishNet.Object.ReconcileDeltaGuard.ConsumeRejection(),
					$"tick {tick} follows a gap and must be rejected, not decoded against a stale baseline");
				LogAssert.IsTrue(ReconcileEquals(baselineBeforeLoss, returned),
					$"a rejected delta must hand back the untouched baseline (tick {tick})");
				rejected++;
			}

			// Tick 30: the periodic absolute snapshot resynchronises the chain.
			authoritative = Advance(authoritative, ServerTickRate);
			CharacterReconcileData repaired = ClientReceive(ref clientBaseline,
				ServerSend(ref serverBaseline, authoritative, OptionForTick(ServerTickRate)));
			LogAssert.IsFalse(FishNet.Object.ReconcileDeltaGuard.ConsumeRejection(), "an absolute snapshot is never rejected");
			AssertReconcileEquals(authoritative, repaired, "the absolute snapshot must resync after a loss");

			// And the chain continues cleanly from there.
			for (uint tick = ServerTickRate + 1; tick <= ServerTickRate + 5; tick++)
			{
				authoritative = Advance(authoritative, tick);
				CharacterReconcileData received = ClientReceive(ref clientBaseline,
					ServerSend(ref serverBaseline, authoritative, OptionForTick(tick)));
				LogAssert.IsFalse(FishNet.Object.ReconcileDeltaGuard.ConsumeRejection(), $"post-resync tick {tick} must be accepted");
				AssertReconcileEquals(authoritative, received, $"post-resync tick {tick}");
			}

			TestContext.WriteLine(
				$"MEASURE one lost StateUpdate at tick 6: {rejected} reconciles rejected (not corrupted) " +
				$"until the tick-{ServerTickRate} absolute snapshot; worst-case uncorrected window = " +
				$"{(ServerTickRate - 6) * 1000 / ServerTickRate} ms");
		}

		/// <summary>
		/// Reordered delivery is treated as a gap: the early packet is rejected, the late one is
		/// accepted, and the chain re-establishes at the next snapshot rather than applying the
		/// early delta against a baseline that does not yet include the late one.
		/// </summary>
		[Test]
		public void ReorderedDelivery_RejectsTheEarlyPacket_AndRecovers()
		{
			CharacterReconcileData serverBaseline = default;
			CharacterReconcileData clientBaseline = default;
			CharacterReconcileData authoritative = MakeReconcileData();

			for (uint tick = 1; tick <= 4; tick++)
			{
				authoritative = Advance(authoritative, tick);
				ClientReceive(ref clientBaseline, ServerSend(ref serverBaseline, authoritative, OptionForTick(tick)));
			}

			authoritative = Advance(authoritative, 5);
			ArraySegment<byte> five = ServerSend(ref serverBaseline, authoritative, OptionForTick(5));
			CharacterReconcileData authoritativeAtFive = authoritative;
			authoritative = Advance(authoritative, 6);
			ArraySegment<byte> six = ServerSend(ref serverBaseline, authoritative, OptionForTick(6));

			// Six arrives first.
			ClientReceive(ref clientBaseline, six);
			LogAssert.IsTrue(FishNet.Object.ReconcileDeltaGuard.ConsumeRejection(), "the early packet must be rejected");

			// Then five: in sequence, accepted.
			CharacterReconcileData received = ClientReceive(ref clientBaseline, five);
			LogAssert.IsFalse(FishNet.Object.ReconcileDeltaGuard.ConsumeRejection(), "the late packet is next in sequence");
			AssertReconcileEquals(authoritativeAtFive, received, "the late packet decodes exactly");

			// The snapshot heals the hole six left behind.
			authoritative = Advance(authoritative, ServerTickRate);
			CharacterReconcileData repaired = ClientReceive(ref clientBaseline,
				ServerSend(ref serverBaseline, authoritative, OptionForTick(ServerTickRate)));
			AssertReconcileEquals(authoritative, repaired, "resync after a reorder");
		}

		/// <summary>
		/// A rejected delta consumes exactly its own bytes, so whatever FishNet packed after it
		/// in the StateUpdate is still readable.
		/// </summary>
		[Test]
		public void RejectedDelta_ConsumesItsPayloadExactly()
		{
			CharacterReconcileData serverBaseline = default;
			CharacterReconcileData clientBaseline = default;
			CharacterReconcileData authoritative = MakeReconcileData();

			for (uint tick = 1; tick <= 3; tick++)
			{
				authoritative = Advance(authoritative, tick);
				ClientReceive(ref clientBaseline, ServerSend(ref serverBaseline, authoritative, OptionForTick(tick)));
			}
			authoritative = Advance(authoritative, 4);
			ServerSend(ref serverBaseline, authoritative, OptionForTick(4)); // lost
			authoritative = Advance(authoritative, 5);                        // lumpy: buffs, attributes, rng change
			authoritative = Advance(authoritative, 9);                        // cooldown + ability id change

			Writer writer = new Writer();
			writer.WriteDelta(serverBaseline, authoritative, DeltaSerializerOption.RootSerialize);
			const int Sentinel = 0x5EED;
			writer.WriteInt32(Sentinel);

			Reader reader = new Reader(writer.GetArraySegment(), null);
			reader.ReadDelta(clientBaseline);
			LogAssert.IsTrue(FishNet.Object.ReconcileDeltaGuard.ConsumeRejection(), "the gapped delta must be rejected");
			LogAssert.AreEqual(Sentinel, reader.ReadInt32(),
				"a rejected delta must leave the reader positioned exactly after its own payload");
		}

		// ── Helpers ──────────────────────────────────────────────────────────

		/// <summary>Moves the authoritative snapshot on by one tick's worth of plausible change.</summary>
		private static CharacterReconcileData Advance(CharacterReconcileData d, uint tick)
		{
			CharacterReconcileData next = d;
			// Every server send advances the chain sequence — CharacterPredictionController.CreateReconcile.
			next.Sequence = unchecked((byte)(d.Sequence + 1));
			next.Cooldowns = (CooldownReconcileEntry[])d.Cooldowns.Clone();
			next.Buffs = (BuffReconcileEntry[])d.Buffs.Clone();
			next.Equipment = (EquipmentReconcileEntry[])d.Equipment.Clone();
			next.Attributes = (AttributeReconcileEntry[])d.Attributes.Clone();

			next.MotorState.Position += new Vector3(0.11f, 0f, 0.03f);
			next.MotorState.BaseVelocity = new Vector3(3.3f, 0f, 0.9f);
			next.MotorState.Rotation = Quaternion.Euler(0f, tick * 1.5f, 0f);
			next.ResourceState.Health = Mathf.Max(1f, d.ResourceState.Health - 0.5f);
			next.ResourceState.NextRegenTick = 900 + tick;
			next.RemainingTicks = tick % 7;

			// Periodic lumpier changes, so the chain is not uniformly tiny deltas.
			if (tick % 5 == 0)
			{
				next.Buffs[0].Stacks = (int)(tick % 4) + 1;
				next.Attributes[1].ExternalModifier = (int)(tick % 11);
				next.RngS0 = 0x1000_0000u + tick;
				next.RngS1 = 0x2000_0000u + tick;
			}
			if (tick % 9 == 0)
			{
				next.Cooldowns[0].StartTick = 100 + tick;
				next.AbilityID = 8800 + tick;
			}
			return next;
		}

		private static bool ReconcileEquals(CharacterReconcileData a, CharacterReconcileData b)
		{
			if (a.AbilityID != b.AbilityID || a.RemainingTicks != b.RemainingTicks ||
				a.Seed != b.Seed || a.PackedFlagsAndSlot != b.PackedFlagsAndSlot ||
				a.RngS0 != b.RngS0 || a.RngS1 != b.RngS1 || a.RngS2 != b.RngS2 || a.RngS3 != b.RngS3)
			{
				return false;
			}
			if (a.ResourceState.MaxHealth != b.ResourceState.MaxHealth ||
				a.ResourceState.MaxMana != b.ResourceState.MaxMana ||
				a.ResourceState.MaxStamina != b.ResourceState.MaxStamina ||
				a.ResourceState.NextRegenTick != b.ResourceState.NextRegenTick ||
				Mathf.Abs(a.ResourceState.Health - b.ResourceState.Health) > 0.01f ||
				Mathf.Abs(a.ResourceState.Mana - b.ResourceState.Mana) > 0.01f ||
				Mathf.Abs(a.ResourceState.Stamina - b.ResourceState.Stamina) > 0.01f)
			{
				return false;
			}
			// Position and rotation ride quantised delta writers, so compare with tolerance.
			if (Vector3.Distance(a.MotorState.Position, b.MotorState.Position) > 0.05f ||
				Quaternion.Angle(a.MotorState.Rotation, b.MotorState.Rotation) > 1.0f ||
				Vector3.Distance(a.MotorState.BaseVelocity, b.MotorState.BaseVelocity) > 0.05f)
			{
				return false;
			}
			if (a.MotorState.IsCrouching != b.MotorState.IsCrouching ||
				a.MotorState.JumpRequested != b.MotorState.JumpRequested ||
				a.MotorState.GroundingStatus.IsStableOnGround != b.MotorState.GroundingStatus.IsStableOnGround)
			{
				return false;
			}
			return ArrayEquals(a.Cooldowns, b.Cooldowns) && ArrayEquals(a.Buffs, b.Buffs)
				&& ArrayEquals(a.Equipment, b.Equipment) && ArrayEquals(a.Attributes, b.Attributes);
		}

		private static bool ArrayEquals<T>(T[] a, T[] b) where T : IEquatable<T>
		{
			int aLen = a?.Length ?? 0;
			int bLen = b?.Length ?? 0;
			if (aLen != bLen)
			{
				return false;
			}
			for (int i = 0; i < aLen; i++)
			{
				if (!a[i].Equals(b[i]))
				{
					return false;
				}
			}
			return true;
		}

		private static void AssertReconcileEquals(CharacterReconcileData expected, CharacterReconcileData actual, string context)
		{
			LogAssert.IsTrue(ReconcileEquals(expected, actual),
				$"Reconcile mismatch ({context}). " +
				$"pos {expected.MotorState.Position}/{actual.MotorState.Position} " +
				$"hp {expected.ResourceState.Health}/{actual.ResourceState.Health} " +
				$"abilityId {expected.AbilityID}/{actual.AbilityID} " +
				$"remainingTicks {expected.RemainingTicks}/{actual.RemainingTicks} " +
				$"rng0 {expected.RngS0}/{actual.RngS0}");
		}

		private static CharacterReconcileData MakeReconcileData()
		{
			CharacterReconcileData d = default;
			d.MotorState = default;
			d.MotorState.Position = new Vector3(112.5f, 30.9f, -47.25f);
			d.MotorState.Rotation = Quaternion.identity;
			d.MotorState.GroundingStatus = default;
			d.MotorState.GroundingStatus.FoundAnyGround = true;
			d.MotorState.GroundingStatus.IsStableOnGround = true;
			d.MotorState.GroundingStatus.GroundNormal = Vector3.up;
			d.Seed = 4242;
			d.PackedFlagsAndSlot = 0x1234;
			d.ResourceState = default;
			d.ResourceState.MaxHealth = 1200; d.ResourceState.Health = 1200f;
			d.ResourceState.MaxMana = 800; d.ResourceState.Mana = 800f;
			d.ResourceState.MaxStamina = 400; d.ResourceState.Stamina = 400f;
			d.ResourceState.NextRegenTick = 900;
			d.RngS0 = 0xDEADBEEF; d.RngS1 = 0x12345678;
			d.RngS2 = 0x0BADF00D; d.RngS3 = 0xFEEDFACE;
			d.Cooldowns = new[] { new CooldownReconcileEntry { AbilityID = 42, StartTick = 100, DurationTicks = 60 } };
			d.Buffs = new[] { new BuffReconcileEntry { TemplateID = 3, ExpiryTick = 500, NextTickTick = 20, Stacks = 1, TickCount = 4, CumulativeTickMultiplier = 1 } };
			d.Equipment = new[]
			{
				new EquipmentReconcileEntry { TemplateID = 5, Slot = 1, Seed = 77, InstanceID = 900 },
				new EquipmentReconcileEntry { TemplateID = 6, Slot = 2, Seed = 78, InstanceID = 901 },
			};
			d.Attributes = new[]
			{
				new AttributeReconcileEntry { TemplateID = 1, Value = 25, ExternalModifier = 4 },
				new AttributeReconcileEntry { TemplateID = 2, Value = 31, ExternalModifier = 0 },
				new AttributeReconcileEntry { TemplateID = 3, Value = 18, ExternalModifier = 6 },
			};
			return d;
		}
	}
}
