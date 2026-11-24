using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
	/// <summary>The reads: the browse list, the player's own listings, and the price hint.</summary>
	public partial class AuctionHouseSystem
	{
		/// <summary>Answers a page of the browse list.</summary>
		private void OnSearch(NetworkConnection conn, AuctionSearchBroadcast msg, Channel channel)
		{
			if (!TryBeginPlayerRequest(conn, out PlayerRequestContext request, PlayerRequestGate.SkipCanAct))
			{
				return;
			}
			IPlayerCharacter character = request.Character;
			if (!AtAuctioneer(character, msg.InteractableID) || !TryBeginGuard(conn, Operation.Search, out long guardKey))
			{
				return;
			}

			AuctionSearchQuery query = new AuctionSearchQuery
			{
				NameContains = AuctionRules.CleanSearch(msg.Name),
				Category = msg.Category == AuctionCategory.All ? (short)-1 : (short)msg.Category,
				Order = (AuctionSearchOrder)Math.Min((int)msg.Sort, (int)AuctionSearchOrder.Name),
				Descending = msg.Descending,
				Offset = Math.Max(0, Math.Min(msg.Page, 10000)) * AuctionRules.PageSize,
				Limit = AuctionRules.PageSize,
			};
			int page = Math.Max(0, Math.Min(msg.Page, 10000));
			long characterID = character.ID;

			if (!TryEnqueueGuardedAsyncWork(async () =>
			{
				if (!TryGetDbService(out IAuctionHouseService service))
				{
					return;
				}
				DateTime now = DateTime.UtcNow;
				DatabaseResult<AuctionSearchPage> result = await service.SearchAsync(query, now);
				if (!result.IsSuccess || result.Data == null)
				{
					await Log.Warning(LOG, $"Search failed for CharID={characterID}: [{result.ErrorCode}] {result.ErrorMessage}");
					return;
				}
				AuctionListingEntry[] entries = ToEntries(result.Data.Rows, characterID, now);
				int total = result.Data.TotalCount;
				TryEnqueueMainThread(() =>
				{
					if (conn != null && conn.IsActive)
					{
						Server.NetworkWrapper.Broadcast(conn, new AuctionSearchResultBroadcast()
						{
							Entries = entries,
							Page = page,
							TotalCount = total,
						}, true, Channel.Reliable);
					}
				});
			}, EndGuard, guardKey, characterID))
			{
				EndGuard(guardKey);
			}
		}

		/// <summary>Answers the player's own listings.</summary>
		private void OnMyListings(NetworkConnection conn, AuctionMyListingsRequestBroadcast msg, Channel channel)
		{
			if (!TryBeginPlayerRequest(conn, out PlayerRequestContext request, PlayerRequestGate.SkipCanAct))
			{
				return;
			}
			if (!AtAuctioneer(request.Character, msg.InteractableID) || !TryBeginGuard(conn, Operation.MyListings, out long guardKey))
			{
				return;
			}
			long characterID = request.Character.ID;
			if (!TryEnqueueGuardedAsyncWork(() => SendMyListingsAsync(conn, characterID), EndGuard, guardKey, characterID))
			{
				EndGuard(guardKey);
			}
		}

		/// <summary>Reads a seller's listings and sends them. Worker thread.</summary>
		private async Task SendMyListingsAsync(NetworkConnection conn, long characterID)
		{
			if (!TryGetDbService(out IAuctionHouseService service))
			{
				return;
			}
			DatabaseResult<List<AuctionListingData>> result = await service.FetchBySellerAsync(characterID);
			if (!result.IsSuccess || result.Data == null)
			{
				await Log.Warning(LOG, $"My listings failed for CharID={characterID}: [{result.ErrorCode}] {result.ErrorMessage}");
				return;
			}
			AuctionListingEntry[] entries = ToEntries(result.Data, characterID, DateTime.UtcNow);
			int max = maxActiveListings;
			TryEnqueueMainThread(() =>
			{
				if (conn != null && conn.IsActive)
				{
					Server.NetworkWrapper.Broadcast(conn, new AuctionMyListingsBroadcast()
					{
						Entries = entries,
						MaxListings = max,
					}, true, Channel.Reliable);
				}
			});
		}

		/// <summary>Answers the cheapest current price of an item the player is about to sell, with the server's fees.</summary>
		private void OnPriceHint(NetworkConnection conn, AuctionPriceHintRequestBroadcast msg, Channel channel)
		{
			if (!TryBeginPlayerRequest(conn, out PlayerRequestContext request, PlayerRequestGate.SkipCanAct))
			{
				return;
			}
			if (BaseItemTemplate.Get<BaseItemTemplate>(msg.TemplateID) == null ||
				!AtAuctioneer(request.Character, msg.InteractableID) ||
				!TryBeginGuard(conn, Operation.PriceHint, out long guardKey))
			{
				return;
			}
			int templateID = msg.TemplateID;
			long characterID = request.Character.ID;
			float deposit = depositPercent;
			long minimum = minimumDeposit;
			float cut = cutPercent;

			if (!TryEnqueueGuardedAsyncWork(async () =>
			{
				if (!TryGetDbService(out IAuctionHouseService service))
				{
					return;
				}
				DatabaseResult<AuctionPriceHintData> result = await service.FetchPriceHintAsync(templateID, DateTime.UtcNow);
				if (!result.IsSuccess)
				{
					await Log.Warning(LOG, $"Price hint failed for CharID={characterID}: [{result.ErrorCode}] {result.ErrorMessage}");
					return;
				}
				AuctionPriceHintData hint = result.Data;
				TryEnqueueMainThread(() =>
				{
					if (conn != null && conn.IsActive)
					{
						Server.NetworkWrapper.Broadcast(conn, new AuctionPriceHintBroadcast()
						{
							TemplateID = templateID,
							LowestUnitPrice = hint.LowestUnitPrice,
							ListedQuantity = hint.ListedQuantity,
							DepositPercent = deposit,
							MinimumDeposit = minimum,
							CutPercent = cut,
						}, true, Channel.Reliable);
					}
				});
			}, EndGuard, guardKey, characterID))
			{
				EndGuard(guardKey);
			}
		}

		/// <summary>Listings as the window shows them.</summary>
		internal static AuctionListingEntry[] ToEntries(IReadOnlyList<AuctionListingData> rows, long viewerID, DateTime nowUtc)
		{
			if (rows == null || rows.Count == 0)
			{
				return Array.Empty<AuctionListingEntry>();
			}
			AuctionListingEntry[] entries = new AuctionListingEntry[rows.Count];
			for (int i = 0; i < rows.Count; i++)
			{
				AuctionListingData row = rows[i];
				double seconds = (row.ExpiresUtc - nowUtc).TotalSeconds;
				entries[i] = new AuctionListingEntry
				{
					ListingID = row.ID,
					TemplateID = row.ItemTemplateID,
					Seed = row.ItemSeed,
					Quantity = row.Quantity,
					UnitPrice = row.UnitPrice,
					SellerName = row.SellerName ?? string.Empty,
					SecondsLeft = seconds <= 0 ? 0 : seconds >= int.MaxValue ? int.MaxValue : (int)Math.Ceiling(seconds),
					Own = row.SellerID == viewerID,
				};
			}
			return entries;
		}
	}
}
