using System.Collections.Generic;
using UnityEngine.AI;

namespace FishMMO.Server.Implementation.World.SceneServer.Navigation
{
	/// <summary>
	/// Adds a world scene's NavMesh while any instance of the scene is loaded, from <see cref="SceneNavMeshCatalogue"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Once per scene, not per instance.</b> The Unity NavMesh is global, not per physics scene, and a scene
	/// server can run several stacked instances of one world scene at the same coordinates. Their NavMeshes would be
	/// identical and overlap exactly, so the first instance adds it and the last one to unload removes it.
	/// </para>
	/// <para>
	/// A plain class rather than part of <see cref="NavMeshSystem"/>, so tests can drive it without FishNet.
	/// </para>
	/// </remarks>
	public sealed class SceneNavMeshes
	{
		private sealed class Loaded
		{
			public NavMeshDataInstance Instance;
			public int Count;
		}

		private readonly Dictionary<string, Loaded> byScene = new Dictionary<string, Loaded>();

		/// <summary>Where the NavMeshes come from.</summary>
		public SceneNavMeshCatalogue Catalogue { get; set; }

		public SceneNavMeshes(SceneNavMeshCatalogue catalogue)
		{
			Catalogue = catalogue;
		}

		/// <summary>True while <paramref name="sceneName"/>'s NavMesh is in the world.</summary>
		public bool IsLoaded(string sceneName) => sceneName != null && byScene.ContainsKey(sceneName);

		/// <summary>How many scenes' NavMeshes are in the world.</summary>
		public int Count => byScene.Count;

		/// <summary>An instance of <paramref name="sceneName"/> loaded. Returns false when the scene has no NavMesh.</summary>
		public bool Add(string sceneName)
		{
			if (string.IsNullOrEmpty(sceneName))
			{
				return false;
			}
			if (byScene.TryGetValue(sceneName, out Loaded loaded))
			{
				loaded.Count++;
				return true;
			}
			if (Catalogue == null || !Catalogue.TryGet(sceneName, out SceneNavMeshCatalogue.Entry entry))
			{
				return false;
			}
			NavMeshDataInstance instance = NavMesh.AddNavMeshData(entry.Data, entry.Position, entry.Rotation);
			if (!instance.valid)
			{
				return false;
			}
			byScene[sceneName] = new Loaded { Instance = instance, Count = 1 };
			return true;
		}

		/// <summary>An instance of <paramref name="sceneName"/> unloaded; the last one takes its NavMesh out.</summary>
		public void Remove(string sceneName)
		{
			if (sceneName == null || !byScene.TryGetValue(sceneName, out Loaded loaded))
			{
				return;
			}
			if (--loaded.Count > 0)
			{
				return;
			}
			loaded.Instance.Remove();
			byScene.Remove(sceneName);
		}

		/// <summary>Takes every NavMesh out.</summary>
		public void Clear()
		{
			foreach (Loaded loaded in byScene.Values)
			{
				loaded.Instance.Remove();
			}
			byScene.Clear();
		}
	}
}
