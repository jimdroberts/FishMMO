using System;

namespace FishMMO.Shared
{
	/// <summary>
	/// Condition that checks the alliance relationship between the initiator and the target (defender).
	/// Allows configuration for which alliance types (self, enemy, neutral, ally) the condition applies to.
	/// </summary>
	[Serializable]
	public sealed class TargetAllianceCondition : BaseCondition
	{
		/// <summary>
		/// Whether the condition applies when the initiator targets themselves.
		/// </summary>
		public bool ApplyToSelf;

		/// <summary>
		/// Whether the condition applies to enemies (default: true).
		/// </summary>
		public bool ApplyToEnemy = true;

		/// <summary>
		/// Whether the condition applies to neutral targets.
		/// </summary>
		public bool ApplyToNeutral;

		/// <summary>
		/// Whether the condition applies to allies.
		/// </summary>
		public bool ApplyToAllies;

		/// <summary>
		/// Evaluates whether the condition applies based on the alliance relationship between initiator and target.
		/// </summary>
		/// <param name="initiator">The character initiating the action or condition.</param>
		/// <param name="eventData">Event data containing the target character.</param>
		/// <returns>True if the alliance relationship matches the allowed types; otherwise, false.</returns>
		/// <remarks>
		/// This method checks the target's alliance level (enemy, neutral, ally, or self) and returns true if the corresponding flag is enabled.
		/// </remarks>
		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			if (eventData.TryGet(out CharacterHitEventData hitEventData))
			{
				ICharacter defender = hitEventData.Target;

				if (initiator == null)
				{
					return false; // Initiator or their faction controller is missing
				}

				if (defender == null)
				{
					// If no defender, this condition typically wouldn't pass for character-specific effects
					return false;
				}

				if (!initiator.TryGet(out IFactionController attackerFactionController))
				{
					return false; // Initiator must have a faction controller
				}

				// If targeting self, use the ApplyToSelf flag
				if (defender.ID == initiator.ID)
				{
					return ApplyToSelf;
				}
				// Otherwise, check the alliance level between initiator and defender
				else if (defender.TryGet(out IFactionController defenderFactionController))
				{
					FactionAllianceLevel allianceLevel = attackerFactionController.GetAllianceLevel(defenderFactionController);

					return (allianceLevel == FactionAllianceLevel.Enemy && ApplyToEnemy) ||
						   (allianceLevel == FactionAllianceLevel.Neutral && ApplyToNeutral) ||
						   (allianceLevel == FactionAllianceLevel.Ally && ApplyToAllies);
				}
			}
			// Not a CharacterHitEventData or other conditions not met
			return false;
		}

	}
}