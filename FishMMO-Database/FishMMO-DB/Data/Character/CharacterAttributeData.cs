namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character attribute data transfer object.
	/// </summary>
	public struct CharacterAttributeData
	{
		public long ID { get; set; }
		public long CharacterID { get; set; }
		public int TemplateID { get; set; }
		public int Value { get; set; }
		public float CurrentValue { get; set; }
	}
}