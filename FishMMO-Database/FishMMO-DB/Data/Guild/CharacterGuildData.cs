namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character guild membership data transfer object.
	/// </summary>
	public struct CharacterGuildData
	{
		public long ID { get; set; }
		public long CharacterID { get; set; }
		public long GuildID { get; set; }
		public byte Rank { get; set; }
		public string Location { get; set; }
	}
}