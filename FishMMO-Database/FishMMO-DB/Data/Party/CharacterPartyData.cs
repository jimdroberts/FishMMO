namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character party membership data transfer object.
	/// </summary>
	public struct CharacterPartyData : IVersioned<CharacterPartyData>
	{
		public readonly long ID;
		public readonly long Version;
		public readonly long CharacterID;
		public readonly long PartyID;
		public readonly byte Rank;
		public readonly float HealthPCT;

		long IVersioned<CharacterPartyData>.Version => Version;

		public CharacterPartyData(long id, long characterID, long partyID, byte rank, float healthPCT)
			: this(id, version: 0, characterID, partyID, rank, healthPCT)
		{
		}

		public CharacterPartyData(long id, long version, long characterID, long partyID, byte rank, float healthPCT)
		{
			ID = id;
			Version = version;
			CharacterID = characterID;
			PartyID = partyID;
			Rank = rank;
			HealthPCT = healthPCT;
		}

		public CharacterPartyData WithVersion(long newVersion)
		{
			return new CharacterPartyData(ID, newVersion, CharacterID, PartyID, Rank, HealthPCT);
		}
	}
}