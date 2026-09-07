using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Transporting;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Logging;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Server.Implementation.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using UnityEngine;

namespace FishMMO.Server.Implementation.World.SceneServer.Interactable
{
	/// <summary>
	/// Waypoints: the fast-travel request from the map, and the server's answer to a waypoint
	/// being discovered.
	/// </summary>
	/// <remarks>
	/// <para>Lives on the interactable system because a waypoint is an interactable and the
	/// request shares this system's per-connection ingress guard. Discovery itself happens in
	/// the ECA action on the waypoint's interaction trigger; this file only hears about it
	/// (<see cref="IWaypointController.OnWaypointUnlocked"/>) and does the two things the shared
	/// assembly cannot: merge the page into the database and tell the owner.</para>
	/// <para>The request is validated end to end on the server: the scene the client's map was
	/// showing must be the scene the character is in, the waypoint must be live in that scene
	/// instance, and <see cref="WaypointTravel.TryTravel"/> applies the rules (can act, not in
	/// combat, discovered, conditions). Every refusal is reported to the owner so the map can
	/// re-enable its button and say why.</para>
	/// </remarks>
	public partial class InteractableSystem
	{
		/// <summary>Ingress-guard operation code for fast travel. Unique among this system's operations.</summary>
		private const byte WaypointTravelOperation = 20;

		[Header("Waypoints")]
		[Tooltip("Minimum milliseconds between fast-travel requests from one connection.")]
		[SerializeField] private int waypointTravelDebounceMilliseconds = 2000;

		/// <summary>Scratch list for collecting dirty pages on the main thread.</summary>
		private readonly List<WaypointPageSnapshot> waypointPageScratch = new List<WaypointPageSnapshot>();

		private void InitializeWaypoints()
		{
			waypointTravelDebounceMilliseconds = Mathf.Max(0, waypointTravelDebounceMilliseconds);

			Server.NetworkWrapper.RegisterBroadcast<WaypointTravelRequestBroadcast>(OnServerWaypointTravelRequestBroadcastReceived, true);

			IWaypointController.OnWaypointUnlocked += IWaypointController_OnWaypointUnlocked;
			IWaypointController.OnWaypointTravelled += IWaypointController_OnWaypointTravelled;
		}

		private void DeinitializeWaypoints()
		{
			Server.NetworkWrapper.UnregisterBroadcast<WaypointTravelRequestBroadcast>(OnServerWaypointTravelRequestBroadcastReceived);

			IWaypointController.OnWaypointUnlocked -= IWaypointController_OnWaypointUnlocked;
			IWaypointController.OnWaypointTravelled -= IWaypointController_OnWaypointTravelled;
		}

		/// <summary>
		/// Handles a fast-travel request from the owner's map.
		/// </summary>
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

			/* Its own guard key, at its own rate. Travel is not "the player clicked something":
			 * it is debounced far more slowly than an interaction, and it must not be locked out
			 * by the interaction the player just made with the waypoint object itself. */
			if (!TryBeginIngressGuard(conn.ClientId, WaypointTravelOperation, waypointTravelDebounceMilliseconds, out long guardKey))
			{
				SendWaypointTravelRefused(character, msg.SceneName, msg.WaypointIndex, WaypointTravelRefusalReason.TooSoon);
				return;
			}

			try
			{
				/* The scene the map was showing, not just an index. A request from a map that
				 * has not yet noticed a zone change would otherwise resolve index N in whatever
				 * scene the character is in now. CurrentSceneName is instance-aware. */
				string currentScene = character.CurrentSceneName();
				if (string.IsNullOrEmpty(msg.SceneName) ||
					!string.Equals(msg.SceneName, currentScene, StringComparison.Ordinal))
				{
					Log.Debug("InteractableSystem", $"Waypoint travel refused for {character.ID}: requested scene '{msg.SceneName}' but character is in '{currentScene}'.");
					SendWaypointTravelRefused(character, msg.SceneName, msg.WaypointIndex, WaypointTravelRefusalReason.NotInScene);
					return;
				}

				if (character.GameObject == null ||
					!WaypointRegistry.TryGet(character.GameObject.scene.handle, msg.WaypointIndex, out IWaypoint waypoint))
				{
					Log.Debug("InteractableSystem", $"Waypoint travel refused for {character.ID}: no waypoint {msg.WaypointIndex} in scene '{currentScene}'.");
					SendWaypointTravelRefused(character, msg.SceneName, msg.WaypointIndex, WaypointTravelRefusalReason.UnknownWaypoint);
					return;
				}

				// On success TryTravel raises OnWaypointTravelled, which tells the owner below.
				if (!WaypointTravel.TryTravel(character, waypoint, WaypointTravelOptions.Default, out WaypointTravelRefusalReason reason))
				{
					SendWaypointTravelRefused(character, msg.SceneName, msg.WaypointIndex, reason);
				}
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		private void SendWaypointTravelRefused(IPlayerCharacter character, string sceneName, int waypointIndex, WaypointTravelRefusalReason reason)
		{
			if (character?.Owner == null)
			{
				return;
			}

			Server.NetworkWrapper.Broadcast(character.Owner, new WaypointTravelRefusedBroadcast()
			{
				SceneName = sceneName,
				WaypointIndex = waypointIndex,
				Reason = reason,
			}, true, Channel.Reliable);
		}

		/// <summary>
		/// A waypoint was discovered: tell the owner, and merge the page now rather than waiting
		/// for the periodic save. The page stays dirty until the merge is confirmed, so the
		/// periodic save retries it if this write is lost.
		/// </summary>
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
				Log.Warning("InteractableSystem", $"ICharacterWaypointService unavailable; waypoint {sceneName}:{waypointIndex} for {player.ID} will be written by the periodic save.");
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
				if (await WaypointPersistence.MergeAsync(service, pages, "InteractableSystem"))
				{
					TryEnqueueMainThread(() => WaypointPersistence.MarkPersisted(pages, ResolveResidentCharacter));
				}
			}, characterID);
		}

		/// <summary>
		/// The character arrived: confirm to the owner so the map can close and the arrival
		/// sound can play. The position itself comes through the reconcile.
		/// </summary>
		private void IWaypointController_OnWaypointTravelled(ICharacter character, string sceneName, int waypointIndex)
		{
			if (character is not IPlayerCharacter player || player.Owner == null)
			{
				return;
			}

			Server.NetworkWrapper.Broadcast(player.Owner, new WaypointTravelledBroadcast()
			{
				SceneName = sceneName,
				WaypointIndex = waypointIndex,
			}, true, Channel.Reliable);
		}

		/// <summary>
		/// Finds a connected character by ID for confirming a write. Main thread only.
		/// </summary>
		/// <remarks>
		/// A character that has since logged out is simply not found and its pages stay marked;
		/// nothing reads that mark again, and the next login restores the row as persisted.
		/// </remarks>
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
}
