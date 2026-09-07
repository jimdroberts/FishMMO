using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Options a caller passes to <see cref="WaypointTravel.TryTravel"/>. The server's map request
	/// uses the defaults; a designer's <see cref="TeleportToWaypointAction"/> may relax them.
	/// </summary>
	public readonly struct WaypointTravelOptions
	{
		/// <summary>The rules a player's own map request is held to.</summary>
		public static readonly WaypointTravelOptions Default = new WaypointTravelOptions(
			requireUnlocked: true, evaluateConditions: true, allowInCombat: false);

		/// <summary>Refuse unless the traveller has discovered the waypoint.</summary>
		public readonly bool RequireUnlocked;

		/// <summary>Refuse unless the waypoint's authored travel conditions pass.</summary>
		public readonly bool EvaluateConditions;

		/// <summary>Permit travel while flagged in combat. Off for anything a player initiates.</summary>
		public readonly bool AllowInCombat;

		public WaypointTravelOptions(bool requireUnlocked, bool evaluateConditions, bool allowInCombat)
		{
			RequireUnlocked = requireUnlocked;
			EvaluateConditions = evaluateConditions;
			AllowInCombat = allowInCombat;
		}
	}

	/// <summary>
	/// Moves a player character to a waypoint, after every check the move depends on.
	/// </summary>
	/// <remarks>
	/// <para>One routine for every caller — the map request handler and the ECA teleport action —
	/// so the rules cannot drift between them. The rules themselves are in <see cref="Decide"/>,
	/// a pure function over the answers, so the truth table is testable without a scene.</para>
	/// <para>Server only. The position write is the same primitive respawn and cross-scene
	/// arrival use (<c>KinematicCharacterMotor.SetPositionAndRotationAndVelocity</c>); the owner's
	/// client learns of it through the next reconcile, exactly as it does for a respawn.</para>
	/// </remarks>
	public static class WaypointTravel
	{
		/// <summary>
		/// The rules, as a pure function of the facts.
		/// </summary>
		/// <param name="canAct">Whether the traveller may act at all (alive, loaded, not mid-transfer, not incapacitated).</param>
		/// <param name="inCombat">Whether the traveller is flagged in combat.</param>
		/// <param name="sameScene">Whether the waypoint stands in the traveller's own scene instance.</param>
		/// <param name="unlocked">Whether the traveller has discovered the waypoint.</param>
		/// <param name="conditionsMet">Whether the waypoint's travel conditions pass for the traveller.</param>
		/// <param name="options">Which rules apply.</param>
		/// <returns><see cref="WaypointTravelRefusalReason.None"/> when travel is permitted, else why not.</returns>
		public static WaypointTravelRefusalReason Decide(bool canAct, bool inCombat, bool sameScene,
			bool unlocked, bool conditionsMet, in WaypointTravelOptions options)
		{
			if (!canAct)
			{
				return WaypointTravelRefusalReason.CannotAct;
			}
			if (inCombat && !options.AllowInCombat)
			{
				return WaypointTravelRefusalReason.InCombat;
			}
			if (!sameScene)
			{
				return WaypointTravelRefusalReason.NotInScene;
			}
			if (options.RequireUnlocked && !unlocked)
			{
				return WaypointTravelRefusalReason.Locked;
			}
			if (options.EvaluateConditions && !conditionsMet)
			{
				return WaypointTravelRefusalReason.ConditionsNotMet;
			}
			return WaypointTravelRefusalReason.None;
		}

		/// <summary>
		/// Checks and, if permitted, performs the move.
		/// </summary>
		/// <param name="player">The traveller.</param>
		/// <param name="waypoint">The destination, resolved by the caller from the live registry.</param>
		/// <param name="options">Which rules apply.</param>
		/// <param name="reason">Why travel was refused, or <see cref="WaypointTravelRefusalReason.None"/>.</param>
		/// <returns>True when the character was moved.</returns>
		public static bool TryTravel(IPlayerCharacter player, IWaypoint waypoint, in WaypointTravelOptions options,
			out WaypointTravelRefusalReason reason)
		{
			if (player == null || waypoint == null || waypoint.GameObject == null)
			{
				reason = WaypointTravelRefusalReason.UnknownWaypoint;
				return false;
			}

			/* Authority, decided at runtime rather than by a build define, for the reason every
			 * ECA action gives: UNITY_SERVER is absent in the editor the scene server is
			 * developed in. A client that somehow reached here must not move its own character —
			 * the server would reconcile it straight back, and in the meantime the arrival
			 * triggers would have fired on a peer that has no business firing them. */
			if (player.NetworkObject == null || !player.NetworkObject.IsServerInitialized)
			{
				reason = WaypointTravelRefusalReason.NotAuthoritative;
				return false;
			}

			bool unlocked = false;
			if (options.RequireUnlocked &&
				player.TryGet(out IWaypointController controller))
			{
				unlocked = controller.IsUnlocked(waypoint.SceneName, waypoint.WaypointIndex);
			}

			WaypointEventData eventData = new WaypointEventData(player, waypoint);

			bool conditionsMet = true;
			if (options.EvaluateConditions && waypoint.TravelConditions != null && waypoint.TravelConditions.Count > 0)
			{
				conditionsMet = TriggerExecution.AreConditionsMet(waypoint.TravelConditions, eventData);
			}

			bool sameScene = player.GameObject != null &&
				player.GameObject.scene.handle == waypoint.GameObject.scene.handle;

			reason = Decide(
				canAct: CharacterStateValidation.CanAct(player),
				inCombat: player.IsFlagged(CharacterFlags.IsInCombat),
				sameScene: sameScene,
				unlocked: unlocked,
				conditionsMet: conditionsMet,
				options: options);

			if (reason != WaypointTravelRefusalReason.None)
			{
				Log.Debug("WaypointTravel", $"Refused {player.ID} -> '{waypoint.Name}' [{waypoint.SceneName}:{waypoint.WaypointIndex}]: {reason}");
				return false;
			}

			Transform arrival = waypoint.ArrivalPoint != null ? waypoint.ArrivalPoint : waypoint.Transform;
			if (player.Motor == null || arrival == null)
			{
				reason = WaypointTravelRefusalReason.UnknownWaypoint;
				return false;
			}

			player.Motor.SetPositionAndRotationAndVelocity(arrival.position, arrival.rotation, Vector3.zero);

			player.Invoke(waypoint.OnTravelTriggers, eventData);
			IWaypointController.OnWaypointTravelled?.Invoke(player, waypoint.SceneName, waypoint.WaypointIndex);
			return true;
		}
	}
}
