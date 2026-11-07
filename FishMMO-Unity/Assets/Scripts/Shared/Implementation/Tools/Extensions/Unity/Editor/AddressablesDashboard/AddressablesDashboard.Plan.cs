#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.CustomBuildTool.Addressables;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;

namespace FishMMO.Shared
{
	/// <summary>
	/// The rules Analyze, Fix All and Smart Group share: what the game loads by key, which entries
	/// are only weight, and which labels are wrong.
	/// </summary>
	/// <remarks>
	/// An entry earns its place when the game asks for it by name: a scene, an AssetReference target,
	/// an asset in a label set the runtime loads (the permanent sets, FactionTemplate), or anything an
	/// authored, non-smart group or label holds. Everything else is reached through those, and is
	/// cheaper as an implicit dependency: no catalogue entry, no bundle of its own, and when two
	/// bundles of a build share it the build isolates it once (SharedDependencyBuildGroup).
	/// </remarks>
	public partial class AddressablesDashboard
	{
		/// <summary>Labels the runtime loads by name; an entry carrying one is never redundant.</summary>
		private static readonly HashSet<string> RuntimeLoadedLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			SmartGroups.SharedStaticPermanent,
			SmartGroups.ServerStaticPermanent,
			SmartGroups.SharedBiomes,
			"FactionTemplate",
		};

		/// <summary>
		/// True for build output that a build registers in its own temporary group (terrain arrays, baked world
		/// maps and planet surfaces, generated biome art). Those folders are gitignored, so a committed entry for
		/// one dangles on a fresh clone.
		/// </summary>
		private static bool IsBuildOutput(string path)
		{
			string normalized = path.Replace('\\', '/');
			return WorldDesign.ProceduralArtBuildGroup.IsGenerated(normalized) ||
				normalized.StartsWith(TerrainArraySet.BakedDirectory + "/", StringComparison.OrdinalIgnoreCase) ||
				normalized.StartsWith(WorldMapDefinition.BakedDirectory + "/", StringComparison.OrdinalIgnoreCase) ||
				normalized.StartsWith(WorldDesign.PlanetSurfaceBaker.BakedDirectory + "/", StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>The AssetReference targets of everything any build ships: GUID → first referrer.</summary>
		private static Dictionary<string, string> ScanRuntimeKeys(AddressableAssetSettings settings)
		{
			var roots = new HashSet<string>(StringComparer.Ordinal);
			foreach (AddressableAssetGroup group in settings.groups)
			{
				if (group == null) continue;
				foreach (AddressableAssetEntry entry in group.entries)
				{
					if (entry != null && !string.IsNullOrEmpty(entry.AssetPath)) roots.Add(entry.AssetPath);
				}
			}
			var paths = new List<string>();
			foreach (string path in AssetDatabase.GetDependencies(new List<string>(roots).ToArray(), true))
			{
				if (AddressablesBuildPlan.IsBundledPath(path)) paths.Add(path);
			}
			return AssetReferenceScanner.Scan(paths);
		}

		/// <summary>AssetReference targets that are not addressable: path → the asset that references it.</summary>
		private static List<(string path, string referrer)> FindMissingRuntimeKeys(AddressableAssetSettings settings,
			Dictionary<string, string> runtimeKeys)
		{
			var missing = new List<(string, string)>();
			foreach (KeyValuePair<string, string> pair in runtimeKeys)
			{
				string path = AssetDatabase.GUIDToAssetPath(pair.Key);
				if (string.IsNullOrEmpty(path) || settings.FindAssetEntry(pair.Key, true) != null) continue;
				// Registered by the build itself while it runs.
				if (IsBuildOutput(path)) continue;
				missing.Add((path, pair.Value));
			}
			missing.Sort((a, b) => string.CompareOrdinal(a.Item1, b.Item1));
			return missing;
		}

		/// <summary>
		/// Entries nothing asks for by key: scene content (props, materials, textures, terrain chunks, details)
		/// that only its scene, or another asset of the scene, references. Only smart-managed entries are
		/// considered; NavMeshes, AssetReference targets and runtime-loaded labels are kept.
		/// </summary>
		private static List<AddressableAssetEntry> FindRedundantEntries(AddressableAssetSettings settings,
			Dictionary<string, string> runtimeKeys)
		{
			var smartGroups = new HashSet<string>(SmartGroups.All, StringComparer.OrdinalIgnoreCase);
			var redundant = new List<AddressableAssetEntry>();
			foreach (AddressableAssetGroup group in settings.groups)
			{
				if (group == null || (!smartGroups.Contains(group.Name) && group != settings.DefaultGroup)) continue;
				foreach (AddressableAssetEntry entry in group.entries)
				{
					if (entry == null || string.IsNullOrEmpty(entry.AssetPath)) continue;
					string path = entry.AssetPath.Replace('\\', '/');
					if (!path.StartsWith("Assets/Scenes/", StringComparison.OrdinalIgnoreCase) ||
						path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) ||
						AssetDatabase.IsValidFolder(path) || IsNavMeshAsset(path) ||
						runtimeKeys.ContainsKey(entry.guid)) continue;
					bool keep = false;
					foreach (string label in entry.labels)
					{
						if (RuntimeLoadedLabels.Contains(label) || !smartGroups.Contains(label))
						{
							keep = true;
							break;
						}
					}
					if (!keep) redundant.Add(entry);
				}
			}
			return redundant;
		}

		/// <summary>Entries of a smart group carrying another smart group's label (labels decide what is preloaded).</summary>
		private static List<AddressableAssetEntry> FindLabelMismatches(AddressableAssetSettings settings)
		{
			var smartGroups = new HashSet<string>(SmartGroups.All, StringComparer.OrdinalIgnoreCase);
			var mismatched = new List<AddressableAssetEntry>();
			foreach (AddressableAssetGroup group in settings.groups)
			{
				if (group == null || !smartGroups.Contains(group.Name)) continue;
				foreach (AddressableAssetEntry entry in group.entries)
				{
					if (entry == null) continue;
					foreach (string label in entry.labels)
					{
						if (smartGroups.Contains(label) && !string.Equals(label, group.Name, StringComparison.OrdinalIgnoreCase))
						{
							mismatched.Add(entry);
							break;
						}
					}
				}
			}
			return mismatched;
		}

		// ──────────────────────────────────────────────
		// Placement by class (decided before any folder rule)
		// ──────────────────────────────────────────────

		/// <summary>Which builds contain a class's code.</summary>
		internal enum ScriptSide
		{
			/// <summary>Both builds (shared code, Unity's own types).</summary>
			Either,
			/// <summary>Client builds only (FishMMO.Client).</summary>
			Client,
			/// <summary>Server builds only (FishMMO.Server, FishMMO.TestHarness.Server).</summary>
			Server,
		}

		/// <summary>
		/// The builds that contain <paramref name="type"/>. An asset of a server-only class in a group the client loads
		/// fails at run time with "Could not load the file 'FishMMO.Server'" (the catalogue names the class's assembly),
		/// and a client-only class does the same to the server.
		/// </summary>
		internal static ScriptSide SideOf(Type type)
		{
			if (type == null) return ScriptSide.Either;
			string assembly = type.Assembly.GetName().Name;
			if (assembly == "FishMMO.Server" || assembly.EndsWith(".Server", StringComparison.Ordinal)) return ScriptSide.Server;
			if (assembly == "FishMMO.Client" || assembly.StartsWith("FishMMO.Client.", StringComparison.Ordinal)) return ScriptSide.Client;
			return ScriptSide.Either;
		}

		/// <summary>
		/// True for a class the runtime finds only in the <see cref="CachedScriptableObject{T}"/> cache. The boot fills
		/// it from the preloaded labels and nothing else (ClientPostbootSystem adds every loaded ICachedObject), so such
		/// an asset outside them is never found: a weather profile moved to Client_Dynamic left the client with no
		/// weather presentation, detail scatter or weather audio.
		/// </summary>
		internal static bool IsCachedType(Type type) => type != null && typeof(ICachedObject).IsAssignableFrom(type);

		/// <summary>The labels the boot loads for a class's side (FactionTemplate is loaded by label as well).</summary>
		private static string[] PreloadedLabelsFor(ScriptSide side)
		{
			switch (side)
			{
				case ScriptSide.Client: return new[] { SmartGroups.ClientStaticPermanent, SmartGroups.SharedStaticPermanent, SmartGroups.SharedBiomes, "FactionTemplate" };
				case ScriptSide.Server: return new[] { SmartGroups.ServerStaticPermanent, SmartGroups.SharedStaticPermanent, SmartGroups.SharedBiomes, "FactionTemplate" };
				default: return new[] { SmartGroups.ClientStaticPermanent, SmartGroups.SharedStaticPermanent, SmartGroups.ServerStaticPermanent, SmartGroups.SharedBiomes, "FactionTemplate" };
			}
		}

		/// <summary>
		/// Entries placed where the runtime cannot use them: a server-only class in a group the client loads (or the
		/// reverse), or a cached class that no boot preload label covers. Each comes with why and the group
		/// <see cref="CategorizeAsset"/> chooses for it, which Fix All moves it to.
		/// </summary>
		private static List<(AddressableAssetEntry Entry, string Reason, string Group)> FindPlacementErrors(AddressableAssetSettings settings)
		{
			var errors = new List<(AddressableAssetEntry, string, string)>();
			string[] clientExclusions = AddressablesBuildPlan.ExclusionsFor(false);
			string[] serverExclusions = AddressablesBuildPlan.ExclusionsFor(true);
			foreach (AddressableAssetGroup group in settings.groups)
			{
				if (group == null || group.Name == SharedDependencyBuildGroup.GroupName) continue;
				bool clientLoads = CustomBuildTool.Core.ContentBuildFilter.IncludesGroup(group.Name, clientExclusions);
				bool serverLoads = CustomBuildTool.Core.ContentBuildFilter.IncludesGroup(group.Name, serverExclusions);
				foreach (AddressableAssetEntry entry in group.entries)
				{
					if (entry == null || string.IsNullOrEmpty(entry.AssetPath) || AssetDatabase.IsValidFolder(entry.AssetPath)) continue;
					Type type = AssetDatabase.GetMainAssetTypeAtPath(entry.AssetPath);
					ScriptSide side = SideOf(type);
					string reason = null;
					if (side == ScriptSide.Server && clientLoads)
					{
						reason = $"{type.Name} exists only in server code, and the client loads '{group.Name}'";
					}
					else if (side == ScriptSide.Client && serverLoads)
					{
						reason = $"{type.Name} exists only in client code, and the server loads '{group.Name}'";
					}
					else if (type != null && typeof(FishMMO.Shared.Biomes.BiomeTemplate).IsAssignableFrom(type) &&
						!string.Equals(group.Name, SmartGroups.SharedBiomes, StringComparison.OrdinalIgnoreCase))
					{
						reason = $"{type.Name} references its whole art set; preloaded in '{group.Name}' it loads every biome's art at boot";
					}
					else if (IsCachedType(type))
					{
						bool preloaded = false;
						foreach (string label in PreloadedLabelsFor(side))
						{
							if (entry.labels.Contains(label)) { preloaded = true; break; }
						}
						if (!preloaded) reason = $"{type.Name} is found only through the boot preload cache, and no preloaded label covers it";
					}
					if (reason == null) continue;
					string target = CategorizeAsset(entry.AssetPath, null).GroupName;
					if (string.IsNullOrEmpty(target) || string.Equals(target, group.Name, StringComparison.OrdinalIgnoreCase)) target = null;
					errors.Add((entry, reason, target));
				}
			}
			return errors;
		}

		/// <summary>Removes the other smart labels from an entry and gives it its own group's.</summary>
		private static void FixLabel(AddressableAssetSettings settings, AddressableAssetEntry entry)
		{
			if (entry?.parentGroup != null) SetExclusiveSmartLabel(settings, entry, entry.parentGroup.Name);
		}
	}
}
#endif
