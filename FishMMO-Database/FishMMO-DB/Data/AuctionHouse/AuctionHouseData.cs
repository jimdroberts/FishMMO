using System;
using System.Collections.Generic;

namespace FishMMO.Database.Data
{
	/// <summary>One auction house listing as it stands in the database.</summary>
	public sealed class AuctionListingData
	{
		/// <summary>The listing's id.</summary>
		public long ID { get; set; }
		/// <summary>The seller.</summary>
		public long SellerID { get; set; }
		/// <summary>The seller's name when they posted.</summary>
		public string SellerName { get; set; } = string.Empty;
		/// <summary>The item's template.</summary>
		public int ItemTemplateID { get; set; }
		/// <summary>The item's generation seed.</summary>
		public int ItemSeed { get; set; }
		/// <summary>The item's name (lower case), as searched.</summary>
		public string ItemName { get; set; } = string.Empty;
		/// <summary>The browse category.</summary>
		public short Category { get; set; }
		/// <summary>How many are still for sale.</summary>
		public int Quantity { get; set; }
		/// <summary>The price of one.</summary>
		public long UnitPrice { get; set; }
		/// <summary>The deposit the seller paid.</summary>
		public long Deposit { get; set; }
		/// <summary>When it was posted (UTC).</summary>
		public DateTime CreatedUtc { get; set; }
		/// <summary>When it goes back to its seller (UTC).</summary>
		public DateTime ExpiresUtc { get; set; }
	}

	/// <summary>A new listing, everything about it decided by the scene server before it is written.</summary>
	public sealed class AuctionPostData
	{
		/// <summary>The seller.</summary>
		public long SellerID { get; set; }
		/// <summary>The seller's name.</summary>
		public string SellerName { get; set; } = string.Empty;
		/// <summary>The item's template.</summary>
		public int ItemTemplateID { get; set; }
		/// <summary>The item's generation seed.</summary>
		public int ItemSeed { get; set; }
		/// <summary>The item's name, lower case.</summary>
		public string ItemName { get; set; } = string.Empty;
		/// <summary>The browse category.</summary>
		public short Category { get; set; }
		/// <summary>How many.</summary>
		public int Quantity { get; set; }
		/// <summary>The price of one.</summary>
		public long UnitPrice { get; set; }
		/// <summary>The deposit paid.</summary>
		public long Deposit { get; set; }
		/// <summary>When it was posted (UTC).</summary>
		public DateTime CreatedUtc { get; set; }
		/// <summary>When it expires (UTC).</summary>
		public DateTime ExpiresUtc { get; set; }
	}

	/// <summary>How a post ended.</summary>
	public enum AuctionPostOutcome : byte
	{
		/// <summary>The listing was written (or a retry found the one its first attempt wrote).</summary>
		Posted = 0,
		/// <summary>The seller already has as many listings as they may.</summary>
		LimitReached = 1,
	}

	/// <summary>The answer to a post.</summary>
	public struct AuctionPostResult
	{
		/// <summary>How it ended.</summary>
		public AuctionPostOutcome Outcome;
		/// <summary>The new listing's id when it was posted.</summary>
		public long ListingID;
	}

	/// <summary>How a browse list is ordered.</summary>
	public enum AuctionSearchOrder : byte
	{
		/// <summary>By the price of one.</summary>
		UnitPrice = 0,
		/// <summary>By the price of the whole listing.</summary>
		TotalPrice = 1,
		/// <summary>By the time left.</summary>
		TimeLeft = 2,
		/// <summary>By the item's name.</summary>
		Name = 3,
	}

	/// <summary>One page of a browse.</summary>
	public sealed class AuctionSearchQuery
	{
		/// <summary>A piece of the item's name (any case), or empty for every name.</summary>
		public string NameContains { get; set; } = string.Empty;
		/// <summary>The category, or a negative number for every category.</summary>
		public short Category { get; set; } = -1;
		/// <summary>The order.</summary>
		public AuctionSearchOrder Order { get; set; }
		/// <summary>Largest first instead of smallest first.</summary>
		public bool Descending { get; set; }
		/// <summary>Rows to skip.</summary>
		public int Offset { get; set; }
		/// <summary>Rows to return.</summary>
		public int Limit { get; set; } = 50;
	}

	/// <summary>One page of listings and how many match in all.</summary>
	public sealed class AuctionSearchPage
	{
		/// <summary>The page.</summary>
		public List<AuctionListingData> Rows { get; set; } = new List<AuctionListingData>();
		/// <summary>Listings matching the query across every page.</summary>
		public int TotalCount { get; set; }
	}

	/// <summary>The cheapest current listing of one item.</summary>
	public struct AuctionPriceHintData
	{
		/// <summary>The lowest price of one, or 0 when none is listed.</summary>
		public long LowestUnitPrice;
		/// <summary>How many are listed in all.</summary>
		public long ListedQuantity;
	}

	/// <summary>A purchase, priced and charged by the scene server before it is written.</summary>
	public sealed class AuctionBuyData
	{
		/// <summary>The listing.</summary>
		public long ListingID { get; set; }
		/// <summary>The buyer.</summary>
		public long BuyerID { get; set; }
		/// <summary>The buyer's name, for the seller's mail.</summary>
		public string BuyerName { get; set; } = string.Empty;
		/// <summary>How many to take.</summary>
		public int Quantity { get; set; }
		/// <summary>The price of one the buyer was shown and charged at; the listing must still be at it.</summary>
		public long ExpectedUnitPrice { get; set; }
		/// <summary>What the house keeps out of the total.</summary>
		public long Cut { get; set; }
		/// <summary>The instant of the purchase (UTC, whole microseconds): both mails carry it.</summary>
		public DateTime AtUtc { get; set; }
	}

	/// <summary>How a purchase ended.</summary>
	public enum AuctionBuyOutcome : byte
	{
		/// <summary>Bought: the item is in the buyer's mail and the proceeds in the seller's.</summary>
		Bought = 0,
		/// <summary>The listing no longer exists (sold, cancelled or expired).</summary>
		Gone = 1,
		/// <summary>Fewer are left than were asked for.</summary>
		NotEnough = 2,
		/// <summary>The listing is not at the price the buyer was charged.</summary>
		PriceChanged = 3,
		/// <summary>The buyer is its seller.</summary>
		OwnListing = 4,
	}

	/// <summary>The answer to a purchase.</summary>
	public struct AuctionBuyResult
	{
		/// <summary>How it ended.</summary>
		public AuctionBuyOutcome Outcome;
		/// <summary>The item bought.</summary>
		public int ItemTemplateID;
		/// <summary>Its seed.</summary>
		public int ItemSeed;
		/// <summary>How many were bought (or, for <see cref="AuctionBuyOutcome.NotEnough"/>, how many are left).</summary>
		public int Quantity;
		/// <summary>The seller.</summary>
		public long SellerID;
	}

	/// <summary>How a cancellation ended.</summary>
	public enum AuctionCancelOutcome : byte
	{
		/// <summary>Cancelled: the stack is in its seller's mail.</summary>
		Cancelled = 0,
		/// <summary>The listing no longer exists, or is not this seller's.</summary>
		Gone = 1,
	}

	/// <summary>The answer to a cancellation.</summary>
	public struct AuctionCancelResult
	{
		/// <summary>How it ended.</summary>
		public AuctionCancelOutcome Outcome;
		/// <summary>The item handed back.</summary>
		public int ItemTemplateID;
		/// <summary>How many.</summary>
		public int Quantity;
	}

	/// <summary>A mail's subject and body.</summary>
	public readonly struct AuctionMailText
	{
		/// <summary>The subject.</summary>
		public readonly string Subject;
		/// <summary>The body.</summary>
		public readonly string Body;

		/// <summary>A subject and a body.</summary>
		public AuctionMailText(string subject, string body)
		{
			Subject = subject;
			Body = body;
		}
	}

	/// <summary>
	/// Words the auction house's mail. The database writes the mail inside the same transaction that ends the listing, so
	/// it asks the game for the words rather than knowing them.
	/// </summary>
	public interface IAuctionMailComposer
	{
		/// <summary>The sender every auction mail shows.</summary>
		string SenderName { get; }

		/// <summary>The buyer's mail, carrying what they bought. For both purchase mails <c>listing.Quantity</c> is how many are left.</summary>
		AuctionMailText Bought(AuctionListingData listing, int quantity, long total);

		/// <summary>The seller's mail, carrying the proceeds.</summary>
		AuctionMailText Sold(AuctionListingData listing, int quantity, long total, long cut, long proceeds, string buyerName);

		/// <summary>The seller's mail handing back a cancelled listing.</summary>
		AuctionMailText Cancelled(AuctionListingData listing);

		/// <summary>The seller's mail handing back a listing that ran out of time.</summary>
		AuctionMailText Expired(AuctionListingData listing);
	}
}
