using System;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Condition that checks if the character currently has a target.
	/// </summary>
	[Serializable]
	public class HasTargetCondition : BaseCondition
	{
		/// <inheritdoc />
		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			ICharacter characterToCheck = (eventData?.TargetCharacter ?? initiator);
			if (characterToCheck == null)
			{
				return false;
			}

			if (!characterToCheck.TryGet(out ITargetController targetController))
			{
				return false;
			}

			return targetController.Current.Target != null;
		}
	}
}
