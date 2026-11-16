using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Transporting;
using UnityEngine;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Logging;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// The scene server's side of fast travel (Travel module): the travel request, the owner's notices of an unlock and of
	/// travel, and the write of a newly discovered waypoint.
	/// </summary>
	/// <remarks>
	/// Was the waypoint part of <c>InteractableSystem</c>. Adopted by the scene server through its
	/// <c>SceneServer_Systems</c> label, so nothing outside the module names it. Its settings (the travel rule, the debounce)
	/// came from the interactable system's asset unchanged.
	/// </remarks>
	[CreateAssetMenu(fileName = "WaypointTravelSystem", menuName = "FishMMO/Server/SceneServer/Waypoint Travel System", order = 1)]
	[RequiresDataContainer(typeof(WaypointTravelSystemMainThreadQueueData))]
	[RequiresDataContainer(typeof(WaypointTravelSystemRuntimeData))]
	[RequiresDataContainer(typeof(AsyncWorkerData))]
	public class WaypointTravelSystem : ServerBehaviour
	{
		[Header("Travel")]
		[Tooltip("Minimum milliseconds between fast-travel requests from one connection.")]
		[SerializeField] private int waypointTravelDebounceMilliseconds = 2000;

		[Tooltip("Game rule (issue #253): a player may only fast travel while standing near a waypoint they have discovered in the same scene. " +
			"Turn off to allow fast travel from anywhere. Item and quest teleports (TeleportToWaypointAction) are never bound by it.")]
		[SerializeField] private bool requireNearbyWaypointForTravel = WaypointTravelPolicy.DefaultRequireNearbyWaypoint;

		[Tooltip("How near, in metres, a player must stand to a discovered waypoint to fast travel. A waypoint's interaction range is 3.5. Ignored when the rule above is off.")]
		[SerializeField] private float waypointTravelOriginRange = WaypointTravelPolicy.DefaultNearbyRange;

		[Tooltip("Run when a player uses any waypoint (discovers it and counts it): attached to every Waypoint at start (InteractableTriggers), so the waypoint prefab names nothing of this module.")]
		[SerializeField] private Trigger waypointInteractTrigger;

		[Header("Main Thread Dispatch")]
		[Tooltip("Max queued actions drained per frame")]
		[SerializeField] private int maxMainThreadActionsPerFrame = 100;

		[Header("Ingress Protection")]
		[Tooltip("Seconds between bounded ingress guard cleanup sweeps")]
		[SerializeField] private float ingressSweepIntervalSeconds = 5.0f;

		[Tooltip("Seconds before stale ingress guard entries are removed")]
		[SerializeField] private float ingressEntryTtlSeconds = 30.0f;

		[Tooltip("Maximum stale ingress guard entries removed per sweep")]
		[SerializeField] private int ingressSweepMaxRemovals = 128;

		private const byte WaypointTravelOperation = 20;

		private readonly List<WaypointPageSnapshot> waypointPageScratch = new List<WaypointPageSnapshot>();

		/// <inheritdoc/>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			if (Server == null)
			{
				Log.Error("WaypointTravelSystem", "InitializeOnce: Server is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}
			if (!Server.DataContainerRegistry.TryGet<IWaypointTravelSystemMainThreadQueueData>(out _) ||
				!Server.DataContainerRegistry.TryGet<IWaypointTravelSystemRuntimeData>(out _))
			{
				Log.Error("WaypointTravelSystem", "InitializeOnce: data containers not found");
				return ServerComponentInitializationStatus.FailedToGetDataContainer;
			}

			waypointTravelDebounceMilliseconds = Mathf.Max(0, waypointTravelDebounceMilliseconds);
			waypointTravelOriginRange = WaypointTravelPolicy.ClampRange(waypointTravelOriginRange);
			maxMainThreadActionsPerFrame = Mathf.Max(1, maxMainThreadActionsPerFrame);

			/* Installed before any character can spawn: the owner's spawn payload carries it, so the map knows the rule
			 * before the player first opens it. */
			WaypointTravelPolicy.Server = new WaypointTravelPolicy(requireNearbyWaypointForTravel, waypointTravelOriginRange);
			Log.Debug("WaypointTravelSystem", $"Waypoint travel: RequireNearbyWaypoint={requireNearbyWaypointForTravel}, OriginRange={waypointTravelOriginRange}m");

			if (waypointInteractTrigger != null)
			{
				InteractableTriggers.Attach(typeof(Waypoint), waypointInteractTrigger);
			}
			else
			{
				Log.Warning("WaypointTravelSystem", "No waypoint interact trigger is assigned: using a waypoint will not discover it.");
			}

			Server.NetworkWrapper.RegisterBroadcast<WaypointTravelRequestBroadcast>(OnServerWaypointTravelRequestBroadcastReceived, true);
			IWaypointController.OnWaypointUnlocked += IWaypointController_OnWaypointUnlocked;
			IWaypointController.OnWaypointTravelled += IWaypointController_OnWaypointTravelled;
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <inheritdoc/>
		public override void OnDeinitialize()
		{
			if (Server == null)
			{
				return;
			}
			DrainMainThreadQueue<IWaypointTravelSystemMainThreadQueueData>(maxMainThreadActionsPerFrame, drainAll: true);
			Server.NetworkWrapper.UnregisterBroadcast<WaypointTravelRequestBroadcast>(OnServerWaypointTravelRequestBroadcastReceived);
			IWaypointController.OnWaypointUnlocked -= IWaypointController_OnWaypointUnlocked;
			IWaypointController.OnWaypointTravelled -= IWaypointController_OnWaypointTravelled;
			WaypointTravelPolicy.Server = WaypointTravelPolicy.Default;
			InteractableTriggers.Detach(typeof(Waypoint), waypointInteractTrigger);
			if (Server.DataContainerRegistry.TryGet<IWaypointTravelSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.IngressGuard?.Clear();
			}
		}

		/// <summary>Drains confirmations and sweeps the ingress guard.</summary>
		protected override void OnUpdate(float deltaTime)
		{
			DrainMainThreadQueue<IWaypointTravelSystemMainThreadQueueData>(maxMainThreadActionsPerFrame, drainAll: false);
			if (Server.DataContainerRegistry.TryGet<IWaypointTravelSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.IngressGuard.Sweep(ingressSweepIntervalSeconds, ingressEntryTtlSeconds, ingressSweepMaxRemovals);
			}
		}

		private bool TryEnqueueMainThread(Action action) => TryEnqueueMainThread<IWaypointTravelSystemMainThreadQueueData>(action);

		private void OnServerWaypointTravelRequestBroadcastReceived(NetworkConnection conn, WaypointTravelRequestBroadcast msg, Channel channel)
		{
			if (conn == null || conn.FirstObject == null)
			{
				return;
			}

			IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (character == null)
			{
				return;
			}

			/* Its own guard, at its own rate. Travel is not "the player clicked something": it is debounced far more slowly
			 * than an interaction, and it must not be locked out by the interaction the player just made with the
			 * waypoint object itself. */
			if (!Server.DataContainerRegistry.TryGet<IWaypointTravelSystemRuntimeData>(out var runtimeData) ||
				!runtimeData.IngressGuard.TryBegin(conn.ClientId, WaypointTravelOperation, waypointTravelDebounceMilliseconds, out long guardKey))
			{
				SendWaypointTravelRefused(character, msg.WaypointIndex, WaypointTravelRefusalReason.TooSoon);
				return;
			}

			try
			{
				/* The scene the map was showing, not just an index. A request from a map that has not yet noticed a zone
				 * change would otherwise resolve index N in whatever scene the character is in now. CurrentSceneName is
				 * instance-aware. */
				string currentScene = character.CurrentSceneName();
				if (string.IsNullOrEmpty(msg.SceneName) ||
					!string.Equals(msg.SceneName, currentScene, StringComparison.Ordinal))
				{
					Log.Debug("WaypointTravelSystem", $"Waypoint travel refused for {character.ID}: requested scene '{msg.SceneName}' but character is in '{currentScene}'.");
					SendWaypointTravelRefused(character, msg.WaypointIndex, WaypointTravelRefusalReason.NotInScene);
					return;
				}

				if (character.GameObject == null ||
					!WaypointRegistry.TryGet(character.GameObject.scene.handle, msg.WaypointIndex, out IWaypoint waypoint))
				{
					Log.Debug("WaypointTravelSystem", $"Waypoint travel refused for {character.ID}: no waypoint {msg.WaypointIndex} in scene '{currentScene}'.");
					SendWaypointTravelRefused(character, msg.WaypointIndex, WaypointTravelRefusalReason.UnknownWaypoint);
					return;
				}

				// On success TryTravel raises OnWaypointTravelled, which tells the owner below.
				WaypointTravelOptions options = WaypointTravelOptions.ForPlayerRequest(WaypointTravelPolicy.Server);
				if (!WaypointTravel.TryTravel(character, waypoint, options, out WaypointTravelRefusalReason reason))
				{
					SendWaypointTravelRefused(character, msg.WaypointIndex, reason);
				}
			}
			finally
			{
				runtimeData.IngressGuard.End(guardKey);
			}
		}

		private void SendWaypointTravelRefused(IPlayerCharacter character, int waypointIndex, WaypointTravelRefusalReason reason)
		{
			if (character?.Owner == null)
			{
				return;
			}

			Server.NetworkWrapper.Broadcast(character.Owner, new WaypointTravelRefusedBroadcast()
			{
				WaypointIndex = waypointIndex,
				Reason = reason,
			}, true, Channel.Reliable);
		}

		private void IWaypointController_OnWaypointUnlocked(ICharacter character, string sceneName, int waypointIndex)
		{
			if (character is not IPlayerCharacter player)
			{
				return;
			}

			if (player.Owner != null)
			{
				Server.NetworkWrapper.Broadcast(player.Owner, new WaypointUnlockedBroadcast()
				{
					SceneName = sceneName,
					WaypointIndex = waypointIndex,
				}, true, Channel.Reliable);
			}

			if (!TryGetDbService(out ICharacterWaypointService service))
			{
				Log.Warning("WaypointTravelSystem", $"ICharacterWaypointService unavailable; waypoint {sceneName}:{waypointIndex} for {player.ID} will be written by the periodic save.");
				return;
			}

			var pages = new List<CharacterWaypointData>(1);
			WaypointPersistence.AppendDirtyPages(player, pages, waypointPageScratch);
			if (pages.Count == 0)
			{
				return;
			}

			long characterID = player.ID;
			EnqueuePersistence(async () =>
			{
				if (await WaypointPersistence.MergeAsync(service, pages, "WaypointTravelSystem"))
				{
					TryEnqueueMainThread(() => WaypointPersistence.MarkPersisted(pages, ResolveResidentCharacter));
				}
			}, characterID);
		}

		private void IWaypointController_OnWaypointTravelled(ICharacter character, string sceneName, int waypointIndex)
		{
			if (character is not IPlayerCharacter player || player.Owner == null)
			{
				return;
			}

			Server.NetworkWrapper.Broadcast(player.Owner, new WaypointTravelledBroadcast()
			{
				WaypointIndex = waypointIndex,
			}, true, Channel.Reliable);
		}

		private ICharacter ResolveResidentCharacter(long characterID)
		{
			if (Server?.DataContainerRegistry != null &&
				Server.DataContainerRegistry.TryGet(out ICharacterMappingData<NetworkConnection> data) &&
				data.CharactersByID.TryGetValue(characterID, out IPlayerCharacter character))
			{
				return character;
			}
			return null;
		}
	}

	/// <summary>The waypoint travel system's main-thread queue.</summary>
	public interface IWaypointTravelSystemMainThreadQueueData : IMainThreadQueueData
	{
	}

	/// <summary>The waypoint travel system's main-thread queue.</summary>
	public class WaypointTravelSystemMainThreadQueueData : SystemMainThreadQueueData, IWaypointTravelSystemMainThreadQueueData
	{
	}

	/// <summary>The waypoint travel system's ingress guard.</summary>
	public interface IWaypointTravelSystemRuntimeData : IRuntimeDataContainer
	{
		IngressGuard IngressGuard { get; }
	}

	/// <summary>The waypoint travel system's ingress guard.</summary>
	public class WaypointTravelSystemRuntimeData : RuntimeDataContainer, IWaypointTravelSystemRuntimeData
	{
		public IngressGuard IngressGuard { get; private set; }

		public override ServerComponentInitializationStatus InitializeOnce()
		{
			IngressGuard = new IngressGuard();
			return ServerComponentInitializationStatus.Initialized;
		}

		public override void Clear()
		{
			IngressGuard?.Clear();
		}

		protected override void OnDeinitialize()
		{
			Clear();
		}
	}
}
