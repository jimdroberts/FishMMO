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
		/// <summary>
		/// If true, inverts the result (returns true when no target).
		/// </summary>
		public bool Invert;

		/// <inheritdoc />
		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			ICharacter characterToCheck = ResolveTarget(initiator, eventData);
			if (characterToCheck == null)
			{
				return false;
			}

			if (!characterToCheck.TryGet(out ITargetController targetController))
			{
				return false;
			}

			bool hasTarget = targetController.Current.Target != null;
			return Invert ? !hasTarget : hasTarget;
		}
	}
}
