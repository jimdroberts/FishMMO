using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared
{
	[CustomPropertyDrawer(typeof(TemplateReferenceAttribute))]
	public class TemplateReferenceDrawer : PropertyDrawer
	{
		private static readonly Dictionary<Type, Dictionary<int, ScriptableObject>> idToAssetCache = new Dictionary<Type, Dictionary<int, ScriptableObject>>();

		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			if (property.propertyType != SerializedPropertyType.Integer)
			{
				EditorGUI.LabelField(position, label.text, "Use [TemplateReference] with int fields.");
				return;
			}

			TemplateReferenceAttribute attr = (TemplateReferenceAttribute)attribute;
			Type templateType = attr.TemplateType;

			EditorGUI.BeginProperty(position, label, property);

			ScriptableObject current = IDToAsset(templateType, property.intValue);

			EditorGUI.BeginChangeCheck();
			ScriptableObject selected = (ScriptableObject)EditorGUI.ObjectField(position, label, current, templateType, false);
			if (EditorGUI.EndChangeCheck())
			{
				if (selected == null)
				{
					property.intValue = 0;
				}
				else
				{
					property.intValue = ComputeID(selected);
				}
			}

			EditorGUI.EndProperty();
		}

		private static int ComputeID(ScriptableObject obj)
		{
			return (obj.GetType().Name + obj.name).GetDeterministicHashCode();
		}

		private static ScriptableObject IDToAsset(Type templateType, int id)
		{
			if (id == 0)
			{
				return null;
			}

			if (idToAssetCache.TryGetValue(templateType, out Dictionary<int, ScriptableObject> lookup) &&
				lookup.TryGetValue(id, out ScriptableObject cached))
			{
				return cached;
			}

			RebuildCache(templateType);

			if (idToAssetCache.TryGetValue(templateType, out lookup) &&
				lookup.TryGetValue(id, out cached))
			{
				return cached;
			}

			return null;
		}

		private static void RebuildCache(Type templateType)
		{
			Dictionary<int, ScriptableObject> lookup = new Dictionary<int, ScriptableObject>();

			string[] guids = AssetDatabase.FindAssets($"t:{templateType.Name}");
			foreach (string guid in guids)
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				ScriptableObject asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
				if (asset != null && templateType.IsAssignableFrom(asset.GetType()))
				{
					int assetID = ComputeID(asset);
					lookup[assetID] = asset;
				}
			}

			idToAssetCache[templateType] = lookup;
		}

		/// <summary>
		/// Call this to force the drawer to re-scan assets (e.g. after creating or renaming templates).
		/// </summary>
		public static void InvalidateCache()
		{
			idToAssetCache.Clear();
		}
	}
}