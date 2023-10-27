using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Item Attribute", menuName = "Item/Item Attribute/Attribute", order = 1)]
	public class ItemAttributeTemplate : CachedScriptableObject<ItemAttributeTemplate>
	{
		public int MinValue;
		public int MaxValue;
		public CharacterAttributeTemplate CharacterAttribute;

		public string Name { get { return this.name; } }
	}
}