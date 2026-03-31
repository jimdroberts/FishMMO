using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA event data for faction change events.
	/// </summary>
	public class FactionEventData : EventData
	{
		/// <summary>
		/// The faction template whose value changed.
		/// </summary>
		public FactionTemplate FactionTemplate { get; }

		/// <summary>
		/// The new faction value.
		/// </summary>
		public int Value { get; }

		/// <summary>
		/// Creates a new FactionEventData.
		/// </summary>
		/// <param name="initiator">The character whose faction changed.</param>
		/// <param name="factionTemplate">The faction template involved.</param>
		/// <param name="value">The new faction value.</param>
		public FactionEventData(ICharacter initiator, FactionTemplate factionTemplate, int value)
			: base(initiator)
		{
			FactionTemplate = factionTemplate;
			Value = value;
		}
	}
}