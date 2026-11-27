namespace FishMMO.Database.Data
{
	/// <summary>What <c>ICharacterPartyService.SeatMercenaryAsync</c> decided.</summary>
	public enum MercenarySeatResult : byte
	{
		/// <summary>The mercenary holds a seat in the party now.</summary>
		Seated = 0,

		/// <summary>The character is not a member of that party.</summary>
		NotAMember = 1,

		/// <summary>Another member already has a mercenary seated; a party holds one.</summary>
		PartyHasMercenary = 2,

		/// <summary>Every seat is taken.</summary>
		PartyFull = 3,
	}
}
