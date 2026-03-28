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
						!dep.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
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
			if (depDirectList == null || depDupesList == null) return;

			depDirectList.Clear();
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
					string[] entryDeps = AssetDatabase.GetDependencies(entry.AssetPath, true);
					for (int d = 0; d < entryDeps.Length; d++)
					{
						string dep = entryDeps[d];
						if (string.Equals(dep, entry.AssetPath, StringComparison.OrdinalIgnoreCase)) continue;
						if (dep.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
							dep.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
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
					dep.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				var label = new Label(dep);
				label.AddToClassList("dep-viewer-item");

				// Highlight if this dep is also a duplicate
				if (!addressablePaths.Contains(dep) &&
					depToGroupNames.TryGetValue(dep, out HashSet<string> groups) &&
					groups.Count > 1)
				{
					label.AddToClassList("dep-viewer-item--duplicate");
					label.tooltip = $"Duplicated across: {string.Join(", ", groups)}";
				}
				else if (addressablePaths.Contains(dep))
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

			// Populate implicit duplicates list —
			// all recursive deps of this entry that are NOT addressable and appear in >1 group
			string[] allDeps = AssetDatabase.GetDependencies(selectedEntry.AssetPath, true);
			int dupeCount = 0;

			for (int i = 0; i < allDeps.Length; i++)
			{
				string dep = allDeps[i];
				if (string.Equals(dep, selectedEntry.AssetPath, StringComparison.OrdinalIgnoreCase)) continue;
				if (dep.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
					dep.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				// Only show non-addressable deps that are referenced by multiple groups
				if (addressablePaths.Contains(dep)) continue;
				if (!depToGroupNames.TryGetValue(dep, out HashSet<string> dupeGroups) || dupeGroups.Count <= 1) continue;

				var depLabel = new Label(dep);
				depLabel.AddToClassList("dep-viewer-item");
				depLabel.AddToClassList("dep-viewer-item--duplicate");
				depDupesList.Add(depLabel);

				var groupsLabel = new Label($"→ {string.Join(", ", dupeGroups)}");
				groupsLabel.AddToClassList("dep-viewer-item--groups");
				depDupesList.Add(groupsLabel);

				dupeCount++;
			}

			if (dupeCount == 0)
			{
				var empty = new Label("No implicit duplicates");
				empty.AddToClassList("dep-viewer-empty");
				depDupesList.Add(empty);
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