namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character inventory item data transfer object.
	/// </summary>
	public struct CharacterInventoryData
	{
		public long ID { get; set; }
		public long CharacterID { get; set; }
		public int TemplateID { get; set; }
		public int Slot { get; set; }
		public int Seed { get; set; }
		public uint Amount { get; set; }
	}
}