using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Damage Attribute", menuName = "Character/Attribute/Damage Attribute", order = 1)]
	public class DamageAttributeTemplate : CharacterAttributeTemplate
	{
		public ResistanceAttributeTemplate Resistance;
		public Color DisplayColor;
	}
}