using FishMMO.Shared;
using UnityEngine;

namespace FishMMO.Server.Conditions
{
	[CreateAssetMenu(fileName = "HasPartyCondition", menuName = "FishMMO/Conditions/Party/Has Party", order = 0)]
	public class HasPartyCondition : BaseCondition
	{
		[Tooltip("If true, the condition passes if the character is NOT in a party.")]
		public bool InvertResult = false;

		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			if (!initiator.TryGet(out IPartyController partyController))
			{
				Log.Warning($"HasPartyCondition: Initiator '{initiator?.Name}' does not have a Party Controller. Condition failed.");
				return false;
			}

			bool isInParty = partyController.ID != 0;

			if (InvertResult)
			{
				if (isInParty)
				{
					Log.Debug($"HasPartyCondition: Character '{initiator?.Name}' is in a party, but 'invertResult' is true. Condition failed.");
				}
				return !isInParty;
			}
			else
			{
				// If invertResult is false, we pass if they ARE in a party
				if (!isInParty)
				{
					Log.Debug($"HasPartyCondition: Character '{initiator?.Name}' is not in a party. Condition failed.");
				}
				return isInParty;
			}
		}
	}
}