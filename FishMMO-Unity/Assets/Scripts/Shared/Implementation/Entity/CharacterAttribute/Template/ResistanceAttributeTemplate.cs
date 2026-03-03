using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Resistance Attribute", menuName = "FishMMO/Character/Attribute/Resistance Attribute", order = 1)]
	/// <summary>
	/// ScriptableObject template for a resistance attribute (e.g., Fire Resistance, Ice Resistance).
	/// Inherits from CharacterAttributeTemplate and is used to define attributes that mitigate specific damage types.
	/// </summary>
	public class ResistanceAttributeTemplate : CharacterAttributeTemplate
	{
	}
}