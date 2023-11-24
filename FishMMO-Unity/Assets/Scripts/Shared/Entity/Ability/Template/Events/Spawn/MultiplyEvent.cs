using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Ability Multiply Event", menuName = "Character/Ability/Spawn Event/Multiply", order = 1)]
	public sealed class MultiplyEvent : SpawnEvent
	{
		public int SpawnCount;

		public override void Invoke(Character self, TargetInfo targetInfo, AbilityObject initialObject, ref int nextID, Dictionary<int, AbilityObject> abilityObjects)
		{
			if (abilityObjects != null)
			{
				for (int i = 0; i < SpawnCount; ++i)
				{
					// create/fetch from pool
					GameObject go = new GameObject(initialObject.name);
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
					abilityObject.RemainingActiveTime = initialObject.RemainingActiveTime;
					abilityObjects.Add(++nextID, abilityObject);
				}
			}
		}

		public override string Tooltip()
		{
			return base.Tooltip().Replace("$SPAWNCOUNT$", SpawnCount.ToString());
		}
	}
}