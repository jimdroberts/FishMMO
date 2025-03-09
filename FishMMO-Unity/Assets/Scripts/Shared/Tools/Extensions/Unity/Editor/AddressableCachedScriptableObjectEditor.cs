using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using System;

namespace FishMMO.Shared
{
	[CustomEditor(typeof(CachedScriptableObject<>), true)]
	public class AddressableCachedScriptableObjectEditor : Editor
	{
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
				if (!entry.labels.Contains(type.Name))
				{
					settings.AddLabel(type.Name);
					entry.labels.Add(type.Name);
					Debug.Log($"Label '{type.Name}' added to asset: {scriptableObject.name}");
				}

				Type baseType = scriptableObject.GetType();

				// Find the base type that matches the generic base class CachedScriptableObject<>
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

			EditorUtility.SetDirty(settings);
			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();

			AddressableAssetGroup group = settings.groups.Find(g => g.Name == lastValidBase.Name);

			if (group == null)
			{
				group = settings.CreateGroup(lastValidBase.Name, false, false, false, settings.DefaultGroup.Schemas);
				Debug.Log($"Group '{lastValidBase.Name}' created.");
			}

			// Add the asset entry to the specific group
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