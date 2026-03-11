namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character friend data transfer object.
	/// </summary>
	public struct CharacterFriendData : IVersioned<CharacterFriendData>
	{
		/// <summary>
		/// Database row identifier.
		/// </summary>
		public readonly long ID;

		/// <summary>
		/// Logical version for optimistic concurrency control.
		/// </summary>
		public readonly long Version;

		/// <summary>
		/// Owning character identifier.
		/// </summary>
		public readonly long CharacterID;

		/// <summary>
		/// Target character identifier.
		/// </summary>
		public readonly long FriendCharacterID;

		/// <summary>
		/// When false this is a friend relationship; when true the target is blocked.
		/// </summary>
		public readonly bool IsBlocked;

		long IVersioned<CharacterFriendData>.Version => Version;

		/// <summary>
		/// Initializes a new instance without a version (defaults to 0) and not blocked.
		/// </summary>
		public CharacterFriendData(long id, long characterID, long friendCharacterID)
			: this(id, version: 0, characterID, friendCharacterID, isBlocked: false)
		{
		}

		/// <summary>
		/// Initializes a new instance with all fields.
		/// </summary>
		public CharacterFriendData(long id, long version, long characterID, long friendCharacterID, bool isBlocked)
		{
			ID = id;
			Version = version;
			CharacterID = characterID;
			FriendCharacterID = friendCharacterID;
			IsBlocked = isBlocked;
		}

		/// <inheritdoc/>
		public CharacterFriendData WithVersion(long newVersion)
		{
			return new CharacterFriendData(ID, newVersion, CharacterID, FriendCharacterID, IsBlocked);
		}
	}
}