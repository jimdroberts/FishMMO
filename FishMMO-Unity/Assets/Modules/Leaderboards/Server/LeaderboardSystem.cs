using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using FishNet.Connection;
using FishNet.Transporting;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Data.Enums;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Logging;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Serves PvP and PvE leaderboards: one page of a board and the asking player's own standing
	/// on it, read from the database and held in this server's memory for a short time.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The database is the only source.</b> A board ranks the whole shard, and the characters
	/// on it are spread across every scene server or offline, so no server's memory can rank them.
	/// Every number shown is one the database returned; nothing is patched from the characters
	/// this server happens to hold, because a board mixing live values for some characters with
	/// saved values for the rest would order people by which server they stand on.
	/// </para>
	/// <para>
	/// <b>Cached, and honest about it.</b> A page is read once and served to every player who
	/// asks for it for <see cref="cacheSeconds"/>; concurrent requests for a page nobody has read
	/// yet share one read rather than each issuing it (<see cref="Core.Collections.SingleFlightCache{TKey,TValue}"/>).
	/// Each page is its own entry, so browsing to a range nobody else is viewing costs one read of
	/// that range and never re-reads the rest. The reply carries the read's age and the panel
	/// shows it. Staleness is therefore bounded and visible: an arena rating is current when its
	/// match ends and at most one cache lifetime old on the board; an attribute or achievement is
	/// current as of the owner's last character save (every 30 s while online), plus the same.
	/// </para>
	/// <para>
	/// <b>A player's standing agrees with the page they are looking at.</b> When they are on it,
	/// their rank is taken from the page's own row. Only when they are not is it read separately —
	/// cached per player and board — and it may then be a few seconds apart from the page, never
	/// inconsistent with it by definition (both are the same RANK() computed the same way).
	/// </para>
	/// <para>
	/// Board requests need no interactable and no CanAct: reading a board is not an action and
	/// leads to none. The ingress guard allows one request in flight per connection, and the
	/// panel keeps at most one outstanding, coalescing clicks made while it waits.
	/// </para>
	/// </remarks>
	[CreateAssetMenu(fileName = "LeaderboardSystem", menuName = "FishMMO/Server/SceneServer/Leaderboard System", order = 1)]
	[RequiresDataContainer(typeof(LeaderboardSystemMainThreadQueueData))]
	[RequiresDataContainer(typeof(LeaderboardSystemRuntimeData))]
	[RequiresDataContainer(typeof(AsyncWorkerData))]
	public class LeaderboardSystem : ServerBehaviour
	{
		[Header("Cache")]
		[Tooltip("Seconds a page or standing read from the database is served before it is read again. The most a board can lag the database; also how often each board page costs a read per scene server while anyone is viewing it.")]
		[SerializeField] private float cacheSeconds = 60.0f;

		[Tooltip("Seconds between bounded sweeps of expired cache entries.")]
		[SerializeField] private float cacheSweepIntervalSeconds = 10.0f;

		[Tooltip("Maximum expired cache entries removed per sweep, per cache.")]
		[SerializeField] private int cacheSweepMaxRemovals = 256;

		[Header("Boards")]
		[Tooltip("Deepest position anyone may page to. A player further down still sees their own rank; they just cannot browse to it. Bounds how deep a page read can reach into a board.")]
		[SerializeField] private int maxBrowsableRank = 1000;

		[Tooltip("Whether characters on game master and administrator accounts are ranked. Off for a live shard; turn on to see your own staff characters on the boards while testing.")]
		[SerializeField] private bool rankStaff = false;

		[Header("Main Thread Dispatch")]
		[Tooltip("Max leaderboard replies drained from the main-thread queue per frame")]
		[SerializeField] private int maxMainThreadActionsPerFrame = 100;

		[Header("Ingress Protection")]
		[Tooltip("Minimum milliseconds between board requests per connection. The panel keeps one request outstanding and waits at least this long between them.")]
		[SerializeField] private int ingressDebounceMilliseconds = 150;

		[Tooltip("Seconds between bounded ingress guard cleanup sweeps")]
		[SerializeField] private float ingressSweepIntervalSeconds = 5.0f;

		[Tooltip("Seconds before stale ingress guard entries are removed")]
		[SerializeField] private float ingressEntryTtlSeconds = 30.0f;

		[Tooltip("Maximum stale ingress guard entries removed per sweep")]
		[SerializeField] private int ingressSweepMaxRemovals = 128;

		/// <summary>Operation codes used for ingress guards.</summary>
		private enum IngressOperation : byte
		{
			PageRequest = 1,
		}

		private float nextCacheSweepAt;

		private TimeSpan CacheTtl => TimeSpan.FromSeconds(cacheSeconds);

		/// <inheritdoc/>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			if (Server == null)
			{
				Log.Error("LeaderboardSystem", "InitializeOnce: Server is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (!Server.DataContainerRegistry.TryGet<ILeaderboardSystemMainThreadQueueData>(out _))
			{
				Log.Error("LeaderboardSystem", "InitializeOnce: ILeaderboardSystemMainThreadQueueData not found");
				return ServerComponentInitializationStatus.FailedToGetDataContainer;
			}

			if (!Server.DataContainerRegistry.TryGet<ILeaderboardSystemRuntimeData>(out _))
			{
				Log.Error("LeaderboardSystem", "InitializeOnce: ILeaderboardSystemRuntimeData not found");
				return ServerComponentInitializationStatus.FailedToGetDataContainer;
			}

			cacheSeconds = Mathf.Max(1.0f, cacheSeconds);
			cacheSweepIntervalSeconds = Mathf.Max(1.0f, cacheSweepIntervalSeconds);
			cacheSweepMaxRemovals = Mathf.Max(1, cacheSweepMaxRemovals);
			maxBrowsableRank = Mathf.Max(LeaderboardPaging.PageSize, maxBrowsableRank);
			maxMainThreadActionsPerFrame = Mathf.Max(1, maxMainThreadActionsPerFrame);
			ingressDebounceMilliseconds = Mathf.Max(0, ingressDebounceMilliseconds);
			ingressSweepIntervalSeconds = Mathf.Max(0.25f, ingressSweepIntervalSeconds);
			ingressEntryTtlSeconds = Mathf.Max(1.0f, ingressEntryTtlSeconds);
			ingressSweepMaxRemovals = Mathf.Max(1, ingressSweepMaxRemovals);

			Server.NetworkWrapper.RegisterBroadcast<LeaderboardPageRequestBroadcast>(OnServerLeaderboardPageRequestReceived, true);

			Log.Debug("LeaderboardSystem", $"Initialized (cache {cacheSeconds:0}s, browsable to rank {maxBrowsableRank}, staff {(rankStaff ? "ranked" : "not ranked")})");
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <inheritdoc/>
		public override void OnDeinitialize()
		{
			if (Server == null)
			{
				return;
			}

			DrainMainThreadQueue<ILeaderboardSystemMainThreadQueueData>(maxMainThreadActionsPerFrame, drainAll: true);

			Server.NetworkWrapper.UnregisterBroadcast<LeaderboardPageRequestBroadcast>(OnServerLeaderboardPageRequestReceived);

			if (Server.DataContainerRegistry.TryGet<ILeaderboardSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.IngressGuard?.Clear();
				runtimeData.Pages?.Clear();
				runtimeData.Standings?.Clear();
			}
		}

		/// <summary>
		/// Drains replies, and sweeps the guard and the caches on their intervals.
		/// </summary>
		protected override void OnUpdate(float deltaTime)
		{
			DrainMainThreadQueue<ILeaderboardSystemMainThreadQueueData>(maxMainThreadActionsPerFrame, drainAll: false);

			if (!Server.DataContainerRegistry.TryGet<ILeaderboardSystemRuntimeData>(out var runtimeData))
			{
				return;
			}

			runtimeData.IngressGuard.Sweep(ingressSweepIntervalSeconds, ingressEntryTtlSeconds, ingressSweepMaxRemovals);

			float now = Time.unscaledTime;
			if (now >= nextCacheSweepAt)
			{
				nextCacheSweepAt = now + cacheSweepIntervalSeconds;
				runtimeData.Pages.SweepExpired(CacheTtl, cacheSweepMaxRemovals * 2, cacheSweepMaxRemovals);
				runtimeData.Standings.SweepExpired(CacheTtl, cacheSweepMaxRemovals * 2, cacheSweepMaxRemovals);
			}
		}

		/// <summary>
		/// A board page request. Validated here on the main thread; read and answered from the
		/// async worker.
		/// </summary>
		public void OnServerLeaderboardPageRequestReceived(NetworkConnection conn, LeaderboardPageRequestBroadcast msg, Channel channel)
		{
			if (conn == null || !Server.DataContainerRegistry.TryGet<ILeaderboardSystemRuntimeData>(out var runtimeData))
			{
				return;
			}

			/* A refusal here is silent, deliberately: it means this connection already has a request
			 * in flight or sent one a moment ago, and the panel is built to wait for the reply it is
			 * owed rather than to fire again. */
			if (!runtimeData.IngressGuard.TryBegin(conn.ClientId, (byte)IngressOperation.PageRequest, ingressDebounceMilliseconds, out long guardKey))
			{
				return;
			}

			bool asyncOwns = false;
			try
			{
				/* SkipCanAct: reading a board is not an action and leads to none, so there is no
				 * later gate for this to defer to — and a dead or stunned player may still look. */
				if (!TryBeginPlayerRequest(conn, out PlayerRequestContext request, PlayerRequestGate.SkipCanAct))
				{
					return;
				}

				LeaderboardTemplate template = msg.TemplateID != 0 ? LeaderboardTemplate.Get<LeaderboardTemplate>(msg.TemplateID) : null;
				if (template == null || !TryBuildQuery(template, rankStaff, out LeaderboardQuery query))
				{
					SendUnavailable(conn, msg.TemplateID);
					return;
				}

				int page = LeaderboardPaging.ClampPage(msg.Page, LeaderboardPaging.MaxPage(maxBrowsableRank));
				long characterID = request.CharacterID;
				int templateID = template.ID;
				asyncOwns = TryEnqueueAsyncWork(() => SendPageAsync(conn, characterID, templateID, query, page, guardKey), conn, characterID);
			}
			finally
			{
				if (!asyncOwns)
				{
					runtimeData.IngressGuard.End(guardKey);
				}
			}
		}

		/// <summary>
		/// Maps a board onto the database query that reads it.
		/// </summary>
		/// <remarks>
		/// The one place the shared <see cref="LeaderboardSource"/> meets the database's
		/// <see cref="LeaderboardSourceKind"/>. A board whose reference is missing is refused
		/// rather than read as template 0.
		/// </remarks>
		/// <param name="template">The board.</param>
		/// <param name="rankStaff">Whether staff accounts' characters are ranked.</param>
		/// <param name="query">The query, when true.</param>
		public static bool TryBuildQuery(LeaderboardTemplate template, bool rankStaff, out LeaderboardQuery query)
		{
			query = default;
			if (template == null || !template.IsConfigured(out _))
			{
				return false;
			}

			switch (template.Source)
			{
				case LeaderboardSource.ArenaSeasonRating:
					query = new LeaderboardQuery(LeaderboardSourceKind.ArenaSeasonRating, 0, Math.Max(1, template.MinimumGames), rankStaff);
					return true;
				case LeaderboardSource.CharacterAttribute:
				case LeaderboardSource.Achievement:
					int sourceID = template.SourceTemplateID;
					if (sourceID == 0)
					{
						return false;
					}
					LeaderboardSourceKind kind = template.Source == LeaderboardSource.CharacterAttribute
						? LeaderboardSourceKind.CharacterAttribute
						: LeaderboardSourceKind.CharacterAchievement;
					query = new LeaderboardQuery(kind, sourceID, 0, rankStaff);
					return true;
				default:
					return false;
			}
		}

		/// <summary>
		/// Reads (or shares a cached read of) the page and the player's standing, then replies.
		/// </summary>
		private async Task SendPageAsync(NetworkConnection conn, long characterID, int templateID, LeaderboardQuery query, int page, long guardKey)
		{
			try
			{
				if (!TryGetDbService(out ILeaderboardService service) ||
					!Server.DataContainerRegistry.TryGet<ILeaderboardSystemRuntimeData>(out var runtimeData))
				{
					EnqueueReply(conn, new LeaderboardPageBroadcast { TemplateID = templateID, Page = page, Unavailable = true });
					return;
				}

				var pageKey = new LeaderboardPageKey(query, LeaderboardPaging.Offset(page), LeaderboardPaging.PageSize);
				LeaderboardPageData pageData = await runtimeData.Pages.GetOrFetchAsync(pageKey, CacheTtl, async () =>
				{
					DatabaseResult<LeaderboardPageData> result = await service.FetchPageAsync(pageKey.Query, pageKey.Offset, pageKey.Limit).ConfigureAwait(false);
					if (!result.IsSuccess)
					{
						throw new InvalidOperationException($"Leaderboard page read failed ({pageKey.Query}, offset {pageKey.Offset}): {result.ErrorCode} {result.ErrorMessage}");
					}
					return result.Data;
				}).ConfigureAwait(false);

				/* The requester's standing: from the page when they are on it, so the two can never
				 * disagree; otherwise a separate read, cached per player and board. */
				LeaderboardStandingData? standing = FindOnPage(pageData, characterID);
				if (!standing.HasValue)
				{
					var standingKey = new LeaderboardStandingKey(query, characterID);
					standing = await runtimeData.Standings.GetOrFetchAsync(standingKey, CacheTtl, async () =>
					{
						DatabaseResult<LeaderboardStandingData> result = await service.FetchStandingAsync(standingKey.Query, standingKey.CharacterID).ConfigureAwait(false);
						if (!result.IsSuccess)
						{
							throw new InvalidOperationException($"Leaderboard standing read failed ({standingKey.Query}, character {standingKey.CharacterID}): {result.ErrorCode} {result.ErrorMessage}");
						}
						return result.Data;
					}).ConfigureAwait(false);
				}

				EnqueueReply(conn, ComposePage(templateID, page, pageData, standing.Value, maxBrowsableRank, DateTime.UtcNow));
			}
			catch (Exception ex)
			{
				await Log.Warning("LeaderboardSystem", $"Board {templateID} page {page} for character {characterID} could not be read: {ex.Message}");
				EnqueueReply(conn, new LeaderboardPageBroadcast { TemplateID = templateID, Page = page, Unavailable = true });
			}
			finally
			{
				if (Server?.DataContainerRegistry.TryGet<ILeaderboardSystemRuntimeData>(out var runtimeData) == true)
				{
					runtimeData.IngressGuard.End(guardKey);
				}
			}
		}

		/// <summary>The character's row on a page, as a standing, or null when they are not on it.</summary>
		public static LeaderboardStandingData? FindOnPage(LeaderboardPageData page, long characterID)
		{
			if (page?.Rows == null)
			{
				return null;
			}
			for (int i = 0; i < page.Rows.Count; ++i)
			{
				LeaderboardRowData row = page.Rows[i];
				if (row.CharacterID == characterID)
				{
					return new LeaderboardStandingData(true, row.Rank, row.Score, row.Wins, row.Losses, page.FetchedAtUtc);
				}
			}
			return null;
		}

		/// <summary>
		/// Builds the reply for one page. Pure: everything it reports comes from its arguments.
		/// </summary>
		/// <param name="templateID">The board.</param>
		/// <param name="page">The page that was read.</param>
		/// <param name="pageData">The rows and the board's size.</param>
		/// <param name="standing">The requester's standing.</param>
		/// <param name="maxBrowsableRank">Deepest position anyone may page to.</param>
		/// <param name="nowUtc">Now, for the read's age.</param>
		public static LeaderboardPageBroadcast ComposePage(int templateID, int page, LeaderboardPageData pageData, LeaderboardStandingData standing, int maxBrowsableRank, DateTime nowUtc)
		{
			IReadOnlyList<LeaderboardRowData> rows = pageData?.Rows ?? Array.Empty<LeaderboardRowData>();
			var entries = new LeaderboardEntry[rows.Count];
			for (int i = 0; i < entries.Length; ++i)
			{
				LeaderboardRowData row = rows[i];
				entries[i] = new LeaderboardEntry
				{
					Rank = row.Rank,
					CharacterID = row.CharacterID,
					CharacterName = row.Name ?? string.Empty,
					Score = row.Score,
					Wins = row.Wins,
					Losses = row.Losses,
				};
			}

			int total = pageData?.TotalRanked ?? 0;
			DateTime readAt = pageData?.FetchedAtUtc ?? nowUtc;
			double age = (nowUtc - readAt).TotalSeconds;

			return new LeaderboardPageBroadcast
			{
				TemplateID = templateID,
				Page = page,
				PageCount = LeaderboardPaging.PageCount(total, maxBrowsableRank),
				TotalRanked = total,
				SeasonName = pageData?.SeasonName ?? string.Empty,
				AgeSeconds = age <= 0 ? 0 : (age >= int.MaxValue ? int.MaxValue : (int)age),
				Entries = entries,
				YourRank = standing.Ranked ? standing.Rank : 0,
				YourScore = standing.Ranked ? standing.Score : 0,
				Unavailable = false,
			};
		}

		private void SendUnavailable(NetworkConnection conn, int templateID)
		{
			if (conn != null && conn.IsActive)
			{
				Server.NetworkWrapper.Broadcast(conn, new LeaderboardPageBroadcast { TemplateID = templateID, Unavailable = true }, true, Channel.Reliable);
			}
		}

		private void EnqueueReply(NetworkConnection conn, LeaderboardPageBroadcast reply)
		{
			TryEnqueueMainThread<ILeaderboardSystemMainThreadQueueData>(() =>
			{
				if (conn != null && conn.IsActive)
				{
					Server.NetworkWrapper.Broadcast(conn, reply, true, Channel.Reliable);
				}
			});
		}
	}
}
