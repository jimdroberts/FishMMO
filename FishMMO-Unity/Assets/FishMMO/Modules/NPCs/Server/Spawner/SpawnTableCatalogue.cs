using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Server.Implementation.World.SceneServer.Spawner
{
	/// <summary>
	/// The baked spawn table of every world scene, by scene name.
	/// </summary>
	/// <remarks>
	/// Generated alongside the tables; the baker keeps it in step. <see cref="SpawnerSystem"/>
	/// references it, which is what pulls every table into the server-only bundle and keeps them
	/// out of client builds.
	/// </remarks>
	public class SpawnTableCatalogue : ScriptableObject
	{
		/// <summary>
		/// One table per world scene that has spawners.
		/// </summary>
		public List<SceneSpawnTable> Tables = new List<SceneSpawnTable>();

		/// <summary>
		/// Tables by scene name, built on first lookup.
		/// </summary>
		[NonSerialized]
		private Dictionary<string, SceneSpawnTable> byScene;

		/// <summary>
		/// The table baked for <paramref name="sceneName"/>, if there is one.
		/// </summary>
		public bool TryGet(string sceneName, out SceneSpawnTable table)
		{
			table = null;
			if (string.IsNullOrEmpty(sceneName))
			{
				return false;
			}

			if (byScene == null)
			{
				byScene = new Dictionary<string, SceneSpawnTable>(Tables.Count);
				for (int i = 0; i < Tables.Count; ++i)
				{
					SceneSpawnTable candidate = Tables[i];
					if (candidate != null && !string.IsNullOrEmpty(candidate.SceneName))
					{
						byScene[candidate.SceneName] = candidate;
					}
				}
			}
			return byScene.TryGetValue(sceneName, out table);
		}

		/// <summary>
		/// Drops the lookup so the next query rebuilds it.
		/// </summary>
		public void Invalidate()
		{
			byScene = null;
		}

		private void OnValidate()
		{
			Invalidate();
		}
	}
}
