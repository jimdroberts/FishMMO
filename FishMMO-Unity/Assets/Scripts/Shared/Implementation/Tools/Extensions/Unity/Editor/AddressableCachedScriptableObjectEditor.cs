using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Custom Unity Editor for CachedScriptableObject assets.
	/// Automatically registers assets with Addressables, applies the Shared_Static_Permanent label,
	/// and moves them to the Shared_Static_Permanent group.
	/// </summary>
	[CustomEditor(typeof(CachedScriptableObject<>), true)]
	public class AddressableCachedScriptableObjectEditor : Editor
	{
		/// <summary>
		/// Called when the editor is enabled. Ensures the target asset is registered with Addressables,
		/// applies the Shared_Static_Permanent label, and moves it to the correct group.
		/// </summary>
		private void OnEnable()
		{
			var scriptableObject = target;

			AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
			if (settings == null)
			{
				Debug.LogError("Addressable Asset Settings not found.");
				return;
			}

			string assetPath = AssetDatabase.GetAssetPath(scriptableObject);
			string guid = AssetDatabase.AssetPathToGUID(assetPath);

			// Find or create the Addressable entry for this asset
			AddressableAssetEntry entry = settings.FindAssetEntry(guid);
			if (entry == null)
			{
				entry = settings.CreateOrMoveEntry(guid, settings.DefaultGroup);
				Debug.Log($"Asset '{scriptableObject.name}' added to Addressables.");
			}

			if (entry == null) return;

			// Add the Shared_Static_Permanent label
			if (!entry.labels.Contains(Constants.SharedStaticLabel))
			{
				settings.AddLabel(Constants.SharedStaticLabel);
				entry.labels.Add(Constants.SharedStaticLabel);
				Debug.Log($"Label '{Constants.SharedStaticLabel}' added to asset: {scriptableObject.name}");
			}

			// Find or create the Shared_Static_Permanent group
			AddressableAssetGroup group = null;
			foreach (var g in settings.groups)
			{
				if (g != null && g.Name == Constants.SharedStaticLabel)
				{
					group = g;
					break;
				}
			}

			if (group == null)
			{
				group = settings.CreateGroup(Constants.SharedStaticLabel, false, false, false, settings.DefaultGroup.Schemas);
				Debug.Log($"Group '{Constants.SharedStaticLabel}' created.");
			}

			// Move the asset entry to the correct group if needed
			if (group != null && entry.parentGroup != group)
			{
				entry.SetAddress(entry.MainAsset.name);
				settings.MoveEntry(entry, group);
				Debug.Log($"Asset '{scriptableObject.name}' moved to Addressables group '{Constants.SharedStaticLabel}'.");
			}

			EditorUtility.SetDirty(settings);
			AssetDatabase.SaveAssets();
		}
	}
}