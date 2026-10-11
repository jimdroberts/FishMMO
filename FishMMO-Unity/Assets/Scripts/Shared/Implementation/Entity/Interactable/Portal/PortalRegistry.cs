using System;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// The live portals in every loaded scene, by Unity scene handle and authored portal index.
	/// </summary>
	/// <remarks>
	/// Keyed by handle, like <see cref="WaypointRegistry"/>, because instances load several copies
	/// of one scene into one process. Portals register in <c>OnEnable</c> on every peer — a
	/// <see cref="SceneTeleporter"/> portal has no NetworkObject and so no server start to hang it
	/// on — and the server uses the registry to know which scenes' world rows to read and which
	/// portals to report to a player entering a scene.
	/// </remarks>
	public static class PortalRegistry
	{
		private static readonly Dictionary<int, Dictionary<int, PortalActivation>> portalsByScene =
			new Dictionary<int, Dictionary<int, PortalActivation>>();

		/// <summary>A portal registered. The server reads its scene's world rows if it has not.</summary>
		public static event Action<PortalActivation> Registered;

		/// <summary>
		/// Adds a portal. A duplicate index in one scene is refused and reported: two portals sharing
		/// an index would open together.
		/// </summary>
		public static bool Register(PortalActivation portal)
		{
			if (portal == null)
			{
				return false;
			}
			int handle = portal.gameObject.scene.handle;
			if (!portalsByScene.TryGetValue(handle, out Dictionary<int, PortalActivation> scene))
			{
				scene = new Dictionary<int, PortalActivation>();
				portalsByScene[handle] = scene;
			}
			if (scene.TryGetValue(portal.PortalIndex, out PortalActivation existing) &&
				existing != null && !ReferenceEquals(existing, portal))
			{
				Logging.Log.Error("PortalRegistry",
					$"Scene '{portal.SceneName}' has two portals with index {portal.PortalIndex}: '{existing.name}' and '{portal.name}'. " +
					$"Portal indices must be unique within a scene; '{portal.name}' is not registered.");
				return false;
			}
			scene[portal.PortalIndex] = portal;
			Registered?.Invoke(portal);
			return true;
		}

		/// <summary>Removes a portal, if it is the one registered under its index.</summary>
		public static void Unregister(PortalActivation portal)
		{
			if (ReferenceEquals(portal, null))
			{
				return;
			}
			foreach (KeyValuePair<int, Dictionary<int, PortalActivation>> scene in portalsByScene)
			{
				if (scene.Value.TryGetValue(portal.PortalIndex, out PortalActivation existing) && ReferenceEquals(existing, portal))
				{
					scene.Value.Remove(portal.PortalIndex);
					if (scene.Value.Count == 0)
					{
						portalsByScene.Remove(scene.Key);
					}
					return;
				}
			}
		}

		/// <summary>Finds a live portal by scene handle and index.</summary>
		public static bool TryGet(int sceneHandle, int portalIndex, out PortalActivation portal)
		{
			portal = null;
			return portalsByScene.TryGetValue(sceneHandle, out Dictionary<int, PortalActivation> scene) &&
				scene.TryGetValue(portalIndex, out portal) && portal != null;
		}

		/// <summary>Appends the live portals of one scene instance, sorted by index.</summary>
		public static void Collect(int sceneHandle, List<PortalActivation> results)
		{
			if (results == null || !portalsByScene.TryGetValue(sceneHandle, out Dictionary<int, PortalActivation> scene))
			{
				return;
			}
			int start = results.Count;
			foreach (PortalActivation portal in scene.Values)
			{
				if (portal != null)
				{
					results.Add(portal);
				}
			}
			results.Sort(start, results.Count - start, Comparer<PortalActivation>.Create((a, b) => a.PortalIndex.CompareTo(b.PortalIndex)));
		}

		/// <summary>Whether any live portal stands in a scene with this name, in any instance.</summary>
		public static bool AnyInScene(string sceneName)
		{
			foreach (Dictionary<int, PortalActivation> scene in portalsByScene.Values)
			{
				foreach (PortalActivation portal in scene.Values)
				{
					if (portal != null && string.Equals(portal.SceneName, sceneName, StringComparison.Ordinal))
					{
						return true;
					}
				}
			}
			return false;
		}

		/// <summary>Appends the names of every scene with a live portal, each once.</summary>
		public static void CollectSceneNames(HashSet<string> results)
		{
			if (results == null)
			{
				return;
			}
			foreach (Dictionary<int, PortalActivation> scene in portalsByScene.Values)
			{
				foreach (PortalActivation portal in scene.Values)
				{
					if (portal != null)
					{
						results.Add(portal.SceneName);
					}
				}
			}
		}

		/// <summary>Drops every registration. Tests.</summary>
		public static void Clear()
		{
			portalsByScene.Clear();
		}
	}
}
