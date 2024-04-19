using UnityEngine;
using SceneManager = UnityEngine.SceneManagement.SceneManager;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	public class AbilityObject : MonoBehaviour
	{
		internal int ContainerID;
		internal int ID;
		public Ability Ability;
		public Character Caster;

		// the number of remaining hits for this ability object before it disappears
		public int HitCount;
		public float RemainingActiveTime;

		public Transform Transform { get; private set; }

		private void Awake()
		{
			Transform = transform;
			Rigidbody rigidbody = GetComponent<Rigidbody>();
			if (rigidbody != null)
			{
				rigidbody.isKinematic = true;
			}
		}

		void Update()
		{
			if (Ability.ActiveTime > 0.0f)
			{
				if (RemainingActiveTime < 0.0f)
				{
					Destroy();
					return;
				}
				RemainingActiveTime -= Time.deltaTime;
			}

			if (Ability != null &&
				Ability.MoveEvents != null)
			{
				foreach (MoveEvent moveEvent in Ability.MoveEvents.Values)
				{
					// invoke
					moveEvent?.Invoke(Ability, Transform, Time.deltaTime);
				}
			}
		}

		void OnCollisionEnter(Collision other)
		{
			Debug.Log($"Collision: {other.gameObject.name}");

			if ((Constants.Layers.Obstruction & (1 << other.collider.gameObject.layer)) != 0)
			{
				Debug.Log("Obstruction");

				HitCount = 0;
			}

			Character hitCharacter = other.gameObject.GetComponent<Character>();

			if (Ability != null)
			{
				foreach (HitEvent hitEvent in Ability.HitEvents.Values)
				{
					if (hitEvent == null)
					{
						continue;
					}
					TargetInfo targetInfo = new TargetInfo()
					{
						Target = other.transform,
						HitPosition = other.GetContact(0).point,
					};

					// we remove hit count with the events return value
					// if hit count falls below 1 the object will be destroyed after iterating all events at least once
					HitCount -= hitEvent.Invoke(Caster, hitCharacter, targetInfo, gameObject);
				}
			}

			if (hitCharacter == null ||
				HitCount < 1)
			{
				Destroy();
			}
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
		public static bool TrySpawn(Ability ability, Character caster, AbilityController controller, Transform abilitySpawner, TargetInfo targetInfo)
		{
			AbilityTemplate template = ability.Template;

			if (template == null ||
				template.FXPrefab == null)
			{
				return false;
			}

			if (template.RequiresTarget &&
				targetInfo.Target == null)
			{
				return false;
			}

			// TODO create/fetch from pool
			GameObject go = Instantiate(template.FXPrefab);
			SceneManager.MoveGameObjectToScene(go, caster.gameObject.scene);
			Transform t = go.transform;
			switch (template.AbilitySpawnTarget)
			{
				case AbilitySpawnTarget.Self:
					t.SetPositionAndRotation(caster.Motor.Transform.position, caster.Motor.Transform.rotation);
					break;
				case AbilitySpawnTarget.Target:
					if (targetInfo.HitPosition != null)
					{
						t.SetPositionAndRotation(targetInfo.HitPosition, caster.Transform.rotation);
					}
					else
					{
						t.SetPositionAndRotation(targetInfo.Target.position, caster.Transform.rotation);
					}
					break;
				case AbilitySpawnTarget.Forward:
					{
						// calculate collider offsets so the spawned ability object appears centered in front of the caster
						float distance = 0.0f;
						float height = 0.0f;
						Collider collider = template.FXPrefab.GetComponent<Collider>();
						if (collider != null)
						{
							Collider casterCollider = caster.gameObject.GetComponent<Collider>();
							if (casterCollider != null)
							{
								distance += casterCollider.bounds.extents.z;
								height += casterCollider.bounds.extents.y;
							}
							distance += collider.bounds.extents.z;
							height += collider.bounds.extents.y;
						}
						Vector3 positionOffset = caster.Transform.forward * (distance + 1.0f);
						positionOffset.y += height;

						Vector3 spawnPosition = caster.Motor.Transform.position + positionOffset;

						t.SetPositionAndRotation(spawnPosition, caster.Transform.rotation);
					}
					break;
				case AbilitySpawnTarget.Camera:
					{
						// Get the camera's forward vector
						Vector3 cameraForward = caster.CharacterController.VirtualCameraRotation * Vector3.forward;

						// TODO Should this value be adjust so it's in front of the player?
						Vector3 spawnPosition = caster.CharacterController.VirtualCameraPosition + cameraForward;

						// TODO - replace this with ability Range... that way the ability is 100% accurate up to the distance
						const float farDistance = 50.0f;

						// Get a target position far from the camera position
						Vector3 farTargetPosition = caster.CharacterController.VirtualCameraPosition + cameraForward * farDistance;//ability.Range;

						// Calculate the look direction towards the far target position
						Vector3 lookDirection = (farTargetPosition - spawnPosition).normalized;

						// Calculate the rotation to align with the look direction
						Quaternion spawnRotation = Quaternion.LookRotation(lookDirection);

						t.SetPositionAndRotation(spawnPosition, spawnRotation);
					}
					break;
				case AbilitySpawnTarget.Spawner:
					t.SetPositionAndRotation(abilitySpawner.position, abilitySpawner.rotation);
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

						t.SetPositionAndRotation(abilitySpawner.position, spawnRotation);
					}
					break;
				default:
					break;
			}
			go.SetActive(false);

			// construct initial ability object
			AbilityObject abilityObject = go.GetComponent<AbilityObject>();
			if (abilityObject == null)
			{
				abilityObject = go.AddComponent<AbilityObject>();
			}
			abilityObject.ID = 0;
			abilityObject.Ability = ability;
			abilityObject.Caster = caster;
			abilityObject.HitCount = template.HitCount;
			abilityObject.RemainingActiveTime = ability.ActiveTime;

			// make sure the objects container exists
			if (ability.Objects == null)
			{
				ability.Objects = new Dictionary<int, Dictionary<int, AbilityObject>>();
			}

			Dictionary<int, AbilityObject> abilityObjects = new Dictionary<int, AbilityObject>();
			// assign random object container ID for the ability object tracking
			int id;
			do
			{
				id = Random.Range(int.MinValue, int.MaxValue);
			} while (ability.Objects.ContainsKey(id));

			ability.Objects.Add(id, abilityObjects);
			abilityObject.ContainerID = id;

			Debug.Log(caster.CharacterName + " at " + caster.Transform.position.ToString() + " Spawned Ability at: " + abilityObject.Transform.position.ToString() + " rot: " + abilityObject.Transform.rotation.eulerAngles.ToString());

			// reset id for spawning
			id = 0;

			// handle pre spawn events
			if (ability.PreSpawnEvents != null)
			{
				foreach (SpawnEvent spawnEvent in ability.PreSpawnEvents.Values)
				{
					spawnEvent.Invoke(caster, targetInfo, abilityObject, ref id, abilityObjects);
				}
			}

			// handle spawn events
			if (ability.SpawnEvents != null)
			{
				foreach (SpawnEvent spawnEvent in ability.SpawnEvents.Values)
				{
					spawnEvent.Invoke(caster, targetInfo, abilityObject, ref id, abilityObjects);
				}
			}

			// finalize
			foreach (AbilityObject obj in abilityObjects.Values)
			{
				obj.gameObject.SetActive(true);
			}
			abilityObject.gameObject.SetActive(true);

			//Debug.Log("Activated " + abilityObject.gameObject.name);

			return true;
		}
	}
}