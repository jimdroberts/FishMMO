#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared
{
	[CustomEditor(typeof(FactionMatrixTemplate))]
	public class FactionMatrixEditor : Editor
	{
		public override void OnInspectorGUI()
		{
			var script = (FactionMatrixTemplate)target;

			if (script.Factions != null &&
				script.Matrix != null &&
				script.Matrix.Factions != null)
			{
				GUILayout.BeginVertical();

				GUILayout.BeginHorizontal();
				GUILayout.Label("", GUILayout.Height(18), GUILayout.Width(96));
				for (int j = 0; j < script.Factions.Count; ++j)
				{
					if (script.Factions[j] == null)
					{
						script.RebuildMatrix();
						EditorUtility.SetDirty(script);
					}
					GUILayout.Label(script.Factions[j].Name, EditorStyles.wordWrappedLabel, GUILayout.Height(36), GUILayout.Width(96));
				}
				GUILayout.EndHorizontal();

				for (int i = 0, y = 0; i < script.Matrix.Factions.Length && y < script.Factions.Count; ++i, ++y)
				{
					GUILayout.BeginHorizontal();
					GUILayout.Label(script.Factions[i % script.Factions.Count].Name, GUILayout.Height(18), GUILayout.Width(96));
					for (int x = 0; x < script.Factions.Count; ++x)
					{
						int index = x + y * script.Factions.Count;
						int invert = y + x * script.Factions.Count;

						/*if (mirrorIndices.Contains(index))
						{
							GUILayout.Label("", GUILayout.Height(18), GUILayout.Width(96));
						}
						else
						{*/
							if (x == y)
							{
								script.Matrix.Factions[index] = (FactionAllianceLevel)EditorGUILayout.EnumPopup(FactionAllianceLevel.Ally, GUILayout.Height(18), GUILayout.Width(96));
							}
							else
							{
								script.Matrix.Factions[index] = (FactionAllianceLevel)EditorGUILayout.EnumPopup(script.Matrix.Factions[index], GUILayout.Height(18), GUILayout.Width(96));

								// mirror alliance level
								//mirrorIndices.Add(invert);

								script.Matrix.Factions[invert] = script.Matrix.Factions[index];
							}
						//}
					}
					GUILayout.EndHorizontal();

					y %= script.Factions.Count;
				}
				GUILayout.EndVertical();
			}

			if (GUILayout.Button("Rebuild Matrix", GUILayout.Height(40)))
			{
				script.RebuildMatrix();
				EditorUtility.SetDirty(script);
			}

			if (GUILayout.Button("Rebuild Factions", GUILayout.Height(40)))
			{
				script.RebuildFactions();
				EditorUtility.SetDirty(script);
			}
		}
	}
}
#endif