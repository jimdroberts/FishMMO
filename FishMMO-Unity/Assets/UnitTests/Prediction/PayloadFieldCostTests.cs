using System;
using System.Collections.Generic;
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
	/// Per-field cost breakdown of the prediction payloads, for deciding what is worth carrying.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Measures each field by <b>ablation</b>: take a baseline snapshot, change exactly one field,
	/// and serialise the delta. The difference against a no-change delta is what that field costs
	/// when it moves. Nothing is mirrored or re-implemented here — every number comes out of the
	/// production serializer, so the table cannot drift away from what actually ships.
	/// </para>
	/// <para>
	/// The marginal delta cost is the number that drives steady-state bandwidth. The absolute cost
	/// matters separately for replicate, whose first entry per packet is always written in full so
	/// the packet survives loss on the unreliable channel — a field that is cheap to delta can still
	/// be expensive there.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class PayloadFieldCostTests
	{
		private static readonly List<(string payload, string field, int deltaBytes, string note)> Rows = new();

		[OneTimeSetUp]
		public void RegisterProductionSerializers()
		{
			Rows.Clear();
			Type[] serializerTypes =
			{
				typeof(CharacterReconcileDataDeltaSerializer),
				typeof(CharacterReplicateDataDeltaSerializer),
				typeof(CharacterTransientGroundingReportDeltaSerializer),
				typeof(KinematicCharacterMotorStateDeltaSerializer),
				typeof(CharacterAttributeResourceStateSerializer),
			};
			foreach (Type t in serializerTypes)
			{
				t.GetMethod("RegisterSerializers", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
					.Invoke(null, null);
			}
		}

		[OneTimeTearDown]
		public void PrintTable()
		{
			if (Rows.Count == 0)
			{
				return;
			}
			TestContext.WriteLine();
			TestContext.WriteLine("PER-FIELD MARGINAL COST — bytes added to the delta when this field alone changes");
			TestContext.WriteLine($"  {"payload",-24}{"field",-32}{"B",4}  note");
			TestContext.WriteLine("  " + new string('-', 88));
			foreach (var r in Rows)
			{
				TestContext.WriteLine($"  {r.payload,-24}{r.field,-32}{r.deltaBytes,4}  {r.note}");
			}
			TestContext.WriteLine("  " + new string('-', 88));
		}

		private static int Bytes(Action<Writer> write)
		{
			Writer writer = new Writer();
			write(writer);
			return writer.Length;
		}

		/// <summary>Bytes the delta grows by when <paramref name="mutate"/> is applied to the baseline.</summary>
		private static int MarginalDelta<T>(T baseline, Func<T, T> mutate)
		{
			int unchanged = Bytes(w => w.WriteDelta(baseline, baseline, DeltaSerializerOption.RootSerialize));
			int changed = Bytes(w => w.WriteDelta(baseline, mutate(baseline), DeltaSerializerOption.RootSerialize));
			return changed - unchanged;
		}

		private static void Record<T>(string payload, string field, T baseline, Func<T, T> mutate, string note)
		{
			Rows.Add((payload, field, MarginalDelta(baseline, mutate), note));
		}

		// ── Replicate ────────────────────────────────────────────────────────

		[Test]
		public void ReplicateData_FieldCosts()
		{
			CharacterReplicateData b = default;
			b.AimDirection = AimDirectionCompression.Quantize(Quaternion.Euler(3f, 40f, 0f) * Vector3.forward);
			b.MoveAxisForward = 1f;
			b.MoveFlags = 1;

			Record("CharacterReplicate", "MoveAxisForward", b, v => { v.MoveAxisForward = 0.5f; return v; }, "float, always full width");
			Record("CharacterReplicate", "MoveAxisRight", b, v => { v.MoveAxisRight = 0.35f; return v; }, "float, always full width");
			Record("CharacterReplicate", "MoveFlags", b, v => { v.MoveFlags = 3; return v; }, "varint delta");
			Record("CharacterReplicate", "AimDirection (1 tick turn)", b,
				v => { v.AimDirection = AimDirectionCompression.Quantize(Quaternion.Euler(3f, 41.2f, 0f) * Vector3.forward); return v; },
				"packed yaw/pitch, varint delta");
			Record("CharacterReplicate", "ActivationFlags", b, v => { v.ActivationFlags = 3; return v; }, "varint delta");
			Record("CharacterReplicate", "QueuedAbilityID", b, v => { v.QueuedAbilityID = 8842; return v; }, "varint delta, only on cast");

			/* The number that actually gets paid three times a packet.
			 *
			 * This used to be dominated by CameraPosition -- a raw Vector3 that was 44% of the
			 * absolute entry. It was removed rather than compressed: it carried the aim origin
			 * from the owning client, and nothing validated it against the caster's own position,
			 * so a modified client chose the point the server raycast for victims from. The origin
			 * is derived from the motor now (CharacterAimOrigin), which happens to be the larger
			 * saving as well as the correct behaviour. */
			int absolute = Bytes(w => w.Write(b));
			int wouldHaveBeen = absolute + Bytes(w => w.WriteVector3(new Vector3(2.5f, 1.8f, -4f)));

			TestContext.WriteLine(
				$"MEASURE replicate absolute entry = {absolute}B (was {wouldHaveBeen}B with a replicated " +
				$"aim origin: {(wouldHaveBeen - absolute) * 100.0 / wouldHaveBeen:F0}% removed, paid on entry 0 of every packet)");

			LogAssert.IsTrue(absolute < wouldHaveBeen,
				"Dropping the replicated aim origin must shrink the absolute entry.");
			LogAssert.IsTrue(absolute <= 20,
				$"The replicate absolute entry is {absolute}B. It is paid on entry 0 of every packet and " +
				"the projections assume it stayed small after the aim origin was removed.");
		}

		// ── Reconcile ────────────────────────────────────────────────────────

		[Test]
		public void ReconcileData_FieldCosts()
		{
			CharacterReconcileData b = MakeReconcile();

			Record("CharacterReconcile", "MotorState.Position", b,
				v => { v.MotorState.Position += new Vector3(0.12f, 0f, 0.04f); return v; }, "Vector3 delta");
			Record("CharacterReconcile", "MotorState.Rotation", b,
				v => { v.MotorState.Rotation = Quaternion.Euler(0f, 22f, 0f); return v; }, "Quaternion delta");
			Record("CharacterReconcile", "MotorState.BaseVelocity", b,
				v => { v.MotorState.BaseVelocity = new Vector3(3.6f, 0f, 1.2f); return v; }, "Vector3 delta");
			Record("CharacterReconcile", "MotorState.GroundNormal", b,
				v => { v.MotorState.GroundingStatus.GroundNormal = new Vector3(0.15f, 0.98f, 0.05f); return v; },
				"unit vector sent as a full Vector3");
			Record("CharacterReconcile", "MotorState.all 3 normals", b,
				v =>
				{
					v.MotorState.GroundingStatus.GroundNormal = new Vector3(0.15f, 0.98f, 0.05f);
					v.MotorState.GroundingStatus.InnerGroundNormal = new Vector3(0.14f, 0.98f, 0.06f);
					v.MotorState.GroundingStatus.OuterGroundNormal = new Vector3(0.16f, 0.97f, 0.04f);
					return v;
				}, "3 unit vectors, 3 Vector3 deltas");
			Record("CharacterReconcile", "MotorState.AttachedRbVelocity", b,
				v => { v.MotorState.AttachedRigidbodyVelocity = new Vector3(1f, 0f, 1f); return v; }, "usually zero");
			Record("CharacterReconcile", "MotorState.LastPlatformPosition", b,
				v => { v.MotorState.LastPlatformPosition = new Vector3(5f, 1f, 5f); return v; }, "usually zero");
			Record("CharacterReconcile", "ResourceState.Health", b,
				v => { v.ResourceState.Health -= 12f; return v; }, "float, always full width");
			Record("CharacterReconcile", "ResourceState.NextRegenTick", b,
				v => { v.ResourceState.NextRegenTick += 30; return v; }, "varint delta");
			Record("CharacterReconcile", "AbilityID", b, v => { v.AbilityID = 8842; return v; }, "varint, only on cast");
			Record("CharacterReconcile", "RemainingTicks", b, v => { v.RemainingTicks = 14; return v; }, "varint");
			Record("CharacterReconcile", "PackedFlagsAndSlot", b, v => { v.PackedFlagsAndSlot ^= 0x5A; return v; }, "varint");
			Record("CharacterReconcile", "RngS0..S3", b,
				v => { v.RngS0 = 0x1234ABCD; v.RngS1 = 0x5678EF01; v.RngS2 = 0x9ABC2345; v.RngS3 = 0xDEF06789; return v; },
				"4 high-entropy words, all-or-nothing, changes on every cast");
			Record("CharacterReconcile", "one Attribute entry", b,
				v => { v.Attributes = (AttributeReconcileEntry[])b.Attributes.Clone(); v.Attributes[1].ExternalModifier = 12; return v; },
				"index-delta");
			Record("CharacterReconcile", "one Buff stack", b,
				v => { v.Buffs = (BuffReconcileEntry[])b.Buffs.Clone(); v.Buffs[0].Stacks = 3; return v; }, "index-delta");
			Record("CharacterReconcile", "one Cooldown start", b,
				v => { v.Cooldowns = (CooldownReconcileEntry[])b.Cooldowns.Clone(); v.Cooldowns[0].StartTick += 30; return v; },
				"index-delta");
			Record("CharacterReconcile", "one Equipment slot", b,
				v => { v.Equipment = (EquipmentReconcileEntry[])b.Equipment.Clone(); v.Equipment[0].Seed = 99; return v; },
				"index-delta, changes rarely");
		}

		[Test]
		public void GroundNormals_AreUnitVectorsPaidAsFullVectors()
		{
			/* The three grounding normals are unit vectors carried as three full Vector3s. The aim
			 * field already demonstrates that a unit direction packs into 4 bytes with far more
			 * angular precision than a ground normal needs, so this quantifies what the current
			 * representation costs by comparison. */
			Vector3[] normals =
			{
				new Vector3(0.15f, 0.98f, 0.05f).normalized,
				new Vector3(0.14f, 0.98f, 0.06f).normalized,
				new Vector3(0.16f, 0.97f, 0.04f).normalized,
			};

			int asVectors = Bytes(w => { foreach (Vector3 n in normals) w.WriteVector3(n); });
			int asPacked = Bytes(w => { foreach (Vector3 n in normals) w.WriteUInt32Unpacked(AimDirectionCompression.Encode(n)); });

			double worstError = 0.0;
			foreach (Vector3 n in normals)
			{
				worstError = Math.Max(worstError, Vector3.Angle(n, AimDirectionCompression.Quantize(n)));
			}

			TestContext.WriteLine(
				$"MEASURE 3 grounding normals: as Vector3 = {asVectors}B, as packed directions = {asPacked}B " +
				$"(saving {asVectors - asPacked}B absolute), worst angular error {worstError:F4} degrees");

			LogAssert.IsTrue(asPacked < asVectors,
				"Packing unit normals must beat full Vector3s or there is no case for changing them.");
		}

		[Test]
		public void IdleCharacter_CostsAlmostNothing()
		{
			// The steady state most entities are in most of the time. Worth stating explicitly:
			// culling and message count matter more than payload once this is already near zero.
			CharacterReconcileData reconcile = MakeReconcile();
			CharacterReplicateData replicate = default;
			replicate.AimDirection = Vector3.forward;

			int reconcileIdle = Bytes(w => w.WriteDelta(reconcile, reconcile, DeltaSerializerOption.RootSerialize));
			int replicateIdle = Bytes(w => w.WriteDelta(replicate, replicate, DeltaSerializerOption.RootSerialize));

			TestContext.WriteLine($"MEASURE idle tick: reconcile={reconcileIdle}B replicate entry={replicateIdle}B " +
				$"(vs a {PredictionRpcHeaderBytes}B FishNet RPC header on each)");

			LogAssert.IsTrue(reconcileIdle + replicateIdle < PredictionRpcHeaderBytes,
				$"An idle character's payloads ({reconcileIdle + replicateIdle}B) are already smaller than the " +
				$"{PredictionRpcHeaderBytes}B per-message header. Further payload work cannot help here — " +
				"message count is the remaining lever.");
		}

		/// <summary>FishNet's per-RPC header, from <c>NetworkBehaviour.MAXIMUM_RPC_HEADER_SIZE</c>.</summary>
		private const int PredictionRpcHeaderBytes = 10;

		private static CharacterReconcileData MakeReconcile()
		{
			CharacterReconcileData d = default;
			d.MotorState = default;
			d.MotorState.Position = new Vector3(112.5f, 30.9f, -47.25f);
			d.MotorState.Rotation = Quaternion.Euler(0f, 20f, 0f);
			d.MotorState.GroundingStatus = default;
			d.MotorState.GroundingStatus.FoundAnyGround = true;
			d.MotorState.GroundingStatus.IsStableOnGround = true;
			d.MotorState.GroundingStatus.GroundNormal = Vector3.up;
			d.MotorState.GroundingStatus.InnerGroundNormal = Vector3.up;
			d.MotorState.GroundingStatus.OuterGroundNormal = Vector3.up;
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
