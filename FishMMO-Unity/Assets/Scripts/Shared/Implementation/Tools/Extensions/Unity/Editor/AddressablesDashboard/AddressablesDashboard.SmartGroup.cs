#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

namespace FishMMO.Shared
{
	public partial class AddressablesDashboard
	{
		// ──────────────────────────────────────────────
		// Fix All
		// ──────────────────────────────────────────────

		/// <summary>
		/// Fixes duplicate dependencies and non-addressable references by routing each asset
		/// into a smart group based on its path and context. Shows per-asset confirmation dialogs
		/// with the suggested group and reason, allowing the user to accept, skip, or accept all.
		/// Assets under Plugins/ are warned about but never auto-fixed.
		/// </summary>
		private void FixAll()
		{
			if (cachedDuplicateCount == 0 && cachedNonAddressableRefCount == 0)
			{
				EditorUtility.DisplayDialog("Fix All",
					"No issues to fix. Run Analyze first to detect duplicate dependencies and non-addressable references.",
					"OK");
				return;
			}

			var settings = AddressableAssetSettingsDefaultObject.Settings;
			if (settings == null)
			{
				SetStatus("Cannot fix — Addressable settings not found.");
				return;
			}

			// Collect all assets that need to be made addressable
			var assetsToFix = new List<string>();
			var addressablePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var depToGroups = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
			var pluginWarnings = new List<string>();

			foreach (var group in settings.groups)
			{
				if (group == null) continue;
				foreach (var entry in group.entries)
				{
					if (entry == null) continue;
					addressablePaths.Add(entry.AssetPath);
				}
			}

			foreach (var group in settings.groups)
			{
				if (group == null) continue;
				foreach (var entry in group.entries)
				{
					if (entry == null) continue;

					string[] deps = AssetDatabase.GetDependencies(entry.AssetPath, true);
					string groupName = group.Name;

					for (int d = 0; d < deps.Length; d++)
					{
						string dep = deps[d];
						if (string.Equals(dep, entry.AssetPath, StringComparison.OrdinalIgnoreCase)) continue;

						// Skip scripts, packages, and editor-only assets — they are not bundled
						if (dep.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
							dep.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase) ||
							IsEditorOnlyPath(dep))
						{
							continue;
						}

						// Track cross-group dependencies
						if (!depToGroups.TryGetValue(dep, out HashSet<string> groups))
						{
							groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
							depToGroups[dep] = groups;
						}
						groups.Add(groupName);
					}
				}
			}

			// Gather non-addressable refs
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var kvp in depToGroups)
			{
				if (!addressablePaths.Contains(kvp.Key) && seen.Add(kvp.Key))
				{
					assetsToFix.Add(kvp.Key);
				}
			}

			// Separate plugin warnings from fixable assets using the categorizer,
			// which has exceptions for known runtime plugins (e.g. TextMesh Pro).
			for (int i = assetsToFix.Count - 1; i >= 0; i--)
			{
				AssetCategory cat = CategorizeAsset(assetsToFix[i], null);
				if (cat.IsPluginWarning)
				{
					pluginWarnings.Add(assetsToFix[i]);
					assetsToFix.RemoveAt(i);
				}
			}

			if (assetsToFix.Count == 0 && pluginWarnings.Count == 0)
			{
				EditorUtility.DisplayDialog("Fix All", "No fixable issues found.", "OK");
				return;
			}

			// Show plugin warnings first
			if (pluginWarnings.Count > 0)
			{
				var sb = new StringBuilder();
				sb.AppendLine($"{pluginWarnings.Count} asset(s) under Assets/Plugins/ are referenced but should be replaced with production assets:\n");
				int shown = 0;
				for (int i = 0; i < pluginWarnings.Count; i++)
				{
					if (shown >= 20)
					{
						sb.AppendLine($"  … and {pluginWarnings.Count - shown} more");
						break;
					}
					sb.AppendLine($"  • {pluginWarnings[i]}");
					shown++;
				}
				Debug.LogWarning($"[AddressablesDashboard] Plugin assets referenced:\n{sb}");
				EditorUtility.DisplayDialog("Plugin Assets Warning",
					sb.ToString(),
					"OK");
			}

			if (assetsToFix.Count == 0)
			{
				SetStatus($"No fixable assets (warned about {pluginWarnings.Count} plugin reference(s)).");
				return;
			}

			if (!EditorUtility.DisplayDialog("Fix All",
				$"Found {assetsToFix.Count} asset(s) to review.\n\n" +
				"Each asset will be categorized into an appropriate group based on its path.\n" +
				"You will be prompted for each asset individually.\n\n" +
				"Begin review?",
				"Begin", "Cancel"))
			{
				return;
			}

			try
			{
				var groupCache = new Dictionary<string, AddressableAssetGroup>(StringComparer.OrdinalIgnoreCase);
				int fixedCount = 0;
				int skippedCount = 0;
				int deniedCount = 0;
				bool acceptAll = false;

				for (int i = 0; i < assetsToFix.Count; i++)
				{
					string assetPath = assetsToFix[i];
					string guid = AssetDatabase.AssetPathToGUID(assetPath);
					if (string.IsNullOrEmpty(guid))
					{
						skippedCount++;
						continue;
					}

					// Skip if already addressable
					AddressableAssetEntry existingEntry = settings.FindAssetEntry(guid);
					if (existingEntry != null)
					{
						skippedCount++;
						continue;
					}

					// Categorize the asset
					depToGroups.TryGetValue(assetPath, out HashSet<string> referencingGroups);
					AssetCategory category = CategorizeAsset(assetPath, referencingGroups);

					// Plugin warning — should not reach here, but guard anyway
					if (category.IsPluginWarning || string.IsNullOrEmpty(category.GroupName))
					{
						skippedCount++;
						continue;
					}

					// Build reason with referencing group info
					string reason = category.Reason;
					if (referencingGroups != null && referencingGroups.Count > 1)
					{
						reason += $" (duplicated across {referencingGroups.Count} groups)";
					}

					// Per-asset confirmation unless user chose "Yes All"
					if (!acceptAll)
					{
						// DisplayDialogComplex returns: 0 = OK, 1 = Cancel, 2 = Alt
						int choice = EditorUtility.DisplayDialogComplex(
							$"Fix All — {fixedCount + deniedCount + 1} of {assetsToFix.Count}",
							$"{assetPath}\n\n" +
							$"Reason: {reason}\n" +
							$"Target group: {category.GroupName}",
							"Yes",       // 0
							"Skip",      // 1
							"Yes All");  // 2

						if (choice == 1)
						{
							deniedCount++;
							continue;
						}
						if (choice == 2)
						{
							acceptAll = true;
						}
					}

					AddressableAssetGroup targetGroup = GetOrCreateGroup(settings, category.GroupName, groupCache);
					if (targetGroup == null)
					{
						Debug.LogWarning($"[AddressablesDashboard] Failed to create group '{category.GroupName}' for '{assetPath}'.");
						skippedCount++;
						continue;
					}

					AddressableAssetEntry newEntry = settings.CreateOrMoveEntry(guid, targetGroup, false, false);
					if (newEntry != null)
					{
						newEntry.SetAddress(Path.GetFileNameWithoutExtension(assetPath));
						fixedCount++;
					}
					else
					{
						skippedCount++;
					}
				}

				// Resolve any address collisions introduced by filename-only addresses
				int collisionsResolved = ResolveAddressCollisions(settings);

				EditorUtility.SetDirty(settings);

				string message = $"Fixed {fixedCount} asset(s) across categorized groups.";
				if (collisionsResolved > 0)
				{
					message += $" Resolved {collisionsResolved} address collision(s).";
				}
				if (deniedCount > 0)
				{
					message += $" Skipped {deniedCount} (denied).";
				}
				if (skippedCount > 0)
				{
					message += $" Skipped {skippedCount} (already addressable or invalid).";
				}
				if (pluginWarnings.Count > 0)
				{
					message += $" Warned about {pluginWarnings.Count} plugin reference(s).";
				}

				Debug.Log($"[AddressablesDashboard] {message}");
				SetStatus(message);

				// Refresh tree to show updated state
				RebuildTree();
			}
			catch (Exception ex)
			{
				Debug.LogError($"[AddressablesDashboard] Fix All failed: {ex.Message}");
				SetStatus($"Fix All failed: {ex.Message}");
			}
		}

		// ──────────────────────────────────────────────
		// Address Collision Resolution
		// ──────────────────────────────────────────────

		/// <summary>
		/// Scans all addressable entries and resolves address collisions where multiple entries
		/// of the same asset type share the same address. Disambiguates in two stages:
		///   1. Try using the filename with extension (e.g. "Ethan.fbx" vs "Ethan.prefab").
		///   2. If still colliding, progressively prepend parent directory segments.
		/// Returns the number of addresses that were renamed.
		/// </summary>
		private static int ResolveAddressCollisions(AddressableAssetSettings settings)
		{
			// Collect all entries grouped by (address, mainAssetType)
			var buckets = new Dictionary<(string address, Type type), List<AddressableAssetEntry>>();

			foreach (var group in settings.groups)
			{
				if (group == null) continue;
				foreach (var entry in group.entries)
				{
					if (entry == null) continue;

					Type assetType = AssetDatabase.GetMainAssetTypeAtPath(entry.AssetPath);
					var key = (entry.address, assetType);

					if (!buckets.TryGetValue(key, out var list))
					{
						list = new List<AddressableAssetEntry>();
						buckets[key] = list;
					}
					list.Add(entry);
				}
			}

			int renamedCount = 0;

			foreach (var kvp in buckets)
			{
				List<AddressableAssetEntry> entries = kvp.Value;
				if (entries.Count < 2) continue;

				// Stage 1: Try filename with extension (e.g. "Ethan" → "Ethan.fbx" / "Ethan.prefab")
				var newAddresses = new string[entries.Count];
				for (int i = 0; i < entries.Count; i++)
				{
					newAddresses[i] = Path.GetFileName(entries[i].AssetPath);
				}

				if (AreAllUnique(newAddresses))
				{
					ApplyAddresses(entries, newAddresses, ref renamedCount);
					continue;
				}

				// Stage 2: Progressively prepend parent directory segments
				// Reset to filename with extension as the base
				var segmentLists = new string[entries.Count][];
				for (int i = 0; i < entries.Count; i++)
				{
					string dir = Path.GetDirectoryName(entries[i].AssetPath);
					if (dir != null)
					{
						dir = dir.Replace('\\', '/');
						string[] parts = dir.Split('/');
						// Reverse so index 0 = nearest parent
						Array.Reverse(parts);
						segmentLists[i] = parts;
					}
					else
					{
						segmentLists[i] = Array.Empty<string>();
					}
				}

				int depth = 0;
				int maxDepth = 0;
				for (int i = 0; i < segmentLists.Length; i++)
				{
					if (segmentLists[i].Length > maxDepth)
						maxDepth = segmentLists[i].Length;
				}

				while (depth < maxDepth)
				{
					if (AreAllUnique(newAddresses)) break;

					for (int i = 0; i < entries.Count; i++)
					{
						if (depth < segmentLists[i].Length)
						{
							newAddresses[i] = segmentLists[i][depth] + "/" + newAddresses[i];
						}
					}
					depth++;
				}

				ApplyAddresses(entries, newAddresses, ref renamedCount);
			}

			return renamedCount;
		}

		/// <summary>
		/// Returns true if every string in the array is unique (case-insensitive).
		/// </summary>
		private static bool AreAllUnique(string[] values)
		{
			for (int i = 0; i < values.Length; i++)
			{
				for (int j = i + 1; j < values.Length; j++)
				{
					if (string.Equals(values[i], values[j], StringComparison.OrdinalIgnoreCase))
					{
						return false;
					}
				}
			}
			return true;
		}

		/// <summary>
		/// Applies new addresses to entries, incrementing the renamed counter for each change.
		/// </summary>
		private static void ApplyAddresses(List<AddressableAssetEntry> entries, string[] newAddresses, ref int renamedCount)
		{
			for (int i = 0; i < entries.Count; i++)
			{
				if (!string.Equals(entries[i].address, newAddresses[i], StringComparison.Ordinal))
				{
					entries[i].SetAddress(newAddresses[i]);
					renamedCount++;
				}
			}
		}

		// ──────────────────────────────────────────────
		// Smart Grouping
		// ──────────────────────────────────────────────

		/// <summary>
		/// Asset file extensions that should be made addressable during Smart Group.
		/// </summary>
		private static readonly HashSet<string> AddressableExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			".prefab",
			".asset",
			".unity",
			".mat",
			".rendertexture",
		};

		/// <summary>
		/// Directory segments that should be excluded from Smart Group scanning.
		/// Assets under these directories are not made addressable.
		/// </summary>
		private static readonly string[] ExcludedDirectorySegments =
		{
			"Builds",
			"Build Profiles",
			"Build Settings",
		};

		/// <summary>
		/// Root directories scanned by Smart Group.
		/// </summary>
		private static readonly string[] SmartGroupScanDirectories =
		{
			"Assets/Prefabs",
			"Assets/Scenes",
			"Assets/Templates",
		};

		/// <summary>
		/// Directories that must always exist so new developers have a clear
		/// project layout. Created automatically at the start of Smart Group.
		/// </summary>
		private static readonly string[] RequiredProjectDirectories =
		{
			// ── Client ──
			"Assets/Prefabs/Client/Animations",
			"Assets/Prefabs/Client/Animator",
			"Assets/Prefabs/Client/Audio",
			"Assets/Prefabs/Client/FX",
			"Assets/Prefabs/Client/Icons",
			"Assets/Prefabs/Client/Materials",
			"Assets/Prefabs/Client/Models",
			"Assets/Prefabs/Client/Music",
			"Assets/Prefabs/Client/Sounds",
			"Assets/Prefabs/Client/Textures",

			// ── Shared ──
			"Assets/Prefabs/Shared/Entity",
			"Assets/Prefabs/Shared/Entity/Abilities",
			"Assets/Prefabs/Shared/Entity/Interactables",
			"Assets/Prefabs/Shared/Entity/NPCs",
			"Assets/Prefabs/Shared/Entity/PlayableCharacters",
			"Assets/Prefabs/Shared/Entity/Regions",
			"Assets/Prefabs/Shared/Placeholders",
			"Assets/Prefabs/Shared/Terrain",
			"Assets/Prefabs/Shared/Conditions",
		};

		/// <summary>
		/// Creates every directory listed in <see cref="RequiredProjectDirectories"/>
		/// that does not already exist, walking the path from root to leaf so
		/// intermediate folders are created as well.
		/// </summary>
		private static void EnsureProjectDirectories()
		{
			bool any = false;
			for (int i = 0; i < RequiredProjectDirectories.Length; i++)
			{
				string fullPath = RequiredProjectDirectories[i];
				if (AssetDatabase.IsValidFolder(fullPath))
					continue;

				// Walk segments and create each missing level.
				string[] parts = fullPath.Split('/');
				string current = parts[0]; // "Assets"
				for (int p = 1; p < parts.Length; p++)
				{
					string next = current + "/" + parts[p];
					if (!AssetDatabase.IsValidFolder(next))
					{
						AssetDatabase.CreateFolder(current, parts[p]);
						any = true;
					}
					current = next;
				}
			}
			if (any)
			{
				AssetDatabase.Refresh();
			}
		}

		/// <summary>
		/// Scans Assets/Prefabs, Assets/Scenes, and Assets/Templates, then categorizes
		/// and assigns every discovered asset to the appropriate Addressable group with
		/// correct packing mode and label. Existing entries in the wrong group are moved.
		/// </summary>
		private void SmartGroupAll()
		{
			var settings = AddressableAssetSettingsDefaultObject.Settings;
			if (settings == null)
			{
				EditorUtility.DisplayDialog("Smart Group", "Addressable settings not found.", "OK");
				return;
			}

			// Guarantee all required project directories exist before scanning.
			EnsureProjectDirectories();

			// Discover candidate assets
			var candidates = new List<string>();
			foreach (string dir in SmartGroupScanDirectories)
			{
				if (!AssetDatabase.IsValidFolder(dir)) continue;

				string[] guids = AssetDatabase.FindAssets("", new[] { dir });
				for (int i = 0; i < guids.Length; i++)
				{
					string path = AssetDatabase.GUIDToAssetPath(guids[i]);
					string ext = Path.GetExtension(path);
					if (!AddressableExtensions.Contains(ext)) continue;

					string normalized = path.Replace('\\', '/');

					// Skip excluded directories
					bool excluded = false;
					for (int e = 0; e < ExcludedDirectorySegments.Length; e++)
					{
						if (ContainsSegment(normalized, ExcludedDirectorySegments[e]))
						{
							excluded = true;
							break;
						}
					}
					if (excluded) continue;

					candidates.Add(path);
				}
			}

			if (candidates.Count == 0)
			{
				EditorUtility.DisplayDialog("Smart Group",
					"No addressable assets found in Assets/Prefabs, Assets/Scenes, or Assets/Templates.",
					"OK");
				return;
			}

			if (!EditorUtility.DisplayDialog("Smart Group",
				$"Found {candidates.Count} asset(s) across Prefabs, Scenes, and Templates.\n\n" +
				"This will:\n" +
				"  • Create/move entries into categorized groups\n" +
				"  • Assign labels matching the group name\n" +
				"  • Set packing modes (PackTogether for static, PackSeparately for dynamic/scenes)\n\n" +
				"Proceed?",
				"Proceed", "Cancel"))
			{
				return;
			}

			try
			{
				var groupCache = new Dictionary<string, AddressableAssetGroup>(StringComparer.OrdinalIgnoreCase);
				int created = 0;
				int moved = 0;
				int skipped = 0;
				int labeled = 0;
				var pluginWarnings = new List<string>();

				for (int i = 0; i < candidates.Count; i++)
				{
					if (EditorUtility.DisplayCancelableProgressBar("Smart Group",
						$"Processing {i + 1} / {candidates.Count}…", (float)i / candidates.Count))
					{
						break;
					}

					string assetPath = candidates[i];
					string guid = AssetDatabase.AssetPathToGUID(assetPath);
					if (string.IsNullOrEmpty(guid))
					{
						skipped++;
						continue;
					}

					AssetCategory category = CategorizeAsset(assetPath, null);

					if (category.IsPluginWarning)
					{
						pluginWarnings.Add(assetPath);
						continue;
					}
					if (string.IsNullOrEmpty(category.GroupName))
					{
						skipped++;
						continue;
					}

					AddressableAssetGroup targetGroup = GetOrCreateGroup(settings, category.GroupName, groupCache);
					if (targetGroup == null)
					{
						skipped++;
						continue;
					}

					AddressableAssetEntry existing = settings.FindAssetEntry(guid);
					if (existing != null)
					{
						// Already addressable — check if in correct group
						if (string.Equals(existing.parentGroup.Name, category.GroupName, StringComparison.OrdinalIgnoreCase))
						{
							// Already correct — clean stale labels and ensure the correct one
							SetExclusiveSmartLabel(settings, existing, category.GroupName);
							skipped++;
							continue;
						}

						// Move to correct group
						settings.CreateOrMoveEntry(guid, targetGroup, false, false);

						// Clean stale labels and set the correct one after move
						AddressableAssetEntry movedEntry = settings.FindAssetEntry(guid);
						if (movedEntry != null)
						{
							SetExclusiveSmartLabel(settings, movedEntry, category.GroupName);
						}
						moved++;
					}
					else
					{
						// Create new entry
						AddressableAssetEntry newEntry = settings.CreateOrMoveEntry(guid, targetGroup, false, false);
						if (newEntry != null)
						{
							newEntry.SetAddress(Path.GetFileNameWithoutExtension(assetPath));
							SetExclusiveSmartLabel(settings, newEntry, category.GroupName);
							created++;
						}
						else
						{
							skipped++;
						}
					}
				}

				// Ensure packing modes are correct on all touched groups
				foreach (var kvp in groupCache)
				{
					ApplyGroupPackingMode(kvp.Value, kvp.Key);
				}

				// ── Resolve cross-group duplicate dependencies ──
				// After smart-grouping the primary assets, scan all entries for
				// non-addressable deps that are referenced by 2+ groups. Making
				// these addressable ensures they are bundled once rather than
				// duplicated in every referencing bundle.
				int dupesFixed = 0;
				int dupesPluginSkipped = 0;
				{
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

					var depToGroups = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
					foreach (var group in settings.groups)
					{
						if (group == null) continue;
						foreach (var entry in group.entries)
						{
							if (entry == null) continue;
							string[] deps = AssetDatabase.GetDependencies(entry.AssetPath, true);
							for (int d = 0; d < deps.Length; d++)
							{
								string dep = deps[d];
								if (string.Equals(dep, entry.AssetPath, StringComparison.OrdinalIgnoreCase)) continue;
								if (dep.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
									dep.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase) ||
									IsEditorOnlyPath(dep))
								{
									continue;
								}
								if (addressablePaths.Contains(dep)) continue;

								if (!depToGroups.TryGetValue(dep, out HashSet<string> dg))
								{
									dg = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
									depToGroups[dep] = dg;
								}
								dg.Add(group.Name);
							}
						}
					}

					foreach (var kvp in depToGroups)
					{
						if (kvp.Value.Count <= 1) continue; // only fix cross-group duplicates

						string depPath = kvp.Key;
						string guid = AssetDatabase.AssetPathToGUID(depPath);
						if (string.IsNullOrEmpty(guid)) continue;
						if (settings.FindAssetEntry(guid) != null) continue; // already addressable

						AssetCategory cat = CategorizeAsset(depPath, kvp.Value);
						if (cat.IsPluginWarning || string.IsNullOrEmpty(cat.GroupName))
						{
							if (cat.IsPluginWarning) dupesPluginSkipped++;
							continue;
						}

						AddressableAssetGroup tg = GetOrCreateGroup(settings, cat.GroupName, groupCache);
						if (tg == null) continue;

						AddressableAssetEntry ne = settings.CreateOrMoveEntry(guid, tg, false, false);
						if (ne != null)
						{
							ne.SetAddress(Path.GetFileNameWithoutExtension(depPath));
							SetExclusiveSmartLabel(settings, ne, cat.GroupName);
							dupesFixed++;
						}
					}

					// Apply packing modes on any newly created groups
					foreach (var kvp2 in groupCache)
					{
						ApplyGroupPackingMode(kvp2.Value, kvp2.Key);
					}
				}

				// Resolve any address collisions introduced by filename-only addresses
				int collisionsResolved = ResolveAddressCollisions(settings);

				// ── Clean up empty groups ──
				int removedGroups = 0;
				var groupsToRemove = new List<AddressableAssetGroup>();
				foreach (var group in settings.groups)
				{
					if (group == null) continue;
					if (group == settings.DefaultGroup) continue;
					if (group.entries.Count == 0)
					{
						groupsToRemove.Add(group);
					}
				}
				for (int i = 0; i < groupsToRemove.Count; i++)
				{
					Debug.Log($"[AddressablesDashboard] Removing empty group: {groupsToRemove[i].Name}");
					settings.RemoveGroup(groupsToRemove[i]);
					removedGroups++;
				}

				// ── Clean up unused labels ──
				int removedLabels = 0;
				var usedLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				foreach (var group in settings.groups)
				{
					if (group == null) continue;
					foreach (var entry in group.entries)
					{
						if (entry == null) continue;
						foreach (string label in entry.labels)
						{
							usedLabels.Add(label);
						}
					}
				}
				var allLabels = settings.GetLabels();
				if (allLabels != null)
				{
					for (int i = allLabels.Count - 1; i >= 0; i--)
					{
						if (!usedLabels.Contains(allLabels[i]))
						{
							Debug.Log($"[AddressablesDashboard] Removing unused label: {allLabels[i]}");
							settings.RemoveLabel(allLabels[i]);
							removedLabels++;
						}
					}
				}

				EditorUtility.SetDirty(settings);

				var sb = new StringBuilder();
				sb.Append($"Smart Group complete. Created {created}, moved {moved}");
				if (dupesFixed > 0) sb.Append($", resolved {dupesFixed} duplicate dep(s)");
				if (collisionsResolved > 0) sb.Append($", resolved {collisionsResolved} address collision(s)");
				if (labeled > 0) sb.Append($", labeled {labeled}");
				if (removedGroups > 0) sb.Append($", removed {removedGroups} empty group(s)");
				if (removedLabels > 0) sb.Append($", removed {removedLabels} unused label(s)");
				if (skipped > 0) sb.Append($", skipped {skipped}");
				sb.Append(".");

				if (pluginWarnings.Count > 0 || dupesPluginSkipped > 0)
				{
					int totalPluginWarn = pluginWarnings.Count + dupesPluginSkipped;
					sb.Append($"\n\n{totalPluginWarn} plugin asset(s) skipped (replace with production assets):");
					int shown = 0;
					for (int i = 0; i < pluginWarnings.Count; i++)
					{
						if (shown >= 15)
						{
							sb.Append($"\n  … and {pluginWarnings.Count - shown} more");
							break;
						}
						sb.Append($"\n  • {pluginWarnings[i]}");
						shown++;
					}
					Debug.LogWarning($"[AddressablesDashboard] Plugin assets skipped:\n{sb}");
				}

				string message = sb.ToString();
				Debug.Log($"[AddressablesDashboard] {message}");
				SetStatus(message.Split('\n')[0]);

				RebuildTree();
			}
			catch (Exception ex)
			{
				Debug.LogError($"[AddressablesDashboard] Smart Group failed: {ex.Message}");
				SetStatus($"Smart Group failed: {ex.Message}");
			}
			finally
			{
				EditorUtility.ClearProgressBar();
			}
		}
	}
}
#endif