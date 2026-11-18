using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Attribute for <see cref="SerializeReference"/> fields that enables a type-selection dropdown
	/// in the Unity Inspector. Supports polymorphic serialization of plain C# class hierarchies.
	/// Apply to fields or list elements decorated with <see cref="SerializeReference"/>.
	/// </summary>
	public class SubclassSelectorAttribute : PropertyAttribute
	{
	}
}