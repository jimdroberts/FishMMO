using FishMMO.Shared.Core;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Manages visibility of body region SkinnedMeshRenderers.
	/// Hides body parts covered by equipped armor and restores them on unequip.
	/// Also provides access to the character's skeleton root for equipment mesh binding.
	/// </summary>
	public interface IBodyVisibilityManager : ICharacterBehaviour
	{
		/// <summary>
		/// The root Transform of the character skeleton.
		/// Equipment meshes are bound to this skeleton.
		/// </summary>
		Transform SkeletonRoot { get; }

		/// <summary>
		/// Hides the specified body regions. Tracks which slot requested the hide.
		/// </summary>
		/// <param name="regions">Body regions to hide.</param>
		/// <param name="slot">The equipment slot causing the hide.</param>
		void HideRegions(BodyRegion[] regions, ItemSlot slot);

		/// <summary>
		/// Shows body regions previously hidden by the specified slot.
		/// Regions still hidden by other equipped items remain hidden.
		/// </summary>
		/// <param name="slot">The equipment slot whose hidden regions to restore.</param>
		void ShowRegionsForSlot(ItemSlot slot);

		/// <summary>
		/// Shows all body regions regardless of equipped items.
		/// </summary>
		void ShowAllRegions();
	}
}
