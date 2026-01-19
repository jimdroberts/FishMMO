namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character guild membership data transfer object.
	/// </summary>
	public struct CharacterGuildData
	{
		public readonly long ID;
		public readonly long CharacterID;
		public readonly long GuildID;
		public readonly byte Rank;
		public readonly string Location;

		public CharacterGuildData(long id, long characterID, long guildID, byte rank, string location)
		{
			ID = id;
			CharacterID = characterID;
			GuildID = guildID;
			Rank = rank;
			Location = location;
		}
	}
}