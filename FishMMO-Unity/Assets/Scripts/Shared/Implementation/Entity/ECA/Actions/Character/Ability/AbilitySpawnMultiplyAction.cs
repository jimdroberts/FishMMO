using System;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared.Core;

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
		/// Hard ceiling on how many copies one spawn event may produce.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The count comes from an authored value provider, and several of those are computed —
		/// <c>StatScaledValue</c> reads a character attribute, <c>RandomRangeValue</c> draws a
		/// number. Nothing bounded the result, so a provider wired to a stat that scales with gear
		/// would instantiate that many GameObjects on every peer, per cast, and a mis-authored
		/// range could do it by accident.
		/// </para>
		/// <para>
		/// Thirty is far above any shotgun or nova a designer would author and far below a number
		/// that costs a frame. It is a backstop, not a tuning knob: an ability that legitimately
		/// wants more should say so here rather than through a provider nobody checks.
		/// </para>
		/// </remarks>
		public const int MaximumSpawnCount = 30;

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
			if (spawnCount > MaximumSpawnCount)
			{
				Log.Warning("AbilitySpawnMultiplyAction",
					$"Spawn count {spawnCount} exceeds the {MaximumSpawnCount} ceiling; clamping. " +
					"A computed value provider produced this — check what it is scaling from.");
				spawnCount = MaximumSpawnCount;
			}

			for (int i = 0; i < spawnCount; ++i)
			{
				GameObject go = UnityEngine.Object.Instantiate(initialObject.gameObject);
				go.SetActive(false);

				var abilityObject = go.GetComponent<AbilityObject>();
				if (abilityObject == null)
				{
					abilityObject = go.AddComponent<AbilityObject>();
				}
				go.transform.SetPositionAndRotation(initialObject.transform.position, initialObject.transform.rotation);

				// Assign a unique ID from the shared counter so RemoveAbilityObject
				// can locate this child by (ContainerID, ID) during cleanup.
				int abilityObjectID = nextID.Value++;
				AbilityObject.InitializeSpawnedChildObject(abilityObject,
					initialObject,
					abilityObjectID,
					spawnEventData.SpawnedAbilityObjects,
					spawnEventData.Seed);
			}
		}
	}
}