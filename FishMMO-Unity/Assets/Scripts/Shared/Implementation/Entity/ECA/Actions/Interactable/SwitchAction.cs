using System;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA action that activates or deactivates an <see cref="ISwitchTarget"/> linked by the interacted
	/// <see cref="ISwitch"/>, then broadcasts the new state to the player.
	/// Toggle switches flip state each interaction; non-toggle switches only activate once
	/// (the <see cref="Switch.CanInteract"/> guard prevents re-interaction on non-toggle switches
	/// that are already activated).
	/// Server-only.
	/// </summary>
	[Serializable]
	public class SwitchAction : BaseAction
	{
		/// <summary>
		/// Toggles or activates the switch target and notifies the client of the new state.
		/// </summary>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			// Server-only. Runtime check, not #if UNITY_SERVER: that define is absent in the
			// editor, where the scene server also runs — see BaseAction.IsServer.
			if (!IsServer(initiator))
			{
				return;
			}

			if (initiator is not IPlayerCharacter player) return;
			if (!eventData.TryGet(out PlayerInteractionEventData data) || data.Interactable == null) return;

			ISwitch switchInteractable = data.Interactable as ISwitch;
			if (switchInteractable?.SwitchTarget == null) return;

			ISwitchTarget target = switchInteractable.SwitchTarget;

			if (target.IsActivated && switchInteractable.IsToggle)
			{
				target.Deactivate(player);
			}
			else
			{
				target.Activate(player);
			}

			initiator.NetworkObject.Broadcast(new SwitchStateBroadcast()
			{
				InteractableID = data.Interactable.ID,
				Activated = target.IsActivated,
			});

			if (switchInteractable.AchievementTemplate != null &&
				player.TryGet(out IAchievementController achievementController))
			{
				achievementController.Increment(switchInteractable.AchievementTemplate, 1);
			}
		}
	}
}