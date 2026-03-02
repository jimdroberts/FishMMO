using UnityEngine;
using System.Collections.Generic;
using System;
using SceneManager = UnityEngine.SceneManagement.SceneManager;

namespace FishMMO.Shared
{

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
		/// Number of hits this object can perform before being destroyed.
		/// </summary>
		public int HitCount;
		/// <summary>
		/// Remaining lifetime in seconds before the object is destroyed.
		/// </summary>
		public float RemainingLifeTime;
		/// <summary>
		/// The network tick at which this ability object was spawned.
		/// Used by the rollback system to identify predicted objects that need to be destroyed on reconcile mismatch.
		/// </summary>
		public uint SpawnTick;

		/// <summary>
		/// Random number generator for ability effects.
		/// </summary>
		public System.Random RNG;

		/// <summary>
		/// Immutable snapshot of the ability data captured at spawn time.
		/// Used as a fallback when the live <see cref="Ability"/> reference becomes null
		/// (e.g., after the owning character disconnects and the ability is detached).
		/// </summary>
		public AbilityObjectSnapshot Snapshot;

		/// <summary>
		/// Effective speed for this ability object's movement effects.
		/// Prefers the live Ability, falls back to the Snapshot.
		/// </summary>
		public float Speed => Ability != null ? Ability.Speed : (Snapshot != null ? Snapshot.Speed : 0f);

		/// <summary>
		/// Total configured lifetime. Prefers the live Ability, falls back to the Snapshot.
		/// </summary>
		public float TotalLifeTime => Ability != null ? Ability.LifeTime : (Snapshot != null ? Snapshot.LifeTime : 0f);

		/// <summary>
		/// OnHit events for collision dispatching. Prefers the live Ability, falls back to the Snapshot.
		/// </summary>
		public Dictionary<int, AbilityOnHitEvent> OnHitEvents => Ability != null ? Ability.OnHitEvents : Snapshot?.OnHitEvents;

		/// <summary>
		/// Cached tick event data instance to avoid per-frame allocation.
		/// </summary>
		private AbilityTickEventData cachedTickEventData;

		/// <summary>
		/// Cached reference to the object's GameObject.
		/// </summary>
		public GameObject GameObject { get; private set; }
		/// <summary>
		/// Cached reference to the object's Transform.
		/// </summary>
		public Transform Transform { get; private set; }

		/// <summary>
		/// Unity Awake callback. Caches references and sets Rigidbody to kinematic if present.
		/// </summary>
		private void Awake()
		{
			GameObject = gameObject;
			Transform = transform;
			CachedRigidBody = GetComponent<Rigidbody>();
			if (CachedRigidBody != null)
			{
				CachedRigidBody.isKinematic = true;
			}
		}

		/// <summary>
		/// Unity Update callback. Handles ticking, event dispatch, and lifetime management.
		/// Ability objects persist even if the caster disconnects or the Ability is detached.
		/// They continue counting down lifetime but skip ECA events that require a live caster,
		/// since <see cref="Trigger.Execute"/> rejects null initiators.
		/// </summary>
		void Update()
		{
			// If both the ability reference and snapshot are gone, this object is truly orphaned.
			if (Ability == null && Snapshot == null)
			{
				DestroyAbilityObjectInternal();
				return;
			}

			float totalLifeTime = TotalLifeTime;

			// Update remaining lifetime if the ability has a positive lifetime.
			if (totalLifeTime > 0.0f)
			{
				RemainingLifeTime -= Time.deltaTime;
			}

			// Dispatch OnTick events only if the caster is still valid.
			// If the caster disconnected, the object keeps existing but skips ECA dispatching
			// since Trigger.Execute rejects null initiators.
			var tickEvents = Ability?.OnTickEvents ?? Snapshot?.OnTickEvents;
			if (tickEvents != null && Caster != null && Caster.IsSpawned)
			{
				if (cachedTickEventData == null)
				{
					cachedTickEventData = new AbilityTickEventData(Caster, Transform, Time.deltaTime, this);
				}
				else
				{
					cachedTickEventData.DeltaTime = Time.deltaTime;
				}

				foreach (var trigger in tickEvents.Values)
				{
					trigger.Execute(cachedTickEventData);
				}
			}

			// If lifetime expired, destroy.
			if (totalLifeTime > 0.0f && RemainingLifeTime < 0.0f)
			{
				DestroyAbilityObjectInternal();
				return;
			}
			else if (totalLifeTime <= 0.0f)
			{
				DestroyAbilityObjectInternal();
				return;
			}
		}

		/// <summary>
		/// Unity OnCollisionEnter callback. Handles collision logic, event dispatch, and destruction.
		/// If the caster has disconnected, collision events are skipped but hit counting still applies.
		/// Uses the snapshot for the TargetTrigger reference when the live Ability is unavailable.
		/// </summary>
		/// <param name="collision">The collision data from Unity.</param>
		void OnCollisionEnter(Collision collision)
		{
			// Resolve the target trigger from the live ability or the snapshot.
			AbilityEvent targetTrigger = Ability?.Template?.TargetTrigger ?? Snapshot?.TargetTrigger;

			// If we have no trigger at all, the object has no collision logic. Destroy it.
			if (Ability == null && Snapshot == null)
			{
				DestroyAbilityObjectInternal();
				return;
			}

			// Only dispatch collision events if the caster is still valid.
			// A disconnected caster means ECA actions (damage, buffs) cannot resolve an initiator,
			// so we gracefully skip event dispatch while still consuming the hit.
			if (targetTrigger != null && Caster != null && Caster.IsSpawned)
			{
				ICharacter hitCharacter = collision.gameObject.GetComponent<ICharacter>();

				AbilityCollisionEventData collisionEvent = new AbilityCollisionEventData(Caster, hitCharacter, this, collision);
				collisionEvent.Add(new CharacterHitEventData(Caster, hitCharacter, RNG));

				targetTrigger.Execute(collisionEvent);
			}

			// Check if object should be destroyed after hits.
			if (HitCount < 1)
			{
				DestroyAbilityObjectInternal();
			}
		}

		/// <summary>
		/// Destroys this ability object, dispatching OnDestroy events and cleaning up references.
		/// Uses the snapshot for OnDestroy events when the live Ability is unavailable.
		/// </summary>
		internal void DestroyAbilityObjectInternal()
		{
			// Dispatch OnDestroy events if the caster is still valid.
			var destroyEvents = Ability?.OnDestroyEvents ?? Snapshot?.OnDestroyEvents;
			if (destroyEvents != null && Caster != null)
			{
				EventData destroyEvent = new EventData(Caster);
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
			GameObject.SetActive(false);
			Destroy(GameObject);
		}

		/// <summary>
		/// Handles primary spawn functionality for all ability objects. Returns true if successful.
		/// </summary>
		/// <param name="ability">The ability to spawn.</param>
		/// <param name="caster">The player character casting the ability.</param>
		/// <param name="abilitySpawner">The transform used as the spawn origin.</param>
		/// <param name="targetInfo">The targeting information for the ability.</param>
		/// <param name="seed">The deterministic RNG seed.</param>
		/// <param name="spawnTick">The network tick at which this object is being spawned, used for rollback.</param>
		public static void Spawn(Ability ability, IPlayerCharacter caster, Transform abilitySpawner, TargetInfo targetInfo, int seed, uint spawnTick)
		{
			AbilityTemplate template = ability.Template;
			if (template == null)
			{
				return;
			}

			if (template.RequiresTarget && targetInfo.Target == null)
			{
				return;
			}

			PetAbilityTemplate petAbilityTemplate = template as PetAbilityTemplate;
			if (petAbilityTemplate != null)
			{
				OnPetSummon?.Invoke(petAbilityTemplate, caster);
				return;
			}

			// Self target abilities don't spawn ability objects and instead apply immediately
			if (template.AbilitySpawnTarget == AbilitySpawnTarget.Self)
			{
				if (template.TargetTrigger != null)
				{
					// Create a collision event for self-target abilities
					AbilityCollisionEventData collisionEvent = new AbilityCollisionEventData(caster, caster);
					collisionEvent.Add(new CharacterHitEventData(caster, caster, new System.Random(seed)));
					template.TargetTrigger.Execute(collisionEvent);
				}
				return;
			}

			if (template.AbilityObjectPrefab == null)
			{
				return;
			}

			GameObject go = Instantiate(template.AbilityObjectPrefab);
			SceneManager.MoveGameObjectToScene(go, caster.GameObject.scene);
			SetAbilitySpawnPosition(caster, ability, abilitySpawner, targetInfo, go.transform);
			go.SetActive(false);

			AbilityObject abilityObject = go.GetComponent<AbilityObject>();
			if (abilityObject == null)
			{
				abilityObject = go.AddComponent<AbilityObject>();
			}
			abilityObject.ID = 0;
			abilityObject.Ability = ability;
			abilityObject.Caster = caster;
			abilityObject.HitCount = template.HitCount;
			abilityObject.RemainingLifeTime = ability.LifeTime;
			abilityObject.RNG = new System.Random(seed);
			abilityObject.SpawnTick = spawnTick;
			abilityObject.Snapshot = new AbilityObjectSnapshot(ability);

			if (ability.Objects == null)
			{
				ability.Objects = new Dictionary<int, Dictionary<int, AbilityObject>>();
			}

			Dictionary<int, AbilityObject> spawnedAbilityObjects = new Dictionary<int, AbilityObject>();
			int containerID;
			do
			{
				containerID = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
			} while (ability.Objects.ContainsKey(containerID));

			ability.Objects.Add(containerID, spawnedAbilityObjects);
			abilityObject.ContainerID = containerID;
			spawnedAbilityObjects[abilityObject.ID] = abilityObject; // Add the initial object to the map

			RefWrapper<int> nextChildID = new RefWrapper<int>(0); // Start ID for child objects

			// Dispatch Pre-Spawn Events
			if (ability.OnPreSpawnEvents != null)
			{
				AbilitySpawnEventData preSpawnEvent = new AbilitySpawnEventData(caster, ability, abilitySpawner, targetInfo, seed, abilityObject, nextChildID, spawnedAbilityObjects);
				foreach (var trigger in ability.OnPreSpawnEvents.Values)
				{
					trigger.Execute(preSpawnEvent);
				}
			}

			// Dispatch Spawn Events
			if (ability.OnSpawnEvents != null)
			{
				AbilitySpawnEventData spawnEvent = new AbilitySpawnEventData(caster, ability, abilitySpawner, targetInfo, seed, abilityObject, nextChildID, spawnedAbilityObjects);
				foreach (var trigger in ability.OnSpawnEvents.Values)
				{
					trigger.Execute(spawnEvent);
				}
			}

			// Finalize activation of all spawned objects (initial and children)
			foreach (AbilityObject obj in spawnedAbilityObjects.Values)
			{
				obj.GameObject.SetActive(true);
			}
		}

		/// <summary>
		/// Positions and rotates the ability object transform based on the spawn target type.
		/// Reads camera data from the caster's KCCController, which is guaranteed fresh because
		/// AbilityController runs on OnPostTick after KCCPlayer processes on OnTick.
		/// </summary>
		/// <param name="caster">The player character casting the ability.</param>
		/// <param name="ability">The ability being spawned.</param>
		/// <param name="abilitySpawner">The transform acting as the spawn origin.</param>
		/// <param name="targetInfo">The targeting information.</param>
		/// <param name="abilityTransform">The transform of the spawned ability object to position.</param>
		public static void SetAbilitySpawnPosition(IPlayerCharacter caster, Ability ability, Transform abilitySpawner, TargetInfo targetInfo, Transform abilityTransform)
		{
			switch (ability.Template.AbilitySpawnTarget)
			{
				case AbilitySpawnTarget.Self:
				case AbilitySpawnTarget.PointBlank:
					abilityTransform.SetPositionAndRotation(caster.Motor.Transform.position, caster.Motor.Transform.rotation);
					break;
				case AbilitySpawnTarget.Target:
					if (targetInfo.HitPosition != null)
					{
						abilityTransform.SetPositionAndRotation(targetInfo.HitPosition, caster.Transform.rotation);
					}
					else
					{
						abilityTransform.SetPositionAndRotation(targetInfo.Target.position, caster.Transform.rotation);
					}
					break;
				case AbilitySpawnTarget.Forward:
					{
						// Calculate collider offsets so the spawned ability object appears centered in front of the caster
						float distance = 0.0f;
						float height = 0.0f;
						Collider collider = ability.Template.AbilityObjectPrefab.GetComponent<Collider>();
						if (collider != null)
						{
							Collider casterCollider = caster.GameObject.GetComponent<Collider>();
							if (casterCollider != null)
							{
								distance += casterCollider.bounds.extents.z;
								height += casterCollider.bounds.extents.y;
							}
							distance += collider.bounds.extents.z;
							height += collider.bounds.extents.y;
						}
						Vector3 positionOffset = caster.Transform.forward * distance;
						positionOffset.y += height;

						Vector3 spawnPosition = caster.Motor.Transform.position + positionOffset;

						abilityTransform.SetPositionAndRotation(spawnPosition, caster.Transform.rotation);
					}
					break;
				case AbilitySpawnTarget.Camera:
					{
						Vector3 cameraPosition = caster.CharacterController.VirtualCameraPosition;
						Quaternion cameraRotation = caster.CharacterController.VirtualCameraRotation;
						Vector3 cameraForward = cameraRotation * Vector3.forward;

						Vector3 spawnPosition = cameraPosition + cameraForward;

						Vector3 farTargetPosition = cameraPosition + cameraForward * ability.Range;

						Vector3 lookDirection = (farTargetPosition - spawnPosition).normalized;

						Quaternion spawnRotation = Quaternion.LookRotation(lookDirection);

						abilityTransform.SetPositionAndRotation(spawnPosition, spawnRotation);
					}
					break;
				case AbilitySpawnTarget.Spawner:
					abilityTransform.SetPositionAndRotation(abilitySpawner.position, abilitySpawner.rotation);
					break;
				case AbilitySpawnTarget.SpawnerWithCameraRotation:
					{
						Vector3 cameraPosition = caster.CharacterController.VirtualCameraPosition;
						Quaternion cameraRotation = caster.CharacterController.VirtualCameraRotation;
						Vector3 cameraForward = cameraRotation * Vector3.forward;

						Vector3 farTargetPosition = cameraPosition + cameraForward * ability.Range;

						Vector3 lookDirection = (farTargetPosition - abilitySpawner.position).normalized;

						Quaternion spawnRotation = Quaternion.LookRotation(lookDirection);

						abilityTransform.SetPositionAndRotation(abilitySpawner.position, spawnRotation);
					}
					break;
				default:
					break;
			}
		}
	}
}