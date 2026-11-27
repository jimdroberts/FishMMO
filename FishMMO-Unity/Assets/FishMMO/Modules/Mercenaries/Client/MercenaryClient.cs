using UnityEngine;
using FishNet.Transporting;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// The client's ways into the Mercenary window: a game menu entry, and right-clicking an item in the bags while the
	/// window is open, which gives it to the mercenary (in the gear slot it goes in). Dragging it onto its socket in the
	/// window does the same (<see cref="UITKMercenary"/>).
	/// </summary>
	public static class MercenaryClient
	{
		/// <summary>The game menu entry's id.</summary>
		public const string MenuEntryId = "mercenaries.window";

		/// <summary>Where it sits in the game menu.</summary>
		public const int MenuOrder = 135;

		/// <summary>The inventory right-click claim's id.</summary>
		public const string ClaimId = "mercenaries.gear";

		/// <summary>Registers the menu entry and the right-click claim.</summary>
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		public static void Install()
		{
			GameMenuEntries.Register(MenuEntryId, "Mercenary", MenuOrder, () => UIManager.ToggleVisibility(UITKMercenary.ControlName));

			InventorySlotClaims.Register(ClaimId, slot =>
			{
				if (!UIManager.TryGetTK(UITKMercenary.ControlName, out UITKMercenary window) || !window.Visible || window.Character == null)
				{
					return false;
				}
				// Only gear is claimed: anything else right-clicked keeps its usual meaning (use, sell...).
				if (!window.Character.TryGet(out IInventoryController inventory) || !inventory.TryGetItem(slot, out Item item) ||
					MercenaryRules.GearSlotFor(item.Template) < 0)
				{
					return false;
				}
				Client.Broadcast(new MercenaryEquipBroadcast() { InventorySlot = slot }, Channel.Reliable);
				return true;
			});
		}
	}
}
