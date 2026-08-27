using UnityEngine;
using System.Collections.Generic;
using System;
using FishNet.Managing.Timing;
using FishNet.Object;
using FishNet.Transporting;
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
		/// Cached reference to the caster's CharacterPredictionController, if available.
		/// Used to read the current replicate tick snapshot in a subscription-order safe way.
		/// </summary>
		private CharacterPredictionController predictionController;
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
		/// server broadcasts. Clients still see the projectile, decrement hit counts, and
		/// destroy the visual object on collision — but the gameplay effects are server-only.
		/// </summary>
		private bool isServer;

		/// <summary>
		/// True when this ability object is simulating on the server. Gameplay effects
		/// (damage, healing, buffs, area queries) are only applied where this is true.
		/// </summary>
		public bool IsServer => isServer;

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
			predictionController = null;
			ElapsedTicks = 0;

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
		/// Unity OnCollisionEnter callback. Handles collision logic, event dispatch, and destruction.
		/// Gameplay-affecting effects (damage, healing, buffs) are only dispatched on the server.
		/// Clients still see the collision (VFX, projectile destruction, hit-count drain) but
		/// do not apply state changes to other characters — those arrive via server broadcasts.
		/// Each <see cref="AbilityOnHitEvent"/> is executed independently; its inherited
		/// <see cref="Trigger.TargetSelector"/> selects the final targets (defaulting to the direct
		/// collision target when an InitiatorTargetSelector or null is configured).
		/// </summary>
		/// <param name="collision">The collision data from Unity.</param>
		void OnCollisionEnter(Collision collision)
		{
			if (destroyed) return;

			// If we have no ability data at all, the object has no collision logic. Destroy it.
			if (Ability == null && Snapshot == null)
			{
				DestroyAbilityObjectInternal();
				return;
			}

			// Only the server dispatches collision events (damage, healing, buffs).
			// Clients skip effect dispatch to avoid visual fighting between predicted
			// state changes and authoritative server broadcasts.
			if (isServer && Caster != null && Caster.IsSpawned)
			{
				var hitEvents = OnHitEvents;
				if (hitEvents != null)
				{
					collision.gameObject.TryGetComponent(out ICharacter hitCharacter);

					foreach (var hitEvent in hitEvents.Values)
					{
						// The trigger's own TargetSelector handles fan-out.
						AbilityCollisionEventData collisionEvent = new AbilityCollisionEventData(Caster, hitCharacter, this, collision, RNG);
						// Thread the raw authoritative tick if available. TickEventData marks this as non-replicate,
						// so prediction-domain consumers must route it through their authoritative fallback.
						uint collisionTick = GetCurrentAuthoritativeTick();

						collisionEvent.Add(new TickEventData(Caster, collisionTick));
						hitEvent.Execute(collisionEvent);
					}
				}
			}

			// Decrement hit count and destroy if exhausted.
			// NOTE: HitCount decrements even for orphaned objects (Ability == null,
			// Snapshot != null) so they drain via collision rather than persisting
			// indefinitely as invulnerable ghosts.
			HitCount--;
			if (HitCount < 1)
			{
				/* Collision is the one end-of-life that is NOT deterministic across peers: a
				 * client resolves it against interpolated characters and can miss a hit the
				 * server landed, leaving a ghost flying to the end of its lifetime. Lifetime
				 * expiry is identical everywhere and needs no message. */
				DestroyAbilityObjectInternal(dispatchDestroyEvents: true, notifyObservers: true);
			}
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
		/// False to skip the OnDestroy ECA events. Used when the object is being <i>replaced</i>
		/// rather than ended — a duplicate spawn superseded by <see cref="AbilityContainerAllocator"/>,
		/// or an observer fast-forwarded past the object's lifetime — where an explosion or proc
		/// would play at the wrong moment.
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
		/// Sent unreliably: a lost message costs one observer a ghost that still expires on its
		/// own lifetime, and the message is cheap enough to send for every collision-ended object.
		/// Skipped once the object has been detached from its ability (rollback and disconnect
		/// paths null <see cref="Ability"/> first), since there is no container to name.
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
			}, true, Channel.Unreliable);
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
		private static void InitializeAbilityObject(
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
				return;
			}

			SetupCoreFields(abilityObject, ability, caster, seed, spawnTick);

			if (ability.Objects == null)
			{
				ability.Objects = new Dictionary<int, Dictionary<int, AbilityObject>>();
			}

			AbilityContainerAllocator.Allocate(ability, seed, spawnTick, out int containerID, out Dictionary<int, AbilityObject> spawnedAbilityObjects);

			ability.Objects.Add(containerID, spawnedAbilityObjects);
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
			// Cache the caster's prediction controller (if present) so this object
			// can read the replicate-domain tick in a subscription-order safe way.
			abilityObject.predictionController = caster.NetworkObject?.GetComponent<CharacterPredictionController>();
			timeManager.OnTick += abilityObject.OnTick;
		}

		/// <summary>
		/// Allocates a deterministic container ID for the spawned ability object.
		/// Derives the first candidate from seed and spawnTick, then probes linearly when
		/// the slot is occupied by a different active spawn. A same seed+tick entry is a
		/// duplicate retry and is destroyed/replaced; a different seed+tick entry is a real
		/// hash collision and must remain alive.
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
			abilityObject.ElapsedTicks = 0;
			abilityObject.Snapshot = source.Snapshot;
			abilityObject.predictionController = source.predictionController;
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
		/// <returns>The spawned root object, or null when the ability spawns nothing (pet, self, no prefab, missing required target).</returns>
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
						AbilityCollisionEventData collisionEvent = new AbilityCollisionEventData(caster, caster, null, null, rng);
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

			InitializeAbilityObject(abilityObject, ability, caster, abilitySpawner, targetInfo, seed, spawnTick);
			return abilityObject.initialized ? abilityObject : null;
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