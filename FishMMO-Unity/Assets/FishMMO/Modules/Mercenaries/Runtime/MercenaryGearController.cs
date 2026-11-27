using FishNet.Transporting;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// The gear a player keeps for their mercenary: one slot per equipment socket. The items are the player's own (saved in
	/// the <see cref="InventoryType.MercenaryEquipment"/> container), worn by whichever mercenary is hired, and kept when it
	/// dies or rests.
	/// </summary>
	public interface IMercenaryGear : ICharacterBehaviour, IItemContainer
	{
	}

	/// <summary>
	/// The player's mercenary gear container. Server-authoritative: the owner's copy is filled from
	/// <see cref="MercenaryGearBroadcast"/>, which always carries the whole set.
	/// </summary>
	/// <remarks>
	/// Nothing in it is equipped onto the player: the core's item load places it (<see cref="ModuleItemContainers"/>)
	/// without equipping, and the snapshot writes it with the bags in one transaction.
	/// </remarks>
	public class MercenaryGearController : ItemContainer, IMercenaryGear
	{
		/// <inheritdoc />
		public override void OnAwake()
		{
			AddSlots(null, MercenaryRules.GearSlotCount);
		}

		/// <inheritdoc />
		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);
			Clear();
		}

#if !UNITY_SERVER
		/// <inheritdoc />
		public override void OnStartCharacter()
		{
			base.OnStartCharacter();
			if (!base.IsOwner)
			{
				enabled = false;
				return;
			}
			ClientManager.RegisterBroadcast<MercenaryGearBroadcast>(OnClientGearBroadcastReceived);
		}

		/// <inheritdoc />
		public override void OnStopCharacter()
		{
			base.OnStopCharacter();
			if (base.IsOwner)
			{
				ClientManager.UnregisterBroadcast<MercenaryGearBroadcast>(OnClientGearBroadcastReceived);
			}
		}

		/// <summary>Replaces the owner's copy with the server's whole set.</summary>
		private void OnClientGearBroadcastReceived(MercenaryGearBroadcast msg, Channel channel)
		{
			ApplyGear(msg.Entries);
		}
#endif

		/// <summary>Fills the container from a whole-set listing: listed slots hold their item, the rest are emptied.</summary>
		internal void ApplyGear(MercenaryGearEntry[] entries)
		{
			int count = MercenaryRules.GearSlotCount;
			bool[] filled = new bool[count];
			if (entries != null)
			{
				for (int i = 0; i < entries.Length; ++i)
				{
					MercenaryGearEntry entry = entries[i];
					if (entry.Slot >= count)
					{
						continue;
					}
					SetItemSlot(new Item(entry.ItemID, entry.Seed, entry.TemplateID, entry.Amount), entry.Slot);
					filled[entry.Slot] = true;
				}
			}
			for (int slot = 0; slot < count; ++slot)
			{
				if (!filled[slot] && !IsSlotEmpty(slot))
				{
					SetItemSlot(null, slot);
				}
			}
		}
	}
}
