using UnityEngine;
using System.Collections.Generic;
using System;
using FishNet.Managing.Timing;
using FishNet.Object;
using FishNet.Transporting;
using Scene = UnityEngine.SceneManagement.Scene;
using SceneManager = UnityEngine.SceneManagement.SceneManager;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// World pose an ability object is spawned with.
	/// </summary>
	/// <remarks>
	/// Resolved once by <see cref="AbilityObject.ResolveSpawnPose"/> on the peer that runs the
	/// authoritative simulation (server) or predicts it (owner), and carried verbatim to observers
	/// in <see cref="AbilityActivatedBroadcast"/>. An observer must not re-resolve it: every spawn
	/// target except <see cref="AbilitySpawnTarget.Camera"/> reads the caster's motor, transform or
	/// spawner, and an observer holds those interpolated — several hundred milliseconds behind —
	/// so a locally resolved pose would put the object where the caster used to be, and the
	/// closed-form trajectory would keep it on that offset line for its whole life.
	/// </remarks>
	public readonly struct AbilitySpawnPose
	{
		/// <summary>World position.</summary>
		public readonly Vector3 Position;
		/// <summary>World rotation.</summary>
		public readonly Quaternion Rotation;

		public AbilitySpawnPose(Vector3 position, Quaternion rotation)
		{
			Position = position;
			Rotation = rotation;
		}
	}

	/// <summary>
	/// Represents a spawned ability object in the world, handling its lifetime, collision, and event triggers.
	/// </summary>
	public class AbilityObject : MonoBehaviour
	{

		/// <summary>
		/// Event invoked when a pet ability is summoned.
		/// </summary>
		public static Action<PetAbilityTemplate, IPlayerCharacter> OnPetSummon;

		/// <summary>
		/// The container ID for grouping spawned ability objects.
		/// </summary>
		internal int ContainerID;
		/// <summary>
		/// The unique ID for this ability object within its container.
		/// </summary>
		internal int ID;
		/// <summary>
		/// The ability instance this object represents.
		/// </summary>
		public Ability Ability;
		/// <summary>
		/// The character who cast or owns this ability object.
		/// May be a live <see cref="IPlayerCharacter"/> during normal play, or a
		/// <see cref="SnapshotCharacter"/> phantom after the caster disconnects.
		/// </summary>
		public ICharacter Caster;
		/// <summary>
		/// Cached reference to the object's Rigidbody, if present.
		/// </summary>
		public Rigidbody CachedRigidBody;

		/// <summary>
		/// The collider whose dimensions the swept hit query uses.
		/// </summary>
		/// <remarks>
		/// The instance's own collider where there is one — root first, then children, so an
		/// authored child hitbox works — and the sweep reads the live scale and rotation.
		/// <see cref="AbilityPrefabColliderCache"/> re-resolves the SAME child-inclusive lookup
		/// against the prefab for an instance whose component was added before its collider
		/// existed; the two lookups must stay identical or peers sweep different shapes. Resolved
		/// once at initialisation rather than per tick because it must outlive the ability: an
		/// orphaned object keeps flying after its caster disconnects, and by then there is no
		/// template left to ask.
		/// </remarks>
		private Collider sweepShape;

		/// <summary>
		/// Where this object was the last time it resolved hits — the start of the segment the next
		/// sweep covers.
		/// </summary>
		/// <remarks>
		/// Seeded with the spawn position so the first tick sweeps the distance actually travelled
		/// rather than starting from wherever the closed form had already put the transform.
		/// </remarks>
		private Vector3 lastSweepPosition;

		/// <summary>Ordered hits from the current tick's sweep. Reused so a tick allocates nothing.</summary>
		private List<AbilitySweepHit> sweepHits;

		/// <summary>
		/// Every target this object has already hit, for its whole lifetime.
		/// </summary>
		/// <remarks>
		/// The sweep runs every tick, so without this a pierce sitting inside a character would
		/// resolve a hit against it on each of them until its hit count drained. Membership only —
		/// nothing here is ever a sort key, so it is safe for this to be a per-process reference set
		/// where an ordering decision would not be.
		/// </remarks>
		private HashSet<GameObject> hitTargets;

		/// <summary>
		/// Number of hits this object can perform before being destroyed.
		/// </summary>
		public int HitCount;
		/// <summary>
		/// Remaining lifetime in seconds before the object is destroyed.
		/// </summary>
		public float RemainingLifeTime;
		/// <summary>
		/// The network tick at which this ability object was spawned, expressed as a
		/// <see cref="PredictionTick"/> sourced from the replicate input.
		/// Used by the rollback system to identify predicted objects that need to be destroyed on reconcile mismatch.
		/// </summary>
		public PredictionTick SpawnTick;

		/// <summary>
		/// Deterministic seed used to create this spawn container.
		/// Paired with <see cref="SpawnTick"/> to distinguish a same-spawn retry from a
		/// genuine container-ID hash collision with another active ability.
		/// </summary>
		internal int SpawnSeed;

		/// <summary>
		/// Random number generator for ability effects.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Shared by every action in this object's event chains, and advanced by side effect —
		/// so a draw that happens on one peer and not another desynchronises everything drawn
		/// after it.</b> The generator is threaded onto the OnSpawn, OnTick, OnHit and OnDestroy
		/// payloads, and <c>RandomRangeValue</c> / <c>RandomRangeFloatValue</c> consume it through
		/// <c>EventData.RNG</c>. Its state is carried in the reconcile, which keeps the server and
		/// the caster's owner aligned; an observer has no reconcile and stays aligned only by
		/// making the same draws in the same order.
		/// </para>
		/// <para>
		/// <b>The rule that follows: a value provider is evaluated BEFORE any peer gate, never
		/// after.</b> An action that draws behind <c>EcaAuthority.MayPredict</c>, an
		/// <c>IsServer</c> test or <see cref="ResolvesHitsLocally"/> advances this generator only
		/// on the peers that pass — and an ungated action later in the same chain
		/// (<c>AbilityForkHitAction</c>) then reads a different number. That put an observer's copy
		/// of a forking projectile on a heading the server never took, permanently, from its first
		/// hit. Evaluate the provider, then gate on what to DO with it. This is the same rule
		/// <c>ResolveTargetAndSpawn</c> already follows when it advances the seed during a replay
		/// whose spawn it deliberately skips.
		/// </para>
		/// </remarks>
		public DeterministicRNG RNG;

		/// <summary>World position this object was spawned at.</summary>
		/// <remarks>
		/// Recorded so trajectories can be evaluated as a closed form —
		/// <c>SpawnPosition + SpawnRotation * direction * speed * elapsed</c> — instead of
		/// accumulating <c>position +=</c> per tick. Accumulation is exactly reproducible only while
		/// every peer takes the identical number of steps; the closed form is reproducible from the
		/// spawn tuple alone, which is what an observer that reconstructs the object from a
		/// broadcast actually holds. See <see cref="AbilityMoveTransformAction"/>.
		/// </remarks>
		public Vector3 SpawnPosition;

		/// <summary>World rotation this object was spawned with. See <see cref="SpawnPosition"/>.</summary>
		public Quaternion SpawnRotation = Quaternion.identity;

		/// <summary>
		/// Ticks this object has simulated since spawn. Advanced in <see cref="OnTick"/> before tick
		/// events dispatch, so the first tick's events see a value of 1.
		/// </summary>
		public uint ElapsedTicks;

		/// <summary>
		/// Ticks this object simulated on legs BEFORE the current one. Zero until a
		/// <see cref="Redirect"/> resets <see cref="ElapsedTicks"/> for a new leg.
		/// </summary>
		/// <remarks>
		/// Lifetime and trajectory age at different clocks once an object has been redirected:
		/// the closed-form pose reads the CURRENT leg's <see cref="ElapsedTicks"/>, but
		/// <see cref="RemainingLifeTime"/> has been running since the original spawn. The spawn
		/// payload's in-flight block anchors its fast-forward to the current leg (that is what
		/// reproduces the pose), so without this counter a late joiner rebuilt a redirected
		/// object with its FULL lifetime less only the current leg — the copy outlived the
		/// server's by however long the object flew before the turn, then played its authored
		/// OnDestroy effects at a place and time where nothing happened. The payload sends this
		/// so the receiver can charge the earlier legs against lifetime too; see
		/// <see cref="ConsumeLifetime"/>.
		/// </remarks>
		public uint PreRedirectElapsedTicks;

		/// <summary>The caster's input tick this object's step zero belongs to: its spawn tick, or for a fork the source's step.</summary>
		private uint viewStepBase;

		/// <summary>
		/// The caster's input tick this step pairs with: the owner sweeps step k on its tick spawn + k, against what it drew on
		/// that tick, so the server rewinds step k to the view the caster claimed with THAT input (LagCompensationTick), never to
		/// whichever input it happened to process last. They differ whenever the replicate queue ran dry or doubled up for a
		/// tick, which input arriving two ticks at a time makes common, and a frame hitch that runs two ticks in one frame
		/// draws both at the same pose.
		/// </summary>
		public uint StepInputTick => unchecked(viewStepBase + PreRedirectElapsedTicks + ElapsedTicks);

		#region Hit claims
		/// <summary>One step's sweep, as the server ran it: the segment, and the caster's input tick the step paired with.</summary>
		private struct RecordedSweep
		{
			public uint StepInputTick;
			/// <summary>The server tick the step was swept on: where an estimated (not exact) view claim is counted back from.</summary>
			public uint ServerTick;
			public Vector3 From;
			public Vector3 To;
			public bool Valid;
		}

		/// <summary>How many of its latest sweeps a server object keeps for checking claims against.</summary>
		private const int RecordedSweepCount = 12;

		/// <summary>
		/// How many steps before the claiming input a claimed hit may have happened on. One is the rule (the owner hits on
		/// a tick and says so with the next input); the rest covers a claim that queued behind another and a server whose
		/// input queue ran dry for a tick, which leaves this object a step ahead of the inputs.
		/// </summary>
		internal const uint ClaimStepWindow = 8;

		/// <summary>The server's latest sweeps, newest overwriting oldest; null until the first, and on every client.</summary>
		private RecordedSweep[] recordedSweeps;
		private int recordedSweepNext;

		private void RecordSweep(Vector3 from, Vector3 to)
		{
			recordedSweeps ??= new RecordedSweep[RecordedSweepCount];
			recordedSweeps[recordedSweepNext] = new RecordedSweep
			{
				StepInputTick = StepInputTick,
				ServerTick = timeManager != null ? timeManager.LocalTick : 0u,
				From = from,
				To = to,
				Valid = true,
			};
			recordedSweepNext = (recordedSweepNext + 1) % RecordedSweepCount;
		}

		/// <summary>
		/// Server: the step just recorded ended at <paramref name="point"/> (the hit that stopped or turned the object), so
		/// the record ends there too. The whole step is recorded before it is swept; left whole, the part beyond the wall
		/// that ended the object was still a path a claim could be measured against, and reached a victim behind the wall.
		/// </summary>
		private void TruncateLastSweep(Vector3 point)
		{
			if (recordedSweeps == null)
			{
				return;
			}
			int last = (recordedSweepNext + RecordedSweepCount - 1) % RecordedSweepCount;
			if (!recordedSweeps[last].Valid)
			{
				return;
			}
			recordedSweeps[last].To = ClosestPointOnSegment(recordedSweeps[last].From, recordedSweeps[last].To, point);
		}

		private static Vector3 ClosestPointOnSegment(Vector3 from, Vector3 to, Vector3 point)
		{
			Vector3 along = to - from;
			float lengthSqr = along.sqrMagnitude;
			if (lengthSqr < 1e-10f)
			{
				return from;
			}
			return from + along * Mathf.Clamp01(Vector3.Dot(point - from, along) / lengthSqr);
		}

		/// <summary>
		/// True when something that is not a character stands between <paramref name="from"/> and <paramref name="to"/> on
		/// the layers this object collides with: scenery. The cover test of the spawn pose, for any two points.
		/// </summary>
		private static bool SceneryBetween(PhysicsScene physicsScene, Vector3 from, Vector3 to, int mask)
		{
			Vector3 span = to - from;
			float distance = span.magnitude;
			if (distance < 1e-3f || mask == 0)
			{
				return false;
			}
			Vector3 direction = span / distance;
			int count;
			while (true)
			{
				count = physicsScene.Raycast(from, direction, coverHits, distance, mask, QueryTriggerInteraction.Ignore);
				if (!TargetOrdering.TryGrowQueryBuffer(ref coverHits, count))
				{
					break;
				}
			}
			for (int i = 0; i < count; ++i)
			{
				Collider struck = coverHits[i].collider;
				/* Not a moving body (a ship's hull, a lift, a platform): it is read where it stands NOW, against a victim pose
				 * up to half a second old, so on a moving deck the deck itself came between them and refused an honest claim. */
				if (struck == null || struck.attachedRigidbody != null)
				{
					continue;
				}
				TargetOrdering.ResolveHitRoot(struck, out ICharacter hitCharacter);
				if (hitCharacter == null)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>The caster's ability controller, for queueing a claim; resolved on first use.</summary>
		private AbilityController casterController;

		/// <summary>Owner: queues a claim for a character this copy just hit (AbilityController.QueueHitClaim).</summary>
		private void NoteOwnerHit(ICharacter hitCharacter)
		{
			NetworkObject victimNob = hitCharacter.NetworkObject;
			NetworkObject casterNob = Caster?.NetworkObject;
			if (victimNob == null || casterNob == null || !victimNob.IsSpawned || ID < 0 || ID > byte.MaxValue)
			{
				return;
			}
			if (casterController == null)
			{
				casterController = casterNob.GetComponent<AbilityController>();
			}
			casterController?.QueueHitClaim(SpawnTick, (byte)ID, victimNob.ObjectId, Ability != null ? Ability.ID : 0);
		}

		/// <summary>
		/// Server: applies a hit the owner claims, if this object could have made it.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Accepted when the victim is not one this object already hit (the server agreed on its own: nothing to do) and
		/// one of this object's recent sweeps passed within <see cref="HitClaimGeometry.Tolerance"/> of where the position
		/// history puts the victim for the view the caster claimed on that step. The history, not the live transform: the
		/// claim is about what the owner saw then.
		/// </para>
		/// <para>
		/// An accepted claim goes through <see cref="ApplyHit"/> like a swept hit, so deflection, blocking, the observers'
		/// message, the OnHit chain and the hit count all behave as they do for one. Nothing here trusts the claim for
		/// more than the choice of victim among those the sweep nearly touched.
		/// </para>
		/// </remarks>
		/// <param name="victim">The character claimed.</param>
		/// <param name="claimInputTick">The tick of the input that carried the claim.</param>
		/// <returns>True when the hit was applied.</returns>
		internal bool TryApplyClaimedHit(ICharacter victim, uint claimInputTick)
		{
			if (!isServer || destroyed || Caster == null || ReferenceEquals(victim, Caster))
			{
				return false;
			}
			if (!TryValidateClaim(victim, claimInputTick, Caster, timeManager, out Vector3 point, out Vector3 normal, out Vector3 localPoint))
			{
				return false;
			}
			ApplyHit(victim, victim.GameObject, point, normal, localPoint);
			return true;
		}

		/// <summary>The ability, caster and clock of an object that ended within the claim grace; null otherwise.</summary>
		private Ability endedAbility;
		private ICharacter endedCaster;
		private TimeManager endedTimeManager;
		/// <summary>True when the hit that ended this object was on a character: it had no hit left for a claim to grant.</summary>
		private bool endedOnCharacter;
		/// <summary>The server tick this object ended on, for an object kept answerable to a claim.</summary>
		internal uint EndedOnTick { get; private set; }

		/// <summary>True for an object that has ended but can still answer a hit claim (DestroyAbilityObjectInternal).</summary>
		internal bool AnswersClaimsAfterEnding => destroyed && endedAbility != null && endedCaster != null;

		/// <summary>
		/// Server: applies a hit the owner claims against an object that has already ENDED, within its grace.
		/// </summary>
		/// <remarks>
		/// The same check as for a live object. What is granted is the OnHit chain on the victim (the damage the owner
		/// saw); the object is not brought back, and nothing is told to observers about it, who watched it end where the
		/// server ended it. Once only: the victim joins the hit set, and the object answers no second claim.
		/// </remarks>
		internal bool TryApplyEndedClaim(ICharacter victim, uint claimInputTick)
		{
			if (!isServer || !AnswersClaimsAfterEnding || ReferenceEquals(victim, endedCaster))
			{
				return false;
			}
			if (!TryValidateClaim(victim, claimInputTick, endedCaster, endedTimeManager, out Vector3 point, out Vector3 normal, out Vector3 localPoint))
			{
				return false;
			}

			// One answer per body, whatever it is: a second claim must not be offered to the same parry or shield again.
			hitTargets ??= new HashSet<GameObject>();
			hitTargets.Add(victim.GameObject);

			/* The victim's own defences, exactly as a swept hit meets them (ApplyHit): a parry first, then a raised shield.
			 * This path ran the hit effects directly, so a hit claimed against a projectile that had already ended went
			 * straight through both. The object is gone, so there is nothing to turn or to stop: the defence is spent (a
			 * hit did reach it) and the claim is simply not granted. */
			Transform headingTransform = Transform != null ? Transform : transform;
			if (DamageMitigation.TryDeflect(victim, headingTransform.forward, normal, mutate: true, out _) ||
				DamageMitigation.TryBlockAtVolume(victim, localPoint, mutate: true))
			{
				// Answered: a defence met it, and an ended object answers one claim only.
				endedAbility = null;
				endedCaster = null;
				endedTimeManager = null;
				return false;
			}

			// The chain reads the ability, the caster and the clock off the object: lent back for the call, then let go for good.
			Ability ability = endedAbility;
			ICharacter caster = endedCaster;
			TimeManager clock = endedTimeManager;
			endedAbility = null;
			endedCaster = null;
			endedTimeManager = null;
			Ability = ability;
			Caster = caster;
			timeManager = clock;
			try
			{
				RunHitEvents(victim, point, normal, isAuthoritativeEcho: false, singleTarget: true);
			}
			finally
			{
				Ability = null;
				Caster = null;
				timeManager = null;
				Snapshot = null;
			}
			return true;
		}

		/// <summary>
		/// True when one of this object's recent sweeps passed close enough to where the history puts
		/// <paramref name="victim"/> for the view <paramref name="caster"/> claimed on that step.
		/// </summary>
		private bool TryValidateClaim(ICharacter victim, uint claimInputTick, ICharacter caster, TimeManager clock,
			out Vector3 point, out Vector3 normal, out Vector3 localPoint)
		{
			point = default;
			normal = default;
			localPoint = default;
			if (victim == null || recordedSweeps == null || caster == null || victim.GameObject == null)
			{
				return false;
			}
			if (hitTargets != null && hitTargets.Contains(victim.GameObject))
			{
				return false;
			}
			if (victim.GameObject.scene != (GameObject != null ? GameObject : gameObject).scene)
			{
				return false;
			}

			CharacterPositionHistory history = victim.GameObject.GetComponent<CharacterPositionHistory>();
			ResolveVictimCapsule(victim, out float victimRadius, out float victimHeight);
			Vector3 halfExtents = AbilityPrefabColliderCache.ResolveShapeHalfExtents(sweepShape);
			float shapeReach = Mathf.Max(halfExtents.x, Mathf.Max(halfExtents.y, halfExtents.z));
			Transform victimTransform = victim.Transform;

			for (int i = 0; i < RecordedSweepCount; ++i)
			{
				RecordedSweep sweep = recordedSweeps[i];
				// The step the owner hit on is before the input that says so, and not long before.
				/* Either side of the claim, within the window. The owner claims with the input AFTER the step, but the server's
				 * object steps on its own ticks, numbered from the input it last ran: after ticks without input those numbers
				 * are estimates and can run ahead of the late inputs that follow, so the step a claim names can carry a number
				 * at or past the claim's. The geometry below is what decides. */
				int age = unchecked((int)(claimInputTick - sweep.StepInputTick));
				if (!sweep.Valid || age < -(int)ClaimStepWindow || age > (int)ClaimStepWindow)
				{
					continue;
				}

				Vector3 victimFeet = victimTransform.position;
				Quaternion victimRotation = victimTransform.rotation;
				float victimSpeed = 0f;
				if (history != null && clock != null &&
					LagCompensationTick.TryResolve(caster, clock, sweep.StepInputTick, out RewindTarget target, sweep.ServerTick) &&
					history.TryResolve(target, out CharacterPositionHistory.Snapshot then))
				{
					victimFeet = then.Position;
					victimRotation = then.Rotation;
					if (target.Tick > 0u &&
						history.TryResolve(new RewindTarget(target.Tick - 1u, target.SubTickFraction), out CharacterPositionHistory.Snapshot before))
					{
						/* A claim older than the history clamps both looks to the oldest sample, which read as standing still and
						 * held an honest high-latency claim to the 0.1 m of a still target. The speed is then taken going forward. */
						if (before.Tick == then.Tick && history.TryResolve(then.Tick + 1u, out CharacterPositionHistory.Snapshot after) && after.Tick != then.Tick)
						{
							before = then;
							then = after;
						}
						victimSpeed = (then.Position - before.Position).magnitude / Mathf.Max(tickDelta, 1e-5f);
					}
				}

				float gap = HitClaimGeometry.Gap(sweep.From, sweep.To, shapeReach, victimFeet, victimTransform.up,
					victimRadius, victimHeight, out point, out normal);
				if (gap > HitClaimGeometry.Tolerance(victimSpeed, tickDelta))
				{
					continue;
				}

				/* And nothing solid between the path and the body. The tolerance is a distance, and a distance alone
				 * reaches round a corner: a victim on the far side of a wall the projectile flew along is as near the
				 * path as one standing in the open. No honest client claims that hit (its own copy meets the same wall);
				 * this is the check that makes a forged one worthless. Scenery does not move, so it is read as it stands. */
				GameObject self = GameObject != null ? GameObject : gameObject;
				if (SceneryBetween(self.scene.GetPhysicsScene(), ClosestPointOnSegment(sweep.From, sweep.To, point), point,
						AbilityObjectSweep.CollisionMaskForLayer(self.layer)))
				{
					continue;
				}

				// The point in the victim's own frame as it stood then, which is what a block volume is tested in.
				/* Where the PROJECTILE was (its path's nearest point to the body), not the point on the body: a shield is a volume
				 * in front of the body, and a point on the capsule's surface can never be inside it. */
				Vector3 pathPoint = ClosestPointOnSegment(sweep.From, sweep.To, point);
				localPoint = Quaternion.Inverse(victimRotation) * (pathPoint - victimFeet);
				return true;
			}
			return false;
		}

		/// <summary>True when this object has already hit <paramref name="character"/> (it hits each body once).</summary>
		internal bool HasHit(ICharacter character)
		{
			return character != null && character.GameObject != null && hitTargets != null && hitTargets.Contains(character.GameObject);
		}

		/// <summary>The victim's capsule, from its collider where it has one; a standing person's otherwise.</summary>
		private static void ResolveVictimCapsule(ICharacter victim, out float radius, out float height)
		{
			radius = 0.4f;
			height = 1.8f;
			if (victim.Collider is CapsuleCollider capsule)
			{
				Vector3 scale = capsule.transform.lossyScale;
				radius = capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
				height = Mathf.Max(capsule.height * Mathf.Abs(scale.y), radius * 2f);
			}
			else if (victim.Collider != null)
			{
				Vector3 extents = victim.Collider.bounds.extents;
				radius = Mathf.Max(extents.x, extents.z);
				height = Mathf.Max(extents.y * 2f, radius * 2f);
			}
		}
		#endregion

		#region Presentation
		/* What a client DRAWS of an ability object, apart from what it simulates.
		 *
		 * The simulation moves the object once a tick, to a position that is a closed form of its spawn pose and a whole
		 * number of ticks (AbilityMoveTransformAction). That is what makes it reproducible, and drawn as it stands it is a
		 * projectile that jumps forty centimetres thirty times a second. Three things are wrong with drawing the
		 * simulation directly, and they share one fix: draw the object's VISUALS at a position of their own.
		 *
		 * 1. Between ticks. The same closed form is evaluated at a FRACTIONAL tick count every frame, so the visuals glide.
		 *    Unlike a character's body this needs no lag: the path is a formula, so "now" can be drawn exactly.
		 * 2. The owner's hand. The object spawns at the hand as the simulation has it (SimulatedSpawnerPose), which is
		 *    ahead of the hand the owner sees by the body's smoothing. The visuals start at the DRAWN hand and blend onto
		 *    the real path over HandBlendSeconds.
		 * 3. An observer's clock. A client draws other characters in the past, on one shared playback clock
		 *    (NetworkTransform.PlaybackDrawTick). An observed projectile drawn at its own present was ahead of everything
		 *    around it by that delay: it left a caster who had not yet finished the cast, and struck a spot its victim had
		 *    not yet reached. An observed copy now runs ON the playback clock: its age is the clock less the server tick
		 *    it spawned on, and a hit, a redirect or an end the server reports is applied when the clock reaches the tick
		 *    the server wrote it on (the tick every message is read against). Nothing extra is sent.
		 *
		 * The visuals are the object's children that carry no collider, moved under one holder; the collider stays on the
		 * root, at the simulated position, and nothing here is read by a sweep, a hit or the server. */

		/// <summary>
		/// False to run observed copies on this client's own ticks from the moment their message arrives, as they did
		/// before the playback clock: drawn ahead of the characters around them by the playback delay.
		/// </summary>
		public static bool ObservedPlayback = true;

		/// <summary>How long the owner's visuals take to leave the drawn hand for the real path, seconds.</summary>
		public const float HandBlendSeconds = 0.2f;

		/// <summary>The most steps an observed copy takes in one tick to reach the playback clock (a late join mid-flight).</summary>
		private const int MaximumPlaybackStepsPerTick = 64;

		/// <summary>
		/// Further behind the playback clock than this, an observed copy is not catching up a hiccup, it was first seen in
		/// mid-flight: it jumps (<see cref="FastForward"/>) instead of stepping, and plays no spawn effect.
		/// </summary>
		private const int LateJoinStepTicks = 6;

		/// <summary>The holder the visual children hang under; null on the server and for an object with none.</summary>
		private Transform presented;
		/// <summary>Renderers on the root itself (a melee swing draws its own box): hidden with the holder until an observed copy shows.</summary>
		private Renderer[] rootRenderers;
		/// <summary>True when the OnTick chain moves this object by the closed form, in <see cref="presentationMoveDirection"/>.</summary>
		private bool presentationMoves;
		private Vector3 presentationMoveDirection;
		/// <summary>Where the owner saw the hand, less where the simulation had it; blended away over <see cref="HandBlendSeconds"/>.</summary>
		private Vector3 presentationOffset;
		/// <summary>True while the blend from the drawn hand is still running.</summary>
		private bool presentationOffsetActive;
		/// <summary>The caster's spawner, for a copy on the playback clock to measure the offset from when it first shows; null for none.</summary>
		private Transform presentationSpawner;
		/// <summary>True once a copy on the playback clock has been shown (and has taken its offset from the drawn spawner).</summary>
		private bool presentationShown;

		/// <summary>True for an observed copy that began in mid-flight (BeginObservedPlayback).</summary>
		private bool observedLateJoin;

		/// <summary>An effect a spawn chain asked for, kept until the visuals it belongs to are drawn (<see cref="ReleaseSpawnEffects"/>).</summary>
		private struct HeldSpawnEffect
		{
			public GameObject Prefab;
			public Scene Scene;
			/// <summary>The object the effect belongs to; null when the event gave <see cref="Position"/> itself (a target, a hit point).</summary>
			public AbilityObject Anchor;
			public Vector3 Position;
		}

		/// <summary>The effects this root's spawn chain asked for and has not played yet; null for none.</summary>
		private List<HeldSpawnEffect> heldSpawnEffects;
		/// <summary>The root whose spawn chain is being dispatched on this client right now; null outside one.</summary>
		private static AbilityObject spawnEffectHolder;

		/// <summary>True for a copy that runs, or is about to be put, on the playback clock (<see cref="BeginObservedPlayback"/>).</summary>
		private bool RunsOnPlaybackClock =>
			ObservedPlayback && !isServer && !destroyed && timeManager != null && !ResolvesHitsLocally && PlaybackTick(timeManager) > 0d;

		/// <summary>
		/// Client: keeps an effect a spawn chain plays until the object it belongs to is drawn. False outside a spawn
		/// chain; the caller then plays it at once.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Why it waits.</b> A spawn chain runs the moment the object is created, which is before anything about how it
		/// is DRAWN is known. For the owner that is before the visuals are put at the spawner as this client draws it
		/// (the hand, wand or staff): played then, the effect sat at the start of the launch line, up by the eye, while
		/// the projectile left the hand. For an observer it is also before the playback clock reaches the cast: the
		/// effect played when the message arrived, a few ticks ahead of the caster's animation and of the projectile it
		/// belongs to.
		/// </para>
		/// <para>
		/// Held on the ROOT, whichever member the effect belongs to: the members of one cast are shown together.
		/// </para>
		/// </remarks>
		internal static bool TryHoldSpawnEffect(GameObject prefab, Scene scene, AbilityObject anchor, Vector3 position)
		{
			AbilityObject holder = spawnEffectHolder;
			if (holder == null || prefab == null || holder.isServer)
			{
				return false;
			}
			holder.heldSpawnEffects ??= new List<HeldSpawnEffect>(2);
			holder.heldSpawnEffects.Add(new HeldSpawnEffect { Prefab = prefab, Scene = scene, Anchor = anchor, Position = position });
			return true;
		}

		/// <summary>
		/// Where an effect that belongs to this object is drawn as it spawns: the caster's spawner as this client draws
		/// it, for an object drawn leaving one; the object itself otherwise (and for a copy first seen far into its flight).
		/// </summary>
		private Vector3 SpawnEffectAnchor()
		{
			Vector3 position = Transform != null ? Transform.position : transform.position;
			if (presentationSpawner != null)
			{
				Vector3 spawner = presentationSpawner.position;
				if ((spawner - position).sqrMagnitude < MaximumHandOffset * MaximumHandOffset)
				{
					return spawner;
				}
			}
			return position;
		}

		/// <summary>Plays the effects the spawn chain asked for, where the objects they belong to are drawn now.</summary>
		private void ReleaseSpawnEffects()
		{
			if (heldSpawnEffects == null || heldSpawnEffects.Count == 0)
			{
				return;
			}
			for (int i = 0; i < heldSpawnEffects.Count; ++i)
			{
				HeldSpawnEffect held = heldSpawnEffects[i];
				if (held.Anchor == null)
				{
					PlayFXAction.SpawnFX(held.Prefab, held.Position, held.Scene);
				}
				else if (!held.Anchor.destroyed)
				{
					PlayFXAction.SpawnFX(held.Prefab, held.Anchor.SpawnEffectAnchor(), held.Scene);
				}
			}
			heldSpawnEffects.Clear();
		}

		/// <summary>
		/// The end of a spawn: plays what the spawn chain asked for, unless this copy is about to go on the playback
		/// clock, which plays it when the copy first shows.
		/// </summary>
		private void ReleaseSpawnEffectsUnlessObserved()
		{
			if (heldSpawnEffects != null && heldSpawnEffects.Count > 0 && !RunsOnPlaybackClock)
			{
				ReleaseSpawnEffects();
			}
		}

		/// <summary>True for an observed copy that runs on the playback clock.</summary>
		private bool onPlaybackClock;
		/// <summary>The server tick the current leg began on (the spawn, or the latest redirect).</summary>
		private uint playbackLegStartTick;
		/// <summary>Ticks into the current leg the visuals are drawn at; negative before the clock reaches the leg's start. Never goes back.</summary>
		private double presentedElapsed = -1d;
		/// <summary>What the server has reported of this object that the playback clock has not reached yet, oldest first.</summary>
		private Queue<ObservedEvent> observedEvents;

		private enum ObservedEventKind : byte { Hit, ActionHit, Block, Deflection, Redirect, End }

		private struct ObservedEvent
		{
			public uint Tick;
			public ObservedEventKind Kind;
			public ICharacter Character;
			public Vector3 Point;
			public Vector3 Vector;
		}

		/// <summary>
		/// Test hook: when set, the playback clock observed copies run on. Null in every build that is not a test
		/// (the same arrangement as LagCompensationTick.ClaimOverride).
		/// </summary>
		internal static System.Func<double> PlaybackTickOverride;

		/// <summary>
		/// The server tick observed characters are drawn at this frame (NetworkTransform.PlaybackDrawTick): the clock an
		/// observed copy runs on. Zero when there is none (nothing received yet, or timed playback is off).
		/// </summary>
		private static double PlaybackTick(TimeManager clock)
		{
			if (PlaybackTickOverride != null)
			{
				return PlaybackTickOverride();
			}
			if (!FishNet.Component.Transforming.NetworkTransform.TimedPlayback)
			{
				return 0d;
			}
			return FishNet.Component.Transforming.NetworkTransform.PlaybackDrawTick(clock, LagCompensationTick.SpectatorInterpolationTicks);
		}

		/// <summary>The position the closed form gives a moving object <paramref name="elapsedTicks"/> (whole or not) into its leg.</summary>
		public static Vector3 PresentedPosition(Vector3 legStart, Quaternion legRotation, Vector3 moveDirection, float speed,
			double elapsedTicks, float tickDelta)
		{
			return legStart + legRotation * moveDirection * (speed * (float)(elapsedTicks * tickDelta));
		}

		/// <summary>How much of the hand's offset is still applied <paramref name="age"/> seconds into the blend: 1 at the start, 0 from <see cref="HandBlendSeconds"/>.</summary>
		public static float HandBlendWeight(float age)
		{
			if (age <= 0f)
			{
				return 1f;
			}
			float u = Mathf.Clamp01(age / HandBlendSeconds);
			return 1f - u * u * (3f - 2f * u);
		}

		/// <summary>
		/// How far into its leg an observed copy is drawn: the playback clock less the leg's start, never less than it was
		/// (the clock steps back when the connection turns late, and a projectile must wait, not reverse).
		/// </summary>
		public static double ObservedElapsed(double drawTick, uint legStartTick, double previous)
		{
			double elapsed = drawTick - legStartTick;
			return elapsed > previous ? elapsed : previous;
		}

		/// <summary>Client: finds how this object moves and gathers its visuals under a holder. Once per object.</summary>
		private void InitializePresentation()
		{
			if (isServer || presented != null)
			{
				return;
			}

			presentationMoves = false;
			IReadOnlyDictionary<int, AbilityOnTickEvent> tickEvents = ActiveOnTickEvents;
			if (tickEvents != null)
			{
				foreach (AbilityOnTickEvent tickEvent in tickEvents.Values)
				{
					if (tickEvent == null || tickEvent.OnConditionsMetActions == null)
					{
						continue;
					}
					for (int i = 0; i < tickEvent.OnConditionsMetActions.Count; ++i)
					{
						if (tickEvent.OnConditionsMetActions[i] is AbilityMoveTransformAction move)
						{
							presentationMoves = true;
							presentationMoveDirection = move.MoveDirection;
						}
					}
				}
			}

			Transform root = Transform != null ? Transform : transform;
			rootRenderers = root.GetComponents<Renderer>();
			Transform holder = null;
			for (int i = root.childCount - 1; i >= 0; --i)
			{
				Transform child = root.GetChild(i);
				if (child.GetComponentInChildren<Collider>(true) != null)
				{
					continue;
				}
				if (holder == null)
				{
					holder = new GameObject("Presented").transform;
					holder.SetParent(root, false);
				}
				// The holder rests at identity on the root, so a child keeps the local pose it was authored with.
				child.SetParent(holder, false);
			}
			presented = holder;
		}

		/// <summary>Owner: the visuals start <paramref name="offset"/> from the simulated position (at the drawn hand) and blend back.</summary>
		internal void SetPresentationOffset(Vector3 offset)
		{
			if (isServer || offset.sqrMagnitude < 1e-8f)
			{
				return;
			}
			presentationOffset = offset;
			presentationOffsetActive = true;
		}

		/// <summary>
		/// Client, for a copy this peer does not resolve hits for: puts it on the playback clock, with its current leg
		/// starting on <paramref name="serverStartTick"/>.
		/// </summary>
		/// <returns>False when there is no playback clock to run on (nothing received yet, timed playback off): the caller catches the copy up the old way.</returns>
		internal bool BeginObservedPlayback(uint serverStartTick, bool lateJoin = false)
		{
			if (!RunsOnPlaybackClock)
			{
				return false;
			}
			double drawTick = PlaybackTick(timeManager);

			onPlaybackClock = true;
			playbackLegStartTick = serverStartTick;
			presentedElapsed = ObservedElapsed(drawTick, serverStartTick, -1d);
			/* Joined in mid-flight: either told so (an object already in flight when this client began watching its caster)
			 * or already well past its start when it began. Decided HERE, once, and not from how far behind the clock the
			 * copy is later: a frame hitch also leaves every copy behind, and treated as a late join it skipped their tick
			 * events and their spawn effects, and a copy whose life ended inside the jump vanished without its impact. */
			observedLateJoin = lateJoin || presentedElapsed > LateJoinStepTicks;
			ShowPresented(presentedElapsed >= 0d);
			return true;
		}

		private void ShowPresented(bool visible)
		{
			if (presented != null && presented.gameObject.activeSelf != visible)
			{
				presented.gameObject.SetActive(visible);
			}
			if (rootRenderers != null)
			{
				for (int i = 0; i < rootRenderers.Length; ++i)
				{
					if (rootRenderers[i] != null && rootRenderers[i].enabled != visible)
					{
						rootRenderers[i].enabled = visible;
					}
				}
			}
		}

		/// <summary>An observed copy's tick: applies what the clock has reached, then steps up to the clock.</summary>
		private void StepOnPlaybackClock()
		{
			double drawTick = PlaybackTick(timeManager);
			if (drawTick <= 0d)
			{
				/* The clock is gone (timed playback switched off under it). A copy that kept waiting for it never moved
				 * and never ran out its life: it carries on by this client's own ticks, shown, with whatever the server
				 * has already reported applied. */
				// Drained while still on the clock, so a redirect among them rebases the leg as it would have.
				DrainObservedEvents(double.MaxValue);
				onPlaybackClock = false;
				if (destroyed)
				{
					return;
				}
				ShowPresented(true);
				ReleaseSpawnEffects();
				StepOnce();
				return;
			}
			DrainObservedEvents(drawTick);
			if (destroyed)
			{
				return;
			}
			StepTo(drawTick - playbackLegStartTick);
		}

		/// <summary>Steps until the leg is <paramref name="legTicks"/> old (whole ticks), a bounded number at a time.</summary>
		private void StepTo(double legTicks)
		{
			long target = (long)System.Math.Floor(legTicks);
			/* Far behind the clock (a copy first seen well into its flight): the distance is JUMPED, by FastForward, which
			 * charges the lifetime and moves the count without running the object's tick events. Stepping it ran every
			 * one of those ticks' events in a single tick (a pulse a second, sixty times at once), which is what
			 * FastForward exists to prevent. The last step is still a real one: it is what places the object. */
			long behind = target - ElapsedTicks;
			if (!destroyed && observedLateJoin && !presentationShown && behind > LateJoinStepTicks)
			{
				FastForward((uint)(behind - 1));
			}
			int steps = 0;
			while (!destroyed && ElapsedTicks < target && steps++ < MaximumPlaybackStepsPerTick)
			{
				StepOnce();
			}
		}

		/// <summary>
		/// True when a report from the server has to wait for the playback clock: this copy runs on it, and the clock has
		/// not reached <paramref name="eventTick"/> (or something earlier is still waiting, and order is kept).
		/// </summary>
		private bool DefersObserved(uint eventTick)
		{
			if (!onPlaybackClock || drainingObservedEvents || destroyed || eventTick == 0u || timeManager == null)
			{
				return false;
			}
			if (observedEvents != null && observedEvents.Count > 0)
			{
				return true;
			}
			double drawTick = PlaybackTick(timeManager);
			return drawTick > 0d && eventTick > drawTick;
		}

		private void DeferObserved(uint eventTick, ObservedEventKind kind, ICharacter character, Vector3 point, Vector3 vector)
		{
			observedEvents ??= new Queue<ObservedEvent>(2);
			observedEvents.Enqueue(new ObservedEvent { Tick = eventTick, Kind = kind, Character = character, Point = point, Vector = vector });
		}

		/// <summary>Applies every waiting report the playback clock has reached, each after stepping up to its tick.</summary>
		private void DrainObservedEvents(double drawTick)
		{
			while (!destroyed && observedEvents != null && observedEvents.Count > 0 && observedEvents.Peek().Tick <= drawTick)
			{
				ObservedEvent observed = observedEvents.Dequeue();
				// The object as it stood on the tick the server reported: a hit lands where the object then was.
				StepTo((double)observed.Tick - playbackLegStartTick);
				if (destroyed)
				{
					return;
				}
				// Applied through the same entry points a message uses; the flag stops them queueing it again.
				drainingObservedEvents = true;
				try
				{
					switch (observed.Kind)
					{
						case ObservedEventKind.Hit:
							ApplyObservedHit(observed.Character, observed.Point, observed.Vector, observed.Tick);
							break;
						case ObservedEventKind.ActionHit:
							ApplyObservedActionHit(observed.Character, observed.Point, observed.Vector, observed.Tick);
							break;
						case ObservedEventKind.Block:
							ApplyObservedBlock(observed.Character, observed.Tick);
							break;
						case ObservedEventKind.Deflection:
							ApplyObservedDeflection(observed.Vector, observed.Tick);
							break;
						case ObservedEventKind.Redirect:
							ApplyObservedRedirect(observed.Vector, 0u, observed.Tick);
							break;
						case ObservedEventKind.End:
							DestroyAbilityObjectInternal();
							break;
					}
				}
				finally
				{
					drainingObservedEvents = false;
				}
			}
		}

		/// <summary>True while <see cref="DrainObservedEvents"/> is applying a report, so applying it does not queue it again.</summary>
		private bool drainingObservedEvents;

		/// <summary>
		/// The object just turned, on <paramref name="legStartTick"/> (a redirect or a deflection re-anchored it and zeroed
		/// its leg): the playback clock counts the new leg from that tick. Called whether the report waited in the queue or
		/// arrived already late; a late one leaves the leg some ticks old, which the next step walks.
		/// </summary>
		private void RebasePlaybackLeg(uint legStartTick)
		{
			if (!onPlaybackClock || destroyed || legStartTick == 0u || timeManager == null)
			{
				return;
			}
			playbackLegStartTick = legStartTick;
			double drawTick = PlaybackTick(timeManager);
			presentedElapsed = ObservedElapsed(drawTick, legStartTick, 0d);
		}

		/// <summary>An end the server reported: now, or when the playback clock reaches <paramref name="eventTick"/>.</summary>
		internal void EndObserved(uint eventTick)
		{
			if (destroyed)
			{
				return;
			}
			if (DefersObserved(eventTick))
			{
				DeferObserved(eventTick, ObservedEventKind.End, null, default, default);
				return;
			}
			DestroyAbilityObjectInternal();
		}

#if !UNITY_SERVER
		/// <summary>Every frame on a client: brings an observed copy up to the playback clock, and places the visuals.</summary>
		private void LateUpdate()
		{
			if (destroyed || !initialized || isServer || timeManager == null)
			{
				return;
			}

			double elapsed;
			// Seconds since the object spawned, by the tick clock: what the hand blend runs on (no wall clock in this file).
			float age = 0f;
			if (onPlaybackClock)
			{
				double drawTick = PlaybackTick(timeManager);
				if (drawTick > 0d)
				{
					DrainObservedEvents(drawTick);
					if (destroyed)
					{
						return;
					}
					presentedElapsed = ObservedElapsed(drawTick, playbackLegStartTick, presentedElapsed);
				}
				ShowPresented(presentedElapsed >= 0d);
				elapsed = System.Math.Max(0d, presentedElapsed);
				age = (float)((PreRedirectElapsedTicks + elapsed) * tickDelta);
				if (!presentationShown && presentedElapsed >= 0d)
				{
					/* First shown: the drawn caster is releasing it now. Its spawner is where the visuals leave from,
					 * measured against the leg's start, which is where the closed form puts them at this moment less the
					 * little they have already travelled. A copy first shown well into its flight (a late join) is past
					 * the blend and takes no offset. */
					presentationShown = true;
					presentationOffsetActive = false;
					if (presentationSpawner != null && PreRedirectElapsedTicks == 0u)
					{
						Vector3 handOffset = presentationSpawner.position - SpawnPosition;
						if (handOffset.sqrMagnitude < MaximumHandOffset * MaximumHandOffset)
						{
							SetPresentationOffset(handOffset);
						}
					}
					// And what its spawn chain plays (a muzzle effect) is played now, with it.
					// Not for a copy first seen in mid-flight: its launch happened before this client was watching.
					if (!observedLateJoin && PreRedirectElapsedTicks == 0u)
					{
						ReleaseSpawnEffects();
					}
					else
					{
						heldSpawnEffects?.Clear();
					}
				}
			}
			else
			{
				// This peer's own ticks: the whole ticks simulated, and how far the clock is into the next.
				double fraction = timeManager.GetTickPercentAsDouble();
				elapsed = ElapsedTicks + (fraction < 0d ? 0d : fraction > 1d ? 1d : fraction);
				age = (float)((PreRedirectElapsedTicks + elapsed) * tickDelta);
			}

			if (presented == null)
			{
				return;
			}

			Transform root = Transform != null ? Transform : transform;
			Vector3 position = presentationMoves
				? ClampToImpact(root, PresentedPosition(SpawnPosition, SpawnRotation, presentationMoveDirection, Speed, elapsed, tickDelta))
				: root.position;
			if (presentationOffsetActive)
			{
				float weight = HandBlendWeight(age);
				position += presentationOffset * weight;
				presentationOffsetActive = weight > 0f;
			}
			presented.SetPositionAndRotation(position, root.rotation);
		}

		/// <summary>
		/// The drawn position, stopped at whatever the object is about to hit.
		/// </summary>
		/// <remarks>
		/// The visuals run AHEAD of the simulated object by the part of a tick already elapsed, and the step that covers
		/// that stretch is swept on the next tick. Drawn freely, a projectile went up to a step (0.4 m at 12 m/s) into a wall
		/// or through a body before the hit ended it, and its impact effect then played behind where it had been seen. The
		/// stretch from the simulated position to the drawn one is traced here and the drawing stops at the first thing in
		/// it: scenery, or a character other than the caster.
		/// </remarks>
		private Vector3 ClampToImpact(Transform root, Vector3 drawn)
		{
			Vector3 from = root.position;
			Vector3 span = drawn - from;
			float distance = span.magnitude;
			if (distance < 1e-3f)
			{
				return drawn;
			}
			GameObject self = root.gameObject;
			int mask = AbilityObjectSweep.CollisionMaskForLayer(self.layer);
			if (mask == 0)
			{
				return drawn;
			}
			Vector3 direction = span / distance;
			PhysicsScene physicsScene = self.scene.GetPhysicsScene();
			int count;
			while (true)
			{
				count = physicsScene.Raycast(from, direction, coverHits, distance, mask, QueryTriggerInteraction.Ignore);
				if (!TargetOrdering.TryGrowQueryBuffer(ref coverHits, count))
				{
					break;
				}
			}
			float nearest = distance;
			for (int i = 0; i < count; ++i)
			{
				Collider struck = coverHits[i].collider;
				if (struck == null || struck.transform.IsChildOf(root))
				{
					continue;
				}
				TargetOrdering.ResolveHitRoot(struck, out ICharacter hitCharacter);
				if (hitCharacter != null && (ReferenceEquals(hitCharacter, Caster) || HasHit(hitCharacter)))
				{
					continue;
				}
				if (coverHits[i].distance < nearest)
				{
					nearest = coverHits[i].distance;
				}
			}
			return from + direction * nearest;
		}
#endif
		#endregion

		/// <summary>
		/// Immutable snapshot of the ability data captured at spawn time.
		/// Used as a fallback when the live <see cref="Ability"/> reference becomes null
		/// (e.g., after the owning character disconnects and the ability is detached).
		/// </summary>
		public AbilityObjectSnapshot Snapshot;

		/// <summary>
		/// Movement speed. Prefers the live Ability, falls back to the Snapshot.
		/// </summary>
		public float Speed => Ability != null ? Ability.Speed : (Snapshot != null ? Snapshot.Speed : 0f);

		/// <summary>
		/// Total configured lifetime. Prefers the live Ability, falls back to the Snapshot.
		/// </summary>
		public float TotalLifeTime => Ability != null ? Ability.LifeTime : (Snapshot != null ? Snapshot.LifeTime : 0f);

		/// <summary>
		/// OnHit events for collision dispatching. Prefers the live Ability, falls back to the Snapshot.
		/// Returns <see cref="IReadOnlyDictionary{TKey,TValue}"/> to prevent mutation through consumer code.
		/// </summary>
		public IReadOnlyDictionary<int, AbilityOnHitEvent> OnHitEvents => Ability != null ? Ability.OnHitEvents : Snapshot?.OnHitEvents;

		/// <summary>
		/// Active OnTick events. Prefers the live Ability, falls back to the Snapshot.
		/// Collapses the repeated null-coalescing pattern into a single property.
		/// </summary>
		private IReadOnlyDictionary<int, AbilityOnTickEvent> ActiveOnTickEvents
			=> Ability?.OnTickEvents ?? Snapshot?.OnTickEvents;

		/// <summary>
		/// Active OnDestroy events. Prefers the live Ability, falls back to the Snapshot.
		/// </summary>
		private IReadOnlyDictionary<int, AbilityOnDestroyEvent> ActiveOnDestroyEvents
			=> Ability?.OnDestroyEvents ?? Snapshot?.OnDestroyEvents;

		/// <summary>
		/// Cached tick event data instance to avoid per-frame allocation.
		/// </summary>
		private AbilityTickEventData cachedTickEventData;

		/// <summary>
		/// Guard flag to prevent double-destroy when lifetime expiry and collision
		/// occur on the same frame. All Unity callbacks and FishNet tick callbacks
		/// execute on the main thread, so a simple bool suffices.
		/// </summary>
		private bool destroyed;

		/// <summary>
		/// Prevents <see cref="InitializeAbilityObject"/> from running twice on
		/// the same instance (e.g., if a code path accidentally re-spawns an object
		/// that is still alive). A second call would cause duplicate event registration,
		/// duplicate container insertion, and invalid rollback state.
		/// </summary>
		private bool initialized;

		/// <summary>
		/// Fixed simulation time step, cached from <see cref="TimeManager.TickDelta"/>
		/// at spawn time. Used instead of <see cref="Time.deltaTime"/> so that lifetime
		/// countdown and tick-event dispatch are deterministic across client and server
		/// regardless of frame rate.
		/// </summary>
		private float tickDelta;

		/// <summary>
		/// Cached reference to the caster's <see cref="TimeManager"/>, obtained from
		/// <c>caster.NetworkObject.TimeManager</c> at spawn time. Used to subscribe to
		/// <see cref="TimeManager.OnTick"/> for deterministic tick-aligned simulation.
		/// </summary>
		private TimeManager timeManager;

		/// <summary>
		/// True when this ability object is running on the server.
		/// Collision-based hit effects (damage, healing, buffs) are only applied on the
		/// server to avoid visual fighting between client-side prediction and authoritative
		/// server broadcasts. The caster's own client also resolves hits, as a prediction; every
		/// other peer is told what was hit and spends no hit count of its own — see
		/// <see cref="ResolvesHitsLocally"/>.
		/// </summary>
		private bool isServer;

		/// <summary>
		/// True when this ability object is simulating on the server. Gameplay effects
		/// (damage, healing, buffs, area queries) are only applied where this is true.
		/// </summary>
		public bool IsServer => isServer;

		/// <summary>
		/// True when this peer may decide for itself what this object hit.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The server, which resolves authoritatively inside a rewind to the caster's view; and the
		/// caster's own client, which resolves the same question against the world it aimed in and
		/// is therefore predicting rather than guessing. Everybody else is told — see
		/// <see cref="ResolveSweptHits"/> and <c>AbilityObjectHitBroadcast</c>.
		/// </para>
		/// <para>
		/// Read live rather than latched at spawn: ownership can change while an object is in the
		/// air, and the peer that may resolve its remaining hits changes with it.
		/// </para>
		/// </remarks>
		/// <remarks>
		/// <para>
		/// Internal rather than private so <c>AbilityHitCountAction</c> can ask the same question
		/// before it MOVES the count. A peer that does not resolve hits never spends the count
		/// either (see <see cref="ApplyHit"/>), so an action that added to it on every peer walked
		/// an observer's copy away from the authoritative number.
		/// </para>
		/// </remarks>
		internal bool ResolvesHitsLocally
		{
			get
			{
				NetworkObject casterNob = Caster?.NetworkObject;
				return ResolvesHitsOnThisPeer(isServer, casterNob != null && casterNob.IsOwner);
			}
		}

		/// <summary>
		/// The whole rule <see cref="ResolvesHitsLocally"/> applies, as a pure function.
		/// </summary>
		/// <remarks>
		/// Separated so the truth table can be asserted directly rather than inferred from two
		/// live NetworkBehaviour facts that only exist on a spawned object. The row that matters is
		/// the last one: before <c>AbilityObjectHitBroadcast</c> existed this answered TRUE for
		/// every peer, and a third-party observer decided for itself what a projectile had hit.
		/// </remarks>
		/// <param name="isServer">Whether this object is simulating on the server.</param>
		/// <param name="casterIsOwner">Whether this peer owns the casting character.</param>
		/// <returns>True when this peer may decide the hit set for itself.</returns>
		internal static bool ResolvesHitsOnThisPeer(bool isServer, bool casterIsOwner)
		{
			return isServer || casterIsOwner;
		}

		/// <summary>
		/// Whether an accepted hit is certainly this object's last, as a pure function.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The answer has to be given BEFORE the OnHit chain runs, because that is when the hit
		/// message is written and the message is where the end is stated (see
		/// <c>AbilityObjectHitBroadcast.Ended</c>). Writing it after the chain is not an option: a
		/// chain is allowed to end the object, and <see cref="DestroyAbilityObjectInternal"/> nulls
		/// <see cref="Ability"/> and <see cref="Caster"/>, so a destroyed object cannot publish
		/// anything at all.
		/// </para>
		/// <para>
		/// So the rule must be CERTAIN rather than likely. <c>AbilityHitCountAction</c> is the one
		/// action that moves <see cref="HitCount"/>, and an amount of +1 exactly cancels the
		/// decrement this impact applies — so a chain carrying one can survive a hit that the count
		/// alone says ends it, and for those the end is reported by the standalone destroy instead.
		/// Every other case is decided by the count going in.
		/// </para>
		/// <para>
		/// A peer that does not resolve hits answers false whatever the count says: it spends none
		/// (see <see cref="ApplyHit"/>) and publishes nothing.
		/// </para>
		/// </remarks>
		/// <param name="resolvesHitsLocally">Whether this peer decides the hit set for itself.</param>
		/// <param name="hitCountBeforeThisHit">The object's remaining hit budget going into this hit.</param>
		/// <param name="chainCanExtendHitCount">Whether the OnHit chain carries an <c>AbilityHitCountAction</c>.</param>
		/// <returns>True when this hit certainly ends the object.</returns>
		internal static bool HitEndsObject(bool resolvesHitsLocally, int hitCountBeforeThisHit, bool chainCanExtendHitCount)
		{
			if (!resolvesHitsLocally)
			{
				return false;
			}
			if (chainCanExtendHitCount)
			{
				return false;
			}
			return hitCountBeforeThisHit <= 1;
		}

		/// <summary>
		/// Memoised answer for <see cref="HitChainCanExtendHitCount"/>. Null until first asked.
		/// </summary>
		private bool? hitChainCanExtendHitCount;

		/// <summary>
		/// Whether this object's OnHit chain carries an action that can ADD to its hit count.
		/// </summary>
		/// <remarks>
		/// A property of the authored ability, identical on every peer, so it is resolved once per
		/// object rather than per hit. The walk is two action lists per OnHit event because that is
		/// the whole of a <c>Trigger</c>'s shape — no serialized action in the project nests another
		/// list of actions — so a shallow scan is exact rather than approximate. An object with no
		/// chain, or one detached from its ability before it was asked, answers false: nothing can
		/// move the count.
		/// </remarks>
		private bool HitChainCanExtendHitCount
		{
			get
			{
				hitChainCanExtendHitCount ??= ChainCanExtendHitCount(OnHitEvents);
				return hitChainCanExtendHitCount.Value;
			}
		}

		/// <summary>Whether any OnHit event in the chain carries an <c>AbilityHitCountAction</c>.</summary>
		/// <param name="hitEvents">The OnHit chain, or null.</param>
		private static bool ChainCanExtendHitCount(IReadOnlyDictionary<int, AbilityOnHitEvent> hitEvents)
		{
			if (hitEvents == null)
			{
				return false;
			}

			foreach (AbilityOnHitEvent hitEvent in hitEvents.Values)
			{
				if (hitEvent == null)
				{
					continue;
				}
				if (ContainsHitCountAction(hitEvent.OnConditionsMetActions) ||
					ContainsHitCountAction(hitEvent.OnConditionsNotMetActions))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>Whether one authored action list carries an <c>AbilityHitCountAction</c>.</summary>
		private static bool ContainsHitCountAction(List<BaseAction> actions)
		{
			if (actions == null)
			{
				return false;
			}
			for (int i = 0; i < actions.Count; ++i)
			{
				if (actions[i] is AbilityHitCountAction)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Pulls the ability object out of whichever event shape an action was wired to.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The three ability event payloads carry the object on three different fields, and an
		/// action that resolves hits is meaningful on all of them: <c>OnSpawn</c> for a blast that
		/// goes off where it appears, <c>OnTick</c> for a lingering field, <c>OnHit</c> for a second
		/// effect fired from wherever the first one landed.
		/// </para>
		/// <para>
		/// <b>Shared because the actions disagreed.</b> <c>AbilityApplyHitscanAction</c> resolved all
		/// three; <c>AbilityApplyAreaAction</c> tested only <see cref="AbilityCollisionEventData"/>
		/// and so silently did nothing on <c>OnSpawn</c> or <c>OnTick</c> — which is exactly the
		/// failure a previous fix to that action believed it had removed, having corrected the peer
		/// gate while leaving the payload gate in place. One implementation is what stops the two
		/// drifting apart again.
		/// </para>
		/// <para>
		/// The collision payload is tried first: an action wired to <c>OnHit</c> is asking about the
		/// object that produced THAT hit, and a fork or a chain can carry a spawn payload alongside
		/// it describing a different object.
		/// </para>
		/// </remarks>
		/// <param name="eventData">The event the action was invoked with. Null yields false.</param>
		/// <param name="abilityObject">The resolved object, or null.</param>
		/// <returns>True when an ability object was found on any of the three payloads.</returns>
		public static bool TryResolveFrom(EventData eventData, out AbilityObject abilityObject)
		{
			abilityObject = null;
			if (eventData == null)
			{
				return false;
			}

			if (eventData.TryGet(out AbilityCollisionEventData collision) && collision.AbilityObject != null)
			{
				abilityObject = collision.AbilityObject;
				return true;
			}
			if (eventData.TryGet(out AbilitySpawnEventData spawn) && spawn.InitialAbilityObject != null)
			{
				abilityObject = spawn.InitialAbilityObject;
				return true;
			}
			if (eventData.TryGet(out AbilityTickEventData tick) && tick.AbilityObject != null)
			{
				abilityObject = tick.AbilityObject;
				return true;
			}
			/* OnDestroy, last because it is the rarest. Without it every object-scoped action wired
			 * to a destroy trigger resolved nothing and silently did nothing — see
			 * AbilityDestroyEventData. */
			if (eventData.TryGet(out AbilityDestroyEventData destroy) && destroy.AbilityObject != null)
			{
				abilityObject = destroy.AbilityObject;
				return true;
			}
			return false;
		}

		/// <summary>
		/// How many distinct bodies this object has already hit. For diagnostics and tests.
		/// </summary>
		internal int HitTargetCount => hitTargets?.Count ?? 0;

		/// <summary>
		/// How many hits this object has published to observers. For diagnostics and tests.
		/// </summary>
		/// <remarks>
		/// Counts ATTEMPTS, taken before the send's own guards, so it measures the one thing worth
		/// pinning: that a hit is published once per body rather than once per tick. The sweep
		/// re-runs every tick and the shipped abilities are stationary and live five seconds, so a
		/// publish on the wrong side of the dedupe is 150 reliable messages per observer instead of
		/// one.
		/// </remarks>
		internal int PublishedHitCount { get; private set; }

		/// <summary>
		/// True once this object has ended, whether by lifetime, collision or eviction.
		/// </summary>
		/// <remarks>
		/// The GameObject survives until Unity collects it at end of frame, so a destroyed object
		/// can still be reached through the ability's container map within the same frame. Anything
		/// enumerating live objects — the spawn payload's in-flight list, for one — has to test
		/// this rather than just the null check.
		/// </remarks>
		internal bool IsDestroyed => destroyed;

		/// <summary>
		/// Cached reference to the object's GameObject.
		/// </summary>
		public GameObject GameObject { get; private set; }
		/// <summary>
		/// Cached reference to the object's Transform.
		/// </summary>
		public Transform Transform { get; private set; }

		/// <summary>
		/// Unity Awake callback. Caches GameObject, Transform, and Rigidbody references.
		/// Sets the Rigidbody to kinematic if present.
		/// </summary>
		private void Awake()
		{
			CacheComponents();
		}

		/// <summary>
		/// Caches GameObject, Transform and Rigidbody references and makes the rigidbody kinematic.
		/// </summary>
		/// <remarks>
		/// Idempotent, and called from initialisation as well as <see cref="Awake"/>: Unity does
		/// not run <c>Awake</c> on a component added to an inactive GameObject, and
		/// <see cref="Spawn"/> deactivates the instance before it adds a missing
		/// <see cref="AbilityObject"/>, so without this an object whose prefab lacks the component
		/// reached activation with <see cref="GameObject"/> null and threw inside Replicate.
		/// </remarks>
		private void CacheComponents()
		{
			GameObject ??= gameObject;
			Transform ??= transform;
			CachedRigidBody ??= GetComponent<Rigidbody>();
			/* Children included, and the prefab cache's lookup matches: an authored child hitbox
			 * (a fat projectile whose collider hangs off a scaled child) used to be found by
			 * NEITHER lookup, silently degrading the sweep to a ray on every peer. The inactive
			 * flag is deliberate — Spawn deactivates the instance before components are cached. */
			sweepShape ??= GetComponentInChildren<Collider>(true);
			if (CachedRigidBody != null)
			{
				CachedRigidBody.isKinematic = true;
			}
		}

		/// <summary>
		/// Resets transient runtime state on a freshly cloned ability object before it is reinitialized.
		/// </summary>
		private void ResetRuntimeState()
		{
			cachedTickEventData = null;
			destroyed = false;
			initialized = false;
			/* The OnHit chain belongs to whichever ability this clone is about to serve, so the
			 * memoised answer from its previous life must not carry over. */
			hitChainCanExtendHitCount = null;
			// A reused clone must not linger in the phantom registry under its previous identity.
			UnregisterDetached();
			ElapsedTicks = 0;
			PreRedirectElapsedTicks = 0;
			recordedSweeps = null;
			recordedSweepNext = 0;
			casterController = null;
			onPlaybackClock = false;
			playbackLegStartTick = 0u;
			presentedElapsed = -1d;
			observedEvents?.Clear();
			presentationOffset = Vector3.zero;
			presentationOffsetActive = false;
			presentationSpawner = null;
			presentationShown = false;
			heldSpawnEffects?.Clear();
			lastSweepPosition = Transform != null ? Transform.position : transform.position;
			hitTargets?.Clear();
			sweepHits?.Clear();

			if (timeManager != null)
			{
				timeManager.OnTick -= OnTick;
				timeManager = null;
			}
		}

		/// <summary>
		/// Called by <see cref="TimeManager.OnTick"/> once per network tick.
		/// Handles lifetime countdown, tick-event dispatch, and orphan detection
		/// with deterministic timing that matches across client and server.
		/// Ability objects persist even if the caster disconnects or the Ability is detached.
		/// They continue counting down lifetime but skip ECA events that require a live caster,
		/// since <see cref="Trigger.Execute"/> rejects null initiators.
		/// </summary>
		private void OnTick()
		{
			if (destroyed) return;

			// If both the ability reference and snapshot are gone, this object is truly orphaned.
			if (Ability == null && Snapshot == null)
			{
				DestroyAbilityObjectInternal();
				return;
			}

			/* Spawn effects nothing released: a copy that was expected on the playback clock and never put there, or one
			 * spawned by a path that does not end in Spawn. Played now, not lost. */
			if (heldSpawnEffects != null && heldSpawnEffects.Count > 0 && (!onPlaybackClock || presentationShown))
			{
				ReleaseSpawnEffects();
			}

			// An observed copy lives on the clock observed characters are drawn on, not on this client's own ticks.
			if (onPlaybackClock)
			{
				StepOnPlaybackClock();
				return;
			}

			StepOnce();
		}

		/// <summary>
		/// One step of the object's life: the lifetime, the tick count, the OnTick chain (which is what moves it), expiry,
		/// and the sweep.
		/// </summary>
		private void StepOnce()
		{
			float totalLifeTime = TotalLifeTime;

			// Update remaining lifetime using deterministic tick delta.
			if (totalLifeTime > 0.0f)
			{
				RemainingLifeTime -= tickDelta;
			}

			// Integer tick count, so closed-form trajectories never accumulate float error.
			ElapsedTicks++;

			// Dispatch OnTick events only if the caster is still valid.
			// If the caster disconnected, the object keeps existing but skips ECA dispatching
			// since Trigger.Execute rejects null initiators.
			var tickEvents = ActiveOnTickEvents;
			if (tickEvents != null && Caster != null && Caster.IsSpawned)
			{
				// Recreate if null or if the Caster has changed (e.g., swapped to a
				// SnapshotCharacter phantom after disconnect). EventData.Initiator is
				// readonly, so a new instance is required when the caster changes.
				if (cachedTickEventData == null || cachedTickEventData.Initiator != Caster)
				{
					cachedTickEventData = new AbilityTickEventData(Caster, tickDelta, this);
				}
				else
				{
					cachedTickEventData.DeltaTime = tickDelta;
				}
				// Update the current tick on the cached event so OnTick-triggered ECA actions
				// (e.g. ApplyBuffAction) receive the authoritative server tick. Carried as a
				// plain uint on AbilityTickEventData rather than a TickEventData sub-payload
				// to avoid per-tick heap allocation on this hot path (§1.4).
				cachedTickEventData.CurrentTick = GetCurrentAuthoritativeTick();
				// Thread the object's deterministic RNG so OnTick ECA actions (e.g. random
				// debuff application) can roll deterministic values. Zero-alloc: same field,
				// same instance — no new allocation on this hot path.
				cachedTickEventData.RNG = RNG;

				foreach (var trigger in tickEvents.Values)
				{
					trigger.Execute(cachedTickEventData);
				}
			}
			else
			{
				// Clear stale reference so it's recreated with the correct Caster
				// if the object re-enters a valid caster state.
				cachedTickEventData = null;
			}

			// If lifetime expired, destroy.
			// A positive lifetime that has elapsed triggers destruction.
			// Zero or negative lifetime means "infinite" — the object persists
			// until destroyed by other means (e.g., HitCount exhaustion).
			if (totalLifeTime > 0.0f && RemainingLifeTime <= 0.0f)
			{
				DestroyAbilityObjectInternal();
				return;
			}

			/* Last, and after the movement the tick events just applied. The sweep covers the segment
			 * from where the object resolved hits previously to where it now is, so it has to run
			 * once the transform has been advanced — and after the expiry check, because an object
			 * that ends this tick never reached a physics step under the callback this replaces and
			 * must not start resolving hits it never used to. */
			ResolveSweptHits();
		}

		/// <summary>
		/// Unity OnDestroy callback. Unsubscribes from <see cref="TimeManager.OnTick"/> to
		/// prevent leaked subscriptions if the GameObject is destroyed externally
		/// (e.g., scene unload) without going through <see cref="DestroyAbilityObjectInternal"/>.
		/// </summary>
		private void OnDestroy()
		{
			if (timeManager != null)
			{
				timeManager.OnTick -= OnTick;
				timeManager = null;
			}
			// A scene teardown that never routed through DestroyAbilityObjectInternal must not
			// leave a dead reference in the phantom registry.
			UnregisterDetached();
		}

		/// <summary>
		/// Resolves everything this object touched over the segment it travelled this tick, and
		/// dispatches an OnHit event for each.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Why this is a query and not a collision callback.</b> An ability object is a local,
		/// deterministic object: a kinematic body teleported once per tick to a closed-form position.
		/// Unity's <c>OnCollisionEnter</c> resolved those teleports against the server's present
		/// positions, which is wrong twice over. It is not lag compensated — the caster's client
		/// rendered its peers roughly two ticks plus half a round trip in the past, a gap measured at
		/// 0.45&#160;m on a same-city connection and 2.2&#160;m at 300&#160;ms — and a rewind scope
		/// cannot be wrapped around a physics step that has already run. And a body that jumps its
		/// whole per-tick step at once tunnels through any target thinner than that step. Sweeping
		/// the segment explicitly fixes both, and it has to be per object rather than one rewind for
		/// all of them because every projectile has a different caster with a different view offset.
		/// </para>
		/// <para>
		/// <b>The server rewinds and a client does not, deliberately.</b> The CASTER'S OWNER renders a
		/// world that already <i>is</i> the rewound one; the server displacing its characters to the
		/// tick that client was looking at makes the two queries run against the same geometry, so a
		/// predicted hit and the authoritative one agree by construction rather than by luck. That
		/// symmetry is the whole point of resolving it here — <see cref="PredictedCombatEvents"/>
		/// greys out a prediction the server contradicts, so the divergence was becoming visible as
		/// damage numbers that retracted.
		/// </para>
		/// <para>
		/// <b>That symmetry does not extend to a third-party observer, so it no longer sweeps.</b>
		/// The server rewinds to the CASTER'S view offset, not to the observer's, so an observer's
		/// local sweep answered a question nobody asked: it resolved the caster's projectile against
		/// the observer's own interpolated world. Two outcomes followed, and only one of them was
		/// ever corrected.
		/// <list type="bullet">
		/// <item>The observer MISSED a hit the server landed — corrected by
		/// <c>AbilityObjectDestroyedBroadcast</c>, which is why that message is reliable. But the
		/// impact effect still never played: the correction ends the object, and the OnHit events
		/// belong to a hit this peer never resolved.</item>
		/// <item>The observer HIT where the server did not — <b>never corrected</b>. Its copy ended
		/// early, played its impact effect where nothing had happened, and with a fork carried on
		/// down a heading the server never took. No message can revive a copy that has ended.</item>
		/// </list>
		/// <para>
		/// Both are closed by having the server publish the hit it resolved
		/// (<c>AbilityObjectHitBroadcast</c>) and by <see cref="ResolvesHitsLocally"/> keeping the
		/// query to the two peers whose world can answer it.
		/// </para>
		/// <para>
		/// <b>Deferring to the server was previously rejected as too slow, and that reasoning does
		/// not survive the numbers.</b> It assumed every observed hit would wait half a round trip.
		/// It does not: an observer's copy is deliberately run
		/// <c>LagCompensationTick.SpectatorInterpolationTicks</c> behind the server's so it stays
		/// consistent with the interpolated peers it is drawn against — see
		/// <c>AbilityController.ComputeObserverFastForwardTicks</c> — which is 66&#160;ms at the
		/// shipped tick rate and is a head start the message already holds. Inside roughly
		/// 133&#160;ms round trip the authoritative answer arrives BEFORE the local guess would have
		/// fired; past it, the impact is late by one-way latency minus that 66&#160;ms rather than by
		/// a round trip. Against that, a wrong impact was permanent.
		/// </para>
		/// <para>
		/// <b>Dispatched on every peer</b>, like the tick events — what differs is only WHERE the
		/// hit is decided, not where its events run. What each action then does about it is the
		/// action's own decision: visual actions run where there is a screen, state-changing actions
		/// run where they are authoritative or predicted (<c>EcaAuthority.MayPredict</c>). Each
		/// <see cref="AbilityOnHitEvent"/> is executed independently and its inherited
		/// <c>Trigger.TargetSelector</c> selects the final targets, defaulting to the direct hit.
		/// </para>
		/// </remarks>
		private void ResolveSweptHits()
		{
			if (destroyed)
			{
				return;
			}

			/* Only a peer whose world can answer the question asks it.
			 *
			 * The SERVER resolves authoritatively, inside a rewind to the caster's view. The
			 * CASTER'S OWNER resolves as a prediction, and is entitled to: its world already IS
			 * that rewound one, which is what makes its predicted hit and the server's agree by
			 * construction rather than by luck.
			 *
			 * A third-party observer is neither. It holds every character interpolated against its
			 * OWN latency, so the same query run here answers a question nobody asked — and while
			 * a missed hit was corrected by AbilityObjectDestroyedBroadcast, an invented one never
			 * was: this copy ended early, played its impact where nothing happened, and a fork sent
			 * it down a heading the server never took. It is told instead, by
			 * AbilityObjectHitBroadcast. */
			Transform shapeTransform = Transform != null ? Transform : transform;
			Vector3 from = lastSweepPosition;
			Vector3 to = shapeTransform.position;
			// Advanced before anything can throw or destroy: a segment must never be swept twice,
			// which would re-hit everything along it that the hit set has not already absorbed.
			lastSweepPosition = to;

			// Kept for a few ticks on the server: what a hit the owner claims is checked against (TryApplyClaimedHit).
			if (isServer)
			{
				RecordSweep(from, to);
			}

			/* Advanced ABOVE this gate, not below it. Ownership can change while an object is in
			 * the air, and a peer that starts resolving part-way through would otherwise sweep from
			 * wherever the origin was last written — the spawn point — and resolve the entire flight
			 * path as one segment. Tracking it on every peer costs two Vector3 writes a tick and
			 * makes the hand-over start from where the object actually is. */
			if (!ResolvesHitsLocally)
			{
				return;
			}

			GameObject self = GameObject != null ? GameObject : gameObject;
			/* The object's own scene, never the global Physics API. A scene server hosts many scenes
			 * and the default one holds none of these colliders — the trap TargetController.UpdateTarget
			 * was already caught by. */
			Scene scene = self.scene;
			PhysicsScene physicsScene = scene.GetPhysicsScene();
			LayerMask mask = AbilityObjectSweep.CollisionMaskForLayer(self.layer);

			sweepHits ??= new List<AbilitySweepHit>(16);

			int count;
			/* Server only, and only for a caster whose view actually lagged. LagCompensationTick
			 * declines for a server-driven character (an NPC brain aims at live positions) and for a
			 * connection whose tick bookkeeping is not established, in which case the query runs
			 * uncompensated — the behaviour this call site had before. On a client there is nothing
			 * to rewind and nothing that should be: its world is already the one it aimed in. */
			if (isServer && LagCompensationTick.TryResolve(Caster, timeManager, StepInputTick, out RewindTarget rewindTarget))
			{
				/* The caster is excluded — it fires from where it is, not where it was. The query is
				 * run eagerly inside the scope and the results are dispatched after it closes, so no
				 * damage or ECA action ever runs against a world several hundred milliseconds stale. */
				// Only the characters near this step are moved (LagCompensationRegistry.RewindReach covers how far one can have come).
				Vector3 sweepHalfExtents = AbilityPrefabColliderCache.ResolveShapeHalfExtents(sweepShape);
				float sweepRadius = (to - from).magnitude * 0.5f + Mathf.Max(sweepHalfExtents.x, Mathf.Max(sweepHalfExtents.y, sweepHalfExtents.z));
				using (LagCompensationRegistry.Rewind(scene, rewindTarget, Caster, (from + to) * 0.5f, sweepRadius))
				{
					count = AbilityObjectSweep.Sweep(physicsScene, sweepShape, shapeTransform, from, to, mask, sweepHits);
				}
			}
			else
			{
				count = AbilityObjectSweep.Sweep(physicsScene, sweepShape, shapeTransform, from, to, mask, sweepHits);
			}

			for (int i = 0; i < count; ++i)
			{
				Vector3 hitPoint = sweepHits[i].Point;
				if (!DispatchSweptHit(sweepHits[i]))
				{
					// The object stopped (or turned) here: so does the record of this step (TruncateLastSweep).
					if (isServer)
					{
						TruncateLastSweep(hitPoint);
					}
					break;
				}
			}

			sweepHits.Clear();
		}

		/// <summary>
		/// Runs the OnHit events for one swept hit and applies its cost to <see cref="HitCount"/>.
		/// </summary>
		/// <param name="hit">The hit to resolve. Never this object's own collider — <see cref="AbilityObjectSweep"/> excludes those.</param>
		/// <returns>False when this object has ended and the rest of the sweep must be abandoned.</returns>
		private bool DispatchSweptHit(AbilitySweepHit hit)
		{
			Collider collider = hit.Collider;
			if (collider == null)
			{
				return true;
			}

			/* The rigidbody's GameObject where there is one, which is what Collision.gameObject
			 * reported — so a hit on a child collider resolves to the body it belongs to rather than
			 * to the bone. Walking the parents afterwards covers a character rigged without one; the
			 * same trap EventData.SetTarget and TargetOrdering.Rank both already resolve this way.
			 *
			 * Shared with the area query rather than spelled out here. AbilityApplyAreaAction used a
			 * bare GetComponent on the collider, so the two hit-resolving paths disagreed about who
			 * was even a candidate: this one saw a character rigged with a child hitbox, that one did
			 * not. One implementation is what keeps them honest. */
			GameObject hitRoot = TargetOrdering.ResolveHitRoot(collider, out ICharacter hitCharacter);

			/* Never the caster's own body.
			 *
			 * AbilityObjectSweep.Accept excludes this object and its children, which is the right
			 * rule for the object -- but the caster is a separate body, and a melee ability spawns
			 * INSIDE it. Its capsule was therefore in the overlap on the very first tick and was
			 * accepted ahead of anything else: 17 of 27 swept hits in one play session resolved to
			 * the caster. Each one claimed a slot in the per-object dedupe and spent a hit from
			 * HitCount, so an ability that may only strike once was frequently used up on the
			 * player who cast it before it ever reached what they aimed at. That is what made melee
			 * damage look intermittent and made a target with more health survive indefinitely,
			 * while the same swing on a weaker one occasionally landed.
			 *
			 * Dropped here rather than in the sweep so the sweep keeps its single, geometric rule,
			 * and because this is the point at which the body has been resolved to a character. */
			if (hitCharacter != null && Caster != null && ReferenceEquals(hitCharacter, Caster))
			{
				return true;
			}

			/* Every swept hit, and what it resolved to. A sweep that strikes a collider but resolves
			 * no ICharacter produces no target at all, and the ability then falls back to the caster
			 * -- which reads downstream as the player hitting themselves, with no indication that a
			 * hit ever happened. Naming the collider, the body it resolved to and whether a character
			 * came out of it separates "the sweep never reached it" from "it was reached and not
			 * recognised", which are otherwise identical from the outside. */
			Log.Debug("AbilityObject",
				$"Swept hit: collider '{collider.name}' on layer {collider.gameObject.layer}, " +
				$"resolved body '{(hitRoot != null ? hitRoot.name : "<null>")}', " +
				$"character {(hitCharacter != null ? hitCharacter.GameObject.name : "<none>")}.");

			return ApplyHit(hitCharacter, hitRoot, hit.Point, hit.Normal, hit.LocalPoint);
		}

		/// <summary>
		/// Applies one resolved hit: the per-object dedupe, the OnHit events, and the hit count.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Shared by the two ways a hit reaches this object — the local sweep on the server and on
		/// the caster's owner, and <c>AbilityObjectHitBroadcast</c> on everybody else — so the two
		/// cannot drift into treating the same hit differently. In particular the dedupe below is
		/// what makes the broadcast a no-op on a peer that had already predicted the hit, which is
		/// why the message can be sent to the owner as well without doubling its effects.
		/// </para>
		/// </remarks>
		/// <param name="hitCharacter">The character that was hit, or null for scenery.</param>
		/// <param name="hitKey">The body to dedupe on when there is no character.</param>
		/// <param name="point">World point of impact.</param>
		/// <param name="normal">Surface normal at the impact.</param>
		/// <param name="localPoint">
		/// The impact point in the hit body's own space, captured inside the rewind scope. The only
		/// impact position it is safe to compare against anything defined relative to that body —
		/// see <see cref="AbilitySweepHit.LocalPoint"/>.
		/// </param>
		/// <param name="isAuthoritativeEcho">
		/// True when this hit arrived from the server rather than being resolved here. Reaches the
		/// OnHit events on the collision payload; see
		/// <see cref="AbilityCollisionEventData.IsAuthoritativeEcho"/>.
		/// </param>
		/// <returns>
		/// False when the rest of this tick's sweep must be abandoned — either because the object
		/// has ended, or because it was deflected and is no longer on the line those hits were
		/// gathered along.
		/// </returns>
		private bool ApplyHit(ICharacter hitCharacter, GameObject hitKey, Vector3 point, Vector3 normal, Vector3 localPoint, bool isAuthoritativeEcho = false)
		{
			/* Once per target for this object's whole life, keyed on the character where there is one
			 * so two hitboxes on one body cost one hit. The sweep runs every tick, and a pierce that
			 * ends up inside a character overlaps it on all of them; without this it would drain its
			 * entire hit count into one victim in a fraction of a second. */
			GameObject dedupeKey = hitCharacter != null ? hitCharacter.GameObject : hitKey;
			if (dedupeKey == null)
			{
				return true;
			}
			hitTargets ??= new HashSet<GameObject>();
			if (!hitTargets.Add(dedupeKey))
			{
				return true;
			}

			/* DEFLECTION, before the hit is accepted as a hit at all.
			 *
			 * A block reduces damage and runs inside CharacterDamageController; a deflect rejects
			 * the hit outright, so it has to happen here — ahead of the OnHit events, ahead of the
			 * hit count, ahead of anything that could end this object. What the defender gets is a
			 * projectile that never struck them; what the projectile gets is a new heading.
			 *
			 * Decided only by a peer that resolves hits (the server, and the caster's own client),
			 * for the same reason the sweep is: an observer holds every character interpolated
			 * against its own latency and would be answering a question nobody asked. It is TOLD,
			 * by AbilityObjectHitBroadcast, which carries both the fact and the resulting heading —
			 * absolute, so a peer that predicted the same deflection applies it as a no-op rather
			 * than mirroring an already-mirrored vector back at the defender.
			 *
			 * The target stays in the hit set. It has had its answer to this object and must not be
			 * offered another one every tick the two of them remain overlapping — the same rule
			 * Redirect states for the fork case, and for the same reason. */
			if (hitCharacter != null && !isAuthoritativeEcho && ResolvesHitsLocally)
			{
				Transform headingTransform = Transform != null ? Transform : transform;
				if (DamageMitigation.TryDeflect(hitCharacter, headingTransform.forward, normal,
						mutate: isServer, out Vector3 deflectedHeading))
				{
					if (isServer)
					{
						BroadcastHitToObservers(hitCharacter, point, normal,
							deflected: true, deflectHeading: deflectedHeading);
					}
					else
					{
						/* The owner's copy met the defender's parry: claimed like a hit, so the server's projectile (whose sweep
						 * may have passed a hand's breadth by) is turned too, and does not fly on to strike somebody else. */
						NoteOwnerHit(hitCharacter);
					}
					ApplyDeflection(deflectedHeading);
					/* The rest of this tick's sweep is abandoned. Those hits were gathered along the
					 * segment the object travelled INTO the defender, and it is not on that line any
					 * more — carrying on would let a projectile that was blocked by the shield in
					 * front also strike whoever was standing behind it, in the same tick. */
					return false;
				}

				/* THE SHIELD, after the parry.
				 *
				 * A deflect window is a timed, skilled action that gives the projectile back; a
				 * raised shield is a standing object that eats it. When a player has both up the
				 * parry should win, because it is the thing they had to time — so it is asked first
				 * and this only sees what it declined.
				 *
				 * The point is compared in the DEFENDER'S OWN SPACE. Hits are dispatched after the
				 * rewind scope has closed, so a world-space volume read here would sit where the
				 * defender is now while the impact point came from where the defender was; a local
				 * point against a local volume has no such disagreement to have. See
				 * AbilitySweepHit.LocalPoint. */
				if (DamageMitigation.TryBlockAtVolume(hitCharacter, localPoint, mutate: isServer))
				{
					if (isServer)
					{
						/* Told as a BLOCK, and that bit is the whole point of it.
						 *
						 * This used to travel as an ordinary impact, and an observer cannot tell the
						 * two apart: the mitigation block above is skipped for an authoritative echo
						 * — correctly, since the peer that resolved the hit already asked whether a
						 * shield stopped it — so the echo fell straight through to RunHitEvents and
						 * every observer ran the ability's entire OnHit chain for a shot the server
						 * had rejected. Impact effects played on a shot that never landed, including
						 * on the blocker's own screen, AbilityForkHitAction redirected the copy off a
						 * hit the server never accepted, and the copy drew RNG the server never did.
						 *
						 * Ended rides the same message, so the destroy that follows here does not
						 * need its own. The point and normal are the shield's, so the destroy chain
						 * draws the impact on the shield face. */
						BroadcastHitToObservers(hitCharacter, point, normal, blocked: true, ended: true);
					}
					else
					{
						// Claimed as for a parry, so the server's shield takes it too.
						NoteOwnerHit(hitCharacter);
					}
					// Stopped by a defence: nothing is left for a late claim to grant.
					endedOnCharacter = true;
					MoveToImpact(point);
					/* Destroyed, not merely stopped: a blocked projectile is gone. Destroy events fire
					 * — an impact on a shield is exactly the moment an authored effect wants to play.
					 * notifyObservers is false because the hit message above already said Ended; see
					 * AbilityObjectHitBroadcast.Ended. */
					DestroyAbilityObjectInternal(dispatchDestroyEvents: true, notifyObservers: false);
					return false;
				}
			}

			/* Published the moment the hit is ACCEPTED, and never before.
			 *
			 * The dedupe above is what makes this once per target rather than once per tick. The
			 * sweep re-runs every tick and a stationary object sitting on a character reports it on
			 * all of them — the authored abilities are stationary and live five seconds, so
			 * broadcasting ahead of this guard sent the same hit 150 times to every observer, on the
			 * reliable channel, against a per-peer budget of roughly 400 B/s.
			 *
			 * Still before the OnHit events and before anything can end this object, so an observer
			 * is told about a hit even if an authored action destroys the object while handling it.
			 * That ordering is not a preference: destruction nulls Ability and Caster, so a
			 * destroyed object cannot publish at all.
			 *
			 * Which is why whether this hit is the object's LAST has to be decided here too. The end
			 * is stated inline on this message rather than by a second one (see
			 * AbilityObjectHitBroadcast.Ended), so the claim is made before the chain runs — and
			 * therefore has to be exact rather than likely. The only thing in an OnHit chain that can
			 * move the count is AbilityHitCountAction, and HitEndsObject refuses to claim an end for
			 * a chain that carries one. */
			bool endsOnThisHit = HitEndsObject(ResolvesHitsLocally, HitCount, HitChainCanExtendHitCount);

			if (isServer)
			{
				BroadcastHitToObservers(hitCharacter, point, normal, ended: endsOnThisHit);
			}
			else if (hitCharacter != null && !isAuthoritativeEcho && ResolvesHitsLocally)
			{
				/* The owner's own copy hit a character: say so with the next input, so the server can grant the hit
				 * even where its history puts that character a hand's breadth from where it was drawn here. A hit the
				 * server tells us about (the echo) is its own already. */
				NoteOwnerHit(hitCharacter);
			}

			/* Captured before the events so a redirect they apply is detectable after. The hit
			 * broadcast above deliberately precedes the events; a heading change inside them gets
			 * its own follow-up message below. */
			Quaternion preEventHeading = SpawnRotation;

			RunHitEvents(hitCharacter, point, normal, isAuthoritativeEcho);

			// An OnHit action is allowed to end this object (and AbilityHitCountAction is allowed to
			// extend it); either way the decision below must read the state the actions left behind.
			if (destroyed)
			{
				return false;
			}

			/* The chain turned the survivor — a fork. The heading travels as its own reliable
			 * message so a receiver that had to DROP the hit (unobservable victim, so it skipped
			 * the fork's RNG draw and cannot re-derive anything) still turns its copy where the
			 * server did. See AbilityObjectRedirectBroadcast. */
			if (isServer && SpawnRotation != preEventHeading)
			{
				BroadcastRedirectToObservers();
			}

			/* The hit count is spent only by a peer that decided the hit.
			 *
			 * On an observer this object's end-of-life belongs to AbilityObjectDestroyedBroadcast,
			 * which the server sends from exactly the tick its own count ran out. Spending the
			 * count here as well would end the copy early on the peer whose answer is not the one
			 * that matters, which is the whole failure this path was rebuilt to remove — and it
			 * cannot be right anyway, since an observer is only told about the hits the server
			 * resolved and never sees the ones it declined. */
			if (!ResolvesHitsLocally)
			{
				return true;
			}

			// NOTE: HitCount decrements even for orphaned objects (Ability == null,
			// Snapshot != null) so they drain via collision rather than persisting
			// indefinitely as invulnerable ghosts.
			HitCount--;
			if (HitCount < 1)
			{
				// For the grace a late hit claim gets: an object that spent its last hit on a character has no hit left to grant.
				endedOnCharacter = hitCharacter != null;
				/* A hit is the one end-of-life that is NOT deterministic across peers: it is
				 * resolved against the caster's world, which no observer holds, so the server tells
				 * them. Lifetime expiry is identical everywhere and needs no message.
				 *
				 * It is told INLINE whenever the hit message could already state it — which is every
				 * shipped ability, all of which carry HitCount 1 — and the standalone destroy is sent
				 * only for the remainder: a pierce chain, whose count this hit could not predict. */
				MoveToImpact(point);
				DestroyAbilityObjectInternal(dispatchDestroyEvents: true, notifyObservers: !endsOnThisHit);
				return false;
			}

			return true;
		}

		/// <summary>
		/// A moving object ending on a hit is put where the hit is, so the effects its end plays (an impact, an explosion)
		/// play there: the root had already been moved to the end of the step before the sweep found the hit, up to a step
		/// past it, inside the wall or behind the victim, while the drawing stopped at the surface (ClampToImpact).
		/// </summary>
		private void MoveToImpact(Vector3 point)
		{
			if (Speed <= 0f || float.IsNaN(point.x) || float.IsNaN(point.y) || float.IsNaN(point.z))
			{
				return;
			}
			Transform root = Transform != null ? Transform : transform;
			root.position = point;
		}

		/// <summary>Stopped by a defence (a raised shield took it): it answers no late claim (see endedOnCharacter).</summary>
		internal void MarkStoppedByDefence()
		{
			endedOnCharacter = true;
		}

		/// <summary>
		/// Turns this object onto the heading a deflection sent it along.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A thin wrapper over <see cref="Redirect"/> so the two callers — the peer that DECIDED the
		/// deflection and the peer that was TOLD about it — cannot start a new leg differently.
		/// Redirect is what actually re-anchors the closed-form trajectory; writing the rotation
		/// alone would be overwritten on the next tick, which is the trap
		/// <c>AbilityForkHitAction</c> fell into.
		/// </para>
		/// <para>
		/// <b>Quantised before it is applied, never after.</b> The peer that DECIDES a deflection
		/// computes a raw <c>Vector3.Reflect</c>, but what every other peer receives is that vector
		/// through <see cref="AimDirectionCompression"/> — so applying the raw one here had the
		/// server and the caster's client re-anchor the closed-form trajectory on a heading the wire
		/// cannot carry while observers flew the decoded one. Small per step, but the new leg is
		/// evaluated from this rotation, so the gap grows along the whole of it. This is the same
		/// quantise-at-the-producer rule <c>KCCPlayer.PopulateInput</c> follows for aim, applied at
		/// the one place the deflect path was still breaking it; a heading that arrived already
		/// decoded is a fixed point, so the observer path is unaffected.
		/// </para>
		/// </remarks>
		/// <param name="heading">Unit direction to travel along from here.</param>
		private void ApplyDeflection(Vector3 heading)
		{
			if (heading.sqrMagnitude < 1e-8f)
			{
				return;
			}
			Redirect(AimDirectionCompression.ToRotation(AimDirectionCompression.Quantize(heading)));
		}

		/// <summary>
		/// Applies a deflection the server resolved, on a peer that does not resolve its own.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The counterpart to <see cref="ApplyObservedHit"/> for a hit that was REJECTED. It runs no
		/// events and touches no hit count — there was no hit — and does exactly one thing: put this
		/// copy of the object on the heading the server's copy took.
		/// </para>
		/// <para>
		/// Separated from <see cref="ApplyObservedHit"/> because it is the half that still applies
		/// when the victim cannot be resolved. A character outside this client's streaming budget is
		/// not spawned here, so the hit itself has to be dropped; the trajectory change does not,
		/// and dropping it left the object flying at a target the server had already turned it away
		/// from — which is also why the heading is carried rather than derived from the victim.
		/// </para>
		/// </remarks>
		/// <param name="heading">The absolute heading the server's copy left on.</param>
		internal void ApplyObservedDeflection(Vector3 heading, uint eventTick = 0u)
		{
			if (destroyed || heading.sqrMagnitude < 1e-8f)
			{
				return;
			}
			if (DefersObserved(eventTick))
			{
				DeferObserved(eventTick, ObservedEventKind.Deflection, null, default, heading);
				return;
			}

			/* Absolute, so a peer that already predicted this deflection — the caster's owner,
			 * which resolves the hit locally and receives the same message half a round trip later
			 * — treats it as confirmation. The heading being absolute made the direction idempotent
			 * but not the turn: ApplyDeflection → Redirect resets the leg clock, re-anchors the
			 * spawn position to wherever the object is NOW, and adds the flown leg to the elapsed
			 * bookkeeping a second time. Invisible on a straight line, a visible jump on anything
			 * else. The same comparison ApplyObservedRedirect makes, for the same reason. */
			Vector3 current = SpawnRotation * Vector3.forward;
			if (Vector3.Dot(current.normalized, heading.normalized) > 0.99999f)
			{
				return;
			}

			ApplyDeflection(heading);
			RebasePlaybackLeg(eventTick);
		}

		/// <summary>
		/// Applies a hit the server resolved, on a peer that does not resolve its own.
		/// </summary>
		/// <remarks>
		/// Idempotent through <see cref="ApplyHit"/>'s per-object hit set, so a message that
		/// duplicates a hit the receiver already predicted — the caster's own client, normally —
		/// does nothing.
        /// </remarks>
		/// <param name="hitCharacter">The character the server hit, or null for scenery.</param>
		/// <param name="point">World point of impact, as measured on the server.</param>
		/// <param name="normal">Surface normal at the impact.</param>
		internal void ApplyObservedHit(ICharacter hitCharacter, Vector3 point, Vector3 normal, uint eventTick = 0u)
		{
			if (destroyed)
			{
				return;
			}
			if (DefersObserved(eventTick))
			{
				DeferObserved(eventTick, ObservedEventKind.Hit, hitCharacter, point, normal);
				return;
			}

			/* Scenery has no networked identity to name, so a hit that resolved to none is keyed on
			 * this object itself: it runs the OnHit events once (an impact decal, a sound) and
			 * cannot run them twice.
			 *
			 * The bound that buys is ONE scenery hit per object on a peer that is told. Exact for
			 * every shipped ability, which all carry HitCount 1, and an under-report only for a
			 * pierce that passes through two separate pieces of scenery — it would draw the first
			 * impact and not the second.
			 *
			 * Keying on the impact POINT instead was the obvious alternative and does not work: the
			 * dedupe exists so that a peer which already predicted this hit absorbs the server's
			 * report of it, and the two peers resolve against different worlds, so their points
			 * differ by a little and every report would look like a new hit. A body is the only key
			 * both sides agree on, and scenery has none to send. */
			GameObject key = hitCharacter != null
				? hitCharacter.GameObject
				: (GameObject != null ? GameObject : gameObject);

			/* Vector3.zero for the local point, and it is never read: the volume gate above is
			 * skipped for an authoritative echo, because the peer that RESOLVED this hit already
			 * asked whether a shield stopped it. A hit that reaches here is one the server decided
			 * landed, and a receiver second-guessing that with its own copy of the defender's buffs
			 * is exactly the observer-resolves-its-own-hits failure the echo exists to remove. */
			ApplyHit(hitCharacter, key, point, normal, Vector3.zero, isAuthoritativeEcho: true);
		}

		/// <summary>
		/// Records a hit the server BLOCKED, on a peer that does not resolve its own.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The counterpart to <see cref="ApplyObservedHit"/> for the other kind of rejected hit.
		/// <see cref="ApplyObservedDeflection"/> gives the object a new heading and runs nothing;
		/// this runs nothing at all. A block is not an impact: the server executed no OnHit chain
		/// for it, spent no hit count and dealt no damage — it destroyed the object and played its
		/// destroy events, which is where the shield impact lives. The destroy reaches this peer on
		/// the same message, through <c>AbilityObjectHitBroadcast.Ended</c>.
		/// </para>
		/// <para>
		/// The victim still goes into the hit set, for the same reason a deflected one does: this
		/// object has had its answer about that body and must not be offered another one on every
		/// tick the two of them remain overlapping. It also absorbs the echo on the caster's own
		/// client, which resolved the same block locally and receives this message anyway.
		/// </para>
		/// </remarks>
		/// <param name="hitCharacter">The character whose shield ate this object, or null for scenery.</param>
		internal void ApplyObservedBlock(ICharacter hitCharacter, uint eventTick = 0u)
		{
			if (destroyed)
			{
				return;
			}
			if (DefersObserved(eventTick))
			{
				DeferObserved(eventTick, ObservedEventKind.Block, hitCharacter, default, default);
				return;
			}

			GameObject key = hitCharacter != null
				? hitCharacter.GameObject
				: (GameObject != null ? GameObject : gameObject);
			if (key == null)
			{
				return;
			}

			hitTargets ??= new HashSet<GameObject>();
			hitTargets.Add(key);
		}

		/// <summary>
		/// Runs this object's OnHit chain for one resolved impact.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Shared by every peer and by both ways an impact reaches this object, so the events an
		/// authored ability runs cannot depend on which of them resolved it. The tick is stamped
		/// AUTHORITATIVE (<see cref="GetCurrentAuthoritativeTick"/>): a hit is not part of any
		/// peer's replicate domain, so prediction-domain consumers must map it themselves.
		/// </para>
		/// </remarks>
		/// <param name="hitCharacter">The character that was hit, or null for scenery.</param>
		/// <param name="point">World point of impact.</param>
		/// <param name="normal">Surface normal at the impact.</param>
		/// <param name="isAuthoritativeEcho">
		/// True when the hit arrived from the server rather than being resolved here. Reaches the
		/// events on the collision payload; see
		/// <see cref="AbilityCollisionEventData.IsAuthoritativeEcho"/>.
		/// </param>
		/// <param name="singleTarget">
		/// For a hit claimed after the object ended: the chain acts on the victim alone. The chain already ran once, where the
		/// object ended, and an area in it was applied there; a second fan-out would apply the area again.
		/// </param>
		private void RunHitEvents(ICharacter hitCharacter, Vector3 point, Vector3 normal, bool isAuthoritativeEcho, bool singleTarget = false)
		{
			if (Caster == null || !Caster.IsSpawned)
			{
				return;
			}

			var hitEvents = OnHitEvents;
			if (hitEvents == null)
			{
				return;
			}

			uint collisionTick = GetCurrentAuthoritativeTick();

			foreach (var hitEvent in hitEvents.Values)
			{
				// The trigger's own TargetSelector handles fan-out.
				/* The echo flag travels with the event so an action can tell "I decided this"
				 * from "I was told this". ApplyDamageAction and ApplyHealAction are what read
				 * it; see AbilityCollisionEventData.IsAuthoritativeEcho. */
				AbilityCollisionEventData collisionEvent = new AbilityCollisionEventData(
					Caster, hitCharacter, this, point, normal, RNG, isAuthoritativeEcho);

				collisionEvent.Add(new TickEventData(Caster, collisionTick));
				if (singleTarget)
				{
					collisionEvent.Add(new SingleTargetEventData(Caster));
				}
				hitEvent.Execute(collisionEvent);
			}
		}

		/// <summary>
		/// Publishes one impact that an ECA ACTION resolved, rather than this object's own sweep.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <c>AbilityApplyHitscanAction</c> and <c>AbilityApplyAreaAction</c> run their own
		/// lag-compensated query and execute the OnHit chain directly, so they never reach
		/// <see cref="ApplyHit"/> — and until this existed, every hitscan shot and every blast was
		/// invisible to third parties. An observer saw the beam object appear and nothing whatever
		/// happen to the people it went through: no impact effect, no decal, no sound. Only a swept
		/// projectile's hits were ever published.
		/// </para>
		/// <para>
		/// <b>Not deduped and not counted</b>, which is why it carries its own flag on the wire
		/// rather than reusing the sweep's path. An area effect wired to OnTick pulses repeatedly
		/// over the same victims (a lingering trap is authored exactly that way), and a beam wired
		/// to OnTick does the same — the per-object hit set that makes a swept projectile spend one
		/// hit per body would collapse every pulse after the first into silence. The budget these
		/// spend is the action's own <c>MaxHits</c>, resolved per query, and it is not this
		/// object's <see cref="HitCount"/>.
		/// </para>
		/// <para>
		/// <b>To observers EXCEPT the owner</b>, unlike <see cref="BroadcastHitToObservers"/>. The
		/// owner resolves these itself (see <see cref="ResolvesHitsLocally"/>) and has already run
		/// the chain locally; with no hit set to absorb the echo, including the owner would play
		/// every impact twice on the screen of the player who fired.
		/// </para>
		/// </remarks>
		/// <param name="hitCharacter">The character that was hit, or null for scenery.</param>
		/// <param name="point">World point of impact.</param>
		/// <param name="normal">Surface normal at the impact.</param>
		internal void PublishActionHit(ICharacter hitCharacter, Vector3 point, Vector3 normal)
		{
			PublishedHitCount++;

			if (!isServer || Ability == null)
			{
				return;
			}

			NetworkObject casterNob = Caster?.NetworkObject;
			if (casterNob == null || !casterNob.IsSpawned)
			{
				return;
			}

			NetworkObject victimNob = hitCharacter?.NetworkObject;

			ObserverBroadcastScope.BroadcastToObserversExceptOwner(casterNob, new AbilityObjectHitBroadcast
			{
				CasterObjectID = casterNob.ObjectId,
				AbilityID = Ability.ID,
				ContainerID = ContainerID,
				ObjectID = ID,
				VictimObjectID = victimNob != null && victimNob.IsSpawned ? victimNob.ObjectId : 0,
				Point = point,
				Normal = normal,
				DirectImpact = true,
			}, Channel.Reliable);
		}

		/// <summary>
		/// Plays one action-resolved impact on a peer that was told about it.
		/// </summary>
		/// <remarks>
		/// The receiving half of <see cref="PublishActionHit"/>. It runs the OnHit chain and
		/// nothing else: no hit set, no mitigation, no hit count. Every one of those belongs to the
		/// peer that RESOLVED the impact, and this peer is by construction not that peer — the
		/// message is sent to observers only.
		/// </remarks>
		/// <param name="hitCharacter">The character that was hit, or null for scenery.</param>
		/// <param name="point">World point of impact.</param>
		/// <param name="normal">Surface normal at the impact.</param>
		internal void ApplyObservedActionHit(ICharacter hitCharacter, Vector3 point, Vector3 normal, uint eventTick = 0u)
		{
			if (destroyed)
			{
				return;
			}
			if (DefersObserved(eventTick))
			{
				DeferObserved(eventTick, ObservedEventKind.ActionHit, hitCharacter, point, normal);
				return;
			}

			RunHitEvents(hitCharacter, point, normal, isAuthoritativeEcho: true);
		}

		/// <summary>
		/// Publishes one server-resolved hit to the caster's observers.
		/// </summary>
		/// <remarks>
		/// Sent to every observer of the caster INCLUDING the owner, exactly like
		/// <see cref="BroadcastDestroyedToObservers"/> and for the same reason: the owner normally
		/// predicted this hit and the receiver's dedupe makes the message free, but an owner that
		/// mispredicted a MISS had no correction at all and its impact effect never played.
		/// Reliable, because there is no repeat behind it — a lost hit message is an impact nobody
		/// off the server ever sees.
		/// </remarks>
		/// <param name="hitCharacter">The character that was hit, or null for scenery.</param>
		/// <param name="point">World point of impact.</param>
		/// <param name="normal">Surface normal at the impact.</param>
		/// <param name="deflected">True when the victim turned this object away instead of being struck.</param>
		/// <param name="deflectHeading">The heading the object left on. Read only when <paramref name="deflected"/>.</param>
		/// <param name="blocked">True when the victim's shield ate this object instead of being struck.</param>
		/// <param name="ended">
		/// True when this hit ended the object, so no standalone destroy follows it. See
		/// <see cref="HitEndsObject"/> for why the answer has to be certain at this point.
		/// </param>
		private void BroadcastHitToObservers(ICharacter hitCharacter, Vector3 point, Vector3 normal,
			bool deflected = false, Vector3 deflectHeading = default,
			bool blocked = false, bool ended = false)
		{
			PublishedHitCount++;

			if (!isServer || Ability == null)
			{
				return;
			}

			NetworkObject casterNob = Caster?.NetworkObject;
			if (casterNob == null || casterNob.NetworkManager == null || !casterNob.IsSpawned)
			{
				return;
			}

			NetworkObject victimNob = hitCharacter?.NetworkObject;

			casterNob.NetworkManager.ServerManager.Broadcast(casterNob, new AbilityObjectHitBroadcast
			{
				CasterObjectID = casterNob.ObjectId,
				AbilityID = Ability.ID,
				ContainerID = ContainerID,
				ObjectID = ID,
				VictimObjectID = victimNob != null && victimNob.IsSpawned ? victimNob.ObjectId : 0,
				Point = point,
				Normal = normal,
				Deflected = deflected,
				PackedDeflectHeading = deflected ? AimDirectionCompression.Encode(deflectHeading) : 0u,
				Blocked = blocked,
				Ended = ended,
			}, true, Channel.Reliable);
		}

		/// <summary>
		/// Publishes this object's post-OnHit heading to the caster's observers. Server only; sent
		/// when the hit chain redirected the object. See <see cref="AbilityObjectRedirectBroadcast"/>.
		/// </summary>
		/// <remarks>
		/// The heading sent is the QUANTISED forward the redirect itself applied
		/// (<see cref="AbilityForkHitAction"/> quantises before <see cref="Redirect"/>), so what a
		/// receiver decodes is bit-identical to what the deciding peers simulate — the same
		/// invariant the deflect heading keeps.
		/// </remarks>
		private void BroadcastRedirectToObservers()
		{
			if (!isServer || Ability == null)
			{
				return;
			}

			NetworkObject casterNob = Caster?.NetworkObject;
			if (casterNob == null || casterNob.NetworkManager == null || !casterNob.IsSpawned)
			{
				return;
			}

			casterNob.NetworkManager.ServerManager.Broadcast(casterNob, new AbilityObjectRedirectBroadcast
			{
				CasterObjectID = casterNob.ObjectId,
				AbilityID = Ability.ID,
				ContainerID = ContainerID,
				ObjectID = ID,
				PackedHeading = AimDirectionCompression.Encode(SpawnRotation * Vector3.forward),
				/* The tick the turn happened on. Redirect resets the leg clock to zero, so a
				 * receiver that applied this message without advancing it restarted the new leg
				 * from the corner at the moment the message ARRIVED — leaving its copy a full
				 * transit delay behind the server's for the rest of the object's life. Every other
				 * observer message that starts a clock carries this for the same reason. */
				ServerTick = casterNob.TimeManager != null ? casterNob.TimeManager.Tick : 0u,
			}, true, Channel.Reliable);
		}

		/// <summary>
		/// Applies a redirect the server's hit chain resolved, on a peer that did not run it.
		/// </summary>
		/// <remarks>
		/// Compared before re-anchoring, because <see cref="Redirect"/> restarts the trajectory
		/// leg: a peer that already turned onto this exact heading (the owner's prediction, or an
		/// observer that resolved the hit locally) must treat the message as confirmation, not as
		/// a second turn that would reset its leg clock and jump the pose.
		/// </remarks>
		/// <param name="heading">The absolute post-chain heading, already decoded.</param>
		internal void ApplyObservedRedirect(Vector3 heading, uint catchUpTicks = 0u, uint eventTick = 0u)
		{
			if (destroyed || heading.sqrMagnitude < 1e-8f)
			{
				return;
			}
			if (DefersObserved(eventTick))
			{
				DeferObserved(eventTick, ObservedEventKind.Redirect, null, default, heading);
				return;
			}
			if (onPlaybackClock)
			{
				// On the playback clock the new leg's age comes from the clock (RebasePlaybackLeg), not from a catch-up.
				catchUpTicks = 0u;
			}

			Vector3 current = SpawnRotation * Vector3.forward;
			if (Vector3.Dot(current.normalized, heading.normalized) > 0.99999f)
			{
				/* Already on this heading: a peer that ran the fork itself. It turned at the tick
				 * the server did and its leg clock is already right, so advancing it here would
				 * push its copy AHEAD of the server's by the transit — the mirror of the bug the
				 * catch-up exists to remove. */
				return;
			}

			Redirect(AimDirectionCompression.ToRotation(AimDirectionCompression.Quantize(heading)));
			RebasePlaybackLeg(eventTick);

			/* The new leg is as old as the message. Redirect zeroed ElapsedTicks, and the
			 * closed-form trajectory reads it, so this is what puts the object where the server
			 * holds it rather than at the corner. Zero for a message that has just arrived — see
			 * AbilityController.ComputeObserverFastForwardTicks. */
			FastForward(catchUpTicks);
		}

		// ── Detached-phantom registry ──────────────────────────────────────────────
		//
		// Client-side only. A caster despawning from this client's view (an observer cull as much
		// as a real despawn) detaches its in-flight objects as visual phantoms; if the caster is
		// RE-observed, the spawn payload rematerialises the same objects as fresh, authoritative
		// copies. The registry lets that rematerialisation reclaim the matching phantoms first, so
		// the observer never renders two copies of one projectile — the phantom being unreachable
		// by every later hit/destroy broadcast (its container mapping died with the detach).

		/// <summary>Detached phantoms by (ability id, container id), for reclamation on re-observation.</summary>
		private static readonly Dictionary<(long abilityId, int containerId), List<AbilityObject>> detachedByContainer =
			new Dictionary<(long, int), List<AbilityObject>>();

		/// <summary>Scratch list so reclamation never iterates the live registry list it is emptying.</summary>
		private static readonly List<AbilityObject> reclaimBuffer = new List<AbilityObject>();

		/// <summary>True while this object is filed in <see cref="detachedByContainer"/>.</summary>
		private bool registeredDetached;

		/// <summary>Registry key halves, valid while <see cref="registeredDetached"/>.</summary>
		private long detachedAbilityId;
		private int detachedContainerId;

		/// <summary>
		/// Files this object as a detached phantom. Called by <c>Ability.DetachAllAbilityObjects</c>;
		/// no-op on the server, which detaches for pooling and never rematerialises from a payload.
		/// </summary>
		internal void RegisterDetached(long abilityId, int containerId)
		{
			if (isServer || destroyed || registeredDetached)
			{
				return;
			}
			registeredDetached = true;
			detachedAbilityId = abilityId;
			detachedContainerId = containerId;
			(long, int) key = (abilityId, containerId);
			if (!detachedByContainer.TryGetValue(key, out List<AbilityObject> list))
			{
				list = new List<AbilityObject>(1);
				detachedByContainer[key] = list;
			}
			list.Add(this);
		}

		/// <summary>Removes this object from the phantom registry, if present.</summary>
		private void UnregisterDetached()
		{
			if (!registeredDetached)
			{
				return;
			}
			registeredDetached = false;
			(long, int) key = (detachedAbilityId, detachedContainerId);
			if (detachedByContainer.TryGetValue(key, out List<AbilityObject> list))
			{
				list.Remove(this);
				if (list.Count == 0)
				{
					detachedByContainer.Remove(key);
				}
			}
		}

		/// <summary>
		/// Quietly destroys every detached phantom belonging to one cast, ahead of that cast being
		/// rematerialised from a spawn payload.
		/// </summary>
		/// <remarks>
		/// Quietly — these are EVICTIONS, not endings: the fresh copy about to spawn is the same
		/// projectile, authoritatively placed, and playing the phantom's authored OnDestroy effects
		/// here would detonate a projectile that is still in flight. The container id is a pure
		/// function of (seed, spawn tick), so the caller derives it from the payload entry alone.
		/// </remarks>
		internal static void ReclaimDetached(long abilityId, int containerId)
		{
			if (!detachedByContainer.TryGetValue((abilityId, containerId), out List<AbilityObject> list) ||
				list.Count == 0)
			{
				return;
			}

			reclaimBuffer.Clear();
			reclaimBuffer.AddRange(list);
			for (int i = 0; i < reclaimBuffer.Count; ++i)
			{
				AbilityObject phantom = reclaimBuffer[i];
				if (phantom != null && !phantom.destroyed)
				{
					// Unregisters itself via DestroyAbilityObjectInternal.
					phantom.DestroyAbilityObjectInternal(dispatchDestroyEvents: false);
				}
			}
			reclaimBuffer.Clear();
		}

		/// <summary>Clears the phantom registry on domain reload, like the prefab collider cache.</summary>
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ClearDetachedRegistryOnDomainReload()
		{
			detachedByContainer.Clear();
			reclaimBuffer.Clear();
		}

		/// <summary>
		/// Re-anchors the closed-form trajectory so this object carries on from where it is now, on a
		/// new heading.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Writing <c>Transform.rotation</c> alone does NOT steer an ability object, which is what
		/// <see cref="AbilityForkHitAction"/> used to do. The trajectory is a closed form evaluated from
		/// <see cref="SpawnPosition"/>, <see cref="SpawnRotation"/> and <see cref="ElapsedTicks"/>
		/// (<see cref="AbilityMoveTransformAction"/>), so the next tick simply recomputed the position
		/// from the ORIGINAL spawn line and overwrote the turn. Redirecting means starting a new leg:
		/// the three closed-form inputs and the sweep origin all have to move together, which is why
		/// this is one method rather than four assignments at the call site.
		/// </para>
		/// <para>
		/// <see cref="RemainingLifeTime"/> is deliberately untouched. A redirect changes where the
		/// object goes, not how long it lives — and lifetime expiry is the one end-of-life every peer
		/// reproduces without being told, so trimming it here would make that no longer true for a peer
		/// that did not resolve the hit which caused the fork. A peer that starts observing only AFTER
		/// the turn cannot re-derive the earlier legs from the leg-scoped counters, which is why they
		/// are preserved in <see cref="PreRedirectElapsedTicks"/> for the spawn payload.
		/// </para>
		/// <para>
		/// The hit set is not cleared either. The object is still overlapping whatever it just hit, so
		/// clearing it would let the new leg immediately re-resolve that same target and drain the hit
		/// count into it — the exact failure the per-lifetime set exists to prevent.
		/// </para>
		/// </remarks>
		/// <param name="rotation">The new heading. The closed form reads this from <see cref="SpawnRotation"/>.</param>
		internal void Redirect(Quaternion rotation)
		{
			if (destroyed)
			{
				return;
			}

			Transform shapeTransform = Transform != null ? Transform : transform;
			shapeTransform.rotation = rotation;

			SpawnPosition = shapeTransform.position;
			SpawnRotation = rotation;
			/* The finished leg's ticks move to the lifetime-only counter before the trajectory
			 * clock resets — see PreRedirectElapsedTicks for why the spawn payload needs them. */
			PreRedirectElapsedTicks += ElapsedTicks;
			ElapsedTicks = 0;
			/* The next sweep starts from the turn, not from where the previous leg last resolved.
			 * Without this the segment swept on the following tick spans the corner and reports
			 * everything inside the triangle the object never actually travelled through. */
			lastSweepPosition = SpawnPosition;
		}

		/// <summary>
		/// Advances this object's simulation clock without running per-tick events, so an
		/// observer that learns about a spawn late can place the object where the server has it.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The closed-form trajectory (<see cref="AbilityMoveTransformAction"/>) reads
		/// <see cref="ElapsedTicks"/>, so bumping the counter is enough to move the object on its
		/// next tick; the lifetime is advanced by the same amount so the object still expires on
		/// the server's schedule. An object fast-forwarded past its lifetime is destroyed quietly —
		/// it no longer exists on the server, so its destroy effects have already played.
		/// </para>
		/// <para>
		/// <b>Per-tick ECA events are deliberately NOT replayed</b> for the skipped ticks. Catching
		/// up a second of a channelled flame would fire a second of its OnTick chain in one frame:
		/// a stack of particle systems at every position the object passed through, and, for any
		/// authored tick action with a peer-visible effect, that effect repeated as many times as
		/// the message was late. What the observer owes is the object's POSE — where the server
		/// holds it now — and advancing the counter is the whole of that debt. The ticks it did not
		/// run are ticks whose visuals happened before this peer was watching.
		/// </para>
		/// </remarks>
		/// <param name="ticks">Ticks the server has already simulated for this object.</param>
		public void FastForward(uint ticks)
		{
			if (ticks == 0u || destroyed)
			{
				return;
			}

			ElapsedTicks += ticks;

			float totalLifeTime = TotalLifeTime;
			if (totalLifeTime > 0.0f)
			{
				RemainingLifeTime -= ticks * tickDelta;
				if (RemainingLifeTime <= 0.0f)
				{
					DestroyAbilityObjectInternal(dispatchDestroyEvents: false);
				}
			}
		}

		/// <summary>
		/// Charges ticks against this object's lifetime WITHOUT advancing its trajectory clock.
		/// </summary>
		/// <remarks>
		/// For a streamed object that was redirected before this client saw it: the in-flight
		/// payload anchors <see cref="FastForward"/> to the current leg so the pose lands where
		/// the server holds it, and this separately charges the legs flown before the turn — see
		/// <see cref="PreRedirectElapsedTicks"/>. Expiry here destroys quietly for the same
		/// reason FastForward's does: an object this far through its life may already be gone on
		/// the server, and its destroy effects have played there.
		/// </remarks>
		/// <param name="ticks">Lifetime ticks consumed on legs this client is not reproducing.</param>
		public void ConsumeLifetime(uint ticks)
		{
			if (ticks == 0u || destroyed)
			{
				return;
			}

			float totalLifeTime = TotalLifeTime;
			if (totalLifeTime > 0.0f)
			{
				RemainingLifeTime -= ticks * tickDelta;
				if (RemainingLifeTime <= 0.0f)
				{
					DestroyAbilityObjectInternal(dispatchDestroyEvents: false);
				}
			}
		}

		/// <summary>
		/// Destroys this ability object, dispatching OnDestroy events and cleaning up references.
		/// Uses the snapshot for OnDestroy events when the live Ability is unavailable.
		/// </summary>
		/// <param name="dispatchDestroyEvents">
		/// False to skip the OnDestroy ECA events. Used when the object is being <i>evicted</i>
		/// rather than ended — a container claimed by a different cast in
		/// <see cref="AbilityContainerAllocator"/>, or an observer fast-forwarded past the object's
		/// lifetime — where an explosion or proc would play at the wrong moment.
		/// </param>
		/// <param name="notifyObservers">
		/// True to tell this caster's observers to destroy their copy. Only meaningful on the
		/// server for an end-of-life that clients cannot reproduce deterministically (collision).
		/// </param>
		internal void DestroyAbilityObjectInternal(bool dispatchDestroyEvents = true, bool notifyObservers = false)
		{
			// Set-once guard: if another path (collision vs lifetime) already
			// set the flag, bail out immediately.
			if (destroyed) return;
			/* A copy whose whole life fell inside one frame (two ticks of a punch, across a hitch) is destroyed before it
			 * was ever drawn, and its spawn effect went with it. It did begin: the effect is played. Before the flag
			 * below, which is what ReleaseSpawnEffects reads to skip an anchor that is gone. */
			if (!isServer && heldSpawnEffects != null && heldSpawnEffects.Count > 0 && !observedLateJoin &&
				(!onPlaybackClock || presentedElapsed >= 0d || (timeManager != null && PlaybackTick(timeManager) >= playbackLegStartTick)))
			{
				ReleaseSpawnEffects();
			}
			destroyed = true;

			// A destroyed phantom leaves the reclamation registry, whatever destroyed it.
			UnregisterDetached();

			// Capture tick before unsubscribing — timeManager.LocalTick is unavailable
			// after the subscription is removed and the reference is nulled.
			uint destroyTick = GetCurrentAuthoritativeTick();

			/* Server: an object that ended WITHOUT spending its last hit on a character (on scenery, or by running out)
			 * stays answerable to a hit claim for a few ticks. The claim for a step arrives with the owner's NEXT input,
			 * and an object that flew a hand's breadth past its victim into the wall behind has already ended by then:
			 * the claim found nothing, and the hit the owner saw was lost. Kept here are only the things a claim needs
			 * (the ability for its OnHit chain, the caster, the clock); the object itself is finished, off the tick, out
			 * of the ability's containers, and destroyed when the grace runs out. */
			/* Server: an object that spent its last hit on a character is not kept, but what it hit is noted, so the owner's
			 * claim for that same hit (which arrives with its next input) is counted as agreed, not refused. */
			if (isServer && AbilityController.HitClaims && endedOnCharacter && hitTargets != null && hitTargets.Count > 0 &&
				Caster is IPlayerCharacter && Caster.NetworkObject != null)
			{
				AbilityController agreedController = Caster.NetworkObject.GetComponent<AbilityController>();
				agreedController?.NoteEndedOnCharacter(SpawnTick.Value, ID, hitTargets);
			}
			bool keepForClaims = isServer && AbilityController.HitClaims && !endedOnCharacter &&
				recordedSweeps != null && Ability != null && Caster is IPlayerCharacter && timeManager != null;
			if (keepForClaims)
			{
				endedAbility = Ability;
				endedCaster = Caster;
				endedTimeManager = timeManager;
				EndedOnTick = destroyTick;
				NetworkObject endedCasterNob = Caster.NetworkObject;
				AbilityController endedController = endedCasterNob != null ? endedCasterNob.GetComponent<AbilityController>() : null;
				if (endedController != null)
				{
					endedController.NoteEndedObject(this);
				}
				else
				{
					keepForClaims = false;
				}
			}

			if (notifyObservers)
			{
				BroadcastDestroyedToObservers();
			}

			// Unsubscribe from tick events before cleanup.
			if (timeManager != null)
			{
				timeManager.OnTick -= OnTick;
				timeManager = null;
			}

			// Dispatch OnDestroy events if the caster is still valid.
			var destroyEvents = dispatchDestroyEvents ? ActiveOnDestroyEvents : null;
			if (destroyEvents != null && Caster != null)
			{
				/* Carries the dying OBJECT, so an object-scoped action wired to OnDestroy can
				 * resolve it — a bare EventData made every one of them a silent no-op, which is
				 * what stopped "projectile detonates where it expired" from being authorable at
				 * all. The payload also sets Target to the object, so a selector on the trigger
				 * centres on the detonation rather than on the caster. See AbilityDestroyEventData. */
				AbilityDestroyEventData destroyEvent = new AbilityDestroyEventData(Caster, this);
				// Thread the raw authoritative destroy tick. TickEventData marks this as
				// non-replicate, so prediction-domain consumers must use an authoritative fallback.
				// Only added when a valid TimeManager tick was captured above.
				if (destroyTick != 0u)
				{
					destroyEvent.Add(new TickEventData(Caster, destroyTick));
				}
				// Thread the object's deterministic RNG so destroy ECA actions can roll
				// deterministic values (e.g. random loot drop, on-death proc effects).
				destroyEvent.RNG = RNG;
				foreach (var trigger in destroyEvents.Values)
				{
					trigger.Execute(destroyEvent);
				}
			}

			if (Ability != null)
			{
				Ability.RemoveAbilityObject(ContainerID, ID);
				Ability = null;
			}

			cachedTickEventData = null;
			Caster = null;
			if (!keepForClaims)
			{
				Snapshot = null;
			}
			GameObject go = GameObject != null ? GameObject : gameObject;
			go.SetActive(false);
			if (keepForClaims)
			{
				// Inactive from here: it draws nothing and runs nothing. Unity destroys it when the grace has passed.
				Destroy(go, (ClaimStepWindow + 2u) * tickDelta);
			}
			else
			{
				Destroy(go);
			}
		}

		/// <summary>
		/// Tells the caster's observers that this object ended on the server, so a client whose
		/// own collision test missed the hit does not keep a ghost flying.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Sent RELIABLY. This is the only correction for an end-of-life no client can reproduce, and
		/// it has no repeat behind it: a lost one is not a ghost that self-heals, it is a projectile
		/// that flies to the end of its lifetime and detonates in empty air well past the target it
		/// actually hit. It is one small message per collision-ended object, which is cheap enough to
		/// guarantee. (It was <c>Channel.Unreliable</c>, while
		/// <see cref="AbilityObjectDestroyedBroadcast"/> documented the reliable behaviour intended
		/// here — the doc was updated and the channel was not.)
		/// </para>
		/// <para>
		/// Reliable also fixes an ordering hazard: <c>AbilityActivatedBroadcast</c> is reliable, and
		/// the two channels have no ordering relationship to each other. For an ability that hits on
		/// its first or second tick the destroy could overtake the activation, find no container, and
		/// no-op — after which the activation arrived and spawned a copy nothing would ever kill.
		/// Both now ride the same ordered channel.
		/// </para>
		/// <para>
		/// Skipped once the object has been detached from its ability (rollback and disconnect
		/// paths null <see cref="Ability"/> first), since there is no container to name.
		/// </para>
		/// </remarks>
		private void BroadcastDestroyedToObservers()
		{
			if (!isServer || Ability == null)
			{
				return;
			}

			NetworkObject casterNob = Caster?.NetworkObject;
			if (casterNob == null || casterNob.NetworkManager == null || !casterNob.IsSpawned)
			{
				return;
			}

			casterNob.NetworkManager.ServerManager.Broadcast(casterNob, new AbilityObjectDestroyedBroadcast
			{
				CasterObjectID = casterNob.ObjectId,
				AbilityID = Ability.ID,
				ContainerID = ContainerID,
				ObjectID = ID,
			}, true, Channel.Reliable);
		}

		/// <summary>
		/// Returns the current authoritative tick — the live <see cref="TimeManager.LocalTick"/>,
		/// or 0 once the object has been unsubscribed.
		/// </summary>
		/// <remarks>
		/// This used to prefer <see cref="CharacterPredictionController.CurrentLocalTickSnapshot"/>
		/// but only when it equalled the live tick, which made it the live tick in every case; the
		/// snapshot is captured from the same <c>LocalTick</c> inside the same <c>OnTick</c>, so
		/// there is no drift to guard against here.
		/// </remarks>
		private uint GetCurrentAuthoritativeTick()
		{
			return timeManager != null ? timeManager.LocalTick : 0u;
		}

		/// <summary>
		/// Shared initialisation for a newly instantiated ability object.
		/// Sets common fields, registers the object in the ability's container dictionary
		/// using a deterministic container ID, dispatches pre-spawn/spawn events, and
		/// activates all spawned GameObjects.
		/// </summary>
		/// <param name="caster">
		/// The character who cast the ability. Typed as <see cref="ICharacter"/> so both
		/// PC (<see cref="IPlayerCharacter"/>) and NPC paths share this helper.
		/// The concrete runtime type is preserved — event handlers can recover
		/// <see cref="IPlayerCharacter"/> via <c>is</c>/<c>as</c> when needed.
		/// </param>
		/// <returns>
		/// The object that now represents this spawn: <paramref name="abilityObject"/> when it was
		/// initialised, the object already simulating this exact spawn when one was found (the
		/// newcomer is destroyed), or null when nothing could be initialised.
		/// </returns>
		private static AbilityObject InitializeAbilityObject(
			AbilityObject abilityObject,
			Ability ability,
			ICharacter caster,
			Transform abilitySpawner,
			TargetInfo targetInfo,
			int seed,
			PredictionTick spawnTick)
		{
			// Guard first to avoid mutating or subscribing a reused instance.
			if (abilityObject.initialized)
			{
				Log.Error("AbilityObject",
					$"InitializeAbilityObject: double-init detected for ability '{ability?.Template?.name}' "
					+ $"(ID {ability?.ID}). Destroying orphaned object.");
				Destroy(abilityObject.gameObject);
				return null;
			}

			/* Claim the container BEFORE any setup. SetupCoreFields subscribes the object to
			 * TimeManager.OnTick, and a duplicate spawn that is about to be abandoned must never
			 * be wired up — it would tick once before being collected. */
			if (!AbilityContainerAllocator.TryAllocate(ability, seed, spawnTick,
					out int containerID,
					out Dictionary<int, AbilityObject> spawnedAbilityObjects,
					out AbilityObject existingRoot))
			{
				/* This exact spawn (same seed, same tick) is already simulating. Keep it — it may
				 * already have been fast-forwarded to where the server holds it — and throw away
				 * the copy that just arrived. See AbilityContainerAllocator. */
				Destroy(abilityObject.gameObject);
				return existingRoot;
			}

			SetupCoreFields(abilityObject, ability, caster, seed, spawnTick);

			abilityObject.ContainerID = containerID;

			// Allocate the root object's ID from the shared counter so that
			// children spawned by events (e.g., AbilitySpawnMultiplyAction)
			// receive sequential IDs that never collide with the root.
			RefWrapper<int> nextChildID = new RefWrapper<int>(0);
			abilityObject.ID = nextChildID.Value++;
			spawnedAbilityObjects[abilityObject.ID] = abilityObject;

			DispatchSpawnEvents(ability, caster, abilitySpawner, targetInfo, seed, abilityObject, nextChildID, spawnedAbilityObjects);

			// Finalize activation of all spawned objects (initial and children)
			foreach (AbilityObject obj in spawnedAbilityObjects.Values)
			{
				obj.GameObject.SetActive(true);
			}

			return abilityObject;
		}

		/// <summary>
		/// Sets core fields, caches the <see cref="TimeManager"/>, and subscribes to
		/// <see cref="TimeManager.OnTick"/> for deterministic simulation.
		/// </summary>
		private static void SetupCoreFields(
			AbilityObject abilityObject,
			Ability ability,
			ICharacter caster,
			int seed,
			PredictionTick spawnTick)
		{
			// Awake has not run if the instance was inactive when the component was added.
			abilityObject.CacheComponents();

			abilityObject.initialized = true;
			abilityObject.Ability = ability;
			abilityObject.Caster = caster;
			abilityObject.HitCount = ability.Template.HitCount;
			abilityObject.RemainingLifeTime = ability.LifeTime;
			abilityObject.RNG = new DeterministicRNG(seed);
			abilityObject.SpawnTick = spawnTick;
			abilityObject.viewStepBase = spawnTick;
			abilityObject.SpawnSeed = seed;
			abilityObject.ElapsedTicks = 0;
			abilityObject.PreRedirectElapsedTicks = 0;
			// Spawn already positioned the transform (SetAbilitySpawnPosition runs before Initialize).
			Transform spawnTransform = abilityObject.Transform;
			abilityObject.SpawnPosition = spawnTransform.position;
			abilityObject.SpawnRotation = spawnTransform.rotation;
			/* The sweep's first segment starts where the object was spawned. Set alongside the spawn
			 * pose so the two can never disagree. */
			abilityObject.lastSweepPosition = abilityObject.SpawnPosition;
			abilityObject.hitTargets?.Clear();
			/* Falls back to the prefab's collider so an object whose component sits above or below the
			 * collider still sweeps its real shape rather than degrading to a ray. Resolved now
			 * because the template is reachable now: a detached object outlives its ability. */
			abilityObject.sweepShape ??= AbilityPrefabColliderCache.GetPrefabCollider(ability.Template);
			// Snapshot is lazily initialized: only created when the Ability reference is
			// about to be nulled (DetachAllAbilityObjects). This avoids 3 heap allocations
			// (3 Dictionary copies) per spawn for the common case where
			// the object is destroyed before the caster disconnects.
			abilityObject.Snapshot = null;
			abilityObject.destroyed = false;

			var timeManager = caster.NetworkObject?.TimeManager;
			if (timeManager == null)
			{
				throw new System.InvalidOperationException(
					"AbilityObject.Initialize: caster has no TimeManager. " +
					"Ability simulation requires deterministic TickDelta — caster must be spawned (per §3.2).");
			}
			abilityObject.timeManager = timeManager;
			abilityObject.tickDelta = (float)timeManager.TickDelta;
			abilityObject.isServer = timeManager.NetworkManager.IsServerStarted;
			timeManager.OnTick += abilityObject.OnTick;
			abilityObject.InitializePresentation();
		}

		/// <summary>
		/// Runs the ability's OnPreSpawn and OnSpawn ECA events for a freshly initialised object,
		/// threading the spawn tick and the object's deterministic RNG through the event data so
		/// prediction-aware actions use the replicate tick rather than a local clock.
		/// </summary>
		private static void DispatchSpawnEvents(
			Ability ability,
			ICharacter caster,
			Transform abilitySpawner,
			TargetInfo targetInfo,
			int seed,
			AbilityObject abilityObject,
			RefWrapper<int> nextChildID,
			Dictionary<int, AbilityObject> spawnedAbilityObjects)
		{
			bool hasPreSpawn = ability.OnPreSpawnEvents != null && ability.OnPreSpawnEvents.Count > 0;
			bool hasSpawn = ability.OnSpawnEvents != null && ability.OnSpawnEvents.Count > 0;

			if (!hasPreSpawn && !hasSpawn)
			{
				return;
			}

			AbilitySpawnEventData spawnEventData = new AbilitySpawnEventData(caster, ability, abilitySpawner, targetInfo, seed, abilityObject, nextChildID, spawnedAbilityObjects);

			/* The spawn tick is REPLICATE-DOMAIN, and that domain belongs to the owning client.
			 *
			 * On the server and on the owner it is exactly what prediction-aware actions want:
			 * ApplyBuffAction writes buff expiry against it, and both peers count in the same
			 * domain, so a reconcile lands on the same tick the prediction used. On a third-party
			 * OBSERVER reproducing this spawn from a broadcast, the same number is a foreign
			 * client's unsynchronised counter — and stamping it replicate-domain told
			 * ApplyBuffAction it had a same-character prediction tick, which took the direct-Apply
			 * path and installed a buff locally, at a tick from somebody else's clock, on the one
			 * peer that must never predict. The observer is handed its own authoritative tick
			 * instead, which routes through the target controller's domain mapper like every other
			 * cross-peer effect. Same predicate as ResolvesHitsOnThisPeer, and for the same reason. */
			NetworkObject spawnCasterNob = caster?.NetworkObject;
			bool spawnTickIsLocalDomain = ResolvesHitsOnThisPeer(
				spawnCasterNob != null && spawnCasterNob.IsServerInitialized,
				spawnCasterNob != null && spawnCasterNob.IsOwner);
			if (spawnTickIsLocalDomain)
			{
				spawnEventData.Add(new TickEventData(caster, abilityObject.SpawnTick));
			}
			else
			{
				uint observedSpawnTick = spawnCasterNob != null && spawnCasterNob.TimeManager != null
					? spawnCasterNob.TimeManager.LocalTick
					: 0u;
				spawnEventData.Add(new TickEventData(caster, observedSpawnTick));
			}
			// Thread the object's deterministic RNG so spawn ECA actions can roll
			// deterministic values using a shared, already-seeded generator.
			spawnEventData.RNG = abilityObject.RNG;

			/* Effects the chain plays are held on the root of the cast until its visuals are drawn (TryHoldSpawnEffect).
			 * A member spawned inside this chain dispatches its own chain inside this one: the outer root keeps holding. */
			AbilityObject outerHolder = spawnEffectHolder;
			if (outerHolder == null && abilityObject != null && !abilityObject.isServer)
			{
				spawnEffectHolder = abilityObject;
			}
			try
			{
				if (hasPreSpawn)
				{
					foreach (var trigger in ability.OnPreSpawnEvents.Values)
					{
						trigger.Execute(spawnEventData);
					}
				}

				if (hasSpawn)
				{
					foreach (var trigger in ability.OnSpawnEvents.Values)
					{
						trigger.Execute(spawnEventData);
					}
				}
			}
			finally
			{
				spawnEffectHolder = outerHolder;
			}
		}

		/// <summary>
		/// Creates a deterministic child RNG seed from the root spawn seed and child object ID.
		/// </summary>
		/// <param name="seed">The root spawn seed.</param>
		/// <param name="abilityObjectID">The child ability object ID.</param>
		/// <returns>A deterministic seed unique within the spawned container.</returns>
		internal static int CreateChildSeed(int seed, int abilityObjectID)
		{
			unchecked
			{
				return (seed * 397) ^ abilityObjectID;
			}
		}

		/// <summary>
		/// Fully initializes a child ability object spawned from an existing root object.
		/// The child shares the root container but receives its own deterministic RNG and tick subscription.
		/// </summary>
		/// <param name="abilityObject">The child ability object to initialize.</param>
		/// <param name="source">The source ability object being duplicated.</param>
		/// <param name="abilityObjectID">The child ID to assign within the shared container.</param>
		/// <param name="spawnedAbilityObjects">The container map tracking spawned objects for this ability activation.</param>
		/// <param name="seed">The root deterministic spawn seed.</param>
		internal static void InitializeSpawnedChildObject(
			AbilityObject abilityObject,
			AbilityObject source,
			int abilityObjectID,
			Dictionary<int, AbilityObject> spawnedAbilityObjects,
			int seed)
		{
			if (abilityObject == null || source == null || spawnedAbilityObjects == null)
			{
				return;
			}

			abilityObject.CacheComponents();

			abilityObject.ResetRuntimeState();
			abilityObject.ContainerID = source.ContainerID;
			abilityObject.ID = abilityObjectID;
			abilityObject.Ability = source.Ability;
			abilityObject.Caster = source.Caster;
			abilityObject.HitCount = source.HitCount;
			abilityObject.RemainingLifeTime = source.RemainingLifeTime;
			abilityObject.RNG = new DeterministicRNG(CreateChildSeed(seed, abilityObjectID));
			abilityObject.SpawnTick = source.SpawnTick;
			// The child's step zero is the source's current step: its first step pairs with the caster's next input.
			abilityObject.viewStepBase = source.StepInputTick;
			abilityObject.SpawnSeed = source.SpawnSeed;
			// A child's own pose, not the parent's: multiply/split actions place children at offsets
			// and rotations of their own, and the closed-form trajectory must start from there.
			abilityObject.SpawnPosition = abilityObject.Transform.position;
			abilityObject.SpawnRotation = abilityObject.Transform.rotation;
			// ResetRuntimeState seeded this from the transform before the child was placed; re-seed it
			// from the pose it will actually start travelling from.
			abilityObject.lastSweepPosition = abilityObject.SpawnPosition;
			/* Not inherited from the source. A child is a new object with its own hit budget, and
			 * copying the parent's set would make a fork unable to hit what the parent already had. */
			abilityObject.hitTargets?.Clear();
			// The child is a clone of the source's GameObject, so CacheComponents already found the
			// same collider; the source's shape covers a prefab-resolved one.
			abilityObject.sweepShape ??= source.sweepShape;
			abilityObject.ElapsedTicks = 0;
			abilityObject.Snapshot = source.Snapshot;
			abilityObject.isServer = source.isServer;
			// Snapshot is lazily initialized — children share the parent's lifecycle
			// and don't need their own eagerly-created snapshot.

			TimeManager timeManager = source.timeManager ?? source.Caster?.NetworkObject?.TimeManager;
			if (source.tickDelta <= 0.0f && timeManager == null)
			{
				throw new System.InvalidOperationException(
					"AbilityObject child clone: no source tickDelta and no TimeManager available. " +
					"Deterministic simulation requires a valid tick delta (per §3.2).");
			}
			abilityObject.timeManager = timeManager;
			abilityObject.tickDelta = source.tickDelta > 0.0f
				? source.tickDelta
				: (float)timeManager.TickDelta;
			abilityObject.initialized = true;

			if (timeManager != null)
			{
				timeManager.OnTick += abilityObject.OnTick;
			}
			abilityObject.InitializePresentation();
			// A fork is born on its source's clock, at the tick the source had reached.
			if (source.onPlaybackClock)
			{
				abilityObject.onPlaybackClock = true;
				abilityObject.playbackLegStartTick = unchecked(source.playbackLegStartTick + source.ElapsedTicks);
				abilityObject.presentedElapsed = 0d;
			}

			spawnedAbilityObjects[abilityObjectID] = abilityObject;
		}

		/// <summary>
		/// Spawns an ability object for any character type (PC or NPC). Handles pet summons,
		/// self-targets, and projectile/area spawning. Pet abilities are only supported for
		/// player characters; NPCs silently ignore them.
		/// </summary>
		/// <param name="ability">The ability to spawn.</param>
		/// <param name="caster">The character casting the ability.</param>
		/// <param name="abilitySpawner">The transform used as the spawn origin.</param>
		/// <param name="targetInfo">The targeting information for the ability.</param>
		/// <param name="seed">The deterministic RNG seed.</param>
		/// <param name="spawnTick">The replicate-input tick at which this object is being spawned, used for rollback.
		/// Must be sourced from <see cref="CharacterReplicateData.GetPredictionTick"/> to preserve type-safe tick sourcing.</param>
		/// <param name="pose">
		/// Pose to spawn at, when the caller already holds the authoritative one (an observer
		/// reproducing a broadcast). Null resolves it locally via <see cref="ResolveSpawnPose"/>.
		/// </param>
		/// <returns>
		/// The root object representing this spawn, or null when the ability spawns nothing (pet,
		/// self, no prefab, missing required target). When an identical spawn (same seed and tick)
		/// is already simulating, that existing object is returned and the new instance is
		/// discarded — see <see cref="AbilityContainerAllocator"/>.
		/// </returns>
		public static AbilityObject Spawn(Ability ability, ICharacter caster, Transform abilitySpawner, TargetInfo targetInfo,
			Vector3 aimOrigin, Vector3 aimDirection, int seed, PredictionTick spawnTick, AbilitySpawnPose? pose = null,
			AimConvergence.AimLaunch launch = default)
		{
			AbilityTemplate template = ability.Template;
			if (template == null)
			{
				return null;
			}

			/* A required target the CALLER had to resolve — but only when the caller is the peer
			 * that resolved it.
			 *
			 * A supplied pose means this is a reproduction: the server already found the target,
			 * already built the pose from it, and sent that pose. The target transform's only use
			 * below is producing the pose (AbilitySpawnTarget.Target reads targetInfo.HitPosition),
			 * so a reproduction needs nothing from it. Refusing here anyway is what made a
			 * RequiresTarget cast VANISH for any observer that could not see the victim — a
			 * character outside that client's streaming budget is not in Objects.Spawned, so the
			 * handler passed a null target and this dropped the whole spawn. The bolt was never
			 * drawn, and the destroy that ended it referred to an object that had never existed. */
			if (template.RequiresTarget && targetInfo.Target == null && pose == null)
			{
				return null;
			}

			// Pet abilities are only supported for player characters.
			if (template is PetAbilityTemplate petAbilityTemplate)
			{
				if (caster is IPlayerCharacter petOwner)
				{
					OnPetSummon?.Invoke(petAbilityTemplate, petOwner);
				}
				return null;
			}

			/* Self-target abilities spawn no world object and apply immediately.
			 *
			 * Each OnHitEvent's inherited Trigger.TargetSelector determines the final targets:
			 *   - InitiatorTargetSelector for self-buffs/self-heals
			 *   - AreaTargetSelector for PBAoE centered on the caster
			 *
			 * Dispatched by the SERVER and by the CASTER'S OWN CLIENT, the same predicate a swept
			 * projectile hit uses. This was server-only, and it was the largest remaining hole in
			 * predicted feedback: a self-buff or self-heal is the most immediate action a player
			 * takes, and nothing whatsoever happened on their screen until the reconcile arrived —
			 * no bar movement, no buff icon, no number.
			 *
			 * The "double-application" this used to guard against is the one the attribute ledger
			 * exists to prevent, and it is prevented: the owner applies the buff into its own
			 * controller (SimulatesBuffEffects is true for it) and separately receives an
			 * authoritative total that already contains the same contribution, and
			 * ModifierSource keys the two together so the residual install cannot count it twice.
			 * A predicted heal is overwritten rather than accumulated by the next resource push.
			 * Neither is a replay hazard either: ResolveTargetAndSpawn skips this whole call on a
			 * replayed tick, so the effect runs once per real tick per peer.
			 *
			 * NOTE: The caller (ResolveTargetAndSpawn) is responsible for advancing the
			 * deterministic seed after this method returns, keeping client/server RNG in sync. The
			 * generator below is seeded from that same reconciled seed, so both peers draw
			 * identically. */
			if (template.AbilitySpawnTarget == AbilitySpawnTarget.Self)
			{
				RunSelfTargetEffects(ability, caster, seed, spawnTick, isReplay: false);
				return null;
			}

			if (template.AbilityObjectPrefab == null)
			{
				return null;
			}

			GameObject go = Instantiate(template.AbilityObjectPrefab);
			SceneManager.MoveGameObjectToScene(go, caster.GameObject.scene);
			AbilitySpawnPose resolvedPose = pose ?? ResolveSpawnPose(caster, ability, abilitySpawner, targetInfo, aimOrigin, aimDirection, launch);
			go.transform.SetPositionAndRotation(resolvedPose.Position, resolvedPose.Rotation);
			go.SetActive(false);

			AbilityObject abilityObject = go.GetComponent<AbilityObject>();
			if (abilityObject == null)
			{
				abilityObject = go.AddComponent<AbilityObject>();
			}

			/* The returned object is not always the one instantiated above: an identical spawn
			 * already simulating is kept and this copy is destroyed, in which case the existing
			 * one comes back so the caller (an observer reproducing a broadcast) can still
			 * fast-forward it rather than silently losing the correction. */
			AbilityObject initialized = InitializeAbilityObject(abilityObject, ability, caster, abilitySpawner, targetInfo, seed, spawnTick);

			/* On a client the visuals start at the SPAWNER as this client draws it (the hand, wand or staff, wherever the
			 * body and its animation have it) and blend onto the simulated path (see the Presentation region). That holds
			 * for the predicting owner, whose body is drawn a tick behind where the pose was resolved, and for an observer,
			 * who is handed the pose and draws the caster from its transform stream. A copy on the playback clock measures
			 * the offset again when it first shows, which is when the drawn caster releases it. */
			if (initialized != null && !initialized.isServer && abilitySpawner != null &&
				(template.AbilitySpawnTarget == AbilitySpawnTarget.Spawner ||
				 template.AbilitySpawnTarget == AbilitySpawnTarget.SpawnerWithCameraRotation))
			{
				Vector3 handOffset = abilitySpawner.position - resolvedPose.Position;
				bool blend = handOffset.sqrMagnitude < MaximumHandOffset * MaximumHandOffset;
				if (ability.Objects != null &&
					ability.Objects.TryGetValue(initialized.ContainerID, out Dictionary<int, AbilityObject> members) &&
					members != null)
				{
					foreach (AbilityObject member in members.Values)
					{
						if (member == null)
						{
							continue;
						}
						member.presentationSpawner = abilitySpawner;
						if (blend)
						{
							member.SetPresentationOffset(handOffset);
						}
					}
				}
			}

			// What the spawn chain plays (a muzzle effect), now that where the visuals start is known.
			initialized?.ReleaseSpawnEffectsUnlessObserved();

			return initialized;
		}

		/// <summary>
		/// Runs a self-target ability's hit events on its caster: the whole of what such an ability does (it has no object).
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>On a replayed tick too</b> (<paramref name="isReplay"/>), which is the point of this being its own method.
		/// A spawn is skipped on replay, because a world object survives a correction and must not be made twice. A
		/// self-cast effect does NOT survive one: the correction for any tick before the cast sets the caster's buffs and
		/// resources back to the server's, which do not have it yet, and the replay then ran the cast's tick without
		/// putting it back. The owner's own heal or buff vanished a tick or two after it was cast and returned a round
		/// trip later, and a speed buff replayed those ticks at the wrong speed. On replay the event is marked
		/// <see cref="TickEventData.ReapplyOnly"/>, so only the actions that restore corrected state run
		/// (<see cref="BaseAction.ReappliesOnReplay"/>); presentation and one-off actions do not repeat.
		/// </para>
		/// </remarks>
		internal static void RunSelfTargetEffects(Ability ability, ICharacter caster, int seed, PredictionTick spawnTick, bool isReplay)
		{
			if (ability == null || caster == null || ability.OnHitEvents == null || ability.OnHitEvents.Count == 0)
			{
				return;
			}
			bool isServer = caster.NetworkObject?.NetworkManager?.IsServerStarted ?? false;
			bool casterIsOwner = caster.NetworkObject != null && caster.NetworkObject.IsOwner;
			if (!ResolvesHitsOnThisPeer(isServer, casterIsOwner))
			{
				return;
			}

			DeterministicRNG rng = new DeterministicRNG(seed);
			foreach (var hitEvent in ability.OnHitEvents.Values)
			{
				// The trigger's own TargetSelector handles fan-out (self / area / etc.).
				AbilityCollisionEventData collisionEvent = new AbilityCollisionEventData(caster, caster, null, rng);
				// Thread the spawn tick so prediction-aware ECA actions (e.g. ApplyBuffAction)
				// use the deterministic replicate tick rather than target.GetLocalTick().
				collisionEvent.Add(new TickEventData(caster, spawnTick, isReplay) { ReapplyOnly = isReplay });
				hitEvent.Execute(collisionEvent);
			}
		}

		/// <summary>
		/// On a REPLAYED tick that would have spawned this ability: its spawn chain's effects on the caster itself, again.
		/// </summary>
		/// <remarks>
		/// The object is not made again (it survives the correction), but what its spawn chain did to the caster does not: a
		/// guard buff a block raises as it spawns, a cost a shot pays before it leaves. The correction for an earlier tick sets
		/// those back, and without this the guard's icon came and went on every tick of a round trip and the cost was refunded
		/// for one. Run with <see cref="TickEventData.ReapplyOnly"/>, so only actions that restore corrected state run, and
		/// only on the caster (Trigger). There is no object, so a selector that needs one finds none and selects nothing.
		/// </remarks>
		internal static void RunSpawnEffectsOnReplay(Ability ability, ICharacter caster, Transform abilitySpawner, int seed, PredictionTick spawnTick)
		{
			if (ability == null || caster == null)
			{
				return;
			}
			bool hasPreSpawn = ability.OnPreSpawnEvents != null && ability.OnPreSpawnEvents.Count > 0;
			bool hasSpawn = ability.OnSpawnEvents != null && ability.OnSpawnEvents.Count > 0;
			if (!hasPreSpawn && !hasSpawn)
			{
				return;
			}
			AbilitySpawnEventData spawnEventData = new AbilitySpawnEventData(caster, ability, abilitySpawner, default, seed, null,
				new RefWrapper<int>(0), new Dictionary<int, AbilityObject>());
			spawnEventData.Add(new TickEventData(caster, spawnTick, true) { ReapplyOnly = true });
			spawnEventData.RNG = new DeterministicRNG(seed);
			try
			{
				if (hasPreSpawn)
				{
					foreach (var trigger in ability.OnPreSpawnEvents.Values)
					{
						trigger.Execute(spawnEventData);
					}
				}
				if (hasSpawn)
				{
					foreach (var trigger in ability.OnSpawnEvents.Values)
					{
						trigger.Execute(spawnEventData);
					}
				}
			}
			catch (Exception ex)
			{
				// A selector written for a live object: nothing is put back for it, and the next correction settles it.
				Log.Debug("AbilityObject", $"Replaying '{ability.Name}' spawn effects on the caster failed: {ex.Message}");
			}
		}

		/// <summary>
		/// The farthest the drawn hand may be from the simulated one and still be blended from, metres. Further apart
		/// than this the body was not smoothing a step, it was somewhere else (a teleport still catching up).
		/// </summary>
		private const float MaximumHandOffset = 3f;

		/// <summary>
		/// True when <see cref="Spawn"/> would create a world object for this template — i.e.
		/// there is something for an observer to reproduce.
		/// </summary>
		/// <remarks>
		/// Pet and self-target abilities create nothing for an observer to reproduce, so an
		/// activation broadcast for them would be received, decoded and discarded for nothing.
		/// Their effects reach an observer through the ordinary state paths instead (buff and
		/// resource broadcasts, pet spawns). Note this is about what an OBSERVER must be told; the
		/// caster's own client does dispatch a self-target ability's effects locally, for feedback.
		/// </remarks>
		public static bool SpawnsWorldObject(AbilityTemplate template)
		{
			return template != null &&
				template.AbilityObjectPrefab != null &&
				!(template is PetAbilityTemplate) &&
				template.AbilitySpawnTarget != AbilitySpawnTarget.Self;
		}

		/// <summary>
		/// Positions and rotates the ability object transform based on the spawn target type.
		/// See <see cref="ResolveSpawnPose"/>.
		/// </summary>
		public static void SetAbilitySpawnPosition(ICharacter caster, Ability ability, Transform abilitySpawner,
			TargetInfo targetInfo, Vector3 aimOrigin, Vector3 aimDirection, Transform abilityTransform, AimConvergence.AimLaunch launch = default)
		{
			AbilitySpawnPose pose = ResolveSpawnPose(caster, ability, abilitySpawner, targetInfo, aimOrigin, aimDirection, launch);
			abilityTransform.SetPositionAndRotation(pose.Position, pose.Rotation);
		}

		/// <summary>
		/// Where the ability spawner is in the SIMULATION, not where it is drawn.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The spawner (AbilitySpawnPoint) is a child of the character's graphical object (Smoothing), which FishNet's tick
		/// smoother holds behind the simulated root on the owner by the owner interpolation (a tick; two until 2026-11-14),
		/// and does not move on the server. During a tick the smoother leaves that lag in place
		/// (UniversalTickSmoother.OnPreTick caches, OnPostTick restores; the graphical rides along with the root in
		/// between), so the owner read the hand where it was DRAWN while the server read it where it IS: two predictions
		/// of the same cast starting that lag's movement apart (20 cm a tick for a running caster), and flying parallel
		/// lines from there.
		/// </para>
		/// <para>
		/// Re-expressed against the root as the graphical object rests on it (identity local pose, as every character
		/// prefab authors it), the result is the simulated pose on both peers: on the server the graphical is at rest
		/// already and nothing changes.
		/// </para>
		/// </remarks>
		public static void SimulatedSpawnerPose(ICharacter caster, Transform abilitySpawner, out Vector3 position, out Quaternion rotation)
		{
			position = abilitySpawner.position;
			rotation = abilitySpawner.rotation;

			Transform graphical = caster != null && caster.NetworkObject != null ? caster.NetworkObject.GetGraphicalObject() : null;
			SimulatedSpawnerPose(graphical, abilitySpawner, out position, out rotation);
		}

		/// <summary>
		/// The spawner's pose with <paramref name="graphical"/> put back at rest on its parent (identity local pose); the
		/// spawner's own pose when it is not under that graphical object.
		/// </summary>
		public static void SimulatedSpawnerPose(Transform graphical, Transform abilitySpawner, out Vector3 position, out Quaternion rotation)
		{
			position = abilitySpawner.position;
			rotation = abilitySpawner.rotation;
			if (graphical == null || graphical.parent == null || !abilitySpawner.IsChildOf(graphical))
			{
				return;
			}
			Transform root = graphical.parent;
			position = root.TransformPoint(graphical.InverseTransformPoint(position));
			rotation = root.rotation * (Quaternion.Inverse(graphical.rotation) * rotation);
		}

		/// <summary>
		/// How far along its launch line a projectile's simulation starts, metres: about as far in front of the body as a
		/// hand is held, so the visuals leave the spawner sideways onto the line and not backward along it.
		/// </summary>
		public const float LaunchLead = 0.5f;

		/// <summary>Hit buffer for <see cref="ApplyCover"/>, grown as needed.</summary>
		private static RaycastHit[] coverHits = new RaycastHit[TargetOrdering.QueryBufferSize(0)];

		/// <summary>
		/// Cover between the caster and the launch line: if the way from the eye to where the visuals join the line is
		/// blocked, the projectile starts at the eye, heading for that point, and so strikes whatever blocks it.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The launch line is the camera's, and a camera on the shoulder sees past a corner the body is behind. Simulated
		/// on that line unchecked, a projectile would leave the hand (as drawn), pass through the corner and carry on.
		/// The check is the body's own line of fire: from the eye, which the server derives, to the point on the launch
		/// line the visuals reach when their blend from the spawner ends. Blocked, the projectile takes THAT path from the
		/// start and its own sweep ends it on the cover; clear, the drawn projectile is on the simulated line before it
		/// reaches anything, and what the player sees is what happens.
		/// </para>
		/// <para>
		/// Scenery only. A character in the way is the sweep's business (it resolves against rewound characters, which a
		/// plain ray here would not), and the ray is cast in the layers the projectile itself collides with. Both peers
		/// cast it from the same replicated numbers against the same scenery, so they agree.
		/// </para>
		/// </remarks>
		private static void ApplyCover(ICharacter caster, Ability ability, Vector3 eye, Vector3 lineOrigin, Vector3 lineDirection,
			ref Vector3 position, ref Quaternion rotation)
		{
			if (caster == null || caster.GameObject == null)
			{
				return;
			}
			Vector3 join = lineOrigin + lineDirection * (LaunchLead + Mathf.Max(0f, ability.Speed) * HandBlendSeconds);
			Vector3 toJoin = join - eye;
			float distance = toJoin.magnitude;
			if (distance < 1e-3f)
			{
				return;
			}
			Vector3 direction = toJoin / distance;

			Collider prefabCollider = AbilityPrefabColliderCache.GetPrefabCollider(ability.Template);
			int mask = prefabCollider != null
				? AbilityObjectSweep.CollisionMaskForLayer(prefabCollider.gameObject.layer)
				: Physics.DefaultRaycastLayers;
			if (mask == 0)
			{
				return;
			}

			PhysicsScene physicsScene = caster.GameObject.scene.GetPhysicsScene();
			int count;
			while (true)
			{
				count = physicsScene.Raycast(eye, direction, coverHits, distance, mask, QueryTriggerInteraction.Ignore);
				if (!TargetOrdering.TryGrowQueryBuffer(ref coverHits, count))
				{
					break;
				}
			}

			bool blocked = false;
			for (int i = 0; i < count; ++i)
			{
				TargetOrdering.ResolveHitRoot(coverHits[i].collider, out ICharacter hitCharacter);
				if (hitCharacter == null)
				{
					blocked = true;
					break;
				}
			}
			if (!blocked)
			{
				return;
			}

			// From just in front of the eye, along the body's line of fire: the sweep finds the cover within a step or two.
			position = eye + direction * Mathf.Min(LaunchLead, distance * 0.5f);
			rotation = Quaternion.LookRotation(direction);
		}

		/// <summary>
		/// Resolves the world pose an ability object spawns with, from the spawn target type.
		/// Resolves motor position from KCC for PCs or transform for NPCs.
		/// </summary>
		/// <remarks>
		/// Run only where the caster's pose is authoritative or predicted — server and owner.
		/// Observers receive the result in <see cref="AbilityActivatedBroadcast"/>; see
		/// <see cref="AbilitySpawnPose"/> for why they must not call this themselves.
		/// </remarks>
		/// <param name="caster">The character casting the ability.</param>
		/// <param name="ability">The ability being spawned.</param>
		/// <param name="abilitySpawner">The transform acting as the spawn origin.</param>
		/// <param name="targetInfo">The targeting information.</param>
		/// <param name="aimOrigin">Aim origin replicated for the tick being simulated.</param>
		/// <param name="aimDirection">Aim direction replicated for the tick being simulated.</param>
		/// <param name="launch">
		/// The replicated launch line for the tick (<see cref="CharacterReplicateData.ConvergenceDistance"/> and the origin
		/// offsets), or not given (an NPC, or a tick the owner did not expect to spawn on): a projectile then launches
		/// from the eye along the aim.
		/// </param>
		public static AbilitySpawnPose ResolveSpawnPose(ICharacter caster, Ability ability, Transform abilitySpawner,
			TargetInfo targetInfo, Vector3 aimOrigin, Vector3 aimDirection, AimConvergence.AimLaunch launch = default)
		{
			// Resolve motor transform (KCC motor for PCs, regular transform for NPCs).
			IPlayerCharacter playerCaster = caster as IPlayerCharacter;
			Vector3 motorPosition = playerCaster != null
				? playerCaster.Motor.Transform.position
				: caster.Transform.position;
			Quaternion motorRotation = playerCaster != null
				? playerCaster.Motor.Transform.rotation
				: caster.Transform.rotation;

			Vector3 position = motorPosition;
			Quaternion rotation = motorRotation;

			/* Aim arrives as a parameter rather than being re-resolved from the caster.
			 *
			 * This used to call AbilityController.ResolveCameraData, which read the live
			 * KCCController or AIController. Both readings were wrong: a player's owner held its
			 * exact camera while the server and observers held the quantised one, so a
			 * deterministic spawn landed differently on each peer; and AIController disables itself
			 * off the server, so on every client an NPC resolved to origin-zero facing +Z. The
			 * caller passes the aim that was replicated for the tick being simulated, which is the
			 * only value all peers agree on. */
			Vector3 cameraPosition = aimOrigin;

			switch (ability.Template.AbilitySpawnTarget)
			{
				case AbilitySpawnTarget.Self:
				case AbilitySpawnTarget.PointBlank:
					position = motorPosition;
					rotation = motorRotation;
					break;
				case AbilitySpawnTarget.Target:
					position = targetInfo.HitPosition;
					rotation = caster.Transform.rotation;
					break;
				case AbilitySpawnTarget.Forward:
					{
						float distance = 0.0f;
						float height = 0.0f;
						Collider collider = AbilityPrefabColliderCache.GetPrefabCollider(ability.Template);
						if (collider != null)
						{
							// The caster's collider is live in the scene, so its bounds are real.
							if (caster.Collider != null)
							{
								distance += caster.Collider.bounds.extents.z;
								height += caster.Collider.bounds.extents.y;
							}
							/* The prefab's is NOT: it is the prefab asset's collider, never simulated,
							 * and its bounds are empty. Read as bounds, this term was zero — Punch and
							 * Orc Slam spawned centred on the caster's surface, half inside it, and an
							 * NPC that had stopped at the reach AIAbilityReach computed from the real
							 * shape hit nothing (reported 2026-09-07). Read the shape instead, through
							 * the same helper the reach uses. */
							Vector3 prefabHalfExtents = AbilityPrefabColliderCache.ResolveShapeHalfExtents(collider);
							distance += prefabHalfExtents.z;
							height += prefabHalfExtents.y;
						}
						/* A player's swing is laid along the AIM, from where the body stood when the aim was taken.
						 *
						 * The aim is the line from the eye at the start of the tick to the point under the crosshair
						 * (AimConvergence), and the body at the start of the tick is directly below that eye. A box laid
						 * out from there along the aim's heading therefore sits in the vertical plane the crosshair's point
						 * is in: a target under the crosshair, within reach, is in front of the swing by construction.
						 *
						 * It was laid from the body AFTER the tick's move, along the body's facing. Both are close to the
						 * aim's, and neither is it: the move shifts the box sideways by a tick of strafing (20 cm at a
						 * run), and the facing is the body's heading, which the heading offset holds a few degrees off the
						 * aim on purpose. An NPC has neither an eye record nor a crosshair and keeps its own pose. */
						Vector3 swingOrigin = motorPosition;
						Vector3 swingForward = caster.Transform.forward;
						Quaternion swingRotation = caster.Transform.rotation;
						if (playerCaster != null && playerCaster.Motor != null)
						{
							Vector3 up = playerCaster.Motor.CharacterUp;
							Vector3 aimHeading = Vector3.ProjectOnPlane(aimDirection, up);
							if (aimHeading.sqrMagnitude > 1e-8f)
							{
								swingForward = aimHeading.normalized;
								swingRotation = Quaternion.LookRotation(swingForward, up);
								swingOrigin = aimOrigin - up * CharacterAimOrigin.EyeHeight(playerCaster.Motor);
							}
						}
						Vector3 positionOffset = swingForward * distance;
						positionOffset.y += height;

						position = swingOrigin + positionOffset;
						rotation = swingRotation;
					}
					break;
				case AbilitySpawnTarget.Camera:
					{
						/* A metre out from the eye, looking along the aim. The rotation used to be taken toward a point a
						 * full range down the same line, which is the aim itself for any reach over a metre and its
						 * REVERSE for one under (a stationary object has no reach at all). */
						Vector3 cameraForward = aimDirection.sqrMagnitude > 1e-8f ? aimDirection.normalized : Vector3.forward;

						position = cameraPosition + cameraForward;
						rotation = Quaternion.LookRotation(cameraForward);
					}
					break;
				case AbilitySpawnTarget.Spawner:
					SimulatedSpawnerPose(caster, abilitySpawner, out position, out rotation);
					break;
				case AbilitySpawnTarget.SpawnerWithCameraRotation:
					{
						if (ability.Speed <= 0f)
						{
							/* Something that stays where it is cast (a flame held at the hand): at the spawner as the
							 * simulation has it, facing the aim. It never travels, so there is no line to put it on. */
							SimulatedSpawnerPose(caster, abilitySpawner, out position, out _);
							rotation = Quaternion.LookRotation(aimDirection.sqrMagnitude > 1e-8f ? aimDirection.normalized : Vector3.forward);
							break;
						}

						/* A projectile is SIMULATED on the launch line: the line through the crosshair (for a player; the
						 * aim from the eye for an NPC), which is where the player aimed it, at every range. It is only
						 * DRAWN leaving the spawner, by the presentation layer, which starts the visuals at the hand, wand
						 * or staff as that client draws it and brings them onto this line within HandBlendSeconds.
						 *
						 * It used to be simulated from the spawner itself, turned toward the crosshair's point. That lands
						 * exactly with the crosshair ON the target and low everywhere short of it, which is where a shot
						 * led ahead of a moving target has to pass; and a spawner that follows a cast animation is in a
						 * different place on every client and nowhere on the server. */
						AimConvergence.LaunchLine(cameraPosition, aimDirection, launch, out Vector3 lineOrigin, out Vector3 lineDirection);
						position = lineOrigin + lineDirection * LaunchLead;
						rotation = Quaternion.LookRotation(lineDirection);
						if (launch.IsGiven)
						{
							ApplyCover(caster, ability, cameraPosition, lineOrigin, lineDirection, ref position, ref rotation);
						}
					}
					break;
				default:
					break;
			}

			return new AbilitySpawnPose(position, rotation);
		}
	}
}