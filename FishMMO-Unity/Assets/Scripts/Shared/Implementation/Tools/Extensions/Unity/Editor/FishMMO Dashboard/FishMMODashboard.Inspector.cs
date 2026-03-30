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
		/// Shows the Game Settings (Constants.cs) as a read-only view in the inspector panel.
		/// </summary>
		private void ShowGameSettingsInspector()
		{
			ClearInspector();

			if (inspectorHeader != null)
			{
				inspectorHeader.text = "Game Settings (Constants.cs)";
			}

			// ── Configuration Section ──
			VisualElement configSection = CreateConstantsSection("Configuration");
			AddConstantRow(configSection, "Project Name", Constants.Configuration.ProjectName);
			AddConstantRow(configSection, "Client Executable", Constants.Configuration.ClientExecutable);
			AddConstantRow(configSection, "Updater Executable", Constants.Configuration.UpdaterExecutable);
			AddConstantRow(configSection, "Setup Directory", Constants.Configuration.SetupDirectory);
			AddConstantRow(configSection, "API Host", Constants.Configuration.APIHost);
			AddConstantRow(configSection, "Game Host", Constants.Configuration.GameHost);
			AddConstantRow(configSection, "Scene Path", Constants.Configuration.ScenePath);
			AddConstantRow(configSection, "Bootstrap Scene Path", Constants.Configuration.BootstrapScenePath);
			AddConstantRow(configSection, "Client Bootstrap Scene Path", Constants.Configuration.ClientBootstrapScenePath);
			AddConstantRow(configSection, "Server Bootstrap Scene Path", Constants.Configuration.ServerBootstrapScenePath);
			AddConstantRow(configSection, "World Scene Path", Constants.Configuration.WorldScenePath);
			AddConstantRow(configSection, "Local Scene Path", Constants.Configuration.LocalScenePath);
			AddConstantRow(configSection, "Maximum Player Hotkeys", Constants.Configuration.MaximumPlayerHotkeys.ToString());
			inspectorContent.Add(configSection);

			// ── Character Section ──
			VisualElement charSection = CreateConstantsSection("Character");
			AddConstantRow(charSection, "Walk Speed", Constants.Character.WalkSpeed.ToString("F1"));
			AddConstantRow(charSection, "Run Speed", Constants.Character.RunSpeed.ToString("F1"));
			AddConstantRow(charSection, "Sprint Speed", Constants.Character.SprintSpeed.ToString("F1"));
			AddConstantRow(charSection, "Sprint Stamina Cost", Constants.Character.SprintStaminaCost.ToString("F1"));
			AddConstantRow(charSection, "Crouch Speed", Constants.Character.CrouchSpeed.ToString("F1"));
			AddConstantRow(charSection, "Jump Up Speed", Constants.Character.JumpUpSpeed.ToString("F1"));
			AddConstantRow(charSection, "Jump Stamina Cost", Constants.Character.JumpStaminaCost.ToString("F1"));
			AddConstantRow(charSection, "Gravity", Constants.Character.Gravity.ToString());
			inspectorContent.Add(charSection);

			// ── Layers Section ──
			VisualElement layerSection = CreateConstantsSection("Layers");
			AddConstantRow(layerSection, "Default", Constants.Layers.Default.ToString());
			AddConstantRow(layerSection, "Ignore Raycast", Constants.Layers.IgnoreRaycast.ToString());
			AddConstantRow(layerSection, "Ground", Constants.Layers.Ground.ToString());
			AddConstantRow(layerSection, "Obstruction", Constants.Layers.Obstruction.ToString());
			AddConstantRow(layerSection, "Player", Constants.Layers.Player.ToString());
			inspectorContent.Add(layerSection);

			// ── Misc ──
			VisualElement miscSection = CreateConstantsSection("Misc");
			AddConstantRow(miscSection, "Shared Static Label", Constants.SharedStaticLabel);
			inspectorContent.Add(miscSection);

			// Info label
			Label infoLabel = new Label("These values are defined in Constants.cs and are read-only.\nEdit Constants.cs directly to change them.");
			infoLabel.style.marginTop = 12;
			infoLabel.style.fontSize = 10;
			infoLabel.style.color = new Color(0.5f, 0.5f, 0.5f, 1f);
			infoLabel.style.whiteSpace = WhiteSpace.Normal;
			inspectorContent.Add(infoLabel);
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