#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;
using UnityEngine.UIElements;

namespace FishMMO.Shared
{
	public partial class AddressablesDashboard
	{
		// ──────────────────────────────────────────────
		// Utility
		// ──────────────────────────────────────────────

		/// <summary>
		/// Returns the file size on disk for an asset at the given path.
		/// Returns 0 if the file does not exist.
		/// </summary>
		/// <param name="assetPath">The asset path relative to the project.</param>
		/// <returns>File size in bytes.</returns>
		private static long GetAssetFileSize(string assetPath)
		{
			if (string.IsNullOrEmpty(assetPath)) return 0L;

			try
			{
				var fileInfo = new FileInfo(assetPath);
				return fileInfo.Exists ? fileInfo.Length : 0L;
			}
			catch
			{
				return 0L;
			}
		}

		/// <summary>
		/// Formats a byte count into a human-readable string (B, KB, MB, GB).
		/// </summary>
		/// <param name="bytes">The number of bytes.</param>
		/// <returns>Formatted size string.</returns>
		private static string FormatBytes(long bytes)
		{
			if (bytes < 1024L) return $"{bytes} B";
			if (bytes < 1024L * 1024L) return $"{bytes / 1024.0:F1} KB";
			if (bytes < 1024L * 1024L * 1024L) return $"{bytes / (1024.0 * 1024.0):F1} MB";
			return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
		}

		/// <summary>
		/// Formats the total file size for all entries in a group.
		/// </summary>
		/// <param name="group">The Addressable group.</param>
		/// <returns>Formatted size string for the group.</returns>
		private static string FormatGroupSize(AddressableAssetGroup group)
		{
			long total = 0L;
			foreach (var entry in group.entries)
			{
				if (entry == null) continue;
				total += GetAssetFileSize(entry.AssetPath);
			}
			return FormatBytes(total);
		}

		/// <summary>
		/// Formats the labels of an entry as a comma-separated string.
		/// </summary>
		/// <param name="entry">The asset entry.</param>
		/// <returns>Label string, or empty if none.</returns>
		private static string FormatLabels(AddressableAssetEntry entry)
		{
			if (entry.labels == null || entry.labels.Count == 0) return "";
			return "[" + string.Join(", ", entry.labels) + "]";
		}

		/// <summary>
		/// Logs all direct dependencies of an addressable entry to the console.
		/// </summary>
		/// <param name="entry">The entry to show dependencies for.</param>
		private static void ShowEntryDependencies(AddressableAssetEntry entry)
		{
			if (entry == null) return;

			string[] deps = AssetDatabase.GetDependencies(entry.AssetPath, true);
			var sb = new StringBuilder();
			sb.AppendLine($"[AddressablesDashboard] Dependencies of '{entry.address}' ({deps.Length - 1}):");

			for (int i = 0; i < deps.Length; i++)
			{
				if (string.Equals(deps[i], entry.AssetPath, StringComparison.OrdinalIgnoreCase)) continue;
				sb.AppendLine($"  • {deps[i]}");
			}

			Debug.Log(sb.ToString());
		}

		/// <summary>
		/// Logs all non-addressable references for entries in a specific group.
		/// </summary>
		/// <param name="group">The group to scan.</param>
		private static void FindNonAddressableRefsInGroup(AddressableAssetGroup group)
		{
			if (group == null) return;

			var settings = AddressableAssetSettingsDefaultObject.Settings;
			if (settings == null) return;

			var addressablePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var g in settings.groups)
			{
				if (g == null) continue;
				foreach (var e in g.entries)
				{
					if (e != null) addressablePaths.Add(e.AssetPath);
				}
			}

			var nonAddrRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var entry in group.entries)
			{
				if (entry == null) continue;
				string[] deps = AssetDatabase.GetDependencies(entry.AssetPath, true);
				for (int d = 0; d < deps.Length; d++)
				{
					string dep = deps[d];
					if (string.Equals(dep, entry.AssetPath, StringComparison.OrdinalIgnoreCase)) continue;
					if (!addressablePaths.Contains(dep) &&
						!dep.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase) &&
						!dep.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) &&
						!ShouldSkipLocalAsset(dep))
					{
						nonAddrRefs.Add(dep);
					}
				}
			}

			if (nonAddrRefs.Count == 0)
			{
				Debug.Log($"[AddressablesDashboard] No non-addressable references found in group '{group.Name}'.");
				return;
			}

			var sb = new StringBuilder();
			sb.AppendLine($"[AddressablesDashboard] {nonAddrRefs.Count} non-addressable ref(s) in group '{group.Name}':");
			foreach (string path in nonAddrRefs)
			{
				sb.AppendLine($"  • {path}");
			}
			Debug.Log(sb.ToString());
		}

		/// <summary>
		/// Determines the CSS class tag for a group based on its Build and Load path profile variables.
		/// Returns "group-local", "group-remote", or "group-mixed".
		/// </summary>
		/// <param name="group">The Addressable group.</param>
		/// <returns>A CSS class name, or empty string if no schema is found.</returns>
		private static string GetGroupPathTag(AddressableAssetGroup group)
		{
			var schema = group.GetSchema<BundledAssetGroupSchema>();
			if (schema == null) return "";

			var settings = AddressableAssetSettingsDefaultObject.Settings;
			if (settings == null) return "";

			string buildPathName = schema.BuildPath.GetName(settings);
			string loadPathName = schema.LoadPath.GetName(settings);

			bool buildIsRemote = buildPathName != null && buildPathName.IndexOf("Remote", StringComparison.OrdinalIgnoreCase) >= 0;
			bool loadIsRemote = loadPathName != null && loadPathName.IndexOf("Remote", StringComparison.OrdinalIgnoreCase) >= 0;

			if (buildIsRemote && loadIsRemote) return "group-remote";
			if (!buildIsRemote && !loadIsRemote) return "group-local";
			return "group-mixed";
		}

		/// <summary>
		/// Returns true if the group's Load path profile variable indicates a remote path.
		/// </summary>
		/// <param name="group">The Addressable group.</param>
		/// <returns>True if the load path is remote.</returns>
		private static bool IsGroupLoadPathRemote(AddressableAssetGroup group)
		{
			var schema = group.GetSchema<BundledAssetGroupSchema>();
			if (schema == null) return false;

			var settings = AddressableAssetSettingsDefaultObject.Settings;
			if (settings == null) return false;

			string loadPathName = schema.LoadPath.GetName(settings);
			return loadPathName != null && loadPathName.IndexOf("Remote", StringComparison.OrdinalIgnoreCase) >= 0;
		}

		/// <summary>
		/// Returns a short display string showing the group's entry count and path type (Local/Remote).
		/// </summary>
		/// <param name="group">The Addressable group.</param>
		/// <returns>Formatted path info string.</returns>
		private static string FormatGroupPathInfo(AddressableAssetGroup group)
		{
			string entryInfo = $"{group.entries.Count} entries";

			var schema = group.GetSchema<BundledAssetGroupSchema>();
			if (schema == null) return entryInfo;

			var settings = AddressableAssetSettingsDefaultObject.Settings;
			if (settings == null) return entryInfo;

			string buildPathName = schema.BuildPath.GetName(settings);
			string loadPathName = schema.LoadPath.GetName(settings);

			bool buildIsRemote = buildPathName != null && buildPathName.IndexOf("Remote", StringComparison.OrdinalIgnoreCase) >= 0;
			bool loadIsRemote = loadPathName != null && loadPathName.IndexOf("Remote", StringComparison.OrdinalIgnoreCase) >= 0;

			string pathType;
			if (buildIsRemote && loadIsRemote)
			{
				pathType = "Remote";
			}
			else if (!buildIsRemote && !loadIsRemote)
			{
				pathType = "Local";
			}
			else
			{
				pathType = "Mixed";
			}

			string packMode;
			switch (schema.BundleMode)
			{
				case BundledAssetGroupSchema.BundlePackingMode.PackTogether:
					packMode = "Pack Together";
					break;
				case BundledAssetGroupSchema.BundlePackingMode.PackSeparately:
					packMode = "Pack Separately";
					break;
				case BundledAssetGroupSchema.BundlePackingMode.PackTogetherByLabel:
					packMode = "Pack By Label";
					break;
				default:
					packMode = schema.BundleMode.ToString();
					break;
			}

			return $"{entryInfo}  [{pathType}]  [{packMode}]";
		}

		/// <summary>
		/// Prompts the user to create a new Addressable group with a default schema.
		/// </summary>
		private void AddNewGroup()
		{
			var settings = AddressableAssetSettingsDefaultObject.Settings;
			if (settings == null)
			{
				EditorUtility.DisplayDialog("Add Group", "Addressable settings not found.", "OK");
				return;
			}

			EditorInputDialog.Show("Add Group", "Enter new group name:", "New Group", (groupName) =>
			{
				if (string.IsNullOrEmpty(groupName)) return;

				var newGroup = settings.CreateGroup(groupName, false, false, true, null, typeof(BundledAssetGroupSchema));
				if (newGroup != null)
				{
					Debug.Log($"[AddressablesDashboard] Created group '{groupName}'.");
					EditorUtility.SetDirty(settings);
					RebuildTree();
				}
			});
		}

		/// <summary>
		/// Updates the status bar text.
		/// </summary>
		/// <param name="text">The status message.</param>
		private void SetStatus(string text)
		{
			if (statusBar != null) statusBar.text = text;
		}

		// ──────────────────────────────────────────────
		// Path Simulator
		// ──────────────────────────────────────────────

		/// <summary>
		/// Handles TreeView selection changes to update the path simulator and dependency viewer.
		/// </summary>
		/// <param name="indices">The newly selected indices.</param>
		private void OnTreeSelectionChanged(IEnumerable<int> indices)
		{
			UpdatePathSimulator();
			UpdateDependencyViewer();
		}

		/// <summary>
		/// Updates the Path Simulator panel with the currently selected entry's simulated paths.
		/// Always shows both Client (CDN) and Server (file://) resolved paths side by side.
		/// Mirrors the transform logic from DynamicAddressableLoadPathSystem.
		/// </summary>
		private void UpdatePathSimulator()
		{
			if (pathSimInternalId == null || pathSimClient == null || pathSimServer == null) return;

			// Find selected entry
			var selectedIndices = treeView.selectedIndices.ToList();
			if (selectedIndices.Count == 0)
			{
				if (pathSimBuild != null) pathSimBuild.text = "—";
				pathSimInternalId.text = "(select an asset)";
				pathSimClient.text = "—";
				pathSimServer.text = "—";
				return;
			}

			int selectedId = treeView.GetIdForIndex(selectedIndices[0]);

			// Only simulate for asset entries, not groups
			if (!idToEntry.TryGetValue(selectedId, out AddressableAssetEntry entry))
			{
				if (pathSimBuild != null) pathSimBuild.text = "—";
				pathSimInternalId.text = "(select an asset entry, not a group)";
				pathSimClient.text = "—";
				pathSimServer.text = "—";
				return;
			}

			// Show the resolved Remote.BuildPath for this entry's group
			if (pathSimBuild != null)
			{
				string buildPath = ResolveGroupBuildPath(entry);
				pathSimBuild.text = buildPath;
				pathSimBuild.tooltip = buildPath;
			}

			// Build an InternalId from the group's Load path and the entry address
			string internalId = BuildSimulatedInternalId(entry);
			pathSimInternalId.text = internalId;
			pathSimInternalId.tooltip = internalId;

			// Always show both resolved paths
			string clientPath = SimulatePathTransform(internalId, false);
			string serverPath = SimulatePathTransform(internalId, true);

			pathSimClient.text = clientPath;
			pathSimClient.tooltip = clientPath;
			pathSimServer.text = serverPath;
			pathSimServer.tooltip = serverPath;
		}

		/// <summary>
		/// Builds a simulated InternalId for an entry based on its group's resolved Load path.
		/// For remote groups this will look like "http://…/bundlename".
		/// For local groups this returns the asset path as-is (no transform needed).
		/// </summary>
		/// <param name="entry">The addressable entry.</param>
		/// <returns>A simulated InternalId string.</returns>
		private static string BuildSimulatedInternalId(AddressableAssetEntry entry)
		{
			if (entry == null || entry.parentGroup == null) return entry != null ? entry.AssetPath : "";

			var schema = entry.parentGroup.GetSchema<BundledAssetGroupSchema>();
			if (schema == null) return entry.AssetPath;

			var settings = AddressableAssetSettingsDefaultObject.Settings;
			if (settings == null) return entry.AssetPath;

			// Evaluate the Load path profile variable to get the resolved URL/path
			string loadPath = settings.profileSettings.GetValueByName(
				settings.activeProfileId,
				schema.LoadPath.GetName(settings));

			if (string.IsNullOrEmpty(loadPath)) return entry.AssetPath;

			// Simulate a bundle name from the group name + entry address
			string bundleName = entry.parentGroup.Name.ToLowerInvariant().Replace(" ", "") + "_" + entry.address.ToLowerInvariant().Replace("/", "_") + ".bundle";

			if (!loadPath.EndsWith("/")) loadPath += "/";
			return loadPath + bundleName;
		}

		/// <summary>
		/// Resolves the Remote.BuildPath for the group of the given entry using the active profile.
		/// This lets you verify that the build output location matches what the LoadPath expects.
		/// </summary>
		/// <param name="entry">The addressable entry whose group to resolve.</param>
		/// <returns>The resolved build path, or a descriptive fallback.</returns>
		private static string ResolveGroupBuildPath(AddressableAssetEntry entry)
		{
			if (entry == null || entry.parentGroup == null) return "—";

			var schema = entry.parentGroup.GetSchema<BundledAssetGroupSchema>();
			if (schema == null) return "(no schema)";

			var settings = AddressableAssetSettingsDefaultObject.Settings;
			if (settings == null) return "—";

			string buildPathName = schema.BuildPath.GetName(settings);
			if (string.IsNullOrEmpty(buildPathName)) return "(not set)";

			string resolved = settings.profileSettings.GetValueByName(
				settings.activeProfileId,
				buildPathName);

			return string.IsNullOrEmpty(resolved) ? "(not set)" : resolved;
		}

		/// <summary>
		/// Simulates the DynamicAddressableLoadPathSystem.InternalIdTransformFunc logic.
		/// When simulateServer is true, replaces remote base URLs with file://StreamingAssets/ServerData/.
		/// When false, replaces remote base URLs with the default client CDN URL.
		/// Local paths are returned as-is.
		/// </summary>
		/// <param name="internalId">The original InternalId to transform.</param>
		/// <param name="simulateServer">True to simulate UNITY_SERVER, false for client.</param>
		/// <returns>The transformed path.</returns>
		private static string SimulatePathTransform(string internalId, bool simulateServer)
		{
			// Only transform http/https paths (mirrors DynamicAddressableLoadPathSystem)
			if (!internalId.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
				!internalId.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
			{
				return internalId;
			}

			string baseUrl;
			if (simulateServer)
			{
				baseUrl = ServerBaseUrlPrefix + Application.streamingAssetsPath + ServerBaseUrlSuffix;
			}
			else
			{
				baseUrl = DefaultClientBaseUrl;
			}

			// Extract relative path after the domain (same as DynamicAddressableLoadPathSystem)
			int startIndex = internalId.IndexOf("://", StringComparison.Ordinal) + 3;
			int thirdSlashIndex = internalId.IndexOf('/', startIndex);

			if (thirdSlashIndex != -1)
			{
				string relativePath = internalId.Substring(thirdSlashIndex + 1);
				return baseUrl + relativePath;
			}

			return baseUrl;
		}

		// ──────────────────────────────────────────────
		// Dependency Viewer
		// ──────────────────────────────────────────────

		/// <summary>
		/// Updates the Dependency Viewer panel for the currently selected asset.
		/// Shows direct dependencies and highlights implicit duplicates (dependencies
		/// that will be packed into multiple bundles because they are not addressable
		/// and are referenced by more than one group).
		/// </summary>
		private void UpdateDependencyViewer()
		{
			if (depDirectList == null || depNonAddrList == null || depDupesList == null) return;

			depDirectList.Clear();
			depNonAddrList.Clear();
			depDupesList.Clear();

			if (depViewerAsset != null)
			{
				depViewerAsset.text = "(select an asset)";
			}

			// Find selected entry
			var selectedIndices = treeView.selectedIndices.ToList();
			if (selectedIndices.Count == 0) return;

			int selectedId = treeView.GetIdForIndex(selectedIndices[0]);
			if (!idToEntry.TryGetValue(selectedId, out AddressableAssetEntry selectedEntry))
			{
				if (depViewerAsset != null)
				{
					depViewerAsset.text = "(select an asset, not a group)";
				}
				return;
			}

			if (depViewerAsset != null)
			{
				depViewerAsset.text = selectedEntry.AssetPath;
			}

			var settings = AddressableAssetSettingsDefaultObject.Settings;
			if (settings == null) return;

			// Get direct dependencies (non-recursive first level only would require manual walk;
			// GetDependencies with recursive=false gives immediate deps)
			string[] directDeps = AssetDatabase.GetDependencies(selectedEntry.AssetPath, false);

			// Build addressable path set
			var addressablePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var group in settings.groups)
			{
				if (group == null) continue;
				foreach (var entry in group.entries)
				{
					if (entry == null) continue;
					if (ShouldSkipLocalAsset(entry.AssetPath)) continue;
					addressablePaths.Add(entry.AssetPath);
				}
			}

			// Build dep→groups map for all groups to detect cross-group duplicates
			var depToGroupNames = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
			foreach (var group in settings.groups)
			{
				if (group == null) continue;
				foreach (var entry in group.entries)
				{
					if (entry == null) continue;
					if (ShouldSkipLocalAsset(entry.AssetPath)) continue;
					string[] entryDeps = AssetDatabase.GetDependencies(entry.AssetPath, true);
					for (int d = 0; d < entryDeps.Length; d++)
					{
						string dep = entryDeps[d];
						if (string.Equals(dep, entry.AssetPath, StringComparison.OrdinalIgnoreCase)) continue;
						if (dep.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
							dep.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase) ||
							IsEditorOnlyPath(dep) ||
							ShouldSkipLocalAsset(dep))
						{
							continue;
						}

						if (!depToGroupNames.TryGetValue(dep, out HashSet<string> groups))
						{
							groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
							depToGroupNames[dep] = groups;
						}
						groups.Add(group.Name);
					}
				}
			}

			// Populate direct dependencies list
			int directCount = 0;
			for (int i = 0; i < directDeps.Length; i++)
			{
				string dep = directDeps[i];
				if (string.Equals(dep, selectedEntry.AssetPath, StringComparison.OrdinalIgnoreCase)) continue;
				if (dep.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
					dep.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase) ||
					IsEditorOnlyPath(dep) ||
					ShouldSkipLocalAsset(dep))
				{
					continue;
				}

				bool isAddressable = addressablePaths.Contains(dep);
				HashSet<string> groups = null;
				bool isDuplicate = !isAddressable &&
					depToGroupNames.TryGetValue(dep, out groups) &&
					groups.Count > 1;

				var label = CreateInteractiveDepLabel(dep, settings, addressablePaths, depToGroupNames);
				label.AddToClassList("dep-viewer-item");

				if (isDuplicate)
				{
					label.AddToClassList("dep-viewer-item--duplicate");
					label.tooltip = $"Duplicated across: {string.Join(", ", groups)}";
				}
				else if (isAddressable)
				{
					label.tooltip = "Addressable — bundled once";
				}

				depDirectList.Add(label);
				directCount++;
			}

			if (directCount == 0)
			{
				var empty = new Label("No direct dependencies");
				empty.AddToClassList("dep-viewer-empty");
				depDirectList.Add(empty);
			}

			// Populate non-addressable and implicit duplicate lists from recursive deps
			string[] allDeps = AssetDatabase.GetDependencies(selectedEntry.AssetPath, true);
			int nonAddrCount = 0;
			int dupeCount = 0;

			for (int i = 0; i < allDeps.Length; i++)
			{
				string dep = allDeps[i];
				if (string.Equals(dep, selectedEntry.AssetPath, StringComparison.OrdinalIgnoreCase)) continue;
				if (dep.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
					dep.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase) ||
					IsEditorOnlyPath(dep) ||
					ShouldSkipLocalAsset(dep))
				{
					continue;
				}

				if (addressablePaths.Contains(dep)) continue;

				// Check if this non-addressable dep is referenced by multiple groups (implicit duplicate)
				HashSet<string> dupeGroups = null;
				bool isDupe = depToGroupNames.TryGetValue(dep, out dupeGroups) && dupeGroups.Count > 1;

				if (isDupe)
				{
					var depLabel = CreateInteractiveDepLabel(dep, settings, addressablePaths, depToGroupNames);
					depLabel.AddToClassList("dep-viewer-item");
					depLabel.AddToClassList("dep-viewer-item--duplicate");
					depDupesList.Add(depLabel);

					var groupsLabel = new Label($"→ {string.Join(", ", dupeGroups)}");
					groupsLabel.AddToClassList("dep-viewer-item--groups");
					depDupesList.Add(groupsLabel);

					dupeCount++;
				}
				else
				{
					// Non-addressable but not duplicated across groups
					var naLabel = CreateInteractiveDepLabel(dep, settings, addressablePaths, depToGroupNames);
					naLabel.AddToClassList("dep-viewer-item");
					naLabel.AddToClassList("dep-viewer-item--nonaddr");
					depNonAddrList.Add(naLabel);

					nonAddrCount++;
				}
			}

			if (nonAddrCount == 0)
			{
				var empty = new Label("No non-addressable dependencies");
				empty.AddToClassList("dep-viewer-empty");
				depNonAddrList.Add(empty);
			}

			if (dupeCount == 0)
			{
				var empty = new Label("No implicit duplicates");
				empty.AddToClassList("dep-viewer-empty");
				depDupesList.Add(empty);
			}
		}

		// ──────────────────────────────────────────────
		// Interactive Dependency Items
		// ──────────────────────────────────────────────

		/// <summary>
		/// Creates a dependency label that supports left-click to select in Project
		/// and right-click for a context menu with fix suggestions.
		/// </summary>
		/// <param name="depPath">The dependency asset path.</param>
		/// <param name="settings">Addressable settings for fix operations.</param>
		/// <param name="addressablePaths">Set of all currently addressable paths.</param>
		/// <param name="depToGroupNames">Map of dependency → referencing group names.</param>
		/// <returns>An interactive Label element.</returns>
		private Label CreateInteractiveDepLabel(
			string depPath,
			AddressableAssetSettings settings,
			HashSet<string> addressablePaths,
			Dictionary<string, HashSet<string>> depToGroupNames)
		{
			var label = new Label(depPath);
			label.AddToClassList("dep-viewer-item--interactive");

			// Left-click: select asset in Project window
			label.RegisterCallback<PointerDownEvent>((evt) =>
			{
				if (evt.button == 0 && evt.clickCount == 1)
				{
					var obj = AssetDatabase.LoadMainAssetAtPath(depPath);
					if (obj != null)
					{
						Selection.activeObject = obj;
						EditorGUIUtility.PingObject(obj);
					}
				}
			});

			// Right-click: context menu with fix suggestions
			label.AddManipulator(new ContextualMenuManipulator((evt) =>
			{
				evt.menu.AppendAction("Select in Project", _ =>
				{
					var obj = AssetDatabase.LoadMainAssetAtPath(depPath);
					if (obj != null)
					{
						Selection.activeObject = obj;
						EditorGUIUtility.PingObject(obj);
					}
				});

				evt.menu.AppendAction("Copy Path", _ =>
				{
					EditorGUIUtility.systemCopyBuffer = depPath;
				});

				evt.menu.AppendSeparator();

				bool isPlugin = depPath.Replace('\\', '/').StartsWith("Assets/Plugins/", StringComparison.OrdinalIgnoreCase);
				bool isAddressable = addressablePaths.Contains(depPath);
				HashSet<string> dupeGroups = null;
				bool isDuplicate = !isAddressable &&
					depToGroupNames.TryGetValue(depPath, out dupeGroups) &&
					dupeGroups.Count > 1;

				if (isPlugin)
				{
					// Plugin-specific fix suggestions
					AppendPluginFixActions(evt.menu, depPath, settings);
				}
				else if (isDuplicate)
				{
					// Non-addressable duplicate: offer to make addressable in the right group
					AppendDuplicateFixActions(evt.menu, depPath, settings, dupeGroups);
				}
				else if (!isAddressable)
				{
					// Non-addressable but not duplicated: offer to make addressable
					HashSet<string> refGroups = null;
					depToGroupNames.TryGetValue(depPath, out refGroups);
					AppendNonAddressableFixActions(evt.menu, depPath, settings, refGroups);
				}
			}));

			return label;
		}

		/// <summary>
		/// Appends context menu actions for plugin dependencies.
		/// Plugin assets should not be made addressable — the fix is to find or create
		/// a non-plugin replacement in the project's own asset directories.
		/// </summary>
		private void AppendPluginFixActions(DropdownMenu menu, string depPath, AddressableAssetSettings settings)
		{
			string ext = Path.GetExtension(depPath).ToLowerInvariant();
			string fileName = Path.GetFileName(depPath);

			// Determine what kind of plugin asset this is and suggest the correct project location
			string suggestedDir;
			string assetKind;

			switch (ext)
			{
				case ".mat":
					suggestedDir = "Assets/Prefabs/Shared/Materials";
					assetKind = "Material";
					break;
				case ".shader":
				case ".shadergraph":
				case ".shadersubgraph":
					suggestedDir = "Assets/Prefabs/Shared/Shaders";
					assetKind = "Shader";
					break;
				case ".png":
				case ".jpg":
				case ".jpeg":
				case ".tga":
				case ".psd":
				case ".exr":
				case ".hdr":
					suggestedDir = "Assets/Prefabs/Client/Textures";
					assetKind = "Texture";
					break;
				case ".fbx":
				case ".obj":
				case ".blend":
					suggestedDir = "Assets/Prefabs/Client/Models";
					assetKind = "Model";
					break;
				case ".prefab":
					suggestedDir = "Assets/Prefabs/Shared/Placeholders";
					assetKind = "Prefab";
					break;
				case ".asset":
					suggestedDir = "Assets/Prefabs/Shared";
					assetKind = "Asset";
					break;
				case ".ttf":
				case ".otf":
					suggestedDir = "Assets/Prefabs/Client/Fonts";
					assetKind = "Font";
					break;
				case ".anim":
				case ".controller":
				case ".overrideController":
					suggestedDir = "Assets/Prefabs/Client/Animations";
					assetKind = "Animation";
					break;
				default:
					suggestedDir = "Assets/Prefabs/Shared";
					assetKind = "Asset";
					break;
			}

			// Find if a same-named asset already exists outside Plugins
			string existingReplacement = FindNonPluginAssetByName(fileName, ext);

			if (!string.IsNullOrEmpty(existingReplacement))
			{
				menu.AppendAction($"Replace with existing: {existingReplacement}", _ =>
				{
					RemapAssetReferences(depPath, existingReplacement);
				});
			}

			menu.AppendAction($"Copy {assetKind} to {suggestedDir}/", _ =>
			{
				CopyPluginAssetToProject(depPath, suggestedDir);
			});

			menu.AppendAction($"Find similar assets in project…", _ =>
			{
				FindSimilarAssets(depPath, ext);
			});

			menu.AppendSeparator();
			menu.AppendAction("Why can't this be auto-fixed?", _ =>
			{
				EditorUtility.DisplayDialog("Plugin Dependencies",
					$"This asset is under Assets/Plugins/ which contains third-party code.\n\n" +
					$"Assets/Plugins/ should not be made addressable because:\n" +
					$"  • Plugin assets may be overwritten by package updates\n" +
					$"  • They are placeholders, not production assets\n\n" +
					$"Recommended fix:\n" +
					$"  1. Copy the {assetKind.ToLowerInvariant()} to {suggestedDir}/\n" +
					$"  2. Update all references to point to the copy\n" +
					$"  3. Run Smart Group to make the copy addressable\n\n" +
					$"Use 'Copy {assetKind} to {suggestedDir}/' from this context menu\n" +
					$"to automate step 1 and 2.",
					"OK");
			});
		}

		/// <summary>
		/// Appends context menu actions for non-addressable duplicate dependencies.
		/// The primary fix is to make the asset addressable in the appropriate smart group
		/// so it is bundled once instead of duplicated into multiple bundles.
		/// </summary>
		private void AppendDuplicateFixActions(DropdownMenu menu, string depPath,
			AddressableAssetSettings settings, HashSet<string> referencingGroups)
		{
			AssetCategory category = CategorizeAsset(depPath, referencingGroups);

			if (!string.IsNullOrEmpty(category.GroupName) && !category.IsPluginWarning)
			{
				menu.AppendAction($"Make addressable → {category.GroupName}", _ =>
				{
					MakeAssetAddressable(depPath, settings, category.GroupName);
				});
			}

			menu.AppendAction("Make addressable → Shared_Dynamic", _ =>
			{
				MakeAssetAddressable(depPath, settings, SmartGroups.SharedDynamic);
			});

			if (referencingGroups != null && referencingGroups.Count > 0)
			{
				menu.AppendSeparator();
				string groupList = string.Join(", ", referencingGroups);
				menu.AppendAction($"Duplicated across: {groupList}", null, DropdownMenuAction.Status.Disabled);
			}
		}

		/// <summary>
		/// Appends context menu actions for non-addressable, non-duplicate dependencies.
		/// </summary>
		private void AppendNonAddressableFixActions(DropdownMenu menu, string depPath,
			AddressableAssetSettings settings, HashSet<string> referencingGroups)
		{
			AssetCategory category = CategorizeAsset(depPath, referencingGroups);

			if (!string.IsNullOrEmpty(category.GroupName) && !category.IsPluginWarning)
			{
				menu.AppendAction($"Make addressable → {category.GroupName} ({category.Reason})", _ =>
				{
					MakeAssetAddressable(depPath, settings, category.GroupName);
				});
			}
		}

		/// <summary>
		/// Makes an asset addressable by adding it to the specified group.
		/// Resolves address collisions after adding.
		/// </summary>
		private void MakeAssetAddressable(string assetPath, AddressableAssetSettings settings, string groupName)
		{
			string guid = AssetDatabase.AssetPathToGUID(assetPath);
			if (string.IsNullOrEmpty(guid))
			{
				Debug.LogWarning($"[AddressablesDashboard] Cannot find GUID for: {assetPath}");
				return;
			}

			var groupCache = new Dictionary<string, AddressableAssetGroup>(StringComparer.OrdinalIgnoreCase);
			AddressableAssetGroup targetGroup = GetOrCreateGroup(settings, groupName, groupCache);
			if (targetGroup == null)
			{
				Debug.LogWarning($"[AddressablesDashboard] Failed to get or create group: {groupName}");
				return;
			}

			AddressableAssetEntry newEntry = settings.CreateOrMoveEntry(guid, targetGroup, false, false);
			if (newEntry != null)
			{
				newEntry.SetAddress(Path.GetFileNameWithoutExtension(assetPath));
				SetExclusiveSmartLabel(settings, newEntry, groupName);
				ResolveAddressCollisions(settings);
				EditorUtility.SetDirty(settings);

				Debug.Log($"[AddressablesDashboard] Made addressable: {assetPath} → {groupName}");
				SetStatus($"Added to {groupName}: {Path.GetFileName(assetPath)}");
				RebuildTree();
			}
		}

		/// <summary>
		/// Returns true if the asset path is inside an Editor folder.
		/// Unity strips these from builds, so they cannot cause bundle duplication.
		/// </summary>
		private static bool IsEditorOnlyPath(string assetPath)
		{
			// Normalize separators for consistent matching
			string normalized = assetPath.Replace('\\', '/');
			return normalized.IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) >= 0 ||
				   normalized.EndsWith("/Editor", StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>
		/// Returns true if the asset path is under Assets/LOCAL and the local directory
		/// toggle is disabled in FishMMO Dashboard settings. LOCAL assets are development-only
		/// and should be ignored unless explicitly enabled via FishMMO Dashboard → Build → Enable Local Directory.
		/// </summary>
		private static bool ShouldSkipLocalAsset(string assetPath)
		{
			if (EditorPrefs.GetBool("FishMMOEnableLocalDirectory", false))
				return false;
			string normalized = assetPath.Replace('\\', '/');
			return normalized.StartsWith("Assets/LOCAL/", StringComparison.OrdinalIgnoreCase) ||
				   normalized.Equals("Assets/LOCAL", StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>
		/// Searches the project for a non-plugin asset with the same filename and extension.
		/// Returns the first match found, or null.
		/// </summary>
		private static string FindNonPluginAssetByName(string fileName, string ext)
		{
			// Search by filename without extension to get candidates
			string nameNoExt = Path.GetFileNameWithoutExtension(fileName);
			string[] guids = AssetDatabase.FindAssets(nameNoExt);

			for (int i = 0; i < guids.Length; i++)
			{
				string path = AssetDatabase.GUIDToAssetPath(guids[i]);
				if (path.Replace('\\', '/').StartsWith("Assets/Plugins/", StringComparison.OrdinalIgnoreCase))
					continue;
				if (!path.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
					continue;
				if (Path.GetFileName(path).Equals(fileName, StringComparison.OrdinalIgnoreCase))
					return path;
			}

			return null;
		}

		/// <summary>
		/// Copies a plugin asset to the specified project directory and remaps all references
		/// from the original plugin path to the new copy.
		/// </summary>
		private void CopyPluginAssetToProject(string pluginPath, string targetDir)
		{
			string fileName = Path.GetFileName(pluginPath);
			string targetPath = targetDir + "/" + fileName;

			// Ensure directory exists
			if (!AssetDatabase.IsValidFolder(targetDir))
			{
				string[] parts = targetDir.Split('/');
				string current = parts[0];
				for (int p = 1; p < parts.Length; p++)
				{
					string next = current + "/" + parts[p];
					if (!AssetDatabase.IsValidFolder(next))
					{
						AssetDatabase.CreateFolder(current, parts[p]);
					}
					current = next;
				}
			}

			if (AssetDatabase.LoadMainAssetAtPath(targetPath) != null)
			{
				if (!EditorUtility.DisplayDialog("Asset Exists",
					$"'{fileName}' already exists at:\n{targetPath}\n\nReplace references to point there?",
					"Remap References", "Cancel"))
				{
					return;
				}
			}
			else
			{
				if (!AssetDatabase.CopyAsset(pluginPath, targetPath))
				{
					Debug.LogError($"[AddressablesDashboard] Failed to copy '{pluginPath}' to '{targetPath}'.");
					return;
				}
				AssetDatabase.Refresh();
				Debug.Log($"[AddressablesDashboard] Copied: {pluginPath} → {targetPath}");
			}

			// Remap references from the old plugin path to the new path
			RemapAssetReferences(pluginPath, targetPath);

			SetStatus($"Copied and remapped: {fileName} → {targetDir}/");
		}

		/// <summary>
		/// Remaps all references from one asset path to another across all addressable entries
		/// and their known dependents. Uses AssetDatabase to find dependents and update serialized references.
		/// </summary>
		private static void RemapAssetReferences(string oldPath, string newPath)
		{
			string oldGuid = AssetDatabase.AssetPathToGUID(oldPath);
			string newGuid = AssetDatabase.AssetPathToGUID(newPath);

			if (string.IsNullOrEmpty(oldGuid) || string.IsNullOrEmpty(newGuid))
			{
				Debug.LogWarning($"[AddressablesDashboard] Cannot remap — missing GUID for old or new asset.");
				return;
			}

			// Find all assets that reference the old asset by scanning .meta and serialized files
			// Unity stores references as GUIDs in serialized files, so we can do a text replacement
			string[] allAssets = AssetDatabase.GetAllAssetPaths();
			int remappedCount = 0;

			for (int i = 0; i < allAssets.Length; i++)
			{
				string assetPath = allAssets[i];
				// Only process project assets (not packages, plugins-to-plugins, or scripts)
				if (!assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) continue;
				if (assetPath.StartsWith("Assets/Plugins/", StringComparison.OrdinalIgnoreCase)) continue;
				if (assetPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) continue;

				// Check text-based serialized files (.prefab, .unity, .asset, .mat, .controller, etc.)
				string ext = Path.GetExtension(assetPath).ToLowerInvariant();
				bool isSerializedText = ext == ".prefab" || ext == ".unity" || ext == ".asset" ||
										ext == ".mat" || ext == ".controller" || ext == ".overridecontroller" ||
										ext == ".anim" || ext == ".physicmaterial" || ext == ".rendertexture" ||
										ext == ".lighting" || ext == ".playable" || ext == ".signal" ||
										ext == ".mixer";

				if (!isSerializedText) continue;

				string fullPath = assetPath;
				if (!File.Exists(fullPath)) continue;

				string content = File.ReadAllText(fullPath);
				if (content.Contains(oldGuid))
				{
					string updated = content.Replace(oldGuid, newGuid);
					File.WriteAllText(fullPath, updated);
					remappedCount++;
				}
			}

			if (remappedCount > 0)
			{
				AssetDatabase.Refresh();
				Debug.Log($"[AddressablesDashboard] Remapped {remappedCount} file(s) from {oldPath} → {newPath}");
			}
			else
			{
				Debug.Log($"[AddressablesDashboard] No serialized references found to remap for {oldPath}");
			}
		}

		/// <summary>
		/// Opens a search in the Project window for assets with the same extension,
		/// helping the user find potential replacements for a plugin dependency.
		/// </summary>
		private static void FindSimilarAssets(string depPath, string ext)
		{
			string nameNoExt = Path.GetFileNameWithoutExtension(depPath);
			string[] guids = AssetDatabase.FindAssets(nameNoExt);
			var results = new StringBuilder();
			results.AppendLine($"[AddressablesDashboard] Assets similar to '{Path.GetFileName(depPath)}':");

			int found = 0;
			for (int i = 0; i < guids.Length; i++)
			{
				string path = AssetDatabase.GUIDToAssetPath(guids[i]);
				if (string.Equals(path, depPath, StringComparison.OrdinalIgnoreCase)) continue;
				if (!path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) continue;

				results.AppendLine($"  • {path}");
				found++;
			}

			if (found == 0)
			{
				results.AppendLine("  (none found)");
			}

			Debug.Log(results.ToString());

			// Also ping the plugin asset so the user can inspect it
			var obj = AssetDatabase.LoadMainAssetAtPath(depPath);
			if (obj != null)
			{
				EditorGUIUtility.PingObject(obj);
			}
		}
	}

	/// <summary>
	/// Callback-based input dialog using ShowUtility for reliable rendering on all platforms.
	/// ShowModalUtility renders blank on Linux/Unity 6, so this uses a non-blocking utility
	/// window with a callback that fires when the user confirms.
	/// </summary>
	public class EditorInputDialog : EditorWindow
	{
		private string inputValue = "";
		private string promptMessage = "";
		private Action<string> onConfirm;
		private bool focusPending = true;

		/// <summary>
		/// Shows a floating input dialog. Calls onConfirm with the entered string on OK/Enter.
		/// Does not invoke onConfirm when cancelled.
		/// </summary>
		/// <param name="title">Window title.</param>
		/// <param name="message">Prompt message.</param>
		/// <param name="defaultValue">Default input value.</param>
		/// <param name="onConfirm">Callback invoked with the entered value on confirmation.</param>
		public static void Show(string title, string message, string defaultValue, Action<string> onConfirm)
		{
			var window = CreateInstance<EditorInputDialog>();
			window.titleContent = new GUIContent(title);
			window.promptMessage = message;
			window.inputValue = defaultValue ?? "";
			window.onConfirm = onConfirm;
			window.minSize = new Vector2(350, 120);
			window.maxSize = new Vector2(350, 120);
			window.ShowUtility();
		}

		/// <summary>
		/// Draws the dialog GUI.
		/// </summary>
		private void OnGUI()
		{
			EditorGUILayout.Space(10);
			EditorGUILayout.LabelField(promptMessage, EditorStyles.wordWrappedLabel);
			EditorGUILayout.Space(4);

			GUI.SetNextControlName("InputField");
			inputValue = EditorGUILayout.TextField(inputValue);

			// Auto-focus the text field on first repaint
			if (focusPending && Event.current.type == EventType.Repaint)
			{
				EditorGUI.FocusTextInControl("InputField");
				focusPending = false;
			}

			EditorGUILayout.Space(6);
			EditorGUILayout.BeginHorizontal();
			GUILayout.FlexibleSpace();

			if (GUILayout.Button("OK", GUILayout.Width(80)))
			{
				onConfirm?.Invoke(inputValue);
				Close();
			}

			if (GUILayout.Button("Cancel", GUILayout.Width(80)))
			{
				Close();
			}

			EditorGUILayout.EndHorizontal();

			// Handle Enter/Escape keys
			if (Event.current.type == EventType.KeyDown)
			{
				if (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)
				{
					onConfirm?.Invoke(inputValue);
					Close();
					Event.current.Use();
				}
				else if (Event.current.keyCode == KeyCode.Escape)
				{
					Close();
					Event.current.Use();
				}
			}
		}
	}
}
#endif