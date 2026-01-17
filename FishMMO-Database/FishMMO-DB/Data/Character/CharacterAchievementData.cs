namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character achievement data transfer object.
	/// </summary>
	public struct CharacterAchievementData
	{
		public long ID { get; set; }
		public long CharacterID { get; set; }
		public int TemplateID { get; set; }
		public byte Tier { get; set; }
		public uint Value { get; set; }
	}
}