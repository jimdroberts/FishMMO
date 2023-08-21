using System.Collections.Generic;
using UnityEngine;

public class AbilityObject : MonoBehaviour
{
	public Ability Ability;
	public Character Caster;

	// the number of remaining hits for this ability object before it disappears
	public int HitCount = 0;

	void Update()
	{
		if (Ability != null)
		{
			foreach (MoveEvent moveEvent in Ability.MoveEvents.Values)
			{
				// invoke
				moveEvent.Invoke(gameObject);
			}
		}
	}

	void OnCollisionEnter(Collision other)
	{
		Character hitCharacter = other.gameObject.GetComponent<Character>();
		if (hitCharacter != null /* && hitCharacter.AggressorInfo.CheckEnemy(Caster)*/)
		{
			if (Ability != null)
			{
				foreach (HitEvent hitEvent in Ability.HitEvents.Values)
				{
					TargetInfo targetInfo = new TargetInfo()
					{
						target = other.transform,
						hitPosition = other.GetContact(0).point,
					};

					// invoke
					int hitCount = hitEvent.Invoke(Caster, hitCharacter, targetInfo, gameObject);

					// we remove one hit every time a HitEvent returns true
					// if hit count falls below 1 the object will be destroyed after iterating all events
					HitCount -= hitCount;
				}
			}
			if (HitCount < 1)
			{
				// TODO - add pooling to destroys ability objects
				gameObject.SetActive(false);
				Destroy(gameObject);
			}
		}
		else
		{
			// we hit something that wasn't a player, default behaviour is to destroy the object?

			// TODO - add pooling to destroys ability objects
			gameObject.SetActive(false);
			Destroy(gameObject);
		}
	}

	/// <summary>
	/// Handles primary spawn functionality for all ability objects.
	/// </summary>
	/// <param name="self"></param>
	/// <param name="targetInfo"></param>
	public static void Spawn(Ability ability, Character self, TargetInfo targetInfo)
	{
		AbilityTemplate template = ability.Template;

		// TODO create/fetch from pool
		GameObject go = Instantiate(template.Prefab);
		switch (template.AbilitySpawnTarget)
		{
			case AbilitySpawnTarget.Self:
				go.transform.SetPositionAndRotation(self.transform.position, self.transform.rotation);
				break;
			case AbilitySpawnTarget.Hand:
				go.transform.SetPositionAndRotation(self.AbilitySpawnPoint.position, self.AbilitySpawnPoint.rotation);
				break;
			case AbilitySpawnTarget.Target:
				if (targetInfo.hitPosition != null)
				{
					go.transform.SetPositionAndRotation(targetInfo.hitPosition, self.transform.rotation);
				}
				else
				{
					go.transform.SetPositionAndRotation(targetInfo.target.position, self.transform.rotation);
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
		abilityObject.Ability = ability;
		abilityObject.Caster = self;
		abilityObject.HitCount = template.HitCount;

		// add our initial object to list and return
		List<AbilityObject> objects = new List<AbilityObject>
		{
			abilityObject
		};

		// handle pre spawn events
		foreach (SpawnEvent spawnEvent in ability.PreSpawnEvents.Values)
		{
			spawnEvent.Invoke(self, targetInfo, ref objects);
		}

		// handle spawn events
		foreach (SpawnEvent spawnEvent in ability.SpawnEvents.Values)
		{
			spawnEvent.Invoke(self, targetInfo, ref objects);
		}

		// finalize
		for (int i = 0; i < objects.Count; ++i)
		{
			objects[i].gameObject.SetActive(true);
		}
	}
}