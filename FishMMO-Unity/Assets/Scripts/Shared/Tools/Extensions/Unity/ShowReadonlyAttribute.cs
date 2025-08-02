using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Attribute to mark fields as readonly in the Unity inspector.
	/// Used with a custom property drawer to display fields as non-editable.
	/// </summary>
	public class ShowReadonlyAttribute : PropertyAttribute
	{
		// No fields or methods; serves as a marker for readonly display in the inspector.
	}
}