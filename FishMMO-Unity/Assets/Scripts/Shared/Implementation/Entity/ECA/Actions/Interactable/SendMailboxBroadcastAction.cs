using System;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA action that opens the mailbox UI for the interacting player.
	/// Broadcasts <see cref="MailboxBroadcast"/> to the owner connection.
	/// Server-only.
	/// </summary>
	[Serializable]
	public class SendMailboxBroadcastAction : BaseAction
	{
		/// <summary>
		/// Sends the mailbox-open broadcast and increments the achievement.
		/// </summary>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
#if UNITY_SERVER
			if (initiator is not IPlayerCharacter player) return;
			if (!eventData.TryGet(out PlayerInteractionEventData data) || data.Interactable == null) return;

			initiator.NetworkObject.Broadcast(new MailboxBroadcast() { InteractableID = data.Interactable.ID });

			IMailbox mailbox = data.Interactable as IMailbox;
			if (mailbox?.AchievementTemplate != null &&
				player.TryGet(out IAchievementController achievementController))
			{
				achievementController.Increment(mailbox.AchievementTemplate, 1);
			}
#endif
		}
	}
}