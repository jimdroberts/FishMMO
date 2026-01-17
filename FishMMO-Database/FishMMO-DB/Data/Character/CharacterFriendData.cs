namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character friend data transfer object.
	/// </summary>
	public struct CharacterFriendData
	{
		public long ID { get; set; }
		public long CharacterID { get; set; }
		public long FriendCharacterID { get; set; }
	}
}