using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The list of a scene's hand edits to its generated props (<see cref="ScenePropEdits"/>): each one's prop, what was done
	/// to it and whether the last bake of its source found it, with Frame and Forget (back to where it was generated).
	/// </summary>
	[CustomEditor(typeof(ScenePropEdits))]
	public sealed class ScenePropEditsEditor : Editor
	{
		private string status = string.Empty;

		public override void OnInspectorGUI()
		{
			var edits = (ScenePropEdits)target;
			EditorGUILayout.HelpBox("Hand edits to this scene's generated props, laid over every bake. Make them with the Scene view tool "
				+ "\"Edit Generated Props\". An unmatched edit's prop is no longer generated where it was (its template or site changed): "
				+ "Frame shows where it was, Forget drops it.", MessageType.None);
			int unmatched = 0;
			foreach (ScenePropEdits.Edit e in edits.Edits)
			{
				unmatched += e.Matched ? 0 : 1;
			}
			EditorGUILayout.LabelField($"{edits.Edits.Count} edit(s), {unmatched} unmatched");
			for (int i = 0; i < edits.Edits.Count; i++)
			{
				ScenePropEdits.Edit e = edits.Edits[i];
				using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
				{
					using (new EditorGUILayout.VerticalScope())
					{
						string what = e.Removed ? "removed" : $"moved {Vector3.Distance(e.Origin, e.Position):F2} m"
							+ (Quaternion.Angle(e.Rotation, e.OriginRotation) > 0.01f ? $", turned {Quaternion.Angle(e.Rotation, e.OriginRotation):F1}°" : "")
							+ ((e.Scale - e.OriginScale).sqrMagnitude > 1e-8f ? ", scaled" : "");
						EditorGUILayout.LabelField($"{(e.Prefab != null ? e.Prefab.name : e.PrefabGuid)}", EditorStyles.boldLabel);
						EditorGUILayout.LabelField($"{e.Source} · {what}{(e.Matched ? "" : " · UNMATCHED")}", EditorStyles.miniLabel);
					}
					if (GUILayout.Button("Frame", GUILayout.Width(52f)) && SceneView.lastActiveSceneView != null)
					{
						SceneView.lastActiveSceneView.Frame(new Bounds(e.Removed ? e.Origin : e.Position, Vector3.one * 8f), false);
					}
					if (GUILayout.Button("Forget", GUILayout.Width(56f)))
					{
						status = ScenePropEditTool.Forget(edits, i);
						GUIUtility.ExitGUI();
					}
				}
			}
			if (status.Length > 0)
			{
				EditorGUILayout.HelpBox(status, MessageType.Info);
			}
		}
	}
}
