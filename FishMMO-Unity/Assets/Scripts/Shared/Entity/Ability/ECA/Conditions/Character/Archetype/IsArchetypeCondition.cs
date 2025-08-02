using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Condition that checks if a character is of a specified archetype.
	/// </summary>
	[CreateAssetMenu(fileName = "New Is Archetype Condition", menuName = "FishMMO/Triggers/Conditions/Archetype/Is Archetype", order = 1)]
	public class IsArchetypeCondition : BaseCondition
	{
		/// <summary>
		/// The archetype template to check against the character's archetype.
		/// </summary>
		public ArchetypeTemplate ArchetypeTemplate;

		/// <summary>
		/// Evaluates whether the character (or event target) is of the specified archetype.
		/// </summary>
		/// <param name="initiator">The character to check, or the fallback if no event target is present.</param>
		/// <param name="eventData">Optional event data that may provide a different character to check.</param>
		/// <returns>True if the character is of the specified archetype; otherwise, false.</returns>
		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			// Determine which character to check: use the event target if available, otherwise use the initiator.
			ICharacter characterToCheck = initiator;
			if (eventData != null && eventData.TryGet(out CharacterHitEventData charTargetEventData) && charTargetEventData.Target != null)
			{
				characterToCheck = charTargetEventData.Target;
			}
			if (characterToCheck == null)
			{
				Log.Warning("IsArchetypeCondition", $"Character does not exist.");
				return false;
			}
			// Try to get the archetype controller from the character.
			if (!characterToCheck.TryGet(out IArchetypeController archetypeController))
			{
				Log.Warning("IsArchetypeCondition", $"Character does not have an IArchetypeController.");
				return false;
			}

			// Check if the character's archetype matches the required template.
			return archetypeController.Template.ID == ArchetypeTemplate.ID;
		}

		/// <summary>
		/// Returns a formatted description of the archetype condition for UI display.
		/// </summary>
		/// <returns>A string describing the required archetype.</returns>
		public override string GetFormattedDescription()
		{
			string archetypeName = ArchetypeTemplate != null ? ArchetypeTemplate.Name : "[Unassigned Archetype]";
			return $"Requires the character to be of archetype: {archetypeName}.";
		}
	}
}