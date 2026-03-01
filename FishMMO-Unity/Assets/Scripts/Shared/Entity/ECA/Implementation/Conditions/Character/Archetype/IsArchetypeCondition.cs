using System;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Condition that checks if a character is of a specified archetype.
	/// </summary>
	[Serializable]
	public class IsArchetypeCondition : BaseCondition, ITooltipContributor
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
			if (ArchetypeTemplate == null)
			{
				Log.Warning("IsArchetypeCondition", "ArchetypeTemplate is not assigned.");
				return false;
			}

			// Determine which character to check: use the event target if available, otherwise use the initiator.
			ICharacter characterToCheck = ResolveTarget(initiator, eventData);
			if (characterToCheck == null)
			{
				Log.Warning("IsArchetypeCondition", "Character does not exist.");
				return false;
			}
			// Try to get the archetype controller from the character.
			if (!characterToCheck.TryGet(out IArchetypeController archetypeController))
			{
				Log.Warning("IsArchetypeCondition", "Character does not have an IArchetypeController.");
				return false;
			}

			// Check if the character's archetype matches the required template.
			return archetypeController.Template != null && archetypeController.Template.ID == ArchetypeTemplate.ID;
		}

		/// <summary>
		/// Returns the tooltip contribution showing the archetype requirement.
		/// </summary>
		public string GetTooltipContribution()
		{
			if (ArchetypeTemplate != null)
			{
				return RichText.Format(ArchetypeTemplate.Name, true, "f5ad6eFF", "120%");
			}
			return null;
		}
	}
}