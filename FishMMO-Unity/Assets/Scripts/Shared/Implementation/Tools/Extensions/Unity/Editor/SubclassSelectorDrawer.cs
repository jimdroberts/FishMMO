using System;
using System.Collections.Generic;
using System.Linq;
using FishNet.Object;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Custom property drawer for <see cref="SubclassSelectorAttribute"/>.
	/// Renders a type-selection dropdown for <see cref="SerializeReference"/> fields,
	/// enabling polymorphic inline editing in the Unity Inspector.
	/// </summary>
	[CustomPropertyDrawer(typeof(SubclassSelectorAttribute))]
	public class SubclassSelectorDrawer : PropertyDrawer
	{
		/// <summary>
		/// Static cache of derived types per base type. Populated once per base type and reused across all OnGUI calls.
		/// </summary>
		private static readonly Dictionary<Type, List<Type>> derivedTypeCache = new Dictionary<Type, List<Type>>();

		/// <summary>
		/// Static cache of display name arrays per base type for the dropdown popup.
		/// </summary>
		private static readonly Dictionary<Type, string[]> displayNameCache = new Dictionary<Type, string[]>();

		/// <summary>
		/// Clears all cached type data after a domain reload so newly compiled types are discovered.
		/// </summary>
		[InitializeOnLoadMethod]
		private static void OnDomainReload()
		{
			derivedTypeCache.Clear();
			displayNameCache.Clear();
		}

		/// <summary>
		/// Returns the total height required to render the property.
		/// Includes the type dropdown line plus all visible child fields when expanded.
		/// </summary>
		/// <param name="property">The serialized property.</param>
		/// <param name="label">The label for the property.</param>
		/// <returns>The height of the property field including children when expanded.</returns>
		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
		{
			float height = EditorGUIUtility.singleLineHeight;

			if (property.isExpanded && property.hasVisibleChildren)
			{
				SerializedProperty iterator = property.Copy();
				SerializedProperty endProperty = iterator.GetEndProperty();

				if (iterator.NextVisible(true))
				{
					do
					{
						if (SerializedProperty.EqualContents(iterator, endProperty))
						{
							break;
						}
						height += EditorGUI.GetPropertyHeight(iterator, true) + EditorGUIUtility.standardVerticalSpacing;
					}
					while (iterator.NextVisible(false));
				}
			}

			return height;
		}

		/// <summary>
		/// Draws a foldout with a type-selection dropdown, followed by the child fields of the selected type.
		/// The foldout arrow controls expand/collapse while the dropdown selects the concrete type.
		/// </summary>
		/// <param name="position">Rectangle on the screen to use for the property GUI.</param>
		/// <param name="property">The serialized property decorated with <see cref="SerializeReference"/>.</param>
		/// <param name="label">The label for the property.</param>
		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			EditorGUI.BeginProperty(position, label, property);

			Type baseType = GetManagedReferenceFieldType(property);
			if (baseType == null)
			{
				EditorGUI.PropertyField(position, property, label, true);
				EditorGUI.EndProperty();
				return;
			}

			List<Type> derivedTypes = GetDerivedTypes(baseType);
			string[] typeNames = GetDisplayNames(baseType, derivedTypes);
			Type currentType = GetManagedReferenceValueType(property);

			// Find the currently selected index.
			int selectedIndex = 0;
			if (currentType != null)
			{
				for (int i = 0; i < derivedTypes.Count; ++i)
				{
					if (derivedTypes[i] == currentType)
					{
						selectedIndex = i + 1;
						break;
					}
				}
			}

			// Draw foldout arrow in the label area.
			Rect foldoutRect = new Rect(position.x, position.y, EditorGUIUtility.labelWidth, EditorGUIUtility.singleLineHeight);
			property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, label, true);

			// Draw type dropdown to the right of the label.
			Rect popupRect = new Rect(
				position.x + EditorGUIUtility.labelWidth + 2f,
				position.y,
				position.width - EditorGUIUtility.labelWidth - 2f,
				EditorGUIUtility.singleLineHeight);
			int newIndex = EditorGUI.Popup(popupRect, selectedIndex, typeNames);

			// Handle type change.
			if (newIndex != selectedIndex)
			{
				if (newIndex == 0)
				{
					property.managedReferenceValue = null;
				}
				else
				{
					Type newType = derivedTypes[newIndex - 1];
					property.managedReferenceValue = Activator.CreateInstance(newType);
				}
				property.serializedObject.ApplyModifiedProperties();
			}

			// Draw child fields when expanded.
			if (property.isExpanded && property.hasVisibleChildren)
			{
				EditorGUI.indentLevel++;

				SerializedProperty iterator = property.Copy();
				SerializedProperty endProperty = iterator.GetEndProperty();
				float y = position.y + EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

				if (iterator.NextVisible(true))
				{
					do
					{
						if (SerializedProperty.EqualContents(iterator, endProperty))
						{
							break;
						}

						float h = EditorGUI.GetPropertyHeight(iterator, true);
						Rect childRect = new Rect(position.x, y, position.width, h);
						DrawChildProperty(childRect, iterator);
						y += h + EditorGUIUtility.standardVerticalSpacing;
					}
					while (iterator.NextVisible(false));
				}

				EditorGUI.indentLevel--;
			}

			EditorGUI.EndProperty();
		}

		/// <summary>
		/// Draws a child property, with prefab GameObject selection support for NetworkObject fields.
		/// </summary>
		/// <param name="position">Rectangle on the screen to use for the property GUI.</param>
		/// <param name="property">The child serialized property to draw.</param>
		private static void DrawChildProperty(Rect position, SerializedProperty property)
		{
			if (IsNetworkObjectReferenceProperty(property))
			{
				DrawNetworkObjectPrefabField(position, property);
				return;
			}

			EditorGUI.PropertyField(position, property, true);
		}

		/// <summary>
		/// Draws a NetworkObject reference as a prefab GameObject picker and stores the selected NetworkObject component.
		/// </summary>
		/// <param name="position">Rectangle on the screen to use for the property GUI.</param>
		/// <param name="property">The NetworkObject reference property.</param>
		private static void DrawNetworkObjectPrefabField(Rect position, SerializedProperty property)
		{
			NetworkObject currentNetworkObject = property.objectReferenceValue as NetworkObject;
			GameObject currentGameObject = currentNetworkObject == null ? null : currentNetworkObject.gameObject;
			EditorGUI.BeginChangeCheck();
			GameObject selectedGameObject = EditorGUI.ObjectField(position, property.displayName, currentGameObject, typeof(GameObject), false) as GameObject;
			if (!EditorGUI.EndChangeCheck())
			{
				return;
			}

			property.objectReferenceValue = selectedGameObject == null ? null : selectedGameObject.GetComponent<NetworkObject>();
		}

		/// <summary>
		/// Returns whether the property is a FishNet NetworkObject reference.
		/// </summary>
		/// <param name="property">The serialized property to inspect.</param>
		/// <returns>True if the property stores a NetworkObject reference.</returns>
		private static bool IsNetworkObjectReferenceProperty(SerializedProperty property)
		{
			return property.propertyType == SerializedPropertyType.ObjectReference &&
				(property.type.Contains("NetworkObject") || property.objectReferenceValue is NetworkObject);
		}

		/// <summary>
		/// Gets all concrete types that derive from the specified base type. Results are cached after the first lookup.
		/// </summary>
		/// <param name="baseType">The base type to find derived types for.</param>
		/// <returns>A cached sorted list of concrete types that can be instantiated.</returns>
		private static List<Type> GetDerivedTypes(Type baseType)
		{
			if (derivedTypeCache.TryGetValue(baseType, out List<Type> cached))
			{
				return cached;
			}

			List<Type> types = AppDomain.CurrentDomain.GetAssemblies()
				.SelectMany(assembly =>
				{
					try { return assembly.GetTypes(); }
					catch { return Array.Empty<Type>(); }
				})
				.Where(type => baseType.IsAssignableFrom(type) && !type.IsAbstract && !type.IsInterface)
				.OrderBy(type => type.Name)
				.ToList();

			derivedTypeCache[baseType] = types;
			return types;
		}

		/// <summary>
		/// Gets the cached display name array for the type dropdown, including a leading "(None)" entry.
		/// </summary>
		/// <param name="baseType">The base type used as cache key.</param>
		/// <param name="derivedTypes">The list of derived types to generate names for.</param>
		/// <returns>A cached string array of display names for the popup.</returns>
		private static string[] GetDisplayNames(Type baseType, List<Type> derivedTypes)
		{
			if (displayNameCache.TryGetValue(baseType, out string[] cached))
			{
				return cached;
			}

			string[] names = new string[derivedTypes.Count + 1];
			names[0] = "(None)";
			for (int i = 0; i < derivedTypes.Count; ++i)
			{
				names[i + 1] = derivedTypes[i].Name;
			}

			displayNameCache[baseType] = names;
			return names;
		}

		/// <summary>
		/// Extracts the base field type from a <see cref="SerializeReference"/> property's managed reference type string.
		/// </summary>
		/// <param name="property">The serialized property.</param>
		/// <returns>The base type of the managed reference field, or null if not found.</returns>
		private static Type GetManagedReferenceFieldType(SerializedProperty property)
		{
			string typeName = property.managedReferenceFieldTypename;
			if (string.IsNullOrEmpty(typeName))
			{
				return null;
			}
			return ParseManagedReferenceTypeName(typeName);
		}

		/// <summary>
		/// Extracts the current concrete type from a <see cref="SerializeReference"/> property's managed reference value.
		/// </summary>
		/// <param name="property">The serialized property.</param>
		/// <returns>The concrete type of the current value, or null if unassigned.</returns>
		private static Type GetManagedReferenceValueType(SerializedProperty property)
		{
			string typeName = property.managedReferenceFullTypename;
			if (string.IsNullOrEmpty(typeName))
			{
				return null;
			}
			return ParseManagedReferenceTypeName(typeName);
		}

		/// <summary>
		/// Parses a Unity managed reference type string in the format "AssemblyName TypeNamespace.TypeName" into a <see cref="Type"/>.
		/// </summary>
		/// <param name="managedReferenceTypeName">The managed reference type string from Unity serialization.</param>
		/// <returns>The resolved <see cref="Type"/>, or null if resolution fails.</returns>
		private static Type ParseManagedReferenceTypeName(string managedReferenceTypeName)
		{
			int splitIndex = managedReferenceTypeName.IndexOf(' ');
			if (splitIndex < 0)
			{
				return null;
			}

			string assemblyName = managedReferenceTypeName.Substring(0, splitIndex);
			string typeName = managedReferenceTypeName.Substring(splitIndex + 1);

			var assembly = AppDomain.CurrentDomain.GetAssemblies()
				.FirstOrDefault(a => a.GetName().Name == assemblyName);

			return assembly?.GetType(typeName);
		}
	}
}