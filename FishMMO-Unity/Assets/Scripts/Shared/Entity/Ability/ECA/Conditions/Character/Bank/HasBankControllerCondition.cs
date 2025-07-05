using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "HasBankControllerCondition", menuName = "FishMMO/Conditions/Character/Has Bank Controller", order = 0)]
	public class HasBankControllerCondition : BaseCondition
	{
		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			// Try to retrieve the BankControllerEventData from the event data's dictionary
			if (initiator.TryGet(out IBankController _))
			{
				return true;
			}

			Log.Warning("HasBankControllerCondition", $"Initiator {initiator?.Name} does not have a bank controller in EventData.");
			return false;
		}
	}
}