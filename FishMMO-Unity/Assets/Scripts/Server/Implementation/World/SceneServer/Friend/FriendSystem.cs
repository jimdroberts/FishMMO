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
using FishMMO.Shared.Core;
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
	[RequiresDataContainer(typeof(FriendSystemRuntimeData))]
	[RequiresDataContainer(typeof(AsyncWorkerData))]
	public class FriendSystem : ServerBehaviour, IFriendSystem
	{
		/// <summary>
		/// Maximum number of queued main-thread actions processed per frame.
		/// This time-slices queue draining to avoid frame spikes.
		/// </summary>
		[Header("Main Thread Dispatch")]
		[Tooltip("Max friend-system actions drained from main-thread queue per frame")]
		[SerializeField] private int maxMainThreadActionsPerFrame = 100;

		/// <summary>
		/// Debounce window in milliseconds for friend ingress operations.
		/// </summary>
		[Header("Ingress Protection")]
		[Tooltip("Minimum milliseconds between friend add/remove requests per connection")]
		[SerializeField] private int ingressDebounceMilliseconds = 100;

		/// <summary>
		/// Interval in seconds between ingress-guard cleanup sweeps.
		/// </summary>
		[Tooltip("Seconds between bounded ingress guard cleanup sweeps")]
		[SerializeField] private float ingressSweepIntervalSeconds = 5.0f;

		/// <summary>
		/// Guard entry time-to-live in seconds.
		/// </summary>
		[Tooltip("Seconds before stale ingress guard entries are removed")]
		[SerializeField] private float ingressEntryTtlSeconds = 30.0f;

		/// <summary>
		/// Maximum stale guard entries removed per cleanup sweep.
		/// </summary>
		[Tooltip("Maximum stale ingress guard entries removed per sweep")]
		[SerializeField] private int ingressSweepMaxRemovals = 128;

		/// <summary>
		/// Achievement to increment when a player adds a friend.
		/// </summary>
		[Header("Achievements")]
		public AchievementTemplate FriendAddAchievementTemplate;

		/// <summary>
		/// Operation codes used for ingress guards.
		/// </summary>
		private enum IngressOperation : byte
		{
			AddFriend = 1,
			RemoveFriend = 2,
		}

		/// <summary>
		/// Maximum number of friends allowed per character.
		/// </summary>
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

			if (!Server.DataContainerRegistry.TryGet<IFriendSystemRuntimeData>(out var runtimeData))
			{
				Log.Error("FriendSystem", "InitializeOnce: IFriendSystemRuntimeData not found");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			// Network broadcasts
			Server.NetworkWrapper.RegisterBroadcast<FriendAddNewBroadcast>(OnServerFriendAddNewBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<FriendRemoveBroadcast>(OnServerFriendRemoveBroadcastReceived, true);

			maxMainThreadActionsPerFrame = Mathf.Max(1, maxMainThreadActionsPerFrame);
			ingressDebounceMilliseconds = Mathf.Max(0, ingressDebounceMilliseconds);
			ingressSweepIntervalSeconds = Mathf.Max(0.25f, ingressSweepIntervalSeconds);
			ingressEntryTtlSeconds = Mathf.Max(1.0f, ingressEntryTtlSeconds);
			ingressSweepMaxRemovals = Mathf.Max(1, ingressSweepMaxRemovals);

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
			DrainMainThreadQueue(drainAll: true);

			// Network broadcasts
			Server.NetworkWrapper.UnregisterBroadcast<FriendAddNewBroadcast>(OnServerFriendAddNewBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<FriendRemoveBroadcast>(OnServerFriendRemoveBroadcastReceived);

			if (Server.DataContainerRegistry.TryGet<IFriendSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.IngressGuard?.Clear();
			}
		}

		/// <summary>
		/// Drains queued main-thread actions from the IFriendSystemMainThreadQueueData container.
		/// </summary>
		private void DrainMainThreadQueue(bool drainAll)
		{
			DrainMainThreadQueue<IFriendSystemMainThreadQueueData>(maxMainThreadActionsPerFrame, drainAll);
		}

		/// <summary>
		/// Enqueues an action to be executed on the main thread.
		/// </summary>
		/// <param name="action">The action to enqueue.</param>
		private bool TryEnqueueMainThread(Action action)
		{
			return TryEnqueueMainThread<IFriendSystemMainThreadQueueData>(action);
		}

		/// <summary>
		/// Drains the main-thread queue each frame.
		/// </summary>
		protected override void OnUpdate(float deltaTime)
		{
			DrainMainThreadQueue(drainAll: false);

			if (Server.DataContainerRegistry.TryGet<IFriendSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.IngressGuard.Sweep(ingressSweepIntervalSeconds, ingressEntryTtlSeconds, ingressSweepMaxRemovals);
			}
		}

		/// <summary>
		/// Attempts to acquire ingress debounce and in-flight guard for a connection operation.
		/// </summary>
		private bool TryBeginIngressGuard(int connectionId, IngressOperation operation, out long guardKey)
		{
			if (!Server.DataContainerRegistry.TryGet<IFriendSystemRuntimeData>(out var runtimeData))
			{
				guardKey = 0;
				return false;
			}
			return runtimeData.IngressGuard.TryBegin(connectionId, (byte)operation, ingressDebounceMilliseconds, out guardKey);
		}

		/// <summary>
		/// Releases a previously acquired ingress guard key.
		/// </summary>
		private void EndIngressGuard(long guardKey)
		{
			if (Server?.DataContainerRegistry.TryGet<IFriendSystemRuntimeData>(out var runtimeData) == true)
			{
				runtimeData.IngressGuard.End(guardKey);
			}
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
			if (conn == null || conn.FirstObject == null)
			{
				return;
			}

			IPlayerCharacter player = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (player == null || !CharacterStateValidation.CanAct(player))
				return;

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.AddFriend, out long guardKey))
			{
				return;
			}

			bool deferGuardRelease = false;
			try
			{
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

				deferGuardRelease = TryEnqueueIngressWork(() => AddFriendAsync(conn, characterID, friendCharacterID), guardKey, characterID);
			}
			finally
			{
				if (!deferGuardRelease)
				{
					EndIngressGuard(guardKey); SendServerBusy(conn);
				}
			}
		}

		/// <summary>
		/// Asynchronously verifies the friend character exists, persists the friendship to the database,
		/// and marshals the in-memory update + Broadcast back to the main thread.
		/// </summary>
		/// <param name="conn">Requesting client connection.</param>
		/// <param name="characterID">Requesting character identifier.</param>
		/// <param name="friendCharacterID">Target friend character identifier.</param>
		/// <returns>Asynchronous add-friend processing task.</returns>
		private async Task AddFriendAsync(NetworkConnection conn, long characterID, long friendCharacterID)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterService>(out var characterService))
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
				DatabaseResult saveResult = await friendService.PersistAsync(characterID, friendCharacterID, 1, false);
				if (!saveResult.IsSuccess)
				{
					return;
				}

				// Marshal in-memory update + Broadcast to main thread
				bool friendOnline = friendData.Online;
				TryEnqueueMainThread(() =>
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
					if (friendController.Friends.Count >= maxFriends)
					{
						return;
					}
					if (friendController.Friends.Contains(friendCharacterID))
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

					// Increment achievement for adding a friend
					if (FriendAddAchievementTemplate != null)
					{
						IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();

						if (character != null && character.TryGet(out IAchievementController achievementController) && CharacterStateValidation.CanAct(character))
						{
							achievementController.Increment(FriendAddAchievementTemplate, 1);
						}
					}
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
			if (conn == null || conn.FirstObject == null)
			{
				return;
			}

			IPlayerCharacter player = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (player == null || !CharacterStateValidation.CanAct(player))
				return;

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.RemoveFriend, out long guardKey))
			{
				return;
			}

			bool deferGuardRelease = false;
			try
			{
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

					deferGuardRelease = TryEnqueueIngressWork(() => RemoveFriendAsync(conn, characterID, friendCharacterID), guardKey, characterID);
				}
			}
			finally
			{
				if (!deferGuardRelease)
				{
					EndIngressGuard(guardKey);
					SendServerBusy(conn);
				}
			}
		}

		/// <summary>
		/// Asynchronously deletes a friend relationship from the database.
		/// On success, marshals in-memory removal and client acknowledgement to the main thread.
		/// </summary>
		/// <param name="conn">Owning connection for in-memory state updates and broadcast.</param>
		/// <param name="characterID">Requesting character identifier.</param>
		/// <param name="friendCharacterID">Target friend character identifier.</param>
		/// <returns>Asynchronous remove-friend processing task.</returns>
		private async Task RemoveFriendAsync(NetworkConnection conn, long characterID, long friendCharacterID)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null)
				{
					return;
				}
				if (!Server.Database.ServiceRegistry.TryGet<ICharacterFriendService>(out var friendService))
				{
					return;
				}

				DatabaseResult deleteResult = await friendService.DeleteAsync(characterID, friendCharacterID, long.MaxValue);
				if (!deleteResult.IsSuccess)
				{
					return;
				}

				TryEnqueueMainThread(() =>
				{
					if (conn == null || !conn.IsActive || conn.FirstObject == null)
					{
						return;
					}

					IFriendController friendController = conn.FirstObject.GetComponent<IFriendController>();
					if (friendController == null || friendController.Character.ID != characterID)
					{
						return;
					}

					if (!friendController.Friends.Contains(friendCharacterID))
					{
						return;
					}

					friendController.Friends.Remove(friendCharacterID);
					Server.NetworkWrapper.Broadcast(conn, new FriendRemoveBroadcast()
					{
						CharacterID = friendCharacterID,
					}, true, Channel.Reliable);
				});
			}
			catch (Exception ex)
			{
				await Log.Error("FriendSystem", $"Error removing friend (CharID={characterID}, FriendID={friendCharacterID}): {ex}");
			}
		}

		/// <summary>
		/// Enqueues ingress work and guarantees the ingress guard is released when async processing completes.
		/// </summary>
		private bool TryEnqueueIngressWork(Func<Task> work, long guardKey, long entityKey = 0, [CallerMemberName] string callerName = null)
		{
			return TryEnqueueGuardedAsyncWork(work, EndIngressGuard, guardKey, entityKey, callerName);
		}
	}
}