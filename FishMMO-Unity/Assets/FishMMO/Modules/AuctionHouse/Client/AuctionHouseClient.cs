using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// What the Auction House module adds to the client outside its window: a right-click on an inventory slot while the
	/// auction house is open puts that item on the Sell tab.
	/// </summary>
	public static class AuctionHouseClient
	{
		/// <summary>The inventory claim's id.</summary>
		public const string ClaimId = "auctionhouse.sell";

		/// <summary>Registers the claim. Runs before the first scene loads; safe to run again.</summary>
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		public static void Install()
		{
			InventorySlotClaims.Register(ClaimId, slot =>
			{
				if (!UIManager.TryGetTK(UITKAuctionHouse.ControlName, out UITKAuctionHouse window) || !window.IsOpenAtAuctioneer)
				{
					return false;
				}
				window.SelectSellSlot(slot);
				return true;
			});
		}
	}
}
