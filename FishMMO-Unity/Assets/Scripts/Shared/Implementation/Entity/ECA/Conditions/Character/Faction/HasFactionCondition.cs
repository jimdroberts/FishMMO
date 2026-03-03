using System;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Condition that checks if a character belongs to a specific faction.
	/// Evaluates true if the character has the specified <see cref="FactionTemplate"/> in their faction controller.
	/// </summary>
	[Serializable]
	public sealed class HasFactionCondition : BaseCondition, ITooltipContributor
	{
		/// <summary>
		/// The faction template the character must belong to.
		/// </summary>
		public FactionTemplate FactionTemplate;

		/// <summary>
		/// Evaluates whether the character has the specified faction.
		/// </summary>
		/// <param name="initiator">The character initiating the check.</param>
		/// <param name="eventData">Optional event data for target redirection.</param>
		/// <returns>True if the character has the faction; otherwise, false.</returns>
		public override bool Evaluate(ICharacter initiator, EventData eventData = null)
		{
			if (FactionTemplate == null) return false;

			ICharacter characterToCheck = ResolveTarget(initiator, eventData);

			if (characterToCheck == null) return false;
			if (!characterToCheck.TryGet(out IFactionController factionController)) return false;

			return factionController.Factions.ContainsKey(FactionTemplate.ID);
		}

		/// <summary>
		/// Returns the tooltip contribution for this condition.
		/// </summary>
		public string GetTooltipContribution()
		{
			if (FactionTemplate != null)
			{
				return RichText.Format(FactionTemplate.Name, true, "f5ad6eFF", "120%");
			}
			return null;
		}
	}
}