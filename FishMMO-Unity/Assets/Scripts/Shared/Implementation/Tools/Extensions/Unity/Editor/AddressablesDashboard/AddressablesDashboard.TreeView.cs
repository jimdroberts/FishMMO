#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.UIElements;

namespace FishMMO.Shared
{
	public partial class AddressablesDashboard
	{
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
			cachedAddressCollisionCount = 0;
			cachedStaleEntryCount = 0;
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

			// Clear previous path-type and violation classes
			element.RemoveFromClassList("group-local");
			element.RemoveFromClassList("group-remote");
			element.RemoveFromClassList("group-mixed");
			element.RemoveFromClassList("entry-violation");
			element.RemoveFromClassList("group-violation");

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

				// Highlight groups that contain violated entries
				if (violationGroupNames.Contains(group.Name))
				{
					element.AddToClassList("group-violation");
				}

				if (iconImage != null)
				{
					iconImage.image = EditorGUIUtility.IconContent("d_Folder Icon").image;
				}
			}
			else if (idToEntry.TryGetValue(itemId, out AddressableAssetEntry entry))
			{
				element.AddToClassList("asset-entry");

				if (nameLabel != null) nameLabel.text = entry.address;
				if (pathLabel != null) pathLabel.text = $"[{entry.address}]  {entry.AssetPath}";
				if (labelsLabel != null) labelsLabel.text = FormatLabels(entry);
				if (sizeLabel != null) sizeLabel.text = FormatBytes(GetAssetFileSize(entry.AssetPath));

				// Highlight entries with analysis violations
				if (violationEntryPaths.Contains(entry.AssetPath))
				{
					element.AddToClassList("entry-violation");
				}

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
	}
}
#endif