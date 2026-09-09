using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;

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
		/// Describes the item and the slot it is worn in.
		/// </summary>
		/// <remarks>
		/// The slot was not in the tooltip at all, in either the old system or the first pass of
		/// this one — so nothing on screen distinguished a helm from a pair of boots but the name.
		/// </remarks>
		/// <param name="content">The content being assembled.</param>
		public override void BuildTooltip(TooltipContent content, bool describingInstance)
		{
			base.BuildTooltip(content, describingInstance);
			content.AddSubtitle(Slot.ToString());
		}

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

		/// <summary>
		/// Addressable references to equipment mesh variations for this item.
		/// The specific mesh is selected from this list using ModelSeed and ModelPools.
		/// These meshes must be skinned to the master skeleton.
		/// </summary>
		[Tooltip("Mesh variations for this equipment item. Selected via ModelSeed/ModelPools.")]
		public List<AssetReference> EquipmentMeshes = new List<AssetReference>();

		/// <summary>
		/// Body regions hidden when this item is equipped.
		/// For example, a chest plate hides the Torso and Arms regions.
		/// </summary>
		[Tooltip("Body regions to hide when this item is equipped.")]
		public BodyRegion[] HiddenRegions;

		/// <summary>
		/// Optional blend shapes on the equipment mesh that sync with body blend shapes.
		/// When the body's blend shape values change, matching entries here are applied to the equipment renderer.
		/// </summary>
		[Tooltip("Optional blend shape profile for this equipment mesh.")]
		public BlendShapeProfile BlendShapes;
	}
}