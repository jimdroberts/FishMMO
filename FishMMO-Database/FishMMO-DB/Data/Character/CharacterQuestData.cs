namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character quest data transfer object.
	/// </summary>
	public struct CharacterQuestData
	{
		public long ID { get; set; }
		public long CharacterID { get; set; }
		public int TemplateID { get; set; }
		public int Progress { get; set; }
		public bool Completed { get; set; }
	}
}