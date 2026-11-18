using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Where a scene's baked assets are kept beside it (its "&lt;Scene&gt; Terrain" folder), and how a rebake keeps every
	/// reference to them: the terrain details, the props and the other per-scene bakes. Core editor (Phase 4 of the core and
	/// modules plan, 2026-11-18): the terrain detail baker uses it whether or not the World Design module is installed.
	/// </summary>
	public static class SceneBakeAssets
	{
		/// <summary>
		/// The terrain folder of the scene being generated, set by the generator for the length of a generation: a
		/// new scene has no path until it is first saved, and without this its baked sets were kept in memory and
		/// serialized into the scene file.
		/// </summary>
		public static string BakeFolder;

		/// <summary>
		/// Where a scene's baked asset named <paramref name="what"/> goes: its terrain folder (<see cref="BakeFolder"/>
		/// while it is generated); null for an unsaved scene outside a generation, or one with no terrain folder.
		/// </summary>
		public static string AssetPath(Scene scene, string what)
		{
			if (!string.IsNullOrEmpty(BakeFolder) && AssetDatabase.IsValidFolder(BakeFolder))
			{
				string folderName = System.IO.Path.GetFileName(BakeFolder.TrimEnd('/'));
				string baseName = folderName.EndsWith(" Terrain") ? folderName.Substring(0, folderName.Length - " Terrain".Length) : folderName;
				return $"{BakeFolder.TrimEnd('/')}/{baseName} {what}.asset";
			}
			if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
			{
				return null;
			}
			string folder = System.IO.Path.GetDirectoryName(scene.path)?.Replace('\\', '/');
			string name = System.IO.Path.GetFileNameWithoutExtension(scene.path);
			string terrain = $"{folder}/{name} Terrain";
			return AssetDatabase.IsValidFolder(terrain) ? $"{terrain}/{name} {what}.asset" : null;
		}

		/// <summary>
		/// Keeps a freshly built set at <paramref name="path"/>: copied into the asset already there, so every reference
		/// to it survives a rebake (the scene's, and the saved scene's when a repaint is discarded: a deleted and
		/// recreated asset gets a new GUID and leaves that scene pointing at nothing); created when there is none.
		/// Returns the object to reference. In memory when the scene has no folder.
		/// </summary>
		public static T Store<T>(T fresh, string path) where T : ScriptableObject
		{
			if (path == null)
			{
				return fresh;
			}
			var existing = AssetDatabase.LoadAssetAtPath<T>(path);
			if (existing != null)
			{
				EditorUtility.CopySerialized(fresh, existing);
				Object.DestroyImmediate(fresh);
				EditorUtility.SetDirty(existing);
				return existing;
			}
			DeleteAsset(path);   // something else under the name
			AssetDatabase.CreateAsset(fresh, path);
			return fresh;
		}

		/// <summary>Deletes the asset at <paramref name="path"/>, if there is one.</summary>
		public static void DeleteAsset(string path)
		{
			if (path != null && AssetDatabase.LoadMainAssetAtPath(path) != null)
			{
				AssetDatabase.DeleteAsset(path);
			}
		}
	}
}
