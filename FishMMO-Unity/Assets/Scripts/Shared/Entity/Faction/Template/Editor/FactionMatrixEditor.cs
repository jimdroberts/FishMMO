#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Custom Unity Editor for FactionMatrixTemplate. Provides a matrix UI for editing faction relationships.
	/// Automatically mirrors alliance levels and allows rebuilding matrix/factions from the inspector.
	/// </summary>
	[CustomEditor(typeof(FactionMatrixTemplate))]
	public class FactionMatrixEditor : Editor
	{
		/// <summary>
		/// Draws the custom inspector GUI for editing the faction matrix.
		/// Handles matrix layout, mirroring, and rebuild buttons.
		/// </summary>
		public override void OnInspectorGUI()
		{
			var script = (FactionMatrixTemplate)target;

			// Ensure all required data is present before drawing the matrix
			if (script.Factions != null &&
				script.Matrix != null &&
				script.Matrix.Factions != null)
			{
				GUILayout.BeginVertical();

				// Draw header row with faction names
				GUILayout.BeginHorizontal();
				GUILayout.Label("", GUILayout.Height(18), GUILayout.Width(96));
				for (int j = 0; j < script.Factions.Count; ++j)
				{
					if (script.Factions[j] == null)
					{
						// If a faction is missing, rebuild the matrix and mark dirty for saving
						script.RebuildMatrix();
						EditorUtility.SetDirty(script);
					}
					GUILayout.Label(script.Factions[j].Name, EditorStyles.wordWrappedLabel, GUILayout.Height(36), GUILayout.Width(96));
				}
				GUILayout.EndHorizontal();

				// Draw each row of the matrix
				for (int i = 0, y = 0; i < script.Matrix.Factions.Length && y < script.Factions.Count; ++i, ++y)
				{
					GUILayout.BeginHorizontal();
					GUILayout.Label(script.Factions[i % script.Factions.Count].Name, GUILayout.Height(18), GUILayout.Width(96));
					for (int x = 0; x < script.Factions.Count; ++x)
					{
						int index = x + y * script.Factions.Count;
						int invert = y + x * script.Factions.Count;

						// Diagonal: always Ally to self
						if (x == y)
						{
							script.Matrix.Factions[index] = (FactionAllianceLevel)EditorGUILayout.EnumPopup(FactionAllianceLevel.Ally, GUILayout.Height(18), GUILayout.Width(96));
						}
						else
						{
							// Edit alliance level for this pair
							script.Matrix.Factions[index] = (FactionAllianceLevel)EditorGUILayout.EnumPopup(script.Matrix.Factions[index], GUILayout.Height(18), GUILayout.Width(96));

							// Mirror alliance level to the inverted index (ensures symmetry)
							script.Matrix.Factions[invert] = script.Matrix.Factions[index];
						}
					}
					GUILayout.EndHorizontal();

					y %= script.Factions.Count;
				}
				GUILayout.EndVertical();
			}

			// Button to rebuild the matrix (recreates alliance levels)
			if (GUILayout.Button("Rebuild Matrix", GUILayout.Height(40)))
			{
				script.RebuildMatrix();
				EditorUtility.SetDirty(script);
			}

			// Button to rebuild factions (refreshes faction list)
			if (GUILayout.Button("Rebuild Factions", GUILayout.Height(40)))
			{
				script.RebuildFactions();
				EditorUtility.SetDirty(script);
			}
		}
	}
}
#endif