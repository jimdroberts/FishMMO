using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA event data for region-related triggers. Carries the Region reference
	/// and whether the FishNet prediction system is currently reconciling.
	/// </summary>
	public class RegionEventData : EventData
	{
		/// <summary>
		/// The region that was entered, stayed in, or exited.
		/// </summary>
		public Region Region { get; }

		/// <summary>
		/// Whether the prediction system is currently reconciling. Client-only visual
		/// effects should be suppressed during reconciliation to prevent visual artifacts.
		/// </summary>
		public bool IsReconciling { get; }

		/// <summary>
		/// Creates a new RegionEventData.
		/// </summary>
		/// <param name="initiator">The character triggering the region event.</param>
		/// <param name="region">The region involved.</param>
		/// <param name="isReconciling">Whether the prediction system is reconciling.</param>
		public RegionEventData(ICharacter initiator, Region region, bool isReconciling)
			: base(initiator)
		{
			Region = region;
			IsReconciling = isReconciling;
		}
	}
}