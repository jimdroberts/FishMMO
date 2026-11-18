using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Damage Attribute", menuName = "FishMMO/Character/Attribute/Damage Attribute", order = 1)]
	/// <summary>
	/// ScriptableObject template for a damage attribute (e.g., Fire, Ice, Physical damage).
	/// Inherits from CharacterAttributeTemplate and adds resistance and display color fields.
	/// </summary>
	public class DamageAttributeTemplate : CharacterAttributeTemplate
	{
		/// <summary>
		/// The resistance attribute template associated with this damage type (e.g., FireResistance for FireDamage).
		/// Used to determine how much of this damage type is mitigated.
		/// </summary>
		public ResistanceAttributeTemplate Resistance;

		/// <summary>
		/// The color used to represent this damage type in the UI (e.g., red for fire, blue for ice).
		/// </summary>
		public Color DisplayColor;
	}
}