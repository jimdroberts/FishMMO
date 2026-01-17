namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character known ability data transfer object.
	/// </summary>
	public struct CharacterKnownAbilityData
	{
		public long ID { get; set; }
		public long CharacterID { get; set; }
		public int TemplateID { get; set; }
	}
}