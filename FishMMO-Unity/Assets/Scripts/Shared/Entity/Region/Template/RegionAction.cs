using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Abstract base class for region actions. Region actions define behaviors that are triggered when a player interacts with a region.
	/// </summary>
	public abstract class RegionAction : ScriptableObject
	{
		/// <summary>
		/// Invokes the region action for the specified player character and region.
		/// </summary>
		/// <param name="character">The player character triggering the action.</param>
		/// <param name="region">The region in which the action is triggered.</param>
		/// <param name="isReconciling">Indicates if the action is part of a reconciliation process (e.g., network state sync).</param>
		public abstract void Invoke(IPlayerCharacter character, Region region, bool isReconciling);
	}
}