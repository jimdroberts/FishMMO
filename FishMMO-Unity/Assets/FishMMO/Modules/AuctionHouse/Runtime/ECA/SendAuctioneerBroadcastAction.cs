using System;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA action that opens the auction house window for the interacting player, naming the Auctioneer used so every
	/// later request can be checked against it. Server only.
	/// </summary>
	[Serializable]
	public class SendAuctioneerBroadcastAction : BaseAction
	{
		/// <inheritdoc />
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			// Runtime check, not #if UNITY_SERVER: the editor runs the scene server without that define (BaseAction.IsServer).
			if (!IsServer(initiator))
			{
				return;
			}
			if (initiator is not IPlayerCharacter player)
			{
				return;
			}
			if (!eventData.TryGet(out PlayerInteractionEventData data) || data.Interactable is not IAuctioneer)
			{
				return;
			}
			SendToOwner(player, new AuctioneerBroadcast() { InteractableID = data.Interactable.ID });
		}
	}
}
