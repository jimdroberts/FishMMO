using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Attribute Controller Condition", menuName = "FishMMO/Conditions/Has Attribute Controller", order = 1)]
	public class HasAttributeControllerCondition : BaseCondition
	{
		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			if (initiator == null)
			{
				Log.Warning($"Character does not exist.");
				return false;
			}
			if (!initiator.TryGet(out ICharacterAttributeController characterAttributeController))
			{
				Log.Warning($"Character does not have an ICharacterAttributeController.");
				return false;
			}
			return true;
		}
	}
}