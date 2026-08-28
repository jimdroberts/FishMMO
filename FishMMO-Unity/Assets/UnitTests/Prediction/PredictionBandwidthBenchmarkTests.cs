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
	/// Measured bandwidth for every prediction type, full serializer versus delta serializer.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Produces the concrete per-tick and per-second byte counts, and the saving as a percentage,
	/// for each prediction payload in three representative scenarios. Numbers are printed to the
	/// NUnit output so they can be lifted straight from a test run as evidence.
	/// </para>
	/// <para>
	/// <b>The "FishNet" column is the one that matters.</b> FishNet does not pass
	/// <see cref="DeltaSerializerOption.Unset"/>. <c>NetworkBehaviour.GetDeltaSerializeOption</c>
	/// returns <see cref="DeltaSerializerOption.FullSerialize"/> on the tick an observer is added
	/// and on every tick where <c>localTick % tickRate == 0</c> — once per second — and
	/// <see cref="DeltaSerializerOption.RootSerialize"/> otherwise. So a second of traffic is one
	/// full serialize plus <see cref="ServerTickRate"/> - 1 root serializes, and that is what the
	/// per-second column models.
	/// </para>
	/// <para>
	/// <b>Which of these are live.</b> Delta reconcile is enabled — see the FISHMMO EDIT markers in
	/// <c>NetworkBehaviour.Prediction.cs</c> — so <see cref="CharacterReconcileData"/> and the three
	/// types nested inside it (<see cref="KinematicCharacterMotorState"/>,
	/// <see cref="CharacterTransientGroundingReport"/>, <see cref="CharacterAttributeResourceState"/>)
	/// ship over the delta path and their rows are the real saving.
	/// <see cref="CharacterReplicateData"/> is live too: upstream's delta replicate branch no longer
	/// compiled against the current <c>ReplicateDataContainer&lt;T&gt;</c> containers, so it was
	/// replaced with a self-contained encoding — see <c>Writer.WriteDeltaReplicate</c>.
	/// </para>
	/// <para>
	/// <b>Do not read the per-type replicate row as the cost of replicate.</b> A real replicate
	/// packet carries <c>PredictionManager.RedundancyCount</c> entries — three here — and the server
	/// relays it to every observer, so it scales per entity per observer exactly as reconcile does.
	/// The redundancy entries are near-identical to each other, which is where the saving is;
	/// <c>Benchmark_RealPacketCost_PerEntityPerObserver</c> measures the packet rather than a single
	/// entry and is the number to use.
	/// </para>
	/// <para>
	/// The <c>FullSer</c> column for <see cref="CharacterReconcileData"/> is larger than the full
	/// serializer by one byte because a full serialize is written as an absolute snapshot plus a
	/// mode byte. That is deliberate: a difference-encoded payload cannot bootstrap a peer that has
	/// no baseline. See <c>CharacterReconcileDataDeltaSerializer.WriteDelta</c>.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class PredictionBandwidthBenchmarkTests
	{
		/// <summary>
		/// Server tick rate, from <c>TimeManager._tickRate</c> in <c>Assets/Scenes/Server/SceneServer.unity</c>.
		/// Drives the per-second projections below.
		/// </summary>
		private const int ServerTickRate = 30;

		/* Per-scenario saving floors, set a few points under the figures measured when this was
		 * written so ordinary drift does not fail the build but a regression does. They are
		 * deliberately NOT uniform: how much a delta can save is a property of how much of the
		 * payload actually changes per tick, and the low floors below are honest results rather
		 * than slack. CharacterReplicateData while walking is the clearest case — it is 27 bytes
		 * of which the move axes, camera position and camera rotation all change every tick, so
		 * there is almost no redundancy left to exploit and ~10% is the real ceiling. The payloads
		 * that dominate the wire (reconcile, motor state) are the ones with headroom. */

		private static readonly List<Row> Results = new List<Row>();

		/// <summary>One measured scenario.</summary>
		private readonly struct Row
		{
			public readonly string Type;
			public readonly string Scenario;
			public readonly int Full;
			public readonly int Unset;
			public readonly int Root;
			public readonly int FullSer;

			public Row(string type, string scenario, int full, int unset, int root, int fullSer)
			{
				Type = type; Scenario = scenario; Full = full; Unset = unset; Root = root; FullSer = fullSer;
			}

			/// <summary>Bytes per second using the full serializer on every tick — today's cost.</summary>
			public int FullPerSecond => Full * ServerTickRate;

			/// <summary>
			/// Bytes per second as FishNet would drive the delta path: one FullSerialize per second,
			/// RootSerialize on every other tick.
			/// </summary>
			public int DeltaPerSecond => FullSer + (Root * (ServerTickRate - 1));

			/// <summary>Saving of <see cref="DeltaPerSecond"/> against <see cref="FullPerSecond"/>.</summary>
			public double SavingPercent =>
				FullPerSecond == 0 ? 0.0 : (1.0 - (double)DeltaPerSecond / FullPerSecond) * 100.0;
		}

		/// <summary>
		/// Runs the registrations that <c>[RuntimeInitializeOnLoadMethod]</c> performs in a player
		/// but not under EditMode. Without this every <c>WriteDelta</c> is a no-op returning false
		/// and every measurement below would read zero.
		/// </summary>
		[OneTimeSetUp]
		public void RegisterProductionSerializers()
		{
			Results.Clear();

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

		/// <summary>Prints the collected table once every measurement has run.</summary>
		[OneTimeTearDown]
		public void PrintTable()
		{
			if (Results.Count == 0)
			{
				return;
			}

			TestContext.WriteLine();
			TestContext.WriteLine($"PREDICTION BANDWIDTH — full vs delta serializer, tickRate={ServerTickRate}");
			TestContext.WriteLine(
				$"{"Type",-34}{"Scenario",-14}{"full B",8}{"Unset",8}{"Root",8}{"FullSer",9}" +
				$"{"full B/s",10}{"delta B/s",11}{"saving",9}");
			TestContext.WriteLine(new string('-', 102));

			foreach (Row r in Results)
			{
				TestContext.WriteLine(
					$"{r.Type,-34}{r.Scenario,-14}{r.Full,8}{r.Unset,8}{r.Root,8}{r.FullSer,9}" +
					$"{r.FullPerSecond,10}{r.DeltaPerSecond,11}{r.SavingPercent,8:F1}%");
			}
			TestContext.WriteLine(new string('-', 102));
		}

		/// <summary>Bytes produced by one write action.</summary>
		private static int Bytes(Action<Writer> write)
		{
			Writer writer = new Writer();
			write(writer);
			return writer.Length;
		}

		/// <summary>
		/// Measures one type/scenario across the full serializer and all three delta options,
		/// records it for the summary table, and returns it.
		/// </summary>
		private static Row Measure<T>(string type, string scenario, T prev, T next)
		{
			Row row = new Row(
				type,
				scenario,
				Bytes(w => w.Write(next)),
				Bytes(w => w.WriteDelta(prev, next, DeltaSerializerOption.Unset)),
				Bytes(w => w.WriteDelta(prev, next, DeltaSerializerOption.RootSerialize)),
				Bytes(w => w.WriteDelta(prev, next, DeltaSerializerOption.FullSerialize)));

			Results.Add(row);

			TestContext.WriteLine(
				$"MEASURE {type}/{scenario}: full={row.Full}B Unset={row.Unset}B Root={row.Root}B " +
				$"FullSer={row.FullSer}B | per-second full={row.FullPerSecond}B delta={row.DeltaPerSecond}B " +
				$"saving={row.SavingPercent:F1}%");

			/* Deliberately no blanket "delta must beat full" assertion here.
			 *
			 * The per-second column multiplies one scenario across a whole second, which is right
			 * for a steady state and wrong for a transient. Leaving the ground happens once, not
			 * thirty times; modelled as if it repeated, a struct whose every field changes costs
			 * more delta'd (flags word plus a varint per field) than written whole — and since the
			 * grounding report is only 15B absolute now, that crossover is real rather than
			 * hypothetical. It costs nothing in production because the enclosing motor-state
			 * serializer only pays it on the ticks the report actually changes.
			 *
			 * Each scenario asserts its own floor instead, so a genuine regression still fails. */
			return row;
		}

		/// <summary>Asserts a scenario still clears its recorded saving floor.</summary>
		/// <param name="row">The measured scenario.</param>
		/// <param name="floorPercent">Saving this scenario is expected to beat.</param>
		private static void AssertSaving(Row row, double floorPercent)
		{
			LogAssert.IsTrue(row.SavingPercent >= floorPercent,
				$"{row.Type}/{row.Scenario} saves only {row.SavingPercent:F1}%, below its recorded floor of " +
				$"{floorPercent:F0}%. The most likely cause is RootSerialize being treated as a full " +
				"serialize again — that alone costs most of the compression.");
		}

		// ── CharacterReplicateData (client → server input, every tick) ────────

		[Test]
		public void Benchmark_CharacterReplicateData()
		{
			CharacterReplicateData idlePrev = default;
			idlePrev.AimDirection = Vector3.forward;
			CharacterReplicateData idleNext = idlePrev;
			AssertSaving(Measure("CharacterReplicateData", "idle", idlePrev, idleNext), 82.0);

			CharacterReplicateData walkPrev = idlePrev;
			CharacterReplicateData walkNext = walkPrev;
			walkNext.MoveAxisForward = 1f;
			walkNext.MoveAxisRight = 0.35f;
			walkNext.MoveFlags = 1;
			walkNext.AimDirection = AimDirectionCompression.Quantize(Quaternion.Euler(0f, 12f, 0f) * Vector3.forward);
			/* Left at 16% deliberately even though this now measures ~32%. The scenario changed
			 * shape when the aim origin was removed: what remains that moves every tick is the aim
			 * direction and the move axes, so the headroom here is genuinely larger than it was.
			 * The floor stays low because it guards against RootSerialize regressing to a full
			 * serialize, not against the scenario getting better. */
			AssertSaving(Measure("CharacterReplicateData", "walking", walkPrev, walkNext), 16.0);

			CharacterReplicateData combatPrev = walkNext;
			CharacterReplicateData combatNext = combatPrev;
			combatNext.AimDirection = AimDirectionCompression.Quantize(Quaternion.Euler(0f, 14f, 0f) * Vector3.forward);
			combatNext.ActivationFlags = 3;
			combatNext.QueuedAbilityID = 8842;
			/* Re-based from 60% when the aim origin stopped being replicated. The delta path got
			 * CHEAPER in absolute terms — roughly 276 -> 213 B/s for this scenario — but the full
			 * baseline it is measured against fell further, from 23B to 11B per tick, because
			 * CameraPosition was 12 of those bytes. A percentage against a smaller baseline is a
			 * smaller percentage; it is not a regression. Read the absolute columns printed above
			 * rather than this figure when comparing against the pre-removal numbers. */
			AssertSaving(Measure("CharacterReplicateData", "casting", combatPrev, combatNext), 30.0);
		}

		// ── CharacterTransientGroundingReport (nested in motor state) ─────────

		[Test]
		public void Benchmark_CharacterTransientGroundingReport()
		{
			CharacterTransientGroundingReport stable = default;
			stable.FoundAnyGround = true;
			stable.IsStableOnGround = true;
			stable.GroundNormal = Vector3.up;
			stable.InnerGroundNormal = Vector3.up;
			stable.OuterGroundNormal = Vector3.up;

			AssertSaving(Measure("CharacterTransientGroundingReport", "stable", stable, stable), 85.0);

			CharacterTransientGroundingReport airborne = stable;
			airborne.FoundAnyGround = false;
			airborne.IsStableOnGround = false;
			airborne.GroundNormal = Vector3.zero;
			airborne.InnerGroundNormal = Vector3.zero;
			airborne.OuterGroundNormal = Vector3.zero;
			/* Roughly break-even, and deliberately so. An ungrounded report carries no normals at
			 * all now — the motor zeroes them on an ungrounded probe, so there was never anything
			 * in them to send — which took this scenario from 15B to 3B written whole. What is left
			 * is three booleans, where a flags word costs about as much as the payload. The floor
			 * guards against it regressing back toward the -20% it measured while the normals were
			 * still being sent. */
			AssertSaving(Measure("CharacterTransientGroundingReport", "leaves ground", stable, airborne), -5.0);

			CharacterTransientGroundingReport slope = stable;
			slope.GroundNormal = new Vector3(0.15f, 0.98f, 0.05f);
			AssertSaving(Measure("CharacterTransientGroundingReport", "slope", stable, slope), 55.0);
		}

		// ── KinematicCharacterMotorState (the bulk of a reconcile) ────────────

		[Test]
		public void Benchmark_KinematicCharacterMotorState()
		{
			KinematicCharacterMotorState standing = MakeMotorState();
			AssertSaving(Measure("KinematicCharacterMotorState", "standing", standing, standing), 90.0);

			KinematicCharacterMotorState walking = standing;
			walking.Position = standing.Position + new Vector3(0.12f, 0f, 0.04f);
			walking.BaseVelocity = new Vector3(3.6f, 0f, 1.2f);
			walking.Rotation = Quaternion.Euler(0f, 22f, 0f);
			/* Floor lowered from 75% to 72% on 2026-08-28, deliberately and once.
			 *
			 * KinematicCharacterMotorStateDeltaSerializer now writes a leading mode byte and routes
			 * FullSerialize through an absolute snapshot, because the type declares IReconcileData
			 * and so advertises that it can be a root reconcile — and a root whose "full" payload is
			 * still a difference against a baseline the receiver may not hold is the exact bug the
			 * mode byte exists to prevent. The byte is unreachable overhead today (the type is only
			 * ever nested, and its parent never passes anything but Unset down), which is why the
			 * cost shows up here as pure loss: one byte on a ~22 byte delta.
			 *
			 * If this floor needs to move again, check that it is a real regression first — the
			 * message below names the usual cause, and it is still the right thing to look at. */
			AssertSaving(Measure("KinematicCharacterMotorState", "walking", standing, walking), 72.0);

			KinematicCharacterMotorState jumping = walking;
			jumping.Position = walking.Position + new Vector3(0.12f, 0.4f, 0.04f);
			jumping.BaseVelocity = new Vector3(3.6f, 6.5f, 1.2f);
			jumping.JumpRequested = true;
			jumping.MustUnground = true;
			jumping.GroundingStatus.FoundAnyGround = false;
			jumping.GroundingStatus.IsStableOnGround = false;
			jumping.GroundingStatus.GroundNormal = Vector3.zero;
			// Lowered from 70% with the walking floor above, and for the same one reason: the mode
			// byte. See the note there before moving either again.
			AssertSaving(Measure("KinematicCharacterMotorState", "jumping", walking, jumping), 66.0);
		}

		// ── CharacterAttributeResourceState ──────────────────────────────────

		[Test]
		public void Benchmark_CharacterAttributeResourceState()
		{
			CharacterAttributeResourceState full = default;
			full.MaxHealth = 1200; full.Health = 1200f;
			full.MaxMana = 800; full.Mana = 800f;
			full.MaxStamina = 400; full.Stamina = 400f;
			full.NextRegenTick = 900;

			AssertSaving(Measure("CharacterAttributeResourceState", "untouched", full, full), 85.0);

			CharacterAttributeResourceState regen = full;
			regen.Health = 1150f;
			regen.NextRegenTick = 930;
			AssertSaving(Measure("CharacterAttributeResourceState", "regen tick", full, regen), 60.0);

			CharacterAttributeResourceState hit = regen;
			hit.Health = 940f;
			hit.Mana = 610f;
			hit.Stamina = 275f;
			AssertSaving(Measure("CharacterAttributeResourceState", "damage+cast", regen, hit), 30.0);
		}

		// ── CharacterReconcileData (server → client, per observed character) ──

		[Test]
		public void Benchmark_CharacterReconcileData()
		{
			CharacterReconcileData idle = MakeReconcileData();
			AssertSaving(Measure("CharacterReconcileData", "idle", idle, idle), 90.0);

			CharacterReconcileData walking = CloneArrays(idle);
			walking.MotorState.Position = idle.MotorState.Position + new Vector3(0.12f, 0f, 0.04f);
			walking.MotorState.BaseVelocity = new Vector3(3.6f, 0f, 1.2f);
			walking.MotorState.Rotation = Quaternion.Euler(0f, 22f, 0f);
			walking.ResourceState.Health = idle.ResourceState.Health - 1f;
			Row walkRow = Measure("CharacterReconcileData", "walking", idle, walking);
			AssertSaving(walkRow, 78.0);

			CharacterReconcileData combat = CloneArrays(walking);
			combat.MotorState.Position = walking.MotorState.Position + new Vector3(0.1f, 0f, 0.02f);
			combat.ResourceState.Health = walking.ResourceState.Health - 85f;
			combat.ResourceState.Mana = walking.ResourceState.Mana - 40f;
			combat.AbilityID = 8842;
			combat.RemainingTicks = 14;
			combat.Cooldowns[0].StartTick += 30;
			combat.Buffs[0].Stacks = 2;
			combat.Attributes[1].ExternalModifier = 12;
			combat.RngS0 = 0x1234ABCD;
			combat.RngS1 = 0x5678EF01;
			combat.RngS2 = 0x9ABC2345;
			combat.RngS3 = 0xDEF06789;
			Row combatRow = Measure("CharacterReconcileData", "combat", walking, combat);
			AssertSaving(combatRow, 58.0);

			/* The scaling headline. Reconcile is the dominant prediction payload and it is sent per
			 * observed character, so the per-second figure multiplies by how many characters a
			 * client observes. Reported as a projection with its assumptions stated rather than as
			 * a measured network capture. */
			const int ObservedCharacters = 20;
			int fullKbPerSec = walkRow.FullPerSecond * ObservedCharacters / 1024;
			int deltaKbPerSec = walkRow.DeltaPerSecond * ObservedCharacters / 1024;

			TestContext.WriteLine();
			TestContext.WriteLine(
				$"PROJECTION reconcile, {ObservedCharacters} observed characters walking, tickRate={ServerTickRate}: " +
				$"full={fullKbPerSec}KB/s delta={deltaKbPerSec}KB/s saving={walkRow.SavingPercent:F1}%");
			TestContext.WriteLine(
				"  (per-character-per-second x observed count; reconcile is sent to the owner and, with state " +
				"forwarding on, to every observer, so this scales linearly with observer count.)");

			LogAssert.IsTrue(deltaKbPerSec < fullKbPerSec,
				"The reconcile projection must show a reduction, or the delta path buys nothing at scale.");
		}

		// ── Real packet shapes, per entity per observer ──────────────────────

		/// <summary>
		/// Redundancy entries per replicate packet: <c>PredictionManager.RedundancyCount</c> is
		/// <c>_stateInterpolation + 1</c>, and the scene server sets <c>_stateInterpolation: 2</c>.
		/// </summary>
		private const int RedundancyCount = 3;

		/// <summary>
		/// Models <c>Writer.WriteReplicate</c>: a count byte, then each entry written in full with
		/// its channel byte.
		/// </summary>
		private static int FullReplicatePacket(CharacterReplicateData[] entries)
		{
			return Bytes(w =>
			{
				w.WriteUInt8Unpacked((byte)entries.Length);
				foreach (CharacterReplicateData e in entries)
				{
					w.Write(e);
					w.WriteUInt8Unpacked(0); // channel
				}
			});
		}

		/// <summary>
		/// Delta replicate, <b>chained across packets</b> — entry 0 is encoded against the last
		/// entry of the previous packet, which is what upstream's <c>WriteDeltaReplicate</c> did.
		/// </summary>
		/// <remarks>
		/// Measured for comparison only. This shape is <b>not safe for replicates</b>: they are sent
		/// on <see cref="FishNet.Transporting.Channel.Unreliable"/> (see the default channel on
		/// <c>CharacterPredictionController.Replicate</c>, which FishNet only upgrades when a packet
		/// is oversized), so a dropped packet leaves the reader without the baseline the next packet
		/// was encoded against and everything after it decodes to garbage. Redundancy exists
		/// precisely to survive that loss, so an encoding that cannot is self-defeating.
		/// </remarks>
		private static int DeltaReplicatePacketChained(CharacterReplicateData previousPacketLast, CharacterReplicateData[] entries, DeltaSerializerOption option)
		{
			return Bytes(w =>
			{
				w.WriteUInt8Unpacked((byte)entries.Length);
				CharacterReplicateData prev = previousPacketLast;
				DeltaSerializerOption o = option;
				foreach (CharacterReplicateData e in entries)
				{
					w.WriteDelta(prev, e, o);
					prev = e;
					o = DeltaSerializerOption.RootSerialize;
				}
			});
		}

		/// <summary>
		/// Delta replicate, <b>self-contained</b> — entry 0 is an absolute snapshot and entries
		/// 1..N-1 are deltas against the entry before them <i>within the same packet</i>.
		/// </summary>
		/// <remarks>
		/// This is the shape that is safe on an unreliable channel: every packet stands alone, so
		/// losing one costs exactly that packet and redundancy still does its job. It captures most
		/// of the available saving anyway, because the bytes being saved are the redundancy entries
		/// — resent past inputs that are near-identical to their neighbours by construction.
		/// </remarks>
		private static int DeltaReplicatePacketSelfContained(CharacterReplicateData[] entries)
		{
			return Bytes(w =>
			{
				w.WriteUInt8Unpacked((byte)entries.Length);
				for (int i = 0; i < entries.Length; i++)
				{
					if (i == 0)
					{
						w.Write(entries[0]);
					}
					else
					{
						w.WriteDelta(entries[i - 1], entries[i], DeltaSerializerOption.RootSerialize);
					}
					w.WriteUInt8Unpacked(0); // channel
				}
			});
		}

		[Test]
		public void Benchmark_RealPacketCost_PerEntityPerObserver()
		{
			/* Three consecutive ticks of a walking player. Redundancy entries are near-identical to
			 * each other by construction — that is the whole point of resending past inputs — which
			 * is why measuring a single entry against its predecessor, as the per-type rows above
			 * do, understates what delta replicate is worth on a real packet. */
			CharacterReplicateData[] packet = WalkingReplicatePacket();
			CharacterReplicateData priorPacketLast = packet[0];

			int replicateFull = FullReplicatePacket(packet);
			int replicateChained = DeltaReplicatePacketChained(priorPacketLast, packet, DeltaSerializerOption.RootSerialize);
			int replicateSelfContained = DeltaReplicatePacketSelfContained(packet);

			int replicateFullPerSec = replicateFull * ServerTickRate;
			// Self-contained is the shape that is safe on an unreliable channel, so it is the one
			// the totals below use. The chained figure is reported only to show what is being left
			// on the table for loss tolerance.
			int replicateDeltaPerSec = replicateSelfContained * ServerTickRate;
			int replicateChainedPerSec = replicateChained * ServerTickRate;
			double replicateSaving = (1.0 - (double)replicateDeltaPerSec / replicateFullPerSec) * 100.0;
			double replicateChainedSaving = (1.0 - (double)replicateChainedPerSec / replicateFullPerSec) * 100.0;

			// Reconcile, walking, as measured by the per-type benchmark — this one IS live.
			CharacterReconcileData reconcilePrev = MakeReconcileData();
			CharacterReconcileData reconcileNext = CloneArrays(reconcilePrev);
			reconcileNext.MotorState.Position += new Vector3(0.12f, 0f, 0.04f);
			reconcileNext.MotorState.BaseVelocity = new Vector3(3.6f, 0f, 1.2f);
			reconcileNext.MotorState.Rotation = Quaternion.Euler(0f, 22f, 0f);
			reconcileNext.ResourceState.Health -= 1f;

			int reconcileFull = Bytes(w => w.Write(reconcileNext));
			int reconcileRoot = Bytes(w => w.WriteDelta(reconcilePrev, reconcileNext, DeltaSerializerOption.RootSerialize));
			int reconcileFullSer = Bytes(w => w.WriteDelta(reconcilePrev, reconcileNext, DeltaSerializerOption.FullSerialize));
			int reconcileFullPerSec = reconcileFull * ServerTickRate;
			int reconcileDeltaPerSec = reconcileFullSer + (reconcileRoot * (ServerTickRate - 1));

			// Today: reconcile is on delta, replicate is still on the full serializer.
			int todayPerSec = reconcileDeltaPerSec + replicateFullPerSec;
			// Before this work: both on the full serializer.
			int beforePerSec = reconcileFullPerSec + replicateFullPerSec;
			// If delta replicate were also enabled.
			int bothPerSec = reconcileDeltaPerSec + replicateDeltaPerSec;

			TestContext.WriteLine();
			TestContext.WriteLine($"PER-ENTITY PER-OBSERVER, walking, tickRate={ServerTickRate}, redundancy={RedundancyCount}");
			TestContext.WriteLine($"  replicate packet   full={replicateFull}B  self-contained={replicateSelfContained}B  chained={replicateChained}B");
			TestContext.WriteLine($"  replicate  /s      full={replicateFullPerSec}B  delta={replicateDeltaPerSec}B  saving={replicateSaving:F1}%" +
				$"   [chained would be {replicateChainedPerSec}B / {replicateChainedSaving:F1}% but is unsafe on an unreliable channel]");
            TestContext.WriteLine($"  reconcile  /s      full={reconcileFullPerSec}B  delta={reconcileDeltaPerSec}B");
			TestContext.WriteLine($"  TOTAL      /s      before={beforePerSec}B  today={todayPerSec}B  " +
				$"both-on-delta={bothPerSec}B");
			TestContext.WriteLine($"  reduction          today={(1.0 - (double)todayPerSec / beforePerSec) * 100.0:F1}%  " +
				$"both-on-delta={(1.0 - (double)bothPerSec / beforePerSec) * 100.0:F1}%");
			TestContext.WriteLine();
			foreach (int observers in new[] { 10, 25, 50 })
			{
				TestContext.WriteLine(
					$"  x{observers,3} observers:  before={beforePerSec * observers / 1024,5}KB/s  " +
					$"today={todayPerSec * observers / 1024,5}KB/s  both-on-delta={bothPerSec * observers / 1024,5}KB/s");
			}

			LogAssert.IsTrue(replicateDeltaPerSec < replicateFullPerSec,
				$"Self-contained delta replicate must beat the full serializer on a real {RedundancyCount}-entry " +
				$"packet ({replicateDeltaPerSec}B/s vs {replicateFullPerSec}B/s), or there is no case for the work.");
			/* Floor lowered from 40% deliberately: narrowing the move axes made the FULL packet
			 * cheaper too (85B -> 67B), so the delta's relative advantage shrank while both paths
			 * got absolutely smaller. The number to watch is bytes on the wire, not the ratio. */
			LogAssert.IsTrue(replicateSaving >= 30.0,
				$"Self-contained delta replicate saves {replicateSaving:F1}%; below ~30% the loss-tolerant " +
				"shape stops being worth a second set of edits to vendored FishNet.");
			LogAssert.IsTrue(todayPerSec < beforePerSec,
				"Enabling delta reconcile must have reduced the per-entity cost.");
		}

		// ── Framed wire cost, including transport overhead ───────────────────

		/* Framing constants for the WebTransport (HTTP/3 over QUIC) transport this project uses.
		 * These are modelled from the transport's own accounting and the relevant RFCs, not from a
		 * packet capture -- treat them as a good estimate, not a measurement.
		 *
		 * QUIC's per-packet overhead is an order of magnitude heavier than a raw-UDP transport's:
		 * every QUIC packet carries a 16-byte AEAD authentication tag whatever else it holds. That
		 * makes batching, not payload size, the thing that decides the bill once payloads are small
		 * -- which after delta encoding they are. */

		/// <summary>IPv4 + UDP headers. Add 20 more for IPv6.</summary>
		private const int IpUdpHeaderBytes = 20 + 8;

		/// <summary>
		/// QUIC and HTTP/3 overhead inside one unreliable datagram, taken from the transport's own
		/// constants: it advertises <c>DatagramMTU = 1150</c> against QUIC's guaranteed 1200-byte
		/// path, and documents the difference as the 1-RTT packet header, the DATAGRAM frame header,
		/// the AEAD tag, and the HTTP/3 Datagram Quarter Stream ID (RFC 9297 section 2.1).
		/// </summary>
		private const int QuicDatagramOverheadBytes = 1200 - 1150;

		/// <summary>Application payload one unreliable datagram can carry — <c>WebTransport.DatagramMTU</c>.</summary>
		private const int DatagramMtuBytes = 1150;

		/// <summary>
		/// QUIC overhead per packet carrying reliable stream data: 1-RTT short header (flags,
		/// connection id, packet number) plus the 16-byte AEAD tag plus a STREAM frame header.
		/// Lower than the datagram case — no DATAGRAM frame and no Quarter Stream ID — and it
		/// amortises across however much stream data shares the packet.
		/// </summary>
		private const int QuicStreamPacketOverheadBytes = 1 + 8 + 2 + 16 + 8;

		/// <summary>
		/// Reliable bytes that fit in one QUIC packet alongside the header above. The reliable
		/// channel runs over a stream, which has no MTU of its own; QUIC segments it to the path.
		/// </summary>
		private const int StreamBytesPerQuicPacket = 1200 - QuicStreamPacketOverheadBytes;

		/// <summary>
		/// FishNet's per-RPC header — <c>NetworkBehaviour.MAXIMUM_RPC_HEADER_SIZE</c>. Packet id,
		/// object and behaviour ids, and the method hash or RPC link. This is the overhead that does
		/// <b>not</b> amortise: it is paid once per entity per observer per tick.
		/// </summary>
		private const int FishNetRpcHeaderBytes = 10;

		private static int PacketsFor(int bytes, int perPacket) => bytes <= 0 ? 0 : (bytes + perPacket - 1) / perPacket;

		/// <summary>
		/// Bytes on the wire per observer per second for <paramref name="entities"/> observed
		/// characters, including FishNet's per-message header and QUIC/UDP/IP framing.
		/// </summary>
		/// <remarks>
		/// Reconcile is sent reliably (a QUIC stream) and replicate unreliably (QUIC datagrams), so
		/// they are framed differently and batched separately. FishNet batches messages up to the
		/// channel MTU before handing them to the transport, so the per-packet cost is paid per
		/// batch rather than per message — it amortises as observer count rises, while the per-RPC
		/// header does not.
		/// </remarks>
		private static int FramedBytesPerObserverPerSecond(int entities, int reconcilePayload, int replicatePayload, int tickRate)
		{
			int reliableBytes = entities * (FishNetRpcHeaderBytes + reconcilePayload);
			int unreliableBytes = entities * (FishNetRpcHeaderBytes + replicatePayload);

			int perTick =
				reliableBytes + PacketsFor(reliableBytes, StreamBytesPerQuicPacket) * (IpUdpHeaderBytes + QuicStreamPacketOverheadBytes) +
				unreliableBytes + PacketsFor(unreliableBytes, DatagramMtuBytes) * (IpUdpHeaderBytes + QuicDatagramOverheadBytes);

			return perTick * tickRate;
		}

		[Test]
		public void Benchmark_FramedWireCost_IncludingTransportOverhead()
		{
			// Steady-state walking payloads, measured rather than assumed.
			CharacterReplicateData[] packet = WalkingReplicatePacket();
			int replicateFull = FullReplicatePacket(packet);
			int replicateDelta = DeltaReplicatePacketSelfContained(packet);

			CharacterReconcileData reconcilePrev = MakeReconcileData();
			CharacterReconcileData reconcileNext = CloneArrays(reconcilePrev);
			reconcileNext.MotorState.Position += new Vector3(0.12f, 0f, 0.04f);
			reconcileNext.MotorState.BaseVelocity = new Vector3(3.6f, 0f, 1.2f);
			reconcileNext.MotorState.Rotation = Quaternion.Euler(0f, 22f, 0f);
			reconcileNext.ResourceState.Health -= 1f;

			int reconcileFull = Bytes(w => w.Write(reconcileNext));
			int reconcileDelta = Bytes(w => w.WriteDelta(reconcilePrev, reconcileNext, DeltaSerializerOption.RootSerialize));

			TestContext.WriteLine();
			TestContext.WriteLine("FRAMED WIRE COST — payload + FishNet RPC header + QUIC/UDP/IP (WebTransport, HTTP/3)");
			TestContext.WriteLine($"  payloads: reconcile full={reconcileFull}B delta={reconcileDelta}B | " +
				$"replicate packet full={replicateFull}B delta={replicateDelta}B");
			TestContext.WriteLine($"  per-message header={FishNetRpcHeaderBytes}B (does not amortise)   " +
				$"per-packet framing = {IpUdpHeaderBytes + QuicStreamPacketOverheadBytes}B reliable (QUIC stream) / " +
				$"{IpUdpHeaderBytes + QuicDatagramOverheadBytes}B unreliable (QUIC datagram, incl. 16B AEAD tag)");
			TestContext.WriteLine();
			TestContext.WriteLine($"  {"entities",-10}{"tickRate",-10}{"full KB/s",12}{"delta KB/s",12}{"saving",10}");
			TestContext.WriteLine("  " + new string('-', 52));

			foreach (int tickRate in new[] { 30, 15 })
			{
				foreach (int entities in new[] { 10, 25, 50 })
				{
					int full = FramedBytesPerObserverPerSecond(entities, reconcileFull, replicateFull, tickRate);
					int delta = FramedBytesPerObserverPerSecond(entities, reconcileDelta, replicateDelta, tickRate);
					double saving = (1.0 - (double)delta / full) * 100.0;

					TestContext.WriteLine(
						$"  {entities,-10}{tickRate,-10}{full / 1024.0,12:F1}{delta / 1024.0,12:F1}{saving,9:F1}%");

					LogAssert.IsTrue(delta < full,
						$"Framed delta cost must beat framed full cost at {entities} entities / {tickRate}Hz.");
				}
			}

			/* What the header floor costs. At these payload sizes the fixed per-message header is a
			 * large fraction of each message, so it caps how much any further serializer work can
			 * buy — worth stating explicitly so the next optimisation targets the right thing. */
			int deltaAt25Hz30 = FramedBytesPerObserverPerSecond(25, reconcileDelta, replicateDelta, 30);
			int payloadOnlyAt25Hz30 = 25 * (reconcileDelta + replicateDelta) * 30;
			double headerShare = (1.0 - (double)payloadOnlyAt25Hz30 / deltaAt25Hz30) * 100.0;
			TestContext.WriteLine();
			TestContext.WriteLine($"  At 25 entities / 30Hz on delta: {headerShare:F1}% of wire bytes are header, not payload.");
			TestContext.WriteLine($"  Halving the tick rate to 15Hz removes ~50% of BOTH, since every cost here is per tick.");
		}

		// ── Fixtures ─────────────────────────────────────────────────────────

		/// <summary>Three consecutive ticks of a walking player — one replicate packet's worth.</summary>
		private static CharacterReplicateData[] WalkingReplicatePacket()
		{
			CharacterReplicateData t0 = default;
			t0.AimDirection = AimDirectionCompression.Quantize(Quaternion.Euler(0f, 40f, 0f) * Vector3.forward);
			t0.MoveAxisForward = 1f;
			t0.MoveFlags = 1;

			CharacterReplicateData t1 = t0;
			t1.AimDirection = AimDirectionCompression.Quantize(Quaternion.Euler(0f, 41.2f, 0f) * Vector3.forward);

			CharacterReplicateData t2 = t1;
			t2.AimDirection = AimDirectionCompression.Quantize(Quaternion.Euler(0f, 42.4f, 0f) * Vector3.forward);

			return new[] { t0, t1, t2 };
		}

		private static KinematicCharacterMotorState MakeMotorState()
		{
			KinematicCharacterMotorState s = default;
			s.Position = new Vector3(112.5f, 30.9f, -47.25f);
			s.Rotation = Quaternion.Euler(0f, 20f, 0f);
			s.BaseVelocity = Vector3.zero;
			s.AttachedRigidbodyVelocity = Vector3.zero;
			s.MustUngroundTime = 0f;
			s.TimeSinceLastAbleToJump = 0f;
			s.TimeSinceJumpRequested = 0f;
			s.GroundingStatus = default;
			s.GroundingStatus.FoundAnyGround = true;
			s.GroundingStatus.IsStableOnGround = true;
			s.GroundingStatus.GroundNormal = Vector3.up;
			s.GroundingStatus.InnerGroundNormal = Vector3.up;
			s.GroundingStatus.OuterGroundNormal = Vector3.up;
			return s;
		}

		/// <summary>A reconcile snapshot for a geared, buffed character standing still.</summary>
		private static CharacterReconcileData MakeReconcileData()
		{
			CharacterReconcileData d = default;
			d.MotorState = MakeMotorState();
			d.AbilityID = 0;
			d.RemainingTicks = 0;
			d.Seed = 4242;
			d.PackedFlagsAndSlot = 0x1234;
			d.ResourceState = default;
			d.ResourceState.MaxHealth = 1200; d.ResourceState.Health = 1200f;
			d.ResourceState.MaxMana = 800; d.ResourceState.Mana = 800f;
			d.ResourceState.MaxStamina = 400; d.ResourceState.Stamina = 400f;
			d.ResourceState.NextRegenTick = 900;
			d.RngS0 = 0xDEADBEEF; d.RngS1 = 0x12345678;
			d.RngS2 = 0x0BADF00D; d.RngS3 = 0xFEEDFACE;
			d.Cooldowns = new[]
			{
				new CooldownReconcileEntry { AbilityID = 42, StartTick = 100, DurationTicks = 60 },
				new CooldownReconcileEntry { AbilityID = 43, StartTick = 140, DurationTicks = 300 },
			};
			d.Buffs = new[]
			{
				new BuffReconcileEntry { TemplateID = 3, ExpiryTick = 500, NextTickTick = 20, Stacks = 1, TickCount = 4, CumulativeTickMultiplier = 1 },
				new BuffReconcileEntry { TemplateID = 9, ExpiryTick = 1500, NextTickTick = 60, Stacks = 3, TickCount = 1, CumulativeTickMultiplier = 1 },
			};
			d.Equipment = new[]
			{
				new EquipmentReconcileEntry { TemplateID = 5, Slot = 1, Seed = 77, InstanceID = 900 },
				new EquipmentReconcileEntry { TemplateID = 6, Slot = 2, Seed = 78, InstanceID = 901 },
				new EquipmentReconcileEntry { TemplateID = 7, Slot = 3, Seed = 79, InstanceID = 902 },
				new EquipmentReconcileEntry { TemplateID = 8, Slot = 4, Seed = 80, InstanceID = 903 },
			};
			d.Attributes = new[]
			{
				new AttributeReconcileEntry { TemplateID = 1, Value = 25, ExternalModifier = 4 },
				new AttributeReconcileEntry { TemplateID = 2, Value = 31, ExternalModifier = 0 },
				new AttributeReconcileEntry { TemplateID = 3, Value = 18, ExternalModifier = 6 },
				new AttributeReconcileEntry { TemplateID = 4, Value = 44, ExternalModifier = 0 },
				new AttributeReconcileEntry { TemplateID = 5, Value = 12, ExternalModifier = 2 },
				new AttributeReconcileEntry { TemplateID = 6, Value = 9, ExternalModifier = 0 },
			};
			return d;
		}

		/// <summary>
		/// Copies the arrays so the "next" snapshot holds distinct instances with identical
		/// contents — the steady-state shape a producer actually creates, and the one that
		/// exercises the index-delta path rather than the ReferenceEquals shortcut.
		/// </summary>
		private static CharacterReconcileData CloneArrays(CharacterReconcileData source)
		{
			CharacterReconcileData copy = source;
			copy.Cooldowns = (CooldownReconcileEntry[])source.Cooldowns.Clone();
			copy.Buffs = (BuffReconcileEntry[])source.Buffs.Clone();
			copy.Equipment = (EquipmentReconcileEntry[])source.Equipment.Clone();
			copy.Attributes = (AttributeReconcileEntry[])source.Attributes.Clone();
			return copy;
		}
	}
}
