using System;
using System.Collections.Generic;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>One Gate Stone a character has discovered: the Gate spell's possible destination (issue #241).</summary>
	public readonly struct DiscoveredGateStone
	{
		public readonly string SceneName;
		public readonly int WaypointIndex;
		public readonly SceneWaypointDetails Details;

		public DiscoveredGateStone(string sceneName, int waypointIndex, SceneWaypointDetails details)
		{
			SceneName = sceneName;
			WaypointIndex = waypointIndex;
			Details = details;
		}
	}

	/// <summary>
	/// The Gate Stones a character has discovered, in every scene the baked details know: what the map lists while a Gate waits
	/// for its stone, and what the server checks before it starts that wait.
	/// </summary>
	/// <remarks>Pure: reads the details cache and the character's waypoint record, allocates nothing but the caller's list.</remarks>
	public static class GateStoneDirectory
	{
		/// <summary>
		/// Fills <paramref name="results"/> with the discovered stones: the scene <paramref name="firstScene"/> first (the one the
		/// character stands in), then the others by scene name, each scene's stones by index.
		/// </summary>
		public static void Collect(WorldSceneDetailsCache cache, IWaypointController waypoints, string firstScene,
			List<DiscoveredGateStone> results)
		{
			Collect(cache != null ? cache.Scenes : null, waypoints, firstScene, results);
		}

		/// <summary><see cref="Collect(WorldSceneDetailsCache, IWaypointController, string, List{DiscoveredGateStone})"/> over a cache's scenes.</summary>
		public static void Collect(IDictionary<string, WorldSceneDetails> scenes, IWaypointController waypoints, string firstScene,
			List<DiscoveredGateStone> results)
		{
			results.Clear();
			if (scenes == null || waypoints == null)
			{
				return;
			}
			foreach (KeyValuePair<string, WorldSceneDetails> scene in scenes)
			{
				if (string.IsNullOrEmpty(scene.Key) || scene.Value?.Waypoints == null)
				{
					continue;
				}
				foreach (KeyValuePair<int, SceneWaypointDetails> waypoint in scene.Value.Waypoints)
				{
					if (waypoint.Value != null && waypoint.Value.IsGateStone && waypoints.IsUnlocked(scene.Key, waypoint.Key))
					{
						results.Add(new DiscoveredGateStone(scene.Key, waypoint.Key, waypoint.Value));
					}
				}
			}
			results.Sort((a, b) =>
			{
				bool aFirst = string.Equals(a.SceneName, firstScene, StringComparison.Ordinal);
				bool bFirst = string.Equals(b.SceneName, firstScene, StringComparison.Ordinal);
				if (aFirst != bFirst)
				{
					return aFirst ? -1 : 1;
				}
				int byScene = string.CompareOrdinal(a.SceneName, b.SceneName);
				return byScene != 0 ? byScene : a.WaypointIndex.CompareTo(b.WaypointIndex);
			});
		}

		/// <summary>Whether the character has discovered at least one Gate Stone anywhere.</summary>
		public static bool AnyDiscovered(WorldSceneDetailsCache cache, IWaypointController waypoints)
		{
			return AnyDiscovered(cache != null ? cache.Scenes : null, waypoints);
		}

		/// <summary><see cref="AnyDiscovered(WorldSceneDetailsCache, IWaypointController)"/> over a cache's scenes.</summary>
		public static bool AnyDiscovered(IDictionary<string, WorldSceneDetails> scenes, IWaypointController waypoints)
		{
			if (scenes == null || waypoints == null)
			{
				return false;
			}
			foreach (KeyValuePair<string, WorldSceneDetails> scene in scenes)
			{
				if (string.IsNullOrEmpty(scene.Key) || scene.Value?.Waypoints == null)
				{
					continue;
				}
				foreach (KeyValuePair<int, SceneWaypointDetails> waypoint in scene.Value.Waypoints)
				{
					if (waypoint.Value != null && waypoint.Value.IsGateStone && waypoints.IsUnlocked(scene.Key, waypoint.Key))
					{
						return true;
					}
				}
			}
			return false;
		}
	}
}
