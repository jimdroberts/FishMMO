#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.CustomBuildTool.Core;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;

namespace FishMMO.Shared.CustomBuildTool.Addressables
{
	/// <summary>
	/// What one Addressables build (client or server) will put in which bundle, worked out from the
	/// group settings before anything is built.
	/// </summary>
	/// <remarks>
	/// <para>
	/// An addressable asset is written once, into its own group's bundle. Anything it uses that is not
	/// addressable in THIS build is an implicit dependency, copied into every bundle that reaches it.
	/// Two things decide "addressable in this build": the group must be included (server builds drop
	/// every group whose name contains "Client", client builds every "Server" one), and its packing mode
	/// decides how many bundles the group becomes (PackSeparately: one per entry). Counting by group
	/// misses both, which is how 46 GB of duplicated textures passed the old check on 2026-11-06.
	/// </para>
	/// <para>
	/// A server build strips everything visual (<see cref="ServerContentPacking"/>: materials, GPU
	/// programs, image textures, sprites, fonts), so the server plan neither records nor follows them.
	/// </para>
	/// </remarks>
	public sealed class AddressablesBuildPlan
	{
		/// <summary>How a group splits into bundles.</summary>
		public enum Packing { Together, Separately, TogetherByLabel }

		/// <summary>One addressable entry as the planner sees it.</summary>
		public readonly struct Entry
		{
			public readonly string Path;
			public readonly string Group;
			public readonly Packing Pack;
			public readonly IReadOnlyCollection<string> Labels;

			public Entry(string path, string group, Packing pack, IReadOnlyCollection<string> labels = null)
			{
				Path = path;
				Group = group;
				Pack = pack;
				Labels = labels ?? Array.Empty<string>();
			}
		}

		/// <summary>Asset lookups, injectable so the planner can be tested without an AssetDatabase.</summary>
		public sealed class Sources
		{
			/// <summary>Direct (non-recursive) dependencies of an asset.</summary>
			public Func<string, string[]> DirectDependencies;
			/// <summary>Main asset type at a path.</summary>
			public Func<string, Type> MainType;
			/// <summary>False for anything a bundle never contains: scripts, package files, editor-only assets.</summary>
			public Func<string, bool> IsBundled;
			/// <summary>True for assets a build registers itself while it runs (generated art), for this build type.</summary>
			public Func<string, bool, bool> IsBuildTimeEntry;
		}

		/// <summary>True for the server build.</summary>
		public bool Server { get; }

		/// <summary>Paths that are explicit (addressable) in this build.</summary>
		public HashSet<string> Explicit { get; } = new HashSet<string>(StringComparer.Ordinal);

		/// <summary>Bundle id → the entries it holds.</summary>
		public Dictionary<string, List<string>> BundleEntries { get; } = new Dictionary<string, List<string>>(StringComparer.Ordinal);

		/// <summary>Implicit dependency → the bundles it would be copied into.</summary>
		public Dictionary<string, SortedSet<string>> ImplicitUsers { get; } = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);

		/// <summary>Implicit dependencies copied into two or more bundles, ordinal order.</summary>
		public List<string> Shared { get; } = new List<string>();

		/// <summary>Number of included entries.</summary>
		public int EntryCount { get; private set; }

		private AddressablesBuildPlan(bool server) { Server = server; }

		/// <summary>The bundle an entry is written to, following Addressables' packing rules.</summary>
		public static string BundleId(Entry entry)
		{
			// A bundle never mixes scenes with other assets; Addressables splits them.
			string kind = entry.Path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) ? "scenes" : "assets";
			switch (entry.Pack)
			{
				case Packing.Separately:
					return entry.Group + "|" + entry.Path;
				case Packing.TogetherByLabel:
					var labels = new List<string>(entry.Labels);
					labels.Sort(StringComparer.Ordinal);
					return entry.Group + "|" + kind + "|" + string.Join(",", labels);
				default:
					return entry.Group + "|" + kind;
			}
		}

		/// <summary>Plans one build from explicit entries.</summary>
		/// <param name="entries">Every entry of every group; excluded groups are filtered here.</param>
		/// <param name="server">True for the server build.</param>
		/// <param name="excludedGroups">The build's group-name exclusions (substring, case-insensitive).</param>
		public static AddressablesBuildPlan Compute(IEnumerable<Entry> entries, bool server,
			IReadOnlyList<string> excludedGroups, Sources sources)
		{
			var plan = new AddressablesBuildPlan(server);
			var typeCache = new Dictionary<string, Type>(StringComparer.Ordinal);
			Type MainType(string path)
			{
				if (!typeCache.TryGetValue(path, out Type type))
				{
					type = sources.MainType(path);
					typeCache.Add(path, type);
				}
				return type;
			}
			var included = new List<Entry>();
			foreach (Entry entry in entries)
			{
				if (string.IsNullOrEmpty(entry.Path) || !ContentBuildFilter.IncludesGroup(entry.Group, excludedGroups)) continue;
				// Server builds strip everything visual even when it is explicit.
				if (server && ServerContentPacking.IsVisualAsset(MainType(entry.Path))) continue;
				included.Add(entry);
				plan.Explicit.Add(entry.Path);
			}
			plan.EntryCount = included.Count;

			var dependencyCache = new Dictionary<string, string[]>(StringComparer.Ordinal);
			string[] Dependencies(string path)
			{
				if (!dependencyCache.TryGetValue(path, out string[] dependencies))
				{
					dependencies = sources.DirectDependencies(path) ?? Array.Empty<string>();
					dependencyCache.Add(path, dependencies);
				}
				return dependencies;
			}
			bool IsExplicit(string path) => plan.Explicit.Contains(path) ||
				(sources.IsBuildTimeEntry != null && sources.IsBuildTimeEntry(path, server));

			foreach (Entry entry in included)
			{
				string bundle = BundleId(entry);
				if (!plan.BundleEntries.TryGetValue(bundle, out List<string> members))
				{
					members = new List<string>();
					plan.BundleEntries.Add(bundle, members);
				}
				members.Add(entry.Path);

				var visited = new HashSet<string>(StringComparer.Ordinal) { entry.Path };
				var pending = new Stack<string>();
				pending.Push(entry.Path);
				while (pending.Count > 0)
				{
					foreach (string dependency in Dependencies(pending.Pop()))
					{
						if (string.IsNullOrEmpty(dependency) || !visited.Add(dependency)) continue;
						if (!sources.IsBundled(dependency)) continue;
						// Referenced, not copied: an explicit asset lives in its own bundle with its own dependencies.
						if (IsExplicit(dependency)) continue;
						if (server && ServerContentPacking.IsVisualAsset(MainType(dependency))) continue;
						if (!plan.ImplicitUsers.TryGetValue(dependency, out SortedSet<string> users))
						{
							users = new SortedSet<string>(StringComparer.Ordinal);
							plan.ImplicitUsers.Add(dependency, users);
						}
						users.Add(bundle);
						pending.Push(dependency);
					}
				}
			}

			foreach (KeyValuePair<string, SortedSet<string>> pair in plan.ImplicitUsers)
			{
				if (pair.Value.Count > 1) plan.Shared.Add(pair.Key);
			}
			plan.Shared.Sort(StringComparer.Ordinal);
			return plan;
		}

		/// <summary>The group-name exclusions a build applies (mirrors CustomBuildTool).</summary>
		public static string[] ExclusionsFor(bool server) => server ? new[] { "Client" } : new[] { "Server" };

		/// <summary>The entries of every bundled group in <paramref name="settings"/>.</summary>
		public static List<Entry> EntriesFrom(AddressableAssetSettings settings, Func<AddressableAssetGroup, bool> skipGroup = null)
		{
			var entries = new List<Entry>();
			if (settings == null) return entries;
			var gathered = new List<AddressableAssetEntry>();
			foreach (AddressableAssetGroup group in settings.groups)
			{
				if (group == null || (skipGroup != null && skipGroup(group))) continue;
				var schema = group.GetSchema<BundledAssetGroupSchema>();
				if (schema == null) continue;
				Packing pack = schema.BundleMode == BundledAssetGroupSchema.BundlePackingMode.PackSeparately ? Packing.Separately
					: schema.BundleMode == BundledAssetGroupSchema.BundlePackingMode.PackTogetherByLabel ? Packing.TogetherByLabel
					: Packing.Together;
				foreach (AddressableAssetEntry entry in group.entries)
				{
					if (entry == null) continue;
					// A folder entry stands for every asset under it.
					gathered.Clear();
					entry.GatherAllAssets(gathered, true, true, false);
					foreach (AddressableAssetEntry asset in gathered)
					{
						if (asset != null && !string.IsNullOrEmpty(asset.AssetPath))
							entries.Add(new Entry(asset.AssetPath, group.Name, pack, new List<string>(entry.labels)));
					}
				}
			}
			return entries;
		}

		/// <summary>The AssetDatabase-backed lookups the editor uses.</summary>
		public static Sources EditorSources()
		{
			return new Sources
			{
				DirectDependencies = path => AssetDatabase.GetDependencies(path, false),
				MainType = AssetDatabase.GetMainAssetTypeAtPath,
				IsBundled = IsBundledPath,
				// Generated biome art is not registered by the build: it is an implicit dependency like any other, so the
				// plan places it with the bundles that use it (and isolates what several share, by that set).
				IsBuildTimeEntry = null,
			};
		}

		/// <summary>
		/// False for paths no bundle contains, or that the planner leaves alone: scripts, package files
		/// (read-only, and shared by Unity's own handling), editor-only folders and the LOCAL directory
		/// while it is disabled.
		/// </summary>
		public static bool IsBundledPath(string path)
		{
			if (string.IsNullOrEmpty(path)) return false;
			string normalized = path.Replace('\\', '/');
			if (!normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) return false;
			if (normalized.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
				normalized.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
				normalized.EndsWith(".asmdef", StringComparison.OrdinalIgnoreCase)) return false;
			if (normalized.IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) >= 0) return false;
			if (!EditorPrefs.GetBool("FishMMOEnableLocalDirectory", false) &&
				normalized.StartsWith("Assets/LOCAL/", StringComparison.OrdinalIgnoreCase)) return false;
			return true;
		}

		/// <summary>Plans the client or server build of the current project settings.</summary>
		public static AddressablesBuildPlan ForProject(AddressableAssetSettings settings, bool server,
			Func<AddressableAssetGroup, bool> skipGroup = null)
		{
			return Compute(EntriesFrom(settings, skipGroup), server, ExclusionsFor(server), EditorSources());
		}
	}
}
#endif
