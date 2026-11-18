using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;

namespace FishMMO.Shared
{
	public partial class AddressablesDashboard
	{
		/// <summary>
		/// Where an asset of a client-only backdrop goes (<see cref="WorldDesign.ClientBackdropScenes"/>): the scene in the
		/// client scene group, what the boot's cache must find (its atlas entry, its biome map) in the client's permanent
		/// group, anything loaded by key in the client's dynamic group, and the rest packed into the scene's bundle. Nothing
		/// of it reaches a group a server build carries, and its NavMesh (no server hosts it) is no entry at all.
		/// </summary>
		private static AssetCategory CategorizeBackdropAsset(string normalized, bool runtimeKey)
		{
			if (normalized.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
			{
				return new AssetCategory { GroupName = SmartGroups.SceneClient, Reason = "Client backdrop scene — drawn only by the client, never hosted" };
			}
			if (IsNavMeshAsset(normalized))
			{
				return new AssetCategory { GroupName = null, Reason = "Client backdrop NavMesh — no server hosts the scene" };
			}
			Type mainType = AssetDatabase.GetMainAssetTypeAtPath(normalized);
			if (IsCachedType(mainType))
			{
				return new AssetCategory { GroupName = SmartGroups.ClientStaticPermanent, Reason = $"Client backdrop {mainType.Name} — found through the client's boot cache" };
			}
			if (runtimeKey)
			{
				return new AssetCategory { GroupName = SmartGroups.ClientDynamic, Reason = "Client backdrop asset loaded by key" };
			}
			return new AssetCategory { GroupName = null, Reason = "Client backdrop content — packed into its scene's bundle" };
		}

		/// <summary>
		/// Moves every Addressables entry a client backdrop owns to its client-side group (or out of Addressables when it is
		/// packed with the scene or not needed), for entries made before the scene was flagged. Returns how many changed.
		/// </summary>
		internal static int MoveClientBackdropEntries(AddressableAssetSettings settings, List<string> notes)
		{
			if (settings == null)
			{
				return 0;
			}
			WorldDesign.ClientBackdropScenes.Invalidate();
			var owned = new List<AddressableAssetEntry>();
			foreach (AddressableAssetGroup group in settings.groups)
			{
				if (group == null)
				{
					continue;
				}
				foreach (AddressableAssetEntry entry in group.entries)
				{
					if (entry != null && WorldDesign.ClientBackdropScenes.Owns(entry.AssetPath))
					{
						owned.Add(entry);
					}
				}
			}
			var groups = new Dictionary<string, AddressableAssetGroup>(StringComparer.OrdinalIgnoreCase);
			int changed = 0;
			foreach (AddressableAssetEntry entry in owned)
			{
				string path = entry.AssetPath;
				AssetCategory category = CategorizeBackdropAsset(path.Replace('\\', '/'), true);
				if (string.IsNullOrEmpty(category.GroupName))
				{
					settings.RemoveAssetEntry(entry.guid);
					notes?.Add($"Addressables: removed '{path}' ({category.Reason}).");
					changed++;
					continue;
				}
				if (entry.parentGroup != null && string.Equals(entry.parentGroup.Name, category.GroupName, StringComparison.OrdinalIgnoreCase)
					&& entry.labels.Contains(category.GroupName))
				{
					continue;
				}
				AddressableAssetGroup target = GetOrCreateGroup(settings, category.GroupName, groups);
				if (target == null)
				{
					throw new InvalidOperationException($"Could not create Addressables group '{category.GroupName}'.");
				}
				AddressableAssetEntry moved = settings.CreateOrMoveEntry(entry.guid, target);
				SetExclusiveSmartLabel(settings, moved, category.GroupName);
				notes?.Add($"Addressables: '{path}' → {category.GroupName}.");
				changed++;
			}
			if (changed > 0)
			{
				EditorUtility.SetDirty(settings);
			}
			return changed;
		}
	}
}
