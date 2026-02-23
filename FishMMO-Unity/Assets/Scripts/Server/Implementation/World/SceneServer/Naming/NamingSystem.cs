using UnityEngine;
using FishNet.Connection;
using FishNet.Transporting;
using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Logging;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Provides name resolution services for game entities, resolving names by ID for characters and guilds.
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
		/// Maximum concurrent in-flight naming lookups per dictionary before new requests are dropped.
		/// </summary>
		private const int MaxInFlightLookups = 5000;

		/// <summary>
		/// Maximum number of queued main-thread actions processed per frame.
		/// This time-slices queue draining to avoid frame spikes.
		/// </summary>
		[Header("Main Thread Dispatch")]
		[Tooltip("Max naming-system actions drained from main-thread queue per frame")]
		[SerializeField] private int maxMainThreadActionsPerFrame = 100;

		/// <summary>
		/// Minimum milliseconds between naming requests per connection.
		/// </summary>
		[Header("Request Protection")]
		[Tooltip("Minimum milliseconds between naming requests per connection")]
		[SerializeField] private int requestDebounceMilliseconds = 75;

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
			Server.NetworkWrapper.RegisterBroadcast<ReverseNamingBroadcast>(OnServerReverseNamingBroadcastReceived, true);

			maxMainThreadActionsPerFrame = Mathf.Max(1, maxMainThreadActionsPerFrame);
			requestDebounceMilliseconds = Mathf.Max(0, requestDebounceMilliseconds);
			cacheTtlSeconds = Mathf.Max(5.0f, cacheTtlSeconds);
			cacheSweepIntervalSeconds = Mathf.Max(0.1f, cacheSweepIntervalSeconds);
			cacheSweepMaxScan = Mathf.Max(1, cacheSweepMaxScan);
			cacheSweepMaxRemove = Mathf.Max(1, cacheSweepMaxRemove);
			if (Server.DataContainerRegistry.TryGet<INamingSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.NextCacheSweepUtc = DateTime.UtcNow;
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
			Server.NetworkWrapper.UnregisterBroadcast<ReverseNamingBroadcast>(OnServerReverseNamingBroadcastReceived);
		}

		/// <summary>
		/// Drains queued main-thread actions from the INamingSystemMainThreadQueueData container.
		/// </summary>
		private void DrainMainThreadQueue(bool drainAll)
		{
			MainThreadQueueHelper.Drain<INamingSystemMainThreadQueueData>(Server, maxMainThreadActionsPerFrame, drainAll);
		}

		/// <summary>
		/// Enqueues an action to be executed on the main thread.
		/// </summary>
		/// <param name="action">The action to enqueue.</param>
		private bool TryEnqueueMainThread(Action action)
		{
			return MainThreadQueueHelper.TryEnqueue<INamingSystemMainThreadQueueData>(Server, action);
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
		/// Checks and updates per-connection request debounce state.
		/// </summary>
		private bool IsRequestDebounced(NetworkConnection conn)
		{
			if (requestDebounceMilliseconds <= 0 || conn == null)
			{
				return false;
			}

			if (!Server.DataContainerRegistry.TryGet<INamingSystemRuntimeData>(out var runtimeData))
			{
				return false;
			}

			DateTime nowUtc = DateTime.UtcNow;
			if (runtimeData.ConnectionRequestTracker.TryGetAndTouch(conn.ClientId, nowUtc, out DateTime lastRequestUtc))
			{
				if ((nowUtc - lastRequestUtc).TotalMilliseconds < requestDebounceMilliseconds)
				{
					return true;
				}
			}

			runtimeData.ConnectionRequestTracker.Upsert(conn.ClientId, nowUtc, nowUtc);
			return false;
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

			DateTime nowUtc = DateTime.UtcNow;
			if (nowUtc < runtimeData.NextCacheSweepUtc)
			{
				return;
			}

			runtimeData.NextCacheSweepUtc = nowUtc.AddSeconds(cacheSweepIntervalSeconds);
			runtimeData.ConnectionRequestTracker.SweepExpired(
				nowUtc,
				TimeSpan.FromSeconds(cacheTtlSeconds),
				cacheSweepMaxScan,
				cacheSweepMaxRemove);

			if (Server.DataContainerRegistry.TryGet<INamingSystemMappingData>(out var mappingData))
			{
				mappingData.SweepAllCaches(nowUtc, TimeSpan.FromSeconds(cacheTtlSeconds), cacheSweepMaxScan, cacheSweepMaxRemove);
			}
		}

		/// <summary>
		/// Handles incoming naming requests from clients, resolves names by ID for characters and guilds.
		/// Checks local cache first, then falls back to async database lookup.
		/// </summary>
		/// <param name="conn">Network connection of the requesting client.</param>
		/// <param name="msg">NamingBroadcast message containing the type and ID to resolve.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		private void OnServerNamingBroadcastReceived(NetworkConnection conn, NamingBroadcast msg, Channel channel)
		{
			if (conn == null)
			{
				return;
			}
			if (IsRequestDebounced(conn))
			{
				return;
			}

			DateTime nowUtc = DateTime.UtcNow;
			if (!Server.DataContainerRegistry.TryGet<INamingSystemRuntimeData>(out var runtimeData) ||
				!Server.DataContainerRegistry.TryGet<INamingSystemMappingData>(out var namingMappingData))
			{
				return;
			}

			switch (msg.Type)
			{
				case NamingSystemType.CharacterName:
					// check our local scene server first
					if (Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var mappingData) &&
						mappingData.CharactersByID.TryGetValue(msg.ID, out IPlayerCharacter character))
					{
						namingMappingData.CharacterNameByIdCache.Upsert(msg.ID, character.CharacterName, nowUtc);
						SendNamingBroadcast(conn, NamingSystemType.CharacterName, msg.ID, character.CharacterName);
					}
					else if (namingMappingData.CharacterNameByIdCache.TryGetAndTouch(msg.ID, nowUtc, out string cachedCharacterName))
					{
						SendNamingBroadcast(conn, NamingSystemType.CharacterName, msg.ID, cachedCharacterName);
					}
					// then check the database asynchronously
					else if (Server.Database?.ServiceRegistry != null)
					{
						if (runtimeData.CharacterNameByIdInFlight.Count < MaxInFlightLookups &&
							runtimeData.CharacterNameByIdInFlight.TryAdd(msg.ID, 0) &&
							!TryEnqueueAsyncWork(() => FetchCharacterNameAsync(conn, msg.ID), msg.ID))
						{
							runtimeData.CharacterNameByIdInFlight.TryRemove(msg.ID, out _);
						}
					}
					break;
				case NamingSystemType.GuildName:
					if (namingMappingData.GuildNameByIdCache.TryGetAndTouch(msg.ID, nowUtc, out string cachedGuildName))
					{
						SendNamingBroadcast(conn, NamingSystemType.GuildName, msg.ID, cachedGuildName);
						break;
					}

					// get the name from the database asynchronously
					if (Server.Database?.ServiceRegistry != null)
					{
						if (runtimeData.GuildNameByIdInFlight.Count < MaxInFlightLookups &&
							runtimeData.GuildNameByIdInFlight.TryAdd(msg.ID, 0) &&
							!TryEnqueueAsyncWork(() => FetchGuildNameAsync(conn, msg.ID), msg.ID))
						{
							runtimeData.GuildNameByIdInFlight.TryRemove(msg.ID, out _);
						}
					}
					break;
				default:
					break;
			}
		}

		/// <summary>
		/// Asynchronously fetches a character name by ID and marshals the Broadcast back to the main thread.
		/// </summary>
		/// <param name="conn">Requesting connection.</param>
		/// <param name="characterID">Character identifier to resolve.</param>
		/// <returns>Asynchronous fetch task.</returns>
		private async Task FetchCharacterNameAsync(NetworkConnection conn, long characterID)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterService>(out var characterService))
				{
					return;
				}

				DatabaseResult<CharacterData?> result = await characterService.FetchAsync(characterID);
				if (!result.IsSuccess || !result.Data.HasValue)
				{
					return;
				}

				string name = result.Data.Value.Name;
				if (string.IsNullOrWhiteSpace(name))
				{
					return;
				}

				if (Server.DataContainerRegistry.TryGet<INamingSystemMappingData>(out var mappingData))
				{
					mappingData.CharacterNameByIdCache.Upsert(characterID, name, DateTime.UtcNow);
				}

				TryEnqueueMainThread(() =>
				{
					if (conn == null || !conn.IsActive) return;
					SendNamingBroadcast(conn, NamingSystemType.CharacterName, characterID, name);
				});
			}
			catch (Exception ex)
			{
				await Log.Error("NamingSystem", $"Error fetching character name (ID={characterID}): {ex}");
			}
			finally
			{
				if (Server?.DataContainerRegistry.TryGet<INamingSystemRuntimeData>(out var runtimeData) == true)
				{
					runtimeData.CharacterNameByIdInFlight.TryRemove(characterID, out _);
				}
			}
		}

		/// <summary>
		/// Asynchronously fetches a guild name by ID and marshals the Broadcast back to the main thread.
		/// </summary>
		/// <param name="conn">Requesting connection.</param>
		/// <param name="guildID">Guild identifier to resolve.</param>
		/// <returns>Asynchronous fetch task.</returns>
		private async Task FetchGuildNameAsync(NetworkConnection conn, long guildID)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<IGuildService>(out var guildService))
				{
					return;
				}

				DatabaseResult<string> result = await guildService.FetchNameAsync(guildID);
				if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.Data))
				{
					return;
				}

				string name = result.Data;
				if (Server.DataContainerRegistry.TryGet<INamingSystemMappingData>(out var mappingData))
				{
					mappingData.GuildNameByIdCache.Upsert(guildID, name, DateTime.UtcNow);
				}

				TryEnqueueMainThread(() =>
				{
					if (conn == null || !conn.IsActive) return;
					SendNamingBroadcast(conn, NamingSystemType.GuildName, guildID, name);
				});
			}
			catch (Exception ex)
			{
				await Log.Error("NamingSystem", $"Error fetching guild name (ID={guildID}): {ex}");
			}
			finally
			{
				if (Server?.DataContainerRegistry.TryGet<INamingSystemRuntimeData>(out var runtimeData) == true)
				{
					runtimeData.GuildNameByIdInFlight.TryRemove(guildID, out _);
				}
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
		/// Handles incoming reverse naming requests from clients, resolves IDs by name for characters.
		/// Checks local cache first, then falls back to async database lookup. Notifies client if not found.
		/// </summary>
		/// <param name="conn">Network connection of the requesting client.</param>
		/// <param name="msg">ReverseNamingBroadcast message containing the type and name to resolve.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		private void OnServerReverseNamingBroadcastReceived(NetworkConnection conn, ReverseNamingBroadcast msg, Channel channel)
		{
			if (conn == null)
			{
				return;
			}
			if (IsRequestDebounced(conn))
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
			DateTime nowUtc = DateTime.UtcNow;
			switch (msg.Type)
			{
				case NamingSystemType.CharacterName:
					// check our local scene server first
					if (Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var mappingData) &&
						mappingData.CharactersByLowerCaseName.TryGetValue(nameLowerCase, out IPlayerCharacter character))
					{
						namingMappingData.CharacterIdByNameCache.Upsert(nameLowerCase, character.ID, nowUtc);
						namingMappingData.CharacterNameByNameCache.Upsert(nameLowerCase, character.CharacterName, nowUtc);
						namingMappingData.CharacterMissingByNameCache.Remove(nameLowerCase);
						SendReverseNamingBroadcast(conn, NamingSystemType.CharacterName, nameLowerCase, character.ID, character.CharacterName);
						break;
					}

					if (namingMappingData.CharacterMissingByNameCache.TryGetAndTouch(nameLowerCase, nowUtc, out _))
					{
						SendReverseNamingBroadcast(conn, NamingSystemType.CharacterName, nameLowerCase, 0, string.Empty);
						break;
					}

					if (namingMappingData.CharacterIdByNameCache.TryGetAndTouch(nameLowerCase, nowUtc, out long cachedId) &&
						namingMappingData.CharacterNameByNameCache.TryGetAndTouch(nameLowerCase, nowUtc, out string cachedName))
					{
						SendReverseNamingBroadcast(conn, NamingSystemType.CharacterName, nameLowerCase, cachedId, cachedName);
						break;
					}

					// then check the database asynchronously
					if (Server.Database?.ServiceRegistry != null)
					{
						if (runtimeData.CharacterByNameInFlight.Count < MaxInFlightLookups &&
							runtimeData.CharacterByNameInFlight.TryAdd(nameLowerCase, 0) &&
							!TryEnqueueAsyncWork(() => FetchCharacterByNameAsync(conn, nameLowerCase)))
						{
							runtimeData.CharacterByNameInFlight.TryRemove(nameLowerCase, out _);
						}
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
		/// Asynchronously fetches a character by name and marshals the Broadcast back to the main thread.
		/// Sends a not-found response if the character does not exist.
		/// </summary>
		/// <param name="conn">Requesting connection.</param>
		/// <param name="nameLowerCase">Lowercase character name to resolve.</param>
		/// <returns>Asynchronous fetch task.</returns>
		private async Task FetchCharacterByNameAsync(NetworkConnection conn, string nameLowerCase)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterService>(out var characterService))
				{
					TryEnqueueMainThread(() =>
					{
						if (conn == null || !conn.IsActive) return;
						SendReverseNamingBroadcast(conn, NamingSystemType.CharacterName, nameLowerCase, 0, "");
					});
					return;
				}

				DatabaseResult<CharacterData?> result = await characterService.FetchAsync(nameLowerCase);
				if (result.IsSuccess && result.Data.HasValue)
				{
					long id = result.Data.Value.ID;
					string name = result.Data.Value.Name;

					if (Server.DataContainerRegistry.TryGet<INamingSystemMappingData>(out var mappingData))
					{
						DateTime nowUtc = DateTime.UtcNow;
						mappingData.CharacterIdByNameCache.Upsert(nameLowerCase, id, nowUtc);
						mappingData.CharacterNameByNameCache.Upsert(nameLowerCase, name, nowUtc);
						mappingData.CharacterMissingByNameCache.Remove(nameLowerCase);
					}

					TryEnqueueMainThread(() =>
					{
						if (conn == null || !conn.IsActive) return;
						SendReverseNamingBroadcast(conn, NamingSystemType.CharacterName, nameLowerCase, id, name);
					});
				}
				else
				{
					if (Server.DataContainerRegistry.TryGet<INamingSystemMappingData>(out var mappingData))
					{
						mappingData.CharacterMissingByNameCache.Upsert(nameLowerCase, 1, DateTime.UtcNow);
					}

					// let the client know it wasn't found
					TryEnqueueMainThread(() =>
					{
						if (conn == null || !conn.IsActive) return;
						SendReverseNamingBroadcast(conn, NamingSystemType.CharacterName, nameLowerCase, 0, string.Empty);
					});
				}
			}
			catch (Exception ex)
			{
				await Log.Error("NamingSystem", $"Error fetching character by name '{nameLowerCase}': {ex}");
			}
			finally
			{
				if (Server?.DataContainerRegistry.TryGet<INamingSystemRuntimeData>(out var runtimeData) == true)
				{
					runtimeData.CharacterByNameInFlight.TryRemove(nameLowerCase, out _);
				}
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