using System;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Draws a <see cref="TemplateOfAttribute"/> field as an object field restricted to the named template type, so a core
	/// field typed on <see cref="CachedScriptableObject"/> picks only, say, ability templates while their module is installed.
	/// </summary>
	internal static class TemplateOfTypes
	{
		/// <summary>A list's element type, or the type itself.</summary>
		public static Type GetElementTypeOrSelf(this Type type)
		{
			if (type.IsArray) return type.GetElementType();
			if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(System.Collections.Generic.List<>)) return type.GetGenericArguments()[0];
			return type;
		}
	}

	[CustomPropertyDrawer(typeof(TemplateOfAttribute))]
	public sealed class TemplateOfDrawer : PropertyDrawer
	{
		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			if (property.propertyType != SerializedPropertyType.ObjectReference)
			{
				EditorGUI.PropertyField(position, property, label, true);
				return;
			}
			TemplateOfAttribute templateOf = (TemplateOfAttribute)attribute;
			Type type = templateOf.Resolve() ?? fieldInfo.FieldType.GetElementTypeOrSelf();
			EditorGUI.BeginProperty(position, label, property);
			UnityEngine.Object picked = EditorGUI.ObjectField(position, label, property.objectReferenceValue, type, false);
			if (picked != property.objectReferenceValue)
			{
				property.objectReferenceValue = picked;
			}
			EditorGUI.EndProperty();
		}
	}
}
