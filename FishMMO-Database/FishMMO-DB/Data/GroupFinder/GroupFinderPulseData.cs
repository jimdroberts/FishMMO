namespace FishMMO.Database.Data
{
	/// <summary>
	/// One queue row as the heartbeat left it, with the character's party membership when the
	/// row names a party.
	/// </summary>
	/// <remarks>
	/// The pump's single read. A row matched into a party is only honoured while the character
	/// is still in that party, and the rank they hold in it is what they are told on the way in;
	/// both come from the same statement that refreshed the heartbeat, so the pump needs no
	/// per-row membership read.
	/// </remarks>
	public readonly struct GroupFinderPulseData
	{
		/// <summary>The row, with its heartbeat just refreshed.</summary>
		public readonly GroupFinderQueueData Row;

		/// <summary>
		/// The party the character's membership row names, or 0 when they have none. Read only
		/// for a matched row whose party is not 0; 0 for every other row.
		/// </summary>
		public readonly long MemberPartyID;

		/// <summary>The rank the membership row holds; meaningful only when <see cref="MemberPartyID"/> is not 0.</summary>
		public readonly byte MemberRank;

		public GroupFinderPulseData(GroupFinderQueueData row, long memberPartyID, byte memberRank)
		{
			Row = row;
			MemberPartyID = memberPartyID;
			MemberRank = memberRank;
		}
	}
}
