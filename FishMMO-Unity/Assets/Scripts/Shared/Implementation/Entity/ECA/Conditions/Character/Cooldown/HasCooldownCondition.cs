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

		/// <summary>
		/// If true, inverts the result (returns true when NOT on cooldown).
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

			bool isOnCooldown = cooldownController.IsOnCooldown(AbilityID, currentTick);
			return Invert ? !isOnCooldown : isOnCooldown;
		}
	}
}