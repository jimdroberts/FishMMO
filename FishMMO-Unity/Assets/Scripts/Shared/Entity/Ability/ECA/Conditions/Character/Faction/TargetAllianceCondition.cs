using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Target Alliance Condition", menuName = "FishMMO/Conditions/Target Alliance", order = 0)]
	public sealed class TargetAllianceCondition : BaseCondition
	{
		public bool ApplyToSelf;
		public bool ApplyToEnemy = true;
		public bool ApplyToNeutral;
		public bool ApplyToAllies;

		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			if (eventData.TryGet(out HitEventData hitEventData))
			{
				ICharacter defender = hitEventData.Defender;

				if (initiator == null || !initiator.TryGet(out IFactionController attackerFactionController))
				{
					return false; // Initiator or their faction controller is missing
				}

				if (defender == null)
				{
					// If no defender, this condition typically wouldn't pass for character-specific effects
					return false;
				}

				// Skip Alliance check if we are targeting ourself
				if (defender.ID == initiator.ID)
				{
					return ApplyToSelf;
				}
				else if (defender.TryGet(out IFactionController defenderFactionController))
				{
					FactionAllianceLevel allianceLevel = attackerFactionController.GetAllianceLevel(defenderFactionController);

					return (allianceLevel == FactionAllianceLevel.Enemy && ApplyToEnemy) ||
						   (allianceLevel == FactionAllianceLevel.Neutral && ApplyToNeutral) ||
						   (allianceLevel == FactionAllianceLevel.Ally && ApplyToAllies);
				}
			}
			return false; // Not a HitEventData or other conditions not met
		}
	}
}