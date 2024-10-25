using FishNet.Transporting;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace FishMMO.Shared
{
	public class InventoryController : ItemContainer, IInventoryController
	{
		public override void OnAwake()
		{
			AddSlots(null, 32);
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

			ClientManager.RegisterBroadcast<InventorySetItemBroadcast>(OnClientInventorySetItemBroadcastReceived);
			ClientManager.RegisterBroadcast<InventorySetMultipleItemsBroadcast>(OnClientInventorySetMultipleItemsBroadcastReceived);
			ClientManager.RegisterBroadcast<InventoryRemoveItemBroadcast>(OnClientInventoryRemoveItemBroadcastReceived);
			ClientManager.RegisterBroadcast<InventorySwapItemSlotsBroadcast>(OnClientInventorySwapItemSlotsBroadcastReceived);
		}

		public override void OnStopCharacter()
		{
			base.OnStopCharacter();

			if (base.IsOwner)
			{
				ClientManager.UnregisterBroadcast<InventorySetItemBroadcast>(OnClientInventorySetItemBroadcastReceived);
				ClientManager.UnregisterBroadcast<InventorySetMultipleItemsBroadcast>(OnClientInventorySetMultipleItemsBroadcastReceived);
				ClientManager.UnregisterBroadcast<InventoryRemoveItemBroadcast>(OnClientInventoryRemoveItemBroadcastReceived);
				ClientManager.UnregisterBroadcast<InventorySwapItemSlotsBroadcast>(OnClientInventorySwapItemSlotsBroadcastReceived);
			}
		}

		/// <summary>
		/// Server sent a set item broadcast. Item slot is set to the received item details.
		/// </summary>
		private void OnClientInventorySetItemBroadcastReceived(InventorySetItemBroadcast msg, Channel channel)
		{
			Item newItem = new Item(msg.InstanceID, msg.Seed, msg.TemplateID, msg.StackSize);
			SetItemSlot(newItem, msg.Slot);
		}

		/// <summary>
		/// Server sent a multiple set item broadcast. Item slot is set to the received item details.
		/// </summary>
		private void OnClientInventorySetMultipleItemsBroadcastReceived(InventorySetMultipleItemsBroadcast msg, Channel channel)
		{
			foreach (InventorySetItemBroadcast subMsg in msg.Items)
			{
				Item newItem = new Item(subMsg.InstanceID, subMsg.Seed, subMsg.TemplateID, subMsg.StackSize);
				SetItemSlot(newItem, subMsg.Slot);
			}
		}

		/// <summary>
		/// Server sent a remove item from slot broadcast. Item is removed from the received slot with server authority.
		/// </summary>
		private void OnClientInventoryRemoveItemBroadcastReceived(InventoryRemoveItemBroadcast msg, Channel channel)
		{
			RemoveItem(msg.Slot);
		}

		/// <summary>
		/// Server sent a swap slot broadcast. Both slots are swapped with server authority.
		/// </summary>
		/// <param name="msg"></param>
		private void OnClientInventorySwapItemSlotsBroadcastReceived(InventorySwapItemSlotsBroadcast msg, Channel channel)
		{
			switch (msg.FromInventory)
			{
				case InventoryType.Inventory:
					SwapItemSlots(msg.From, msg.To);
					break;
				case InventoryType.Equipment:
					break;
				case InventoryType.Bank:
					if (Character.TryGet(out IBankController bankController) &&
						bankController.TryGetItem(msg.From, out Item bankItem))
					{
						if (TryGetItem(msg.To, out Item inventoryItem))
						{
							bankController.SetItemSlot(inventoryItem, msg.From);
						}
						else
						{
							bankController.SetItemSlot(null, msg.From);
						}

						SetItemSlot(bankItem, msg.To);
					}
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

		public void Activate(int index)
		{
			if (!Character.TryGet(out ICharacterDamageController damageController) ||
				!damageController.IsAlive)
			{
				//Debug.Log("Cannot activate an item while dead.");
				return;
			}
			if (TryGetItem(index, out Item item))
			{
				Debug.Log("InventoryController: using item in slot[" + index + "]");
				//items[index].OnUseItem();
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool CanSwapItemSlots(int from, int to, InventoryType fromInventory)
		{
			return !(fromInventory == InventoryType.Inventory && from == to);
		}
	}
}