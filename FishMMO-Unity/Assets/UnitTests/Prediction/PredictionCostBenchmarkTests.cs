using System;
using System.Diagnostics;
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
	/// Measured CPU and allocation cost of the prediction pipeline, alongside the wire cost.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The bandwidth work already has a home in <see cref="PredictionBandwidthBenchmarkTests"/>.
	/// This fixture covers the two axes that were previously argued from reading rather than
	/// measurement: how much CPU a tick costs per character, and how much garbage a tick produces.
	/// Both scale per observed peer on every client while state forwarding is on, so both bear
	/// directly on whether 100-200 players per scene is reachable.
	/// </para>
	/// <para>
	/// <b>What makes these numbers meaningful.</b> <c>KCCPlayer.OnReplicate</c> calls
	/// <c>Motor.UpdatePhase1</c> and <c>UpdatePhase2</c> with no server or owner gate, and
	/// <c>KCCController</c> contains no <c>IsServer</c>/<c>IsOwner</c> branch at all. Every peer
	/// therefore runs a full motor solve for every character it observes, every tick. That is the
	/// dominant per-tick cost and it is what disabling state forwarding removes from clients.
	/// </para>
	/// <para>
	/// <b>What they are not.</b> These run in the editor against a bare physics scene with no
	/// terrain, no other colliders and no animation. A motor solving against real level geometry
	/// does more sweep work than one solving against nothing, so treat the KCC figure as a floor
	/// rather than a prediction of shipped cost. The relative comparison — which is what the
	/// architectural decision turns on — holds regardless.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class PredictionCostBenchmarkTests
	{
		private const int ServerTickRate = 30;

		/// <summary>Iterations per timed measurement. Large enough to swamp timer granularity.</summary>
		private const int Iterations = 2000;

		/// <summary>Warmup iterations, discarded — JIT and first-touch allocation are not the subject.</summary>
		private const int Warmup = 200;

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

			foreach (Type t in serializerTypes)
			{
				MethodInfo register = t.GetMethod("RegisterSerializers",
					BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
				LogAssert.IsNotNull(register, $"{t.Name} must expose a RegisterSerializers hook.");
				register.Invoke(null, null);
			}
		}

		#region Helpers.

		/// <summary>Times an action, returning microseconds per invocation.</summary>
		private static double MicrosPerOp(Action action, int iterations = Iterations)
		{
			for (int i = 0; i < Warmup; i++)
			{
				action();
			}

			Stopwatch sw = Stopwatch.StartNew();
			for (int i = 0; i < iterations; i++)
			{
				action();
			}
			sw.Stop();

			return sw.Elapsed.TotalMilliseconds * 1000.0 / iterations;
		}

		/// <summary>
		/// Bytes allocated per invocation, via total-heap deltas across a large batch.
		/// </summary>
		/// <remarks>
		/// <c>GC.GetAllocatedBytesForCurrentThread</c> reads as a flat zero under Unity's Mono
		/// runtime, which silently turns every allocation measurement into "allocates nothing" —
		/// a false pass rather than a failure. Heap deltas are noisier but real. A collection
		/// landing mid-batch shows up as a negative or absurdly small delta, so several trials are
		/// run and the median of the plausible ones is returned; -1 means no trial survived and the
		/// caller must report the measurement as unavailable rather than as zero.
		/// </remarks>
		private static double BytesPerOp(Action action, int iterations = 20000)
		{
			for (int i = 0; i < Warmup; i++)
			{
				action();
			}

			System.Collections.Generic.List<double> samples = new System.Collections.Generic.List<double>();

			for (int trial = 0; trial < 5; trial++)
			{
				GC.Collect();
				GC.WaitForPendingFinalizers();
				GC.Collect();

				long before = GC.GetTotalMemory(false);
				for (int i = 0; i < iterations; i++)
				{
					action();
				}
				long after = GC.GetTotalMemory(false);

				double per = (after - before) / (double)iterations;
				if (per > 0.0)
				{
					samples.Add(per);
				}
			}

			if (samples.Count == 0)
			{
				return -1.0;
			}

			samples.Sort();
			return samples[samples.Count / 2];
		}

		/// <summary>Formats a byte measurement, marking an unavailable one rather than printing zero.</summary>
		private static string Bytes(double v) => v < 0.0 ? "unmeasurable" : $"{v:F0} B";

		/// <summary>Builds a representative in-combat reconcile payload.</summary>
		private static CharacterReconcileData BuildReconcile(int cooldowns, int buffs, int equipment, int attributes)
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

			d.Cooldowns = new CooldownReconcileEntry[cooldowns];
			for (int i = 0; i < cooldowns; i++)
			{
				d.Cooldowns[i] = new CooldownReconcileEntry { AbilityID = 100 + i, StartTick = 900u + (uint)i, DurationTicks = 60 };
			}

			d.Buffs = new BuffReconcileEntry[buffs];
			for (int i = 0; i < buffs; i++)
			{
				d.Buffs[i] = new BuffReconcileEntry
				{
					TemplateID = 200 + i, Stacks = 1, ExpiryTick = 1200u + (uint)i,
					NextTickTick = 930u + (uint)i, TickCount = i, CumulativeTickMultiplier = 1,
				};
			}

			d.Equipment = new EquipmentReconcileEntry[equipment];
			for (int i = 0; i < equipment; i++)
			{
				d.Equipment[i] = new EquipmentReconcileEntry { Slot = (byte)i, TemplateID = 300 + i, Seed = i * 7, ItemID = 5000 + i };
			}

			d.Attributes = new AttributeReconcileEntry[attributes];
			for (int i = 0; i < attributes; i++)
			{
				d.Attributes[i] = new AttributeReconcileEntry { TemplateID = 400 + i, Value = 10 + i, ExternalModifier = i };
			}

			return d;
		}

		#endregion

		#region KCC motor solve — the dominant per-tick cost.

		/// <summary>
		/// Cost of one character's motor solve, which every peer pays for every character it observes.
		/// </summary>
		/// <remarks>
		/// This is the measurement the client-side scaling argument rests on. Because
		/// <c>KCCPlayer.OnReplicate</c> has no owner gate around <c>UpdatePhase1</c>/<c>UpdatePhase2</c>,
		/// a client observing 60 peers runs 60 of these per tick — 1800 per second at tick rate 30 —
		/// purely to keep predicted spectators in step. Disabling state forwarding removes all of them
		/// from the client while leaving the server's own cost untouched, since the server must
		/// simulate every character regardless.
		/// </remarks>
		[Test]
		public void Measure_KccMotorSolve_PerCharacterPerTick()
		{
			GameObject go = new GameObject("MotorBench");
			try
			{
				CapsuleCollider capsule = go.AddComponent<CapsuleCollider>();
				capsule.radius = 0.3f;
				capsule.height = 1.6f;
				capsule.center = new Vector3(0f, 0.8f, 0f);

				KinematicCharacterMotor motor = go.AddComponent<KinematicCharacterMotor>();
				KCCController controller = go.AddComponent<KCCController>();
				controller.Motor = motor;
				motor.CharacterController = controller;

				/* Awake does not run for a component added to a GameObject in EditMode, and it is
				 * where the motor caches _transform. Without it SetPosition dereferences null and
				 * the measurement never executes. Invoking it directly is the same trick the
				 * serializer fixtures use for [RuntimeInitializeOnLoadMethod]. */
				MethodInfo awake = typeof(KinematicCharacterMotor).GetMethod("Awake",
					BindingFlags.Instance | BindingFlags.NonPublic);
				LogAssert.IsNotNull(awake, "KinematicCharacterMotor.Awake must exist; it caches _transform.");
				awake.Invoke(motor, null);

				motor.SetCapsuleDimensions(0.3f, 1.6f, 0.8f);
				motor.SetPosition(new Vector3(0f, 5f, 0f));

				const float dt = 1f / ServerTickRate;
				double micros = MicrosPerOp(() =>
				{
					motor.UpdatePhase1(dt);
					motor.UpdatePhase2(dt);
				}, 1000);

				double bytes = BytesPerOp(() =>
				{
					motor.UpdatePhase1(dt);
					motor.UpdatePhase2(dt);
				}, 5000);

				TestContext.WriteLine(
					$"MEASURE KCC motor solve: {micros:F2} us/char/tick, {Bytes(bytes)} allocated");

				double budgetMs = 1000.0 / ServerTickRate;
				TestContext.WriteLine(
					$"MEASURE tick budget at {ServerTickRate}Hz = {budgetMs:F1} ms; " +
					$"one core saturates at ~{budgetMs * 1000.0 / Math.Max(micros, 0.0001):F0} concurrent motor solves");

				foreach (int peers in new[] { 25, 60, 150, 200 })
				{
					double msPerTick = peers * micros / 1000.0;
					TestContext.WriteLine(
						$"MEASURE {peers,3} observed peers -> {msPerTick:F2} ms/tick " +
						$"({100.0 * msPerTick / budgetMs:F1}% of one core's tick budget)");
				}

				LogAssert.IsTrue(micros > 0.0, "The motor solve must actually execute to be measured.");
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		#endregion

		#region Reconcile CPU and allocation.

		/// <summary>
		/// Serialize and deserialize cost of a reconcile payload, and what it allocates.
		/// </summary>
		/// <remarks>
		/// Paid once per observer per tick while state forwarding is on, and once per owner per tick
		/// after it is disabled. The allocation figure matters more than it looks: the reconcile
		/// carries four arrays, and the read side must materialise them on every peer that receives
		/// it, so this is per-observer garbage rather than a one-off.
		/// </remarks>
		[Test]
		public void Measure_ReconcileSerialization_CpuAndAllocation()
		{
			CharacterReconcileData prev = BuildReconcile(2, 3, 6, 8);
			CharacterReconcileData next = BuildReconcile(2, 3, 6, 8);
			next.MotorState.Position += new Vector3(0.12f, 0f, 0.04f);
			next.RemainingTicks = 13;
			next.Sequence = unchecked((byte)(prev.Sequence + 1)); // delta chain continuity

			int deltaBytes = 0;
			double writeMicros = MicrosPerOp(() =>
			{
				Writer w = new Writer();
				w.WriteDelta(prev, next, DeltaSerializerOption.RootSerialize);
				deltaBytes = w.Length;
			});

			Writer prepared = new Writer();
			prepared.WriteDelta(prev, next, DeltaSerializerOption.RootSerialize);
			ArraySegment<byte> payload = prepared.GetArraySegment();

			double readMicros = MicrosPerOp(() =>
			{
				Reader r = new Reader(payload, null);
				r.ReadDelta(prev);
			});

			double writeBytes = BytesPerOp(() =>
			{
				Writer w = new Writer();
				w.WriteDelta(prev, next, DeltaSerializerOption.RootSerialize);
			});

			double readBytes = BytesPerOp(() =>
			{
				Reader r = new Reader(payload, null);
				r.ReadDelta(prev);
			});

			TestContext.WriteLine(
				$"MEASURE reconcile delta: {deltaBytes}B wire | write {writeMicros:F2} us / {Bytes(writeBytes)} | " +
				$"read {readMicros:F2} us / {Bytes(readBytes)}");

			foreach (int observers in new[] { 25, 60, 150 })
			{
				double serverMs = observers * writeMicros / 1000.0;
				TestContext.WriteLine(
					$"MEASURE server write cost for one character at {observers,3} observers: " +
					$"{serverMs:F2} ms/tick (forwarding on) vs {writeMicros / 1000.0:F3} ms/tick (owner only)");
			}

			LogAssert.IsTrue(deltaBytes > 0, "The reconcile must produce bytes to be measured.");
		}

		/// <summary>
		/// Allocation produced by building one reconcile snapshot, before it is ever serialised.
		/// </summary>
		/// <remarks>
		/// <c>CharacterReconcileData</c> carries four arrays — cooldowns, buffs, equipment and
		/// attributes. Whether these are rebuilt or reused per tick decides whether the prediction
		/// pipeline produces steady garbage at 30 Hz per character. Measured here at a realistic
		/// in-combat loadout.
		/// </remarks>
		[Test]
		public void Measure_ReconcileSnapshot_Allocation()
		{
			double idle = BytesPerOp(() => BuildReconcile(0, 0, 6, 8));
			double combat = BytesPerOp(() => BuildReconcile(2, 3, 6, 8));
			double heavy = BytesPerOp(() => BuildReconcile(6, 12, 12, 24));

			TestContext.WriteLine(
				$"MEASURE reconcile snapshot allocation: idle {Bytes(idle)}, combat {Bytes(combat)}, heavy {Bytes(heavy)}");

			if (combat > 0)
			{
				double perSecond = combat * ServerTickRate;
				TestContext.WriteLine(
					$"MEASURE at {ServerTickRate}Hz that is {perSecond / 1024.0:F1} KB/s per character if rebuilt every tick");

				foreach (int chars in new[] { 100, 200 })
				{
					TestContext.WriteLine(
						$"MEASURE {chars} characters -> {chars * perSecond / 1024.0 / 1024.0:F1} MB/s of garbage " +
						"if rebuilt every tick");
				}
			}

			LogAssert.IsTrue(combat > 0,
				"Building a snapshot with four arrays must allocate. A zero here means the measurement " +
				"method is broken, not that the code is allocation-free — do not report it as a result.");
		}

		#endregion

		#region Ability spawn cost.

		/// <summary>
		/// Cost of the GameObject churn an ability cast produces.
		/// </summary>
		/// <remarks>
		/// <c>AbilityObject.Spawn</c> calls <c>Instantiate</c> and
		/// <c>DestroyAbilityObjectInternal</c> calls <c>Destroy</c> — there is no pooling on this
		/// path, and <c>AbilityContainerAllocator.Allocate</c> additionally allocates a
		/// <c>Dictionary</c> per spawn. Every cast therefore costs a full instantiate/destroy cycle
		/// on the server and on every peer that simulates it.
		/// </remarks>
		[Test]
		public void Measure_AbilityObject_InstantiateDestroyCost()
		{
			GameObject prefab = new GameObject("AbilityPrefab");
			try
			{
				BoxCollider box = prefab.AddComponent<BoxCollider>();
				box.size = Vector3.one;
				Rigidbody rb = prefab.AddComponent<Rigidbody>();
				rb.isKinematic = true;
				rb.useGravity = false;
				prefab.SetActive(false);

				double micros = MicrosPerOp(() =>
				{
					GameObject go = UnityEngine.Object.Instantiate(prefab);
					UnityEngine.Object.DestroyImmediate(go);
				}, 500);

				double bytes = BytesPerOp(() =>
				{
					GameObject go = UnityEngine.Object.Instantiate(prefab);
					UnityEngine.Object.DestroyImmediate(go);
				}, 2000);

				TestContext.WriteLine(
					$"MEASURE ability object instantiate+destroy: {micros:F1} us, {Bytes(bytes)} managed");

				foreach (int casters in new[] { 50, 200 })
				{
					double msPerSec = casters * 1.0 * micros / 1000.0;
					TestContext.WriteLine(
						$"MEASURE {casters,3} casters at 1 cast/s -> {msPerSec:F2} ms/s of instantiate/destroy " +
						$"({100.0 * msPerSec / 1000.0:F2}% of one core)");
				}

				LogAssert.IsTrue(micros > 0, "Instantiate/destroy must execute to be measured.");
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(prefab);
			}
		}

		#endregion
	}
}
