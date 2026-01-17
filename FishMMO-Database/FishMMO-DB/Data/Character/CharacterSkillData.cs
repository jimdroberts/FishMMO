namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character skill data transfer object.
	/// </summary>
	public struct CharacterSkillData
	{
		public long ID { get; set; }
		public long CharacterID { get; set; }
		public int TemplateID { get; set; }
		public int Level { get; set; }
		public int Experience { get; set; }
	}
}