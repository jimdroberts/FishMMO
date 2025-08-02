using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using System;

namespace FishMMO.Shared
{
	/// <summary>
	/// Custom Unity Editor for CachedScriptableObject assets.
	/// Automatically manages Addressables labels and groups for scriptable objects based on their type and inheritance.
	/// </summary>
	[CustomEditor(typeof(CachedScriptableObject<>), true)]
	public class AddressableCachedScriptableObjectEditor : Editor
	{
		/// <summary>
		/// Called when the editor is enabled. Ensures the target asset is registered with Addressables,
		/// adds appropriate labels for its type and base types, and moves it to the correct Addressables group.
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

			// Find or create the Addressable entry for this asset
			AddressableAssetEntry entry = settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(assetPath));
			if (entry == null)
			{
				entry = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(assetPath), settings.DefaultGroup);
				Debug.Log($"Asset '{scriptableObject.name}' added to Addressables.");
			}

			Type type = scriptableObject.GetType();
			Type lastValidBase = type;

			if (entry != null)
			{
				// Add label for the asset's type if not present
				if (!entry.labels.Contains(type.Name))
				{
					settings.AddLabel(type.Name);
					entry.labels.Add(type.Name);
					Debug.Log($"Label '{type.Name}' added to asset: {scriptableObject.name}");
				}

				Type baseType = scriptableObject.GetType();

				// Traverse the inheritance chain to add labels for each base type until reaching CachedScriptableObject<>
				while (baseType != null && (!baseType.IsGenericType || baseType.GetGenericTypeDefinition() != typeof(CachedScriptableObject<>)))
				{
					// Add the base type label to the entry if it hasn't been added already
					if (!entry.labels.Contains(baseType.Name))
					{
						settings.AddLabel(baseType.Name);
						entry.labels.Add(baseType.Name);
						Debug.Log($"Label '{baseType.Name}' added to asset: {scriptableObject.name}");
					}

					lastValidBase = baseType;

					// Move up the inheritance chain
					baseType = baseType.BaseType;
				}
			}

			// Mark settings as dirty and save changes to ensure Addressables data is updated
			EditorUtility.SetDirty(settings);
			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();

			// Find or create the Addressables group for the last valid base type
			AddressableAssetGroup group = settings.groups.Find(g => g.Name == lastValidBase.Name);

			if (group == null)
			{
				group = settings.CreateGroup(lastValidBase.Name, false, false, false, settings.DefaultGroup.Schemas);
				Debug.Log($"Group '{lastValidBase.Name}' created.");
			}

			// Move the asset entry to the correct group if needed
			if (entry != null && group != null)
			{
				// Move the entry to the group if it is not already in it
				if (entry.parentGroup != group)
				{
					entry.SetAddress(entry.MainAsset.name);
					settings.MoveEntry(entry, group);
					Debug.Log($"Asset '{scriptableObject.name}' added to Addressables group '{lastValidBase.Name}'.");
				}
			}
		}
	}
}