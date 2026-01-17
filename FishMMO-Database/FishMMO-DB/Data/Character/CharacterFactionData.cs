namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character faction data transfer object.
	/// </summary>
	public struct CharacterFactionData
	{
		public long ID { get; set; }
		public long CharacterID { get; set; }
		public int TemplateID { get; set; }
		public int Value { get; set; }
	}
}