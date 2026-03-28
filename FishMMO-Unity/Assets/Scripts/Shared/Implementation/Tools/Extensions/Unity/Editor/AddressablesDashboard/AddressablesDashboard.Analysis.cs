#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;

namespace FishMMO.Shared
{
	public partial class AddressablesDashboard
	{
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

			if (statAddressCollisions != null)
			{
				statAddressCollisions.text = cachedAddressCollisionCount > 0 ? cachedAddressCollisionCount.ToString() : "—";
				statAddressCollisions.EnableInClassList("stat-value--error", cachedAddressCollisionCount > 0);
			}

			if (statStaleEntries != null)
			{
				statStaleEntries.text = cachedStaleEntryCount > 0 ? cachedStaleEntryCount.ToString() : "—";
				statStaleEntries.EnableInClassList("stat-value--error", cachedStaleEntryCount > 0);
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
		/// unused labels, address collisions, stale entries, total dependency count, and builds a detailed report.
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
				var pluginRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				var allUniqueDeps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

				// Track per-group non-addressable refs for the report
				var groupNonAddrRefs = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

				// Track which labels are actually used by entries
				var usedLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

				// Detect address collisions: address → list of (group, path) tuples
				var addressMap = new Dictionary<string, List<(string group, string path)>>(StringComparer.OrdinalIgnoreCase);

				// Detect stale entries: entries whose source asset no longer exists
				var staleEntries = new List<(string address, string group, string path)>();

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

					// Address collision detection
					string address = entry.address;
					string groupName = entry.parentGroup != null ? entry.parentGroup.Name : "Unknown";

					if (!addressMap.TryGetValue(address, out var addressEntries))
					{
						addressEntries = new List<(string group, string path)>();
						addressMap[address] = addressEntries;
					}
					addressEntries.Add((groupName, entry.AssetPath));

					// Stale entry detection
					string guid = AssetDatabase.AssetPathToGUID(entry.AssetPath);
					if (string.IsNullOrEmpty(guid) || string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(guid)))
					{
						staleEntries.Add((entry.address, groupName, entry.AssetPath));
					}

					string[] deps = AssetDatabase.GetDependencies(entry.AssetPath, true);

					for (int d = 0; d < deps.Length; d++)
					{
						string dep = deps[d];
						if (string.Equals(dep, entry.AssetPath, StringComparison.OrdinalIgnoreCase)) continue;

						// Skip scripts and packages — they are not bundled assets
						if (dep.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
							dep.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
						{
							continue;
						}

						allUniqueDeps.Add(dep);

						// Track plugin references — these are temporary and should be replaced
						if (dep.Replace('\\', '/').StartsWith("Assets/Plugins/", StringComparison.OrdinalIgnoreCase))
						{
							pluginRefs.Add(dep);
						}

						// Track cross-group duplicate deps
						if (!depToGroups.TryGetValue(dep, out HashSet<string> groups))
						{
							groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
							depToGroups[dep] = groups;
						}
						groups.Add(groupName);

						// Track non-addressable references
						if (!addressablePaths.Contains(dep))
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
					if (kvp.Value.Count > 1 &&
						!kvp.Key.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
					{
						duplicateDepCount++;
						duplicateDepList.Add(kvp);
					}
				}

				// Count address collisions (addresses used by more than one entry)
				int addressCollisionCount = 0;
				var addressCollisions = new List<KeyValuePair<string, List<(string group, string path)>>>();
				foreach (var kvp in addressMap)
				{
					if (kvp.Value.Count > 1)
					{
						addressCollisionCount++;
						addressCollisions.Add(kvp);
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
				cachedAddressCollisionCount = addressCollisionCount;
				cachedStaleEntryCount = staleEntries.Count;

				// Build detailed report
				var report = new StringBuilder();

				// Summary
				report.AppendLine("═══ ANALYSIS SUMMARY ═══");
				report.AppendLine($"Scanned {total} addressable entries across {settings.groups.Count} groups.");
				report.AppendLine($"Total unique dependencies: {allUniqueDeps.Count}");
				report.AppendLine($"Cross-group duplicate dependencies: {duplicateDepCount}");
				report.AppendLine($"Non-addressable referenced assets: {nonAddressableRefs.Count}");
				report.AppendLine($"Plugin asset references: {pluginRefs.Count}");
				report.AppendLine($"Address collisions: {addressCollisionCount}");
				report.AppendLine($"Stale entries (missing source): {staleEntries.Count}");
				report.AppendLine($"Unused labels: {unusedLabels.Count}");
				report.AppendLine($"Empty groups: {emptyGroups.Count}");
				report.AppendLine();

				// Address collisions — critical: causes silent runtime load bugs
				if (addressCollisionCount > 0)
				{
					report.AppendLine("═══ ✖ ADDRESS COLLISIONS ═══");
					report.AppendLine("Multiple entries share the same address. LoadAssetAsync will");
					report.AppendLine("pick one arbitrarily, causing silent runtime bugs.\n");

					int shown = 0;
					for (int i = 0; i < addressCollisions.Count; i++)
					{
						if (shown >= 30)
						{
							report.AppendLine($"  … and {addressCollisions.Count - shown} more");
							break;
						}
						var kvp = addressCollisions[i];
						report.AppendLine($"  Address: \"{kvp.Key}\"");
						for (int j = 0; j < kvp.Value.Count; j++)
						{
							report.AppendLine($"    → [{kvp.Value[j].group}] {kvp.Value[j].path}");
						}
						shown++;
					}
					report.AppendLine();
				}

				// Stale entries — critical: break builds
				if (staleEntries.Count > 0)
				{
					report.AppendLine("═══ ✖ STALE ENTRIES ═══");
					report.AppendLine("These entries reference assets that no longer exist or have been moved.");
					report.AppendLine("They will cause build errors.\n");

					int shown = 0;
					for (int i = 0; i < staleEntries.Count; i++)
					{
						if (shown >= 30)
						{
							report.AppendLine($"  … and {staleEntries.Count - shown} more");
							break;
						}
						var e = staleEntries[i];
						report.AppendLine($"  [{e.group}] \"{e.address}\" → {e.path}");
						shown++;
					}
					report.AppendLine();
				}

				// Plugin warnings — shown first since they need developer attention
				if (pluginRefs.Count > 0)
				{
					report.AppendLine("═══ ⚠ PLUGIN ASSET REFERENCES ═══");
					report.AppendLine("Assets under Assets/Plugins/ are temporary placeholders.");
					report.AppendLine("Replace these with production assets. They will NOT be auto-fixed.\n");

					int shown = 0;
					foreach (string path in pluginRefs)
					{
						if (shown >= 20)
						{
							report.AppendLine($"  … and {pluginRefs.Count - shown} more");
							break;
						}
						report.AppendLine($"  • {path}");
						shown++;
					}
					report.AppendLine();
				}

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
				UnityEngine.Debug.Log($"[AddressablesDashboard] Analysis complete — {duplicateDepCount} duplicate dep(s), " +
					$"{nonAddressableRefs.Count} non-addressable ref(s), {pluginRefs.Count} plugin ref(s), " +
					$"{addressCollisionCount} address collision(s), {staleEntries.Count} stale entry(ies), " +
					$"{unusedLabels.Count} unused label(s), {allUniqueDeps.Count} total unique dep(s).");

				SetStatus($"Analysis complete — {duplicateDepCount} dup dep(s), {nonAddressableRefs.Count} non-addr ref(s), " +
					$"{pluginRefs.Count} plugin ref(s), {addressCollisionCount} collision(s), {staleEntries.Count} stale, " +
					$"{unusedLabels.Count} unused label(s), {allUniqueDeps.Count} total dep(s).");

				// Auto-open detail foldout if there are issues
				if (detailFoldout != null && (duplicateDepCount > 0 || nonAddressableRefs.Count > 0 ||
					unusedLabels.Count > 0 || pluginRefs.Count > 0 || addressCollisionCount > 0 || staleEntries.Count > 0))
				{
					detailFoldout.value = true;
				}
			}
			finally
			{
				EditorUtility.ClearProgressBar();
			}
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
			UnityEngine.Debug.Log($"[AddressablesDashboard] Analysis report exported to: {path}");
			SetStatus($"Report exported to {Path.GetFileName(path)}");
		}
	}
}
#endif