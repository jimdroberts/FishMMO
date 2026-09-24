#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Asks for a new scene's name, and whether to add local detail to its ground.
	/// </summary>
	/// <remarks>
	/// A window rather than a plain dialog because a name has to be refused with a reason while it
	/// is being typed. <c>EditorUtility.DisplayDialog</c> cannot take text at all, and finding out
	/// that a name is taken only after the folder, the scene and a terrain asset per tile have been
	/// written is the version of this that wastes somebody's afternoon.
	/// </remarks>
	public sealed class SceneNamePrompt : EditorWindow
	{
		private string details;
		private string sceneName;
		private bool fineDetail = true;
		private bool accepted;
		private bool done;

		/// <summary>
		/// Asks for a name. Returns null if it was cancelled.
		/// </summary>
		/// <param name="details">What is about to be cut, shown above the field.</param>
		/// <param name="suggestion">The name the field opens with.</param>
		/// <param name="fineDetail">Whether local detail was asked for.</param>
		public static string Ask(string details, string suggestion, out bool fineDetail)
		{
			var window = CreateInstance<SceneNamePrompt>();
			window.titleContent = new GUIContent("Cut scene");
			window.details = details;
			window.sceneName = suggestion;
			window.minSize = new Vector2(460f, 260f);
			window.maxSize = new Vector2(460f, 260f);
			// Modal, so the globe underneath cannot be turned to somewhere else while the
			// rectangle that is about to be cut is the one that was drawn.
			window.ShowModalUtility();

			fineDetail = window.fineDetail;
			string result = window.accepted ? window.sceneName : null;
			DestroyImmediate(window);
			return result;
		}

		private void OnGUI()
		{
			if (done)
			{
				return;
			}

			EditorGUILayout.Space(6f);
			EditorGUILayout.LabelField(details, EditorStyles.wordWrappedLabel);
			EditorGUILayout.Space(8f);

			GUI.SetNextControlName("SceneName");
			sceneName = EditorGUILayout.TextField("Scene name", sceneName);
			if (Event.current.type == EventType.Repaint)
			{
				GUI.FocusControl("SceneName");
			}

			string problem = SceneGeneration.NameProblem(sceneName, SceneGenerator.ExistingSceneNames());
			if (problem != null)
			{
				EditorGUILayout.HelpBox(problem, MessageType.Warning);
			}

			EditorGUILayout.Space(4f);
			fineDetail = EditorGUILayout.ToggleLeft(
				new GUIContent("Add local detail",
					"The planet's own shape is continent-scale and very smooth across a few kilometres. " +
					"This adds metre-scale hills and roughness on top. It averages to nothing, so the " +
					"scene still sits exactly where the globe says it does."),
				fineDetail);

			EditorGUILayout.Space(10f);
			using (new EditorGUILayout.HorizontalScope())
			{
				GUILayout.FlexibleSpace();
				if (GUILayout.Button("Cancel", GUILayout.Width(90f)))
				{
					Close();
				}
				using (new EditorGUI.DisabledScope(problem != null))
				{
					if (GUILayout.Button("Generate", GUILayout.Width(110f)))
					{
						accepted = true;
						done = true;
						Close();
					}
				}
			}

			// Enter accepts a usable name, Escape cancels; a modal window somebody cannot dismiss
			// from the keyboard is the one they force-quit the editor to escape.
			if (Event.current.type == EventType.KeyDown)
			{
				if (Event.current.keyCode == KeyCode.Escape)
				{
					Close();
				}
				else if ((Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter) && problem == null)
				{
					accepted = true;
					done = true;
					Close();
				}
			}
		}
	}
}
#endif
