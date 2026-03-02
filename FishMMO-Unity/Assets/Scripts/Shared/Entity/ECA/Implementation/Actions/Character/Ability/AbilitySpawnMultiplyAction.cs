using System;
using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Action that multiplies the spawn of an ability, creating multiple instances of the ability object.
	/// This is typically used to spawn several copies of a projectile or effect at once.
	/// </summary>
	[Serializable]
	public class AbilitySpawnMultiplyAction : BaseAction
	{
		/// <summary>
		/// The value provider that determines how many times to spawn (duplicate) the ability object.
		/// </summary>
		[Tooltip("The value provider that determines how many times to multiply the ability object.")]
		[SerializeReference, SubclassSelector]
		public IIntValueProvider SpawnCountValue;

		/// <summary>
		/// Spawns multiple copies of the initial ability object, each with the same properties as the original.
		/// </summary>
		/// <param name="initiator">The character initiating the action.</param>
		/// <param name="eventData">The event data containing ability spawn information. Must be of type <see cref="AbilitySpawnEventData"/>.</param>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (SpawnCountValue == null)
			{
				Log.Warning("AbilitySpawnMultiplyAction", "SpawnCountValue provider is null.");
				return;
			}

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

			var initialObject = spawnEventData.InitialAbilityObject;
			var caster = initialObject.Caster;
			var ability = initialObject.Ability;
			var targetInfo = spawnEventData.TargetInfo;
			var nextID = spawnEventData.CurrentAbilityObjectID;

			int spawnCount = SpawnCountValue.GetValue(initiator, eventData);
			for (int i = 0; i < spawnCount; ++i)
			{
				GameObject go = UnityEngine.Object.Instantiate(initialObject.gameObject);
				go.SetActive(false);

				var abilityObject = go.GetComponent<AbilityObject>();
				if (abilityObject == null)
				{
					abilityObject = go.AddComponent<AbilityObject>();
				}
				// Copy relevant properties from the original object.
				abilityObject.ContainerID = initialObject.ContainerID;
				abilityObject.Ability = ability;
				abilityObject.Caster = caster;
				abilityObject.HitCount = initialObject.HitCount;
				abilityObject.RemainingLifeTime = initialObject.RemainingLifeTime;
				abilityObject.RNG = initialObject.RNG;
				abilityObject.SpawnTick = initialObject.SpawnTick;
				abilityObject.Snapshot = initialObject.Snapshot;

				go.transform.SetPositionAndRotation(initialObject.transform.position, initialObject.transform.rotation);

				spawnEventData.SpawnedAbilityObjects[++nextID.Value] = abilityObject;
			}
		}
	}
}