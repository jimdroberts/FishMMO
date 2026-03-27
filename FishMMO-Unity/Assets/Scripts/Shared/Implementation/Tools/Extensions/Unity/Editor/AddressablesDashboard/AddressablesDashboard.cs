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
using UnityEditor.UIElements;

namespace FishMMO.Shared
{
	/// <summary>
	/// Professional-grade Addressables Dashboard editor window.
	/// Displays groups, entries, labels, paths, sizes, and project-wide statistics.
	/// Supports drag-and-drop between groups, context menu operations, and deep dependency analysis.
	/// </summary>
	public class AddressablesDashboard : EditorWindow
	{
		private const string WindowTitle = "Addressables Dashboard";
		private const string UxmlPath = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/AddressablesDashboard/AddressablesDashboard.uxml";
		private const int ProgressReportInterval = 50;
		private const string DefaultClientBaseUrl = "http://127.0.0.1:8000/";
		private const string ServerBaseUrlPrefix = "file://";
		private const string ServerBaseUrlSuffix = "/ServerData/";

		private TreeView treeView;
		private ToolbarSearchField searchField;
		private Label statusBar;

		// Stat labels — basic
		private Label statGroups;
		private Label statAssets;
		private Label statSize;
		private Label statLabels;
		private Label statEmptyGroups;
		private Label statLargestAsset;

		// Stat labels — analysis (populated by Analyze)
		private Label statDuplicates;
		private Label statNonAddressableRefs;
		private Label statTotalDeps;
		private Label statUnusedLabels;

		// Detail panel
		private Foldout detailFoldout;
		private Label detailContent;

		// Path Simulator
		private Label pathSimBuild;
		private Label pathSimInternalId;
		private Label pathSimClient;
		private Label pathSimServer;

		/// <summary>
		/// Unique ID counter for TreeView items.
		/// </summary>
		private int nextId;

		/// <summary>
		/// Maps TreeView item IDs to their backing Addressable group.
		/// </summary>
		private readonly Dictionary<int, AddressableAssetGroup> idToGroup = new Dictionary<int, AddressableAssetGroup>();

		/// <summary>
		/// Maps TreeView item IDs to their backing Addressable asset entry.
		/// </summary>
		private readonly Dictionary<int, AddressableAssetEntry> idToEntry = new Dictionary<int, AddressableAssetEntry>();

		/// <summary>
		/// Current search filter applied to the tree.
		/// </summary>
		private string currentFilter = string.Empty;

		/// <summary>
		/// Cached tree data for rebuilding after filter changes.
		/// </summary>
		private List<TreeViewItemData<string>> fullTreeData;

		/// <summary>
		/// Cached analysis results updated on Analyze.
		/// </summary>
		private int cachedDuplicateCount;
		private int cachedNonAddressableRefCount;
		private int cachedTotalDepCount;
		private int cachedUnusedLabelCount;

		/// <summary>
		/// Cached detailed analysis report text for the detail panel.
		/// </summary>
		private string cachedDetailReport = "";

		/// <summary>
		/// Opens the Addressables Dashboard window from the menu.
		/// </summary>
		[MenuItem("FishMMO/Addressables Dashboard")]
		public static void ShowWindow()
		{
			var window = GetWindow<AddressablesDashboard>();
			window.titleContent = new GUIContent(WindowTitle);
			window.minSize = new Vector2(600, 400);
		}

		/// <summary>
		/// Called when the window is created. Builds the UI from UXML and wires up events.
		/// </summary>
		public void CreateGUI()
		{
			var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
			if (visualTree == null)
			{
				Debug.LogError($"[AddressablesDashboard] Could not load UXML at: {UxmlPath}");
				return;
			}

			visualTree.CloneTree(rootVisualElement);

			treeView = rootVisualElement.Q<TreeView>("addressables-tree");
			searchField = rootVisualElement.Q<ToolbarSearchField>("search-field");
			statusBar = rootVisualElement.Q<Label>("status-bar");

			statGroups = rootVisualElement.Q<Label>("stat-groups");
			statAssets = rootVisualElement.Q<Label>("stat-assets");
			statSize = rootVisualElement.Q<Label>("stat-size");
			statLabels = rootVisualElement.Q<Label>("stat-labels");
			statEmptyGroups = rootVisualElement.Q<Label>("stat-empty-groups");
			statLargestAsset = rootVisualElement.Q<Label>("stat-largest-asset");
			statDuplicates = rootVisualElement.Q<Label>("stat-duplicates");
			statNonAddressableRefs = rootVisualElement.Q<Label>("stat-non-addressable-refs");
			statTotalDeps = rootVisualElement.Q<Label>("stat-total-deps");
			statUnusedLabels = rootVisualElement.Q<Label>("stat-unused-labels");

			detailFoldout = rootVisualElement.Q<Foldout>("detail-foldout");
			detailContent = rootVisualElement.Q<Label>("detail-content");

			// Path Simulator
			pathSimBuild = rootVisualElement.Q<Label>("path-sim-build");
			pathSimInternalId = rootVisualElement.Q<Label>("path-sim-internal-id");
			pathSimClient = rootVisualElement.Q<Label>("path-sim-client");
			pathSimServer = rootVisualElement.Q<Label>("path-sim-server");

			if (treeView == null || searchField == null || statusBar == null)
			{
				Debug.LogError("[AddressablesDashboard] Required UI elements not found in UXML.");
				return;
			}

			treeView.makeItem = MakeTreeItem;
			treeView.bindItem = BindTreeItem;
			treeView.selectionType = SelectionType.Single;
			treeView.selectedIndicesChanged += OnTreeSelectionChanged;

			// Search
			searchField.RegisterValueChangedCallback(OnSearchChanged);

			// Refresh button
			var refreshButton = rootVisualElement.Q<ToolbarButton>("refresh-button");
			if (refreshButton != null)
			{
				refreshButton.clicked += RebuildTree;
			}

			// Analyze button
			var analyzeButton = rootVisualElement.Q<ToolbarButton>("analyze-button");
			if (analyzeButton != null)
			{
				analyzeButton.clicked += RunAnalysis;
			}

			// Export button
			var exportButton = rootVisualElement.Q<ToolbarButton>("export-button");
			if (exportButton != null)
			{
				exportButton.clicked += ExportAnalysis;
			}

			// Add Group button
			var addGroupButton = rootVisualElement.Q<ToolbarButton>("add-group-button");
			if (addGroupButton != null)
			{
				addGroupButton.clicked += AddNewGroup;
			}

			// Drag and drop
			treeView.RegisterCallback<DragUpdatedEvent>(OnDragUpdated);
			treeView.RegisterCallback<DragPerformEvent>(OnDragPerform);
			treeView.RegisterCallback<PointerDownEvent>(OnPointerDownForDrag);

			RebuildTree();
		}

		// ──────────────────────────────────────────────
		// Tree Building
		// ──────────────────────────────────────────────

		/// <summary>
		/// Rebuilds the entire TreeView and statistics from the current Addressable settings.
		/// Also clears any previously cached analysis data.
		/// </summary>
		private void RebuildTree()
		{
			nextId = 0;
			idToGroup.Clear();
			idToEntry.Clear();

			// Clear cached analysis results
			cachedDuplicateCount = 0;
			cachedNonAddressableRefCount = 0;
			cachedTotalDepCount = 0;
			cachedUnusedLabelCount = 0;
			cachedDetailReport = "";
			if (detailFoldout != null)
			{
				detailFoldout.value = false;
				detailFoldout.text = "Analysis Details (click Analyze to populate)";
			}
			if (detailContent != null) detailContent.text = "";

			var settings = AddressableAssetSettingsDefaultObject.Settings;
			if (settings == null)
			{
				SetStatus("Addressable settings not found.");
				treeView.SetRootItems(new List<TreeViewItemData<string>>());
				treeView.Rebuild();
				UpdateBasicStats(0, 0, 0L, 0, 0, "", 0L);
				return;
			}

			int totalAssets = 0;
			long totalBytes = 0L;
			string largestAssetName = "";
			long largestAssetSize = 0L;

			fullTreeData = BuildTreeData(settings, out totalAssets, out totalBytes, out largestAssetName, out largestAssetSize);
			ApplyFilter();

			int groupCount = settings.groups != null ? settings.groups.Count : 0;
			int labelCount = settings.GetLabels() != null ? settings.GetLabels().Count : 0;

			// Count empty groups
			int emptyGroupCount = 0;
			foreach (var group in settings.groups)
			{
				if (group != null && group.entries.Count == 0)
				{
					emptyGroupCount++;
				}
			}

			UpdateBasicStats(groupCount, totalAssets, totalBytes, labelCount, emptyGroupCount, largestAssetName, largestAssetSize);
		}

		/// <summary>
		/// Constructs TreeViewItemData from Addressable groups and their entries.
		/// Also accumulates size and asset counts, and identifies the largest asset.
		/// </summary>
		/// <param name="settings">The addressable asset settings to read from.</param>
		/// <param name="totalAssets">Output: total number of asset entries.</param>
		/// <param name="totalBytes">Output: total estimated size in bytes.</param>
		/// <param name="largestAssetName">Output: address of the largest asset.</param>
		/// <param name="largestAssetSize">Output: size of the largest asset in bytes.</param>
		/// <returns>A list of root-level tree items representing groups.</returns>
		private List<TreeViewItemData<string>> BuildTreeData(AddressableAssetSettings settings, out int totalAssets, out long totalBytes, out string largestAssetName, out long largestAssetSize)
		{
			var roots = new List<TreeViewItemData<string>>();
			totalAssets = 0;
			totalBytes = 0L;
			largestAssetName = "";
			largestAssetSize = 0L;

			foreach (var group in settings.groups)
			{
				if (group == null) continue;

				int groupId = nextId++;
				idToGroup[groupId] = group;

				var children = new List<TreeViewItemData<string>>();
				foreach (var entry in group.entries)
				{
					if (entry == null) continue;

					int entryId = nextId++;
					idToEntry[entryId] = entry;
					children.Add(new TreeViewItemData<string>(entryId, entry.address));

					long entrySize = GetAssetFileSize(entry.AssetPath);
					totalAssets++;
					totalBytes += entrySize;

					if (entrySize > largestAssetSize)
					{
						largestAssetSize = entrySize;
						largestAssetName = entry.address;
					}
				}

				string groupDisplay = $"{group.Name}  ({children.Count})";
				roots.Add(new TreeViewItemData<string>(groupId, groupDisplay, children));
			}

			return roots;
		}

		/// <summary>
		/// Applies the current search filter and updates the TreeView.
		/// </summary>
		private void ApplyFilter()
		{
			if (fullTreeData == null) return;

			List<TreeViewItemData<string>> filtered;
			if (string.IsNullOrEmpty(currentFilter))
			{
				filtered = fullTreeData;
			}
			else
			{
				filtered = FilterTree(fullTreeData, currentFilter);
			}

			treeView.SetRootItems(filtered);
			treeView.Rebuild();

			int groupCount = filtered.Count;
			int assetCount = 0;
			for (int i = 0; i < filtered.Count; i++)
			{
				if (filtered[i].hasChildren)
				{
					assetCount += filtered[i].children.Count();
				}
			}

			SetStatus($"Showing {groupCount} group(s), {assetCount} asset(s)" +
				(string.IsNullOrEmpty(currentFilter) ? "" : $"  [filter: \"{currentFilter}\"]"));
		}

		/// <summary>
		/// Filters the tree data so only groups/entries matching the query are shown.
		/// Matches group names, entry addresses, entry paths, and entry labels.
		/// </summary>
		/// <param name="source">The unfiltered tree data.</param>
		/// <param name="filter">The search string to filter by.</param>
		/// <returns>Filtered tree data.</returns>
		private List<TreeViewItemData<string>> FilterTree(List<TreeViewItemData<string>> source, string filter)
		{
			var result = new List<TreeViewItemData<string>>();

			for (int i = 0; i < source.Count; i++)
			{
				var groupItem = source[i];
				int groupItemId = groupItem.id;
				bool groupMatches = false;

				// Check group name match
				if (idToGroup.TryGetValue(groupItemId, out AddressableAssetGroup grp))
				{
					groupMatches = grp.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
				}

				var matchingChildren = new List<TreeViewItemData<string>>();
				if (groupItem.hasChildren)
				{
					foreach (var child in groupItem.children)
					{
						if (EntryMatchesFilter(child.id, filter))
						{
							matchingChildren.Add(child);
						}
					}
				}

				if (groupMatches || matchingChildren.Count > 0)
				{
					var children = groupMatches
						? (groupItem.hasChildren ? new List<TreeViewItemData<string>>(groupItem.children) : new List<TreeViewItemData<string>>())
						: matchingChildren;

					result.Add(new TreeViewItemData<string>(groupItem.id, groupItem.data, children));
				}
			}

			return result;
		}

		/// <summary>
		/// Checks whether an entry matches the filter by address, asset path, or label.
		/// </summary>
		/// <param name="entryId">The TreeView item ID of the entry.</param>
		/// <param name="filter">The search string.</param>
		/// <returns>True if the entry matches the filter.</returns>
		private bool EntryMatchesFilter(int entryId, string filter)
		{
			if (!idToEntry.TryGetValue(entryId, out AddressableAssetEntry entry)) return false;

			if (entry.address.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) return true;
			if (entry.AssetPath.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) return true;

			foreach (string label in entry.labels)
			{
				if (label.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) return true;
			}

			return false;
		}

		// ──────────────────────────────────────────────
		// Row Rendering
		// ──────────────────────────────────────────────

		/// <summary>
		/// Creates a new visual element for a tree row with icon, name, path, labels, and size columns.
		/// </summary>
		/// <returns>The row container element.</returns>
		private VisualElement MakeTreeItem()
		{
			var row = new VisualElement();
			row.AddToClassList("row-container");

			// Per-row context menu — reads item ID from userData set during BindTreeItem
			row.AddManipulator(new ContextualMenuManipulator(evt => OnRowContextMenu(evt, row)));

			var icon = new Image();
			icon.AddToClassList("row-icon");
			icon.name = "row-icon";
			row.Add(icon);

			var nameLabel = new Label();
			nameLabel.AddToClassList("row-name");
			nameLabel.name = "row-name";
			row.Add(nameLabel);

			var pathLabel = new Label();
			pathLabel.AddToClassList("row-path");
			pathLabel.name = "row-path";
			row.Add(pathLabel);

			var labelsLabel = new Label();
			labelsLabel.AddToClassList("row-labels");
			labelsLabel.name = "row-labels";
			row.Add(labelsLabel);

			var sizeLabel = new Label();
			sizeLabel.AddToClassList("row-size");
			sizeLabel.name = "row-size";
			row.Add(sizeLabel);

			return row;
		}

		/// <summary>
		/// Binds data to a tree row element, populating all columns.
		/// </summary>
		/// <param name="element">The visual element for the row.</param>
		/// <param name="index">The index of the item in the TreeView.</param>
		private void BindTreeItem(VisualElement element, int index)
		{
			int itemId = treeView.GetIdForIndex(index);

			// Store the item ID so the per-row context menu manipulator can read it
			element.userData = itemId;

			var nameLabel = element.Q<Label>("row-name");
			var pathLabel = element.Q<Label>("row-path");
			var labelsLabel = element.Q<Label>("row-labels");
			var sizeLabel = element.Q<Label>("row-size");
			var iconImage = element.Q<Image>("row-icon");

			element.RemoveFromClassList("group-header");
			element.RemoveFromClassList("asset-entry");

			// Clear previous path-type classes
			element.RemoveFromClassList("group-local");
			element.RemoveFromClassList("group-remote");
			element.RemoveFromClassList("group-mixed");

			if (idToGroup.TryGetValue(itemId, out AddressableAssetGroup group))
			{
				element.AddToClassList("group-header");

				// Determine Build & Load path type for color coding
				string pathTag = GetGroupPathTag(group);
				if (!string.IsNullOrEmpty(pathTag))
				{
					element.AddToClassList(pathTag);
				}

				if (nameLabel != null) nameLabel.text = group.Name;
				if (pathLabel != null) pathLabel.text = FormatGroupPathInfo(group);
				if (labelsLabel != null) labelsLabel.text = "";
				if (sizeLabel != null) sizeLabel.text = FormatGroupSize(group);

				if (iconImage != null)
				{
					iconImage.image = EditorGUIUtility.IconContent("d_Folder Icon").image;
				}
			}
			else if (idToEntry.TryGetValue(itemId, out AddressableAssetEntry entry))
			{
				element.AddToClassList("asset-entry");

				if (nameLabel != null) nameLabel.text = entry.address;
				if (pathLabel != null) pathLabel.text = entry.AssetPath;
				if (labelsLabel != null) labelsLabel.text = FormatLabels(entry);
				if (sizeLabel != null) sizeLabel.text = FormatBytes(GetAssetFileSize(entry.AssetPath));

				if (iconImage != null)
				{
					var assetIcon = AssetDatabase.GetCachedIcon(entry.AssetPath);
					iconImage.image = assetIcon != null ? assetIcon : EditorGUIUtility.IconContent("d_DefaultAsset Icon").image;
				}
			}
			else
			{
				string displayText = treeView.GetItemDataForIndex<string>(index);
				if (nameLabel != null) nameLabel.text = displayText;
				if (pathLabel != null) pathLabel.text = "";
				if (labelsLabel != null) labelsLabel.text = "";
				if (sizeLabel != null) sizeLabel.text = "";
				if (iconImage != null) iconImage.image = null;
			}
		}

		// ──────────────────────────────────────────────
		// Statistics
		// ──────────────────────────────────────────────

		/// <summary>
		/// Updates the basic (non-analysis) statistics in the panel.
		/// </summary>
		/// <param name="groups">Number of groups.</param>
		/// <param name="assets">Number of asset entries.</param>
		/// <param name="bytes">Total estimated file size.</param>
		/// <param name="labels">Number of defined labels.</param>
		/// <param name="emptyGroups">Number of groups with zero entries.</param>
		/// <param name="largestAssetName">Address of the largest asset.</param>
		/// <param name="largestAssetSize">Size of the largest asset in bytes.</param>
		private void UpdateBasicStats(int groups, int assets, long bytes, int labels, int emptyGroups, string largestAssetName, long largestAssetSize)
		{
			if (statGroups != null) statGroups.text = groups.ToString();
			if (statAssets != null) statAssets.text = assets.ToString();
			if (statSize != null) statSize.text = FormatBytes(bytes);
			if (statLabels != null) statLabels.text = labels.ToString();

			if (statEmptyGroups != null)
			{
				statEmptyGroups.text = emptyGroups.ToString();
				statEmptyGroups.EnableInClassList("stat-value--warning", emptyGroups > 0);
			}

			if (statLargestAsset != null)
			{
				if (string.IsNullOrEmpty(largestAssetName))
				{
					statLargestAsset.text = "—";
				}
				else
				{
					// Truncate long names for the stat box
					string displayName = largestAssetName.Length > 20
						? largestAssetName.Substring(0, 17) + "…"
						: largestAssetName;
					statLargestAsset.text = $"{FormatBytes(largestAssetSize)}";
					statLargestAsset.tooltip = largestAssetName;
				}
			}

			UpdateAnalysisStats();
		}

		/// <summary>
		/// Updates the analysis-dependent stats using cached values and refreshes the detail panel.
		/// </summary>
		private void UpdateAnalysisStats()
		{
			if (statDuplicates != null)
			{
				statDuplicates.text = cachedDuplicateCount > 0 ? cachedDuplicateCount.ToString() : "—";
				statDuplicates.EnableInClassList("stat-value--warning", cachedDuplicateCount > 0);
			}

			if (statNonAddressableRefs != null)
			{
				statNonAddressableRefs.text = cachedNonAddressableRefCount > 0 ? cachedNonAddressableRefCount.ToString() : "—";
				statNonAddressableRefs.EnableInClassList("stat-value--error", cachedNonAddressableRefCount > 0);
			}

			if (statTotalDeps != null)
			{
				statTotalDeps.text = cachedTotalDepCount > 0 ? cachedTotalDepCount.ToString() : "—";
			}

			if (statUnusedLabels != null)
			{
				statUnusedLabels.text = cachedUnusedLabelCount > 0 ? cachedUnusedLabelCount.ToString() : "—";
				statUnusedLabels.EnableInClassList("stat-value--warning", cachedUnusedLabelCount > 0);
			}

			if (detailContent != null)
			{
				detailContent.text = cachedDetailReport;
			}

			if (detailFoldout != null && !string.IsNullOrEmpty(cachedDetailReport))
			{
				detailFoldout.text = "Analysis Details";
			}
		}

		/// <summary>
		/// Runs the full project analysis: duplicate dependencies, non-addressable references,
		/// unused labels, total dependency count, and builds a detailed report.
		/// Uses EditorUtility.DisplayProgressBar for feedback on large projects.
		/// </summary>
		private void RunAnalysis()
		{
			var settings = AddressableAssetSettingsDefaultObject.Settings;
			if (settings == null)
			{
				SetStatus("Cannot analyze — Addressable settings not found.");
				return;
			}

			SetStatus("Analyzing dependencies…");

			try
			{
				// Build a set of all addressable asset paths
				var addressablePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				var allEntries = new List<AddressableAssetEntry>();

				foreach (var group in settings.groups)
				{
					if (group == null) continue;
					foreach (var entry in group.entries)
					{
						if (entry == null) continue;
						addressablePaths.Add(entry.AssetPath);
						allEntries.Add(entry);
					}
				}

				// Map: dependency path → set of group names that reference it
				var depToGroups = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
				var nonAddressableRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				var allUniqueDeps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

				// Track per-group non-addressable refs for the report
				var groupNonAddrRefs = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

				// Track which labels are actually used by entries
				var usedLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

				int total = allEntries.Count;

				for (int i = 0; i < total; i++)
				{
					var entry = allEntries[i];

					if (i % ProgressReportInterval == 0)
					{
						EditorUtility.DisplayProgressBar(
							"Addressables Analysis",
							$"Scanning {entry.address} ({i + 1}/{total})",
							(float)i / total);
					}

					// Collect used labels
					foreach (string entryLabel in entry.labels)
					{
						usedLabels.Add(entryLabel);
					}

					string[] deps = AssetDatabase.GetDependencies(entry.AssetPath, true);
					string groupName = entry.parentGroup != null ? entry.parentGroup.Name : "Unknown";

					for (int d = 0; d < deps.Length; d++)
					{
						string dep = deps[d];
						if (string.Equals(dep, entry.AssetPath, StringComparison.OrdinalIgnoreCase)) continue;

						allUniqueDeps.Add(dep);

						// Track cross-group duplicate deps
						if (!depToGroups.TryGetValue(dep, out HashSet<string> groups))
						{
							groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
							depToGroups[dep] = groups;
						}
						groups.Add(groupName);

						// Track non-addressable references (skip built-in packages and scripts)
						if (!addressablePaths.Contains(dep) &&
							!dep.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase) &&
							!dep.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
						{
							nonAddressableRefs.Add(dep);

							if (!groupNonAddrRefs.TryGetValue(groupName, out HashSet<string> grpRefs))
							{
								grpRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
								groupNonAddrRefs[groupName] = grpRefs;
							}
							grpRefs.Add(dep);
						}
					}
				}

				// Count dependencies referenced by more than one group
				int duplicateDepCount = 0;
				var duplicateDepList = new List<KeyValuePair<string, HashSet<string>>>();
				foreach (var kvp in depToGroups)
				{
					if (kvp.Value.Count > 1)
					{
						duplicateDepCount++;
						duplicateDepList.Add(kvp);
					}
				}

				// Find unused labels
				var allLabels = settings.GetLabels();
				var unusedLabels = new List<string>();
				if (allLabels != null)
				{
					for (int i = 0; i < allLabels.Count; i++)
					{
						if (!usedLabels.Contains(allLabels[i]))
						{
							unusedLabels.Add(allLabels[i]);
						}
					}
				}

				// Find empty groups
				var emptyGroups = new List<string>();
				foreach (var group in settings.groups)
				{
					if (group != null && group.entries.Count == 0)
					{
						emptyGroups.Add(group.Name);
					}
				}

				// Cache results
				cachedDuplicateCount = duplicateDepCount;
				cachedNonAddressableRefCount = nonAddressableRefs.Count;
				cachedTotalDepCount = allUniqueDeps.Count;
				cachedUnusedLabelCount = unusedLabels.Count;

				// Build detailed report
				var report = new StringBuilder();

				// Summary
				report.AppendLine("═══ ANALYSIS SUMMARY ═══");
				report.AppendLine($"Scanned {total} addressable entries across {settings.groups.Count} groups.");
				report.AppendLine($"Total unique dependencies: {allUniqueDeps.Count}");
				report.AppendLine($"Cross-group duplicate dependencies: {duplicateDepCount}");
				report.AppendLine($"Non-addressable referenced assets: {nonAddressableRefs.Count}");
				report.AppendLine($"Unused labels: {unusedLabels.Count}");
				report.AppendLine($"Empty groups: {emptyGroups.Count}");
				report.AppendLine();

				// Non-addressable references by group
				if (nonAddressableRefs.Count > 0)
				{
					report.AppendLine("═══ NON-ADDRESSABLE REFERENCES ═══");
					report.AppendLine("These assets are referenced by addressable entries but are not addressable themselves.");
					report.AppendLine("They will be duplicated into every bundle that references them.\n");

					foreach (var kvp in groupNonAddrRefs)
					{
						report.AppendLine($"  [{kvp.Key}] — {kvp.Value.Count} non-addressable ref(s):");
						int shown = 0;
						foreach (string path in kvp.Value)
						{
							if (shown >= 15)
							{
								report.AppendLine($"    … and {kvp.Value.Count - shown} more");
								break;
							}
							report.AppendLine($"    • {path}");
							shown++;
						}
						report.AppendLine();
					}
				}

				// Duplicate dependencies
				if (duplicateDepCount > 0)
				{
					report.AppendLine("═══ DUPLICATE DEPENDENCIES ═══");
					report.AppendLine("These dependencies are shared across multiple groups and will be duplicated in bundles.\n");

					int shown = 0;
					for (int i = 0; i < duplicateDepList.Count; i++)
					{
						if (shown >= 30)
						{
							report.AppendLine($"  … and {duplicateDepList.Count - shown} more");
							break;
						}
						var kvp = duplicateDepList[i];
						report.AppendLine($"  • {kvp.Key}");
						report.AppendLine($"    → groups: {string.Join(", ", kvp.Value)}");
						shown++;
					}
					report.AppendLine();
				}

				// Unused labels
				if (unusedLabels.Count > 0)
				{
					report.AppendLine("═══ UNUSED LABELS ═══");
					report.AppendLine("These labels are defined but not assigned to any entry.\n");
					for (int i = 0; i < unusedLabels.Count; i++)
					{
						report.AppendLine($"  • {unusedLabels[i]}");
					}
					report.AppendLine();
				}

				// Empty groups
				if (emptyGroups.Count > 0)
				{
					report.AppendLine("═══ EMPTY GROUPS ═══");
					for (int i = 0; i < emptyGroups.Count; i++)
					{
						report.AppendLine($"  • {emptyGroups[i]}");
					}
					report.AppendLine();
				}

				cachedDetailReport = report.ToString();
				UpdateAnalysisStats();

				// Also log summary to console
				Debug.Log($"[AddressablesDashboard] Analysis complete — {duplicateDepCount} duplicate dep(s), " +
					$"{nonAddressableRefs.Count} non-addressable ref(s), {unusedLabels.Count} unused label(s), " +
					$"{allUniqueDeps.Count} total unique dep(s).");

				SetStatus($"Analysis complete — {duplicateDepCount} dup dep(s), {nonAddressableRefs.Count} non-addr ref(s), " +
					$"{unusedLabels.Count} unused label(s), {allUniqueDeps.Count} total dep(s).");

				// Auto-open detail foldout if there are issues
				if (detailFoldout != null && (duplicateDepCount > 0 || nonAddressableRefs.Count > 0 || unusedLabels.Count > 0))
				{
					detailFoldout.value = true;
				}
			}
			finally
			{
				EditorUtility.ClearProgressBar();
			}
		}

		// ──────────────────────────────────────────────
		// Context Menu
		// ──────────────────────────────────────────────

		/// <summary>
		/// Populates the context menu for a specific row element.
		/// Reads the item ID from the row's userData, set during BindTreeItem.
		/// </summary>
		/// <param name="evt">The contextual menu event.</param>
		/// <param name="row">The row VisualElement that was right-clicked.</param>
		private void OnRowContextMenu(ContextualMenuPopulateEvent evt, VisualElement row)
		{
			if (row.userData == null) return;

			int selectedId = (int)row.userData;
			var settings = AddressableAssetSettingsDefaultObject.Settings;

			if (idToGroup.TryGetValue(selectedId, out AddressableAssetGroup group))
			{
				evt.menu.AppendAction("Inspect Group Settings", _ => InspectGroupSettings(group));
				evt.menu.AppendAction("Rename Group", _ => RenameGroup(group));
				evt.menu.AppendAction("Remove Group", _ => RemoveGroup(group));
				evt.menu.AppendSeparator();
				evt.menu.AppendAction("Find Non-Addressable Refs in Group", _ => FindNonAddressableRefsInGroup(group));
				evt.menu.AppendAction("Select All Entries in Project", _ => SelectGroupEntries(group));
			}
			else if (idToEntry.TryGetValue(selectedId, out AddressableAssetEntry entry))
			{
				// Move to Group submenu
				if (settings != null)
				{
					foreach (var targetGroup in settings.groups)
					{
						if (targetGroup == null || targetGroup == entry.parentGroup) continue;
						string targetName = targetGroup.Name;
						evt.menu.AppendAction($"Move to Group/{targetName}", _ => MoveEntryToGroup(entry, targetGroup));
					}
				}

				evt.menu.AppendSeparator();

				// Label management submenu
				if (settings != null)
				{
					var allLabels = settings.GetLabels();
					if (allLabels != null)
					{
						for (int i = 0; i < allLabels.Count; i++)
						{
							string label = allLabels[i];
							bool hasLabel = entry.labels.Contains(label);
							string prefix = hasLabel ? "✓ " : "  ";
							evt.menu.AppendAction($"Labels/{prefix}{label}", _ => ToggleLabel(entry, label));
						}
					}

					evt.menu.AppendSeparator("Labels/");
					evt.menu.AppendAction("Labels/Add New Label…", _ => AddNewLabel(entry));
				}

				evt.menu.AppendSeparator();
				evt.menu.AppendAction("Change Address…", _ => ChangeAddress(entry));
				evt.menu.AppendAction("Show Dependencies", _ => ShowEntryDependencies(entry));
				evt.menu.AppendAction("Find Duplicates", _ => FindDuplicateDependencies(entry));
				evt.menu.AppendAction("Select in Project", _ => SelectEntryInProject(entry));
				evt.menu.AppendAction("Copy Address", _ => CopyToClipboard(entry.address));
				evt.menu.AppendAction("Copy Path", _ => CopyToClipboard(entry.AssetPath));
				evt.menu.AppendSeparator();
				evt.menu.AppendAction("Remove Entry", _ => RemoveEntry(entry));
			}
		}

		// ──────────────────────────────────────────────
		// Context Menu Actions
		// ──────────────────────────────────────────────

		/// <summary>
		/// Selects the group asset in the Inspector so its settings are visible.
		/// </summary>
		/// <param name="group">The Addressable group to inspect.</param>
		private static void InspectGroupSettings(AddressableAssetGroup group)
		{
			if (group == null) return;
			Selection.activeObject = group;
			EditorGUIUtility.PingObject(group);
		}

		/// <summary>
		/// Prompts the user to rename an Addressable group.
		/// </summary>
		/// <param name="group">The group to rename.</param>
		private void RenameGroup(AddressableAssetGroup group)
		{
			if (group == null) return;

			string newName = EditorInputDialog.Show("Rename Group", "Enter new group name:", group.Name);
			if (string.IsNullOrEmpty(newName) || newName == group.Name) return;

			Undo.RecordObject(group, "Rename Addressable Group");
			group.Name = newName;
			EditorUtility.SetDirty(group);
			RebuildTree();
		}

		/// <summary>
		/// Removes an Addressable group after confirmation.
		/// </summary>
		/// <param name="group">The group to remove.</param>
		private void RemoveGroup(AddressableAssetGroup group)
		{
			if (group == null) return;

			if (!EditorUtility.DisplayDialog("Remove Group",
				$"Remove group '{group.Name}' and all its entries?\nThis cannot be undone.",
				"Remove", "Cancel"))
			{
				return;
			}

			var settings = AddressableAssetSettingsDefaultObject.Settings;
			if (settings == null) return;

			settings.RemoveGroup(group);
			RebuildTree();
		}

		/// <summary>
		/// Selects all asset entries of a group in the Project window.
		/// </summary>
		/// <param name="group">The group whose entries to select.</param>
		private static void SelectGroupEntries(AddressableAssetGroup group)
		{
			if (group == null) return;

			var objects = new List<UnityEngine.Object>();
			foreach (var entry in group.entries)
			{
				if (entry == null) continue;
				var obj = AssetDatabase.LoadMainAssetAtPath(entry.AssetPath);
				if (obj != null) objects.Add(obj);
			}

			if (objects.Count > 0)
			{
				Selection.objects = objects.ToArray();
			}
		}

		/// <summary>
		/// Moves an entry to a different Addressable group with Undo support.
		/// </summary>
		/// <param name="entry">The entry to move.</param>
		/// <param name="targetGroup">The target group.</param>
		private void MoveEntryToGroup(AddressableAssetEntry entry, AddressableAssetGroup targetGroup)
		{
			if (entry == null || targetGroup == null) return;

			var settings = AddressableAssetSettingsDefaultObject.Settings;
			if (settings == null) return;

			Undo.RecordObject(settings, "Move Addressable Entry");
			settings.MoveEntry(entry, targetGroup, false, false);
			Debug.Log($"[AddressablesDashboard] Moved '{entry.address}' to group '{targetGroup.Name}'.");
			RebuildTree();
		}

		/// <summary>
		/// Toggles a label on an asset entry.
		/// </summary>
		/// <param name="entry">The entry to modify.</param>
		/// <param name="label">The label to toggle.</param>
		private void ToggleLabel(AddressableAssetEntry entry, string label)
		{
			if (entry == null) return;

			bool hasLabel = entry.labels.Contains(label);
			entry.SetLabel(label, !hasLabel, true);

			Debug.Log($"[AddressablesDashboard] {(!hasLabel ? "Added" : "Removed")} label '{label}' on '{entry.address}'.");
			RebuildTree();
		}

		/// <summary>
		/// Prompts the user to create a new label and apply it to an entry.
		/// </summary>
		/// <param name="entry">The entry to apply the new label to.</param>
		private void AddNewLabel(AddressableAssetEntry entry)
		{
			if (entry == null) return;

			string newLabel = EditorInputDialog.Show("Add New Label", "Enter label name:", "");
			if (string.IsNullOrEmpty(newLabel)) return;

			var settings = AddressableAssetSettingsDefaultObject.Settings;
			if (settings == null) return;

			settings.AddLabel(newLabel);
			entry.SetLabel(newLabel, true, true);

			Debug.Log($"[AddressablesDashboard] Created label '{newLabel}' and applied to '{entry.address}'.");
			RebuildTree();
		}

		/// <summary>
		/// Prompts the user to change an entry's address.
		/// </summary>
		/// <param name="entry">The entry to rename.</param>
		private void ChangeAddress(AddressableAssetEntry entry)
		{
			if (entry == null) return;

			string newAddress = EditorInputDialog.Show("Change Address", "Enter new address:", entry.address);
			if (string.IsNullOrEmpty(newAddress) || newAddress == entry.address) return;

			entry.SetAddress(newAddress);
			Debug.Log($"[AddressablesDashboard] Changed address to '{newAddress}'.");
			RebuildTree();
		}

		/// <summary>
		/// Selects an entry's source asset in the Project window.
		/// </summary>
		/// <param name="entry">The entry to locate.</param>
		private static void SelectEntryInProject(AddressableAssetEntry entry)
		{
			if (entry == null) return;
			var obj = AssetDatabase.LoadMainAssetAtPath(entry.AssetPath);
			if (obj != null)
			{
				Selection.activeObject = obj;
				EditorGUIUtility.PingObject(obj);
			}
		}

		/// <summary>
		/// Copies a string to the system clipboard.
		/// </summary>
		/// <param name="text">The text to copy.</param>
		private static void CopyToClipboard(string text)
		{
			EditorGUIUtility.systemCopyBuffer = text;
		}

		/// <summary>
		/// Removes an entry from its group after confirmation.
		/// </summary>
		/// <param name="entry">The entry to remove.</param>
		private void RemoveEntry(AddressableAssetEntry entry)
		{
			if (entry == null) return;

			if (!EditorUtility.DisplayDialog("Remove Entry",
				$"Remove '{entry.address}' from group '{entry.parentGroup.Name}'?",
				"Remove", "Cancel"))
			{
				return;
			}

			var settings = AddressableAssetSettingsDefaultObject.Settings;
			if (settings == null) return;

			Undo.RecordObject(settings, "Remove Addressable Entry");
			settings.RemoveAssetEntry(entry.guid);
			RebuildTree();
		}

		/// <summary>
		/// Checks if the entry's dependencies are shared by assets in other groups.
		/// Results are logged to the console.
		/// </summary>
		/// <param name="entry">The asset entry to check for duplicate dependencies.</param>
		private static void FindDuplicateDependencies(AddressableAssetEntry entry)
		{
			if (entry == null) return;

			var settings = AddressableAssetSettingsDefaultObject.Settings;
			if (settings == null) return;

			string assetPath = entry.AssetPath;
			string[] dependencies = AssetDatabase.GetDependencies(assetPath, true);

			var depSet = new HashSet<string>(dependencies, StringComparer.OrdinalIgnoreCase);
			depSet.Remove(assetPath);

			if (depSet.Count == 0)
			{
				Debug.Log($"[AddressablesDashboard] '{entry.address}' has no dependencies.");
				return;
			}

			var duplicates = new Dictionary<string, List<string>>();

			foreach (var group in settings.groups)
			{
				if (group == null || group == entry.parentGroup) continue;

				foreach (var otherEntry in group.entries)
				{
					if (otherEntry == null) continue;

					string[] otherDeps = AssetDatabase.GetDependencies(otherEntry.AssetPath, true);
					for (int i = 0; i < otherDeps.Length; i++)
					{
						string dep = otherDeps[i];
						if (depSet.Contains(dep))
						{
							if (!duplicates.TryGetValue(dep, out List<string> list))
							{
								list = new List<string>();
								duplicates[dep] = list;
							}

							string reference = $"{group.Name}/{otherEntry.address}";
							if (!list.Contains(reference))
							{
								list.Add(reference);
							}
						}
					}
				}
			}

			if (duplicates.Count == 0)
			{
				Debug.Log($"[AddressablesDashboard] No duplicate dependencies found for '{entry.address}'.");
				return;
			}

			var sb = new StringBuilder();
			sb.AppendLine($"[AddressablesDashboard] Duplicate dependencies for '{entry.address}':");
			foreach (var kvp in duplicates)
			{
				sb.AppendLine($"  {kvp.Key} — also used by: {string.Join(", ", kvp.Value)}");
			}
			Debug.Log(sb.ToString());
		}

		// ──────────────────────────────────────────────
		// Drag and Drop
		// ──────────────────────────────────────────────

		/// <summary>
		/// Initiates a drag operation when the user presses on an asset entry row.
		/// </summary>
		/// <param name="evt">The pointer down event.</param>
		private void OnPointerDownForDrag(PointerDownEvent evt)
		{
			if (evt.button != 0) return;

			var selectedIndices = treeView.selectedIndices.ToList();
			if (selectedIndices.Count == 0) return;

			int selectedId = treeView.GetIdForIndex(selectedIndices[0]);
			if (!idToEntry.ContainsKey(selectedId)) return;

			// Set up drag data
			DragAndDrop.PrepareStartDrag();
			DragAndDrop.SetGenericData("AddressablesDashboard_EntryId", selectedId);
			DragAndDrop.StartDrag("Move Addressable Entry");
		}

		/// <summary>
		/// Validates whether a drag operation can be accepted (asset entry dragged over a group).
		/// </summary>
		/// <param name="evt">The drag updated event.</param>
		private void OnDragUpdated(DragUpdatedEvent evt)
		{
			if (DragAndDrop.GetGenericData("AddressablesDashboard_EntryId") == null) return;

			DragAndDrop.visualMode = DragAndDropVisualMode.Move;
			evt.StopPropagation();
		}

		/// <summary>
		/// Performs the drop operation, moving an asset entry to the target group.
		/// </summary>
		/// <param name="evt">The drag perform event.</param>
		private void OnDragPerform(DragPerformEvent evt)
		{
			object rawEntryId = DragAndDrop.GetGenericData("AddressablesDashboard_EntryId");
			if (rawEntryId == null) return;

			int draggedEntryId = (int)rawEntryId;
			if (!idToEntry.TryGetValue(draggedEntryId, out AddressableAssetEntry draggedEntry)) return;

			int targetIndex = ResolveDropTargetIndex();
			if (targetIndex < 0) return;

			int targetId = treeView.GetIdForIndex(targetIndex);
			AddressableAssetGroup targetGroup = null;

			if (idToGroup.TryGetValue(targetId, out AddressableAssetGroup directGroup))
			{
				targetGroup = directGroup;
			}
			else if (idToEntry.TryGetValue(targetId, out AddressableAssetEntry targetEntry))
			{
				targetGroup = targetEntry.parentGroup;
			}

			if (targetGroup == null || targetGroup == draggedEntry.parentGroup) return;

			MoveEntryToGroup(draggedEntry, targetGroup);

			DragAndDrop.AcceptDrag();
			evt.StopPropagation();
		}

		/// <summary>
		/// Resolves the drop target index from the current TreeView selection.
		/// </summary>
		/// <returns>The selected item index, or -1 if none.</returns>
		private int ResolveDropTargetIndex()
		{
			var selectedIndices = treeView.selectedIndices.ToList();
			if (selectedIndices.Count > 0)
			{
				return selectedIndices[0];
			}
			return -1;
		}

		// ──────────────────────────────────────────────
		// Search
		// ──────────────────────────────────────────────

		/// <summary>
		/// Handles the search field value change to filter the tree.
		/// </summary>
		/// <param name="evt">The change event containing the new search value.</param>
		private void OnSearchChanged(ChangeEvent<string> evt)
		{
			currentFilter = evt.newValue ?? string.Empty;
			ApplyFilter();
		}

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
		/// Exports the cached analysis report to a text file chosen by the user.
		/// </summary>
		private void ExportAnalysis()
		{
			if (string.IsNullOrEmpty(cachedDetailReport))
			{
				EditorUtility.DisplayDialog("Export Analysis", "No analysis data available. Run Analyze first.", "OK");
				return;
			}

			string path = EditorUtility.SaveFilePanel(
				"Export Addressables Analysis",
				"",
				"AddressablesAnalysis",
				"txt");

			if (string.IsNullOrEmpty(path)) return;

			File.WriteAllText(path, cachedDetailReport);
			Debug.Log($"[AddressablesDashboard] Analysis report exported to: {path}");
			SetStatus($"Report exported to {Path.GetFileName(path)}");
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

			return $"{entryInfo}  [{pathType}]";
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

			string groupName = EditorInputDialog.Show("Add Group", "Enter new group name:", "New Group");
			if (string.IsNullOrEmpty(groupName)) return;

			var newGroup = settings.CreateGroup(groupName, false, false, true, null, typeof(BundledAssetGroupSchema));
			if (newGroup != null)
			{
				Debug.Log($"[AddressablesDashboard] Created group '{groupName}'.");
				EditorUtility.SetDirty(settings);
				RebuildTree();
			}
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
		/// Handles TreeView selection changes to update the path simulator.
		/// </summary>
		/// <param name="indices">The newly selected indices.</param>
		private void OnTreeSelectionChanged(IEnumerable<int> indices)
		{
			UpdatePathSimulator();
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
	}

	/// <summary>
	/// Simple modal input dialog for editor string prompts.
	/// </summary>
	public class EditorInputDialog : EditorWindow
	{
		private string inputValue = "";
		private string promptMessage = "";
		private bool confirmed;
		private bool initialized;
		private static string result;

		/// <summary>
		/// Shows a modal input dialog and returns the entered string, or null if cancelled.
		/// </summary>
		/// <param name="title">Window title.</param>
		/// <param name="message">Prompt message.</param>
		/// <param name="defaultValue">Default input value.</param>
		/// <returns>The entered string, or null if cancelled.</returns>
		public static string Show(string title, string message, string defaultValue)
		{
			result = null;
			var window = CreateInstance<EditorInputDialog>();
			window.titleContent = new GUIContent(title);
			window.promptMessage = message;
			window.inputValue = defaultValue ?? "";
			window.minSize = new Vector2(300, 100);
			window.maxSize = new Vector2(500, 100);
			window.ShowModal();
			return result;
		}

		/// <summary>
		/// Draws the dialog GUI.
		/// </summary>
		private void OnGUI()
		{
			if (!initialized)
			{
				GUI.FocusControl("InputField");
				initialized = true;
			}

			EditorGUILayout.Space(8);
			EditorGUILayout.LabelField(promptMessage);

			GUI.SetNextControlName("InputField");
			inputValue = EditorGUILayout.TextField(inputValue);

			EditorGUILayout.Space(4);
			EditorGUILayout.BeginHorizontal();
			GUILayout.FlexibleSpace();

			if (GUILayout.Button("OK", GUILayout.Width(80)))
			{
				result = inputValue;
				Close();
			}

			if (GUILayout.Button("Cancel", GUILayout.Width(80)))
			{
				result = null;
				Close();
			}

			EditorGUILayout.EndHorizontal();

			// Handle Enter/Escape keys
			if (Event.current.type == EventType.KeyDown)
			{
				if (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)
				{
					result = inputValue;
					Close();
					Event.current.Use();
				}
				else if (Event.current.keyCode == KeyCode.Escape)
				{
					result = null;
					Close();
					Event.current.Use();
				}
			}
		}
	}
}
#endif
