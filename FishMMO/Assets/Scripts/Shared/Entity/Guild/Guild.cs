using System.Collections.Generic;

public class Guild
{
	public const int MAX_GUILD_SIZE = 100;
	public const int MAX_GUILDNAME_LENGTH = 32;

	public string ID;
	public long LeaderID;
	public readonly Dictionary<long, GuildController> Officers = new Dictionary<long, GuildController>();
	public readonly Dictionary<long, GuildController> Members = new Dictionary<long, GuildController>();

	public bool IsFull { get { return !(Members.Count < MAX_GUILD_SIZE); } }

	public static bool GuildNameValid(string name)
	{
		return !string.IsNullOrEmpty(name) && name.Length < MAX_GUILDNAME_LENGTH;
	}

	public Guild(string guildID, GuildController leader)
	{
		ID = guildID;
		LeaderID = leader.Character.ID;
		Members.Add(LeaderID, leader);
	}

	public GuildController RemoveMember(long memberID)
	{
		if (Members.TryGetValue(memberID, out GuildController controller))
		{
			Members.Remove(memberID);
			return controller;
		}
		return null;
	}
}