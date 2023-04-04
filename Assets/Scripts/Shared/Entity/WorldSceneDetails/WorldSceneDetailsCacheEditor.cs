#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(WorldSceneDetailsCache))]
public class WorldSceneDetailsCacheEditor : Editor
{
	public override void OnInspectorGUI()
	{
		base.OnInspectorGUI();
		var script = (WorldSceneDetailsCache)target;

		if (GUILayout.Button("Search", GUILayout.Height(40)))
		{
			script.Search();
			EditorUtility.SetDirty(script);
		}
	}
}
#endif