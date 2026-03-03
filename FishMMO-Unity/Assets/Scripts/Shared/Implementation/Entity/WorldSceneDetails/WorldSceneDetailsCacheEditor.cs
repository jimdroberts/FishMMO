#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Custom editor for WorldSceneDetailsCache ScriptableObject.
	/// Adds a "Rebuild" button to the inspector for manual cache rebuilding.
	/// </summary>
	[CustomEditor(typeof(WorldSceneDetailsCache))]
	public class WorldSceneDetailsCacheEditor : Editor
	{
		/// <summary>
		/// Draws the custom inspector GUI, including the "Rebuild" button.
		/// When pressed, triggers the cache rebuild and marks the asset as dirty.
		/// </summary>
		public override void OnInspectorGUI()
		{
			base.OnInspectorGUI();
			var script = (WorldSceneDetailsCache)target;

			// Draw a large "Rebuild" button in the inspector.
			if (GUILayout.Button("Rebuild", GUILayout.Height(40)))
			{
				// Rebuild the cache and mark the asset as dirty so changes are saved.
				script.Rebuild();
				EditorUtility.SetDirty(script);
			}
		}
	}
}
#endif