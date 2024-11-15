using System.Collections.Generic;
using UnityEngine;
using SceneManager = UnityEngine.SceneManagement.SceneManager;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Ability Multiply Event", menuName = "Character/Ability/Spawn Event/Multiply", order = 1)]
	public sealed class MultiplyEvent : SpawnEvent
	{
		public int SpawnCount;

		public override void Invoke(ICharacter self, TargetInfo targetInfo, AbilityObject initialObject, ref int nextID, Dictionary<int, AbilityObject> abilityObjects)
		{
			if (abilityObjects != null)
			{
				for (int i = 0; i < SpawnCount; ++i)
				{
					// create/fetch from pool
					GameObject go = new GameObject(initialObject.name);
					SceneManager.MoveGameObjectToScene(go, self.GameObject.scene);
					go.SetActive(false);

					// construct additional ability objects
					AbilityObject abilityObject = go.GetComponent<AbilityObject>();
					if (abilityObject == null)
					{
						abilityObject = go.AddComponent<AbilityObject>();
					}
					go.transform.SetPositionAndRotation(initialObject.transform.position, initialObject.transform.rotation);
					abilityObject.ContainerID = initialObject.ContainerID;
					abilityObject.Ability = initialObject.Ability;
					abilityObject.Caster = initialObject.Caster;
					abilityObject.HitCount = initialObject.HitCount;
					abilityObject.RemainingLifeTime = initialObject.RemainingLifeTime;
					abilityObjects.Add(++nextID, abilityObject);

					// Self target abilities don't trigger collisions
					if (initialObject.Ability.Template.AbilitySpawnTarget == AbilitySpawnTarget.Self)
					{
						// Disable the collider so we can still play FX
						Collider collider = go.GetComponent<Collider>();
						if (collider != null)
						{
							collider.enabled = false;
						}
					}
				}
			}
		}

		public override string GetFormattedDescription()
		{
			return Description.Replace("$SPAWNCOUNT$", SpawnCount.ToString());
		}
	}
}