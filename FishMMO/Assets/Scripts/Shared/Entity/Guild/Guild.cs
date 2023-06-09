﻿using System.Collections.Generic;

public class Guild
{
	public const int MAX_GUILD_SIZE = 100;

	public ulong id;
	public readonly List<GuildController> members = new List<GuildController>();

	public bool IsFull { get { return !(members.Count < MAX_GUILD_SIZE); } }

	public Guild(ulong guildId, GuildController leader)
	{
		id = guildId;
		members.Add(leader);
	}

	public GuildController RemoveMember(long memberId)
	{
		foreach (GuildController member in members)
		{
			if (member.character.id == memberId)
			{
				members.Remove(member);
				return member;
			}
		}
		return null;
	}
}