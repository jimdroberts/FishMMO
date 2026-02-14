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
using FishMMO.Server.Implementation;
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
	[RequiresDataContainer(typeof(NamingSystemMainThreadQueueData))]
	[RequiresDataContainer(typeof(AsyncWorkerData))]
	public class NamingSystem : ServerBehaviour, INamingSystem<NetworkConnection>
	{
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

			// Network broadcasts
			Server.NetworkWrapper.RegisterBroadcast<NamingBroadcast>(OnServerNamingBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<ReverseNamingBroadcast>(OnServerReverseNamingBroadcastReceived, true);

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
			DrainMainThreadQueue();

			// Network broadcasts
			Server.NetworkWrapper.UnregisterBroadcast<NamingBroadcast>(OnServerNamingBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<ReverseNamingBroadcast>(OnServerReverseNamingBroadcastReceived);
		}

		/// <summary>
		/// Drains queued main-thread actions from the INamingSystemMainThreadQueueData container.
		/// </summary>
		private void DrainMainThreadQueue()
		{
			if (Server?.DataContainerRegistry.TryGet<INamingSystemMainThreadQueueData>(out var queueData) == true)
			{
				queueData.Drain();
			}
		}

		/// <summary>
		/// Enqueues an action to be executed on the main thread.
		/// </summary>
		/// <param name="action">The action to enqueue.</param>
		private void EnqueueMainThread(Action action)
		{
			if (Server?.DataContainerRegistry.TryGet<INamingSystemMainThreadQueueData>(out var queueData) == true)
			{
				queueData.Enqueue(action);
			}
		}

		/// <summary>
		/// Drains the main-thread queue each frame.
		/// </summary>
		public override void OnLateUpdate(float deltaTime)
		{
			DrainMainThreadQueue();
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

			switch (msg.Type)
			{
				case NamingSystemType.CharacterName:
					// check our local scene server first
					if (Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var mappingData) &&
						mappingData.CharactersByID.TryGetValue(msg.ID, out IPlayerCharacter character))
					{
						SendNamingBroadcast(conn, NamingSystemType.CharacterName, msg.ID, character.CharacterName);
					}
					// then check the database asynchronously
					else if (Server.Database?.ServiceRegistry != null)
					{
						TryEnqueueAsyncWork(() => FetchCharacterNameAsync(conn, msg.ID), msg.ID);
					}
					break;
				case NamingSystemType.GuildName:
					// get the name from the database asynchronously
					if (Server.Database?.ServiceRegistry != null)
					{
						TryEnqueueAsyncWork(() => FetchGuildNameAsync(conn, msg.ID), msg.ID);
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
				if (!Server.Database.ServiceRegistry.TryGet<ICharacterService>(out var characterService))
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

				EnqueueMainThread(() =>
				{
					if (conn == null || !conn.IsActive) return;
					SendNamingBroadcast(conn, NamingSystemType.CharacterName, characterID, name);
				});
			}
			catch (Exception ex)
			{
				await Log.Error("NamingSystem", $"Error fetching character name (ID={characterID}): {ex}");
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
				if (!Server.Database.ServiceRegistry.TryGet<IGuildService>(out var guildService))
				{
					return;
				}

				DatabaseResult<string> result = await guildService.FetchNameAsync(guildID);
				if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.Data))
				{
					return;
				}

				string name = result.Data;

				EnqueueMainThread(() =>
				{
					if (conn == null || !conn.IsActive) return;
					SendNamingBroadcast(conn, NamingSystemType.GuildName, guildID, name);
				});
			}
			catch (Exception ex)
			{
				await Log.Error("NamingSystem", $"Error fetching guild name (ID={guildID}): {ex}");
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

			if (string.IsNullOrWhiteSpace(msg.NameLowerCase))
			{
				SendReverseNamingBroadcast(conn, msg.Type, string.Empty, 0, string.Empty);
				return;
			}

			var nameLowerCase = msg.NameLowerCase.ToLowerInvariant();
			switch (msg.Type)
			{
				case NamingSystemType.CharacterName:
					// check our local scene server first
					if (Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var mappingData) &&
						mappingData.CharactersByLowerCaseName.TryGetValue(nameLowerCase, out IPlayerCharacter character))
					{
						SendReverseNamingBroadcast(conn, NamingSystemType.CharacterName, nameLowerCase, character.ID, character.CharacterName);
						break;
					}
					// then check the database asynchronously
					if (Server.Database?.ServiceRegistry != null)
					{
						TryEnqueueAsyncWork(() => FetchCharacterByNameAsync(conn, nameLowerCase));
					}
					else
					{
						// let the client know it wasn't found
						SendReverseNamingBroadcast(conn, NamingSystemType.CharacterName, nameLowerCase, 0, "");
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
				if (!Server.Database.ServiceRegistry.TryGet<ICharacterService>(out var characterService))
				{
					EnqueueMainThread(() =>
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

					EnqueueMainThread(() =>
					{
						if (conn == null || !conn.IsActive) return;
						SendReverseNamingBroadcast(conn, NamingSystemType.CharacterName, nameLowerCase, id, name);
					});
				}
				else
				{
					// let the client know it wasn't found
					EnqueueMainThread(() =>
					{
						if (conn == null || !conn.IsActive) return;
						SendReverseNamingBroadcast(conn, NamingSystemType.CharacterName, nameLowerCase, 0, "");
					});
				}
			}
			catch (Exception ex)
			{
				await Log.Error("NamingSystem", $"Error fetching character by name '{nameLowerCase}': {ex}");
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

		/// <summary>
		/// Enqueues an async work item to the centralized async worker for controlled execution.
		/// Returns false when the queue is unavailable or rejected due to backpressure.
		/// </summary>
		/// <param name="work">Asynchronous work delegate to queue.</param>
		/// <param name="entityKey">Optional entity key for ordered execution.</param>
		/// <param name="callerName">Optional caller name used for diagnostics.</param>
		/// <returns>True if work was accepted by the queue; otherwise false.</returns>
		private bool TryEnqueueAsyncWork(Func<Task> work, long entityKey = 0, [CallerMemberName] string callerName = null)
		{
			if (Server?.DataContainerRegistry.TryGet<IAsyncWorkerData>(out var asyncWorker) == true)
			{
				if (entityKey != 0)
				{
					if (asyncWorker.Enqueue(work, entityKey, callerName))
					{
						return true;
					}

					Log.Warning("NamingSystem", $"{callerName}: Async worker queue rejected work (entityKey={entityKey}).");
					return false;
				}
				else
				{
					if (asyncWorker.Enqueue(work, callerName))
					{
						return true;
					}

					Log.Warning("NamingSystem", $"{callerName}: Async worker queue rejected work.");
					return false;
				}
			}

			Log.Warning("NamingSystem", $"{callerName}: IAsyncWorkerData unavailable; work was not enqueued.");
			return false;
		}
	}
}