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
	/// Why observers get their own broadcasts instead of riding the reconcile stream.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The reconcile is delta encoded and genuinely small — a walking character's delta measures in
	/// single-digit bytes. That makes "just keep using reconcile for everyone" an entirely
	/// reasonable question, and the answer is not about the size of one message. It is about two
	/// things the delta encoding does not touch: <b>how often</b> the message is sent, and <b>how
	/// many people</b> it is sent to.
	/// </para>
	/// <para>
	/// A reconcile goes out every tick, and while state forwarding is on it goes to every observer —
	/// FishNet's own accounting says so outright: <c>written = stateForwarding ? writer.Length *
	/// Observers.Count : writer.Length</c>. So the per-character cost is payload × tickRate ×
	/// observers, and delta encoding only shrinks the first term. The broadcasts replace the other
	/// two: they are sent on change rather than per tick, and rate limited on top.
	/// </para>
	/// <para>
	/// There is a second argument these tests make concrete: most of the reconcile is not for
	/// observers at all. It carries motor state, RNG words, cooldowns and equipment so the
	/// <i>owner</i> can correct its own prediction. An observer needs almost none of it.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class ObserverChannelCostTests
	{
		private const int TickRate = 30;
		private const int RpcHeaderBytes = 10;

		[OneTimeSetUp]
		public void RegisterProductionSerializers()
		{
			Type[] types =
			{
				typeof(CharacterReconcileDataDeltaSerializer),
				typeof(CharacterReplicateDataDeltaSerializer),
				typeof(CharacterTransientGroundingReportDeltaSerializer),
				typeof(KinematicCharacterMotorStateDeltaSerializer),
				typeof(CharacterAttributeResourceStateSerializer),
			};
			foreach (Type t in types)
			{
				MethodInfo m = t.GetMethod("RegisterSerializers",
					BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
				LogAssert.IsNotNull(m, $"{t.Name} must expose RegisterSerializers.");
				m.Invoke(null, null);
			}
		}

		private static int Bytes(Action<Writer> write)
		{
			Writer w = new Writer();
			write(w);
			return w.Length;
		}

		/// <summary>Builds a representative in-combat reconcile.</summary>
		private static CharacterReconcileData BuildReconcile()
		{
			CharacterReconcileData d = default;
			d.MotorState = new KinematicCharacterMotorState
			{
				Position = new Vector3(128.5f, 12.25f, -64.75f),
				Rotation = Quaternion.Euler(0f, 42f, 0f),
				BaseVelocity = new Vector3(3.2f, -0.4f, 1.1f),
			};
			d.AbilityID = 8842;
			d.RemainingTicks = 14;
			d.Seed = -1_713_468_379;
			d.PackedFlagsAndSlot = CharacterReconcileData.Pack(0b0101, -1);
			d.RngS0 = 0x9E3779B9; d.RngS1 = 0x243F6A88; d.RngS2 = 0xB7E15162; d.RngS3 = 0x8AED2A6A;
			d.ResourceState = new CharacterAttributeResourceState
			{
				Health = 812f, MaxHealth = 1200, Mana = 240f, MaxMana = 800,
				Stamina = 310f, MaxStamina = 400, NextRegenTick = 930,
			};
			d.Cooldowns = new[] { new CooldownReconcileEntry { AbilityID = 8842, StartTick = 900, DurationTicks = 60 } };
			d.Buffs = new[] { new BuffReconcileEntry { TemplateID = 200, Stacks = 1, ExpiryTick = 1200, NextTickTick = 930 } };
			d.Equipment = new[] { new EquipmentReconcileEntry { Slot = 0, TemplateID = 300, Seed = 7, InstanceID = 5001 } };
			d.Attributes = new[] { new AttributeReconcileEntry { TemplateID = 400, Value = 42, ExternalModifier = 3 } };
			return d;
		}

		/// <summary>
		/// How much of the reconcile an observer has any use for.
		/// </summary>
		/// <remarks>
		/// Splits the payload into what corrects the owner's prediction and what an observer would
		/// actually render. The point is not the exact ratio — it moves with loadout — but that the
		/// reconcile is a prediction-correction channel, and sending it to observers pays for a
		/// correction none of them are performing.
		/// </remarks>
		[Test]
		public void Measure_HowMuchOfTheReconcile_AnObserverNeeds()
		{
			CharacterReconcileData d = BuildReconcile();

			int full = Bytes(w => w.Write(d));
			int motorState = Bytes(w => w.Write(d.MotorState));
			int resources = Bytes(w => w.Write(d.ResourceState));
			int rngWords = Bytes(w => { w.WriteUInt32Unpacked(d.RngS0); w.WriteUInt32Unpacked(d.RngS1);
										w.WriteUInt32Unpacked(d.RngS2); w.WriteUInt32Unpacked(d.RngS3); });

			TestContext.WriteLine($"MEASURE full reconcile absolute = {full}B");
			TestContext.WriteLine($"MEASURE   motor state  {motorState,4}B  owner-only (observers use NetworkTransform)");
			TestContext.WriteLine($"MEASURE   rng words    {rngWords,4}B  owner-only (mismatch detection)");
			TestContext.WriteLine($"MEASURE   resources    {resources,4}B  the ONLY part an observer renders");
			TestContext.WriteLine(
				$"MEASURE observers use ~{resources * 100.0 / full:F0}% of the reconcile they are sent");

			LogAssert.IsTrue(resources < full / 2,
				"Resources are expected to be a minority of the reconcile; if they dominate, the " +
				"argument for a separate observer channel is weaker than stated.");
		}

		/// <summary>
		/// The reconcile's cost is frequency times observers — delta encoding shrinks neither.
		/// </summary>
		[Test]
		public void Measure_ReconcileScaling_VersusBroadcasts()
		{
			CharacterReconcileData prev = BuildReconcile();
			CharacterReconcileData next = BuildReconcile();
			next.MotorState.Position += new Vector3(0.12f, 0f, 0.04f);
			next.RemainingTicks = 13;
			next.ResourceState.Health = 780f;

			int deltaBytes = Bytes(w => w.WriteDelta(prev, next, DeltaSerializerOption.RootSerialize));
			int framedReconcile = deltaBytes + RpcHeaderBytes;

			// The replacement channel: resources on change, rate limited to 5Hz by
			// CharacterAttributeController.observedResourcePushInterval.
			int resourceBroadcast = Bytes(w =>
			{
				w.WriteInt32(1234);                       // CharacterObjectID
				w.WriteSingle(780f); w.WriteInt32(1200);  // health
				w.WriteSingle(240f); w.WriteInt32(800);   // mana
				w.WriteSingle(310f); w.WriteInt32(400);   // stamina
			}) + RpcHeaderBytes;

			const double ResourceHz = 5.0;

			TestContext.WriteLine(
				$"MEASURE reconcile delta {deltaBytes}B (+{RpcHeaderBytes}B header) sent {TickRate}x/sec to EVERY observer");
			TestContext.WriteLine(
				$"MEASURE resource broadcast {resourceBroadcast}B sent at most {ResourceHz}x/sec, and only when a value changed");
			TestContext.WriteLine("");

			foreach (int observers in new[] { 10, 25, 60, 150 })
			{
				double reconcilePerSec = framedReconcile * (double)TickRate * observers;
				double broadcastPerSec = resourceBroadcast * ResourceHz * observers;
				TestContext.WriteLine(
					$"MEASURE {observers,3} observers: reconcile-to-all {reconcilePerSec / 1024.0,8:F1} KB/s  " +
					$"vs broadcast {broadcastPerSec / 1024.0,6:F1} KB/s  ({reconcilePerSec / broadcastPerSec:F0}x)");
			}

			TestContext.WriteLine("");
			TestContext.WriteLine(
				"MEASURE an idle character at full health broadcasts 0 B/s; its reconcile is still sent every tick");

			double r60 = framedReconcile * (double)TickRate * 60;
			double b60 = resourceBroadcast * ResourceHz * 60;
			LogAssert.IsTrue(r60 > b60 * 5,
				$"The observer broadcast must be at least 5x cheaper than relaying reconcile at 60 observers; " +
				$"measured {r60 / b60:F1}x.");
		}

		/// <summary>
		/// Where the saving actually comes from: message count, not message size.
		/// </summary>
		/// <remarks>
		/// Isolates the two terms. Delta encoding already did excellent work on payload size — this
		/// shows that even a hypothetical zero-byte payload would not fix the problem, because the
		/// header and the per-observer multiplier survive it.
		/// </remarks>
		[Test]
		public void Measure_WhereTheSavingComesFrom()
		{
			CharacterReconcileData prev = BuildReconcile();
			CharacterReconcileData next = BuildReconcile();
			next.MotorState.Position += new Vector3(0.12f, 0f, 0.04f);

			int deltaBytes = Bytes(w => w.WriteDelta(prev, next, DeltaSerializerOption.RootSerialize));
			const int observers = 60;

			double actual = (deltaBytes + RpcHeaderBytes) * (double)TickRate * observers;
			double freePayload = RpcHeaderBytes * (double)TickRate * observers;
			double onePerSecond = (deltaBytes + RpcHeaderBytes) * 1.0 * observers;

			TestContext.WriteLine($"MEASURE at {observers} observers, {TickRate}Hz:");
			TestContext.WriteLine($"MEASURE   as sent today                 {actual / 1024.0,7:F1} KB/s");
			TestContext.WriteLine($"MEASURE   if the payload were FREE      {freePayload / 1024.0,7:F1} KB/s  " +
				$"({freePayload * 100.0 / actual:F0}% of today — headers alone)");
			TestContext.WriteLine($"MEASURE   same payload at 1Hz instead   {onePerSecond / 1024.0,7:F1} KB/s  " +
				$"({onePerSecond * 100.0 / actual:F0}% of today)");
			TestContext.WriteLine(
				"MEASURE conclusion: frequency dominates. Compressing the payload further cannot reach " +
				"what sending it less often does.");

			LogAssert.IsTrue(onePerSecond < freePayload,
				"Reducing frequency must beat eliminating the payload entirely, or the framing " +
				"assumption behind this argument is wrong.");
		}
	}
}
