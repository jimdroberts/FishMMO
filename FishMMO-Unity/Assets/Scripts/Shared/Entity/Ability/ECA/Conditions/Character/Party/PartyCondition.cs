using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Party Condition", menuName = "FishMMO/Conditions/Party Condition", order = 1)]
	public class PartyCondition : BaseCondition
	{
		public bool MustBeInParty = true;
		public int MinimumPartyMembers = 0;

		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			if (initiator == null)
			{
				Log.Warning("PartyCondition", "Player character does not exist.");
				return false;
			}

			if (!initiator.TryGet(out IPartyController partyController))
			{
				Log.Warning("PartyCondition", "Player character does not have an IPartyController. Cannot evaluate party condition.");
				return false;
			}

			bool playerIsInParty = partyController.ID != 0;

			if (MustBeInParty != playerIsInParty)
			{
				return false;
			}

			// Step 2: If the player *must* be in a party, then check the minimum member count.
			// This check only applies if MustBeInParty is true.
			if (MustBeInParty)
			{
				// Assuming PartyController has a way to get the current member count.
				// Replace partyController.GetMemberCount() with your actual method.
				/*int currentPartyMembers = partyController.GetMemberCount();
                if (currentPartyMembers < MinimumPartyMembers)
                {
                    //Log.Debug("PartyCondition", "Player {playerCharacter.Name}'s party (size {currentPartyMembers}) is less than required minimum of {MinimumPartyMembers}.");
                    return false;
                }*/
			}

			return true;
		}
	}
}