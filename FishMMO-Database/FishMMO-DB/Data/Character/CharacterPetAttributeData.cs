namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character pet attribute data transfer object.
	/// </summary>
	public struct CharacterPetAttributeData
	{
		public long ID { get; set; }
		public long CharacterID { get; set; }
		public int TemplateID { get; set; }
		public int Value { get; set; }
	}
}