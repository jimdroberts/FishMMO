using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// The auction house: one market shared by every scene server.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Everything that moves an item or a price is one transaction with its mail.</b> A purchase takes the stack off
	/// the listing and writes the buyer's mail (the item) and the seller's mail (the proceeds) together; a cancellation and
	/// an expiry delete the listing and write the mail that hands it back together. There is no moment at which the
	/// item is in a listing and a mailbox, or in neither.
	/// </para>
	/// <para>
	/// <b>Retries find their own work.</b> The service layer retries a lost connection, which can happen after the
	/// commit. A post carries a request key; a purchase and a cancellation carry the instant they were made, which their
	/// mail is written at, and a retry that finds that mail answers with it instead of doing the work twice. An expiry
	/// sweep retried finds the rows it already deleted gone.
	/// </para>
	/// </remarks>
	public interface IAuctionHouseService
	{
		/// <summary>
		/// Writes a new listing, unless the seller already has <paramref name="maxActiveListings"/>.
		/// </summary>
		/// <remarks>
		/// The seller's character row is locked for the count, so two posts by one seller on two servers cannot both see
		/// room for one more.
		/// </remarks>
		Task<DatabaseResult<AuctionPostResult>> PostAsync(AuctionPostData post, int maxActiveListings, Guid requestKey, CancellationToken cancellationToken = default);

		/// <summary>One page of the listings still for sale at <paramref name="nowUtc"/>.</summary>
		Task<DatabaseResult<AuctionSearchPage>> SearchAsync(AuctionSearchQuery query, DateTime nowUtc, CancellationToken cancellationToken = default);

		/// <summary>Every listing one seller has, including any past its time the sweep has not reached yet.</summary>
		Task<DatabaseResult<List<AuctionListingData>>> FetchBySellerAsync(long sellerID, CancellationToken cancellationToken = default);

		/// <summary>The cheapest current price of one item, and how many are listed.</summary>
		Task<DatabaseResult<AuctionPriceHintData>> FetchPriceHintAsync(int itemTemplateID, DateTime nowUtc, CancellationToken cancellationToken = default);

		/// <summary>Buys from a listing and mails the item and the proceeds.</summary>
		Task<DatabaseResult<AuctionBuyResult>> BuyAsync(AuctionBuyData buy, IAuctionMailComposer composer, CancellationToken cancellationToken = default);

		/// <summary>Takes a seller's listing down and mails the stack back.</summary>
		/// <param name="atUtc">The instant of the cancellation (whole microseconds): the mail carries it.</param>
		Task<DatabaseResult<AuctionCancelResult>> CancelAsync(long listingID, long sellerID, DateTime atUtc, IAuctionMailComposer composer, CancellationToken cancellationToken = default);

		/// <summary>
		/// Ends up to <paramref name="batchSize"/> listings past their time and mails each stack back.
		/// </summary>
		/// <remarks>Safe on every scene server at once: rows another sweep holds are skipped, not waited for.</remarks>
		/// <returns>How many listings were ended.</returns>
		Task<DatabaseResult<int>> ExpireAsync(DateTime nowUtc, int batchSize, IAuctionMailComposer composer, CancellationToken cancellationToken = default);
	}
}
