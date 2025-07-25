using UnityEngine;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	public class AbilitySpawnEventData : EventData
	{
		public Ability Ability { get; }
		public Transform AbilitySpawner { get; }
		public TargetInfo TargetInfo { get; }
		public int Seed { get; }
		public AbilityObject InitialAbilityObject { get; }
		public RefWrapper<int> CurrentAbilityObjectID { get; }
		public Dictionary<int, AbilityObject> SpawnedAbilityObjects { get; } // Reference to the dictionary to add spawned objects

		public AbilitySpawnEventData(ICharacter initiator, Ability ability, Transform abilitySpawner, TargetInfo targetInfo, int seed, AbilityObject initialAbilityObject, RefWrapper<int> currentAbilityObjectID, Dictionary<int, AbilityObject> spawnedAbilityObjects)
			: base(initiator)
		{
			Ability = ability;
			AbilitySpawner = abilitySpawner;
			TargetInfo = targetInfo;
			Seed = seed;
			InitialAbilityObject = initialAbilityObject;
			CurrentAbilityObjectID = currentAbilityObjectID;
			SpawnedAbilityObjects = spawnedAbilityObjects;
		}
	}
}