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
		/// Runs the full project analysis. Both builds are simulated bundle by bundle
		/// (<see cref="CustomBuildTool.Addressables.AddressablesBuildPlan"/>): which groups each includes, how
		/// each group splits into bundles, and, for the server, that everything visual is stripped. Reports the
		/// shared dependencies each build will isolate, AssetReference targets that are not addressable,
		/// entries that are only weight, label mismatches, address collisions, stale entries, unused labels and
		/// empty groups.
		/// </summary>
		private void RunAnalysis()
		{
			var settings = AddressableAssetSettingsDefaultObject.Settings;
			if (settings == null)
			{
				SetStatus("Cannot analyze — Addressable settings not found.");
				return;
			}

			SetStatus("Analyzing…");

			try
			{
				var allEntries = new List<AddressableAssetEntry>();
				var usedLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				// (address, assetType) → entries: different types at one address are told apart by LoadAssetAsync<T>.
				var addressMap = new Dictionary<(string address, System.Type type), List<(string group, string path)>>();
				var staleEntries = new List<(string address, string group, string path)>();

				foreach (var group in settings.groups)
				{
					if (group == null) continue;
					foreach (var entry in group.entries)
					{
						if (entry == null || ShouldSkipLocalAsset(entry.AssetPath)) continue;
						allEntries.Add(entry);
						foreach (string entryLabel in entry.labels) usedLabels.Add(entryLabel);

						var addressKey = (entry.address, AssetDatabase.GetMainAssetTypeAtPath(entry.AssetPath));
						if (!addressMap.TryGetValue(addressKey, out var addressEntries))
						{
							addressEntries = new List<(string group, string path)>();
							addressMap[addressKey] = addressEntries;
						}
						addressEntries.Add((group.Name, entry.AssetPath));

						string guid = AssetDatabase.AssetPathToGUID(entry.AssetPath);
						if (string.IsNullOrEmpty(guid) || string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(guid)))
						{
							staleEntries.Add((entry.address, group.Name, entry.AssetPath));
						}
					}
				}

				EditorUtility.DisplayProgressBar("Addressables Analysis", "Planning the client build…", 0.15f);
				var client = CustomBuildTool.Addressables.AddressablesBuildPlan.ForProject(settings, false);
				EditorUtility.DisplayProgressBar("Addressables Analysis", "Planning the server build…", 0.4f);
				var server = CustomBuildTool.Addressables.AddressablesBuildPlan.ForProject(settings, true);
				EditorUtility.DisplayProgressBar("Addressables Analysis", "Scanning AssetReferences…", 0.65f);
				Dictionary<string, string> runtimeKeys = ScanRuntimeKeys(settings);
				List<(string path, string referrer)> missing = FindMissingRuntimeKeys(settings, runtimeKeys);
				EditorUtility.DisplayProgressBar("Addressables Analysis", "Checking entries…", 0.85f);
				List<AddressableAssetEntry> redundant = FindRedundantEntries(settings, runtimeKeys);
				List<AddressableAssetEntry> mislabeled = FindLabelMismatches(settings);
				var misplaced = FindPlacementErrors(settings);

				// Plugin assets either build would ship.
				var allImplicit = new HashSet<string>(client.ImplicitUsers.Keys, StringComparer.Ordinal);
				allImplicit.UnionWith(server.ImplicitUsers.Keys);
				var pluginRefs = new List<string>();
				foreach (string dep in allImplicit)
				{
					if (dep.StartsWith("Assets/Plugins/", StringComparison.OrdinalIgnoreCase) &&
						CategorizeAsset(dep, null).IsPluginWarning) pluginRefs.Add(dep);
				}
				pluginRefs.Sort(StringComparer.Ordinal);

				var addressCollisions = new List<KeyValuePair<(string address, System.Type type), List<(string group, string path)>>>();
				foreach (var kvp in addressMap)
				{
					if (kvp.Value.Count > 1) addressCollisions.Add(kvp);
				}
				var unusedLabels = new List<string>();
				var allLabels = settings.GetLabels();
				if (allLabels != null)
				{
					foreach (string label in allLabels)
					{
						if (!usedLabels.Contains(label)) unusedLabels.Add(label);
					}
				}
				var emptyGroups = new List<string>();
				foreach (var group in settings.groups)
				{
					if (group != null && group.entries.Count == 0) emptyGroups.Add(group.Name);
				}

				// Tree highlighting: entries that need attention.
				violationEntryPaths.Clear();
				violationGroupNames.Clear();
				void Flag(string path, string group)
				{
					violationEntryPaths.Add(path);
					if (!string.IsNullOrEmpty(group)) violationGroupNames.Add(group);
				}
				foreach (var e in staleEntries) Flag(e.path, e.group);
				foreach (var kvp in addressCollisions) foreach (var e in kvp.Value) Flag(e.path, e.group);
				foreach (var e in redundant) Flag(e.AssetPath, e.parentGroup?.Name);
				foreach (var e in mislabeled) Flag(e.AssetPath, e.parentGroup?.Name);
				foreach (var e in misplaced) Flag(e.Entry.AssetPath, e.Entry.parentGroup?.Name);

				cachedDuplicateCount = client.Shared.Count + server.Shared.Count;
				cachedNonAddressableRefCount = missing.Count;
				cachedTotalDepCount = allImplicit.Count;
				cachedUnusedLabelCount = unusedLabels.Count;
				cachedAddressCollisionCount = addressCollisions.Count;
				cachedStaleEntryCount = staleEntries.Count;
				cachedRedundantEntryCount = redundant.Count;
				cachedLabelMismatchCount = mislabeled.Count;
				cachedPlacementErrorCount = misplaced.Count;
				analysisCurrent = true;

				var report = new StringBuilder();
				report.AppendLine("═══ ANALYSIS SUMMARY ═══");
				report.AppendLine($"{allEntries.Count} entries in {settings.groups.Count} groups.");
				AppendPlanSummary(report, "Client", client);
				AppendPlanSummary(report, "Server", server);
				report.AppendLine($"Missing AssetReference targets (fail at run time): {missing.Count}");
				report.AppendLine($"Redundant entries (scene content nothing loads by key): {redundant.Count}");
				report.AppendLine($"Misplaced by class (fail at run time): {misplaced.Count}");
				report.AppendLine($"Label mismatches: {mislabeled.Count}");
				report.AppendLine($"Address collisions: {addressCollisions.Count}");
				report.AppendLine($"Stale entries (missing source): {staleEntries.Count}");
				report.AppendLine($"Plugin asset references: {pluginRefs.Count}");
				report.AppendLine($"Unused labels: {unusedLabels.Count}");
				report.AppendLine($"Empty groups: {emptyGroups.Count}");
				report.AppendLine();

				if (missing.Count > 0)
				{
					report.AppendLine("═══ ✖ MISSING ASSETREFERENCE TARGETS ═══");
					report.AppendLine("An AssetReference points at these, but they are not addressable: loading them throws");
					report.AppendLine("InvalidKeyException at run time. Fix All registers them.\n");
					AppendList(report, missing, 40, m => $"  • {m.path}\n      ← {m.referrer}");
				}
				if (misplaced.Count > 0)
				{
					report.AppendLine("═══ ✖ MISPLACED BY CLASS ═══");
					report.AppendLine("The asset's class decides where it may go: a server-only class in a group the client loads");
					report.AppendLine("(\"Could not load the file 'FishMMO.Server'\"), or a cached class outside the preloaded labels");
					report.AppendLine("(the runtime never finds it). Fix All moves each to the group shown.\n");
					AppendList(report, misplaced, 40, e => $"  [{e.Entry.parentGroup?.Name}] {e.Entry.AssetPath}\n      {e.Reason} → {e.Group ?? "(no rule: move it by hand)"}");
				}
				if (addressCollisions.Count > 0)
				{
					report.AppendLine("═══ ✖ ADDRESS COLLISIONS ═══");
					report.AppendLine("Multiple entries of the same type share an address; LoadAssetAsync<T> picks one arbitrarily.\n");
					AppendList(report, addressCollisions, 30, kvp =>
					{
						var sb = new StringBuilder($"  Address: \"{kvp.Key.address}\"  Type: {(kvp.Key.type != null ? kvp.Key.type.Name : "Unknown")}");
						foreach (var e in kvp.Value) sb.Append($"\n    → [{e.group}] {e.path}");
						return sb.ToString();
					});
				}
				if (staleEntries.Count > 0)
				{
					report.AppendLine("═══ ✖ STALE ENTRIES ═══");
					report.AppendLine("These entries reference assets that no longer exist and will break the build.\n");
					AppendList(report, staleEntries, 30, e => $"  [{e.group}] \"{e.address}\" → {e.path}");
				}
				if (redundant.Count > 0)
				{
					report.AppendLine("═══ ⚠ REDUNDANT ENTRIES ═══");
					report.AppendLine("Scene content only its scene uses. As entries they each add a catalogue entry (and, in a");
					report.AppendLine("PackSeparately group, a bundle) and land render assets in the client's boot-loaded group.");
					report.AppendLine("Left implicit they travel in their scene's bundle. Smart Group removes them.\n");
					AppendList(report, redundant, 30, e => $"  [{e.parentGroup?.Name}] {e.AssetPath}");
				}
				if (mislabeled.Count > 0)
				{
					report.AppendLine("═══ ⚠ LABEL MISMATCHES ═══");
					report.AppendLine("Labels decide what is preloaded; these carry another group's label. Fix All corrects them.\n");
					AppendList(report, mislabeled, 30, e => $"  [{e.parentGroup?.Name}] {e.AssetPath}  labels: {string.Join(", ", e.labels)}");
				}
				AppendShared(report, "CLIENT", client);
				AppendShared(report, "SERVER", server);
				if (pluginRefs.Count > 0)
				{
					report.AppendLine("═══ ⚠ PLUGIN ASSET REFERENCES ═══");
					report.AppendLine("A build ships these plugin assets. Replace them with production assets or add a categorization rule.\n");
					AppendList(report, pluginRefs, 20, p => $"  • {p}");
				}
				if (unusedLabels.Count > 0)
				{
					report.AppendLine("═══ UNUSED LABELS ═══");
					AppendList(report, unusedLabels, 50, l => $"  • {l}");
				}
				if (emptyGroups.Count > 0)
				{
					report.AppendLine("═══ EMPTY GROUPS ═══");
					AppendList(report, emptyGroups, 50, g => $"  • {g}");
				}

				cachedDetailReport = report.ToString();
				UpdateAnalysisStats();

				string summary = $"Analysis complete — client {client.BundleEntries.Count} bundles / {client.Shared.Count} shared, " +
					$"server {server.BundleEntries.Count} bundles / {server.Shared.Count} shared, {missing.Count} missing ref(s), " +
					$"{redundant.Count} redundant, {mislabeled.Count} mislabeled, {addressCollisions.Count} collision(s), {staleEntries.Count} stale.";
				UnityEngine.Debug.Log($"[AddressablesDashboard] {summary}");
				SetStatus(summary);

				if (detailFoldout != null) detailFoldout.value = true;
				if (treeView != null) treeView.RefreshItems();
			}
			finally
			{
				EditorUtility.ClearProgressBar();
			}
		}

		private static void AppendPlanSummary(StringBuilder report, string name,
			CustomBuildTool.Addressables.AddressablesBuildPlan plan)
		{
			long bytes = 0;
			foreach (string path in plan.Shared) bytes += FileSize(path);
			report.AppendLine($"{name} build: {plan.EntryCount} entries in {plan.BundleEntries.Count} bundles; " +
				$"{plan.ImplicitUsers.Count} implicit dependencies, {plan.Shared.Count} shared by 2+ bundles " +
				$"({FormatBytes(bytes)} of source) — isolated once at build time.");
		}

		private static void AppendShared(StringBuilder report, string name,
			CustomBuildTool.Addressables.AddressablesBuildPlan plan)
		{
			if (plan.Shared.Count == 0) return;
			report.AppendLine($"═══ SHARED DEPENDENCIES — {name} BUILD ═══");
			report.AppendLine("Two or more bundles reach these. The build stores each once in a temporary group");
			report.AppendLine("(Build_Shared_Dependencies); without that they would be copied into every one of them.\n");
			var ranked = new List<(long weight, string path, int users)>();
			foreach (string path in plan.Shared)
			{
				int users = plan.ImplicitUsers[path].Count;
				ranked.Add((FileSize(path) * (users - 1), path, users));
			}
			ranked.Sort((a, b) => b.weight.CompareTo(a.weight));
			AppendList(report, ranked, 25, r => $"  • {r.path}  ×{r.users} bundles");
		}

		private static void AppendList<T>(StringBuilder report, IList<T> items, int max, Func<T, string> format)
		{
			for (int i = 0; i < items.Count && i < max; i++) report.AppendLine(format(items[i]));
			if (items.Count > max) report.AppendLine($"  … and {items.Count - max} more");
			report.AppendLine();
		}

		private static long FileSize(string path)
		{
			try { return new FileInfo(path).Length; }
			catch (IOException) { return 0; }
			catch (UnauthorizedAccessException) { return 0; }
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