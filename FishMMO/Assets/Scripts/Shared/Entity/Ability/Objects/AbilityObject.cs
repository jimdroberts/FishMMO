using UnityEngine;

public class AbilityObject : MonoBehaviour
{
	internal int ID = 0;
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
				Destroy();
			}
		}
		else
		{
			// we hit something that wasn't a player, default behaviour is to destroy the object?
			Destroy();
		}
	}

	internal void Destroy()
	{
		// TODO - add pooling to destroys ability objects
		Ability.objects.Remove(ID);
		gameObject.SetActive(false);
		Destroy(gameObject);
	}

	/// <summary>
	/// Handles primary spawn functionality for all ability objects.
	/// </summary>
	/// <param name="self"></param>
	/// <param name="targetInfo"></param>
	public static void Spawn(Ability ability, Character self, Transform abilitySpawner, TargetInfo targetInfo)
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
				go.transform.SetPositionAndRotation(abilitySpawner.position, abilitySpawner.rotation);
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
		int id = 0;
		AbilityObject abilityObject = go.GetComponent<AbilityObject>();
		if (abilityObject == null)
		{
			abilityObject = go.AddComponent<AbilityObject>();
		}
		abilityObject.Ability = ability;
		abilityObject.Caster = self;
		abilityObject.HitCount = template.HitCount;
		while (ability.objects.ContainsKey(id))
		{
			++id;
		}
		abilityObject.ID = id;
		ability.objects.Add(abilityObject.ID, abilityObject);

		// handle pre spawn events
		foreach (SpawnEvent spawnEvent in ability.PreSpawnEvents.Values)
		{
			spawnEvent.Invoke(self, targetInfo, abilityObject, ref id, ref ability.objects);
		}

		// handle spawn events
		foreach (SpawnEvent spawnEvent in ability.SpawnEvents.Values)
		{
			spawnEvent.Invoke(self, targetInfo, abilityObject, ref id, ref ability.objects);
		}

		// finalize
		foreach (AbilityObject obj in ability.objects.Values)
		{
			obj.gameObject.SetActive(true);
		}
	}
}