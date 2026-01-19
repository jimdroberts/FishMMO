namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character party membership data transfer object.
	/// </summary>
	public struct CharacterPartyData
	{
		public readonly long ID;
		public readonly long CharacterID;
		public readonly long PartyID;
		public readonly byte Rank;
		public readonly float HealthPCT;

		public CharacterPartyData(long id, long characterID, long partyID, byte rank, float healthPCT)
		{
			ID = id;
			CharacterID = characterID;
			PartyID = partyID;
			Rank = rank;
			HealthPCT = healthPCT;
		}
	}
}