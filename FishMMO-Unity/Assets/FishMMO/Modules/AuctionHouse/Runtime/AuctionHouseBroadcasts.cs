using FishNet.Broadcast;

namespace FishMMO.Shared
{
	/// <summary>Server → client: open the auction house window at this Auctioneer.</summary>
	public struct AuctioneerBroadcast : IBroadcast
	{
		/// <summary>The Auctioneer's scene object id; every request names it back.</summary>
		public long InteractableID;
	}

	/// <summary>One listing as the window shows it.</summary>
	public struct AuctionListingEntry
	{
		/// <summary>The listing's id.</summary>
		public long ListingID;
		/// <summary>The item's template.</summary>
		public int TemplateID;
		/// <summary>The item's generation seed, so the window can show the item it really is.</summary>
		public int Seed;
		/// <summary>How many are left.</summary>
		public int Quantity;
		/// <summary>The price of one.</summary>
		public long UnitPrice;
		/// <summary>The seller.</summary>
		public string SellerName;
		/// <summary>Seconds until it expires (0 or less: expired, about to be handed back).</summary>
		public int SecondsLeft;
		/// <summary>True when the player asking is its seller.</summary>
		public bool Own;
	}

	/// <summary>Client → server: one page of the browse list.</summary>
	/// <remarks>A read: it names a filter and a page, never a listing or a price.</remarks>
	public struct AuctionSearchBroadcast : IBroadcast
	{
		/// <summary>The Auctioneer in use.</summary>
		public long InteractableID;
		/// <summary>A piece of the item's name, or empty.</summary>
		public string Name;
		/// <summary>The shelf, or <see cref="AuctionCategory.All"/>.</summary>
		public AuctionCategory Category;
		/// <summary>The order.</summary>
		public AuctionSort Sort;
		/// <summary>Largest first.</summary>
		public bool Descending;
		/// <summary>The page, from 0.</summary>
		public int Page;
	}

	/// <summary>Server → client: one page of the browse list.</summary>
	public struct AuctionSearchResultBroadcast : IBroadcast
	{
		/// <summary>The page's listings.</summary>
		public AuctionListingEntry[] Entries;
		/// <summary>The page, from 0.</summary>
		public int Page;
		/// <summary>Listings matching across every page.</summary>
		public int TotalCount;
	}

	/// <summary>Client → server: the player's own listings.</summary>
	public struct AuctionMyListingsRequestBroadcast : IBroadcast
	{
		/// <summary>The Auctioneer in use.</summary>
		public long InteractableID;
	}

	/// <summary>Server → client: the player's own listings.</summary>
	public struct AuctionMyListingsBroadcast : IBroadcast
	{
		/// <summary>Every listing the player has.</summary>
		public AuctionListingEntry[] Entries;
		/// <summary>How many they may have.</summary>
		public int MaxListings;
	}

	/// <summary>Client → server: the cheapest current price of an item the player is about to sell.</summary>
	public struct AuctionPriceHintRequestBroadcast : IBroadcast
	{
		/// <summary>The Auctioneer in use.</summary>
		public long InteractableID;
		/// <summary>The item's template.</summary>
		public int TemplateID;
	}

	/// <summary>Server → client: the cheapest current price of an item, and the server's fees.</summary>
	public struct AuctionPriceHintBroadcast : IBroadcast
	{
		/// <summary>The item's template.</summary>
		public int TemplateID;
		/// <summary>The lowest price of one listed now, or 0 when none is.</summary>
		public long LowestUnitPrice;
		/// <summary>How many are listed now.</summary>
		public long ListedQuantity;
		/// <summary>The server's deposit rate (percent of merchant price per 12 hours).</summary>
		public float DepositPercent;
		/// <summary>The server's minimum deposit.</summary>
		public long MinimumDeposit;
		/// <summary>The server's cut (percent of a sale).</summary>
		public float CutPercent;
	}

	/// <summary>Client → server: list a stack from the bags.</summary>
	/// <remarks>
	/// Names an inventory slot and a quantity, like every item request: what is in the slot is what is listed, and the
	/// server prices the deposit itself.
	/// </remarks>
	public struct AuctionPostBroadcast : IBroadcast
	{
		/// <summary>The Auctioneer in use.</summary>
		public long InteractableID;
		/// <summary>The inventory slot.</summary>
		public int Slot;
		/// <summary>How many of the stack to list (clamped to the stack).</summary>
		public int Quantity;
		/// <summary>The asking price of one.</summary>
		public long UnitPrice;
		/// <summary>How long it runs.</summary>
		public AuctionDuration Duration;
	}

	/// <summary>Client → server: buy from a listing.</summary>
	/// <remarks>
	/// Carries the price of one the player was shown. The server charges that, and the purchase only goes through while
	/// the listing is still at it; a client that names a lower price than the listing's is refused, not undercharged.
	/// </remarks>
	public struct AuctionBuyBroadcast : IBroadcast
	{
		/// <summary>The Auctioneer in use.</summary>
		public long InteractableID;
		/// <summary>The listing.</summary>
		public long ListingID;
		/// <summary>How many to buy.</summary>
		public int Quantity;
		/// <summary>The price of one the player was shown.</summary>
		public long ExpectedUnitPrice;
	}

	/// <summary>Client → server: take one of the player's listings down.</summary>
	public struct AuctionCancelBroadcast : IBroadcast
	{
		/// <summary>The Auctioneer in use.</summary>
		public long InteractableID;
		/// <summary>The listing.</summary>
		public long ListingID;
	}

	/// <summary>Server → client: how a post, purchase or cancellation ended. Every one of those gets exactly one.</summary>
	public struct AuctionResultBroadcast : IBroadcast
	{
		/// <summary>What was asked.</summary>
		public AuctionOperation Operation;
		/// <summary>How it ended.</summary>
		public AuctionResult Result;
		/// <summary>The listing it concerned (0 for a refused post).</summary>
		public long ListingID;
		/// <summary>For a refused purchase of too many: how many are left.</summary>
		public int Quantity;
		/// <summary>Currency involved: the deposit paid, or the price paid.</summary>
		public long Amount;
	}
}
