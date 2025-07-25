using FishMMO.Shared;
using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.Server.Conditions
{
	[CreateAssetMenu(fileName = "HasPartyCondition", menuName = "FishMMO/Triggers/Conditions/Party/Has Party", order = 0)]
	public class HasPartyCondition : BaseCondition
	{
		[Tooltip("If true, the condition passes if the character is NOT in a party.")]
		public bool InvertResult = false;

		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			ICharacter characterToCheck = initiator;
			if (eventData != null && eventData.TryGet(out CharacterHitEventData charTargetEventData) && charTargetEventData.Target != null)
			{
				characterToCheck = charTargetEventData.Target;
			}
			if (!characterToCheck.TryGet(out IPartyController partyController))
			{
				Log.Warning("HasPartyCondition", $"Character '{characterToCheck?.Name}' does not have a Party Controller. Condition failed.");
				return false;
			}
			bool isInParty = partyController.ID != 0;
			if (InvertResult)
			{
				if (isInParty)
				{
					Log.Debug("HasPartyCondition", $"Character '{characterToCheck?.Name}' is in a party, but 'invertResult' is true. Condition failed.");
				}
				return !isInParty;
			}
			else
			{
				if (!isInParty)
				{
					Log.Debug("HasPartyCondition", $"Character '{characterToCheck?.Name}' is not in a party. Condition failed.");
				}
				return isInParty;
			}
		}

		public override string GetFormattedDescription()
		{
			return InvertResult
				? "Requires the character to NOT be in a party."
				: "Requires the character to be in a party.";
		}
	}
}