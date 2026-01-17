namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character party membership data transfer object.
	/// </summary>
	public struct CharacterPartyData
	{
		public long ID { get; set; }
		public long CharacterID { get; set; }
		public long PartyID { get; set; }
		public byte Rank { get; set; }
		public float HealthPCT { get; set; }
	}
}