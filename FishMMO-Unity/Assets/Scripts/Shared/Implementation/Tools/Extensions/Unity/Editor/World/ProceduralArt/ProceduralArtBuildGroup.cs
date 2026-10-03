#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Generated biome art in Addressables only while a build runs, and only the part of it the build uses.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Generated art is the framework's default art, not content anybody authored.</b> It is rebuilt per
	/// machine from code (<see cref="ProceduralArtPayload.GeneratedRoots"/>, gitignored), committed biomes and
	/// scenes reference it by path-derived GUIDs, and a project's own art replaces it through LOCAL. So it has
	/// no business in the committed Addressables groups: there it lists thousands of files that do not exist on
	/// a fresh clone, churns every time the generator renames something, and ships every placeholder whether
	/// or not anything uses it. Smart Group put 1,947 of them there on 2026-10-02; it now skips the generated
	/// folders and takes out what it finds (<see cref="RemoveCommittedEntries"/>).
	/// </para>
	/// <para>
	/// <b>Only what the build reaches.</b> <see cref="RegisterReachable"/> walks the dependencies of every scene
	/// in the build and every asset in every other addressable group, and registers the generated assets among
	/// them in <see cref="GroupName"/> — a group whose file is gitignored, like the terrain arrays'. A placeholder
	/// nothing references never ships. Registering them at all, rather than leaving them as implicit
	/// dependencies, is what stops a prefab that both a biome (shared bundle) and a scene's terrain (scene bundle)
	/// reference from being copied into both bundles.
	/// </para>
	/// <para>
	/// The group is removed again after the build (<see cref="RemoveGroup"/>), so the project is left as it was
	/// found. Its name contains neither "Client" nor "Server", so both builds keep it.
	/// </para>
	/// </remarks>
	public static class ProceduralArtBuildGroup
	{
		/// <summary>The build-time group. Its file is gitignored.</summary>
		public const string GroupName = "Generated_Biome_Art";

		/// <summary>True for a path under one of the generated roots.</summary>
		public static bool IsGenerated(string path)
		{
			if (string.IsNullOrEmpty(path))
			{
				return false;
			}
			string normalized = path.Replace('\\', '/');
			foreach (string root in ProceduralArtPayload.GeneratedRoots)
			{
				if (normalized.StartsWith(root + "/", StringComparison.Ordinal) || normalized == root)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Registers every generated asset the build reaches in <see cref="GroupName"/>, and nothing else.
		/// Returns how many were registered.
		/// </summary>
		public static int RegisterReachable(List<string> log)
		{
			AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
			if (settings == null)
			{
				log?.Add("Addressables is not initialised, so no generated biome art was registered for the build.");
				return 0;
			}

			// What the build starts from: its scenes, and everything any other group ships.
			var roots = new HashSet<string>(StringComparer.Ordinal);
			foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
			{
				if (scene.enabled && !string.IsNullOrEmpty(scene.path))
				{
					roots.Add(scene.path);
				}
			}
			var gathered = new List<AddressableAssetEntry>();
			foreach (AddressableAssetGroup group in settings.groups)
			{
				if (group == null || group.Name == GroupName)
				{
					continue;
				}
				foreach (AddressableAssetEntry entry in group.entries)
				{
					gathered.Clear();
					entry.GatherAllAssets(gathered, true, true, false);
					foreach (AddressableAssetEntry asset in gathered)
					{
						if (!IsGenerated(asset.AssetPath))
						{
							roots.Add(asset.AssetPath);
						}
					}
				}
			}

			var reached = new List<string>();
			foreach (string dependency in AssetDatabase.GetDependencies(new List<string>(roots).ToArray(), true))
			{
				if (IsGenerated(dependency) && !dependency.EndsWith(".cs", StringComparison.Ordinal))
				{
					reached.Add(dependency);
				}
			}
			reached.Sort(StringComparer.Ordinal);

			AddressableAssetGroup target = settings.FindGroup(GroupName)
				?? settings.CreateGroup(GroupName, false, false, false, null,
					settings.DefaultGroup.Schemas.ConvertAll(schema => schema.GetType()).ToArray());
			int registered = 0;
			foreach (string path in reached)
			{
				string guid = AssetDatabase.AssetPathToGUID(path);
				if (string.IsNullOrEmpty(guid))
				{
					continue;
				}
				AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, target, false, false);
				if (entry != null)
				{
					entry.address = path;
					registered++;
				}
			}
			settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification, null, true, true);
			AssetDatabase.SaveAssets();
			log?.Add($"Registered {registered} generated biome asset(s) the build reaches in '{GroupName}' (of {roots.Count} root asset(s)).");
			return registered;
		}

		/// <summary>Removes the build-time group after a build.</summary>
		public static void RemoveGroup()
		{
			AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
			AddressableAssetGroup group = settings != null ? settings.FindGroup(GroupName) : null;
			if (group != null)
			{
				settings.RemoveGroup(group);
				AssetDatabase.SaveAssets();
			}
		}

		/// <summary>
		/// Takes every generated asset out of every other group: they belong in the build-time group only.
		/// Returns how many entries were removed.
		/// </summary>
		public static int RemoveCommittedEntries(List<string> log)
		{
			AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
			if (settings == null)
			{
				return 0;
			}
			var doomed = new List<AddressableAssetEntry>();
			foreach (AddressableAssetGroup group in settings.groups)
			{
				if (group == null || group.Name == GroupName)
				{
					continue;
				}
				foreach (AddressableAssetEntry entry in group.entries)
				{
					if (IsGenerated(entry.AssetPath))
					{
						doomed.Add(entry);
					}
				}
			}
			foreach (AddressableAssetEntry entry in doomed)
			{
				settings.RemoveAssetEntry(entry.guid, false);
			}
			if (doomed.Count > 0)
			{
				settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification, null, true, true);
				AssetDatabase.SaveAssets();
				log?.Add($"Removed {doomed.Count} generated biome asset(s) from the committed Addressables groups; they are registered at build time only.");
			}
			return doomed.Count;
		}
	}
}
#endif
