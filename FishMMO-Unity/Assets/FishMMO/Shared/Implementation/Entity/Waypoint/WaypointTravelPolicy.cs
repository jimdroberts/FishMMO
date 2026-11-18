using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// The game-wide rule for where a player may fast travel <i>from</i>: when
	/// <see cref="RequireNearbyWaypoint"/> is on, only from within <see cref="NearbyRange"/> of a
	/// waypoint they have discovered in the same scene instance (issue #253). Off, the map's
	/// travel button works from anywhere.
	/// </summary>
	/// <remarks>
	/// <para><b>Where it is set.</b> The server's <c>InteractableSystem</c> asset owns the values and
	/// installs them in <see cref="Server"/> when it initialises; the travel request reads them from
	/// there. The owner client is told the server's values in the <see cref="WaypointController"/>
	/// spawn payload, so the map can grey out its button and say why rather than send a request
	/// that will be refused. The client's copy is a hint only — the server re-checks every request.</para>
	/// <para><b>What counts as the origin.</b> Any live waypoint in the traveller's scene instance,
	/// including the destination itself, measured from the waypoint's own transform (the position
	/// the map cache bakes). It must be one the traveller has discovered whenever the request also
	/// requires the destination to be discovered: a player standing at a stone they never activated
	/// has not "used" it. Interacting with a waypoint discovers it, so in play this costs nothing.</para>
	/// <para><b>Who it binds.</b> The player's own map request. Designer-authored moves
	/// (<see cref="TeleportToWaypointAction"/> — a town-portal item, a quest return) are exempt:
	/// being usable away from a waypoint is their point.</para>
	/// </remarks>
	public readonly struct WaypointTravelPolicy
	{
		/// <summary>The shipped default: fast travel only from a waypoint.</summary>
		public const bool DefaultRequireNearbyWaypoint = true;

		/// <summary>
		/// The shipped origin radius, in metres. A waypoint's interaction range is 3.5 m and using
		/// the waypoint is how the travel map opens; the remainder is room to step back from the
		/// stone, and for the server's copy of the character lagging the client's.
		/// </summary>
		public const float DefaultNearbyRange = 10.0f;

		/// <summary>The floor for a configured radius. Below a character's own width nothing would ever qualify.</summary>
		public const float MinimumNearbyRange = 1.0f;

		/// <summary>
		/// The ceiling for a configured radius. Past this the rule is no longer "at a waypoint"; turn
		/// it off instead.
		/// </summary>
		public const float MaximumNearbyRange = 500.0f;

		/// <summary>The rule as shipped.</summary>
		public static readonly WaypointTravelPolicy Default = new WaypointTravelPolicy(DefaultRequireNearbyWaypoint, DefaultNearbyRange);

		/// <summary>
		/// The rule this server process enforces. Installed by the interactable system at
		/// initialisation and restored to <see cref="Default"/> when it shuts down. Server only;
		/// a client reads <see cref="IWaypointController.TravelPolicy"/> instead.
		/// </summary>
		public static WaypointTravelPolicy Server { get; set; } = Default;

		/// <summary>Refuse a player's travel request unless they stand near a discovered waypoint.</summary>
		public readonly bool RequireNearbyWaypoint;

		/// <summary>How near, in metres. Clamped to [<see cref="MinimumNearbyRange"/>, <see cref="MaximumNearbyRange"/>].</summary>
		public readonly float NearbyRange;

		public WaypointTravelPolicy(bool requireNearbyWaypoint, float nearbyRange)
		{
			RequireNearbyWaypoint = requireNearbyWaypoint;
			NearbyRange = ClampRange(nearbyRange);
		}

		/// <summary>
		/// Brings a configured radius into range. NaN and infinities, which a hand-edited asset or a
		/// corrupt payload can carry, become the default rather than a comparison that is always false.
		/// </summary>
		public static float ClampRange(float range)
		{
			if (float.IsNaN(range) || float.IsInfinity(range))
			{
				return DefaultNearbyRange;
			}
			return Mathf.Clamp(range, MinimumNearbyRange, MaximumNearbyRange);
		}

		/// <summary>True when <paramref name="waypoint"/> is within <see cref="NearbyRange"/> of <paramref name="traveller"/>. Inclusive.</summary>
		public bool IsWithinRange(Vector3 traveller, Vector3 waypoint)
		{
			return (traveller - waypoint).sqrMagnitude <= NearbyRange * NearbyRange;
		}

		/// <summary>
		/// The client's form of the check: whether <paramref name="traveller"/> stands near a
		/// waypoint of the map cache's scene that <paramref name="controller"/> has discovered.
		/// Always true when the rule is off.
		/// </summary>
		/// <param name="details">The cached scene the character is in.</param>
		/// <param name="sceneName">The instance-aware scene name the record is keyed by.</param>
		/// <param name="controller">The traveller's discovery record.</param>
		/// <param name="traveller">The traveller's position.</param>
		public bool IsSatisfiedBy(WorldSceneDetails details, string sceneName, IWaypointController controller, Vector3 traveller)
		{
			if (!RequireNearbyWaypoint)
			{
				return true;
			}
			if (details?.Waypoints == null || controller == null || string.IsNullOrEmpty(sceneName))
			{
				return false;
			}
			foreach (KeyValuePair<int, SceneWaypointDetails> entry in details.Waypoints)
			{
				if (entry.Value != null &&
					controller.IsUnlocked(sceneName, entry.Key) &&
					IsWithinRange(traveller, entry.Value.Position))
				{
					return true;
				}
			}
			return false;
		}
	}
}
