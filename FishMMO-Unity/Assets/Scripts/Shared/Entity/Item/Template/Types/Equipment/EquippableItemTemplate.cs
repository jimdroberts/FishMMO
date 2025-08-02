using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Abstract base class for equippable item templates, defining slot, attributes, and model data.
	/// Used for equipment items such as armor and weapons.
	/// </summary>
	public abstract class EquippableItemTemplate : BaseItemTemplate
	{
		/// <summary>
		/// The equipment slot this item can be equipped to (e.g., head, chest, weapon).
		/// </summary>
		public ItemSlot Slot;

		/// <summary>
		/// The maximum number of attributes the item will have when it's generated.
		/// </summary>
		[Tooltip("The maximum number of attributes the item will have when it's generated.")]
		public int MaxItemAttributes;

		/// <summary>
		/// The databases of random item attributes that can be added to the item when it's generated.
		/// </summary>
		[Tooltip("The database of random item attributes that can be added to the item when it's generated.")]
		public ItemAttributeTemplateDatabase[] RandomAttributeDatabases;

		/// <summary>
		/// The seed value used for model randomization and selection.
		/// </summary>
		public uint ModelSeed;

		/// <summary>
		/// Pools of models for different model variations.
		/// Used to select different visual models for the item.
		/// </summary>
		public int[] ModelPools;
	}
}