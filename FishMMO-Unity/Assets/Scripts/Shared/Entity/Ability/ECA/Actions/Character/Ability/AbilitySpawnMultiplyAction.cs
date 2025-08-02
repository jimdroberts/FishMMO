using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Action that multiplies the spawn of an ability, creating multiple instances of the ability object.
	/// This is typically used to spawn several copies of a projectile or effect at once.
	/// </summary>
	[CreateAssetMenu(fileName = "New Ability Spawn Multiply Action", menuName = "FishMMO/Triggers/Actions/Ability/Spawn Multiply", order = 1)]
	public class AbilitySpawnMultiplyAction : BaseAction
	{
		/// <summary>
		/// The number of times to spawn (duplicate) the ability object. Must be >= 1.
		/// </summary>
		[Tooltip("How many times to multiply the ability object.")]
		public int SpawnCount = 1;

		/// <summary>
		/// Spawns multiple copies of the initial ability object, each with the same properties as the original.
		/// </summary>
		/// <param name="initiator">The character initiating the action.</param>
		/// <param name="eventData">The event data containing ability spawn information. Must be of type <see cref="AbilitySpawnEventData"/>.</param>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			// Try to get the spawn event data. If not present, log a warning and exit.
			if (!eventData.TryGet(out AbilitySpawnEventData spawnEventData))
			{
				Log.Warning("AbilitySpawnMultiplyAction", "EventData is not AbilitySpawnEventData.");
				return;
			}
			// Validate that the initial ability object exists.
			if (spawnEventData.InitialAbilityObject == null)
			{
				Log.Warning("AbilitySpawnMultiplyAction", "AbilityObject is null in AbilitySpawnEventData.");
				return;
			}
			// Reference to the original ability object to duplicate.
			var initialObject = spawnEventData.InitialAbilityObject;
			var caster = initialObject.Caster;
			var ability = initialObject.Ability;
			var targetInfo = spawnEventData.TargetInfo;
			var nextID = spawnEventData.CurrentAbilityObjectID;
			// Loop to create the specified number of copies.
			for (int i = 0; i < SpawnCount; ++i)
			{
				// Instantiate a new GameObject based on the original.
				GameObject go = Object.Instantiate(initialObject.gameObject);
				go.SetActive(false); // Prevents initialization logic from running immediately.
									 // Try to get the AbilityObject component, or add it if missing.
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
				// Set the position and rotation to match the original.
				go.transform.SetPositionAndRotation(initialObject.transform.position, initialObject.transform.rotation);
				// Register the new ability object in the spawned objects dictionary with a unique ID.
				spawnEventData.SpawnedAbilityObjects[++nextID.Value] = abilityObject;
			}
		}

		/// <summary>
		/// Returns a formatted description of the spawn multiply action for UI display.
		/// </summary>
		/// <returns>A string describing the number of ability objects spawned.</returns>
		public override string GetFormattedDescription()
		{
			return $"Spawns <color=#FFD700>{SpawnCount}</color> copies of the ability object.";
		}
	}
}