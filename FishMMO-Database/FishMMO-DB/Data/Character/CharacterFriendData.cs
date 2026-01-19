namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character friend data transfer object.
	/// </summary>
	public struct CharacterFriendData
	{
		public readonly long ID;
		public readonly long CharacterID;
		public readonly long FriendCharacterID;

		public CharacterFriendData(long id, long characterID, long friendCharacterID)
		{
			ID = id;
			CharacterID = characterID;
			FriendCharacterID = friendCharacterID;
		}
	}
}