using System;
using System.Collections.Generic;
using UnityEditor;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Every world scene a server runs: the world scene folder (and the local one when it is enabled), without the
	/// backdrops only the client draws. Shared by the bakes that visit each world scene (portal views; the NPCs module's
	/// spawn tables).
	/// </summary>
	public static class WorldScenePaths
	{
		/// <summary>The world scenes' paths, sorted.</summary>
		public static List<string> FindAll()
		{
			List<string> folders = new List<string> { Constants.Configuration.WorldScenePath.TrimEnd('/') };
			if (EditorPrefs.GetBool("FishMMOEnableLocalDirectory"))
			{
				string local = Constants.Configuration.LocalScenePath.TrimEnd('/');
				if (AssetDatabase.IsValidFolder(local))
				{
					folders.Add(local);
				}
			}

			List<string> paths = new List<string>();
			foreach (string guid in AssetDatabase.FindAssets("t:Scene", folders.ToArray()))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				// A backdrop only the client draws has nothing to spawn and no server to run it.
				if (path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) && !paths.Contains(path)
					&& !ClientBackdropScenes.IsBackdropScene(path))
				{
					paths.Add(path);
				}
			}
			paths.Sort(StringComparer.Ordinal);
			return paths;
		}
	}
}
