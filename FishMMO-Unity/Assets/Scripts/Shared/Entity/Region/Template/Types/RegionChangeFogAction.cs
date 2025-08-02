using UnityEngine;
using System;

namespace FishMMO.Shared
{
	/// <summary>
	/// Region action that triggers a change in fog settings for the client when invoked.
	/// </summary>
	[CreateAssetMenu(fileName = "New Region Change Fog Action", menuName = "FishMMO/Region/Region Change Fog", order = 1)]
	public class RegionChangeFogAction : RegionAction
	{
		/// <summary>
		/// Event invoked to notify listeners to change fog settings. Typically handled by client-side systems.
		/// </summary>
		public static event Action<FogSettings> OnChangeFog;

		/// <summary>
		/// The fog settings to apply when this region action is triggered.
		/// </summary>
		public FogSettings FogSettings;

		/// <summary>
		/// Invokes the region action, triggering a fog change for the owning client if conditions are met.
		/// </summary>
		/// <param name="character">The player character triggering the action.</param>
		/// <param name="region">The region in which the action is triggered.</param>
		/// <param name="isReconciling">Indicates if the action is part of a reconciliation process.</param>
		public override void Invoke(IPlayerCharacter character, Region region, bool isReconciling)
		{
#if !UNITY_SERVER
			// Only trigger fog change for the owning client and not during reconciliation.
			if (character != null)
			{
				if (!character.NetworkObject.IsOwner ||
					isReconciling)
				{
					return;
				}
			}

			// Notify listeners to change fog settings.
			OnChangeFog?.Invoke(FogSettings);
#endif
		}
	}
}