using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// ScriptableObject template for item attributes, defining min/max values and associated character attribute.
	/// Used to configure item attribute ranges and their effect on character stats.
	/// </summary>
	[CreateAssetMenu(fileName = "New Item Attribute", menuName = "FishMMO/Item/Item Attribute/Attribute", order = 1)]
	public class ItemAttributeTemplate : CachedScriptableObject<ItemAttributeTemplate>, ICachedObject
	{
		/// <summary>
		/// The minimum value for this item attribute.
		/// </summary>
		public int MinValue;

		/// <summary>
		/// The maximum value for this item attribute.
		/// </summary>
		public int MaxValue;

		/// <summary>
		/// The character attribute this item attribute affects or is associated with.
		/// </summary>
		public CharacterAttributeTemplate CharacterAttribute;

		/// <summary>
		/// Gets the name of this item attribute template (defaults to the asset name).
		/// </summary>
		public string Name { get { return this.name; } }
	}
}