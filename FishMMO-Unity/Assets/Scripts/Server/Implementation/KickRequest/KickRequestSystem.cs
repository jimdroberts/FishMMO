using FishNet.Connection;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// System for processing kick requests from the database and disconnecting accounts as needed.
	/// Periodically polls the database for new kick requests and processes them asynchronously.
	/// Kick/Disconnect calls are marshalled back to the main thread via a RuntimeDataContainer queue.
	/// </summary>
	[CreateAssetMenu(fileName = "KickRequestSystem", menuName = "FishMMO/Server/WorldServer/Kick Request System", order = 1)]
	[RequiresDataContainer(typeof(KickRequestSystemQueueData))]
	[RequiresDataContainer(typeof(KickRequestSystemMainThreadQueueData))]
	[RequiresDataContainer(typeof(AsyncWorkerData))]
	public class KickRequestSystem : ServerBehaviour, IKickRequestSystem
	{
		/// <summary>
		/// Maximum number of queued main-thread actions processed per frame.
		/// This time-slices queue draining to avoid frame spikes.
		/// </summary>
		[Header("Main Thread Dispatch")]
		[Tooltip("Max kick-request actions drained from main-thread queue per frame")]
		[SerializeField]
		private int maxMainThreadActionsPerFrame = 100;

		/// <summary>
		/// Gets or sets the server kick request update pump rate limit in seconds.
		/// </summary>
		[Tooltip("The server kick request update pump rate limit in seconds.")]
		[SerializeField]
		private float updatePumpRate = 5.0f;
		/// <summary>
		/// Maximum number of kick requests to fetch per poll.
		/// </summary>
		[SerializeField]
		private int updateFetchCount = 100;
		/// <summary>
		/// Dedicated lock object for synchronizing kick-request processing state.
		/// Must NOT lock on the <see cref="IKickRequestSystemQueueData"/> interface reference
		/// since the concrete type is publicly visible and could be locked externally.
		/// </summary>
		private readonly object kickLock = new object();

		/// <summary>
		/// Gets or sets the server kick request update pump rate limit in seconds.
		/// </summary>
		public float UpdatePumpRate { get { return updatePumpRate; } set { updatePumpRate = value; } }
		/// <summary>
		/// Gets or sets the maximum number of kick requests to fetch per poll.
		/// </summary>
		public int UpdateFetchCount { get { return updateFetchCount; } set { updateFetchCount = value; } }

		/// <summary>
		/// Called once to initialize the system. Subscribes to server connection state events.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			if (Server == null)
			{
				Log.Error("KickRequestSystem", "InitializeOnce: Server is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (ServerManager == null)
			{
				Log.Error("KickRequestSystem", "InitializeOnce: ServerManager is null");
				return ServerComponentInitializationStatus.FailedToFindServerManager;
			}

			// Verify required data containers
			if (!Server.DataContainerRegistry.TryGet<IKickRequestSystemQueueData>(out _))
			{
				Log.Error("KickRequestSystem", "Failed to initialize: IKickRequestSystemQueueData not found");
				return ServerComponentInitializationStatus.FailedToGetDataContainer;
			}

			if (!Server.DataContainerRegistry.TryGet<IKickRequestSystemMainThreadQueueData>(out _))
			{
				Log.Error("KickRequestSystem", "Failed to initialize: IKickRequestSystemMainThreadQueueData not found");
				return ServerComponentInitializationStatus.FailedToGetDataContainer;
			}

			// Connection state events
			SubscribeToConnectionEvents();

			// Periodic callbacks
			if (Server is IPeriodicUpdateSystem periodicSystem)
			{
				periodicSystem.RegisterPeriodicCallback(updatePumpRate, OnPeriodicUpdate);
			}

			maxMainThreadActionsPerFrame = Mathf.Max(1, maxMainThreadActionsPerFrame);

			Log.Debug("KickRequestSystem", $"Initialized (UpdatePumpRate={updatePumpRate}s, FetchCount={updateFetchCount})");
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Called when the system is being destroyed. Unsubscribes from server connection state events.
		/// Drains remaining main-thread responses so clients get their final messages.
		/// </summary>
		public override void OnDeinitialize()
		{
			if (Server == null)
			{
				Log.Error("KickRequestSystem", "OnDeinitialize: Server is null");
				return;
			}

			if (ServerManager == null)
			{
				Log.Error("KickRequestSystem", "OnDeinitialize: ServerManager is null");
				return;
			}

			// Drain remaining responses
			DrainMainThreadQueue(drainAll: true);

			// Connection state events
			UnsubscribeFromConnectionEvents();

			// Periodic callbacks
			if (Server is IPeriodicUpdateSystem periodicSystem)
			{
				periodicSystem.UnregisterPeriodicCallback(OnPeriodicUpdate);
			}
		}

		/// <summary>
		/// Handles remote connection disconnects. Deletes kick requests for accounts that disconnect.
		/// Database operation is performed asynchronously to avoid blocking the callback thread.
		/// </summary>
		/// <param name="conn">The network connection.</param>
		protected override void OnRemoteConnectionStopped(NetworkConnection conn)
		{
			if (Server.AccountManager.GetAccountNameByConnection(conn, out string accountName))
			{
				long entityKey = (long)accountName.GetDeterministicHashCode();
				if (!TryEnqueueAsyncWork(() => DeleteKickRequestAsync(accountName), entityKey))
				{
					Log.Warning("KickRequestSystem", $"Failed to enqueue kick-request cleanup for account '{accountName}'.");
				}
			}
		}

		/// <summary>
		/// Asynchronously deletes a kick request for the specified account.
		/// </summary>
		/// <param name="accountName">The account name to delete the kick request for.</param>
		private async Task DeleteKickRequestAsync(string accountName)
		{
			try
			{
				if (Server.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<IKickRequestService>(out var kickRequestService))
				{
					return;
				}

				DatabaseResult<int> result = await kickRequestService.DeleteAsync(accountName);
				if (!result.IsSuccess)
				{
					await Log.Warning("KickRequestSystem", $"DeleteKickRequestAsync DB error for {accountName}: {result.ErrorCode} - {result.ErrorMessage}");
				}
			}
			catch (Exception ex)
			{
				await Log.Error("KickRequestSystem", $"Error deleting kick request for {accountName}: {ex}");
			}
		}

		/// <summary>
		/// Drains the main-thread response queue each frame.
		/// All network operations from async workers are marshalled through this queue
		/// to ensure they execute on the main Unity thread.
		/// </summary>
		/// <param name="deltaTime">Time elapsed since last frame.</param>
		protected override void OnUpdate(float deltaTime)
		{
			DrainMainThreadQueue(drainAll: false);
		}

		/// <summary>
		/// Periodic callback that fetches and processes kick requests from the database asynchronously.
		/// </summary>
		/// <param name="deltaTime">Delta time parameter (unused).</param>
		private void OnPeriodicUpdate(float deltaTime)
		{
			if (!Initialized || Server == null || Server.ServerState != ConnectionState.Started)
			{
				return;
			}

			if (!TryEnqueueAsyncWork(() => ProcessKickRequestsAsync()))
			{
				Log.Warning("KickRequestSystem", "Failed to enqueue periodic kick-request processing work item.");
			}
		}

		/// <summary>
		/// Asynchronously fetches and processes kick requests from the database.
		/// Fetches new requests since the last poll, then kicks matching connections.
		/// Kick calls are marshalled to the main thread since FishNet is not thread-safe.
		/// </summary>
		private async Task ProcessKickRequestsAsync()
		{
			if (!Server.DataContainerRegistry.TryGet<IKickRequestSystemQueueData>(out var data))
			{
				return;
			}

			lock (this.kickLock)
			{
				if (data.IsProcessing)
				{
					return;
				}

				data.IsProcessing = true;
			}

			try
			{
				if (Server.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<IKickRequestService>(out var kickRequestService) ||
					!Server.Database.ServiceRegistry.TryGet<IAccountService>(out var accountService))
				{
					return;
				}

				// Fetch kick requests from the database
				DatabaseResult<List<KickRequestData>> dbResult = await kickRequestService.FetchAsync(
					data.LastFetchTime, data.LastPosition, UpdateFetchCount);

				if (!dbResult.IsSuccess || dbResult.Data == null || dbResult.Data.Count == 0)
				{
					return;
				}

				// Update polling position under lock for safe cross-thread visibility.
				KickRequestData latest = dbResult.Data[dbResult.Data.Count - 1];
				lock (this.kickLock)
				{
					data.LastFetchTime = latest.TimeCreated;
					data.LastPosition = latest.ID;
				}

				// Process last-login checks in batches to avoid saturating the DB connection pool.
				const int BatchSize = 10;
				var loginResults = new DatabaseResult<DateTime>[dbResult.Data.Count];
				for (int batchStart = 0; batchStart < dbResult.Data.Count; batchStart += BatchSize)
				{
					int batchEnd = Math.Min(batchStart + BatchSize, dbResult.Data.Count);
					var batchTasks = new Task<DatabaseResult<DateTime>>[batchEnd - batchStart];
					for (int i = batchStart; i < batchEnd; i++)
					{
						batchTasks[i - batchStart] = accountService.FetchLastLoginAsync(dbResult.Data[i].AccountName);
					}
					await Task.WhenAll(batchTasks);
					for (int i = batchStart; i < batchEnd; i++)
					{
						loginResults[i] = batchTasks[i - batchStart].Result;
					}
				}

				// Process results — kick any accounts that haven't reconnected since the request
				for (int i = 0; i < dbResult.Data.Count; i++)
				{
					KickRequestData kickRequest = dbResult.Data[i];
					DatabaseResult<DateTime> lastLoginResult = loginResults[i];

					// If the last successful login happened after the kick request,
					// the account reconnected and the kick is stale.
					if (lastLoginResult.IsSuccess && lastLoginResult.Data >= kickRequest.TimeCreated)
					{
						continue;
					}

					// Marshal Kick to main thread - FishNet is not thread-safe
					string accountName = kickRequest.AccountName;
					TryEnqueueMainThread(() =>
					{
						/* A combat-logout body has no connection, so the lookup below never finds
						 * it and the kick used to do nothing at all to a character that had logged
						 * out mid-fight — while the body went on standing in the world holding that
						 * character's session claim. A kick is the operator's remedy for a stuck
						 * character, so the one case where the character is hardest to load is
						 * exactly the case it silently skipped.
						 *
						 * Ending the linger is its ordinary conclusion: the body is saved and
						 * despawned and the claim handed back. Attempted before the connection
						 * lookup because the two are mutually exclusive — a reconnected player has
						 * already reclaimed their body — and because TryGet fails harmlessly on the
						 * login and world servers, which run this system with no character system. */
						if (Server.BehaviourRegistry != null &&
							Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, UnityEngine.SceneManagement.Scene> lingerSystem) &&
							lingerSystem.TryEndCombatLingerForAccount(accountName, "administrative kick"))
						{
							Log.Warning("KickRequestSystem",
								$"Removed the combat-logout body of kicked account '{accountName}'.");
						}

						if (Server.AccountManager.GetConnectionByAccountName(accountName, out NetworkConnection conn))
						{
							/* An operator kick is not a player quitting mid-fight.
							 *
							 * This system runs on the scene server too, where the ordinary disconnect
							 * path starts a combat-logout linger for any character that was in combat.
							 * Applied to a kick that is backwards: there is no escape to deny, because
							 * the player did not choose to leave — and the linger keeps the kicked
							 * character's body in the world, still targetable, while holding its session
							 * claim for as long as it lasts. Kicking is the standard remedy for a
							 * character that is stuck, so making that character harder to load for the
							 * next minute is the opposite of what the operator asked for.
							 *
							 * TryGet fails harmlessly on the login and world servers, which run this
							 * system but have no character system. */
							if (Server.BehaviourRegistry != null &&
								Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, UnityEngine.SceneManagement.Scene> characterSystem))
							{
								characterSystem.SuppressCombatLingerOnDisconnect(conn);
							}

							/* DisconnectWithNotice, not Kick.
							 *
							 * FishNet carries no kick reason to the client, so this landed the
							 * player back on the login screen with nothing to go on — and,
							 * because an operator kick is not a transport fault, the client's
							 * reconnect loop then spent its full ten attempts dialling back into
							 * a server that was going to refuse them again.
							 *
							 * Terminal: an operator kick does not resolve by retrying, so the
							 * client abandons the loop and shows the reason immediately. Kick
							 * would also have discarded the notice with the rest of the
							 * outgoing bundle — see DisconnectWithNotice. */
							DisconnectWithNotice(conn, DisconnectNoticeReason.AdministrativeKick, terminal: true);
						}
					});
				}
			}
			catch (Exception ex)
			{
				await Log.Error("KickRequestSystem", $"Error processing kick requests: {ex}");
			}
			finally
			{
				lock (this.kickLock)
				{
					data.IsProcessing = false;
				}
			}
		}

		/// <summary>
		/// Drains the main-thread queue via the base class generic helper.
		/// </summary>
		private void DrainMainThreadQueue(bool drainAll)
		{
			DrainMainThreadQueue<IKickRequestSystemMainThreadQueueData>(maxMainThreadActionsPerFrame, drainAll);
		}

		/// <summary>
		/// Thread-safe enqueue of an action to be executed on the main Unity thread
		/// via the base class generic helper.
		/// </summary>
		/// <param name="action">The action to execute on the main thread.</param>
		private bool TryEnqueueMainThread(Action action)
		{
			return TryEnqueueMainThread<IKickRequestSystemMainThreadQueueData>(action);
		}
	}
}