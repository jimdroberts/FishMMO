using System.Collections.Generic;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Server-side index of the live waypoints in every loaded scene, by Unity scene handle and
	/// authored waypoint index.
	/// </summary>
	/// <remarks>
	/// <para>A fast-travel request names a waypoint by (scene, index), not by scene-object ID: the
	/// waypoint may be on the far side of the zone, outside the requesting client's observer
	/// range, so the client has no object ID to send. The server resolves the pair here against
	/// the objects actually loaded, which also means the destination position comes from the live
	/// transform rather than from a cache that could be stale.</para>
	/// <para>Keyed by scene <i>handle</i> rather than name because scene stacking loads several
	/// copies of one scene (instances) into one process, and a character in one instance must land
	/// at the waypoint in that instance. Waypoints register in <c>OnStartServer</c> and unregister
	/// in <c>OnStopServer</c>, which is also what an unloaded scene calls on its objects.</para>
	/// </remarks>
	public static class WaypointRegistry
	{
		private static readonly Dictionary<int, Dictionary<int, IWaypoint>> waypointsByScene =
			new Dictionary<int, Dictionary<int, IWaypoint>>();

		/// <summary>
		/// Adds a waypoint. A duplicate index in the same scene is refused and reported, since
		/// two waypoints sharing a bit would let discovering one unlock the other.
		/// </summary>
		/// <returns>True when registered.</returns>
		public static bool Register(IWaypoint waypoint)
		{
			if (waypoint == null || waypoint.GameObject == null)
			{
				return false;
			}
			int handle = waypoint.GameObject.scene.handle;
			if (!waypointsByScene.TryGetValue(handle, out Dictionary<int, IWaypoint> scene))
			{
				scene = new Dictionary<int, IWaypoint>();
				waypointsByScene[handle] = scene;
			}
			if (scene.TryGetValue(waypoint.WaypointIndex, out IWaypoint existing) &&
				existing != null && !ReferenceEquals(existing, waypoint) && existing.GameObject != null)
			{
				Logging.Log.Error("WaypointRegistry",
					$"Scene '{waypoint.SceneName}' has two waypoints with index {waypoint.WaypointIndex}: " +
					$"'{existing.Name}' and '{waypoint.Name}'. Waypoint indices must be unique within a scene; " +
					$"'{waypoint.Name}' will not be reachable by fast travel.");
				return false;
			}
			scene[waypoint.WaypointIndex] = waypoint;
			return true;
		}

		/// <summary>Removes a waypoint, if it is the one registered under its index.</summary>
		public static void Unregister(IWaypoint waypoint)
		{
			if (waypoint == null || waypoint.GameObject == null)
			{
				return;
			}
			int handle = waypoint.GameObject.scene.handle;
			if (!waypointsByScene.TryGetValue(handle, out Dictionary<int, IWaypoint> scene))
			{
				return;
			}
			if (scene.TryGetValue(waypoint.WaypointIndex, out IWaypoint existing) && ReferenceEquals(existing, waypoint))
			{
				scene.Remove(waypoint.WaypointIndex);
			}
			if (scene.Count == 0)
			{
				waypointsByScene.Remove(handle);
			}
		}

		/// <summary>Finds a live waypoint by scene handle and index.</summary>
		public static bool TryGet(int sceneHandle, int waypointIndex, out IWaypoint waypoint)
		{
			waypoint = null;
			if (!waypointsByScene.TryGetValue(sceneHandle, out Dictionary<int, IWaypoint> scene))
			{
				return false;
			}
			if (!scene.TryGetValue(waypointIndex, out waypoint) || waypoint == null || waypoint.GameObject == null)
			{
				waypoint = null;
				return false;
			}
			return true;
		}

		/// <summary>Number of waypoints registered in a scene.</summary>
		public static int Count(int sceneHandle)
		{
			return waypointsByScene.TryGetValue(sceneHandle, out Dictionary<int, IWaypoint> scene) ? scene.Count : 0;
		}

		/// <summary>Drops every registration. Tests, and process teardown.</summary>
		public static void Clear()
		{
			waypointsByScene.Clear();
		}
	}
}
