using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Transporting;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	public partial class MercenarySystem
	{
		/// <summary>
		/// Puts an item from the bags into the mercenary gear, in the slot it goes in (swapping out what is there), and onto
		/// the mercenary when one is out. Allowed with no mercenary: the gear is the player's, kept for the next hire.
		/// </summary>
		private void OnEquip(NetworkConnection conn, MercenaryEquipBroadcast msg, Channel channel)
		{
			if (!TryBeginPlayerRequest(conn, out PlayerRequestContext request))
			{
				return;
			}
			if (!TryBeginIngressGuard(conn, IngressOperation.Gear, out long guardKey))
			{
				return;
			}
			try
			{
				IPlayerCharacter character = request.Character;
				if (!character.TryGet(out IInventoryController inventory) || !character.TryGet(out IMercenaryGear gear) ||
					!Server.BehaviourRegistry.TryGet(out ICharacterInventorySystem inventorySystem))
				{
					return;
				}
				if (!inventory.IsValidSlot(msg.InventorySlot) || !inventory.TryGetItem(msg.InventorySlot, out Item item))
				{
					return;
				}
				int gearSlot = MercenaryRules.GearSlotFor(item.Template);
				if (!MercenaryRules.Fits(item, gearSlot) || item.ID <= 0)
				{
					// Not gear, or an item still waiting for its identity (its slot is locked until it has one).
					SendResult(character, MercenaryResult.DoesNotFit, null);
					return;
				}

				if (!inventorySystem.TryMoveBetweenContainers(character, inventory, InventoryType.Inventory, msg.InventorySlot,
					gear, InventoryType.MercenaryEquipment, gearSlot, "MercenaryEquip", out List<Item> nowInBags, out List<Item> nowWorn))
				{
					SendResult(character, MercenaryResult.Busy, null);
					return;
				}

				Mercenary mercenary = live.TryGetValue(character.ID, out LiveEntry entry) ? entry.Mercenary : null;
				if (mercenary != null)
				{
					for (int i = 0; i < nowInBags.Count; ++i)
					{
						mercenary.TakeOff(nowInBags[i]);
					}
					for (int i = 0; i < nowWorn.Count; ++i)
					{
						mercenary.Wear(nowWorn[i]);
					}
				}

				inventorySystem.NotifyInventorySlots(character, nowInBags, nowInBags.Count == 0 ? new[] { msg.InventorySlot } : null);
				SendGear(character);
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		/// <summary>Takes an item out of the mercenary gear (and off the mercenary) into the first free bag slot.</summary>
		private void OnUnequip(NetworkConnection conn, MercenaryUnequipBroadcast msg, Channel channel)
		{
			if (!TryBeginPlayerRequest(conn, out PlayerRequestContext request))
			{
				return;
			}
			if (!TryBeginIngressGuard(conn, IngressOperation.Gear, out long guardKey))
			{
				return;
			}
			try
			{
				IPlayerCharacter character = request.Character;
				if (!character.TryGet(out IInventoryController inventory) || !character.TryGet(out IMercenaryGear gear) ||
					!Server.BehaviourRegistry.TryGet(out ICharacterInventorySystem inventorySystem))
				{
					return;
				}
				if (!gear.IsValidSlot(msg.GearSlot) || !gear.TryGetItem(msg.GearSlot, out Item item))
				{
					return;
				}
				int bagSlot = FirstFreeSlot(inventory);
				if (bagSlot < 0)
				{
					SendResult(character, MercenaryResult.NoRoom, null);
					return;
				}

				if (!inventorySystem.TryMoveBetweenContainers(character, gear, InventoryType.MercenaryEquipment, msg.GearSlot,
					inventory, InventoryType.Inventory, bagSlot, "MercenaryUnequip", out List<Item> _, out List<Item> nowInBags))
				{
					SendResult(character, MercenaryResult.Busy, null);
					return;
				}

				if (live.TryGetValue(character.ID, out LiveEntry entry) && entry.Mercenary != null)
				{
					entry.Mercenary.TakeOff(item);
				}

				inventorySystem.NotifyInventorySlots(character, nowInBags, null);
				SendGear(character);
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		/// <summary>The first empty, unlocked bag slot, or -1.</summary>
		private static int FirstFreeSlot(IInventoryController inventory)
		{
			for (int i = 0; i < inventory.Items.Count; ++i)
			{
				if (inventory.IsSlotEmpty(i) && !inventory.IsSlotLocked(i))
				{
					return i;
				}
			}
			return -1;
		}
	}
}
