namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character item cooldown data transfer object.
	/// </summary>
	public struct CharacterItemCooldownData
	{
		public long ID { get; set; }
		public long CharacterID { get; set; }
		public int Category { get; set; }
		public float CooldownEnd { get; set; }
	}
}