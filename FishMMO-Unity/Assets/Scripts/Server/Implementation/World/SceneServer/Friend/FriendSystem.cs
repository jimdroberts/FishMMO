using UnityEngine;
using UnityEngine.SceneManagement;
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
	/// Manages player friend lists, including adding and removing friends with validation and database persistence.
	/// Game logic and Broadcasts run synchronously on the main thread.
	/// Database operations are fire-and-forget async to avoid blocking the main thread.
	/// Results from async DB queries that require main-thread Broadcasts are marshalled via IFriendSystemMainThreadQueueData.
	/// </summary>
	[CreateAssetMenu(fileName = "FriendSystem", menuName = "FishMMO/Server/SceneServer/Friend System", order = 1)]
	[RequiresDataContainer(typeof(FriendSystemMainThreadQueueData))]
	[RequiresDataContainer(typeof(AsyncWorkerData))]
	public class FriendSystem : ServerBehaviour, IFriendSystem
	{
		[SerializeField]
		private int maxFriends = 100;

		/// <summary>
		/// Maximum number of friends allowed per character.
		/// </summary>
		public int MaxFriends { get { return maxFriends; } }

		/// <summary>
		/// Initializes the friend system, registering broadcast handlers for friend add and remove requests.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			if (Server == null)
			{
				Log.Error("FriendSystem", "InitializeOnce: Server is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (!Server.DataContainerRegistry.TryGet<IFriendSystemMainThreadQueueData>(out _))
			{
				Log.Error("FriendSystem", "Failed to initialize: IFriendSystemMainThreadQueueData not found");
				return ServerComponentInitializationStatus.FailedToGetDataContainer;
			}

			if (!Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, Scene> characterSystem))
			{
				Log.Error("FriendSystem", "InitializeOnce: ICharacterSystem not found");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			// Network broadcasts
			Server.NetworkWrapper.RegisterBroadcast<FriendAddNewBroadcast>(OnServerFriendAddNewBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<FriendRemoveBroadcast>(OnServerFriendRemoveBroadcastReceived, true);

			Log.Debug("FriendSystem", $"Initialized (MaxFriends={maxFriends})");
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Cleans up the friend system, unregistering broadcast handlers.
		/// </summary>
		public override void OnDeinitialize()
		{
			if (Server == null)
			{
				Log.Error("FriendSystem", "OnDeinitialize: Server is null");
				return;
			}

			// Drain any remaining queued main-thread actions
			DrainMainThreadQueue();

			// Network broadcasts
			Server.NetworkWrapper.UnregisterBroadcast<FriendAddNewBroadcast>(OnServerFriendAddNewBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<FriendRemoveBroadcast>(OnServerFriendRemoveBroadcastReceived);
		}

		/// <summary>
		/// Drains queued main-thread actions from the IFriendSystemMainThreadQueueData container.
		/// </summary>
		private void DrainMainThreadQueue()
		{
			if (Server?.DataContainerRegistry.TryGet<IFriendSystemMainThreadQueueData>(out var queueData) == true)
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
			if (Server?.DataContainerRegistry.TryGet<IFriendSystemMainThreadQueueData>(out var queueData) == true)
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
		/// Handles broadcast to add a new friend for a player character.
		/// Validates character, friend count, and prevents self-friending.
		/// Fires an async task that verifies the friend character exists, persists the friendship,
		/// and marshals the in-memory update + Broadcast back to the main thread.
		/// </summary>
		/// <param name="conn">Network connection of the requesting client.</param>
		/// <param name="msg">FriendAddNewBroadcast message containing friend data.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		public void OnServerFriendAddNewBroadcastReceived(NetworkConnection conn, FriendAddNewBroadcast msg, Channel channel)
		{
			if (conn.FirstObject == null)
			{
				return;
			}
			IFriendController friendController = conn.FirstObject.GetComponent<IFriendController>();

			// Validate character
			if (friendController == null ||
				friendController.Friends.Count >= maxFriends)
			{
				return;
			}

			// Validate database availability
			if (Server == null || Server.Database?.ServiceRegistry == null)
			{
				return;
			}

			// Are we trying to become our own friend again...
			long characterID = friendController.Character.ID;
			if (characterID == msg.CharacterID)
			{
				return;
			}

			// Capture immutable data for the async path
			long friendCharacterID = msg.CharacterID;

			EnqueueAsyncWork(() => AddFriendAsync(conn, characterID, friendCharacterID));
		}

		/// <summary>
		/// Asynchronously verifies the friend character exists, persists the friendship to the database,
		/// and marshals the in-memory update + Broadcast back to the main thread.
		/// </summary>
		private async Task AddFriendAsync(NetworkConnection conn, long characterID, long friendCharacterID)
		{
			try
			{
				if (!Server.Database.ServiceRegistry.TryGet<ICharacterService>(out var characterService))
				{
					return;
				}
				if (!Server.Database.ServiceRegistry.TryGet<ICharacterFriendService>(out var friendService))
				{
					return;
				}

				// Verify the friend character exists and get online status
				DatabaseResult<CharacterData?> fetchResult = await characterService.FetchAsync(friendCharacterID);
				if (!fetchResult.IsSuccess || !fetchResult.Data.HasValue)
				{
					return;
				}

				CharacterData friendData = fetchResult.Data.Value;

				// Persist the friendship to the database
				DatabaseResult saveResult = await friendService.PersistAsync(characterID, friendCharacterID, 1);
				if (!saveResult.IsSuccess)
				{
					return;
				}

				// Marshal in-memory update + Broadcast to main thread
				bool friendOnline = friendData.Online;
				EnqueueMainThread(() =>
				{
					// Re-validate the connection is still valid
					if (conn == null || !conn.IsActive || conn.FirstObject == null)
					{
						return;
					}

					IFriendController friendController = conn.FirstObject.GetComponent<IFriendController>();
					if (friendController == null)
					{
						return;
					}

					// Add the friend to the characters friend controller
					friendController.AddFriend(friendCharacterID);

					// Tell the character they added a new friend!
					Server.NetworkWrapper.Broadcast(conn, new FriendAddBroadcast()
					{
						CharacterID = friendCharacterID,
						Online = friendOnline,
					}, true, Channel.Reliable);
				});
			}
			catch (Exception ex)
			{
				await Log.Error("FriendSystem", $"Error adding friend (CharID={characterID}, FriendID={friendCharacterID}): {ex}");
			}
		}

		/// <summary>
		/// Handles broadcast to remove a friend for a player character.
		/// Validates character, removes friend from in-memory state and broadcasts immediately,
		/// then fires an async task to persist the removal to the database.
		/// </summary>
		/// <param name="conn">Network connection of the requesting client.</param>
		/// <param name="msg">FriendRemoveBroadcast message containing friend removal data.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		public void OnServerFriendRemoveBroadcastReceived(NetworkConnection conn, FriendRemoveBroadcast msg, Channel channel)
		{
			if (conn.FirstObject == null)
			{
				return;
			}
			IFriendController friendController = conn.FirstObject.GetComponent<IFriendController>();

			// Validate character
			if (friendController == null)
			{
				return;
			}

			// Remove the friend if it exists in-memory
			if (friendController.Friends.Contains(msg.CharacterID))
			{
				long characterID = friendController.Character.ID;
				long friendCharacterID = msg.CharacterID;

				// Remove from in-memory state immediately
				friendController.Friends.Remove(friendCharacterID);

				// Tell the character they removed a friend
				Server.NetworkWrapper.Broadcast(conn, new FriendRemoveBroadcast()
				{
					CharacterID = friendCharacterID,
				}, true, Channel.Reliable);

				// Fire-and-forget async DB delete
				EnqueueAsyncWork(() => DeleteFriendAsync(characterID, friendCharacterID));
			}
		}

		/// <summary>
		/// Asynchronously deletes a friend relationship from the database.
		/// </summary>
		private async Task DeleteFriendAsync(long characterID, long friendCharacterID)
		{
			try
			{
				if (Server.Database?.ServiceRegistry == null)
				{
					return;
				}
				if (!Server.Database.ServiceRegistry.TryGet<ICharacterFriendService>(out var friendService))
				{
					return;
				}

				await friendService.DeleteAsync(characterID, friendCharacterID, long.MaxValue);
			}
			catch (Exception ex)
			{
				await Log.Error("FriendSystem", $"Error deleting friend (CharID={characterID}, FriendID={friendCharacterID}): {ex}");
			}
		}

		/// <summary>
		/// Enqueues an async work item to the centralized async worker for controlled execution.
		/// </summary>
		private void EnqueueAsyncWork(Func<Task> work, long entityKey = 0, [CallerMemberName] string callerName = null)
		{
			if (Server?.DataContainerRegistry.TryGet<IAsyncWorkerData>(out var asyncWorker) == true)
			{
				if (entityKey != 0)
					asyncWorker.Enqueue(work, entityKey, callerName);
				else
					asyncWorker.Enqueue(work, callerName);
			}
		}
	}
}