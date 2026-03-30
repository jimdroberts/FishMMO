#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.UIElements;
using BuildTool = FishMMO.Shared.CustomBuildTool.Core.CustomBuildTool;

namespace FishMMO.Shared
{
	/// <summary>
	/// Partial class handling Build and Version inspector panels for the FishMMO Dashboard.
	/// </summary>
	public partial class FishMMODashboard
	{
		/// <summary>
		/// Cached VersionConfig asset reference for the Version panel.
		/// </summary>
		private VersionConfig cachedVersionConfig;

		// ────────────────────────────────────────────
		//  BUILD PANEL
		// ────────────────────────────────────────────

		/// <summary>
		/// Shows the Build configuration panel in the inspector area.
		/// Exposes OS Target, Build Type, Environment selection, and Build/Build Addressables buttons.
		/// </summary>
		private void ShowBuildInspector()
		{
			ClearInspector();

			if (inspectorHeader != null)
			{
				inspectorHeader.text = "Build Configuration";
			}

			// ── Current Settings Section ──
			VisualElement currentSection = CreateConstantsSection("Current Build Settings");

			BuildTypeEnvironment buildType = BuildEnvironmentOptions.GetBuildType();
			OSTargetEnvironment osTarget = BuildEnvironmentOptions.GetOSTarget();
			WorkingEnvironmentState envState = WorkingEnvironmentOptions.GetWorkingEnvironmentState();

			AddConstantRow(currentSection, "Build Type", buildType.ToString());
			AddConstantRow(currentSection, "OS Target", GetOSTargetDisplayName(osTarget));
			AddConstantRow(currentSection, "Environment", envState.ToString());
			AddConstantRow(currentSection, "Active Build Target", EditorUserBuildSettings.activeBuildTarget.ToString());
			AddConstantRow(currentSection, "Build Subtarget", EditorUserBuildSettings.standaloneBuildSubtarget.ToString());

			inspectorContent.Add(currentSection);

			// ── Build Type Selection ──
			VisualElement buildTypeSection = CreateConstantsSection("Build Type");

			EnumField buildTypeField = new EnumField("Build Type", buildType);
			buildTypeField.style.marginBottom = 4;
			buildTypeField.RegisterValueChangedCallback(evt =>
			{
				BuildTypeEnvironment newVal = (BuildTypeEnvironment)evt.newValue;
				EditorPrefs.SetInt("FishMMOBuildType", (int)newVal);
				SetStatus($"Switching to: {newVal}...");
				BuildEnvironmentOptions.SwitchToEnvironmentBuildTarget();
				EditorApplication.delayCall += () => ShowBuildInspector();
			});
			buildTypeSection.Add(buildTypeField);
			inspectorContent.Add(buildTypeSection);

			// ── OS Target Selection ──
			VisualElement osSection = CreateConstantsSection("OS Target");

			EnumField osField = new EnumField("OS Target", osTarget);
			osField.style.marginBottom = 4;
			osField.RegisterValueChangedCallback(evt =>
			{
				OSTargetEnvironment newVal = (OSTargetEnvironment)evt.newValue;
				EditorPrefs.SetInt("FishMMOOSTarget", (int)newVal);
				SetStatus($"Switching to: {GetOSTargetDisplayName(newVal)}...");
				BuildEnvironmentOptions.SwitchToEnvironmentBuildTarget();
				EditorApplication.delayCall += () => ShowBuildInspector();
			});
			osSection.Add(osField);
			inspectorContent.Add(osSection);

			// ── Environment Selection ──
			VisualElement envSection = CreateConstantsSection("Environment");

			EnumField envField = new EnumField("Working Environment", envState);
			envField.style.marginBottom = 4;
			envField.RegisterValueChangedCallback(evt =>
			{
				WorkingEnvironmentState newVal = (WorkingEnvironmentState)evt.newValue;
				EditorPrefs.SetInt("FishMMOWorkingEnvironmentToggle", (int)newVal);
				SetStatus($"Environment set to: {newVal}");
				ShowBuildInspector();
			});
			envSection.Add(envField);

			Toggle localDirToggle = new Toggle("Enable Local Directory");
			localDirToggle.value = EditorPrefs.GetBool("FishMMOEnableLocalDirectory", false);
			localDirToggle.RegisterValueChangedCallback(evt =>
			{
				EditorPrefs.SetBool("FishMMOEnableLocalDirectory", evt.newValue);
				SetStatus($"Local Directory: {(evt.newValue ? "Enabled" : "Disabled")}");
			});
			envSection.Add(localDirToggle);
			inspectorContent.Add(envSection);

			// ── Apply Platform Button ──
			Button applyPlatformButton = new Button(() =>
			{
				if (EditorApplication.isCompiling)
				{
					EditorUtility.DisplayDialog("Please Wait", "Scripts are currently compiling.", "OK");
					return;
				}
				BuildEnvironmentOptions.SwitchToEnvironmentBuildTarget();
				SetStatus("Switching build platform...");
				// Delayed refresh after platform switch
				EditorApplication.delayCall += () => ShowBuildInspector();
			});
			applyPlatformButton.text = "Apply Platform Settings";
			applyPlatformButton.style.height = 28;
			applyPlatformButton.style.marginTop = 8;
			applyPlatformButton.style.backgroundColor = new Color(0.25f, 0.35f, 0.55f, 1f);
			applyPlatformButton.style.color = new Color(0.75f, 0.85f, 1f, 1f);
			applyPlatformButton.style.borderTopLeftRadius = 4;
			applyPlatformButton.style.borderTopRightRadius = 4;
			applyPlatformButton.style.borderBottomLeftRadius = 4;
			applyPlatformButton.style.borderBottomRightRadius = 4;
			inspectorContent.Add(applyPlatformButton);

			// ── Build Buttons ──
			VisualElement buildButtonSection = CreateConstantsSection("Actions");

			Button buildAddressablesButton = new Button(() =>
			{
				if (EditorApplication.isCompiling)
				{
					EditorUtility.DisplayDialog("Build Blocked", "Scripts are currently compiling.\nPlease wait.", "OK");
					return;
				}

				if (!EditorUtility.DisplayDialog("Build Addressables",
					$"Build addressables for {BuildEnvironmentOptions.GetBuildType()} / {GetOSTargetDisplayName(BuildEnvironmentOptions.GetOSTarget())}?",
					"Build", "Cancel"))
				{
					return;
				}

				SetStatus("Building Addressables...");
				BuildTool.BuildAddressablesWithEnvironmentOptions();
				SetStatus("Addressables build complete.");
			});
			buildAddressablesButton.text = "Build Addressables";
			buildAddressablesButton.style.height = 28;
			buildAddressablesButton.style.marginTop = 4;
			buildAddressablesButton.style.backgroundColor = new Color(0.35f, 0.45f, 0.25f, 1f);
			buildAddressablesButton.style.color = new Color(0.8f, 0.95f, 0.7f, 1f);
			buildAddressablesButton.style.borderTopLeftRadius = 4;
			buildAddressablesButton.style.borderTopRightRadius = 4;
			buildAddressablesButton.style.borderBottomLeftRadius = 4;
			buildAddressablesButton.style.borderBottomRightRadius = 4;
			buildButtonSection.Add(buildAddressablesButton);

			Button buildGameButton = new Button(() =>
			{
				if (EditorApplication.isCompiling)
				{
					EditorUtility.DisplayDialog("Build Blocked", "Scripts are currently compiling.\nPlease wait.", "OK");
					return;
				}

				BuildTypeEnvironment bt = BuildEnvironmentOptions.GetBuildType();
				OSTargetEnvironment os = BuildEnvironmentOptions.GetOSTarget();

				if (!EditorUtility.DisplayDialog("Build Game",
					$"Build {bt} for {GetOSTargetDisplayName(os)}?\n\nThis may take several minutes.",
					"Build", "Cancel"))
				{
					return;
				}

				SetStatus("Building Game...");
				BuildTool.BuildGameWithEnvironmentOptions();
				SetStatus("Game build complete.");
			});
			buildGameButton.text = "Build Game";
			buildGameButton.style.height = 32;
			buildGameButton.style.marginTop = 4;
			buildGameButton.style.backgroundColor = new Color(0.4f, 0.28f, 0.12f, 1f);
			buildGameButton.style.color = new Color(1f, 0.82f, 0.55f, 1f);
			buildGameButton.style.borderTopLeftRadius = 4;
			buildGameButton.style.borderTopRightRadius = 4;
			buildGameButton.style.borderBottomLeftRadius = 4;
			buildGameButton.style.borderBottomRightRadius = 4;
			buildGameButton.style.fontSize = 13;
			buildGameButton.style.unityFontStyleAndWeight = FontStyle.Bold;
			buildButtonSection.Add(buildGameButton);

			Button updateLinkerButton = new Button(() =>
			{
				BuildTool.UpdateLinker();
				SetStatus("Linker updated.");
			});
			updateLinkerButton.text = "Update Linker";
			updateLinkerButton.style.height = 24;
			updateLinkerButton.style.marginTop = 4;
			buildButtonSection.Add(updateLinkerButton);

			inspectorContent.Add(buildButtonSection);
		}

		/// <summary>
		/// Returns a friendly display name for the OS target.
		/// </summary>
		private static string GetOSTargetDisplayName(OSTargetEnvironment os)
		{
			switch (os)
			{
				case OSTargetEnvironment.Windows: return "Windows x64";
				case OSTargetEnvironment.Linux: return "Linux x64";
				case OSTargetEnvironment.WebGL: return "WebGL";
				default: return os.ToString();
			}
		}

		// ────────────────────────────────────────────
		//  VERSION PANEL
		// ────────────────────────────────────────────

		/// <summary>
		/// Shows the Version configuration panel. Finds or creates the VersionConfig asset
		/// and ensures it is registered as an Addressable under the Shared_Static_Permanent group/label.
		/// </summary>
		private void ShowVersionInspector()
		{
			ClearInspector();

			if (inspectorHeader != null)
			{
				inspectorHeader.text = "Version Configuration";
			}

			// Find or create VersionConfig
			VersionConfig config = FindOrCreateVersionConfig();
			if (config == null)
			{
				Label errorLabel = new Label("Failed to find or create VersionConfig asset.");
				errorLabel.AddToClassList("empty-state-label");
				inspectorContent.Add(errorLabel);
				return;
			}

			cachedVersionConfig = config;

			// ── Current Version Display ──
			VisualElement versionSection = CreateConstantsSection("Current Version");

			Label versionLabel = new Label(config.FullVersion);
			versionLabel.style.fontSize = 24;
			versionLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
			versionLabel.style.color = new Color(0.6f, 0.85f, 1f, 1f);
			versionLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
			versionLabel.style.marginTop = 8;
			versionLabel.style.marginBottom = 8;
			versionSection.Add(versionLabel);

			AddConstantRow(versionSection, "Major", config.Major.ToString());
			AddConstantRow(versionSection, "Minor", config.Minor.ToString());
			AddConstantRow(versionSection, "Patch", config.Patch.ToString());
			AddConstantRow(versionSection, "Pre-Release", string.IsNullOrEmpty(config.PreRelease) ? "(none)" : config.PreRelease);

			inspectorContent.Add(versionSection);

			// ── Increment Buttons ──
			VisualElement incrementSection = CreateConstantsSection("Version Actions");

			Button incrementMajorButton = CreateVersionButton("Increment Major", new Color(0.5f, 0.2f, 0.2f, 1f), new Color(1f, 0.7f, 0.7f, 1f), () =>
			{
				Undo.RecordObject(config, "Increment Major Version");
				config.Major++;
				config.Minor = 0;
				config.Patch = 0;
				SaveVersionConfig(config);
				ShowVersionInspector();
			});
			incrementSection.Add(incrementMajorButton);

			Button incrementMinorButton = CreateVersionButton("Increment Minor", new Color(0.35f, 0.35f, 0.2f, 1f), new Color(1f, 1f, 0.7f, 1f), () =>
			{
				Undo.RecordObject(config, "Increment Minor Version");
				config.Minor++;
				config.Patch = 0;
				SaveVersionConfig(config);
				ShowVersionInspector();
			});
			incrementSection.Add(incrementMinorButton);

			Button incrementPatchButton = CreateVersionButton("Increment Patch", new Color(0.2f, 0.35f, 0.2f, 1f), new Color(0.7f, 1f, 0.7f, 1f), () =>
			{
				Undo.RecordObject(config, "Increment Patch Version");
				config.Patch++;
				SaveVersionConfig(config);
				ShowVersionInspector();
			});
			incrementSection.Add(incrementPatchButton);

			// Pre-release field
			TextField preReleaseField = new TextField("Pre-Release Tag");
			preReleaseField.value = config.PreRelease ?? "";
			preReleaseField.style.marginTop = 8;
			preReleaseField.RegisterValueChangedCallback(evt =>
			{
				Undo.RecordObject(config, "Change Pre-Release Tag");
				config.PreRelease = evt.newValue;
				SaveVersionConfig(config);
			});
			incrementSection.Add(preReleaseField);

			// Reset button
			Button resetButton = CreateVersionButton("Reset to 0.0.0", new Color(0.4f, 0.15f, 0.15f, 1f), new Color(1f, 0.6f, 0.6f, 1f), () =>
			{
				if (EditorUtility.DisplayDialog("Reset Version",
					"Reset the version to 0.0.0?\nThis cannot be undone.",
					"Reset", "Cancel"))
				{
					Undo.RecordObject(config, "Reset Version");
					config.Major = 0;
					config.Minor = 0;
					config.Patch = 0;
					config.PreRelease = "";
					SaveVersionConfig(config);
					ShowVersionInspector();
				}
			});
			resetButton.style.marginTop = 12;
			incrementSection.Add(resetButton);

			inspectorContent.Add(incrementSection);

			// ── Addressable Status ──
			VisualElement addrSection = CreateConstantsSection("Addressable Status");
			string assetPath = AssetDatabase.GetAssetPath(config);
			bool isAddressable = IsAssetAddressable(assetPath);

			AddConstantRow(addrSection, "Asset Path", assetPath);
			AddConstantRow(addrSection, "Is Addressable", isAddressable ? "Yes" : "No");

			if (isAddressable)
			{
				AddConstantRow(addrSection, "Group", Constants.SharedStaticLabel);
				AddConstantRow(addrSection, "Label", Constants.SharedStaticLabel);
			}
			else
			{
				Button makeAddressableButton = new Button(() =>
				{
					EnsureVersionConfigAddressable(config);
					ShowVersionInspector();
				});
				makeAddressableButton.text = "Make Addressable";
				makeAddressableButton.style.height = 24;
				makeAddressableButton.style.marginTop = 4;
				makeAddressableButton.style.backgroundColor = new Color(0.35f, 0.25f, 0.5f, 1f);
				makeAddressableButton.style.color = new Color(0.85f, 0.75f, 1f, 1f);
				makeAddressableButton.style.borderTopLeftRadius = 4;
				makeAddressableButton.style.borderTopRightRadius = 4;
				makeAddressableButton.style.borderBottomLeftRadius = 4;
				makeAddressableButton.style.borderBottomRightRadius = 4;
				addrSection.Add(makeAddressableButton);
			}

			inspectorContent.Add(addrSection);

			// ── Select in Project button ──
			Button selectButton = new Button(() =>
			{
				Selection.activeObject = config;
				EditorGUIUtility.PingObject(config);
			});
			selectButton.text = "Select in Project";
			selectButton.style.marginTop = 8;
			selectButton.style.height = 24;
			inspectorContent.Add(selectButton);
		}

		/// <summary>
		/// Creates a styled version action button.
		/// </summary>
		private Button CreateVersionButton(string text, Color bgColor, Color textColor, System.Action onClick)
		{
			Button button = new Button(onClick);
			button.text = text;
			button.style.height = 28;
			button.style.marginTop = 4;
			button.style.backgroundColor = bgColor;
			button.style.color = textColor;
			button.style.borderTopLeftRadius = 4;
			button.style.borderTopRightRadius = 4;
			button.style.borderBottomLeftRadius = 4;
			button.style.borderBottomRightRadius = 4;
			return button;
		}

		/// <summary>
		/// Finds an existing VersionConfig asset or creates one in Assets/.
		/// </summary>
		private VersionConfig FindOrCreateVersionConfig()
		{
			// Search for existing VersionConfig assets
			string[] guids = AssetDatabase.FindAssets("t:VersionConfig");
			if (guids.Length > 0)
			{
				string path = AssetDatabase.GUIDToAssetPath(guids[0]);
				VersionConfig config = AssetDatabase.LoadAssetAtPath<VersionConfig>(path);
				if (config != null)
				{
					EnsureVersionConfigAddressable(config);
					return config;
				}
			}

			// Create a new one
			VersionConfig newConfig = ScriptableObject.CreateInstance<VersionConfig>();
			string assetPath = "Assets/VersionConfig.asset";
			assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);

			AssetDatabase.CreateAsset(newConfig, assetPath);
			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();

			newConfig = AssetDatabase.LoadAssetAtPath<VersionConfig>(assetPath);
			EnsureVersionConfigAddressable(newConfig);

			SetStatus($"Created VersionConfig at: {assetPath}");
			return newConfig;
		}

		/// <summary>
		/// Ensures the VersionConfig asset is registered as Addressable under the Shared_Static_Permanent group and label.
		/// </summary>
		private void EnsureVersionConfigAddressable(VersionConfig config)
		{
			if (config == null) return;

			string assetPath = AssetDatabase.GetAssetPath(config);
			if (string.IsNullOrEmpty(assetPath)) return;

			AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
			if (settings == null) return;

			string guid = AssetDatabase.AssetPathToGUID(assetPath);
			AddressableAssetEntry entry = settings.FindAssetEntry(guid);

			if (entry != null) return; // Already addressable

			// Find or create the Shared_Static_Permanent group
			AddressableAssetGroup group = null;
			foreach (var g in settings.groups)
			{
				if (g != null && g.Name == Constants.SharedStaticLabel)
				{
					group = g;
					break;
				}
			}

			if (group == null)
			{
				group = settings.CreateGroup(Constants.SharedStaticLabel, false, false, true, null);
			}

			// Add entry to the group
			entry = settings.CreateOrMoveEntry(guid, group, false, false);

			if (entry != null)
			{
				// Add the label
				settings.AddLabel(Constants.SharedStaticLabel, false);
				entry.SetLabel(Constants.SharedStaticLabel, true, false);
				entry.address = "VersionConfig";

				EditorUtility.SetDirty(settings);
				AssetDatabase.SaveAssets();
			}
		}

		/// <summary>
		/// Checks whether the asset at the given path is registered as an Addressable.
		/// </summary>
		private bool IsAssetAddressable(string assetPath)
		{
			if (string.IsNullOrEmpty(assetPath)) return false;

			AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.GetSettings(false);
			if (settings == null) return false;

			string guid = AssetDatabase.AssetPathToGUID(assetPath);
			return settings.FindAssetEntry(guid) != null;
		}

		/// <summary>
		/// Marks the VersionConfig dirty and saves it.
		/// </summary>
		private void SaveVersionConfig(VersionConfig config)
		{
			if (config == null) return;

			EditorUtility.SetDirty(config);
			AssetDatabase.SaveAssetIfDirty(config);
			SetStatus($"Version: {config.FullVersion}");
		}
	}
}
#endif