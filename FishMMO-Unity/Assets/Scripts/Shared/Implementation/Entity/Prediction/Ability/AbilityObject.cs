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
		/// The instance's own collider where there is one, so the sweep reads the live scale and
		/// rotation; <see cref="AbilityPrefabColliderCache"/> supplies the shape for an object whose
		/// prefab put the collider somewhere this component cannot reach. Resolved once at
		/// initialisation rather than per tick because it must outlive the ability: an orphaned
		/// object keeps flying after its caster disconnects, and by then there is no template left to
		/// ask.
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
			sweepShape ??= GetComponent<Collider>();
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
			ElapsedTicks = 0;
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
			if (isServer && LagCompensationTick.TryResolve(Caster, timeManager, out RewindTarget rewindTarget))
			{
				/* The caster is excluded — it fires from where it is, not where it was. The query is
				 * run eagerly inside the scope and the results are dispatched after it closes, so no
				 * damage or ECA action ever runs against a world several hundred milliseconds stale. */
				using (LagCompensationRegistry.Rewind(scene, rewindTarget, Caster))
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
				if (!DispatchSweptHit(sweepHits[i]))
				{
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
						/* Told as an ordinary hit rather than a deflection: the object ENDED here,
						 * and the destroy broadcast below is what an observer acts on. The point and
						 * normal are the shield's, so the impact plays on the shield face. */
						BroadcastHitToObservers(hitCharacter, point, normal);
					}
					/* Destroyed, not merely stopped: a blocked projectile is gone. Observers are told
					 * through the same reliable message that ends any collision, so nothing new has
					 * to travel for a block. Destroy events fire — an impact on a shield is exactly
					 * the moment an authored effect wants to play. */
					DestroyAbilityObjectInternal(dispatchDestroyEvents: true, notifyObservers: isServer);
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
			 * is told about a hit even if an authored action destroys the object while handling it. */
			if (isServer)
			{
				BroadcastHitToObservers(hitCharacter, point, normal);
			}

			if (Caster != null && Caster.IsSpawned)
			{
				var hitEvents = OnHitEvents;
				if (hitEvents != null)
				{
					// Thread the raw authoritative tick if available. TickEventData marks this as non-replicate,
					// so prediction-domain consumers must route it through their authoritative fallback.
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
						hitEvent.Execute(collisionEvent);
					}
				}
			}

			// An OnHit action is allowed to end this object (and AbilityHitCountAction is allowed to
			// extend it); either way the decision below must read the state the actions left behind.
			if (destroyed)
			{
				return false;
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
				/* A hit is the one end-of-life that is NOT deterministic across peers: it is
				 * resolved against the caster's world, which no observer holds, so the server tells
				 * them. Lifetime expiry is identical everywhere and needs no message. */
				DestroyAbilityObjectInternal(dispatchDestroyEvents: true, notifyObservers: true);
				return false;
			}

			return true;
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
		internal void ApplyObservedDeflection(Vector3 heading)
		{
			if (destroyed)
			{
				return;
			}
			/* Absolute, so applying it to a peer that already predicted the same deflection is a
			 * no-op rather than a second mirror — see AbilityObjectHitBroadcast.PackedDeflectHeading. */
			ApplyDeflection(heading);
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
		internal void ApplyObservedHit(ICharacter hitCharacter, Vector3 point, Vector3 normal)
		{
			if (destroyed)
			{
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
		private void BroadcastHitToObservers(ICharacter hitCharacter, Vector3 point, Vector3 normal,
			bool deflected = false, Vector3 deflectHeading = default)
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
			}, true, Channel.Reliable);
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
		/// that did not resolve the hit which caused the fork.
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
		/// The closed-form trajectory (<see cref="AbilityMoveTransformAction"/>) reads
		/// <see cref="ElapsedTicks"/>, so bumping the counter is enough to move the object on its
		/// next tick; the lifetime is advanced by the same amount so the object still expires on
		/// the server's schedule. An object fast-forwarded past its lifetime is destroyed quietly —
		/// it no longer exists on the server, so its destroy effects have already played.
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
			destroyed = true;

			// Capture tick before unsubscribing — timeManager.LocalTick is unavailable
			// after the subscription is removed and the reference is nulled.
			uint destroyTick = GetCurrentAuthoritativeTick();

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
				EventData destroyEvent = new EventData(Caster);
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
			Snapshot = null;
			GameObject go = GameObject != null ? GameObject : gameObject;
			go.SetActive(false);
			Destroy(go);
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
			abilityObject.SpawnSeed = seed;
			abilityObject.ElapsedTicks = 0;
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
			// Thread the spawn tick so prediction-aware ECA actions (e.g. ApplyBuffAction)
			// use the deterministic replicate tick rather than target.GetLocalTick().
			spawnEventData.Add(new TickEventData(caster, abilityObject.SpawnTick));
			// Thread the object's deterministic RNG so spawn ECA actions can roll
			// deterministic values using a shared, already-seeded generator.
			spawnEventData.RNG = abilityObject.RNG;

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
			Vector3 aimOrigin, Vector3 aimDirection, int seed, PredictionTick spawnTick, AbilitySpawnPose? pose = null)
		{
			AbilityTemplate template = ability.Template;
			if (template == null)
			{
				return null;
			}

			if (template.RequiresTarget && targetInfo.Target == null)
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

			// Self-target abilities don't spawn ability objects and instead apply immediately.
			// Effects are server-authoritative, same as projectile hits — the result reaches
			// the client via reconcile (resources/buffs) or broadcast. During client-side
			// prediction this path is skipped to avoid double-application.
			// Each OnHitEvent's inherited Trigger.TargetSelector determines the final targets:
			//   - InitiatorTargetSelector for self-buffs/self-heals
			//   - AreaTargetSelector for PBAoE centered on the caster
			// NOTE: The caller (ResolveTargetAndSpawn) is responsible for advancing the
			// deterministic seed after this method returns, keeping client/server RNG in sync.
			if (template.AbilitySpawnTarget == AbilitySpawnTarget.Self)
			{
				bool isServer = caster.NetworkObject?.NetworkManager?.IsServerStarted ?? false;
				if (isServer && ability.OnHitEvents != null && ability.OnHitEvents.Count > 0)
				{
					DeterministicRNG rng = new DeterministicRNG(seed);
					foreach (var hitEvent in ability.OnHitEvents.Values)
					{
						// The trigger's own TargetSelector handles fan-out (self / area / etc.).
						AbilityCollisionEventData collisionEvent = new AbilityCollisionEventData(caster, caster, null, rng);
						// Thread the spawn tick so prediction-aware ECA actions (e.g. ApplyBuffAction)
						// use the deterministic replicate tick rather than target.GetLocalTick().
						collisionEvent.Add(new TickEventData(caster, spawnTick));
						hitEvent.Execute(collisionEvent);
					}
				}
				return null;
			}

			if (template.AbilityObjectPrefab == null)
			{
				return null;
			}

			GameObject go = Instantiate(template.AbilityObjectPrefab);
			SceneManager.MoveGameObjectToScene(go, caster.GameObject.scene);
			AbilitySpawnPose resolvedPose = pose ?? ResolveSpawnPose(caster, ability, abilitySpawner, targetInfo, aimOrigin, aimDirection);
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
			return InitializeAbilityObject(abilityObject, ability, caster, abilitySpawner, targetInfo, seed, spawnTick);
		}

		/// <summary>
		/// True when <see cref="Spawn"/> would create a world object for this template — i.e.
		/// there is something for an observer to reproduce.
		/// </summary>
		/// <remarks>
		/// Pet and self-target abilities apply on the server and reach clients through their own
		/// paths (pet broadcasts, reconcile/resource broadcasts), so an activation broadcast for
		/// them would be received, decoded and discarded by every observer for nothing.
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
			TargetInfo targetInfo, Vector3 aimOrigin, Vector3 aimDirection, Transform abilityTransform)
		{
			AbilitySpawnPose pose = ResolveSpawnPose(caster, ability, abilitySpawner, targetInfo, aimOrigin, aimDirection);
			abilityTransform.SetPositionAndRotation(pose.Position, pose.Rotation);
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
		public static AbilitySpawnPose ResolveSpawnPose(ICharacter caster, Ability ability, Transform abilitySpawner,
			TargetInfo targetInfo, Vector3 aimOrigin, Vector3 aimDirection)
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
							if (caster.Collider != null)
							{
								distance += caster.Collider.bounds.extents.z;
								height += caster.Collider.bounds.extents.y;
							}
							distance += collider.bounds.extents.z;
							height += collider.bounds.extents.y;
						}
						Vector3 positionOffset = caster.Transform.forward * distance;
						positionOffset.y += height;

						position = motorPosition + positionOffset;
						rotation = caster.Transform.rotation;
					}
					break;
				case AbilitySpawnTarget.Camera:
					{
						Vector3 cameraForward = aimDirection;

						Vector3 spawnPosition = cameraPosition + cameraForward;

						Vector3 farTargetPosition = cameraPosition + cameraForward * ability.Range;

						Vector3 lookDirection = (farTargetPosition - spawnPosition).normalized;

						position = spawnPosition;
						rotation = Quaternion.LookRotation(lookDirection);
					}
					break;
				case AbilitySpawnTarget.Spawner:
					position = abilitySpawner.position;
					rotation = abilitySpawner.rotation;
					break;
				case AbilitySpawnTarget.SpawnerWithCameraRotation:
					{
						Vector3 cameraForward = aimDirection;

						Vector3 farTargetPosition = cameraPosition + cameraForward * ability.Range;

						Vector3 lookDirection = (farTargetPosition - abilitySpawner.position).normalized;

						position = abilitySpawner.position;
						rotation = Quaternion.LookRotation(lookDirection);
					}
					break;
				default:
					break;
			}

			return new AbilitySpawnPose(position, rotation);
		}
	}
}