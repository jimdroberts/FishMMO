namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character guild membership data transfer object.
	/// </summary>
	public struct CharacterGuildData : IVersioned<CharacterGuildData>
	{
		public readonly long ID;
		public readonly long Version;
		public readonly long CharacterID;
		public readonly long GuildID;
		public readonly byte Rank;
		public readonly string Location;

		long IVersioned<CharacterGuildData>.Version => Version;

		public CharacterGuildData(long id, long characterID, long guildID, byte rank, string location)
			: this(id, version: 0, characterID, guildID, rank, location)
		{
		}

		public CharacterGuildData(long id, long version, long characterID, long guildID, byte rank, string location)
		{
			ID = id;
			Version = version;
			CharacterID = characterID;
			GuildID = guildID;
			Rank = rank;
			Location = location;
		}

		public CharacterGuildData WithVersion(long newVersion)
		{
			return new CharacterGuildData(ID, newVersion, CharacterID, GuildID, Rank, Location);
		}
	}
}