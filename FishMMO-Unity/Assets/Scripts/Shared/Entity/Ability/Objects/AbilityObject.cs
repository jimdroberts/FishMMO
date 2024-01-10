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

			if (Ability != null)
			{
				foreach (MoveEvent moveEvent in Ability.MoveEvents.Values)
				{
					// invoke
					moveEvent.Invoke(Ability, Transform, Time.deltaTime);
				}
			}
		}

		void OnCollisionEnter(Collision other)
		{
			/*Debug.Log("Hit");

			if (other.collider.gameObject.layer == Constants.Layers.Ground)
			{
				Debug.Log("Ground");
			}*/

			Character hitCharacter = other.gameObject.GetComponent<Character>();

			if (Ability != null)
			{
				foreach (HitEvent hitEvent in Ability.HitEvents.Values)
				{
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
		/// <param name="self"></param>
		/// <param name="targetInfo"></param>
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
					t.SetPositionAndRotation(caster.Transform.position, caster.Transform.rotation);
					break;
				case AbilitySpawnTarget.Hand:
					t.SetPositionAndRotation(abilitySpawner.position, abilitySpawner.rotation);
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
			abilityObject.RemainingActiveTime = ability.ActiveTime * controller.CalculateSpeedReduction(controller.AttackSpeedReductionTemplate);

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