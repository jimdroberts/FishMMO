using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA event data carrying the ability ID for ability activation events.
	/// </summary>
	public class AbilityEventData : EventData
	{
		/// <summary>
		/// The reference ID of the ability that was activated or completed.
		/// </summary>
		public long AbilityID { get; }

		/// <summary>
		/// Creates a new AbilityEventData.
		/// </summary>
		/// <param name="initiator">The character activating the ability.</param>
		/// <param name="abilityID">The ability reference ID.</param>
		public AbilityEventData(ICharacter initiator, long abilityID)
			: base(initiator)
		{
			AbilityID = abilityID;
		}
	}
}