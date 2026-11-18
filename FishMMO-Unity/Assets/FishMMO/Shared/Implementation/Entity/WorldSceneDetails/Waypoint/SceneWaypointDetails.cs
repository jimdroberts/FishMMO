using System;
using UnityEngine;

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

		/// <summary>The player-facing name.</summary>
		public string Name;

		/// <summary>Optional description shown in the map's waypoint panel.</summary>
		[TextArea(1, 3)]
		public string Description;

		/// <summary>Where the marker is drawn, in world space.</summary>
		public Vector3 Position;

		/// <summary>Icon drawn for the waypoint. Falls back to the type's themed marker when null.</summary>
		public Sprite Icon;
	}
}
