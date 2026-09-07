using System;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA action for a waypoint's interaction: discovers the waypoint for the interacting
	/// character. Server only.
	/// </summary>
	/// <remarks>
	/// <para>Abortable, and meant to be authored with <see cref="BaseAction.StopChainOnFailure"/>
	/// set: it reports failure when the waypoint was already discovered, so an achievement
	/// increment placed after it in the trigger counts each waypoint once per character rather
	/// than once per click.</para>
	/// <para>When the waypoint is already discovered it instead asks the owner's client to open
	/// the world map on it (<see cref="OpenMapWhenAlreadyUnlocked"/>), so the object stays useful
	/// after discovery — walk up to a waypoint, click it, pick where to go.</para>
	/// <para>Persistence and the owner notification are not done here. They are the server's
	/// answer to <see cref="IWaypointController.OnWaypointUnlocked"/>, which this raises through
	/// the controller, so a waypoint granted by a quest reward is persisted and announced by the
	/// same code as one found on foot.</para>
	/// </remarks>
	[Serializable]
	public class UnlockWaypointAction : BaseAction, IAbortableAction
	{
		[Tooltip("When the waypoint is already discovered, ask the owner's client to open the world map on it.")]
		public bool OpenMapWhenAlreadyUnlocked = true;

		/// <inheritdoc />
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			TryExecute(initiator, eventData);
		}

		/// <inheritdoc />
		public bool TryExecute(ICharacter initiator, EventData eventData)
		{
			if (!IsServer(initiator))
			{
				return false;
			}

			if (initiator is not IPlayerCharacter player)
			{
				return false;
			}

			if (eventData == null ||
				!eventData.TryGet(out PlayerInteractionEventData data) ||
				data.Interactable is not IWaypoint waypoint)
			{
				return false;
			}

			if (!player.TryGet(out IWaypointController controller))
			{
				return false;
			}

			if (controller.Unlock(waypoint.SceneName, waypoint.WaypointIndex))
			{
				return true;
			}

			if (OpenMapWhenAlreadyUnlocked)
			{
				SendToOwner(player, new WaypointOpenMapBroadcast()
				{
					SceneName = waypoint.SceneName,
					WaypointIndex = waypoint.WaypointIndex,
				});
			}
			return false;
		}
	}
}
