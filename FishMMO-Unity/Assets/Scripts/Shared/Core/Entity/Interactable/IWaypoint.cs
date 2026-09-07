using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// A discoverable fast-travel point. Interacting with one unlocks it for the character;
	/// once unlocked it can be travelled to from the world map.
	/// </summary>
	/// <remarks>
	/// Identity is the pair (<see cref="SceneName"/>, <see cref="WaypointIndex"/>). The index is
	/// authored on the component and must be unique within its scene; it is the bit position the
	/// character's unlock record stores, which is why it is a small integer and not a name or a
	/// GUID — see <c>WaypointUnlockMask</c>.
	/// </remarks>
	public interface IWaypoint : IInteractable
	{
		/// <summary>The authored index of this waypoint within its scene, from 0.</summary>
		int WaypointIndex { get; }

		/// <summary>The name of the scene this waypoint stands in.</summary>
		string SceneName { get; }

		/// <summary>The player-facing name shown on the map.</summary>
		string WaypointName { get; }

		/// <summary>Optional description shown in the map's waypoint panel.</summary>
		string Description { get; }

		/// <summary>Where a travelling character lands. Falls back to the waypoint's own transform.</summary>
		Transform ArrivalPoint { get; }

		/// <summary>
		/// Designer-authored conditions a character must meet to travel here. Evaluated on the
		/// server against the traveller. Null or empty means no conditions.
		/// </summary>
		List<BaseCondition> TravelConditions { get; }

		/// <summary>Triggers invoked server-side after a character arrives here by fast travel.</summary>
		List<Trigger> OnTravelTriggers { get; }
	}
}
