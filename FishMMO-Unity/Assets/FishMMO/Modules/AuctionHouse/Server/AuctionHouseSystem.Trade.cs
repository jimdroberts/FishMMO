using System;
using System.Threading;
using FishNet.Connection;
using FishNet.Transporting;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Logging;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>The writes: posting, buying, cancelling, and the expiry sweep.</summary>
	public partial class AuctionHouseSystem
	{
		// ── Post ─────────────────────────────────────────────────────────────────────

		/// <summary>Lists a stack from the seller's bags.</summary>
		private void OnPost(NetworkConnection conn, AuctionPostBroadcast msg, Channel channel)
		{
			if (!TryBeginPlayerRequest(conn, out PlayerRequestContext request, PlayerRequestGate.SkipCanAct))
			{
				return;
			}
			IPlayerCharacter character = request.Character;
			// Checked here rather than by the gate, so the window is told instead of waiting for an answer that never comes.
			if (!CharacterStateValidation.CanAct(character))
			{
				SendResult(conn, AuctionOperation.Post, AuctionResult.CannotAct);
				return;
			}
			if (!TryBeginGuard(conn, Operation.Post, out long guardKey))
			{
				SendResult(conn, AuctionOperation.Post, AuctionResult.Busy);
				return;
			}

			bool asyncOwnsGuard = false;
			AuctionResult result = AuctionResult.ServerError;
			try
			{
				result = ValidatePost(character, msg, out Item item, out int quantity, out long deposit);
				if (result != AuctionResult.Success)
				{
					return;
				}

				Escrow escrow = default;
				if (!TryTakeCurrency(character, deposit, CurrencyMovementReason.AuctionDeposit, ref escrow))
				{
					result = AuctionResult.NotEnoughCurrency;
					return;
				}
				BaseItemTemplate template = item.Template;
				if (!TryTakeItem(conn, character, msg.Slot, item, quantity, ref escrow))
				{
					Refund(conn, character.ID, escrow, "the item moved");
					result = AuctionResult.InvalidItem;
					return;
				}

				DateTime now = UtcNowMicroseconds();
				AuctionPostData post = new AuctionPostData
				{
					SellerID = character.ID,
					SellerName = character.CharacterName,
					ItemTemplateID = escrow.ItemTemplateID,
					ItemSeed = escrow.ItemSeed,
					ItemName = template.Name,
					Category = (short)AuctionRules.Categorize(template),
					Quantity = quantity,
					UnitPrice = msg.UnitPrice,
					Deposit = deposit,
					CreatedUtc = now,
					ExpiresUtc = now.AddHours(AuctionRules.Hours(msg.Duration)),
				};
				long characterID = character.ID;
				int max = maxActiveListings;
				Guid requestKey = Guid.NewGuid();

				if (TryEnqueueGuardedAsyncWork(async () =>
				{
					AuctionResult outcome = AuctionResult.ServerError;
					long listingID = 0;
					try
					{
						if (TryGetDbService(out IAuctionHouseService service))
						{
							DatabaseResult<AuctionPostResult> posted = await service.PostAsync(post, max, requestKey);
							if (!posted.IsSuccess)
							{
								await Log.Warning(LOG, $"Post failed for CharID={characterID}: [{posted.ErrorCode}] {posted.ErrorMessage}");
							}
							else if (posted.Data.Outcome == AuctionPostOutcome.LimitReached)
							{
								outcome = AuctionResult.LimitReached;
							}
							else
							{
								outcome = AuctionResult.Success;
								listingID = posted.Data.ListingID;
							}
						}
					}
					catch (Exception ex)
					{
						await Log.Error(LOG, $"Post threw for CharID={characterID}: {ex}");
					}
					finally
					{
						AuctionResult final = outcome;
						long id = listingID;
						if (!TryEnqueueMainThread(() =>
						{
							if (final != AuctionResult.Success)
							{
								Refund(conn, characterID, escrow, AuctionRules.Describe(AuctionOperation.Post, final));
							}
							SendResult(conn, AuctionOperation.Post, final, id, quantity, final == AuctionResult.Success ? deposit : 0);
						}))
						{
							Log.Error(LOG, $"Post: the main-thread queue refused the reply for CharID={characterID} ({final}); {(final == AuctionResult.Success ? "the listing stands" : "the escrow was NOT returned")}.");
						}
					}
				}, EndGuard, guardKey, characterID))
				{
					asyncOwnsGuard = true;
					result = AuctionResult.Success;
				}
				else
				{
					Refund(conn, characterID, escrow, "the server was busy");
					result = AuctionResult.Busy;
				}
			}
			finally
			{
				if (!asyncOwnsGuard)
				{
					EndGuard(guardKey);
					SendResult(conn, AuctionOperation.Post, result);
				}
			}
		}

		/// <summary>Everything a post can be refused for before anything is taken.</summary>
		private AuctionResult ValidatePost(IPlayerCharacter character, AuctionPostBroadcast msg, out Item item, out int quantity, out long deposit)
		{
			item = null;
			quantity = 0;
			deposit = 0;
			if (currencyTemplate == null)
			{
				return AuctionResult.Disabled;
			}
			if (!AtAuctioneer(character, msg.InteractableID))
			{
				return AuctionResult.NoAuctioneer;
			}
			if (!AuctionRules.IsValid(msg.Duration))
			{
				return AuctionResult.InvalidQuantity;
			}
			if (!character.TryGet(out IInventoryController inventory) ||
				!inventory.IsValidSlot(msg.Slot) ||
				inventory.IsSlotLocked(msg.Slot) ||
				!inventory.TryGetItem(msg.Slot, out item) ||
				item?.Template == null)
			{
				item = null;
				return AuctionResult.InvalidItem;
			}
			long available = item.IsStackable ? item.Stackable.Amount : 1;
			long requested = msg.Quantity <= 0 ? available : msg.Quantity;
			if (requested > available)
			{
				return AuctionResult.InvalidQuantity;
			}
			quantity = (int)requested;
			if (!AuctionRules.TryTotal(msg.UnitPrice, quantity, out _))
			{
				return AuctionResult.InvalidPrice;
			}
			deposit = AuctionRules.Deposit(item.Template.Price, quantity, msg.Duration, depositPercent, minimumDeposit);
			if (!CharacterCurrency.TryGetBalance(character, currencyTemplate, out long balance) || balance < deposit)
			{
				return AuctionResult.NotEnoughCurrency;
			}
			return AuctionResult.Success;
		}

		// ── Buy ──────────────────────────────────────────────────────────────────────

		/// <summary>Buys from a listing.</summary>
		private void OnBuy(NetworkConnection conn, AuctionBuyBroadcast msg, Channel channel)
		{
			if (!TryBeginPlayerRequest(conn, out PlayerRequestContext request, PlayerRequestGate.SkipCanAct))
			{
				return;
			}
			IPlayerCharacter character = request.Character;
			// Checked here rather than by the gate, so the window is told instead of waiting for an answer that never comes.
			if (!CharacterStateValidation.CanAct(character))
			{
				SendResult(conn, AuctionOperation.Buy, AuctionResult.CannotAct, msg.ListingID);
				return;
			}
			if (!TryBeginGuard(conn, Operation.Buy, out long guardKey))
			{
				SendResult(conn, AuctionOperation.Buy, AuctionResult.Busy, msg.ListingID);
				return;
			}

			bool asyncOwnsGuard = false;
			AuctionResult result = AuctionResult.ServerError;
			try
			{
				if (currencyTemplate == null)
				{
					result = AuctionResult.Disabled;
					return;
				}
				if (!AtAuctioneer(character, msg.InteractableID))
				{
					result = AuctionResult.NoAuctioneer;
					return;
				}
				if (msg.ListingID <= 0 || msg.Quantity < 1)
				{
					result = AuctionResult.InvalidQuantity;
					return;
				}
				if (!AuctionRules.TryTotal(msg.ExpectedUnitPrice, msg.Quantity, out long total))
				{
					result = AuctionResult.InvalidPrice;
					return;
				}

				Escrow escrow = default;
				if (!TryTakeCurrency(character, total, CurrencyMovementReason.AuctionPurchase, ref escrow))
				{
					result = AuctionResult.NotEnoughCurrency;
					return;
				}

				AuctionBuyData buy = new AuctionBuyData
				{
					ListingID = msg.ListingID,
					BuyerID = character.ID,
					BuyerName = character.CharacterName,
					Quantity = msg.Quantity,
					ExpectedUnitPrice = msg.ExpectedUnitPrice,
					Cut = AuctionRules.Cut(total, cutPercent),
					AtUtc = UtcNowMicroseconds(),
				};
				long characterID = character.ID;
				long listingID = msg.ListingID;

				if (TryEnqueueGuardedAsyncWork(async () =>
				{
					AuctionResult outcome = AuctionResult.ServerError;
					int left = 0;
					try
					{
						if (TryGetDbService(out IAuctionHouseService service))
						{
							DatabaseResult<AuctionBuyResult> bought = await service.BuyAsync(buy, mailComposer);
							if (!bought.IsSuccess)
							{
								await Log.Warning(LOG, $"Buy failed for CharID={characterID} listing {listingID}: [{bought.ErrorCode}] {bought.ErrorMessage}");
							}
							else
							{
								outcome = ToResult(bought.Data.Outcome);
								left = bought.Data.Outcome == AuctionBuyOutcome.NotEnough ? bought.Data.Quantity : 0;
							}
						}
					}
					catch (Exception ex)
					{
						await Log.Error(LOG, $"Buy threw for CharID={characterID} listing {listingID}: {ex}");
					}
					finally
					{
						AuctionResult final = outcome;
						int remaining = left;
						if (!TryEnqueueMainThread(() =>
						{
							if (final != AuctionResult.Success)
							{
								Refund(conn, characterID, escrow, AuctionRules.Describe(AuctionOperation.Buy, final));
							}
							SendResult(conn, AuctionOperation.Buy, final, listingID, final == AuctionResult.Success ? buy.Quantity : remaining,
								final == AuctionResult.Success ? total : 0);
						}))
						{
							Log.Error(LOG, $"Buy: the main-thread queue refused the reply for CharID={characterID} ({final}); {(final == AuctionResult.Success ? "the purchase stands" : "the price was NOT returned")}.");
						}
					}
				}, EndGuard, guardKey, characterID))
				{
					asyncOwnsGuard = true;
					result = AuctionResult.Success;
				}
				else
				{
					Refund(conn, characterID, escrow, "the server was busy");
					result = AuctionResult.Busy;
				}
			}
			finally
			{
				if (!asyncOwnsGuard)
				{
					EndGuard(guardKey);
					SendResult(conn, AuctionOperation.Buy, result, msg.ListingID);
				}
			}
		}

		/// <summary>A purchase's database outcome as the window's result.</summary>
		internal static AuctionResult ToResult(AuctionBuyOutcome outcome)
		{
			switch (outcome)
			{
				case AuctionBuyOutcome.Bought: return AuctionResult.Success;
				case AuctionBuyOutcome.Gone: return AuctionResult.Gone;
				case AuctionBuyOutcome.NotEnough: return AuctionResult.NotEnough;
				case AuctionBuyOutcome.PriceChanged: return AuctionResult.PriceChanged;
				case AuctionBuyOutcome.OwnListing: return AuctionResult.OwnListing;
				default: return AuctionResult.ServerError;
			}
		}

		// ── Cancel ───────────────────────────────────────────────────────────────────

		/// <summary>Takes one of the seller's listings down; the stack comes back by mail. The deposit is kept.</summary>
		private void OnCancel(NetworkConnection conn, AuctionCancelBroadcast msg, Channel channel)
		{
			if (!TryBeginPlayerRequest(conn, out PlayerRequestContext request, PlayerRequestGate.SkipCanAct))
			{
				return;
			}
			IPlayerCharacter character = request.Character;
			// Checked here rather than by the gate, so the window is told instead of waiting for an answer that never comes.
			if (!CharacterStateValidation.CanAct(character))
			{
				SendResult(conn, AuctionOperation.Cancel, AuctionResult.CannotAct, msg.ListingID);
				return;
			}
			if (!TryBeginGuard(conn, Operation.Cancel, out long guardKey))
			{
				SendResult(conn, AuctionOperation.Cancel, AuctionResult.Busy, msg.ListingID);
				return;
			}
			if (!AtAuctioneer(character, msg.InteractableID))
			{
				EndGuard(guardKey);
				SendResult(conn, AuctionOperation.Cancel, AuctionResult.NoAuctioneer, msg.ListingID);
				return;
			}

			long characterID = character.ID;
			long listingID = msg.ListingID;
			DateTime at = UtcNowMicroseconds();
			if (!TryEnqueueGuardedAsyncWork(async () =>
			{
				AuctionResult outcome = AuctionResult.ServerError;
				int quantity = 0;
				try
				{
					if (TryGetDbService(out IAuctionHouseService service))
					{
						DatabaseResult<AuctionCancelResult> cancelled = await service.CancelAsync(listingID, characterID, at, mailComposer);
						if (!cancelled.IsSuccess)
						{
							await Log.Warning(LOG, $"Cancel failed for CharID={characterID} listing {listingID}: [{cancelled.ErrorCode}] {cancelled.ErrorMessage}");
						}
						else
						{
							outcome = cancelled.Data.Outcome == AuctionCancelOutcome.Cancelled ? AuctionResult.Success : AuctionResult.Gone;
							quantity = cancelled.Data.Quantity;
						}
					}
				}
				catch (Exception ex)
				{
					await Log.Error(LOG, $"Cancel threw for CharID={characterID} listing {listingID}: {ex}");
				}
				finally
				{
					SendResultLater(conn, AuctionOperation.Cancel, outcome, listingID, quantity);
				}
			}, EndGuard, guardKey, characterID))
			{
				EndGuard(guardKey);
				SendResult(conn, AuctionOperation.Cancel, AuctionResult.Busy, listingID);
			}
		}

		// ── Expiry ───────────────────────────────────────────────────────────────────

		/// <summary>Hands back listings past their time, a batch at a time, unless a sweep is already running.</summary>
		private void StartExpirySweep()
		{
			if (Interlocked.CompareExchange(ref expirySweepRunning, 1, 0) != 0)
			{
				return;
			}
			int batch = expiryBatchSize;
			if (!TryEnqueueAsyncWork(async () =>
			{
				try
				{
					if (!TryGetDbService(out IAuctionHouseService service))
					{
						return;
					}
					// A few batches per sweep at most, so a backlog drains over several sweeps rather than holding a worker.
					for (int i = 0; i < 5; i++)
					{
						DatabaseResult<int> ended = await service.ExpireAsync(UtcNowMicroseconds(), batch, mailComposer);
						if (!ended.IsSuccess)
						{
							await Log.Warning(LOG, $"Expiry sweep failed: [{ended.ErrorCode}] {ended.ErrorMessage}");
							return;
						}
						if (ended.Data > 0)
						{
							await Log.Debug(LOG, $"Expiry sweep handed back {ended.Data} listing(s).");
						}
						if (ended.Data < batch)
						{
							return;
						}
					}
				}
				catch (Exception ex)
				{
					await Log.Error(LOG, $"Expiry sweep threw: {ex}");
				}
				finally
				{
					Interlocked.Exchange(ref expirySweepRunning, 0);
				}
			}))
			{
				Interlocked.Exchange(ref expirySweepRunning, 0);
			}
		}
	}
}
