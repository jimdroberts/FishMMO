using FishNet.Transporting;
using UnityEngine;

namespace FishMMO.Shared
{
	[RequireComponent(typeof(Character))]
	public class InventoryController : ItemContainer
	{
		public Character Character;

		public long Currency = 0;

		private void Awake()
		{
			AddSlots(null, 32);
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

			ClientManager.RegisterBroadcast<InventorySetItemBroadcast>(OnClientInventorySetItemBroadcastReceived);
			ClientManager.RegisterBroadcast<InventorySetMultipleItemsBroadcast>(OnClientInventorySetMultipleItemsBroadcastReceived);
			ClientManager.RegisterBroadcast<InventoryRemoveItemBroadcast>(OnClientInventoryRemoveItemBroadcastReceived);
			ClientManager.RegisterBroadcast<InventorySwapItemSlotsBroadcast>(OnClientInventorySwapItemSlotsBroadcastReceived);
		}

		public override void OnStopClient()
		{
			base.OnStopClient();

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
		private void OnClientInventorySetItemBroadcastReceived(InventorySetItemBroadcast msg)
		{
			Item newItem = new Item(msg.instanceID, msg.templateID, msg.stackSize);
			SetItemSlot(newItem, msg.slot);
		}

		/// <summary>
		/// Server sent a multiple set item broadcast. Item slot is set to the received item details.
		/// </summary>
		private void OnClientInventorySetMultipleItemsBroadcastReceived(InventorySetMultipleItemsBroadcast msg)
		{
			foreach (InventorySetItemBroadcast subMsg in msg.items)
			{
				Item newItem = new Item(subMsg.instanceID, subMsg.templateID, subMsg.stackSize);
				SetItemSlot(newItem, subMsg.slot);
			}
		}

		/// <summary>
		/// Server sent a remove item from slot broadcast. Item is removed from the received slot with server authority.
		/// </summary>
		private void OnClientInventoryRemoveItemBroadcastReceived(InventoryRemoveItemBroadcast msg)
		{
			RemoveItem(msg.slot);
		}

		/// <summary>
		/// Server sent a swap slot broadcast. Both slots are swapped with server authority.
		/// </summary>
		/// <param name="msg"></param>
		private void OnClientInventorySwapItemSlotsBroadcastReceived(InventorySwapItemSlotsBroadcast msg)
		{
			SwapItemSlots(msg.from, msg.to);
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
			if (TryGetItem(index, out Item item))
			{
				Debug.Log("InventoryController: using item in slot[" + index + "]");
				//items[index].OnUseItem();
			}
		}

		public void SendSwapItemSlotsRequest(int from, int to)
		{
			if (from != to)
			{
				ClientManager.Broadcast(new InventorySwapItemSlotsBroadcast()
				{
					from = from,
					to = to,
				}, Channel.Reliable);
			}
		}
	}
}