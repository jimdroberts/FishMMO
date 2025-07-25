using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Ability Spawn Multiply Action", menuName = "FishMMO/Triggers/Actions/Ability/Spawn Multiply", order = 1)]
	public class AbilitySpawnMultiplyAction : BaseAction
	{
		[Tooltip("How many times to multiply the ability object.")]
		public int SpawnCount = 1;

		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (!eventData.TryGet(out AbilitySpawnEventData spawnEventData))
			{
				Log.Warning("AbilitySpawnMultiplyAction", "EventData is not AbilitySpawnEventData.");
				return;
			}
			if (spawnEventData.InitialAbilityObject == null)
			{
				Log.Warning("AbilitySpawnMultiplyAction", "AbilityObject is null in AbilitySpawnEventData.");
				return;
			}
			if (spawnEventData.InitialAbilityObject == null)
			{
				Log.Warning("AbilitySpawnMultiplyAction", "SpawnedObjects dictionary is null.");
				return;
			}
			if (spawnEventData.InitialAbilityObject == null)
			{
				Log.Warning("AbilitySpawnMultiplyAction", "AbilityObject is null.");
				return;
			}
			if (spawnEventData.InitialAbilityObject == null)
			{
				Log.Warning("AbilitySpawnMultiplyAction", "SpawnedObjects dictionary is null.");
				return;
			}
			var initialObject = spawnEventData.InitialAbilityObject;
			var caster = initialObject.Caster;
			var ability = initialObject.Ability;
			var targetInfo = spawnEventData.TargetInfo;
			var nextID = spawnEventData.CurrentAbilityObjectID;
			for (int i = 0; i < SpawnCount; ++i)
			{
				GameObject go = Object.Instantiate(initialObject.gameObject);
				go.SetActive(false);
				var abilityObject = go.GetComponent<AbilityObject>();
				if (abilityObject == null)
				{
					abilityObject = go.AddComponent<AbilityObject>();
				}
				abilityObject.ContainerID = initialObject.ContainerID;
				abilityObject.Ability = ability;
				abilityObject.Caster = caster;
				abilityObject.HitCount = initialObject.HitCount;
				abilityObject.RemainingLifeTime = initialObject.RemainingLifeTime;
				abilityObject.RNG = initialObject.RNG;
				go.transform.SetPositionAndRotation(initialObject.transform.position, initialObject.transform.rotation);
				spawnEventData.SpawnedAbilityObjects[++nextID.Value] = abilityObject;
			}
		}
	}
}