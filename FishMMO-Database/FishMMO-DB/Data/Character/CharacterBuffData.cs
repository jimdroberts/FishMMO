namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character buff data transfer object.
	/// </summary>
	public struct CharacterBuffData
	{
		public long ID { get; set; }
		public long CharacterID { get; set; }
		public int TemplateID { get; set; }
		public float RemainingTime { get; set; }
		public float TickTime { get; set; }
		public int Stacks { get; set; }
	}
}