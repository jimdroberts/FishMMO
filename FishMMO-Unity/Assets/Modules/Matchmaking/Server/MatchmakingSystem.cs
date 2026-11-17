using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FishNet.Connection;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Logging;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Implementation.World.SceneServer.Interactable
{
	/// <summary>
	/// Matchmaking on a scene server: the group finder, the dungeon finder and dungeon instances (<c>/closedungeon</c>),
	/// arenas and arena matches.
	/// </summary>
	/// <remarks>
	/// The Matchmaking module (Phase 3, 2026-11-17). These were partials of <c>InteractableSystem</c>; they are their own
	/// system now, with their own request guard and main-thread queue, so a game without matchmaking leaves the module
	/// out. The entrances, boards and arena objectives placed in the world stay in the core; their ECA actions raise
	/// events this system answers.
	/// </remarks>
	[CreateAssetMenu(fileName = "MatchmakingSystem", menuName = "FishMMO/Server/SceneServer/Matchmaking System", order = 1)]
	[RequiresDataContainer(typeof(MatchmakingSystemMainThreadQueueData))]
	[RequiresDataContainer(typeof(MatchmakingSystemRuntimeData))]
	[RequiresDataContainer(typeof(AsyncWorkerData))]
	public partial class MatchmakingSystem : ServerBehaviour
	{
		[Header("Main Thread Dispatch")]
		[Tooltip("Most matchmaking replies drained from the main-thread queue per frame.")]
		[SerializeField] private int maxMainThreadActionsPerFrame = 100;

		[Header("Request Protection")]
		[Tooltip("Minimum milliseconds between two matchmaking requests from one connection (the shared operation).")]
		[SerializeField] private int interactionDebounceMilliseconds = 1000;
		[Tooltip("Seconds between sweeps of stale request-guard entries.")]
		[SerializeField] private float debounceSweepIntervalSeconds = 5.0f;
		[Tooltip("Seconds before a request-guard entry is stale.")]
		[SerializeField] private float debounceEntryTtlSeconds = 30.0f;
		[Tooltip("Most stale request-guard entries removed per sweep.")]
		[SerializeField] private int debounceSweepMaxRemovals = 128;

		/// <summary>The world scenes, for arena and dungeon instance details (the same asset the interactable system uses).</summary>
		[Header("World")]
		[SerializeField] private WorldSceneDetailsCache worldSceneDetailsCache;

		/// <summary>The character system whose disconnects drop group finder rows.</summary>
		private ICharacterSystem<NetworkConnection, Scene> matchmakingCharacterSystem;

		/// <inheritdoc />
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			if (Server == null)
			{
				Log.Error("MatchmakingSystem", "InitializeOnce: Server is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}
			if (Server.Database?.ServiceRegistry == null)
			{
				Log.Error("MatchmakingSystem", "InitializeOnce: Database ServiceRegistry is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}
			if (!Server.DataContainerRegistry.TryGet<IMatchmakingSystemMainThreadQueueData>(out _) ||
				!Server.DataContainerRegistry.TryGet<IMatchmakingSystemRuntimeData>(out _))
			{
				Log.Error("MatchmakingSystem", "InitializeOnce: its data containers are missing");
				return ServerComponentInitializationStatus.FailedToGetDataContainer;
			}

			Server.NetworkWrapper.RegisterBroadcast<DungeonFinderListBroadcast>(OnServerDungeonFinderListBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<DungeonFinderCreateBroadcast>(OnServerDungeonFinderCreateBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<DungeonFinderJoinBroadcast>(OnServerDungeonFinderJoinBroadcastReceived, true);

			ChatHelper.AddCommands(new Dictionary<string, ChatCommand>()
			{
				{ "/closedungeon", OnCloseDungeonCommand },
				{ "/closeinstance", OnCloseDungeonCommand },
			});
			ChatHelper.SetCommandHelp("/closedungeon", new ChatCommandHelp()
			{
				Category = "Instance",
				Summary = "Party leader: closes your party's dungeon so you can start another.",
				Aliases = new[] { "/closeinstance" },
			});

			if (Server.BehaviourRegistry.TryGet(out matchmakingCharacterSystem) && matchmakingCharacterSystem != null)
			{
				matchmakingCharacterSystem.OnDisconnect += CharacterSystem_OnGroupFinderCharacterDisconnected;
			}
			else
			{
				Log.Warning("MatchmakingSystem", "InitializeOnce: ICharacterSystem not found; group finder rows will outlive their characters until the stale sweep.");
			}

			// Group finder, arenas and arena matches: their requests, pumps and tunables. See MatchmakingSystem.GroupFinder.cs.
			InitializeGroupFinder();

			maxMainThreadActionsPerFrame = Mathf.Max(1, maxMainThreadActionsPerFrame);
			interactionDebounceMilliseconds = Mathf.Max(0, interactionDebounceMilliseconds);
			debounceSweepIntervalSeconds = Mathf.Max(0.25f, debounceSweepIntervalSeconds);
			debounceEntryTtlSeconds = Mathf.Max(1.0f, debounceEntryTtlSeconds);
			debounceSweepMaxRemovals = Mathf.Max(1, debounceSweepMaxRemovals);

			Log.Debug("MatchmakingSystem", "Initialized");
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <inheritdoc />
		public override void OnDeinitialize()
		{
			if (Server == null)
			{
				return;
			}
			DrainMainThreadQueue(drainAll: true);
			if (Server.DataContainerRegistry.TryGet<IMatchmakingSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.IngressGuard?.Clear();
			}

			Server.NetworkWrapper.UnregisterBroadcast<DungeonFinderListBroadcast>(OnServerDungeonFinderListBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<DungeonFinderCreateBroadcast>(OnServerDungeonFinderCreateBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<DungeonFinderJoinBroadcast>(OnServerDungeonFinderJoinBroadcastReceived);

			// Static registry: a command left behind outlives this ScriptableObject.
			ChatHelper.RemoveCommands(new[] { "/closedungeon", "/closeinstance" });

			if (matchmakingCharacterSystem != null)
			{
				matchmakingCharacterSystem.OnDisconnect -= CharacterSystem_OnGroupFinderCharacterDisconnected;
				matchmakingCharacterSystem = null;
			}

			DeinitializeGroupFinder();
		}

		/// <inheritdoc />
		protected override void OnUpdate(float deltaTime)
		{
			DrainMainThreadQueue(drainAll: false);
			if (Server.DataContainerRegistry.TryGet<IMatchmakingSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.IngressGuard.Sweep(debounceSweepIntervalSeconds, debounceEntryTtlSeconds, debounceSweepMaxRemovals);
			}
		}

		private void DrainMainThreadQueue(bool drainAll)
		{
			DrainMainThreadQueue<IMatchmakingSystemMainThreadQueueData>(maxMainThreadActionsPerFrame, drainAll);
		}

		/// <summary>Runs <paramref name="action"/> on the main thread; false when the queue refused it.</summary>
		private bool TryEnqueueMainThread(Action action)
		{
			return TryEnqueueMainThread<IMatchmakingSystemMainThreadQueueData>(action);
		}

		/// <summary>Acquires the request guard for the shared operation (0) at the system's debounce.</summary>
		private bool TryBeginIngressGuard(int connectionId, out long guardKey)
		{
			return TryBeginIngressGuard(connectionId, 0, interactionDebounceMilliseconds, out guardKey);
		}

		/// <summary>
		/// Acquires the request guard for one named operation at its own debounce: browsing the dungeon list and entering
		/// a dungeon are debounced at rates two orders of magnitude apart, and sharing a key would let the cheap one lock
		/// out the expensive one.
		/// </summary>
		private bool TryBeginIngressGuard(int connectionId, byte operation, int debounceMilliseconds, out long guardKey)
		{
			if (!Server.DataContainerRegistry.TryGet<IMatchmakingSystemRuntimeData>(out var runtimeData))
			{
				guardKey = 0;
				return false;
			}
			return runtimeData.IngressGuard.TryBegin(connectionId, operation, debounceMilliseconds, out guardKey);
		}

		/// <summary>Releases the request guard.</summary>
		private void EndIngressGuard(long guardKey)
		{
			if (Server.DataContainerRegistry.TryGet<IMatchmakingSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.IngressGuard.End(guardKey);
			}
		}

		/// <summary>
		/// The scene object with <paramref name="sceneObjectID"/>, when it exists, is live (not pooled) and stands in the
		/// character's scene (the same check the interactable system makes).
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private bool ValidateSceneObject(long sceneObjectID, SceneHandle characterSceneHandle, out ISceneObject sceneObject)
		{
			if (!SceneObject.Objects.TryGetValue(sceneObjectID, out sceneObject))
			{
				Log.Debug("MatchmakingSystem", $"Missing SceneObject ID:{sceneObjectID}");
				return false;
			}
			GameObject gameObject = sceneObject.GameObject;
			if (gameObject == null || !gameObject.activeInHierarchy)
			{
				Log.Debug("MatchmakingSystem", $"SceneObject ID:{sceneObjectID} is despawned.");
				sceneObject = null;
				return false;
			}
			if (gameObject.scene.handle != characterSceneHandle)
			{
				Log.Debug("MatchmakingSystem", "Object scene mismatch.");
				sceneObject = null;
				return false;
			}
			return true;
		}
	}
}
