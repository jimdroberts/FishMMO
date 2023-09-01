using System.Collections.Generic;
using UnityEngine;

public sealed class MultiplyEvent : SpawnEvent
{
	public int SpawnCount;

	public override void Invoke(Character self, TargetInfo targetInfo, AbilityObject initialObject, ref int nextID, ref Dictionary<int, AbilityObject> abilityObjects)
	{
		if (abilityObjects != null && abilityObjects.Count > 0)
		{
			for (int i = 0; i < SpawnCount; i++)
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
				abilityObject.Ability = initialObject.Ability;
				abilityObject.Caster = initialObject.Caster;
				abilityObject.HitCount = initialObject.HitCount;
				while (abilityObjects.ContainsKey(nextID))
				{
					++nextID;
				}
				abilityObject.ID = nextID;
				abilityObjects.Add(abilityObject.ID, abilityObject);
			}
		}
	}
}