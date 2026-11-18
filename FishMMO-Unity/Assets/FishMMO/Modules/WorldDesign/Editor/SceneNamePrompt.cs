#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Celestial;

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

		/// <summary>The ground the rectangle sits on, for naming it. Null disables the button.</summary>
		private Func<SuggestedSceneName> suggest;
		private SuggestedSceneName suggestion;
		private bool suggested;

		/// <summary>
		/// Asks for a name. Returns null if it was cancelled.
		/// </summary>
		/// <param name="details">What is about to be cut, shown above the field.</param>
		/// <param name="suggestion">The name the field opens with.</param>
		/// <param name="fineDetail">Whether local detail was asked for.</param>
		/// <param name="suggest">
		/// Draws a name from the ground being cut. Null hides the button — for a caller that has no
		/// body to ask about.
		/// </param>
		public static string Ask(string details, string suggestion, out bool fineDetail,
			Func<SuggestedSceneName> suggest = null)
		{
			var window = CreateInstance<SceneNamePrompt>();
			window.titleContent = new GUIContent("Cut scene");
			window.details = details;
			window.sceneName = suggestion;
			window.suggest = suggest;
			window.minSize = new Vector2(460f, 300f);
			window.maxSize = new Vector2(460f, 300f);
			// Modal, so the globe underneath cannot be turned to somewhere else while the
			// rectangle that is about to be cut is the one that was drawn.
			window.ShowModalUtility();

			fineDetail = window.fineDetail;
			string result = window.accepted ? window.sceneName : null;
			DestroyImmediate(window);
			return result;
		}

		/// <summary>
		/// Convenience for the atlas: names the ground at a point on a body.
		/// </summary>
		public static Func<SuggestedSceneName> NamerFor(WorldBody body, WorldAtlasLayer layer, double latitude, double longitude)
		{
			if (body == null)
			{
				return null;
			}
			return () => GeneratedSceneNames.Suggest(body, layer, latitude, longitude, SceneGenerator.ExistingSceneNames());
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

			using (new EditorGUILayout.HorizontalScope())
			{
				GUI.SetNextControlName("SceneName");
				sceneName = EditorGUILayout.TextField("Scene name", sceneName);
				using (new EditorGUI.DisabledScope(suggest == null))
				{
					if (GUILayout.Button(new GUIContent("Generate",
							"Names this scene after the ground under the rectangle: the planet says which biome is " +
							"there, and the name generator says what such a place is called. Press again for another."),
						GUILayout.Width(80f)))
					{
						Draw();
					}
				}
			}
			if (Event.current.type == EventType.Repaint && !suggested)
			{
				GUI.FocusControl("SceneName");
			}

			string problem = SceneGeneration.NameProblem(sceneName, SceneGenerator.ExistingSceneNames());
			if (problem != null)
			{
				EditorGUILayout.HelpBox(problem, MessageType.Warning);
			}
			else if (suggested && !string.IsNullOrEmpty(suggestion.Problem))
			{
				EditorGUILayout.HelpBox(suggestion.Problem, MessageType.Warning);
			}
			else if (suggested && !string.IsNullOrEmpty(suggestion.Biome))
			{
				EditorGUILayout.HelpBox(
					string.IsNullOrEmpty(suggestion.Variant)
						? $"Named from the {suggestion.Biome} under the rectangle."
						: $"Named from the {suggestion.Variant.ToLowerInvariant()} {suggestion.Biome} under the rectangle.",
					MessageType.None);
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
					if (GUILayout.Button("Cut scene", GUILayout.Width(110f)))
					{
						Accept();
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
					Accept();
				}
			}
		}

		private void Accept()
		{
			accepted = true;
			done = true;
			Close();
		}

		/// <summary>
		/// Draws a name from the ground and puts it in the field.
		/// </summary>
		/// <remarks>
		/// Keyboard focus is taken off the text field first. A UI that writes into a control the
		/// user is editing is one where the old text comes back the moment they type, because IMGUI
		/// keeps the focused field's own editing buffer and it wins over the value passed in.
		/// </remarks>
		private void Draw()
		{
			GUI.FocusControl(null);
			GUIUtility.keyboardControl = 0;
			try
			{
				suggestion = suggest();
			}
			catch (Exception ex)
			{
				suggestion = new SuggestedSceneName { Problem = $"Naming threw: {ex.Message}" };
				Debug.LogException(ex);
			}
			suggested = true;
			if (suggestion.Usable)
			{
				sceneName = suggestion.Name;
			}
			Repaint();
		}
	}
}
#endif
