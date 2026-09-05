using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.Biomes.Editor
{
	/// <summary>
	/// Draws a <see cref="MinMaxRange"/> marked with <see cref="MinMaxRangeAttribute"/> as
	/// two float fields around a min-max slider, clamped to the attribute's limits.
	/// </summary>
	[CustomPropertyDrawer(typeof(MinMaxRangeAttribute))]
	public class MinMaxDrawer : PropertyDrawer
	{
		private const float LabelWidth = 40f;
		private const float Spacing = 5f;

		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			SerializedProperty minProp = property.FindPropertyRelative("min");
			SerializedProperty maxProp = property.FindPropertyRelative("max");
			if (minProp == null || maxProp == null)
			{
				EditorGUI.PropertyField(position, property, label, true);
				return;
			}

			EditorGUI.BeginProperty(position, label, property);
			position = EditorGUI.PrefixLabel(position, GUIUtility.GetControlID(FocusType.Passive), label);

			var range = (MinMaxRangeAttribute)attribute;
			float minVal = minProp.floatValue;
			float maxVal = maxProp.floatValue;

			float sliderWidth = position.width - (LabelWidth * 2f);
			var minRect = new Rect(position.x, position.y, LabelWidth, position.height);
			var sliderRect = new Rect(minRect.xMax + Spacing, position.y, sliderWidth - (Spacing * 2f), position.height);
			var maxRect = new Rect(sliderRect.xMax + Spacing, position.y, LabelWidth, position.height);

			minVal = EditorGUI.FloatField(minRect, minVal);
			EditorGUI.MinMaxSlider(sliderRect, ref minVal, ref maxVal, range.minLimit, range.maxLimit);
			maxVal = EditorGUI.FloatField(maxRect, maxVal);

			minProp.floatValue = Mathf.Clamp(minVal, range.minLimit, range.maxLimit);
			maxProp.floatValue = Mathf.Clamp(maxVal, range.minLimit, range.maxLimit);
			if (minProp.floatValue > maxProp.floatValue)
			{
				maxProp.floatValue = minProp.floatValue;
			}

			EditorGUI.EndProperty();
		}
	}
}
