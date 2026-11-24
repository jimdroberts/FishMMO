using System;

namespace FishMMO.Shared
{
	/// <summary>How long a listing runs. The deposit grows with it.</summary>
	public enum AuctionDuration : byte
	{
		/// <summary>12 hours.</summary>
		Short = 0,
		/// <summary>24 hours.</summary>
		Medium = 1,
		/// <summary>48 hours.</summary>
		Long = 2,
	}

	/// <summary>What shelf a listing is browsed under, decided by the server from the item's template.</summary>
	/// <remarks>Stored on the listing row, so the values are persisted: append, never renumber.</remarks>
	public enum AuctionCategory : byte
	{
		/// <summary>Every category (a browse filter only; no listing has it).</summary>
		All = 0,
		/// <summary>Weapons.</summary>
		Weapons = 1,
		/// <summary>Armour.</summary>
		Armor = 2,
		/// <summary>Food, potions and other things used up.</summary>
		Consumables = 3,
		/// <summary>Crafting materials.</summary>
		Materials = 4,
		/// <summary>Housing structure kits.</summary>
		StructureKits = 5,
		/// <summary>Anything else worn (saddles, trinkets).</summary>
		OtherGear = 6,
		/// <summary>Everything else.</summary>
		Other = 7,
	}

	/// <summary>How the browse list is ordered. Matches the database's order one for one.</summary>
	public enum AuctionSort : byte
	{
		/// <summary>By the price of one.</summary>
		UnitPrice = 0,
		/// <summary>By the price of the whole listing.</summary>
		TotalPrice = 1,
		/// <summary>By the time left.</summary>
		TimeLeft = 2,
		/// <summary>By name.</summary>
		Name = 3,
	}

	/// <summary>The request a result answers.</summary>
	public enum AuctionOperation : byte
	{
		/// <summary>Posting a listing.</summary>
		Post = 0,
		/// <summary>Buying from a listing.</summary>
		Buy = 1,
		/// <summary>Cancelling a listing.</summary>
		Cancel = 2,
	}

	/// <summary>How an auction request ended.</summary>
	public enum AuctionResult : byte
	{
		/// <summary>Done.</summary>
		Success = 0,
		/// <summary>Something failed on the server; nothing changed.</summary>
		ServerError = 1,
		/// <summary>The player is not at an Auctioneer.</summary>
		NoAuctioneer = 2,
		/// <summary>That slot holds nothing that can be sold.</summary>
		InvalidItem = 3,
		/// <summary>The price is not allowed.</summary>
		InvalidPrice = 4,
		/// <summary>The quantity is not allowed.</summary>
		InvalidQuantity = 5,
		/// <summary>The player cannot pay.</summary>
		NotEnoughCurrency = 6,
		/// <summary>The seller already has as many listings as allowed.</summary>
		LimitReached = 7,
		/// <summary>The listing is sold, cancelled or expired.</summary>
		Gone = 8,
		/// <summary>Fewer are left than were asked for.</summary>
		NotEnough = 9,
		/// <summary>The listing is not at the price shown.</summary>
		PriceChanged = 10,
		/// <summary>A player cannot buy their own listing.</summary>
		OwnListing = 11,
		/// <summary>The server is busy, or a request is still being answered.</summary>
		Busy = 12,
		/// <summary>The auction house is closed on this server.</summary>
		Disabled = 13,
		/// <summary>The player cannot act now (dead, stunned, mid-transfer).</summary>
		CannotAct = 14,
	}

	/// <summary>
	/// The auction house's arithmetic and limits, shared by the server (which enforces them) and the window (which shows
	/// them before a request is sent).
	/// </summary>
	public static class AuctionRules
	{
		/// <summary>Listings one character may have at once, unless the server asset says otherwise.</summary>
		public const int DefaultMaxActiveListings = 10;

		/// <summary>Rows in one page of the browse list.</summary>
		public const int PageSize = 50;

		/// <summary>Longest name search, in characters.</summary>
		public const int MaxSearchLength = 64;

		/// <summary>The default deposit: this share of the item's merchant price per 12 hours listed.</summary>
		public const float DefaultDepositPercent = 5f;

		/// <summary>The default share of a sale the house keeps.</summary>
		public const float DefaultCutPercent = 5f;

		/// <summary>
		/// The most one listing may be worth in all. Proceeds and refunds travel in mail, whose currency is a 32-bit
		/// number, so a listing must fit in one.
		/// </summary>
		public const long MaxListingTotal = int.MaxValue;

		/// <summary>The sender every auction mail shows.</summary>
		public const string MailSenderName = "Auction House";

		/// <summary>Hours a duration runs.</summary>
		public static int Hours(AuctionDuration duration)
		{
			switch (duration)
			{
				case AuctionDuration.Short: return 12;
				case AuctionDuration.Long: return 48;
				default: return 24;
			}
		}

		/// <summary>True for a duration the wire may carry.</summary>
		public static bool IsValid(AuctionDuration duration)
		{
			return duration == AuctionDuration.Short || duration == AuctionDuration.Medium || duration == AuctionDuration.Long;
		}

		/// <summary>
		/// The price of a whole listing, when it is allowed: at least one, at a price of at least one, worth no more than
		/// <see cref="MaxListingTotal"/>.
		/// </summary>
		public static bool TryTotal(long unitPrice, long quantity, out long total)
		{
			total = 0;
			if (unitPrice <= 0 || quantity <= 0 || unitPrice > MaxListingTotal || quantity > MaxListingTotal)
			{
				return false;
			}
			total = unitPrice * quantity;
			return total <= MaxListingTotal;
		}

		/// <summary>
		/// The deposit for a listing: <paramref name="percent"/> of the item's merchant price, times how many, per 12 hours
		/// listed, rounded up and never under <paramref name="minimum"/>. It is not returned, whatever becomes of the
		/// listing: what it is for is making a listing nobody wants cost something.
		/// </summary>
		/// <remarks>From the merchant price, not the asking price, so a seller cannot shrink it by asking less.</remarks>
		public static long Deposit(int merchantPrice, int quantity, AuctionDuration duration, float percent, long minimum)
		{
			minimum = Math.Max(0, minimum);
			if (merchantPrice <= 0 || quantity <= 0 || percent <= 0f)
			{
				return minimum;
			}
			double periods = Hours(duration) / 12.0;
			double raw = (double)merchantPrice * quantity * (percent / 100.0) * periods;
			long deposit = raw >= MaxListingTotal ? MaxListingTotal : (long)Math.Ceiling(raw - 1e-9);
			return Math.Max(minimum, deposit);
		}

		/// <summary>
		/// What the house keeps of a sale worth <paramref name="total"/>: <paramref name="percent"/> of it, rounded down,
		/// so a seller always receives at least the rest.
		/// </summary>
		public static long Cut(long total, float percent)
		{
			if (total <= 0 || percent <= 0f)
			{
				return 0;
			}
			if (percent >= 100f)
			{
				return total;
			}
			long cut = (long)Math.Floor(total * (percent / 100.0) + 1e-9);
			return Math.Max(0, Math.Min(total, cut));
		}

		/// <summary>The shelf an item is browsed under.</summary>
		public static AuctionCategory Categorize(BaseItemTemplate template)
		{
			switch (template)
			{
				case null: return AuctionCategory.Other;
				case WeaponTemplate _: return AuctionCategory.Weapons;
				case ArmorTemplate _: return AuctionCategory.Armor;
				case EquippableItemTemplate _: return AuctionCategory.OtherGear;
				case ConsumableTemplate _: return AuctionCategory.Consumables;
				case CraftingMaterialTemplate _: return AuctionCategory.Materials;
				case StructureKitTemplate _: return AuctionCategory.StructureKits;
				default: return AuctionCategory.Other;
			}
		}

		/// <summary>A category's name in the window.</summary>
		public static string Label(AuctionCategory category)
		{
			switch (category)
			{
				case AuctionCategory.Weapons: return "Weapons";
				case AuctionCategory.Armor: return "Armour";
				case AuctionCategory.Consumables: return "Consumables";
				case AuctionCategory.Materials: return "Materials";
				case AuctionCategory.StructureKits: return "Structure kits";
				case AuctionCategory.OtherGear: return "Other gear";
				case AuctionCategory.Other: return "Other";
				default: return "All";
			}
		}

		/// <summary>A listing's time left as the window shows it.</summary>
		public static string FormatTimeLeft(int seconds)
		{
			if (seconds <= 0)
			{
				return "Expired";
			}
			if (seconds < 3600)
			{
				return "< 1h";
			}
			return $"{seconds / 3600}h";
		}

		/// <summary>A search text the server will accept: trimmed and no longer than <see cref="MaxSearchLength"/>.</summary>
		public static string CleanSearch(string text)
		{
			if (string.IsNullOrWhiteSpace(text))
			{
				return string.Empty;
			}
			text = text.Trim();
			return text.Length > MaxSearchLength ? text.Substring(0, MaxSearchLength) : text;
		}

		/// <summary>What a result tells the player.</summary>
		public static string Describe(AuctionOperation operation, AuctionResult result)
		{
			switch (result)
			{
				case AuctionResult.Success:
					switch (operation)
					{
						case AuctionOperation.Post: return "Listed.";
						case AuctionOperation.Buy: return "Bought. It is waiting in your mailbox.";
						default: return "Cancelled. The item is waiting in your mailbox.";
					}
				case AuctionResult.NoAuctioneer: return "You are too far from the Auctioneer.";
				case AuctionResult.InvalidItem: return "That cannot be sold here.";
				case AuctionResult.InvalidPrice: return "That price is not allowed.";
				case AuctionResult.InvalidQuantity: return "That quantity is not allowed.";
				case AuctionResult.NotEnoughCurrency: return operation == AuctionOperation.Post ? "You cannot afford the deposit." : "You cannot afford that.";
				case AuctionResult.LimitReached: return "You already have as many listings as allowed.";
				case AuctionResult.Gone: return "That listing is no longer for sale.";
				case AuctionResult.NotEnough: return "There are not that many left.";
				case AuctionResult.PriceChanged: return "That listing's price has changed.";
				case AuctionResult.OwnListing: return "You cannot buy your own listing.";
				case AuctionResult.Busy: return "Still working on your last request.";
				case AuctionResult.Disabled: return "The auction house is closed.";
				case AuctionResult.CannotAct: return "You cannot do that right now.";
				default: return "Something went wrong. Nothing was changed.";
			}
		}
	}
}
