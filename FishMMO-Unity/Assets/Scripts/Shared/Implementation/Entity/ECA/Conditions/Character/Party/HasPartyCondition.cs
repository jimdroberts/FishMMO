using System;
using FishMMO.Logging;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Condition that checks if a character is in a party, with optional inversion.
	/// </summary>
	[Serializable]
	public class HasPartyCondition : BaseCondition
	{
		/// <inheritdoc />
		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			ICharacter characterToCheck = (eventData?.TargetCharacter ?? initiator);
			if (!characterToCheck.TryGet(out IPartyController partyController))
			{
				Log.Warning("HasPartyCondition", $"Character '{characterToCheck?.Name}' does not have a Party Controller. Condition failed.");
				return false;
			}
			bool isInParty = partyController.ID != 0;
			if (!isInParty)
			{
				Log.Debug("HasPartyCondition", $"Character '{characterToCheck?.Name}' is not in a party.");
			}
			return isInParty;
		}
	}
}