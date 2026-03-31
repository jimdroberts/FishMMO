using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA event data for party join and leave events.
	/// </summary>
	public class PartyEventData : EventData
	{
		/// <summary>
		/// The party ID. Zero indicates no party.
		/// </summary>
		public long PartyID { get; }

		/// <summary>
		/// The rank of the character in the party.
		/// </summary>
		public PartyRank Rank { get; }

		/// <summary>
		/// Creates a new PartyEventData.
		/// </summary>
		/// <param name="initiator">The character joining or leaving the party.</param>
		/// <param name="partyID">The party ID.</param>
		/// <param name="rank">The rank in the party.</param>
		public PartyEventData(ICharacter initiator, long partyID, PartyRank rank)
			: base(initiator)
		{
			PartyID = partyID;
			Rank = rank;
		}
	}
}