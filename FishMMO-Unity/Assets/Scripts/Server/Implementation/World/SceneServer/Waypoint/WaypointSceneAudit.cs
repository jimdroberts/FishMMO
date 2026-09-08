using System.Collections.Generic;
using System.Text;
using FishMMO.Logging;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Checks, once per loaded scene, that the waypoints the client can see are the waypoints
	/// the server can travel to.
	/// </summary>
	/// <remarks>
	/// <para>The map draws from the baked <see cref="WorldSceneDetails.Waypoints"/>; the server
	/// resolves a travel request from the live <see cref="WaypointRegistry"/>. They are keyed the
	/// same way, (scene, index), and nothing else ties them together. A waypoint added to a scene
	/// without a cache rebuild is discoverable on foot and then invisible on every map; a
	/// waypoint in the cache whose object no longer spawns is drawn and refused. Both are silent
	/// authoring drift, and the only place that holds both sides at once is a scene server that
	/// has just finished loading the scene.</para>
	/// <para>The comparison is a pure function over the two index sets so it can be pinned by a
	/// test; the scene-server hook only gathers the inputs and logs the answer.</para>
	/// </remarks>
	public static class WaypointSceneAudit
	{
		/// <summary>
		/// Compares the live and baked waypoint indices of one scene.
		/// </summary>
		/// <param name="live">Indices registered by spawned <c>Waypoint</c> objects.</param>
		/// <param name="baked">Indices in the world scene details cache.</param>
		/// <param name="liveOnly">Live but not baked: the map will never draw them.</param>
		/// <param name="bakedOnly">Baked but not live: the map draws them and travel is refused.</param>
		/// <returns>True when the two sets agree.</returns>
		public static bool Compare(IEnumerable<int> live, IEnumerable<int> baked,
			List<int> liveOnly, List<int> bakedOnly)
		{
			liveOnly.Clear();
			bakedOnly.Clear();

			HashSet<int> liveSet = new HashSet<int>(live ?? System.Array.Empty<int>());
			HashSet<int> bakedSet = new HashSet<int>(baked ?? System.Array.Empty<int>());

			foreach (int index in liveSet)
			{
				if (!bakedSet.Contains(index))
				{
					liveOnly.Add(index);
				}
			}
			foreach (int index in bakedSet)
			{
				if (!liveSet.Contains(index))
				{
					bakedOnly.Add(index);
				}
			}
			liveOnly.Sort();
			bakedOnly.Sort();
			return liveOnly.Count == 0 && bakedOnly.Count == 0;
		}

		/// <summary>
		/// Runs the comparison for a scene that has finished loading and reports drift as errors.
		/// </summary>
		/// <param name="sceneName">The scene's name, as the cache keys it.</param>
		/// <param name="sceneHandle">The loaded scene's handle, as the registry keys it.</param>
		/// <param name="cache">The world scene details cache this server is running with.</param>
		/// <returns>True when nothing is wrong.</returns>
		public static bool Audit(string sceneName, int sceneHandle, WorldSceneDetailsCache cache)
		{
			if (string.IsNullOrEmpty(sceneName) || cache == null)
			{
				return true;
			}

			List<int> live = new List<int>();
			WaypointRegistry.CollectIndices(sceneHandle, live);

			List<int> baked = new List<int>();
			if (cache.Scenes != null &&
				cache.Scenes.TryGetValue(sceneName, out WorldSceneDetails details) &&
				details?.Waypoints != null)
			{
				foreach (KeyValuePair<int, SceneWaypointDetails> entry in details.Waypoints)
				{
					baked.Add(entry.Key);
				}
			}

			List<int> liveOnly = new List<int>();
			List<int> bakedOnly = new List<int>();
			if (Compare(live, baked, liveOnly, bakedOnly))
			{
				if (live.Count > 0)
				{
					Log.Debug("WaypointSceneAudit", $"Scene '{sceneName}': {live.Count} waypoint(s) live and baked.");
				}
				return true;
			}

			StringBuilder sb = new StringBuilder();
			sb.Append("Scene '").Append(sceneName).Append("': the live waypoints and the world scene details cache disagree.");
			if (liveOnly.Count > 0)
			{
				sb.Append(" Live but not baked (players can discover these but no map will ever draw them): ")
				  .Append(string.Join(", ", liveOnly)).Append('.');
			}
			if (bakedOnly.Count > 0)
			{
				sb.Append(" Baked but not live (the map draws these and every travel request is refused): ")
				  .Append(string.Join(", ", bakedOnly)).Append('.');
			}
			sb.Append(" Rebuild the world scene details cache (FishMMO/Rebuild World Scene Details) and redeploy it with the scene.");
			Log.Error("WaypointSceneAudit", sb.ToString());
			return false;
		}
	}
}
