#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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

			EditorInputDialog.Show("Rename Group", "Enter new group name:", group.Name, (newName) =>
			{
				if (string.IsNullOrEmpty(newName) || newName == group.Name) return;

				Undo.RecordObject(group, "Rename Addressable Group");
				group.Name = newName;
				EditorUtility.SetDirty(group);
				RebuildTree();
			});
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

			EditorInputDialog.Show("Add New Label", "Enter label name:", "", (newLabel) =>
			{
				if (string.IsNullOrEmpty(newLabel)) return;

				var settings = AddressableAssetSettingsDefaultObject.Settings;
				if (settings == null) return;

				settings.AddLabel(newLabel);
				entry.SetLabel(newLabel, true, true);

				Debug.Log($"[AddressablesDashboard] Created label '{newLabel}' and applied to '{entry.address}'.");
				RebuildTree();
			});
		}

		/// <summary>
		/// Prompts the user to change an entry's address.
		/// </summary>
		/// <param name="entry">The entry to rename.</param>
		private void ChangeAddress(AddressableAssetEntry entry)
		{
			if (entry == null) return;

			EditorInputDialog.Show("Change Address", "Enter new address:", entry.address, (newAddress) =>
			{
				if (string.IsNullOrEmpty(newAddress) || newAddress == entry.address) return;

				entry.SetAddress(newAddress);
				Debug.Log($"[AddressablesDashboard] Changed address to '{newAddress}'.");
				RebuildTree();
			});
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
		// Double-click
		// ──────────────────────────────────────────────

		/// <summary>
		/// Handles double-click on a tree row to select the asset in the Project window,
		/// or ping the group's ScriptableObject.
		/// </summary>
		/// <param name="evt">The pointer down event with clickCount == 2.</param>
		private void OnTreeDoubleClick(PointerDownEvent evt)
		{
			if (evt.button != 0 || evt.clickCount != 2) return;

			var selectedIndices = treeView.selectedIndices.ToList();
			if (selectedIndices.Count == 0) return;

			int selectedId = treeView.GetIdForIndex(selectedIndices[0]);

			if (idToEntry.TryGetValue(selectedId, out AddressableAssetEntry entry))
			{
				SelectEntryInProject(entry);
				evt.StopPropagation();
			}
			else if (idToGroup.TryGetValue(selectedId, out AddressableAssetGroup group))
			{
				EditorGUIUtility.PingObject(group);
				Selection.activeObject = group;
				evt.StopPropagation();
			}
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
	}
}
#endif