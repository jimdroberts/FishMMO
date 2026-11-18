using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Custom property drawer for ShowReadonlyAttribute. Renders properties as readonly in the inspector.
	/// </summary>
	[CustomPropertyDrawer(typeof(ShowReadonlyAttribute))]
	public class ReadOnlyDrawer : PropertyDrawer
	{
		/// <summary>
		/// Gets the height of the property in the inspector.
		/// </summary>
		/// <param name="property">The serialized property.</param>
		/// <param name="label">The label for the property.</param>
		/// <returns>The height of the property field.</returns>
		public override float GetPropertyHeight(SerializedProperty property,
												GUIContent label)
		{
			return EditorGUI.GetPropertyHeight(property, label, true);
		}

		/// <summary>
		/// Draws the property field in the inspector as readonly (disabled).
		/// </summary>
		/// <param name="position">Rectangle on the screen to use for the property GUI.</param>
		/// <param name="property">The serialized property.</param>
		/// <param name="label">The label for the property.</param>
		public override void OnGUI(Rect position,
								   SerializedProperty property,
								   GUIContent label)
		{
			// Disable GUI to make the property readonly
			GUI.enabled = false;
			EditorGUI.PropertyField(position, property, label, true);
			GUI.enabled = true;
		}
	}
}
