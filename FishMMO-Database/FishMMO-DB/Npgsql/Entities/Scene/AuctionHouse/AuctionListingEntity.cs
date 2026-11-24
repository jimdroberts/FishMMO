using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// One item for sale on the auction house: a stack taken out of its seller's bags, at a fixed price per unit, until it
	/// sells out, is cancelled or runs out of time.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>One market for the whole world.</b> Every scene server reads and writes this one table, so a listing posted at
	/// one town's auctioneer is for sale at every other. Nothing here belongs to a scene.
	/// </para>
	/// <para>
	/// <b>The row is the item.</b> While a listing exists its stack is in nobody's bags: the post takes it out of the
	/// seller's inventory before the row is written, and every way a listing ends (sold, cancelled, expired) deletes the
	/// row and writes the mail that carries the stack or its price on, in the same transaction. A finished listing is
	/// therefore never kept, and the table only ever holds what is for sale now.
	/// </para>
	/// <para>
	/// An item is its template, its generation seed and how many: the same three numbers a mail attachment carries, so
	/// a listing can always be handed back or delivered by mail without losing what made the item what it was.
	/// </para>
	/// </remarks>
	public class AuctionListingEntity : IVersionedEntity
	{
		/// <summary>Primary key; also the listing's id on the wire.</summary>
		public long ID { get; set; }

		/// <summary>Bumped on every write (a partial purchase).</summary>
		public long Version { get; set; }

		/// <summary>The character selling it.</summary>
		public long SellerID { get; set; }

		/// <summary>The seller's name as it was when they posted, for the browse list.</summary>
		public string SellerName { get; set; } = string.Empty;

		/// <summary>The item's template.</summary>
		public int ItemTemplateID { get; set; }

		/// <summary>The item's generation seed (0 for an item that does not generate).</summary>
		public int ItemSeed { get; set; }

		/// <summary>The template's name when it was posted, lower-cased, for the name search.</summary>
		/// <remarks>Kept on the row because the database knows nothing about item templates.</remarks>
		public string ItemName { get; set; } = string.Empty;

		/// <summary>The browse category (<c>AuctionCategory</c> in the Auction House module).</summary>
		public short Category { get; set; }

		/// <summary>How many are still for sale. Never zero: the purchase that takes the last one deletes the row.</summary>
		public int Quantity { get; set; }

		/// <summary>The price of one.</summary>
		/// <remarks>A buyer may take part of a stack, so the unit price is what the listing is for.</remarks>
		public long UnitPrice { get; set; }

		/// <summary>The deposit the seller paid to post it (kept, whatever happens to the listing).</summary>
		public long Deposit { get; set; }

		/// <summary>When it was posted (UTC).</summary>
		public DateTime CreatedUtc { get; set; }

		/// <summary>When it stops being for sale and goes back to its seller (UTC).</summary>
		public DateTime ExpiresUtc { get; set; }

		/// <summary>
		/// The post's idempotency key, so a retry after a lost commit reply finds its own row instead of listing the
		/// stack twice.
		/// </summary>
		public Guid? RequestKey { get; set; }
	}
}
