using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// The trade window's hooks into the core client: "Trade" on a player in the world and on a party roster row (a
	/// <see cref="CharacterMenuEntries"/> entry, so every menu lists its actions in one order: Add Friend, Invite to Party,
	/// Trade, Report), and right-click on an inventory slot while a trade is open puts the item on the table
	/// (<see cref="InventorySlotClaims"/>).
	/// </summary>
	public static class TradeClient
	{
		/// <summary>The menu entry's id.</summary>
		public const string EntryId = "commerce.trade";

		/// <summary>After Add Friend (100) and Invite to Party (110), before Report.</summary>
		public const int Order = 120;

		/// <summary>Registers the menu entry and the inventory claim. Runs before the first scene loads; safe to run again.</summary>
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		public static void Install()
		{
			CharacterMenuEntries.Register(EntryId, "Trade", Order, target =>
			{
				// The trade window owns the request: it pre-checks range and state, and the server applies the same
				// rules again before anyone sees an invitation.
				if (UIManager.TryGetTK("UITrade", out UITKTrade uiTrade))
				{
					uiTrade.RequestTrade(target.CharacterID);
				}
			}, target => target.Source == CharacterMenuSource.World || target.Source == CharacterMenuSource.Party);

			/* While a trade window is open, right-click means "put this on the table" for every item, wearable or
			 * not: the convention every MMO trade window follows, and the one thing a player with a trade open is
			 * trying to do with their bag. The trade panel pre-checks the slot and the server decides. */
			InventorySlotClaims.Register(EntryId, slot =>
			{
				if (!UIManager.TryGetTK("UITrade", out UITKTrade uiTrade) || !uiTrade.SessionOpen || !uiTrade.Visible)
				{
					return false;
				}
				uiTrade.TryOfferInventorySlot(slot);
				return true;
			});
		}
	}
}
