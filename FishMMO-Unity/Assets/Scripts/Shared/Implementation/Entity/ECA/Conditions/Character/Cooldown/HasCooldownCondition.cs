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

			// Only replicate-domain TickEventData can be used directly. Raw authoritative
			// ticks must be mapped through the cooldown controller so comparisons happen
			// in the same domain as CooldownController.ExpireElapsed(input.GetTick()).
			uint currentTick = 0;
			TickEventData tickData = null;
			if (eventData != null &&
				eventData.TryGet(out tickData) &&
				tickData.IsReplicateTick &&
				tickData.IsForCharacter(characterToCheck))
			{
				currentTick = tickData.Tick;
			}
			else
			{
				uint serverTick = 0u;
				if (eventData != null &&
					eventData.TryGet(out AbilityTickEventData abilityTickData) &&
					abilityTickData.CurrentTick != 0u)
				{
					serverTick = abilityTickData.CurrentTick;
				}
				else if (tickData != null && !tickData.IsReplicateTick)
				{
					serverTick = tickData.Tick;
				}
				else if (characterToCheck.NetworkObject != null &&
					characterToCheck.NetworkObject.TimeManager != null)
				{
					serverTick = characterToCheck.NetworkObject.TimeManager.LocalTick;
				}

				currentTick = cooldownController.ResolveAuthoritativeTick(serverTick);
			}

			return cooldownController.IsOnCooldown(AbilityID, currentTick);
		}
	}
}