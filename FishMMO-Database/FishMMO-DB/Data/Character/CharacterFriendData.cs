namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character friend data transfer object.
	/// </summary>
	public struct CharacterFriendData
	{
		public readonly long ID;
		public readonly long Version;
		public readonly long CharacterID;
		public readonly long FriendCharacterID;

		public CharacterFriendData(long id, long characterID, long friendCharacterID)
			: this(id, version: 0, characterID, friendCharacterID)
		{
		}

		public CharacterFriendData(long id, long version, long characterID, long friendCharacterID)
		{
			ID = id;
			Version = version;
			CharacterID = characterID;
			FriendCharacterID = friendCharacterID;
		}
	}
}