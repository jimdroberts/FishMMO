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
		/// </summary>
		public IPlayerCharacter Caster;
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
		/// Random number generator for ability effects.
		/// </summary>
		public System.Random RNG;

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
		/// </summary>
		void Update()
		{
			// Update remaining lifetime if the ability has a positive lifetime
			if (Ability.LifeTime > 0.0f)
			{
				RemainingLifeTime -= Time.deltaTime;
			}

			// Dispatch OnTick events for this ability object
			if (Ability?.OnTickEvents != null)
			{
				AbilityTickEventData tickEvent = new AbilityTickEventData(Caster, Transform, Time.deltaTime, this);
				foreach (var trigger in Ability.OnTickEvents.Values)
				{
					trigger.Execute(tickEvent);
				}
			}

			// If lifetime reaches 0, trigger destruction directly (or via a trigger for more control)
			// For simplicity, destroy immediately if no trigger handles it
			if (Ability.LifeTime > 0.0f && RemainingLifeTime < 0.0f)
			{
				DestroyAbilityObjectInternal();
				return;
			}
			else if (Ability.LifeTime <= 0.0f) // Immediately destroy if lifetime is 0
			{
				DestroyAbilityObjectInternal();
				return;
			}
		}

		/// <summary>
		/// Unity OnCollisionEnter callback. Handles collision logic, event dispatch, and destruction.
		/// </summary>
		/// <param name="collision">The collision data from Unity.</param>
		void OnCollisionEnter(Collision collision)
		{
			ICharacter hitCharacter = collision.gameObject.GetComponent<ICharacter>();

			// Guard against null or invalid ability
			if (Ability == null || Ability.Template == null)
			{
				DestroyAbilityObjectInternal();
				return;
			}

			if (Ability.Template.TargetTrigger != null)
			{
				// Create an AbilityCollisionEventData for the collision
				AbilityCollisionEventData collisionEvent = new AbilityCollisionEventData(Caster, hitCharacter, this, collision);
				// Add the CharacterHitEventData to the collision event (for damage, effects, etc)
				collisionEvent.Add(new CharacterHitEventData(Caster, hitCharacter, RNG));

				Ability.Template.TargetTrigger.Execute(collisionEvent);
			}

			// Check if object should be destroyed after hits.
			if (HitCount < 1)
			{
				DestroyAbilityObjectInternal();
			}
		}

		/// <summary>
		/// Destroys this ability object, dispatching OnDestroy events and cleaning up references.
		/// </summary>
		/// <remarks>
		/// Renamed to avoid confusion with MonoBehaviour.Destroy().
		/// </remarks>
		internal void DestroyAbilityObjectInternal()
		{
			// Log.Debug("Destroyed " + gameObject.name);
			if (Ability != null)
			{
				// Dispatch OnDestroy Event if needed
				if (Ability.OnDestroyEvents != null)
				{
					// You might need a specific EventData for destruction
					// For example, AbilityDestroyEventData with `AbilityObject` reference
					// For now, just pass a generic EventData if no specific data is needed
					EventData destroyEvent = new EventData(Caster); // Or a new AbilityDestroyEventData(Caster, this);
					foreach (var trigger in Ability.OnDestroyEvents.Values)
					{
						trigger.Execute(destroyEvent);
					}
				}

				Ability.RemoveAbilityObject(ContainerID, ID);
				Ability = null;
			}
			Caster = null;
			Destroy(GameObject);
			GameObject.SetActive(false); // Destroy takes a frame, deactivate immediately
		}

		/// <summary>
		/// Handles primary spawn functionality for all ability objects. Returns true if successful.
		/// </summary>
		public static void Spawn(Ability ability, IPlayerCharacter caster, Transform abilitySpawner, TargetInfo targetInfo, int seed)
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
						// Get the camera's forward vector
						Vector3 cameraForward = caster.CharacterController.VirtualCameraRotation * Vector3.forward;

						// TODO Should this value be adjust so it's in front of the player?
						Vector3 spawnPosition = caster.CharacterController.VirtualCameraPosition + cameraForward;

						// Get a target position far from the camera position
						Vector3 farTargetPosition = caster.CharacterController.VirtualCameraPosition + cameraForward * ability.Range;

						// Calculate the look direction towards the far target position
						Vector3 lookDirection = (farTargetPosition - spawnPosition).normalized;

						// Calculate the rotation to align with the look direction
						Quaternion spawnRotation = Quaternion.LookRotation(lookDirection);

						abilityTransform.SetPositionAndRotation(spawnPosition, spawnRotation);
					}
					break;
				case AbilitySpawnTarget.Spawner:
					abilityTransform.SetPositionAndRotation(abilitySpawner.position, abilitySpawner.rotation);
					break;
				case AbilitySpawnTarget.SpawnerWithCameraRotation:
					{
						// Get the camera's forward vector
						Vector3 cameraForward = caster.CharacterController.VirtualCameraRotation * Vector3.forward;

						// Get a target position far from the camera position
						Vector3 farTargetPosition = caster.CharacterController.VirtualCameraPosition + cameraForward * ability.Range;

						// Calculate the look direction towards the far target position
						Vector3 lookDirection = (farTargetPosition - abilitySpawner.position).normalized;

						// Calculate the rotation to align with the look direction
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