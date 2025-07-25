using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "HasBankControllerCondition", menuName = "FishMMO/Triggers/Conditions/Bank/Has Bank Controller", order = 0)]
	public class HasBankControllerCondition : BaseCondition
	{
		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			ICharacter characterToCheck = initiator;
			if (eventData != null && eventData.TryGet(out CharacterHitEventData charTargetEventData) && charTargetEventData.Target != null)
			{
				characterToCheck = charTargetEventData.Target;
			}
			if (characterToCheck.TryGet(out IBankController _))
			{
				return true;
			}
			Log.Warning("HasBankControllerCondition", $"Character {characterToCheck?.Name} does not have a bank controller in EventData.");
			return false;
		}
	}
}