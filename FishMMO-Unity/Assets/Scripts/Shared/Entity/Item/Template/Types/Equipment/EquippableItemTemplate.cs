using UnityEngine;

namespace FishMMO.Shared
{
	public abstract class EquippableItemTemplate : BaseItemTemplate
	{
		public ItemSlot Slot;
		[Tooltip("The maximum number of attributes the item will have when it's generated.")]
		public int MaxItemAttributes;
		[Tooltip("The database of random item attributes that can be added to the item when it's generated.")]
		public ItemAttributeTemplateDatabase[] RandomAttributeDatabases;
		public uint ModelSeed;
		// Different pools for different models
		public int[] ModelPools;
	}
}