using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Region action that changes the skybox material for the owning client when invoked.
	/// </summary>
	[CreateAssetMenu(fileName = "New Region Change Skybox Action", menuName = "FishMMO/Region/Region Change Skybox", order = 1)]
	public class RegionChangeSkyboxAction : RegionAction
	{
		/// <summary>
		/// The skybox material to set when this region action is triggered.
		/// </summary>
		public Material Material;

		/// <summary>
		/// Invokes the region action, setting the skybox material for the owning client if conditions are met.
		/// </summary>
		/// <param name="character">The player character triggering the action.</param>
		/// <param name="region">The region in which the action is triggered.</param>
		/// <param name="isReconciling">Indicates if the action is part of a reconciliation process.</param>
		public override void Invoke(IPlayerCharacter character, Region region, bool isReconciling)
		{
#if !UNITY_SERVER
			// Only change the skybox for the owning client and not during reconciliation.
			if (!character.NetworkObject.IsOwner ||
				isReconciling)
			{
				return;
			}
			// If a material is assigned, set it as the current skybox.
			if (Material != null)
			{
				RenderSettings.skybox = Material;
			}
#endif
		}
	}
}