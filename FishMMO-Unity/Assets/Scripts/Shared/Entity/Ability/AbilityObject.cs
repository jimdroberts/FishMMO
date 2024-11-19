using UnityEngine;
using SceneManager = UnityEngine.SceneManagement.SceneManager;
using System.Collections.Generic;
using System;

namespace FishMMO.Shared
{
	public class AbilityObject : MonoBehaviour
	{
		public static Action<PetAbilityTemplate, IPlayerCharacter> OnPetSummon;

		internal int ContainerID;
		internal int ID;
		public Ability Ability;
		public IPlayerCharacter Caster;
		public Rigidbody CachedRigidBody;
		// The number of remaining hits for this ability object before it disappears
		public int HitCount;
		public float RemainingLifeTime;

		public System.Random RNG;

		public Transform Transform { get; private set; }

		private void Awake()
		{
			Transform = transform;
			CachedRigidBody = GetComponent<Rigidbody>();
			if (CachedRigidBody != null)
			{
				CachedRigidBody.isKinematic = true;
			}
		}

		void Update()
		{
			if (Ability.LifeTime > 0.0f)
			{
				if (RemainingLifeTime < 0.0f)
				{
					Destroy();
					return;
				}
				RemainingLifeTime -= Time.deltaTime;
			}
			else // Immediately destroy the object if it has no lifetime
			{
				Destroy();
				return;
			}

			if (Ability != null &&
				Ability.MoveEvents != null)
			{
				foreach (MoveEvent moveEvent in Ability.MoveEvents.Values)
				{
					// Invoke
					moveEvent?.Invoke(this, Time.deltaTime);
				}
			}
		}

		void OnCollisionEnter(Collision other)
		{
			// Check if we hit an obstruction
			if ((Constants.Layers.Obstruction & (1 << other.collider.gameObject.layer)) != 0)
			{
				HitCount = 0;
			}

			ICharacter hitCharacter = other.gameObject.GetComponent<ICharacter>();

			HitCount = ApplyHitEvents(Ability, Caster, hitCharacter, this, other, HitCount);

			if (hitCharacter == null ||
				HitCount < 1)
			{
				Destroy();
			}
		}

		private static int ApplyHitEvents(Ability ability, ICharacter caster, ICharacter hitCharacter, AbilityObject abilityObject, Collision other = null, int hitCount = 0)
		{
			if (ability == null)
			{
				return 0;
			}

			TargetInfo targetInfo;
			if (other == null)
			{
				if (hitCharacter != null)
				{
					targetInfo = new TargetInfo()
					{
						Target = hitCharacter.Transform,
						HitPosition = hitCharacter.Transform.position,
					};
				}
				else
				{
					targetInfo = default;
				}
			}
			else
			{
				targetInfo = new TargetInfo()
				{
					Target = other.transform,
					HitPosition = other.GetContact(0).point,
				};
			}

			foreach (HitEvent hitEvent in ability.HitEvents.Values)
			{
				if (hitEvent == null)
				{
					continue;
				}

				// We remove hit count with the events return value
				// If hit count falls below 1 the object will be destroyed after iterating all events at least once
				hitCount -= hitEvent.Invoke(caster, hitCharacter, targetInfo, abilityObject);

				// Display FX
				hitEvent.OnApplyFX(targetInfo.HitPosition);
			}

			return hitCount;
		}

		internal void Destroy()
		{
			//Debug.Log("Destroyed " + gameObject.name);
			// TODO - add pooling instead of destroying ability objects
			if (Ability != null)
			{
				Ability.RemoveAbilityObject(ContainerID, ID);
				Ability = null;
			}
			Caster = null;
			Destroy(gameObject);
			gameObject.SetActive(false);
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

			if (template.RequiresTarget &&
				targetInfo.Target == null)
			{
				return;
			}

			// Pet Summons are spawned by the server
			PetAbilityTemplate petAbilityTemplate = template as PetAbilityTemplate;
			if (petAbilityTemplate != null)
			{
				// Handle server side Spawning of the pet object
				OnPetSummon?.Invoke(petAbilityTemplate, caster);
				return;
			}

			// Self target abilities don't spawn ability objects and are instead applied immediately
			if (ability.Template.AbilitySpawnTarget == AbilitySpawnTarget.Self)
			{
				ApplyHitEvents(ability, caster, caster, null);
				return;
			}

			// Missing ability object prefab
			if (template.AbilityObjectPrefab == null)
			{
				return;
			}

			// TODO create/fetch from pool
			GameObject go = Instantiate(template.AbilityObjectPrefab);
			SceneManager.MoveGameObjectToScene(go, caster.GameObject.scene);
			SetAbilitySpawnPosition(caster, ability, abilitySpawner, targetInfo, go.transform);
			go.SetActive(false);

			// Construct initial ability object
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

			// Make sure the objects container exists
			if (ability.Objects == null)
			{
				ability.Objects = new Dictionary<int, Dictionary<int, AbilityObject>>();
			}

			Dictionary<int, AbilityObject> abilityObjects = new Dictionary<int, AbilityObject>();
			// Assign random object container ID for the ability object tracking
			int id;
			do
			{
				id = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
			} while (ability.Objects.ContainsKey(id));

			ability.Objects.Add(id, abilityObjects);
			abilityObject.ContainerID = id;

			//Debug.Log(caster.CharacterName + " at " + caster.Transform.position.ToString() + " Spawned Ability at: " + abilityObject.Transform.position.ToString() + " rot: " + abilityObject.Transform.rotation.eulerAngles.ToString());

			// Reset id for spawning
			id = 0;

			// Handle pre spawn events
			if (ability.PreSpawnEvents != null)
			{
				foreach (SpawnEvent spawnEvent in ability.PreSpawnEvents.Values)
				{
					spawnEvent?.Invoke(caster, targetInfo, abilityObject, ref id, abilityObjects);
				}
			}

			// Handle spawn events
			if (ability.SpawnEvents != null)
			{
				foreach (SpawnEvent spawnEvent in ability.SpawnEvents.Values)
				{
					spawnEvent?.Invoke(caster, targetInfo, abilityObject, ref id, abilityObjects);
				}
			}

			// Finalize
			foreach (AbilityObject obj in abilityObjects.Values)
			{
				obj.gameObject.SetActive(true);
			}
			abilityObject.gameObject.SetActive(true);

			//Debug.Log("Activated " + abilityObject.gameObject.name);
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

						// TODO - replace this with ability Range... that way the ability is 100% accurate up to the distance
						const float farDistance = 50.0f;

						// Get a target position far from the camera position
						Vector3 farTargetPosition = caster.CharacterController.VirtualCameraPosition + cameraForward * farDistance;//ability.Range;

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