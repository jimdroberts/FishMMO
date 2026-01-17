namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character pet buff data transfer object.
	/// </summary>
	public struct CharacterPetBuffData
	{
		public long ID { get; set; }
		public long CharacterID { get; set; }
		public int TemplateID { get; set; }
		public int Level { get; set; }
		public float BuffTimeEnd { get; set; }
	}
}