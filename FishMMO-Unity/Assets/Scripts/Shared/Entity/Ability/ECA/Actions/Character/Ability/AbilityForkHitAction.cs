using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Action that forks an ability hit in a specified arc and distance.
	/// This action is typically used to create a spread or scatter effect for abilities, such as projectiles that split or fork after hitting a target.
	/// </summary>
	[CreateAssetMenu(fileName = "New Ability Fork Hit Action", menuName = "FishMMO/Triggers/Actions/Ability/Ability Fork Hit")]
	public class AbilityForkHitAction : BaseAction
	{
		/// <summary>
		/// The arc in degrees within which the ability can fork.
		/// For example, 180 means the forked directions will be spread within a half-circle in front of the original direction.
		/// </summary>
		[Tooltip("The arc in degrees within which the ability can fork. E.g., 180 = half-circle spread.")]
		public float Arc = 180.0f;

		/// <summary>
		/// The maximum distance the forked ability can reach.
		/// This limits how far the forked projectiles or effects will travel from their origin.
		/// </summary>
		[Tooltip("The maximum distance the forked ability can reach.")]
		public float Distance = 60.0f;

		/// <summary>
		/// Executes the fork hit action, modifying the ability object's direction within the specified arc and distance.
		/// </summary>
		/// <param name="initiator">The character initiating the action.</param>
		/// <param name="eventData">The event data containing ability collision information.</param>
		/// <remarks>
		/// This method attempts to retrieve <see cref="AbilityCollisionEventData"/> from the event data. If successful, it randomizes the ability object's rotation within the specified arc and distance using a conical distribution.
		/// </remarks>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			// Attempt to retrieve collision event data. If not present, do nothing.
			if (eventData.TryGet(out AbilityCollisionEventData abilityEventData))
			{
				var abilityObject = abilityEventData.AbilityObject;
				if (abilityObject != null)
				{
					// Randomize the rotation of the ability object within a conical arc and distance.
					// This creates a forked or spread effect for the ability.
					abilityObject.Transform.rotation = abilityObject.Transform.forward.GetRandomConicalDirection(
						abilityObject.Transform.position, Arc, Distance, abilityObject.RNG);
				}
			}
		}

		/// <summary>
		/// Returns a formatted description of the fork hit action for UI display.
		/// </summary>
		/// <returns>A string describing the arc and distance of the fork.</returns>
		public override string GetFormattedDescription()
		{
			// Format the description for UI, highlighting arc and distance values.
			return $"Forks ability hit in a <color=#FFD700>{Arc}°</color> arc up to <color=#FFD700>{Distance}</color> units.";
		}
	}
}