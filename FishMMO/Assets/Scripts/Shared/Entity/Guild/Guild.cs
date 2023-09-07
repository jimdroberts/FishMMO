using System.Collections.Generic;

public class Guild
{
	public const int MAX_GUILD_SIZE = 100;

	public ulong ID;
	public readonly List<GuildController> Members = new List<GuildController>();

	public bool IsFull { get { return !(Members.Count < MAX_GUILD_SIZE); } }

	public Guild(ulong guildId, GuildController leader)
	{
		ID = guildId;
		Members.Add(leader);
	}

	public GuildController RemoveMember(long memberId)
	{
		foreach (GuildController member in Members)
		{
			if (member.Character.ID == memberId)
			{
				Members.Remove(member);
				return member;
			}
		}
		return null;
	}
}