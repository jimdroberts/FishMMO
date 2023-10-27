using UnityEngine;

namespace FishMMO.Shared
{
	public abstract class CharacterAttributeFormulaTemplate : ScriptableObject
	{
		public abstract int CalculateBonus(CharacterAttribute self, CharacterAttribute bonusAttribute);
	}
}