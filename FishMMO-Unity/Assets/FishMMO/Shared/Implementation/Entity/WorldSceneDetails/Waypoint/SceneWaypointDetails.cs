using System;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// A waypoint as harvested into the world scene details cache: what the client needs to draw
	/// it on the map without the object being streamed.
	/// </summary>
	/// <remarks>
	/// The server does not use this — it resolves the live object through
	/// <see cref="WaypointRegistry"/> — so a stale cache can only produce a marker the server then
	/// refuses, never a teleport to somewhere the waypoint no longer is.
	/// </remarks>
	[Serializable]
	public class SceneWaypointDetails
	{
		/// <summary>The waypoint's authored index within its scene.</summary>
		public int Index;

		/// <summary>A fast-travel point or a Gate Stone (issue #241). A cache baked before kinds existed reads Waypoint.</summary>
		public WaypointKind Kind;

		/// <summary>The player-facing name.</summary>
		public string Name;

		/// <summary>Optional description shown in the map's waypoint panel.</summary>
		[TextArea(1, 3)]
		public string Description;

		/// <summary>Where the marker is drawn, in world space.</summary>
		public Vector3 Position;

		/// <summary>Icon drawn for the waypoint. Falls back to the type's themed marker when null.</summary>
		public Sprite Icon;

		/// <summary>
		/// Whether <see cref="ArrivalPosition"/> and <see cref="ArrivalYaw"/> were baked (a cache from formerly has
		/// neither). A Gate spell arriving at a Gate Stone on another scene server lands here: the destination is not loaded
		/// where the gate is cast, so the cache is the only place the caster's server can read it from.
		/// </summary>
		public bool HasArrival;

		/// <summary>Where a traveller lands (the waypoint's arrival point), in world space.</summary>
		public Vector3 ArrivalPosition;

		/// <summary>The arrival point's facing, degrees about up.</summary>
		public float ArrivalYaw;

		/// <summary>Whether this is a Gate Stone.</summary>
		public bool IsGateStone => Kind == WaypointKind.GateStone;
	}
}
