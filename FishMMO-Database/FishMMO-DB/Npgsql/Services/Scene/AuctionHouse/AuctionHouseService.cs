using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Database.Npgsql.Services.Interfaces;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// The auction house's listings, and the mail every finished listing turns into. See <see cref="IAuctionHouseService"/>.
	/// </summary>
	public sealed class AuctionHouseService : BaseService<AuctionListingEntity>, IAuctionHouseService
	{
		/// <summary>Every listing column, in the order <see cref="ReadListing"/> reads them.</summary>
		private const string ListingColumns =
			"id, seller_id, seller_name, item_template_id, item_seed, item_name, category, quantity, unit_price, deposit, created_utc, expires_utc";

		/// <summary>Largest page a search may ask for.</summary>
		public const int MaxPageSize = 200;

		/// <summary>Largest expiry batch.</summary>
		public const int MaxExpireBatch = 500;

		/// <summary>Creates the service.</summary>
		public AuctionHouseService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<AuctionPostResult>> PostAsync(AuctionPostData post, int maxActiveListings, Guid requestKey, CancellationToken cancellationToken = default)
		{
			if (post == null || post.SellerID <= 0 || post.ItemTemplateID == 0 || post.Quantity <= 0 || post.UnitPrice <= 0 || post.Deposit < 0)
			{
				return DatabaseResult<AuctionPostResult>.Failure(DatabaseErrorCodes.ValidationError, "Invalid listing.");
			}
			if (maxActiveListings <= 0)
			{
				return DatabaseResult<AuctionPostResult>.Success(new AuctionPostResult { Outcome = AuctionPostOutcome.LimitReached });
			}
			if (requestKey == Guid.Empty)
			{
				return DatabaseResult<AuctionPostResult>.Failure(DatabaseErrorCodes.ValidationError, "A post needs a request key.");
			}

			return await ExecuteTransactionAsync(async dbContext =>
			{
				string characters = dbContext.GetTableName<CharacterEntity>();

				// A retry after a lost commit reply: the first attempt's row is this post.
				long? mine = await ExecuteReturningOrDefaultAsync<long?>(dbContext,
					$"SELECT id FROM {TableName} WHERE request_key = {{0}}",
					new object[] { requestKey }, r => r.GetInt64(0), cancellationToken).ConfigureAwait(false);
				if (mine.HasValue)
				{
					return new AuctionPostResult { Outcome = AuctionPostOutcome.Posted, ListingID = mine.Value };
				}

				/* Lock, then count. Without the lock two posts by one seller (two servers, or a retry racing its original)
				 * both count one short of the cap and both insert. */
				await ExecuteReturningOrDefaultAsync<long?>(dbContext,
					$"SELECT id FROM {characters} WHERE id = {{0}} FOR UPDATE",
					new object[] { post.SellerID }, r => r.GetInt64(0), cancellationToken).ConfigureAwait(false);

				int active = await ExecuteScalarIntAsync(dbContext,
					$"SELECT COUNT(*)::int FROM {TableName} WHERE seller_id = {{0}}",
					new object[] { post.SellerID }, cancellationToken).ConfigureAwait(false);
				if (active >= maxActiveListings)
				{
					return new AuctionPostResult { Outcome = AuctionPostOutcome.LimitReached };
				}

				long id = await ExecuteReturningAsync(dbContext,
					$@"INSERT INTO {TableName}
						(version, seller_id, seller_name, item_template_id, item_seed, item_name, category, quantity, unit_price, deposit, created_utc, expires_utc, request_key)
					VALUES (1, {{0}}, {{1}}, {{2}}, {{3}}, {{4}}, {{5}}, {{6}}, {{7}}, {{8}}, {{9}}, {{10}}, {{11}})
					RETURNING id",
					new object[]
					{
						post.SellerID, post.SellerName ?? string.Empty, post.ItemTemplateID, post.ItemSeed, (post.ItemName ?? string.Empty).ToLowerInvariant(),
						post.Category, post.Quantity, post.UnitPrice, post.Deposit, post.CreatedUtc, post.ExpiresUtc, requestKey,
					},
					r => r.GetInt64(0), cancellationToken).ConfigureAwait(false);

				return new AuctionPostResult { Outcome = AuctionPostOutcome.Posted, ListingID = id };
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<AuctionSearchPage>> SearchAsync(AuctionSearchQuery query, DateTime nowUtc, CancellationToken cancellationToken = default)
		{
			query ??= new AuctionSearchQuery();
			int limit = Math.Clamp(query.Limit, 1, MaxPageSize);
			int offset = Math.Max(0, query.Offset);

			var where = new StringBuilder("expires_utc > {0}");
			var parameters = new List<object> { nowUtc };
			if (query.Category >= 0)
			{
				parameters.Add(query.Category);
				where.Append($" AND category = {{{parameters.Count - 1}}}");
			}
			string term = EscapeLike((query.NameContains ?? string.Empty).Trim().ToLowerInvariant());
			if (term.Length > 0)
			{
				parameters.Add("%" + term + "%");
				where.Append($" AND item_name LIKE {{{parameters.Count - 1}}} ESCAPE '\\'");
			}

			string direction = query.Descending ? "DESC" : "ASC";
			string order;
			switch (query.Order)
			{
				case AuctionSearchOrder.TotalPrice: order = $"(unit_price * quantity) {direction}"; break;
				case AuctionSearchOrder.TimeLeft: order = $"expires_utc {direction}"; break;
				case AuctionSearchOrder.Name: order = $"item_name {direction}, unit_price ASC"; break;
				default: order = $"unit_price {direction}"; break;
			}

			object[] filter = parameters.ToArray();
			parameters.Add(limit);
			int limitIndex = parameters.Count - 1;
			parameters.Add(offset);
			int offsetIndex = parameters.Count - 1;
			object[] paged = parameters.ToArray();

			return await ExecuteReadAsync(async dbContext =>
			{
				int total = await ExecuteScalarIntAsync(dbContext,
					$"SELECT COUNT(*)::int FROM {TableName} WHERE {where}", filter, cancellationToken).ConfigureAwait(false);

				List<AuctionListingData> rows = await ReadRowsAsync(dbContext,
					$"SELECT {ListingColumns} FROM {TableName} WHERE {where} ORDER BY {order}, id ASC LIMIT {{{limitIndex}}} OFFSET {{{offsetIndex}}}",
					paged, ReadListing, cancellationToken).ConfigureAwait(false);

				return new AuctionSearchPage { Rows = rows, TotalCount = total };
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<List<AuctionListingData>>> FetchBySellerAsync(long sellerID, CancellationToken cancellationToken = default)
		{
			return await ExecuteReadAsync(async dbContext =>
			{
				return await ReadRowsAsync(dbContext,
					$"SELECT {ListingColumns} FROM {TableName} WHERE seller_id = {{0}} ORDER BY expires_utc ASC, id ASC",
					new object[] { sellerID }, ReadListing, cancellationToken).ConfigureAwait(false);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<AuctionPriceHintData>> FetchPriceHintAsync(int itemTemplateID, DateTime nowUtc, CancellationToken cancellationToken = default)
		{
			return await ExecuteReadAsync(async dbContext =>
			{
				return await ExecuteReturningAsync(dbContext,
					$"SELECT COALESCE(MIN(unit_price), 0), COALESCE(SUM(quantity), 0) FROM {TableName} WHERE item_template_id = {{0}} AND expires_utc > {{1}}",
					new object[] { itemTemplateID, nowUtc },
					r => new AuctionPriceHintData { LowestUnitPrice = r.GetInt64(0), ListedQuantity = Convert.ToInt64(r.GetValue(1)) },
					cancellationToken).ConfigureAwait(false);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<AuctionBuyResult>> BuyAsync(AuctionBuyData buy, IAuctionMailComposer composer, CancellationToken cancellationToken = default)
		{
			if (buy == null || composer == null || buy.ListingID <= 0 || buy.BuyerID <= 0 || buy.Quantity <= 0 || buy.ExpectedUnitPrice <= 0 || buy.Cut < 0)
			{
				return DatabaseResult<AuctionBuyResult>.Failure(DatabaseErrorCodes.ValidationError, "Invalid purchase.");
			}
			long total = buy.ExpectedUnitPrice * buy.Quantity;
			long proceeds = total - buy.Cut;
			if (proceeds < 0 || total > int.MaxValue)
			{
				return DatabaseResult<AuctionBuyResult>.Failure(DatabaseErrorCodes.ValidationError, "The purchase does not fit in a mail.");
			}

			return await ExecuteTransactionAsync(async dbContext =>
			{
				string mail = dbContext.GetTableName<CharacterMailEntity>();

				// A retry after a lost commit reply: the buyer's mail at this instant is this purchase.
				AuctionBuyResult? done = await ExecuteReturningOrDefaultAsync<AuctionBuyResult?>(dbContext,
					$@"SELECT item_attachment_template_id, item_attachment_seed, item_attachment_amount, sender_id FROM {mail}
					WHERE character_id = {{0}} AND time_created = {{1}} AND sender_name = {{2}} LIMIT 1",
					new object[] { buy.BuyerID, buy.AtUtc, composer.SenderName },
					r => new AuctionBuyResult
					{
						Outcome = AuctionBuyOutcome.Bought,
						ItemTemplateID = r.GetInt32(0),
						ItemSeed = r.GetInt32(1),
						Quantity = (int)Convert.ToInt64(r.GetValue(2)),
						SellerID = r.GetInt64(3),
					}, cancellationToken).ConfigureAwait(false);
				if (done.HasValue)
				{
					return done.Value;
				}

				/* Every condition a purchase needs is in the statement that takes the stack, so it holds under the row lock.
				 * Taking the last of them deletes the listing (a row never holds zero: the table refuses it); taking fewer
				 * leaves the rest. Exactly one of the two can match. */
				const string Conditions = "id = {0} AND unit_price = {2} AND seller_id <> {3} AND expires_utc > {4}";
				object[] parameters = { buy.ListingID, buy.Quantity, buy.ExpectedUnitPrice, buy.BuyerID, buy.AtUtc };

				AuctionListingData? taken = await ExecuteReturningOrDefaultAsync(dbContext,
					$"DELETE FROM {TableName} WHERE {Conditions} AND quantity = {{1}} RETURNING {ListingColumns}",
					parameters, ReadListing, cancellationToken).ConfigureAwait(false);

				// From here taken.Quantity is how many are left: none when the last were taken, the UPDATE's new value otherwise.
				if (taken != null)
				{
					taken.Quantity = 0;
				}

				taken ??= await ExecuteReturningOrDefaultAsync(dbContext,
					$@"UPDATE {TableName} SET quantity = quantity - {{1}}, version = version + 1
					WHERE {Conditions} AND quantity > {{1}}
					RETURNING {ListingColumns}",
					parameters, ReadListing, cancellationToken).ConfigureAwait(false);

				if (taken == null)
				{
					return await DiagnoseRefusedBuyAsync(dbContext, buy, cancellationToken).ConfigureAwait(false);
				}

				AuctionMailText bought = composer.Bought(taken, buy.Quantity, total);
				await InsertMailAsync(dbContext, mail, taken.SellerID, composer.SenderName, buy.BuyerID, bought,
					taken.ItemTemplateID, taken.ItemSeed, buy.Quantity, 0, buy.AtUtc, cancellationToken).ConfigureAwait(false);

				AuctionMailText sold = composer.Sold(taken, buy.Quantity, total, buy.Cut, proceeds, buy.BuyerName ?? string.Empty);
				await InsertMailAsync(dbContext, mail, buy.BuyerID, composer.SenderName, taken.SellerID, sold,
					0, 0, 0, (int)proceeds, buy.AtUtc, cancellationToken).ConfigureAwait(false);

				return new AuctionBuyResult
				{
					Outcome = AuctionBuyOutcome.Bought,
					ItemTemplateID = taken.ItemTemplateID,
					ItemSeed = taken.ItemSeed,
					Quantity = buy.Quantity,
					SellerID = taken.SellerID,
				};
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <summary>Why a purchase's take matched nothing. Read only; the transaction changes nothing.</summary>
		private async Task<AuctionBuyResult> DiagnoseRefusedBuyAsync(NpgsqlDbContext dbContext, AuctionBuyData buy, CancellationToken cancellationToken)
		{
			AuctionListingData? row = await ExecuteReturningOrDefaultAsync(dbContext,
				$"SELECT {ListingColumns} FROM {TableName} WHERE id = {{0}}",
				new object[] { buy.ListingID }, ReadListing, cancellationToken).ConfigureAwait(false);

			if (row == null || row.ExpiresUtc <= buy.AtUtc)
			{
				return new AuctionBuyResult { Outcome = AuctionBuyOutcome.Gone };
			}
			if (row.SellerID == buy.BuyerID)
			{
				return new AuctionBuyResult { Outcome = AuctionBuyOutcome.OwnListing };
			}
			if (row.UnitPrice != buy.ExpectedUnitPrice)
			{
				return new AuctionBuyResult { Outcome = AuctionBuyOutcome.PriceChanged };
			}
			return new AuctionBuyResult { Outcome = AuctionBuyOutcome.NotEnough, Quantity = row.Quantity };
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<AuctionCancelResult>> CancelAsync(long listingID, long sellerID, DateTime atUtc, IAuctionMailComposer composer, CancellationToken cancellationToken = default)
		{
			if (listingID <= 0 || sellerID <= 0 || composer == null)
			{
				return DatabaseResult<AuctionCancelResult>.Failure(DatabaseErrorCodes.ValidationError, "Invalid cancellation.");
			}

			return await ExecuteTransactionAsync(async dbContext =>
			{
				string mail = dbContext.GetTableName<CharacterMailEntity>();

				AuctionCancelResult? done = await ExecuteReturningOrDefaultAsync<AuctionCancelResult?>(dbContext,
					$@"SELECT item_attachment_template_id, item_attachment_amount FROM {mail}
					WHERE character_id = {{0}} AND sender_id = {{0}} AND time_created = {{1}} AND sender_name = {{2}} LIMIT 1",
					new object[] { sellerID, atUtc, composer.SenderName },
					r => new AuctionCancelResult
					{
						Outcome = AuctionCancelOutcome.Cancelled,
						ItemTemplateID = r.GetInt32(0),
						Quantity = (int)Convert.ToInt64(r.GetValue(1)),
					}, cancellationToken).ConfigureAwait(false);
				if (done.HasValue)
				{
					return done.Value;
				}

				AuctionListingData? gone = await ExecuteReturningOrDefaultAsync(dbContext,
					$"DELETE FROM {TableName} WHERE id = {{0}} AND seller_id = {{1}} RETURNING {ListingColumns}",
					new object[] { listingID, sellerID }, ReadListing, cancellationToken).ConfigureAwait(false);
				if (gone == null)
				{
					return new AuctionCancelResult { Outcome = AuctionCancelOutcome.Gone };
				}

				await InsertMailAsync(dbContext, mail, sellerID, composer.SenderName, sellerID, composer.Cancelled(gone),
					gone.ItemTemplateID, gone.ItemSeed, gone.Quantity, 0, atUtc, cancellationToken).ConfigureAwait(false);

				return new AuctionCancelResult
				{
					Outcome = AuctionCancelOutcome.Cancelled,
					ItemTemplateID = gone.ItemTemplateID,
					Quantity = gone.Quantity,
				};
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> ExpireAsync(DateTime nowUtc, int batchSize, IAuctionMailComposer composer, CancellationToken cancellationToken = default)
		{
			if (composer == null)
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "No mail composer.");
			}
			int batch = Math.Clamp(batchSize, 1, MaxExpireBatch);

			return await ExecuteTransactionAsync(async dbContext =>
			{
				string mail = dbContext.GetTableName<CharacterMailEntity>();

				/* SKIP LOCKED, so every scene server can sweep at once: each takes rows nobody else holds, and a listing being
				 * bought right now is left for the next sweep instead of being waited on. */
				List<AuctionListingData> expired = await ReadRowsAsync(dbContext,
					$@"DELETE FROM {TableName} WHERE id IN (
						SELECT id FROM {TableName} WHERE expires_utc <= {{0}} ORDER BY expires_utc LIMIT {{1}} FOR UPDATE SKIP LOCKED)
					RETURNING {ListingColumns}",
					new object[] { nowUtc, batch }, ReadListing, cancellationToken).ConfigureAwait(false);

				for (int i = 0; i < expired.Count; i++)
				{
					AuctionListingData row = expired[i];
					await InsertMailAsync(dbContext, mail, row.SellerID, composer.SenderName, row.SellerID, composer.Expired(row),
						row.ItemTemplateID, row.ItemSeed, row.Quantity, 0, nowUtc, cancellationToken).ConfigureAwait(false);
				}
				return expired.Count;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <summary>Writes one auction mail on the ambient transaction.</summary>
		private static async Task InsertMailAsync(NpgsqlDbContext dbContext, string mailTable, long senderID, string senderName, long recipientID,
			AuctionMailText text, int itemTemplateID, int itemSeed, int itemAmount, int currency, DateTime atUtc, CancellationToken cancellationToken)
		{
			await ExecuteScalarIntAsync(dbContext,
				$@"WITH sent AS (
					INSERT INTO {mailTable}
						(sender_id, sender_name, character_id, subject, body, item_attachment_template_id, item_attachment_seed, item_attachment_amount, currency_attachment, read, version, time_created, deleted, time_deleted)
					VALUES ({{0}}, {{1}}, {{2}}, {{3}}, {{4}}, {{5}}, {{6}}, {{7}}, {{8}}, FALSE, 1, {{9}}, FALSE, NULL)
					RETURNING 1)
				SELECT COUNT(*)::int FROM sent",
				new object[]
				{
					senderID, senderName ?? string.Empty, recipientID, text.Subject ?? string.Empty, text.Body ?? string.Empty,
					itemTemplateID, itemSeed, (long)itemAmount, currency, atUtc,
				},
				cancellationToken).ConfigureAwait(false);
		}

		/// <summary>Reads a row selected with <see cref="ListingColumns"/>.</summary>
		private static AuctionListingData ReadListing(DbDataReader r)
		{
			return new AuctionListingData
			{
				ID = r.GetInt64(0),
				SellerID = r.GetInt64(1),
				SellerName = r.IsDBNull(2) ? string.Empty : r.GetString(2),
				ItemTemplateID = r.GetInt32(3),
				ItemSeed = r.GetInt32(4),
				ItemName = r.IsDBNull(5) ? string.Empty : r.GetString(5),
				Category = r.GetInt16(6),
				Quantity = r.GetInt32(7),
				UnitPrice = r.GetInt64(8),
				Deposit = r.GetInt64(9),
				CreatedUtc = DateTime.SpecifyKind(r.GetDateTime(10), DateTimeKind.Utc),
				ExpiresUtc = DateTime.SpecifyKind(r.GetDateTime(11), DateTimeKind.Utc),
			};
		}

		/// <summary>Escapes LIKE's wildcards (and its escape character) in a user's search text.</summary>
		public static string EscapeLike(string text)
		{
			if (string.IsNullOrEmpty(text))
			{
				return string.Empty;
			}
			var sb = new StringBuilder(text.Length);
			foreach (char c in text)
			{
				if (c == '\\' || c == '%' || c == '_')
				{
					sb.Append('\\');
				}
				sb.Append(c);
			}
			return sb.ToString();
		}
	}
}
