using FishMMO.Shared.Core;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Manages character visual appearance: bone scaling for body proportions,
	/// blend shape synchronization across body and equipment, and appearance data serialization.
	/// </summary>
	public interface ICharacterAppearanceManager : ICharacterBehaviour
	{
		/// <summary>
		/// Applies race default proportions via bone scaling.
		/// Called once when the character model is instantiated.
		/// </summary>
		/// <param name="proportions">The race proportion values to apply.</param>
		void ApplyRaceProportions(RaceProportions proportions);

		/// <summary>
		/// Sets a blend shape value on all body region renderers and all equipped equipment renderers
		/// that have a matching blend shape name.
		/// </summary>
		/// <param name="name">Blend shape name (e.g., "Weight", "MuscleMass").</param>
		/// <param name="value">Blend weight (0-100).</param>
		void SetBlendShape(string name, float value);

		/// <summary>
		/// Pushes current body blend shape values to an equipment SkinnedMeshRenderer.
		/// Only applies blend shapes that exist on the equipment mesh.
		/// Called when new equipment is equipped.
		/// </summary>
		/// <param name="equipmentRenderer">The newly equipped item's SkinnedMeshRenderer.</param>
		void SyncBlendShapesToEquipment(SkinnedMeshRenderer equipmentRenderer);

		/// <summary>
		/// Returns a snapshot of the character's current appearance.
		/// </summary>
		/// <returns>A snapshot with all proportions, blend shapes, and equipped item IDs.</returns>
		CharacterAppearanceData GetAppearanceData();

		/// <summary>
		/// Restores appearance from a serialized data snapshot.
		/// </summary>
		/// <param name="data">The appearance data to apply.</param>
		void ApplyAppearanceData(CharacterAppearanceData data);

		/// <summary>
		/// Registers an equipment renderer for blend shape syncing.
		/// Called by EquipmentVisualController when an item is equipped.
		/// </summary>
		/// <param name="renderer">The equipment SkinnedMeshRenderer to register.</param>
		void RegisterEquipmentRenderer(SkinnedMeshRenderer renderer);

		/// <summary>
		/// Unregisters an equipment renderer from blend shape syncing.
		/// Called by EquipmentVisualController when an item is unequipped.
		/// </summary>
		/// <param name="renderer">The equipment SkinnedMeshRenderer to unregister.</param>
		void UnregisterEquipmentRenderer(SkinnedMeshRenderer renderer);
	}
}
