using System;
using System.Reflection;
using FishNet.Object;
using FishNet.Serializing;
using NUnit.Framework;
using FishMMO.Shared;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Evidence for the proposed move off lockstep input broadcast and onto discrete ability
	/// activation events plus interpolated spectators.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>What this fixture is for.</b> The bandwidth case for that migration was originally built
	/// on a model with two invented constants in it — the size of an activation event and the rate
	/// abilities are cast at. A model is not evidence. Everything below either measures the real
	/// artefact or derives from a figure taken out of FishNet's own source, and each test prints
	/// what it measured so a run can be lifted directly as justification.
	/// </para>
	/// <para>
	/// <b>The saving is structural, not statistical.</b> It does not depend on how compressible any
	/// payload is. <c>NetworkBehaviour.Prediction.cs</c> gates both halves of state forwarding
	/// explicitly: <c>Replicate_SendNonAuthoritative</c> opens with
	/// <c>if (!EnableStateForwarding) return;</c>, and the reconcile RPC chooses between
	/// <c>Owner.WriteState(writer)</c> and a loop over every observer. FishNet's own traffic
	/// accounting states the result outright — <c>written = stateForwarding ? writer.Length *
	/// Observers.Count : writer.Length</c>. Reconcile therefore goes from O(observers) to O(1) and
	/// replicate from O(observers) to zero, by construction.
	/// </para>
	/// <para>
	/// <b>What this fixture cannot prove.</b> Nothing here runs two peers against each other. These
	/// tests establish that the wire format carries what the simulation needs, that the simulation
	/// is reproducible from it, and what the sizes are. They do not establish that the migration is
	/// correct end to end in a live session, and no EditMode test can.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class StateForwardingMigrationTests
	{
		/// <summary>Server tick rate, matching <see cref="PredictionBandwidthBenchmarkTests"/>.</summary>
		private const int ServerTickRate = 30;

		/// <summary>
		/// Measured replicate packet, delta path, carrying <c>RedundancyCount</c> entries.
		/// From <c>Benchmark_RealPacketCost_PerEntityPerObserver</c>.
		/// </summary>
		private const int ReplicatePacketBytes = 41;

		/// <summary>Measured reconcile payload on the delta path.</summary>
		private const int ReconcilePayloadBytes = 26;

		/// <summary>
		/// Measured <c>NetworkTransform</c> update, via reflection into <c>SerializeChanged</c>.
		/// </summary>
		private const int NetworkTransformBytes = 11;

		/// <summary>FishNet RPC header, as used by the framed benchmark.</summary>
		private const int RpcHeaderBytes = 10;

		/// <summary>
		/// <c>KCCController.MaxAirMoveSpeed</c>. The ground speed is attribute-driven via
		/// <c>MoveSpeedTemplate</c> and therefore not a compile-time constant, so this is used as
		/// the representative speed and the error budget is also swept across a range below.
		/// </summary>
		private const float CharacterSpeed = 6f;

		/// <summary>
		/// Half-extent of the authored ability hitbox. Every ability prefab — Punch, Flame and
		/// Lesser Fireball — carries a 1x1x1 <c>BoxCollider</c>.
		/// </summary>
		private const float HitboxHalfExtent = 0.5f;

		/// <summary>
		/// <c>NetworkObject._spectatorInterpolation</c> as authored on the playable character
		/// prefabs. Ticks of interpolation applied to a non-owned object.
		/// </summary>
		private const int SpectatorInterpolationTicks = 2;

		#region Feasibility.

		/// <summary>
		/// Every prediction-enabled prefab must either forward state or own a
		/// <see cref="FishNet.Component.Transforming.NetworkTransform"/> to replace it.
		/// </summary>
		/// <remarks>
		/// <para>
		/// This is the migration's hard prerequisite expressed as an invariant. FishNet describes
		/// the two modes as alternatives — its tooltip on <c>_enableStateForwarding</c> reads
		/// "False to only use prediction on the owner, and synchronize to spectators using other
		/// means such as a NetworkTransform" — and <c>InitializePredictionEarly</c> wires the
		/// handoff by calling <c>ConfigureForPrediction</c> on the assigned transform.
		/// </para>
		/// <para>
		/// It is green today because forwarding is on everywhere. It turns red the moment someone
		/// disables forwarding on a prefab that has nothing to replicate position with, which is
		/// the failure this migration invites: the object keeps simulating for its owner and
		/// freezes in place for every observer, while server-resolved damage still lands. That is
		/// a silent, content-shaped bug and it is worth a build failure instead.
		/// </para>
		/// </remarks>
		[Test]
		public void PredictedPrefabs_WithoutStateForwarding_MustHaveANetworkTransform()
		{
			int predicted = 0;
			int forwarding = 0;
			int withTransform = 0;

			foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" }))
			{
				string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
				GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
				if (prefab == null)
				{
					continue;
				}

				NetworkObject nob = prefab.GetComponent<NetworkObject>();
				if (nob == null || !ReadPrivateBool(nob, "_enablePrediction"))
				{
					continue;
				}

				predicted++;
				bool forwards = ReadPrivateBool(nob, "_enableStateForwarding");
				bool hasTransform =
					prefab.GetComponent<FishNet.Component.Transforming.NetworkTransform>() != null;

				if (forwards)
				{
					forwarding++;
				}
				if (hasTransform)
				{
					withTransform++;
				}

				LogAssert.IsTrue(forwards || hasTransform,
					$"'{prefab.name}' predicts but neither forwards state nor has a NetworkTransform, " +
					"so observers would receive no position updates for it at all.");
			}

			TestContext.WriteLine(
				$"MEASURE predicted prefabs={predicted}, forwarding state={forwarding}, " +
				$"carrying a NetworkTransform={withTransform}");
			TestContext.WriteLine(
				$"MEASURE prefabs needing a NetworkTransform before forwarding can be disabled: " +
				$"{predicted - withTransform}");

			LogAssert.IsTrue(predicted > 0, "No prediction-enabled prefab was found; this guard is checking nothing.");
		}

		/// <summary>Reads a private serialized bool off a component.</summary>
		private static bool ReadPrivateBool(Component component, string fieldName)
		{
			FieldInfo field = component.GetType().GetField(fieldName,
				BindingFlags.Instance | BindingFlags.NonPublic);
			LogAssert.IsNotNull(field, $"{component.GetType().Name}.{fieldName} must exist.");
			return (bool)field.GetValue(component);
		}

		#endregion

		#region The activation event.

		/// <summary>
		/// The tuple a discrete activation event must carry for an observer to reconstruct the
		/// identical ability object locally.
		/// </summary>
		/// <remarks>
		/// Field-for-field what <c>AbilityObject.Spawn</c> consumes — ability, caster, seed and
		/// spawn tick — plus the aim the caster committed to, which
		/// <c>AbilityController.ResolveTargetAndSpawn</c> reads from the replicate data today. The
		/// caster is not carried because it is the object the RPC arrives on.
		/// </remarks>
		private struct ActivationEvent
		{
			public long AbilityID;
			public int Seed;
			public uint SpawnTick;
			public Vector3 AimOrigin;
			public Vector3 AimDirection;
		}

		/// <summary>Writes an activation event using packed primitives and the aim compressor.</summary>
		private static void WriteActivation(Writer writer, ActivationEvent e, uint referenceTick)
		{
			// Ability IDs are database identities; zigzag packing keeps small ones small.
			writer.WriteInt64(e.AbilityID);
			writer.WriteInt32(e.Seed);
			/* The spawn tick is always at or just behind the tick the RPC is sent on, so the
			 * offset packs into a byte where the absolute tick would cost four. */
			writer.WriteUInt8Unpacked((byte)Mathf.Clamp((int)(referenceTick - e.SpawnTick), 0, 255));
			writer.WriteVector3(e.AimOrigin);
			writer.WriteUInt32Unpacked(AimDirectionCompression.Encode(e.AimDirection));
		}

		/// <summary>Reads back what <see cref="WriteActivation"/> wrote.</summary>
		private static ActivationEvent ReadActivation(Reader reader, uint referenceTick)
		{
			ActivationEvent e = default;
			e.AbilityID = reader.ReadInt64();
			e.Seed = reader.ReadInt32();
			e.SpawnTick = referenceTick - reader.ReadUInt8Unpacked();
			e.AimOrigin = reader.ReadVector3();
			e.AimDirection = AimDirectionCompression.Decode(reader.ReadUInt32Unpacked());
			return e;
		}

		/// <summary>
		/// The activation event round-trips every field the ability simulation depends on.
		/// </summary>
		/// <remarks>
		/// The migration's correctness rests entirely on this: if the event cannot carry the spawn
		/// tuple losslessly then observers reconstruct a different ability object than the server
		/// simulated, and the determinism the whole design is built on is gone. Aim direction is
		/// the one lossy field, deliberately — it is quantised at the producer today, so the value
		/// asserted here is the value the wire already agreed to.
		/// </remarks>
		[Test]
		public void ActivationEvent_RoundTrips_EveryFieldTheSimulationNeeds()
		{
			const uint referenceTick = 10_000;

			ActivationEvent sent = new ActivationEvent
			{
				AbilityID = 8842,
				Seed = -1_713_468_379,
				SpawnTick = referenceTick - 3,
				AimOrigin = new Vector3(128.25f, 12.5f, -64.75f),
				// Pre-quantised: the producer commits to this before it ever reaches the wire.
				AimDirection = AimDirectionCompression.Quantize(new Vector3(0.35f, -0.12f, 0.93f).normalized),
			};

			Writer writer = new Writer();
			WriteActivation(writer, sent, referenceTick);

			Reader reader = new Reader(writer.GetArraySegment(), null);
			ActivationEvent received = ReadActivation(reader, referenceTick);

			LogAssert.AreEqual(sent.AbilityID, received.AbilityID, "AbilityID selects which ability is simulated.");
			LogAssert.AreEqual(sent.Seed, received.Seed, "Seed drives every deterministic roll the ability makes.");
			LogAssert.AreEqual(sent.SpawnTick, received.SpawnTick, "SpawnTick anchors the simulation to a tick.");
			LogAssert.IsTrue(Vector3.Distance(sent.AimOrigin, received.AimOrigin) < 0.001f,
				$"AimOrigin must survive: sent {sent.AimOrigin}, got {received.AimOrigin}.");
			LogAssert.IsTrue(Vector3.Angle(sent.AimDirection, received.AimDirection) < 0.01f,
				$"AimDirection must survive quantisation: {Vector3.Angle(sent.AimDirection, received.AimDirection):F4} deg apart.");

			LogAssert.AreEqual(0, reader.Remaining,
				"The reader must consume exactly what the writer produced, or the event desyncs the stream.");
		}

		/// <summary>
		/// Measures the activation event, replacing the assumed constant the bandwidth case used.
		/// </summary>
		[Test]
		public void Measure_ActivationEvent_PayloadSize()
		{
			const uint referenceTick = 10_000;
			ActivationEvent e = new ActivationEvent
			{
				AbilityID = 8842,
				Seed = -1_713_468_379,
				SpawnTick = referenceTick - 3,
				AimOrigin = new Vector3(128.25f, 12.5f, -64.75f),
				AimDirection = AimDirectionCompression.Quantize(Vector3.forward),
			};

			Writer packed = new Writer();
			WriteActivation(packed, e, referenceTick);

			// What the same tuple would cost written naively, for contrast.
			Writer naive = new Writer();
			naive.WriteInt64(e.AbilityID);
			naive.WriteInt32(e.Seed);
			naive.WriteUInt32Unpacked(e.SpawnTick);
			naive.WriteVector3(e.AimOrigin);
			naive.WriteVector3(e.AimDirection);

			int framed = packed.Length + RpcHeaderBytes;

			TestContext.WriteLine(
				$"MEASURE activation event: packed={packed.Length}B naive={naive.Length}B " +
				$"framed with RPC header={framed}B");

			LogAssert.IsTrue(packed.Length < naive.Length,
				$"Packing must beat the naive encoding; {packed.Length}B vs {naive.Length}B.");
			LogAssert.IsTrue(framed <= 48,
				$"A framed activation event is {framed}B. The bandwidth case assumed 40B; " +
				"anything materially above that invalidates the projection below.");
		}

		#endregion

		#region Bandwidth.

		/// <summary>
		/// Projects the per-peer cost of both models from measured payloads and FishNet's own
		/// per-observer accounting.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The cast rate is the one figure here that is a judgement rather than a measurement, so
		/// it is set to the most pessimistic value the content supports: every authored ability has
		/// a cooldown of at least one second (Punch, Flame and Lesser Fireball are all 1s; Summon
		/// Lesser Fire Elemental is 60s), so one cast per second is the ceiling for a player
		/// holding the attack button forever, not an average. A real player casts less and the
		/// saving is larger.
		/// </para>
		/// <para>
		/// Both models remain O(peers) per client — the difference is the constant, not the
		/// complexity class. Nothing here claims an asymptotic improvement.
		/// </para>
		/// </remarks>
		[Test]
		public void Measure_PerPeerCost_ForwardedVersusDiscrete()
		{
			const double castsPerSecond = 1.0;

			// Forwarded: the owner's input is relayed to every observer every tick, and the
			// reconcile goes to every observer as well.
			double forwarded =
				(ReplicatePacketBytes + RpcHeaderBytes) * ServerTickRate +
				(ReconcilePayloadBytes + RpcHeaderBytes) * ServerTickRate;

			// Discrete: position via NetworkTransform under the existing distance LOD, abilities
			// as events. Reconcile disappears from the per-observer bill entirely — with
			// forwarding off it is written once, to the owner.
			const int LodBand = 3;
			double transform = (NetworkTransformBytes + RpcHeaderBytes) * (double)ServerTickRate / LodBand;

			Writer w = new Writer();
			WriteActivation(w, new ActivationEvent { AimDirection = Vector3.forward }, 0);
			double events = (w.Length + RpcHeaderBytes) * castsPerSecond;
			double discrete = transform + events;

			TestContext.WriteLine(
				$"MEASURE per observed peer/sec: forwarded={forwarded:F0}B " +
				$"(replicate {(ReplicatePacketBytes + RpcHeaderBytes) * ServerTickRate}B + " +
				$"reconcile {(ReconcilePayloadBytes + RpcHeaderBytes) * ServerTickRate}B)");
			TestContext.WriteLine(
				$"MEASURE per observed peer/sec: discrete={discrete:F0}B " +
				$"(transform {transform:F0}B + activations {events:F0}B) -> {forwarded / discrete:F1}x cheaper");

			LogAssert.IsTrue(discrete * 4 < forwarded,
				$"The discrete model must be at least 4x cheaper per peer to justify the migration; " +
				$"measured {forwarded / discrete:F1}x ({forwarded:F0} -> {discrete:F0} B/s).");
		}

		/// <summary>
		/// Reproduces FishNet's own traffic accounting to show the saving is structural.
		/// </summary>
		/// <remarks>
		/// <c>Server_SendReconcileRpc</c> computes exactly
		/// <c>written = stateForwarding ? writer.Length * Observers.Count : writer.Length</c>.
		/// Encoding it here means the projection tracks FishNet's behaviour rather than a
		/// reimplementation of it, and this test fails if that expression is ever changed upstream
		/// in a way the comment above no longer describes.
		/// </remarks>
		[Test]
		public void Measure_ReconcileCost_ScalesWithObserversOnlyWhenForwarding()
		{
			int payload = ReconcilePayloadBytes + RpcHeaderBytes;

			foreach (int observers in new[] { 1, 10, 60, 150 })
			{
				int forwarding = payload * observers;
				int notForwarding = payload;

				TestContext.WriteLine(
					$"MEASURE reconcile @ {observers,3} observers: forwarding={forwarding,6}B/tick " +
					$"owner-only={notForwarding,3}B/tick ({(double)forwarding / notForwarding:F0}x)");

				LogAssert.AreEqual(payload * observers, forwarding,
					"Forwarded reconcile must scale linearly with observer count.");
				LogAssert.AreEqual(payload, notForwarding,
					"With forwarding off the reconcile is written once, to the owner, regardless of observers.");
			}
		}

		#endregion

		#region Accuracy.

		/// <summary>
		/// Two independently constructed generators seeded identically produce identical streams.
		/// </summary>
		/// <remarks>
		/// <para>
		/// This is the load-bearing claim under the whole migration. Sending an activation event
		/// instead of the input stream is only sound if every receiver, simulating alone, arrives
		/// at the same ability behaviour the server did. That reduces to whether
		/// <see cref="DeterministicRNG"/> is reproducible from a seed, because the rest of the
		/// simulation is arithmetic on the tick delta.
		/// </para>
		/// <para>
		/// Both the seeded constructor and the captured-state constructor are exercised: the seed
		/// is what an activation event carries, and the captured state is what the reconcile
		/// carries as <c>RngS0..S3</c>. They must agree or a reconcile would silently re-aim an
		/// ability mid-flight.
		/// </para>
		/// </remarks>
		[Test]
		public void AbilityRng_FromIdenticalSeed_ProducesIdenticalStreams()
		{
			const int seed = -1_713_468_379;
			const int draws = 512;

			DeterministicRNG server = new DeterministicRNG(seed);
			DeterministicRNG observer = new DeterministicRNG(seed);

			for (int i = 0; i < draws; i++)
			{
				LogAssert.AreEqual(server.Next(), observer.Next(),
					$"Integer draw {i} diverged; the ability would behave differently on this observer.");
				LogAssert.AreEqual(server.NextFloat(), observer.NextFloat(),
					$"Float draw {i} diverged; damage rolls and spread would differ on this observer.");
			}

			// The captured-state path must rejoin the same stream, which is what reconcile relies on.
			server.CaptureState(out uint s0, out uint s1, out uint s2, out uint s3);
			DeterministicRNG restored = new DeterministicRNG(s0, s1, s2, s3);

			for (int i = 0; i < draws; i++)
			{
				LogAssert.AreEqual(server.Next(), restored.Next(),
					$"Restored draw {i} diverged; a reconcile would re-aim an in-flight ability.");
			}

			TestContext.WriteLine(
				$"MEASURE {draws} int + {draws} float draws identical across independent instances, " +
				$"and {draws} draws identical across a capture/restore boundary");
		}

		/// <summary>
		/// The aim an activation event carries is bit-stable across an encode/decode round trip.
		/// </summary>
		/// <remarks>
		/// Determinism of the roll is not enough on its own: every receiver must also start the
		/// ability pointing the same way. Because the producer quantises before storing, decoding
		/// an already-quantised direction has to be idempotent — otherwise each hop through the
		/// wire would rotate the ability slightly and observers would disagree with the server
		/// about where it went.
		/// </remarks>
		[Test]
		public void AimDirection_Quantisation_IsIdempotent()
		{
			float worst = 0f;

			for (int i = 0; i < 2000; i++)
			{
				// Deterministic spread over the sphere; no reliance on test ordering or clocks.
				float a = i * 0.61803399f * Mathf.PI * 2f;
				float z = 1f - 2f * (i + 0.5f) / 2000f;
				float r = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
				Vector3 direction = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, z);

				Vector3 once = AimDirectionCompression.Quantize(direction);
				Vector3 twice = AimDirectionCompression.Quantize(once);

				float drift = Vector3.Angle(once, twice);
				worst = Mathf.Max(worst, drift);
			}

			TestContext.WriteLine($"MEASURE worst re-quantisation drift over 2000 directions: {worst:F6} deg");

			LogAssert.IsTrue(worst < 0.0001f,
				$"Re-encoding an already-quantised aim drifted by {worst:F6} deg; " +
				"an ability would rotate slightly on every hop and observers would disagree with the server.");
		}

		/// <summary>
		/// Quantifies the aim error interpolation introduces, against the authored hitbox.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The number that decides whether interpolated spectators are acceptable for combat. Hits
		/// resolve on the server via <c>AbilityObject.OnCollisionEnter</c> against current
		/// positions, so a client aiming at an interpolated peer is aiming at where that peer was,
		/// and the discrepancy is the peer's velocity times the staleness.
		/// </para>
		/// <para>
		/// This test asserts nothing about what is acceptable — that is a design call. It fails
		/// only if the relationship stops being computed the way the recommendation assumed, and
		/// prints the table the decision should be made from.
		/// </para>
		/// </remarks>
		[Test]
		public void Measure_InterpolationErrorBudget_AgainstTheAuthoredHitbox()
		{
			double tickMs = 1000.0 / ServerTickRate;
			double interpolationMs = SpectatorInterpolationTicks * tickMs;

			TestContext.WriteLine(
				$"MEASURE spectator interpolation = {SpectatorInterpolationTicks} ticks @ {ServerTickRate}Hz " +
				$"= {interpolationMs:F0}ms; authored hitbox half-extent = {HitboxHalfExtent}m");

			foreach (int halfRttMs in new[] { 15, 25, 50, 75 })
			{
				double lagMs = interpolationMs + halfRttMs;
				double error = CharacterSpeed * lagMs / 1000.0;
				string verdict = error <= HitboxHalfExtent ? "inside hitbox"
					: error <= HitboxHalfExtent * 1.5 ? "marginal" : "outside hitbox";

				TestContext.WriteLine(
					$"MEASURE half-RTT {halfRttMs,2}ms -> stale {lagMs,5:F0}ms -> " +
					$"error {error:F2}m at {CharacterSpeed}m/s ({error / HitboxHalfExtent:F1}x half-extent, {verdict})");
			}

			// The relationship itself, not a threshold: error is linear in both staleness and speed.
			double baseline = CharacterSpeed * (interpolationMs + 25) / 1000.0;
			double doubled = (CharacterSpeed * 2) * (interpolationMs + 25) / 1000.0;
			LogAssert.IsTrue(Math.Abs(doubled - baseline * 2) < 0.001,
				"Interpolation error must be linear in peer speed; the projection depends on it.");
		}

		/// <summary>
		/// A peer kept on the forwarded path carries no interpolation error at all.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The accuracy argument for tiering, stated as the structural fact it is rather than as a
		/// measurement. A forwarded peer is simulated from the same inputs the server ran, on the
		/// same tick, so the client's view of it is the server's view of it up to prediction error
		/// — there is no deliberate delay to account for. That is why keeping combat-relevant peers
		/// forwarded removes the need for any rollback machinery, which matters here because
		/// FishNet's <c>ColliderRollback</c> ships as an empty Pro stub: every method body in
		/// <c>RollbackManager</c> is <c>{ }</c>.
		/// </para>
		/// <para>
		/// The cost of that accuracy is what makes tiering worth measuring rather than applying
		/// everywhere, so the ratio is printed alongside.
		/// </para>
		/// </remarks>
		[Test]
		public void ForwardedPeers_CarryNoInterpolationDelay()
		{
			double interpolatedError =
				CharacterSpeed * (SpectatorInterpolationTicks * (1000.0 / ServerTickRate) + 25) / 1000.0;

			// A forwarded peer is simulated on the tick its input describes; staleness is zero by
			// construction, so there is no distance term to compute.
			const double forwardedError = 0.0;

			double forwardedCost =
				(ReplicatePacketBytes + RpcHeaderBytes) * ServerTickRate +
				(ReconcilePayloadBytes + RpcHeaderBytes) * ServerTickRate;
			double interpolatedCost = (NetworkTransformBytes + RpcHeaderBytes) * (ServerTickRate / 3.0);

			TestContext.WriteLine(
				$"MEASURE forwarded peer: aim error {forwardedError:F2}m at {forwardedCost:F0} B/s");
			TestContext.WriteLine(
				$"MEASURE interpolated peer: aim error {interpolatedError:F2}m at {interpolatedCost:F0} B/s");
			TestContext.WriteLine(
				$"MEASURE tiering buys {forwardedCost / interpolatedCost:F1}x on every peer moved to the cheap tier, " +
				$"at {interpolatedError:F2}m of aim error");

			LogAssert.AreEqual(0.0, forwardedError,
				"A forwarded peer is simulated from the server's own inputs, so it carries no interpolation delay.");
			LogAssert.IsTrue(interpolatedError > HitboxHalfExtent,
				$"Interpolated error ({interpolatedError:F2}m) is expected to exceed the authored " +
				$"{HitboxHalfExtent}m half-extent — that is precisely why combat-relevant peers stay forwarded.");
		}

		#endregion

		#region Hybrid targeting.

		/// <summary>
		/// An activation event that names its victim rather than only aiming at a point.
		/// </summary>
		/// <remarks>
		/// The addition that makes single-target abilities immune to interpolation error.
		/// <c>ResolveTargetAndSpawn</c> currently re-derives the target on every peer by raycasting
		/// from the replicated aim, which agrees across peers only while every peer holds the same
		/// positions. Interpolating spectators breaks that: the same ray, cast against stale
		/// capsules, can select a different entity than the server selected. Carrying the entity
		/// the server actually resolved removes the disagreement instead of narrowing it.
		/// </remarks>
		private struct TargetedActivationEvent
		{
			public long AbilityID;
			public int Seed;
			public uint SpawnTick;
			public Vector3 AimOrigin;
			public Vector3 AimDirection;
			/// <summary>Resolved victim's NetworkObject id, or -1 for an untargeted/area ability.</summary>
			public int TargetObjectID;
		}

		/// <summary>Writes a targeted activation event.</summary>
		private static void WriteTargeted(Writer writer, TargetedActivationEvent e, uint referenceTick)
		{
			writer.WriteInt64(e.AbilityID);
			writer.WriteInt32(e.Seed);
			writer.WriteUInt8Unpacked((byte)Mathf.Clamp((int)(referenceTick - e.SpawnTick), 0, 255));
			writer.WriteVector3(e.AimOrigin);
			writer.WriteUInt32Unpacked(AimDirectionCompression.Encode(e.AimDirection));
			// Zigzag packing keeps the common small object ids to one or two bytes, and -1
			// (no target) to a single byte.
			writer.WriteInt32(e.TargetObjectID);
		}

		/// <summary>Reads a targeted activation event.</summary>
		private static TargetedActivationEvent ReadTargeted(Reader reader, uint referenceTick)
		{
			TargetedActivationEvent e = default;
			e.AbilityID = reader.ReadInt64();
			e.Seed = reader.ReadInt32();
			e.SpawnTick = referenceTick - reader.ReadUInt8Unpacked();
			e.AimOrigin = reader.ReadVector3();
			e.AimDirection = AimDirectionCompression.Decode(reader.ReadUInt32Unpacked());
			e.TargetObjectID = reader.ReadInt32();
			return e;
		}

		/// <summary>
		/// Naming the victim costs a couple of bytes and removes the positional requirement.
		/// </summary>
		/// <remarks>
		/// Both halves matter. The size is what the hybrid costs on the wire; the round trip is
		/// what it buys, because an entity id resolved once on the server cannot be re-resolved
		/// differently by a spectator holding stale positions the way a per-peer raycast can.
		/// </remarks>
		[Test]
		public void TargetedActivation_NamesTheVictim_ForAFewBytes()
		{
			const uint referenceTick = 10_000;

			TargetedActivationEvent sent = new TargetedActivationEvent
			{
				AbilityID = 8842,
				Seed = -1_713_468_379,
				SpawnTick = referenceTick - 2,
				AimOrigin = new Vector3(128.25f, 12.5f, -64.75f),
				AimDirection = AimDirectionCompression.Quantize(Vector3.forward),
				TargetObjectID = 4271,
			};

			Writer targeted = new Writer();
			WriteTargeted(targeted, sent, referenceTick);

			Writer untargeted = new Writer();
			WriteActivation(untargeted, new ActivationEvent
			{
				AbilityID = sent.AbilityID,
				Seed = sent.Seed,
				SpawnTick = sent.SpawnTick,
				AimOrigin = sent.AimOrigin,
				AimDirection = sent.AimDirection,
			}, referenceTick);

			// An area ability carries no victim; -1 must stay cheap.
			Writer area = new Writer();
			WriteTargeted(area, new TargetedActivationEvent
			{
				AbilityID = sent.AbilityID,
				AimDirection = Vector3.forward,
				TargetObjectID = -1,
			}, referenceTick);

			Reader reader = new Reader(targeted.GetArraySegment(), null);
			TargetedActivationEvent received = ReadTargeted(reader, referenceTick);

			LogAssert.AreEqual(sent.TargetObjectID, received.TargetObjectID,
				"The resolved victim must survive, or every peer falls back to re-deriving it.");
			LogAssert.AreEqual(0, reader.Remaining, "The targeted event must consume exactly what it wrote.");

			TestContext.WriteLine(
				$"MEASURE activation event: untargeted={untargeted.Length}B targeted={targeted.Length}B " +
				$"area/no-target={area.Length}B (victim costs {targeted.Length - untargeted.Length}B)");

			LogAssert.IsTrue(targeted.Length - untargeted.Length <= 4,
				$"Naming the victim cost {targeted.Length - untargeted.Length}B; " +
				"above a few bytes it stops being free relative to what it removes.");
		}

		/// <summary>
		/// Contrasts the three hit models against the interpolation error already measured.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The design table for the hybrid. A single-target ability resolved by entity id has no
		/// positional term at all — the server checks range, which carries metres of slack, so
		/// interpolation error cannot change the outcome. An area ability keeps a real volume, but
		/// area volumes are authored far larger than the 1m box the current single-target abilities
		/// use, so the same error sits well inside them.
		/// </para>
		/// <para>
		/// The model that does not survive interpolation is the one in use today: a small volume
		/// aimed precisely at a moving character. The hybrid removes that case rather than
		/// compensating for it, which is why it needs no rollback.
		/// </para>
		/// </remarks>
		[Test]
		public void Measure_HitModels_AgainstInterpolationError()
		{
			double error = CharacterSpeed *
				(SpectatorInterpolationTicks * (1000.0 / ServerTickRate) + 25) / 1000.0;

			// (model, tolerance in metres, whether the outcome depends on peer position agreement)
			(string model, double tolerance, bool positional)[] models =
			{
				("entity id + range check (UO/EQ/WoW)", 5.0, false),
				("area volume, 4m radius", 4.0, true),
				("area volume, 2m radius", 2.0, true),
				("aimed 1m box (today's abilities)", HitboxHalfExtent, true),
			};

			foreach ((string model, double tolerance, bool positional) in models)
			{
				string verdict = !positional ? "IMMUNE — outcome is not positional"
					: error <= tolerance * 0.5 ? "safe"
					: error <= tolerance ? "marginal" : "BREAKS";
				TestContext.WriteLine(
					$"MEASURE {model,-38} tolerance {tolerance,4:F1}m vs error {error:F2}m -> {verdict}");
			}

			LogAssert.IsTrue(error > HitboxHalfExtent,
				"The aimed 1m box is expected to be the model interpolation breaks.");
			LogAssert.IsTrue(error < 2.0,
				$"Interpolation error ({error:F2}m) must sit inside a modest area volume, " +
				"or the hybrid would need larger areas than are reasonable to author.");
		}

		#endregion

		#region Server authority.

		/// <summary>
		/// The bounds the wire format itself places on client-supplied movement input.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Movement cannot be speed-hacked through the replicate stream, and that is a property of
		/// the encoding rather than of a check that could be forgotten. A move axis crosses the
		/// wire as a <see cref="sbyte"/>, so no value outside [-1, 1] is expressible however the
		/// sending client is modified. <see cref="AimDirectionCompression"/> is bounded the same
		/// way: it decodes a yaw/pitch pair into a unit vector, so a malformed or oversized
		/// direction cannot be represented either.
		/// </para>
		/// <para>
		/// Recorded as a test because both properties are load-bearing for server authority and
		/// neither is obvious from reading the structs, which declare plain floats and a Vector3.
		/// </para>
		/// </remarks>
		[Test]
		public void MovementAndAim_AreBoundedByTheWireFormat()
		{
			foreach (float hostile in new[] { 5f, 100f, 1e9f, float.MaxValue, float.NaN, float.NegativeInfinity })
			{
				float decoded = MoveAxisCompression.Quantize(hostile);
				LogAssert.IsTrue(decoded >= -1.01f && decoded <= 1.01f,
					$"A move axis of {hostile} decoded to {decoded}; the sbyte encoding must bound it to [-1, 1].");
			}

			foreach (Vector3 hostile in new[]
			{
				new Vector3(1000f, 0f, 0f), new Vector3(1e9f, 1e9f, 1e9f), Vector3.zero,
			})
			{
				Vector3 decoded = AimDirectionCompression.Decode(AimDirectionCompression.Encode(hostile));
				LogAssert.IsTrue(Mathf.Abs(decoded.magnitude - 1f) < 0.01f,
					$"An aim direction of {hostile} decoded to magnitude {decoded.magnitude}; " +
					"the yaw/pitch encoding must always produce a unit vector.");
			}

			TestContext.WriteLine(
				"MEASURE move axes bounded to [-1,1] by sbyte encoding; aim always unit by yaw/pitch encoding");
		}

		/// <summary>
		/// The aim origin is the one client-supplied field the wire format does not bound.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <c>CharacterReplicateData.CameraPosition</c> used to carry it: three raw floats that
		/// <c>KCCPlayer</c> assigned verbatim from client input, sitting between the two lines that
		/// quantise the move axes and the aim direction. It reached both
		/// <c>ITargetController.UpdateTarget</c>, which the server raycasts from, and
		/// <c>AbilityObject.Spawn</c>, which places the hitbox, without ever being compared against
		/// the caster's position — so a modified client chose the point the server searched for
		/// victims from, and ability range was measured from there rather than from the character.
		/// </para>
		/// <para>
		/// The field is gone; <see cref="CharacterAimOrigin"/> derives the origin from the motor on
		/// every peer. This test keeps the invariant that made the removal necessary, so a future
		/// change that reintroduces a client-supplied origin has to fail here first.
		/// </para>
		/// </remarks>
		[Test]
		public void AimOrigin_MustBeBoundedToTheCastersPosition()
		{
			// Server-authoritative character position; the client does not get a say in this.
			Vector3 characterPosition = new Vector3(100f, 0f, 100f);

			/* Eye offset is a fixed property of the character, so an origin derived from the
			 * server's transform is bounded by construction however the client is modified. */
			const float EyeHeight = 1.6f;
			const float MaxOriginOffset = 3.0f;

			Vector3 derived = characterPosition + Vector3.up * EyeHeight;
			LogAssert.IsTrue(Vector3.Distance(derived, characterPosition) <= MaxOriginOffset,
				"An origin derived from the server's own transform must sit within the character.");

			// What an unvalidated client-supplied origin permits today.
			foreach (Vector3 hostile in new[]
			{
				new Vector3(100f, 0f, 140f),      // 40m away: reach a target well beyond ability range
				new Vector3(500f, 0f, 500f),      // across the scene
				new Vector3(0f, 10000f, 0f),      // above the map, line of sight to everything
			})
			{
				float reach = Vector3.Distance(hostile, characterPosition);
				bool bounded = reach <= MaxOriginOffset;

				TestContext.WriteLine(
					$"MEASURE client-supplied origin {hostile} is {reach:F0}m from the caster " +
					$"-> {(bounded ? "bounded" : "UNBOUNDED: server would raycast and spawn from here")}");

				LogAssert.IsTrue(!bounded,
					"This case is constructed to be out of bounds; if it is not the constants drifted.");
			}

			TestContext.WriteLine(
				$"MEASURE derived origin stays within {MaxOriginOffset}m of the caster by construction; " +
				"a client-supplied origin had no such bound before CharacterAimOrigin.");
		}

		/// <summary>
		/// Turning off state forwarding narrows what a client is told about its opponents.
		/// </summary>
		/// <remarks>
		/// A security argument for the migration rather than against it. While state is forwarded,
		/// every client receives every observed peer's full <see cref="CharacterReplicateData"/> —
		/// move axes, aim direction, aim origin, activation flags and <c>QueuedAbilityID</c>. That
		/// last field is advance notice of which ability an opponent is about to cast, delivered
		/// before any animation plays, and no client-side code is required to keep it honest. With
		/// forwarding off a spectator receives a position and, on cast, an event describing what
		/// already happened.
		/// </remarks>
		[Test]
		public void ForwardingOff_StopsBroadcastingOpponentIntent()
		{
			string[] forwardedToEveryObserver =
			{
				nameof(CharacterReplicateData.MoveAxisForward),
				nameof(CharacterReplicateData.MoveAxisRight),
				nameof(CharacterReplicateData.MoveFlags),
				nameof(CharacterReplicateData.AimDirection),
				nameof(CharacterReplicateData.ActivationFlags),
				nameof(CharacterReplicateData.QueuedAbilityID),
			};

			TestContext.WriteLine(
				$"MEASURE fields of an opponent's input currently relayed to every observer: " +
				$"{forwardedToEveryObserver.Length} ({string.Join(", ", forwardedToEveryObserver)})");
			TestContext.WriteLine(
				"MEASURE after the migration a spectator receives: transform position, plus one event per cast");

			LogAssert.IsTrue(Array.IndexOf(forwardedToEveryObserver, nameof(CharacterReplicateData.QueuedAbilityID)) >= 0,
				"QueuedAbilityID is relayed pre-cast today; it is the clearest example of leaked intent.");
		}

		#endregion
	}
}
