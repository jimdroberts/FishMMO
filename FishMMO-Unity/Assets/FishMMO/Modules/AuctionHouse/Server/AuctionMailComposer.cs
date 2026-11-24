using FishMMO.Database.Data;
using FishMMO.Shared;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// The words of the auction house's mail. Called by the database service inside the transaction that ends a listing,
	/// on a worker thread, so it only reads (the item template cache is filled at load and never written after).
	/// </summary>
	internal sealed class AuctionMailComposer : IAuctionMailComposer
	{
		/// <inheritdoc />
		public string SenderName => AuctionRules.MailSenderName;

		/// <inheritdoc />
		public AuctionMailText Bought(AuctionListingData listing, int quantity, long total)
		{
			string item = ItemName(listing);
			return new AuctionMailText(
				$"Auction won: {item} x{quantity}",
				$"You bought {quantity} {item} from {listing.SellerName} for {total:N0} gold.\nTake it from this mail.");
		}

		/// <inheritdoc />
		public AuctionMailText Sold(AuctionListingData listing, int quantity, long total, long cut, long proceeds, string buyerName)
		{
			string item = ItemName(listing);
			string rest = listing.Quantity > 0 ? $"\n{listing.Quantity} are still for sale." : string.Empty;
			return new AuctionMailText(
				$"Auction sold: {item} x{quantity}",
				$"{buyerName} bought {quantity} {item} for {total:N0} gold.\nThe auction house kept {cut:N0}.\nYour proceeds: {proceeds:N0} gold.{rest}");
		}

		/// <inheritdoc />
		public AuctionMailText Cancelled(AuctionListingData listing)
		{
			string item = ItemName(listing);
			return new AuctionMailText(
				$"Auction cancelled: {item} x{listing.Quantity}",
				$"Your listing of {listing.Quantity} {item} was taken down. The deposit is not returned.\nTake the items from this mail.");
		}

		/// <inheritdoc />
		public AuctionMailText Expired(AuctionListingData listing)
		{
			string item = ItemName(listing);
			return new AuctionMailText(
				$"Auction expired: {item} x{listing.Quantity}",
				$"Nobody bought your {listing.Quantity} {item} before the listing ran out of time.\nTake the items from this mail.");
		}

		/// <summary>The item's proper name from its template, or the name stored on the listing.</summary>
		internal static string ItemName(AuctionListingData listing)
		{
			BaseItemTemplate template = BaseItemTemplate.Get<BaseItemTemplate>(listing.ItemTemplateID);
			return template != null ? template.Name : listing.ItemName;
		}
	}
}
