#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using FishMMO.Shared.CustomBuildTool.Core;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;

namespace FishMMO.Shared.CustomBuildTool.Addressables
{
	/// <summary>
	/// Stores every asset that two or more bundles of a build would each copy exactly once, in a
	/// group that exists only while the build runs.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The committed groups hold only what the game asks for by name: scenes, the permanent label
	/// sets, AssetReference targets. Everything else is reached through them, and is an implicit
	/// dependency of the bundle that reaches it. That keeps the entry and bundle count low, and a
	/// scene's private content loads and unloads with its scene. An implicit dependency reached from
	/// more than one bundle, though, would be copied into each of them, and only the build knows
	/// which those are: it depends on which groups the build includes and on the server stripping
	/// everything visual. So the build plans itself (<see cref="AddressablesBuildPlan"/>) and
	/// registers exactly those assets here.
	/// </para>
	/// <para>
	/// Assets are packed by the set of bundles that use them (one label per set, PackTogetherByLabel):
	/// what is always needed together is one bundle, and nothing is downloaded or loaded for a bundle
	/// that does not need it. The group's name contains neither "Client" nor "Server", so the build that
	/// created it includes it. It is removed again after the build (<see cref="RemoveGroup"/>), and its
	/// file is gitignored.
	/// </para>
	/// </remarks>
	public static class SharedDependencyBuildGroup
	{
		/// <summary>The build-time group. Its file is gitignored.</summary>
		public const string GroupName = "Build_Shared_Dependencies";

		/// <summary>Prefix of the per-user-set labels this group creates.</summary>
		public const string LabelPrefix = "SharedDeps_";

		/// <summary>
		/// Plans the build and registers every asset more than one of its bundles would copy.
		/// Returns how many were registered.
		/// </summary>
		public static int Register(bool server, List<string> log)
		{
			AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
			if (settings == null)
			{
				log?.Add("Addressables is not initialised, so shared dependencies were not isolated.");
				return 0;
			}
			RemoveGroup();

			// One pass is enough: everything beneath a shared asset is reached from every bundle that reaches
			// the asset, so it is shared by the same bundles or more and is registered in the same pass.
			EditorUtility.DisplayProgressBar("Addressables", $"Planning the {(server ? "server" : "client")} build…", 0.1f);
			AddressablesBuildPlan plan;
			try
			{
				plan = AddressablesBuildPlan.ForProject(settings, server);
			}
			finally
			{
				EditorUtility.ClearProgressBar();
			}
			log?.Add($"Planned the {(server ? "server" : "client")} build: {plan.EntryCount} entries in {plan.BundleEntries.Count} bundles, " +
				$"{plan.ImplicitUsers.Count} implicit dependencies, {plan.Shared.Count} shared.");
			if (plan.Shared.Count == 0) return 0;

			AddressableAssetGroup target = settings.CreateGroup(GroupName, false, false, false, null,
				typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
			var schema = target.GetSchema<BundledAssetGroupSchema>();
			schema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackTogetherByLabel;
			var defaults = settings.DefaultGroup != null ? settings.DefaultGroup.GetSchema<BundledAssetGroupSchema>() : null;
			if (defaults != null)
			{
				schema.BuildPath.SetVariableByName(settings, defaults.BuildPath.GetName(settings));
				schema.LoadPath.SetVariableByName(settings, defaults.LoadPath.GetName(settings));
				schema.Compression = defaults.Compression;
			}

			// Every label first, without events; then the entries.
			var labels = new Dictionary<string, string>(StringComparer.Ordinal);
			foreach (string path in plan.Shared)
			{
				string label = LabelFor(plan.ImplicitUsers[path]);
				labels[path] = label;
			}
			foreach (string label in new HashSet<string>(labels.Values)) settings.AddLabel(label, false);

			int registered = 0;
			long bytes = 0;
			try
			{
				for (int i = 0; i < plan.Shared.Count; i++)
				{
					if (i % 200 == 0)
						EditorUtility.DisplayProgressBar("Addressables", $"Isolating shared dependencies ({i}/{plan.Shared.Count})…", (float)i / plan.Shared.Count);
					string path = plan.Shared[i];
					string guid = AssetDatabase.AssetPathToGUID(path);
					if (string.IsNullOrEmpty(guid) || settings.FindAssetEntry(guid) != null) continue;
					AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, target, false, false);
					if (entry == null) continue;
					entry.SetAddress(path, false);
					entry.SetLabel(labels[path], true, false, false);
					registered++;
					try { bytes += new System.IO.FileInfo(path).Length; } catch (System.IO.IOException) { }
				}
			}
			finally
			{
				EditorUtility.ClearProgressBar();
			}

			if (registered > 0)
			{
				settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification, null, true, true);
				AssetDatabase.SaveAssets();
			}
			log?.Add($"Isolated {registered} shared dependenc{(registered == 1 ? "y" : "ies")} ({bytes / (1024 * 1024)} MB of source) " +
				$"in '{GroupName}' for the {(server ? "server" : "client")} build, so no bundle copies another's content.");
			return registered;
		}

		/// <summary>One stable label per set of bundles that use an asset.</summary>
		public static string LabelFor(IEnumerable<string> bundles)
		{
			var sorted = new List<string>(bundles);
			sorted.Sort(StringComparer.Ordinal);
			using (var sha = SHA1.Create())
			{
				byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", sorted)));
				var sb = new StringBuilder(LabelPrefix);
				for (int i = 0; i < 6; i++) sb.Append(hash[i].ToString("x2"));
				return sb.ToString();
			}
		}

		/// <summary>Removes the build-time group and its labels.</summary>
		public static void RemoveGroup()
		{
			AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
			if (settings == null) return;
			AddressableAssetGroup group = settings.FindGroup(GroupName);
			bool changed = false;
			if (group != null)
			{
				settings.RemoveGroup(group);
				changed = true;
			}
			List<string> labels = settings.GetLabels();
			for (int i = labels.Count - 1; i >= 0; i--)
			{
				if (labels[i].StartsWith(LabelPrefix, StringComparison.Ordinal))
				{
					settings.RemoveLabel(labels[i], false);
					changed = true;
				}
			}
			if (changed) AssetDatabase.SaveAssets();
		}
	}
}
#endif
