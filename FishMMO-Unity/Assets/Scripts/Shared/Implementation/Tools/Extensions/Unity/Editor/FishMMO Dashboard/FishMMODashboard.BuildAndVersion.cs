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


		// Build panel controls — stored so they can be disabled during background tasks.
		private Button buildApplyPlatformButton;
		private Button buildAddressablesButton;
		private Button buildGameButton;
		private Button buildUpdateLinkerButton;
		private EnumField buildTypeField;
		private EnumField osTargetField;
		private EnumField envField;
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
			this.buildTypeField = buildTypeField;
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
			this.osTargetField = osField;
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
			this.envField = envField;
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

				if (!evt.newValue)
				{
					RemoveLocalAddressableEntries();
				}

				SetStatus($"Local Directory: {(evt.newValue ? "Enabled" : "Disabled")}");
			});
			envSection.Add(localDirToggle);
			inspectorContent.Add(envSection);

			// ── Server Build Options ──
			VisualElement serverSection = CreateConstantsSection("Server Build Options");

			Toggle serverIl2cppToggle = new Toggle("Dedicated Server: Use IL2CPP");
			serverIl2cppToggle.tooltip =
				"When enabled, the dedicated-server build uses the IL2CPP scripting backend " +
				"(matches the client; produces il2cpp_data/ folders, larger but harder to decompile). " +
				"When disabled, the server build uses the Mono2x backend (faster build, smaller output, " +
				"managed assemblies are present and decompilable).";
			bool serverUsesIl2Cpp = EditorPrefs.GetBool(BuildEnvironmentOptions.PREF_SERVER_USE_IL2CPP, false);
			serverIl2cppToggle.value = serverUsesIl2Cpp;
			BuildEnvironmentOptions.ApplyServerScriptingBackend(serverUsesIl2Cpp);
			serverIl2cppToggle.RegisterValueChangedCallback(evt =>
			{
				EditorPrefs.SetBool(BuildEnvironmentOptions.PREF_SERVER_USE_IL2CPP, evt.newValue);
				BuildEnvironmentOptions.ApplyServerScriptingBackend(evt.newValue);
				SetStatus($"Server scripting backend: {(evt.newValue ? "IL2CPP" : "Mono2x")}");
			});
			serverSection.Add(serverIl2cppToggle);
			inspectorContent.Add(serverSection);

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
			this.buildApplyPlatformButton = applyPlatformButton;
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
				EditorApplication.delayCall += () =>
				{
					BuildTool.BuildAddressablesWithEnvironmentOptions();
					SetStatus("Addressables build complete.");
				};
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
			this.buildAddressablesButton = buildAddressablesButton;
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
				EditorApplication.delayCall += () =>
				{
					BuildTool.BuildGameWithEnvironmentOptions();
					SetStatus("Game build complete.");
				};
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
			this.buildGameButton = buildGameButton;
			buildButtonSection.Add(buildGameButton);

			Button updateLinkerButton = new Button(() =>
			{
				BuildTool.UpdateLinker();
				SetStatus("Linker updated.");
			});
			updateLinkerButton.text = "Update Linker";
			updateLinkerButton.style.height = 24;
			updateLinkerButton.style.marginTop = 4;
			this.buildUpdateLinkerButton = updateLinkerButton;
			buildButtonSection.Add(updateLinkerButton);

			inspectorContent.Add(buildButtonSection);

			// ── Background Tasks Section ──
			VisualElement tasksSection = CreateConstantsSection("Background Tasks");

			var tasksContainer = new VisualElement();
			tasksContainer.name = "background-tasks-container";
			tasksSection.Add(tasksContainer);

			// Poll Unity's global progress system for active background tasks
			// (Compiling Scripts, Importing Assets, Baking, etc.)
			tasksContainer.schedule.Execute(() =>
			{
				RefreshBackgroundTasks(tasksContainer);
				UpdateBuildControlStates();
			}).Every(500);

			inspectorContent.Add(tasksSection);

			// Set initial control state so buttons reflect any active tasks
			// immediately when the Build panel is first shown.
			UpdateBuildControlStates();
		}

		/// <summary>
		/// Returns true when Unity is running background tasks that should block
		/// build operations (compilation, asset import, Progress API tasks, or
		/// an active patcher operation).
		/// </summary>
		private bool IsBackgroundTaskActive()
		{
			if (EditorApplication.isCompiling) return true;
			if (EditorApplication.isUpdating) return true;
			if (patcherIsProcessing) return true;
			try { return Progress.GetRunningProgressCount() > 0; }
			catch { return false; }
		}

		/// <summary>
		/// Enables or disables all build action controls based on background task state.
		/// Called periodically and immediately after control creation so buttons
		/// are greyed out before the user can click them during compilation etc.
		/// </summary>
		private void UpdateBuildControlStates()
		{
			bool blocked = IsBackgroundTaskActive();

			buildApplyPlatformButton?.SetEnabled(!blocked);
			buildAddressablesButton?.SetEnabled(!blocked);
			buildGameButton?.SetEnabled(!blocked);
			buildUpdateLinkerButton?.SetEnabled(!blocked);
			buildTypeField?.SetEnabled(!blocked);
			osTargetField?.SetEnabled(!blocked);
			envField?.SetEnabled(!blocked);
		}

		/// <summary>
		/// Refreshes the background tasks display by querying Unity's global Progress API.
		/// Shows active tasks with progress bars and descriptions.
		/// </summary>
		private static void RefreshBackgroundTasks(VisualElement container)
		{
			container.Clear();

			bool hasActive = false;

			// ── Script Compilation ──
			if (EditorApplication.isCompiling)
			{
				hasActive = true;
				AddTaskRow(container, "Compiling Scripts", -1f, "C# scripts are compiling...");
			}

			// ── Asset Import ──
			if (EditorApplication.isUpdating)
			{
				hasActive = true;
				AddTaskRow(container, "Importing Assets", -1f, "Asset database is updating...");
			}

			// ── Unity Progress API (any other registered background tasks) ──
			try
			{
				int runningCount = Progress.GetRunningProgressCount();
				if (runningCount > 0)
				{
					hasActive = true;
					AddTaskRow(container, "Background Tasks", -1f, $"{runningCount} task(s) running...");
				}
			}
			catch
			{
				// Progress API unavailable on this Unity version; isCompiling/isUpdating
				// already cover the most important cases.
			}

			if (!hasActive)
			{
				Label emptyLabel = new Label("No active tasks");
				emptyLabel.style.color = new Color(0.4f, 0.4f, 0.4f, 1f);
				emptyLabel.style.fontSize = 11;
				emptyLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
				emptyLabel.style.paddingTop = 4;
				emptyLabel.style.paddingBottom = 4;
				container.Add(emptyLabel);
			}
		}

		private static void AddTaskRow(VisualElement container, string name, float progress, string description)
		{
			VisualElement row = new VisualElement();
			row.style.flexDirection = FlexDirection.Row;
			row.style.alignItems = Align.Center;
			row.style.paddingTop = 2;
			row.style.paddingBottom = 2;

			// Name label
			Label nameLabel = new Label(name);
			nameLabel.style.width = 140;
			nameLabel.style.color = new Color(0.65f, 0.75f, 0.9f, 1f);
			nameLabel.style.fontSize = 11;
			nameLabel.style.unityTextAlign = TextAnchor.MiddleRight;
			nameLabel.style.marginRight = 6;
			row.Add(nameLabel);

			// Progress bar
			ProgressBar bar = new ProgressBar();
			bar.style.flexGrow = 1;
			bar.value = progress * 100f;
			string descText = string.IsNullOrEmpty(description) ? "" : $" — {description}";
			bar.title = progress > 0f ? $"{progress * 100f:F0}%{descText}" : descText.TrimStart(' ', '—', ' ');
			row.Add(bar);

			container.Add(row);
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

		/// <summary>
		/// Removes all Addressable entries whose asset path is under Assets/LOCAL/.
		/// Called when the Enable Local Directory toggle is turned off so that
		/// LOCAL assets are no longer included in Addressable builds.
		/// </summary>
		private static void RemoveLocalAddressableEntries()
		{
			var settings = AddressableAssetSettingsDefaultObject.Settings;
			if (settings == null) return;

			int removed = 0;

			foreach (var group in settings.groups)
			{
				if (group == null) continue;

				// Collect entries to remove (cannot modify collection while iterating)
				var toRemove = new System.Collections.Generic.List<AddressableAssetEntry>();
				foreach (var entry in group.entries)
				{
					if (entry == null) continue;
					string normalized = entry.AssetPath.Replace('\\', '/');
					if (normalized.StartsWith("Assets/LOCAL/", System.StringComparison.OrdinalIgnoreCase))
					{
						toRemove.Add(entry);
					}
				}

				for (int i = 0; i < toRemove.Count; i++)
				{
					group.RemoveAssetEntry(toRemove[i]);
					removed++;
				}
			}

			if (removed > 0)
			{
				EditorUtility.SetDirty(settings);
				Debug.Log($"[FishMMO] Removed {removed} LOCAL addressable entry/entries.");
			}
		}
	}
}
#endif