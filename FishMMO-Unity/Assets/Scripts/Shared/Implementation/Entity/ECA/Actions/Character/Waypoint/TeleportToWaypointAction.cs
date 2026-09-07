using System;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA action that moves a player character to a specific waypoint — a town-portal item, a
	/// quest hand-in that returns the player to camp, a dungeon exit. Server only.
	/// </summary>
	/// <remarks>
	/// <para>Goes through <see cref="WaypointTravel.TryTravel"/>, the same routine the map's
	/// fast-travel request uses, so the rules are the same; the options below let a designer
	/// relax the two that are policy rather than physics (discovery, combat). The
	/// waypoint's own travel conditions are a third: an item that bypasses a level gate is a
	/// legitimate thing to author.</para>
	/// <para>Same-scene only for now. The waypoint is resolved in the character's own scene
	/// instance; a <see cref="SceneName"/> naming another scene is refused with a warning until
	/// the world-map transfer lands.</para>
	/// </remarks>
	[Serializable]
	public class TeleportToWaypointAction : BaseAction, IAbortableAction
	{
		[Tooltip("The scene the waypoint stands in. Leave empty for the character's current scene.")]
		public string SceneName;

		[Tooltip("The waypoint's authored index within that scene.")]
		public int WaypointIndex;

		[Tooltip("Refuse unless the character has discovered the waypoint.")]
		public bool RequireUnlocked = true;

		[Tooltip("Refuse unless the waypoint's own travel conditions pass.")]
		public bool EvaluateTravelConditions = true;

		[Tooltip("Permit the move while the character is in combat. Off unless the item or event is meant as an escape.")]
		public bool AllowInCombat;

		/// <inheritdoc />
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			TryExecute(initiator, eventData);
		}

		/// <inheritdoc />
		public bool TryExecute(ICharacter initiator, EventData eventData)
		{
			if (!EcaAuthority.IsServer(initiator, eventData))
			{
				return false;
			}

			if (!TryResolveTargetOrInitiator(initiator, eventData, out ICharacter target) ||
				target is not IPlayerCharacter player ||
				player.GameObject == null)
			{
				return false;
			}

			if (!string.IsNullOrEmpty(SceneName) &&
				!string.Equals(SceneName, player.GameObject.scene.name, StringComparison.Ordinal))
			{
				Log.Warning("TeleportToWaypointAction",
					$"Waypoint {SceneName}:{WaypointIndex} is not in {player.ID}'s current scene '{player.GameObject.scene.name}'. Cross-scene waypoint travel is not supported yet.");
				return false;
			}

			if (!WaypointRegistry.TryGet(player.GameObject.scene.handle, WaypointIndex, out IWaypoint waypoint))
			{
				Log.Warning("TeleportToWaypointAction",
					$"No waypoint with index {WaypointIndex} is registered in scene '{player.GameObject.scene.name}'.");
				return false;
			}

			WaypointTravelOptions options = new WaypointTravelOptions(RequireUnlocked, EvaluateTravelConditions, AllowInCombat);
			return WaypointTravel.TryTravel(player, waypoint, options, out _);
		}

		/// <inheritdoc />
		public override string GetTooltipContribution()
		{
			return "Teleports you to a waypoint";
		}
	}
}
