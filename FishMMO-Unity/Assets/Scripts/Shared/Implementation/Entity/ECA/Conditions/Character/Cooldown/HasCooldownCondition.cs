using System;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Condition that checks if an ability is currently on cooldown.
	/// Resolves the current tick from the character's NetworkObject.
	/// </summary>
	[Serializable]
	public class HasCooldownCondition : BaseCondition
	{
		/// <summary>
		/// The ability ID to check for cooldown.
		/// </summary>
		public long AbilityID;

		/// <inheritdoc />
		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			ICharacter characterToCheck = (eventData?.TargetCharacter ?? initiator);
			if (characterToCheck == null)
			{
				return false;
			}

			if (!characterToCheck.TryGet(out ICooldownController cooldownController))
			{
				return false;
			}

			uint currentTick = 0;
			if (characterToCheck.NetworkObject != null &&
				characterToCheck.NetworkObject.TimeManager != null)
			{
				currentTick = characterToCheck.NetworkObject.TimeManager.LocalTick;
			}

			return cooldownController.IsOnCooldown(AbilityID, currentTick);
		}
	}
}