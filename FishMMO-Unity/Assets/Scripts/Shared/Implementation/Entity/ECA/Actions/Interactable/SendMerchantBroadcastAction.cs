using System;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA action that opens the merchant UI for the interacting player.
	/// Requires the interactable to implement <see cref="IMerchant"/>.
	/// Broadcasts <see cref="MerchantBroadcast"/>.
	/// Server-only.
	/// </summary>
	[Serializable]
	public class SendMerchantBroadcastAction : BaseAction
	{
		/// <summary>
		/// Sends the merchant-open broadcast with the merchant template ID.
		/// </summary>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
#if UNITY_SERVER
			if (!eventData.TryGet(out PlayerInteractionEventData data) || data.Interactable == null) return;

			IMerchant merchant = data.Interactable as IMerchant;
			if (merchant?.Template == null) return;

			initiator.NetworkObject.Broadcast(new MerchantBroadcast()
			{
				InteractableID = data.Interactable.ID,
				TemplateID = merchant.Template.ID,
			});
#endif
		}
	}
}