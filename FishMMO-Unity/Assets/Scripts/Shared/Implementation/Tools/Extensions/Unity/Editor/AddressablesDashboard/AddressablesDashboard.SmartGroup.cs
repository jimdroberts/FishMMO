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
		/// Fixes what Analyze reports as wrong: registers AssetReference targets that are not addressable
		/// (one confirmation per asset, or Yes All), corrects entries carrying another group's smart label,
		/// and resolves address collisions. Shared dependencies need no fix (the build isolates them) and
		/// redundant entries are Smart Group's to remove. Plugin assets are warned about, never registered.
		/// </summary>
		private void FixAll()
		{
			var settings = AddressableAssetSettingsDefaultObject.Settings;
			if (settings == null)
			{
				SetStatus("Cannot fix — Addressable settings not found.");
				return;
			}
			if (!analysisCurrent)
			{
				EditorUtility.DisplayDialog("Fix All", "Run Analyze first.", "OK");
				return;
			}

			Dictionary<string, string> runtimeKeys = ScanRuntimeKeys(settings);
			List<(string path, string referrer)> missing = FindMissingRuntimeKeys(settings, runtimeKeys);
			List<AddressableAssetEntry> mislabeled = FindLabelMismatches(settings);
			var misplaced = FindPlacementErrors(settings);
			var pluginWarnings = new List<string>();
			for (int i = missing.Count - 1; i >= 0; i--)
			{
				if (CategorizeAsset(missing[i].path, null, true).IsPluginWarning)
				{
					pluginWarnings.Add(missing[i].path);
					missing.RemoveAt(i);
				}
			}

			if (missing.Count == 0 && mislabeled.Count == 0 && misplaced.Count == 0 && cachedAddressCollisionCount == 0 && pluginWarnings.Count == 0)
			{
				EditorUtility.DisplayDialog("Fix All", "No issues to fix.", "OK");
				return;
			}
			if (pluginWarnings.Count > 0)
			{
				var sb = new StringBuilder();
				sb.AppendLine($"{pluginWarnings.Count} AssetReference target(s) under Assets/Plugins/ are not addressable; replace them with production assets:\n");
				for (int i = 0; i < pluginWarnings.Count && i < 20; i++) sb.AppendLine($"  • {pluginWarnings[i]}");
				if (pluginWarnings.Count > 20) sb.AppendLine($"  … and {pluginWarnings.Count - 20} more");
				Debug.LogWarning($"[AddressablesDashboard] Plugin AssetReference targets:\n{sb}");
				EditorUtility.DisplayDialog("Plugin Assets Warning", sb.ToString(), "OK");
			}

			try
			{
				var groupCache = new Dictionary<string, AddressableAssetGroup>(StringComparer.OrdinalIgnoreCase);
				int fixedCount = 0, deniedCount = 0, skippedCount = 0;
				bool acceptAll = false;
				for (int i = 0; i < missing.Count; i++)
				{
					string assetPath = missing[i].path;
					string guid = AssetDatabase.AssetPathToGUID(assetPath);
					if (string.IsNullOrEmpty(guid) || settings.FindAssetEntry(guid) != null) { skippedCount++; continue; }
					AssetCategory category = CategorizeAsset(assetPath, null, true);
					if (string.IsNullOrEmpty(category.GroupName)) { skippedCount++; continue; }
					if (!acceptAll)
					{
						int choice = EditorUtility.DisplayDialogComplex(
							$"Fix All — {i + 1} of {missing.Count}",
							$"{assetPath}\n\nAn AssetReference in {missing[i].referrer} points at it.\n" +
							$"Reason: {category.Reason}\nTarget group: {category.GroupName}",
							"Yes", "Skip", "Yes All");
						if (choice == 1) { deniedCount++; continue; }
						if (choice == 2) acceptAll = true;
					}
					AddressableAssetGroup targetGroup = GetOrCreateGroup(settings, category.GroupName, groupCache);
					AddressableAssetEntry entry = targetGroup != null ? settings.CreateOrMoveEntry(guid, targetGroup, false, false) : null;
					if (entry == null) { skippedCount++; continue; }
					entry.SetAddress(Path.GetFileNameWithoutExtension(assetPath));
					SetExclusiveSmartLabel(settings, entry, category.GroupName);
					fixedCount++;
				}

				// Entries the runtime cannot use where they are (server-only class in a client group, cached class
				// outside the preloaded labels): moved to the group their class requires, after one confirmation.
				int moved = 0, unmovable = 0;
				var movable = misplaced.FindAll(m => m.Group != null);
				unmovable = misplaced.Count - movable.Count;
				if (movable.Count > 0)
				{
					var list = new StringBuilder();
					for (int i = 0; i < movable.Count && i < 15; i++)
						list.AppendLine($"  {movable[i].Entry.parentGroup?.Name} → {movable[i].Group}: {Path.GetFileName(movable[i].Entry.AssetPath)}");
					if (movable.Count > 15) list.AppendLine($"  … and {movable.Count - 15} more (see the Analyze report)");
					if (EditorUtility.DisplayDialog("Fix All — misplaced by class",
							$"Move {movable.Count} entr{(movable.Count == 1 ? "y" : "ies")} to the group their class requires?\n\n{list}", "Move", "Skip"))
					{
						foreach (var m in movable)
						{
							AddressableAssetGroup targetGroup = GetOrCreateGroup(settings, m.Group, groupCache);
							if (targetGroup == null) { unmovable++; continue; }
							AddressableAssetEntry entry = settings.CreateOrMoveEntry(m.Entry.guid, targetGroup, false, false);
							if (entry == null) { unmovable++; continue; }
							SetExclusiveSmartLabel(settings, entry, m.Group);
							moved++;
						}
					}
				}

				foreach (AddressableAssetEntry entry in mislabeled) FixLabel(settings, entry);
				int collisionsResolved = ResolveAddressCollisions(settings);
				EditorUtility.SetDirty(settings);

				string message = $"Registered {fixedCount} AssetReference target(s), moved {moved} misplaced entr{(moved == 1 ? "y" : "ies")}, corrected {mislabeled.Count} label(s)";
				if (unmovable > 0) message += $", left {unmovable} misplaced (no group; see Analyze)";
				if (collisionsResolved > 0) message += $", resolved {collisionsResolved} address collision(s)";
				if (deniedCount > 0) message += $", skipped {deniedCount} (denied)";
				if (skippedCount > 0) message += $", skipped {skippedCount} (already addressable or no group)";
				if (pluginWarnings.Count > 0) message += $", warned about {pluginWarnings.Count} plugin asset(s)";
				message += ".";
				Debug.Log($"[AddressablesDashboard] {message}");
				SetStatus(message);
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
		/// Asset file extensions Smart Group considers as roots. Materials, textures and models are not roots:
		/// they travel with whatever uses them, and the build isolates any two bundles share.
		/// </summary>
		private static readonly HashSet<string> AddressableExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			".prefab",
			".asset",
			".unity",
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

			/* Generated biome art is build output: it is registered at build time, and only what the build
			 * reaches (ProceduralArtBuildGroup). Whatever an earlier Smart Group put in the committed groups
			 * comes out here, and the scan below skips it. */
			FishMMO.Shared.WorldDesign.ProceduralArtBuildGroup.RemoveCommittedEntries(null);

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

					// Build output (generated art, terrain arrays, baked maps and planet surfaces) is registered
					// by the build in its own temporary group and is gitignored: never a committed entry.
					if (IsBuildOutput(normalized)) continue;

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
				"  • Create/move entries into categorized groups (prefabs, assets and scenes only;\n" +
				"    scene content stays with its scene)\n" +
				"  • Assign labels matching the group name\n" +
				"  • Set packing modes (PackTogether for static, PackSeparately for dynamic/scenes)\n" +
				"  • Route the Default Group's project assets by the same rules\n" +
				"  • Offer to remove redundant entries (asked separately)\n\n" +
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
					else if (CustomBuildTool.Core.ServerContentPacking.IsVisualAsset(AssetDatabase.GetMainAssetTypeAtPath(assetPath)))
					{
						// A texture saved as .asset is not a root either.
						skipped++;
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

				// ── Default Group: route project assets by the same rules ──
				// It is included in both builds, so a client model or texture parked there ships to the server.
				// Plugin assets stay: they are warned about, and some are loaded by address on both sides.
				int rerouted = 0;
				if (settings.DefaultGroup != null)
				{
					foreach (AddressableAssetEntry entry in new List<AddressableAssetEntry>(settings.DefaultGroup.entries))
					{
						if (entry == null || entry.AssetPath.StartsWith("Assets/Plugins/", StringComparison.OrdinalIgnoreCase)) continue;
						AssetCategory category = CategorizeAsset(entry.AssetPath, null, true);
						if (category.IsPluginWarning || string.IsNullOrEmpty(category.GroupName)) continue;
						AddressableAssetGroup target = GetOrCreateGroup(settings, category.GroupName, groupCache);
						if (target == null) continue;
						settings.CreateOrMoveEntry(entry.guid, target, false, false);
						AddressableAssetEntry movedEntry = settings.FindAssetEntry(entry.guid);
						if (movedEntry != null) SetExclusiveSmartLabel(settings, movedEntry, category.GroupName);
						rerouted++;
					}
				}

				// ── Redundant entries: scene content nothing loads by key ──
				// Shared dependencies are not fixed here any more: the build isolates whatever two of its bundles share.
				int pruned = 0;
				Dictionary<string, string> runtimeKeys = ScanRuntimeKeys(settings);
				List<AddressableAssetEntry> redundant = FindRedundantEntries(settings, runtimeKeys);
				if (redundant.Count > 0 && EditorUtility.DisplayDialog("Smart Group — redundant entries",
					$"{redundant.Count} entr{(redundant.Count == 1 ? "y is" : "ies are")} scene content that only its scene uses " +
					"(props, materials, textures, terrain chunks, details).\n\n" +
					"Removing them packs each into its scene's bundle, so it loads and unloads with the scene instead of " +
					"being its own bundle or staying resident in the client's permanent group. Anything two bundles share " +
					"is isolated once at build time.\n\nNavMeshes, AssetReference targets and runtime-loaded labels are kept. " +
					"Remove these entries?", "Remove", "Keep"))
				{
					foreach (AddressableAssetEntry entry in redundant)
					{
						if (settings.RemoveAssetEntry(entry.guid, false)) pruned++;
					}
				}
				foreach (AddressableAssetEntry entry in FindLabelMismatches(settings)) FixLabel(settings, entry);

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
				if (rerouted > 0) sb.Append($", re-routed {rerouted} from the Default Group");
				if (pruned > 0) sb.Append($", removed {pruned} redundant entr{(pruned == 1 ? "y" : "ies")}");
				if (collisionsResolved > 0) sb.Append($", resolved {collisionsResolved} address collision(s)");
				if (labeled > 0) sb.Append($", labeled {labeled}");
				if (removedGroups > 0) sb.Append($", removed {removedGroups} empty group(s)");
				if (removedLabels > 0) sb.Append($", removed {removedLabels} unused label(s)");
				if (skipped > 0) sb.Append($", skipped {skipped}");
				sb.Append(".");

				if (pluginWarnings.Count > 0)
				{
					int totalPluginWarn = pluginWarnings.Count;
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