using UnityEngine;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Event data for spawning abilities, containing all relevant information for ability instantiation and tracking.
	/// </summary>
	public class AbilitySpawnEventData : EventData
	{
		/// <summary>
		/// The ability being spawned.
		/// </summary>
		public Ability Ability { get; }

		/// <summary>
		/// The transform that acts as the spawner for the ability (e.g., the caster's hand, a spawn point).
		/// </summary>
		public Transform AbilitySpawner { get; }

		/// <summary>
		/// Information about the target of the ability (position, entity, etc.).
		/// </summary>
		public TargetInfo TargetInfo { get; }

		/// <summary>
		/// The random seed used for deterministic ability spawning.
		/// </summary>
		public int Seed { get; }

		/// <summary>
		/// The initial ability object created (if any).
		/// </summary>
		public AbilityObject InitialAbilityObject { get; }

		/// <summary>
		/// A reference wrapper for the current ability object ID (used for tracking spawned objects).
		/// </summary>
		public RefWrapper<int> CurrentAbilityObjectID { get; }

		/// <summary>
		/// A dictionary to store all spawned ability objects, keyed by their unique ID.
		/// </summary>
		public Dictionary<int, AbilityObject> SpawnedAbilityObjects { get; }

		/// <summary>
		/// Initializes a new instance of the <see cref="AbilitySpawnEventData"/> class.
		/// </summary>
		/// <param name="initiator">The character initiating the ability spawn.</param>
		/// <param name="ability">The ability being spawned.</param>
		/// <param name="abilitySpawner">The transform acting as the spawner for the ability.</param>
		/// <param name="targetInfo">Information about the target of the ability.</param>
		/// <param name="seed">The random seed for deterministic spawning.</param>
		/// <param name="initialAbilityObject">The initial ability object created (if any).</param>
		/// <param name="currentAbilityObjectID">A reference wrapper for the current ability object ID.</param>
		/// <param name="spawnedAbilityObjects">A dictionary to store all spawned ability objects.</param>
		public AbilitySpawnEventData(
			ICharacter initiator,
			Ability ability,
			Transform abilitySpawner,
			TargetInfo targetInfo,
			int seed,
			AbilityObject initialAbilityObject,
			RefWrapper<int> currentAbilityObjectID,
			Dictionary<int, AbilityObject> spawnedAbilityObjects)
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