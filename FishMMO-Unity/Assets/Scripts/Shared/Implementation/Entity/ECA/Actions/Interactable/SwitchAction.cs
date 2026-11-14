using System;
using FishNet.Transporting;
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

			bool activate = !(target.IsActivated && switchInteractable.IsToggle);

			/* Never moved onto anyone: a door shut on someone standing in its doorway would close
			 * its leaf round them, and the character controller would pop them out through it. The
			 * server's own positions decide; the refusal changes nothing, so no client disagrees. */
			if (target is IClearanceSwitchTarget clearance && clearance.IsObstructed(activate))
			{
				SendFeedback(player, $"Someone is in the way of the {data.Interactable.Title.ToLowerInvariant()}.");
				return;
			}

			if (activate)
			{
				target.Activate(player);
			}
			else
			{
				target.Deactivate(player);
			}

			/* World state, not a personal result — sent to the observers of the SWITCH rather
			 * than to the player who threw it, so everyone who can see the door sees it move.
			 * Broadcasting from the player's NetworkObject, which is what this used to do, picked
			 * the set of clients who can see the player: near enough to be plausible, and not the
			 * same set at all. */
			Switch.BroadcastState(data.Interactable, target);

			if (switchInteractable.AchievementTemplate != null &&
				player.TryGet(out IAchievementController achievementController))
			{
				achievementController.Increment(switchInteractable.AchievementTemplate, 1);
			}
		}

		/// <summary>Tells the player why nothing moved, on the System chat channel (owner only).</summary>
		private static void SendFeedback(IPlayerCharacter player, string text)
		{
			if (player?.Owner == null || !player.Owner.IsActive)
			{
				return;
			}
			player.Owner.Broadcast(new ChatBroadcast()
			{
				Channel = ChatChannel.System,
				Text = text,
			}, true, Channel.Reliable);
		}
	}
}