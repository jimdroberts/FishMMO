using FishNet.Transporting;
#if !UNITY_SERVER
using static FishMMO.Client.Client;
#endif

namespace FishMMO.Shared
{
	public class BankController : ItemContainer
	{
		public long Currency = 0;

		public override void OnAwake()
		{
			AddSlots(null, 100);
		}

#if !UNITY_SERVER
		public override void OnStartClient()
		{
			base.OnStartClient();

			if (!base.IsOwner)
			{
				enabled = false;
				return;
			}

			ClientManager.RegisterBroadcast<BankSetItemBroadcast>(OnClientBankSetItemBroadcastReceived);
			ClientManager.RegisterBroadcast<BankSetMultipleItemsBroadcast>(OnClientBankSetMultipleItemsBroadcastReceived);
			ClientManager.RegisterBroadcast<BankRemoveItemBroadcast>(OnClientBankRemoveItemBroadcastReceived);
			ClientManager.RegisterBroadcast<BankSwapItemSlotsBroadcast>(OnClientBankSwapItemSlotsBroadcastReceived);
		}

		public override void OnStopClient()
		{
			base.OnStopClient();

			if (base.IsOwner)
			{
				ClientManager.UnregisterBroadcast<BankSetItemBroadcast>(OnClientBankSetItemBroadcastReceived);
				ClientManager.UnregisterBroadcast<BankSetMultipleItemsBroadcast>(OnClientBankSetMultipleItemsBroadcastReceived);
				ClientManager.UnregisterBroadcast<BankRemoveItemBroadcast>(OnClientBankRemoveItemBroadcastReceived);
				ClientManager.UnregisterBroadcast<BankSwapItemSlotsBroadcast>(OnClientBankSwapItemSlotsBroadcastReceived);
			}
		}

		/// <summary>
		/// Server sent a set item broadcast. Item slot is set to the received item details.
		/// </summary>
		private void OnClientBankSetItemBroadcastReceived(BankSetItemBroadcast msg, Channel channel)
		{
			Item newItem = new Item(msg.instanceID, msg.seed, msg.templateID, msg.stackSize);
			SetItemSlot(newItem, msg.slot);
		}

		/// <summary>
		/// Server sent a multiple set item broadcast. Item slot is set to the received item details.
		/// </summary>
		private void OnClientBankSetMultipleItemsBroadcastReceived(BankSetMultipleItemsBroadcast msg, Channel channel)
		{
			foreach (BankSetItemBroadcast subMsg in msg.items)
			{
				Item newItem = new Item(subMsg.instanceID, subMsg.seed, subMsg.templateID, subMsg.stackSize);
				SetItemSlot(newItem, subMsg.slot);
			}
		}

		/// <summary>
		/// Server sent a remove item from slot broadcast. Item is removed from the received slot with server authority.
		/// </summary>
		private void OnClientBankRemoveItemBroadcastReceived(BankRemoveItemBroadcast msg, Channel channel)
		{
			RemoveItem(msg.slot);
		}

		/// <summary>
		/// Server sent a swap slot broadcast. Both slots are swapped with server authority.
		/// </summary>
		/// <param name="msg"></param>
		private void OnClientBankSwapItemSlotsBroadcastReceived(BankSwapItemSlotsBroadcast msg, Channel channel)
		{
			switch (msg.fromInventory)
			{
				case InventoryType.Inventory:
					if (Character.TryGet(out InventoryController inventoryController) &&
						inventoryController.TryGetItem(msg.from, out Item inventoryItem))
					{
						if (TryGetItem(msg.to, out Item bankItem))
						{
							inventoryController.SetItemSlot(bankItem, msg.from);
						}
						else
						{
							inventoryController.SetItemSlot(null, msg.from);
						}

						SetItemSlot(inventoryItem, msg.to);
					}
					break;
				case InventoryType.Equipment:
					break;
				case InventoryType.Bank:
					SwapItemSlots(msg.from, msg.to);
					break;
				default: return;
			}
		}
#endif

		public override bool CanManipulate()
		{
			if (!base.CanManipulate())
			{
				return false;
			}

			/*if ((character.State == CharacterState.Idle ||
				  character.State == CharacterState.Moving) &&
				  character.State != CharacterState.UsingObject &&
				  character.State != CharacterState.IsFrozen &&
				  character.State != CharacterState.IsStunned &&
				  character.State != CharacterState.IsMesmerized) return true;
			*/
			return true;
		}

		public void SendSwapItemSlotsRequest(int from, int to, InventoryType fromInventory)
		{
#if !UNITY_SERVER
			if (fromInventory == InventoryType.Bank &&
				from == to)
			{
				return;
			}
			Broadcast(new BankSwapItemSlotsBroadcast()
			{
				from = from,
				to = to,
				fromInventory = fromInventory,
			}, Channel.Reliable);
#endif
		}
	}
}