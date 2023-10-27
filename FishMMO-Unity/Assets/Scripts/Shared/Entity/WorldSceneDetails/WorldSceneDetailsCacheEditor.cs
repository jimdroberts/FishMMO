#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared
{
	[CustomEditor(typeof(WorldSceneDetailsCache))]
	public class WorldSceneDetailsCacheEditor : Editor
	{
		public override void OnInspectorGUI()
		{
			base.OnInspectorGUI();
			var script = (WorldSceneDetailsCache)target;

			if (GUILayout.Button("Rebuild", GUILayout.Height(40)))
			{
				script.Rebuild();
				EditorUtility.SetDirty(script);
			}
		}
	}
}
#endif