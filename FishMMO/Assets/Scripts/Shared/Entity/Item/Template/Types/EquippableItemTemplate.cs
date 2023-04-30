using UnityEngine;

public abstract class EquippableItemTemplate : BaseItemTemplate
{
	public ItemSlot Slot;
	[Tooltip("The maximum number of attributes the item will have when it's generated.")]
	public int MaxItemAttributes;
	//random item attributes
	public ItemAttributeTemplateDatabase[] AttributeDatabases;
	public uint ModelSeed;
	//different pools for different models
	public int[] ModelPools;
}