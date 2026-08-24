#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

namespace FishMMO.Shared
{
	public partial class FishMMODashboard
	{
		/// <summary>
		/// Active editor instance for the currently selected asset.
		/// Destroyed when switching assets.
		/// </summary>
		private Editor activeEditor;

		/// <summary>
		/// Clears the inspector panel and destroys the active editor.
		/// </summary>
		private void ClearInspector()
		{
			if (inspectorContent != null)
			{
				inspectorContent.Clear();
			}

			if (inspectorHeader != null)
			{
				inspectorHeader.text = "Inspector";
			}

			DestroyActiveEditor();
		}

		/// <summary>
		/// Destroys the active editor instance if one exists.
		/// </summary>
		private void DestroyActiveEditor()
		{
			if (activeEditor != null)
			{
				DestroyImmediate(activeEditor);
				activeEditor = null;
			}
		}

		/// <summary>
		/// Shows the full Unity inspector for the given asset in the inspector panel.
		/// Uses InspectorElement to render the default (or custom) editor.
		/// </summary>
		private void ShowAssetInspector(UnityEngine.Object asset)
		{
			ClearInspector();

			if (asset == null) return;

			if (inspectorHeader != null)
			{
				inspectorHeader.text = asset.name;
			}

			// Create and cache an Editor for this asset
			activeEditor = Editor.CreateEditor(asset);

			if (activeEditor == null)
			{
				Label errorLabel = new Label($"Cannot create editor for {asset.GetType().Name}");
				errorLabel.AddToClassList("empty-state-label");
				inspectorContent.Add(errorLabel);
				return;
			}

			// Use InspectorElement to embed the full inspector
			InspectorElement inspectorElement = new InspectorElement(activeEditor);
			inspectorContent.Add(inspectorElement);

			// Add a save button below the inspector
			Button saveButton = new Button(() =>
			{
				if (asset != null)
				{
					EditorUtility.SetDirty(asset);
					AssetDatabase.SaveAssetIfDirty(asset);
					SetStatus($"Saved: {asset.name}");
				}
			});
			saveButton.text = "Save Asset";
			saveButton.style.marginTop = 8;
			saveButton.style.height = 28;
			saveButton.style.backgroundColor = new Color(0.24f, 0.43f, 0.24f, 1f);
			saveButton.style.color = new Color(0.75f, 0.95f, 0.75f, 1f);
			saveButton.style.borderTopLeftRadius = 4;
			saveButton.style.borderTopRightRadius = 4;
			saveButton.style.borderBottomLeftRadius = 4;
			saveButton.style.borderBottomRightRadius = 4;
			inspectorContent.Add(saveButton);

			/* Assets with a purpose-built visual editor get a button to open it. A behavior tree
			 * inspected as a flat list of node references is close to unreadable — the structure
			 * is the asset — so the graph editor is the real editing surface and the dashboard
			 * should hand you straight to it. */
			AddSpecialisedEditorButton(asset);

			// Add an "Open in Default Inspector" button
			Button openDefaultButton = new Button(() =>
			{
				Selection.activeObject = asset;
				EditorGUIUtility.PingObject(asset);
			});
			openDefaultButton.text = "Select in Project";
			openDefaultButton.style.marginTop = 4;
			openDefaultButton.style.height = 24;
			inspectorContent.Add(openDefaultButton);
		}


		/// <summary>
		/// Adds an "open in graph editor" button for asset types that have one.
		/// </summary>
		/// <param name="asset">The selected asset.</param>
		private void AddSpecialisedEditorButton(UnityEngine.Object asset)
		{
			AIBehaviorTree behaviorTree = asset as AIBehaviorTree;
			if (behaviorTree == null)
			{
				return;
			}

			Button openGraphButton = new Button(() => BehaviorTreeEditorWindow.Open(behaviorTree));
			openGraphButton.text = "Open Behavior Tree Editor";
			openGraphButton.style.marginTop = 4;
			openGraphButton.style.height = 26;
			openGraphButton.style.backgroundColor = new Color(0.22f, 0.30f, 0.45f, 1f);
			openGraphButton.style.color = new Color(0.80f, 0.88f, 1f, 1f);
			inspectorContent.Add(openGraphButton);
		}

		/// <summary>
		/// Creates a section container with a header label.
		/// </summary>
		private VisualElement CreateConstantsSection(string title)
		{
			VisualElement section = new VisualElement();
			section.AddToClassList("constants-section");

			Label header = new Label(title);
			header.AddToClassList("constants-section-header");
			section.Add(header);

			return section;
		}

		/// <summary>
		/// Adds a key-value row to a constants section.
		/// </summary>
		private void AddConstantRow(VisualElement parent, string key, string value)
		{
			VisualElement row = new VisualElement();
			row.AddToClassList("constants-row");

			Label keyLabel = new Label(key);
			keyLabel.AddToClassList("constants-key");
			row.Add(keyLabel);

			Label valueLabel = new Label(value);
			valueLabel.AddToClassList("constants-value");
			row.Add(valueLabel);

			parent.Add(row);
		}

		/// <summary>
		/// Cleanup when the window is closed.
		/// </summary>
		private void OnDisable()
		{
			DestroyActiveEditor();
		}
	}
}
#endif