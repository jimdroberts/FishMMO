using System;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA action that opens the bank UI for the interacting player.
	/// Sets the character's last-interactable ID so follow-up bank transactions are associated
	/// with this banker, then broadcasts <see cref="BankerBroadcast"/> to the owner connection.
	/// Server-only.
	/// </summary>
	[Serializable]
	public class SendBankerBroadcastAction : BaseAction
	{
		/// <summary>
		/// Sends the bank-open broadcast and records the interactable ID on the bank controller.
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

			IBanker banker = data.Interactable as IBanker;

			if (player.TryGet(out IBankController bankController))
			{
				bankController.LastInteractableID = data.Interactable.ID;

				initiator.NetworkObject.Broadcast(new BankerBroadcast());
			}

			if (banker?.AchievementTemplate != null &&
				player.TryGet(out IAchievementController achievementController))
			{
				achievementController.Increment(banker.AchievementTemplate, 1);
			}
		}
	}
}