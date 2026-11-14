using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The world scenes only the client draws (<see cref="WorldSceneSettings.ClientBackdrop"/>: the login screen's world),
	/// and the assets that belong to them: the scene, its cut's terrain folder and its atlas entry. The Addressables rules
	/// put all of it in client groups, so a server build carries none of it.
	/// </summary>
	/// <remarks>
	/// Found by reading each world scene's file for the flag, remembered against the file's write time, so a pass over
	/// thousands of assets reads each scene once. The scene list itself is refreshed at most every few seconds.
	/// </remarks>
	public static class ClientBackdropScenes
	{
		private const string Flag = "ClientBackdrop: 1";
		private const double ListSeconds = 5.0;

		private static readonly Dictionary<string, (DateTime Written, bool Backdrop)> byPath =
			new Dictionary<string, (DateTime, bool)>(StringComparer.OrdinalIgnoreCase);
		private static List<string> backdrops;
		private static double listedAt = double.NegativeInfinity;

		/// <summary>Whether the scene file at <paramref name="scenePath"/> is flagged as a client backdrop.</summary>
		public static bool IsBackdropScene(string scenePath)
		{
			if (string.IsNullOrEmpty(scenePath) || !scenePath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) || !File.Exists(scenePath))
			{
				return false;
			}
			DateTime written = File.GetLastWriteTimeUtc(scenePath);
			if (byPath.TryGetValue(scenePath, out (DateTime Written, bool Backdrop) known) && known.Written == written)
			{
				return known.Backdrop;
			}
			bool backdrop = false;
			foreach (string line in File.ReadLines(scenePath))
			{
				if (line.Trim() == Flag)
				{
					backdrop = true;
					break;
				}
			}
			byPath[scenePath] = (written, backdrop);
			return backdrop;
		}

		/// <summary>The backdrop scenes among the world scenes.</summary>
		public static IReadOnlyList<string> All()
		{
			double now = EditorApplication.timeSinceStartup;
			if (backdrops == null || now - listedAt > ListSeconds || now < listedAt)
			{
				backdrops = new List<string>();
				foreach (string path in WorldEditorAssets.WorldScenePaths())
				{
					if (IsBackdropScene(path))
					{
						backdrops.Add(path);
					}
				}
				listedAt = now;
			}
			return backdrops;
		}

		/// <summary>Forgets the scene list (a backdrop was just made or its flag changed).</summary>
		public static void Invalidate()
		{
			backdrops = null;
		}

		/// <summary>
		/// Whether <paramref name="assetPath"/> belongs to a backdrop: the scene itself, anything in its cut's terrain
		/// folder, or its atlas entry and site settings.
		/// </summary>
		public static bool Owns(string assetPath)
		{
			if (string.IsNullOrEmpty(assetPath))
			{
				return false;
			}
			string path = assetPath.Replace('\\', '/');
			foreach (string scene in All())
			{
				if (string.Equals(path, scene, StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
				string name = Path.GetFileNameWithoutExtension(scene);
				string terrainFolder = $"{Path.GetDirectoryName(scene)?.Replace('\\', '/')}/{WorldEditorAssets.Sanitize(name)} Terrain/";
				if (path.StartsWith(terrainFolder, StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
				string entry = $"{WorldEditorAssets.ScenesFolder}/{WorldEditorAssets.Sanitize(name)}";
				if (string.Equals(path, entry + ".asset", StringComparison.OrdinalIgnoreCase) ||
					string.Equals(path, entry + " POI Settings.asset", StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}
			return false;
		}
	}
}
