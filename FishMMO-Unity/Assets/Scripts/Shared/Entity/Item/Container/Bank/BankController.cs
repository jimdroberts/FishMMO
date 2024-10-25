using FishNet.Transporting;
using System.Runtime.CompilerServices;

namespace FishMMO.Shared
{
	public class BankController : ItemContainer, IBankController
	{
		public long LastInteractableID { get; set; }
		public long Currency { get; set; }

		public override void OnAwake()
		{
			Currency = 0;
			AddSlots(null, 100);
		}

#if !UNITY_SERVER
		public override void OnStartCharacter()
		{
			base.OnStartCharacter();

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

		public override void OnStopCharacter()
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
			Item newItem = new Item(msg.InstanceID, msg.Seed, msg.TemplateID, msg.StackSize);
			SetItemSlot(newItem, msg.Slot);
		}

		/// <summary>
		/// Server sent a multiple set item broadcast. Item slot is set to the received item details.
		/// </summary>
		private void OnClientBankSetMultipleItemsBroadcastReceived(BankSetMultipleItemsBroadcast msg, Channel channel)
		{
			foreach (BankSetItemBroadcast subMsg in msg.Items)
			{
				Item newItem = new Item(subMsg.InstanceID, subMsg.Seed, subMsg.TemplateID, subMsg.StackSize);
				SetItemSlot(newItem, subMsg.Slot);
			}
		}

		/// <summary>
		/// Server sent a remove item from slot broadcast. Item is removed from the received slot with server authority.
		/// </summary>
		private void OnClientBankRemoveItemBroadcastReceived(BankRemoveItemBroadcast msg, Channel channel)
		{
			RemoveItem(msg.Slot);
		}

		/// <summary>
		/// Server sent a swap slot broadcast. Both slots are swapped with server authority.
		/// </summary>
		/// <param name="msg"></param>
		private void OnClientBankSwapItemSlotsBroadcastReceived(BankSwapItemSlotsBroadcast msg, Channel channel)
		{
			switch (msg.FromInventory)
			{
				case InventoryType.Inventory:
					if (Character.TryGet(out IInventoryController inventoryController) &&
						inventoryController.TryGetItem(msg.From, out Item inventoryItem))
					{
						if (TryGetItem(msg.To, out Item bankItem))
						{
							inventoryController.SetItemSlot(bankItem, msg.From);
						}
						else
						{
							inventoryController.SetItemSlot(null, msg.From);
						}

						SetItemSlot(inventoryItem, msg.To);
					}
					break;
				case InventoryType.Equipment:
					break;
				case InventoryType.Bank:
					SwapItemSlots(msg.From, msg.To);
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

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool CanSwapItemSlots(int from, int to, InventoryType fromInventory)
		{
			return !(fromInventory == InventoryType.Bank && from == to);
		}
	}
}