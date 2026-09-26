using UnityEngine;
using FishNet.Connection;
using FishNet.Transporting;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Server.Core;
using FishMMO.Server.Core.Collections;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared.Core;
using FishMMO.Shared;
using FishMMO.Logging;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Provides name resolution services for game entities, resolving names by ID for characters and guilds.
	/// Names by ID are asked for in batches (<see cref="NamingRequestBatchBroadcast"/>) or, by older
	/// clients, one at a time (<see cref="NamingBroadcast"/>), and each is answered in the form it asked in.
	/// Game logic and Broadcasts run synchronously on the main thread.
	/// Database lookups are async to avoid blocking the main thread.
	/// Results from async DB queries are marshalled back via INamingSystemMainThreadQueueData.
	/// </summary>
	[CreateAssetMenu(fileName = "NamingSystem", menuName = "FishMMO/Server/SceneServer/Naming System", order = 1)]
	[RequiresDataContainer(typeof(NamingSystemRuntimeData))]
	[RequiresDataContainer(typeof(NamingSystemMappingData))]
	[RequiresDataContainer(typeof(NamingSystemMainThreadQueueData))]
	[RequiresDataContainer(typeof(AsyncWorkerData))]
	public class NamingSystem : ServerBehaviour, INamingSystem<NetworkConnection>
	{
		/// <summary>
		/// Maximum number of queued main-thread actions processed per frame.
		/// This time-slices queue draining to avoid frame spikes.
		/// </summary>
		[Header("Main Thread Dispatch")]
		[Tooltip("Max naming-system actions drained from main-thread queue per frame")]
		[SerializeField] private int maxMainThreadActionsPerFrame = 100;

		/// <summary>
		/// Naming requests one connection may send in a burst before its refill rate applies.
		/// </summary>
		/// <remarks>
		/// Sized for the largest honest burst: a client that has never seen a 100-member guild
		/// opens its roster and asks for every name in one frame, while the characters around it
		/// spawn and ask for theirs. The old 75 ms window answered the first request of any burst
		/// and dropped the rest (hot-path audit M18). Zero or less turns the budget off.
		/// </remarks>
		[Header("Request Protection")]
		[Tooltip("Naming requests one connection may send in a burst (token bucket capacity); 0 disables the budget")]
		[SerializeField] private int requestBurst = 200;

		/// <summary>
		/// Naming requests per second one connection earns back after a burst.
		/// </summary>
		/// <remarks>
		/// Above the 13 a second the old 75 ms window allowed in steady state, which bounded the
		/// database work one connection could cause; the burst is what changed, not the ceiling.
		/// </remarks>
		[Tooltip("Naming requests per second a connection earns back (token bucket refill)")]
		[SerializeField] private float requestsPerSecond = 20.0f;

		/// <summary>
		/// Seconds a lookup may be out before it is presumed lost and restarted by the next request.
		/// </summary>
		/// <remarks>
		/// A lookup's answer is handed back through the main-thread queue, which refuses work when
		/// full. Without a bound, a lookup whose answer was refused would hold its key forever and
		/// every later request for that name would wait on an answer that is never coming.
		/// </remarks>
		[Tooltip("Seconds a naming lookup may be in flight before the next request for it starts a new one")]
		[SerializeField] private float inFlightStaleSeconds = 15.0f;

		/// <summary>
		/// Reusable waiter list for completing a reverse lookup. Main thread only.
		/// </summary>
		private readonly List<NetworkConnection> completedWaiters = new List<NetworkConnection>();

		/// <summary>
		/// Reusable waiter list for completing a name-by-ID lookup. Main thread only.
		/// </summary>
		private readonly List<NamingWaiter<NetworkConnection>> completedNameWaiters = new List<NamingWaiter<NetworkConnection>>();

		/// <summary>
		/// Reusable scratch for reading one request's IDs. Main thread only.
		/// </summary>
		private readonly HashSet<long> requestIdSeen = new HashSet<long>();

		/// <summary>
		/// The IDs of the request being handled. Main thread only.
		/// </summary>
		private readonly List<long> requestIds = new List<long>(NamingRequestBatchBroadcast.MaxIDs);

		/// <summary>
		/// The IDs the request being handled starts a lookup for. Main thread only.
		/// </summary>
		private readonly List<long> idsToFetch = new List<long>(NamingRequestBatchBroadcast.MaxIDs);

		/// <summary>
		/// Batched answers waiting to be sent, grouped by connection. Filled and drained within one
		/// request or one completed lookup. Main thread only.
		/// </summary>
		private readonly NamingReplyBatches<NetworkConnection> replyBatches = new NamingReplyBatches<NetworkConnection>();

		/// <summary>
		/// <see cref="inFlightStaleSeconds"/> as a span.
		/// </summary>
		private TimeSpan InFlightStaleAfter => TimeSpan.FromSeconds(inFlightStaleSeconds);

		/// <summary>
		/// Cache TTL in seconds for naming cache entries.
		/// </summary>
		[Tooltip("Cache TTL in seconds for naming lookup caches")]
		[SerializeField] private float cacheTtlSeconds = 30.0f;

		/// <summary>
		/// Interval in seconds between bounded cache sweeps.
		/// </summary>
		[Tooltip("Seconds between bounded naming cache sweeps")]
		[SerializeField] private float cacheSweepIntervalSeconds = 1.0f;

		/// <summary>
		/// Maximum cache entries scanned per sweep pass.
		/// </summary>
		[Tooltip("Maximum cache entries scanned per sweep")]
		[SerializeField] private int cacheSweepMaxScan = 128;

		/// <summary>
		/// Maximum cache entries removed per sweep pass.
		/// </summary>
		[Tooltip("Maximum cache entries removed per sweep")]
		[SerializeField] private int cacheSweepMaxRemove = 128;

		/// <summary>
		/// Initializes the naming system, registering broadcast handlers for naming and reverse naming requests.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			if (Server == null)
			{
				Log.Error("NamingSystem", "InitializeOnce: Server is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (!Server.DataContainerRegistry.TryGet<INamingSystemMainThreadQueueData>(out _))
			{
				Log.Error("NamingSystem", "Failed to initialize: INamingSystemMainThreadQueueData not found");
				return ServerComponentInitializationStatus.FailedToGetDataContainer;
			}

			if (!Server.DataContainerRegistry.TryGet<INamingSystemRuntimeData>(out _))
			{
				Log.Error("NamingSystem", "Failed to initialize: INamingSystemRuntimeData not found");
				return ServerComponentInitializationStatus.FailedToGetDataContainer;
			}

			if (!Server.DataContainerRegistry.TryGet<INamingSystemMappingData>(out _))
			{
				Log.Error("NamingSystem", "Failed to initialize: INamingSystemMappingData not found");
				return ServerComponentInitializationStatus.FailedToGetDataContainer;
			}

			// Network broadcasts
			Server.NetworkWrapper.RegisterBroadcast<NamingBroadcast>(OnServerNamingBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<NamingRequestBatchBroadcast>(OnServerNamingRequestBatchBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<ReverseNamingBroadcast>(OnServerReverseNamingBroadcastReceived, true);

			maxMainThreadActionsPerFrame = Mathf.Max(1, maxMainThreadActionsPerFrame);
			requestsPerSecond = Mathf.Max(0.1f, requestsPerSecond);
			inFlightStaleSeconds = Mathf.Max(1.0f, inFlightStaleSeconds);
			cacheTtlSeconds = Mathf.Max(5.0f, cacheTtlSeconds);
			cacheSweepIntervalSeconds = Mathf.Max(0.1f, cacheSweepIntervalSeconds);
			cacheSweepMaxScan = Mathf.Max(1, cacheSweepMaxScan);
			cacheSweepMaxRemove = Mathf.Max(1, cacheSweepMaxRemove);
			if (Server.DataContainerRegistry.TryGet<INamingSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.NextCacheSweepAt = MonotonicClock.NowSeconds;
			}

			Log.Debug("NamingSystem", "Initialized");
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Cleans up the naming system, unregistering broadcast handlers.
		/// </summary>
		public override void OnDeinitialize()
		{
			if (Server == null)
			{
				Log.Error("NamingSystem", "OnDeinitialize: Server is null");
				return;
			}

			// Drain any remaining queued main-thread actions
			DrainMainThreadQueue(drainAll: true);

			// Network broadcasts
			Server.NetworkWrapper.UnregisterBroadcast<NamingBroadcast>(OnServerNamingBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<NamingRequestBatchBroadcast>(OnServerNamingRequestBatchBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<ReverseNamingBroadcast>(OnServerReverseNamingBroadcastReceived);
		}

		/// <summary>
		/// Drains queued main-thread actions from the INamingSystemMainThreadQueueData container.
		/// </summary>
		private void DrainMainThreadQueue(bool drainAll)
		{
			DrainMainThreadQueue<INamingSystemMainThreadQueueData>(maxMainThreadActionsPerFrame, drainAll);
		}

		/// <summary>
		/// Enqueues an action to be executed on the main thread.
		/// </summary>
		/// <param name="action">The action to enqueue.</param>
		private bool TryEnqueueMainThread(Action action)
		{
			return TryEnqueueMainThread<INamingSystemMainThreadQueueData>(action);
		}

		/// <summary>
		/// Drains the main-thread queue each frame.
		/// </summary>
		protected override void OnUpdate(float deltaTime)
		{
			DrainMainThreadQueue(drainAll: false);
			SweepCaches();
		}

		/// <summary>
		/// Takes one request from a connection's budget. See <see cref="NamingRequestBucket"/>.
		/// </summary>
		/// <param name="conn">The requesting connection.</param>
		/// <returns>True if the request may be served; false if it is dropped.</returns>
		private bool TryTakeRequestToken(NetworkConnection conn)
		{
			return TryTakeRequestTokens(conn, 1) == 1;
		}

		/// <summary>
		/// Takes up to <paramref name="requested"/> requests from a connection's budget, one per ID.
		/// See <see cref="NamingRequestBucket.TryTakeUpTo"/>.
		/// </summary>
		/// <param name="conn">The requesting connection.</param>
		/// <param name="requested">IDs asked for.</param>
		/// <returns>How many of them may be served, from the first.</returns>
		private int TryTakeRequestTokens(NetworkConnection conn, int requested)
		{
			if (requested <= 0)
			{
				return 0;
			}
			if (requestBurst <= 0 || conn == null)
			{
				return requested;
			}

			if (!Server.DataContainerRegistry.TryGet<INamingSystemRuntimeData>(out var runtimeData))
			{
				return requested;
			}

			/* The budget is a local duration, so the bucket refills by the monotonic clock: on the
			 * wall clock a host stepped forward refilled every connection's budget at once. */
			double now = MonotonicClock.NowSeconds;
			long nowTicks = (long)(now * TimeSpan.TicksPerSecond);
			if (!runtimeData.ConnectionRequestBuckets.TryGetAndTouch(conn.ClientId, now, out NamingRequestBucket bucket))
			{
				bucket = NamingRequestBucket.Full(requestBurst, nowTicks);
			}

			int admitted = bucket.TryTakeUpTo(nowTicks, requestBurst, requestsPerSecond, requested);
			runtimeData.ConnectionRequestBuckets.Upsert(conn.ClientId, bucket, now);
			return admitted;
		}

		/// <summary>
		/// Adds a connection to the lookup for a key, starting the fetch if none is out. Reverse
		/// (name to ID) lookups; lookups by ID go through <see cref="ResolveNames"/>, which fetches
		/// every key one request starts in a single query.
		/// </summary>
		/// <remarks>
		/// A connection asking for a key another connection is already waiting on joins that
		/// lookup and receives its answer; it used to be dropped. See <see cref="InFlightLookupTable{TKey, TWaiter}"/>.
		/// </remarks>
		/// <param name="table">The in-flight table for this kind of lookup.</param>
		/// <param name="key">What is being looked up.</param>
		/// <param name="conn">The requesting connection.</param>
		/// <param name="entityKey">Async worker partition key.</param>
		/// <param name="fetch">Starts the fetch; it must finish through <see cref="CompleteLookup{TKey}"/>.</param>
		private void BeginLookup<TKey>(InFlightLookupTable<TKey, NetworkConnection> table, TKey key, NetworkConnection conn, long entityKey, Func<Task> fetch)
		{
			if (table == null)
			{
				return;
			}

			if (table.Join(key, conn, MonotonicClock.NowSeconds, InFlightStaleAfter) != InFlightLookupTable<TKey, NetworkConnection>.JoinResult.Started)
			{
				// Joined: the lookup already out answers this connection too. Refused: the table is
				// full and the request goes unanswered, as one over its connection's budget does.
				return;
			}

			if (!TryEnqueueAsyncWork(fetch, conn, entityKey))
			{
				// Nothing will answer it: release the key so the next request can try again.
				table.TryComplete(key, null);
			}
		}

		/// <summary>
		/// Ends a lookup and answers every connection still waiting on it. Main thread only.
		/// </summary>
		/// <param name="select">Picks the lookup's in-flight table off the runtime data.</param>
		/// <param name="key">The key whose fetch finished.</param>
		/// <param name="answer">Sends the answer to one waiter, or null to release the waiters unanswered.</param>
		private void CompleteLookup<TKey>(Func<INamingSystemRuntimeData, InFlightLookupTable<TKey, NetworkConnection>> select, TKey key, Action<NetworkConnection> answer)
		{
			if (Server == null ||
				!Server.DataContainerRegistry.TryGet<INamingSystemRuntimeData>(out var runtimeData))
			{
				return;
			}

			InFlightLookupTable<TKey, NetworkConnection> table = select(runtimeData);
			completedWaiters.Clear();
			if (table == null || !table.TryComplete(key, completedWaiters))
			{
				return;
			}

			try
			{
				if (answer == null)
				{
					return;
				}
				for (int i = 0; i < completedWaiters.Count; i++)
				{
					NetworkConnection waiter = completedWaiters[i];
					if (waiter != null && waiter.IsActive)
					{
						answer(waiter);
					}
				}
			}
			finally
			{
				completedWaiters.Clear();
			}
		}

		/// <summary>
		/// Performs bounded TTL sweeps for naming runtime and mapping caches.
		/// </summary>
		private void SweepCaches()
		{
			if (!Server.DataContainerRegistry.TryGet<INamingSystemRuntimeData>(out var runtimeData))
			{
				return;
			}

			double now = MonotonicClock.NowSeconds;
			if (now < runtimeData.NextCacheSweepAt)
			{
				return;
			}

			runtimeData.NextCacheSweepAt = now + cacheSweepIntervalSeconds;
			runtimeData.ConnectionRequestBuckets.SweepExpired(
				now,
				TimeSpan.FromSeconds(cacheTtlSeconds),
				cacheSweepMaxScan,
				cacheSweepMaxRemove);

			// Lookups whose answer never came back; see inFlightStaleSeconds.
			runtimeData.CharacterNameByIdInFlight.SweepStale(now, InFlightStaleAfter);
			runtimeData.GuildNameByIdInFlight.SweepStale(now, InFlightStaleAfter);
			runtimeData.CharacterByNameInFlight.SweepStale(now, InFlightStaleAfter);

			if (Server.DataContainerRegistry.TryGet<INamingSystemMappingData>(out var mappingData))
			{
				mappingData.SweepAllCaches(now, TimeSpan.FromSeconds(cacheTtlSeconds), cacheSweepMaxScan, cacheSweepMaxRemove);
			}
		}

		/// <summary>
		/// Whether a connection may ask for names of this type at all.
		/// </summary>
		/// <param name="conn">The requesting connection.</param>
		/// <param name="type">What kind of name it asks for.</param>
		/// <returns>True if the request may go on to the budget.</returns>
		private static bool MayRequestNames(NetworkConnection conn, NamingSystemType type)
		{
			if (conn == null || conn.FirstObject == null)
			{
				return false;
			}

			switch (type)
			{
				case NamingSystemType.CharacterName:
					// Require the requester to be loaded in a scene to prevent
					// cross-server character name harvesting.
					IPlayerCharacter requester = conn.FirstObject.GetComponent<IPlayerCharacter>();
					return requester != null && !string.IsNullOrEmpty(requester.SceneName);
				case NamingSystemType.GuildName:
					return true;
				default:
					return false;
			}
		}

		/// <summary>
		/// Handles a single-ID naming request, the form clients sent before
		/// <see cref="NamingRequestBatchBroadcast"/>. Kept so they are still answered, in the same form.
		/// </summary>
		/// <param name="conn">Network connection of the requesting client.</param>
		/// <param name="msg">NamingBroadcast message containing the type and ID to resolve.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		private void OnServerNamingBroadcastReceived(NetworkConnection conn, NamingBroadcast msg, Channel channel)
		{
			if (!MayRequestNames(conn, msg.Type) || !TryTakeRequestToken(conn))
			{
				return;
			}

			requestIds.Clear();
			if (msg.ID > 0)
			{
				requestIds.Add(msg.ID);
			}
			ResolveNames(conn, msg.Type, requestIds, batched: false);
		}

		/// <summary>
		/// Handles a batched naming request: every ID a client asked for of one type in one frame.
		/// </summary>
		/// <remarks>
		/// Charged one token per ID, as that many single requests would be, after the message is
		/// cut to <see cref="NamingRequestBatchBroadcast.MaxIDs"/>; IDs past either limit are not
		/// read, and the client asks for them again. What the request cannot be answered from
		/// memory is fetched in ONE query (hot-path audit M18: a roster used to cost one query and
		/// one message per member).
		/// </remarks>
		/// <param name="conn">Network connection of the requesting client.</param>
		/// <param name="msg">The request.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		private void OnServerNamingRequestBatchBroadcastReceived(NetworkConnection conn, NamingRequestBatchBroadcast msg, Channel channel)
		{
			if (msg.IDs == null || msg.IDs.Length == 0 || !MayRequestNames(conn, msg.Type))
			{
				return;
			}

			int admitted = TryTakeRequestTokens(conn, Math.Min(msg.IDs.Length, NamingRequestBatchBroadcast.MaxIDs));
			if (admitted <= 0)
			{
				return;
			}

			requestIds.Clear();
			NamingBatchRequest.SelectIds(msg.IDs, admitted, requestIdSeen, requestIds);
			ResolveNames(conn, msg.Type, requestIds, batched: true);
		}

		/// <summary>
		/// The in-flight table for a kind of name-by-ID lookup, or null for a kind with none.
		/// </summary>
		private static InFlightLookupTable<long, NamingWaiter<NetworkConnection>> NameTableFor(INamingSystemRuntimeData runtimeData, NamingSystemType type)
		{
			switch (type)
			{
				case NamingSystemType.CharacterName:
					return runtimeData.CharacterNameByIdInFlight;
				case NamingSystemType.GuildName:
					return runtimeData.GuildNameByIdInFlight;
				default:
					return null;
			}
		}

		/// <summary>
		/// Answers every ID of one request that the server already knows, and joins or starts the
		/// lookup for the rest. The lookups this request starts are fetched together, in one query.
		/// </summary>
		/// <remarks>
		/// A connection asking for an ID another connection is already waiting on joins that lookup
		/// and receives its answer; see <see cref="InFlightLookupTable{TKey, TWaiter}"/>. Each
		/// waiter is answered in the form it asked in (<see cref="NamingWaiter{TConnection}"/>).
		/// </remarks>
		/// <param name="conn">The requesting connection.</param>
		/// <param name="type">What kind of name.</param>
		/// <param name="ids">Distinct positive IDs, already admitted by the budget.</param>
		/// <param name="batched">True if the request was batched and is answered with batched replies.</param>
		private void ResolveNames(NetworkConnection conn, NamingSystemType type, List<long> ids, bool batched)
		{
			if (ids.Count == 0 ||
				!Server.DataContainerRegistry.TryGet<INamingSystemRuntimeData>(out var runtimeData) ||
				!Server.DataContainerRegistry.TryGet<INamingSystemMappingData>(out var namingMappingData))
			{
				return;
			}

			InFlightLookupTable<long, NamingWaiter<NetworkConnection>> table = NameTableFor(runtimeData, type);
			if (table == null)
			{
				return;
			}

			Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var characterMapping);
			bool canFetch = Server.Database?.ServiceRegistry != null;
			var waiter = new NamingWaiter<NetworkConnection>(conn, batched);
			double now = MonotonicClock.NowSeconds;

			idsToFetch.Clear();
			try
			{
				for (int i = 0; i < ids.Count; i++)
				{
					long id = ids[i];
					if (TryGetKnownName(type, id, now, characterMapping, namingMappingData, out string knownName))
					{
						Answer(waiter, type, id, knownName);
						continue;
					}

					/* Joined: the lookup already out answers this connection too. Refused: the table
					 * is full and the ID goes unanswered, as one over the connection's budget does. */
					if (canFetch && table.Join(id, waiter, now, InFlightStaleAfter) == InFlightLookupTable<long, NamingWaiter<NetworkConnection>>.JoinResult.Started)
					{
						idsToFetch.Add(id);
					}
				}

				if (idsToFetch.Count > 0)
				{
					long[] fetchIds = idsToFetch.ToArray();
					if (!TryEnqueueAsyncWork(() => FetchNamesAsync(type, fetchIds), conn))
					{
						// Nothing will answer them: release the keys so the next request can try again.
						for (int i = 0; i < fetchIds.Length; i++)
						{
							table.TryComplete(fetchIds[i], null);
						}
					}
				}
			}
			finally
			{
				idsToFetch.Clear();
				SendReplyBatches(type);
			}
		}

		/// <summary>
		/// A name the server holds without asking the database: a character on this scene server,
		/// or a cached answer.
		/// </summary>
		private static bool TryGetKnownName(NamingSystemType type, long id, double now, ICharacterMappingData<NetworkConnection> characterMapping, INamingSystemMappingData namingMappingData, out string name)
		{
			switch (type)
			{
				case NamingSystemType.CharacterName:
					// check our local scene server first
					if (characterMapping != null &&
						characterMapping.CharactersByID.TryGetValue(id, out IPlayerCharacter character) &&
						character != null &&
						!string.IsNullOrEmpty(character.CharacterName))
					{
						name = character.CharacterName;
						namingMappingData.CharacterNameByIdCache.Upsert(id, name, now);
						return true;
					}
					return namingMappingData.CharacterNameByIdCache.TryGetAndTouch(id, now, out name);
				case NamingSystemType.GuildName:
					return namingMappingData.GuildNameByIdCache.TryGetAndTouch(id, now, out name);
				default:
					name = null;
					return false;
			}
		}

		/// <summary>
		/// Answers one waiter for one ID in the form it asked in. Batched answers are queued on
		/// <see cref="replyBatches"/> and sent by <see cref="SendReplyBatches"/>.
		/// </summary>
		/// <param name="waiter">Who is answered, and how.</param>
		/// <param name="type">What kind of name.</param>
		/// <param name="id">The ID answered.</param>
		/// <param name="name">Its name, or null when the lookup found no such entity.</param>
		private void Answer(NamingWaiter<NetworkConnection> waiter, NamingSystemType type, long id, string name)
		{
			// Not-found is an answer in the batched form only; see NamingAnswerRule.
			if (!NamingAnswerRule.TryGetReply(waiter.Batched, name, out string reply))
			{
				return;
			}

			if (waiter.Batched)
			{
				replyBatches.Add(waiter.Connection, id, reply);
			}
			else
			{
				SendNamingBroadcast(waiter.Connection, type, id, reply);
			}
		}

		/// <summary>
		/// Sends every queued batched answer, one reply per connection per
		/// <see cref="NamingBatchBroadcast.MaxEntries"/> names.
		/// </summary>
		private void SendReplyBatches(NamingSystemType type)
		{
			if (replyBatches.ConnectionCount == 0)
			{
				return;
			}
			replyBatches.Drain(NamingBatchBroadcast.MaxEntries, (conn, ids, names) => SendNamingBatchBroadcast(conn, type, ids, names));
		}

		/// <summary>
		/// Fetches the names of a set of IDs of one type in one query, then answers every connection
		/// waiting on any of them from the main thread.
		/// </summary>
		/// <param name="type">What kind of name.</param>
		/// <param name="ids">The IDs this request started lookups for. At most <see cref="NamingRequestBatchBroadcast.MaxIDs"/>.</param>
		/// <returns>Asynchronous fetch task.</returns>
		private async Task FetchNamesAsync(NamingSystemType type, long[] ids)
		{
			Dictionary<long, string> found = null;
			try
			{
				if (Server?.Database?.ServiceRegistry == null)
				{
					return;
				}

				switch (type)
				{
					case NamingSystemType.CharacterName:
						{
							if (!Server.Database.ServiceRegistry.TryGet<ICharacterService>(out var characterService))
							{
								return;
							}
							DatabaseResult<IReadOnlyList<CharacterNameData>> result = await characterService.FetchNamesAsync(ids);
							if (!result.IsSuccess)
							{
								// Unanswered, not answered "no such character": the client asks again.
								await Log.Warning("NamingSystem", $"Could not read the names of {ids.Length} character(s): [{result.ErrorCode}] {result.ErrorMessage}");
								return;
							}
							var names = new Dictionary<long, string>(ids.Length);
							if (result.Data != null)
							{
								foreach (CharacterNameData row in result.Data)
								{
									if (!string.IsNullOrWhiteSpace(row.Name))
									{
										names[row.CharacterID] = row.Name;
									}
								}
							}
							found = names;
							break;
						}
					case NamingSystemType.GuildName:
						{
							if (!Server.Database.ServiceRegistry.TryGet<IGuildService>(out var guildService))
							{
								return;
							}
							DatabaseResult<IReadOnlyDictionary<long, string>> result = await guildService.FetchNamesAsync(ids);
							if (!result.IsSuccess)
							{
								await Log.Warning("NamingSystem", $"Could not read the names of {ids.Length} guild(s): [{result.ErrorCode}] {result.ErrorMessage}");
								return;
							}
							var names = new Dictionary<long, string>(ids.Length);
							if (result.Data != null)
							{
								foreach (KeyValuePair<long, string> row in result.Data)
								{
									if (!string.IsNullOrWhiteSpace(row.Value))
									{
										names[row.Key] = row.Value;
									}
								}
							}
							found = names;
							break;
						}
					default:
						return;
				}

				if (found.Count > 0 && Server.DataContainerRegistry.TryGet<INamingSystemMappingData>(out var mappingData))
				{
					LastSeenCacheTracker<long, string> cache = type == NamingSystemType.CharacterName
						? mappingData.CharacterNameByIdCache
						: mappingData.GuildNameByIdCache;
					double now = MonotonicClock.NowSeconds;
					foreach (KeyValuePair<long, string> pair in found)
					{
						cache.Upsert(pair.Key, pair.Value, now);
					}
				}
			}
			catch (Exception ex)
			{
				await Log.Error("NamingSystem", $"Error fetching {type} names ({ids.Length} ID(s)): {ex}");
			}
			finally
			{
				/* Always handed back, answered or not: the waiters are released on the main thread,
				 * where the table lives. If the queue refuses it, the lookups go stale and the next
				 * request restarts them; see inFlightStaleSeconds. */
				Dictionary<long, string> answered = found;
				TryEnqueueMainThread(() => CompleteNameLookups(type, ids, answered));
			}
		}

		/// <summary>
		/// Ends the lookups for a set of IDs and answers every connection that was waiting on any
		/// of them — batched waiters with one reply each. Main thread only.
		/// </summary>
		/// <param name="type">What kind of name.</param>
		/// <param name="ids">The IDs whose fetch finished.</param>
		/// <param name="found">
		/// The names found, by ID. An ID missing from it does not exist, and batched waiters are
		/// told so. Null when the read failed: every waiter is released unanswered and asks again.
		/// </param>
		private void CompleteNameLookups(NamingSystemType type, long[] ids, Dictionary<long, string> found)
		{
			if (Server == null ||
				!Server.DataContainerRegistry.TryGet<INamingSystemRuntimeData>(out var runtimeData))
			{
				return;
			}

			InFlightLookupTable<long, NamingWaiter<NetworkConnection>> table = NameTableFor(runtimeData, type);
			if (table == null)
			{
				return;
			}

			try
			{
				for (int i = 0; i < ids.Length; i++)
				{
					long id = ids[i];
					completedNameWaiters.Clear();
					if (!table.TryComplete(id, completedNameWaiters) || found == null)
					{
						continue;
					}

					found.TryGetValue(id, out string name);
					for (int w = 0; w < completedNameWaiters.Count; w++)
					{
						NamingWaiter<NetworkConnection> waiter = completedNameWaiters[w];
						if (waiter.Connection != null && waiter.Connection.IsActive)
						{
							Answer(waiter, type, id, name);
						}
					}
				}
			}
			finally
			{
				completedNameWaiters.Clear();
				SendReplyBatches(type);
			}
		}

		/// <summary>
		/// Sends a naming broadcast to the specified connection, providing the resolved name for the given ID and type.
		/// </summary>
		/// <param name="conn">Network connection to send the broadcast to.</param>
		/// <param name="type">Type of naming system (character, guild, etc.).</param>
		/// <param name="id">ID of the object to resolve.</param>
		/// <param name="name">Resolved name to send.</param>
		public void SendNamingBroadcast(NetworkConnection conn, NamingSystemType type, long id, string name)
		{
			if (conn == null)
				return;

			NamingBroadcast msg = new NamingBroadcast()
			{
				Type = type,
				ID = id,
				Name = name,
			};

			Server.NetworkWrapper.Broadcast(conn, msg, true, Channel.Reliable);
		}

		/// <summary>
		/// Sends several names of one type to the specified connection, in replies of at most
		/// <see cref="NamingBatchBroadcast.MaxEntries"/> names.
		/// </summary>
		/// <param name="conn">Network connection to send the broadcast to.</param>
		/// <param name="type">Type of naming system (character, guild, etc.).</param>
		/// <param name="ids">IDs answered.</param>
		/// <param name="names">The name of the ID at the same index, or empty when no such entity exists.</param>
		public void SendNamingBatchBroadcast(NetworkConnection conn, NamingSystemType type, long[] ids, string[] names)
		{
			if (conn == null || ids == null || names == null || ids.Length == 0 || ids.Length != names.Length)
				return;

			for (int offset = 0; offset < ids.Length; offset += NamingBatchBroadcast.MaxEntries)
			{
				int count = Math.Min(NamingBatchBroadcast.MaxEntries, ids.Length - offset);
				long[] chunkIds = ids;
				string[] chunkNames = names;
				if (count != ids.Length)
				{
					chunkIds = new long[count];
					chunkNames = new string[count];
					Array.Copy(ids, offset, chunkIds, 0, count);
					Array.Copy(names, offset, chunkNames, 0, count);
				}

				NamingBatchBroadcast msg = new NamingBatchBroadcast()
				{
					Type = type,
					IDs = chunkIds,
					Names = chunkNames,
				};
				Server.NetworkWrapper.Broadcast(conn, msg, true, Channel.Reliable);
			}
		}

		/// <summary>
		/// Handles incoming reverse naming requests from clients, resolves IDs by name for characters.
		/// Checks local cache first, then falls back to async database lookup. Notifies client if not found.
		/// </summary>
		/// <param name="conn">Network connection of the requesting client.</param>
		/// <param name="msg">ReverseNamingBroadcast message containing the type and name to resolve.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		private void OnServerReverseNamingBroadcastReceived(NetworkConnection conn, ReverseNamingBroadcast msg, Channel channel)
		{
			if (conn == null || conn.FirstObject == null)
			{
				return;
			}

			/* A loaded character, like the forward lookup's character-name path requires. Without
			 * it this was a name-enumeration oracle any authenticated connection could drive at
			 * the request budget's rate before it had even finished spawning. */
			IPlayerCharacter requester = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (requester == null || !requester.IsFlagged(CharacterFlags.IsLoaded))
			{
				return;
			}

			if (!TryTakeRequestToken(conn))
			{
				return;
			}

			if (string.IsNullOrWhiteSpace(msg.NameLowerCase))
			{
				SendReverseNamingBroadcast(conn, msg.Type, string.Empty, 0, string.Empty);
				return;
			}

			// Reject oversized names before any cache lookup or DB query.
			if (msg.NameLowerCase.Length > Authentication.CharacterNameMaxLength)
			{
				return;
			}

			if (!Server.DataContainerRegistry.TryGet<INamingSystemRuntimeData>(out var runtimeData) ||
				!Server.DataContainerRegistry.TryGet<INamingSystemMappingData>(out var namingMappingData))
			{
				return;
			}

			var nameLowerCase = msg.NameLowerCase.ToLowerInvariant();
			double now = MonotonicClock.NowSeconds;
			switch (msg.Type)
			{
				case NamingSystemType.CharacterName:
					// check our local scene server first
					if (Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var mappingData) &&
						mappingData.CharactersByLowerCaseName.TryGetValue(nameLowerCase, out IPlayerCharacter character))
					{
						namingMappingData.CharacterIdByNameCache.Upsert(nameLowerCase, character.ID, now);
						namingMappingData.CharacterNameByNameCache.Upsert(nameLowerCase, character.CharacterName, now);
						namingMappingData.CharacterMissingByNameCache.Remove(nameLowerCase);
						SendReverseNamingBroadcast(conn, NamingSystemType.CharacterName, nameLowerCase, character.ID, character.CharacterName);
						break;
					}

					if (namingMappingData.CharacterMissingByNameCache.TryGetAndTouch(nameLowerCase, now, out _))
					{
						SendReverseNamingBroadcast(conn, NamingSystemType.CharacterName, nameLowerCase, 0, string.Empty);
						break;
					}

					if (namingMappingData.CharacterIdByNameCache.TryGetAndTouch(nameLowerCase, now, out long cachedId) &&
						namingMappingData.CharacterNameByNameCache.TryGetAndTouch(nameLowerCase, now, out string cachedName))
					{
						SendReverseNamingBroadcast(conn, NamingSystemType.CharacterName, nameLowerCase, cachedId, cachedName);
						break;
					}

					// then check the database asynchronously
					if (Server.Database?.ServiceRegistry != null)
					{
						BeginLookup(runtimeData.CharacterByNameInFlight, nameLowerCase, conn, 0, () => FetchCharacterByNameAsync(nameLowerCase));
					}
					else
					{
						// let the client know it wasn't found
						SendReverseNamingBroadcast(conn, NamingSystemType.CharacterName, nameLowerCase, 0, string.Empty);
					}
					break;
				case NamingSystemType.GuildName:
					// Currently not supported, implement this if/when needed
					break;
				default:
					break;
			}
		}

		/// <summary>
		/// Fetches a character by name, then answers every connection waiting on it from the main
		/// thread — with the character, or with not-found.
		/// </summary>
		/// <param name="nameLowerCase">Lowercase character name to resolve.</param>
		/// <returns>Asynchronous fetch task.</returns>
		private async Task FetchCharacterByNameAsync(string nameLowerCase)
		{
			long id = 0;
			string name = string.Empty;
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterService>(out var characterService))
				{
					return;
				}

				DatabaseResult<CharacterData?> result = await characterService.FetchAsync(nameLowerCase);
				if (result.IsSuccess && result.Data.HasValue)
				{
					id = result.Data.Value.ID;
					name = result.Data.Value.Name ?? string.Empty;

					if (Server.DataContainerRegistry.TryGet<INamingSystemMappingData>(out var mappingData))
					{
						double now = MonotonicClock.NowSeconds;
						mappingData.CharacterIdByNameCache.Upsert(nameLowerCase, id, now);
						mappingData.CharacterNameByNameCache.Upsert(nameLowerCase, name, now);
						mappingData.CharacterMissingByNameCache.Remove(nameLowerCase);
					}
				}
				else
				{
					/* Only an ANSWER is cached. The service returns success with no row for a name
					 * nobody has, and a validation failure for one nobody can have; either is safe to
					 * remember. Any other failure is the database not answering, and it used to be
					 * cached as "no such character" too — refreshed by every lookup that hit it, so a
					 * player retrying a /tell or an invite kept the entry alive and was told the name
					 * did not exist for as long as they kept trying. */
					bool answered = result.IsSuccess || result.ErrorCode == DatabaseErrorCodes.ValidationError;
					if (!answered)
					{
						await Log.Warning("NamingSystem", $"Could not look up character name '{nameLowerCase}': [{result.ErrorCode}] {result.ErrorMessage}");
					}
					else if (Server.DataContainerRegistry.TryGet<INamingSystemMappingData>(out var mappingData))
					{
						mappingData.CharacterMissingByNameCache.Upsert(nameLowerCase, 1, MonotonicClock.NowSeconds);
					}
				}
			}
			catch (Exception ex)
			{
				await Log.Error("NamingSystem", $"Error fetching character by name '{nameLowerCase}': {ex}");
			}
			finally
			{
				/* Every waiter is answered, found or not: a reverse lookup's caller is waiting on a
				 * yes or a no (a /tell, an invite by name), and not-found is the answer for a name the
				 * database would not resolve either. Always handed back; see FetchNamesAsync. */
				long answeredId = id;
				string answeredName = name;
				TryEnqueueMainThread(() => CompleteLookup(
					data => data.CharacterByNameInFlight,
					nameLowerCase,
					waiter => SendReverseNamingBroadcast(waiter, NamingSystemType.CharacterName, nameLowerCase, answeredId, answeredName)));
			}
		}

		/// <summary>
		/// Sends a reverse naming broadcast to the specified connection, providing the resolved ID and name for the given type and name.
		/// </summary>
		/// <param name="conn">Network connection to send the broadcast to.</param>
		/// <param name="type">Type of naming system (character, guild, etc.).</param>
		/// <param name="nameLowerCase">Lowercase name to resolve.</param>
		/// <param name="id">Resolved ID to send.</param>
		/// <param name="name">Resolved name to send.</param>
		public void SendReverseNamingBroadcast(NetworkConnection conn, NamingSystemType type, string nameLowerCase, long id, string name)
		{
			if (conn == null)
				return;

			ReverseNamingBroadcast msg = new ReverseNamingBroadcast()
			{
				Type = type,
				NameLowerCase = nameLowerCase,
				ID = id,
				Name = name
			};

			Server.NetworkWrapper.Broadcast(conn, msg, true, Channel.Reliable);
		}
	}
}