#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Shared.NameGeneration;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The scene's "Points of Interest" root in the inspector: its settings editable in place (Jim, 2026-10-10: density
	/// is "editable afterwards on a settings component in the scene"), what the last plan made, and Regenerate POIs.
	/// </summary>
	[CustomEditor(typeof(ScenePointOfInterestSettings))]
	public sealed class ScenePointOfInterestSettingsEditor : UnityEditor.Editor
	{
		private UnityEditor.Editor settingsEditor;
		private bool showSites;

		private void OnDisable()
		{
			if (settingsEditor != null)
			{
				DestroyImmediate(settingsEditor);
			}
		}

		public override void OnInspectorGUI()
		{
			DrawDefaultInspector();
			var component = (ScenePointOfInterestSettings)target;

			if (component.Settings != null)
			{
				EditorGUILayout.Space();
				EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);
				CreateCachedEditor(component.Settings, null, ref settingsEditor);
				using (new EditorGUI.IndentLevelScope())
				{
					settingsEditor.OnInspectorGUI();
				}
			}
			else
			{
				EditorGUILayout.HelpBox("No settings asset: the scene is planned at Normal density with no capital. The atlas page makes one when the scene is cut.", MessageType.Info);
			}

			ScenePointsOfInterest points = component.Points;
			if (points != null)
			{
				EditorGUILayout.Space();
				EditorGUILayout.LabelField(PointOfInterestStage.Summary(PointOfInterestPlan.FromAsset(points)), EditorStyles.wordWrappedMiniLabel);
				var groups = new SortedDictionary<string, int>();
				foreach (PointOfInterestRecord record in points.Points)
				{
					string group = PointOfInterestKinds.Info(record.Kind).Group.ToString();
					groups.TryGetValue(group, out int n);
					groups[group] = n + 1;
				}
				var parts = new List<string>();
				foreach (KeyValuePair<string, int> pair in groups)
				{
					parts.Add($"{pair.Key} {pair.Value}");
				}
				EditorGUILayout.LabelField(string.Join(", ", parts), EditorStyles.wordWrappedMiniLabel);
				showSites = EditorGUILayout.Foldout(showSites, $"Sites ({points.Points.Count})", true);
				if (showSites)
				{
					foreach (PointOfInterestRecord record in points.Points)
					{
						EditorGUILayout.LabelField($"{record.Name}", $"{PointOfInterestKinds.Info(record.Kind).DisplayName}{(string.IsNullOrEmpty(record.Template) ? string.Empty : $" · {record.Template}")}");
					}
				}
			}

			EditorGUILayout.Space();
			if (GUILayout.Button("Regenerate POIs"))
			{
				if (EditorUtility.DisplayDialog("Regenerate POIs", "Plan and build this scene's points of interest again? Every generated site is replaced; pads already flattened stay.", "Regenerate", "Cancel"))
				{
					PointOfInterestGenerator.RegenerateOpenScene();
					GUIUtility.ExitGUI();
				}
			}
		}
	}
}
#endif
