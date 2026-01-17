namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character hotkey data transfer object.
	/// </summary>
	public struct CharacterHotkeyData
	{
		public long ID { get; set; }
		public long CharacterID { get; set; }
		public byte Type { get; set; }
		public int Slot { get; set; }
		public long ReferenceID { get; set; }
	}
}