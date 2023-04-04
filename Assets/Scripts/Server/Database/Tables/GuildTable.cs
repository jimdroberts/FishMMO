using SQLite;

namespace Server
{
	public partial class Database
	{
		class character_guild
		{
			// guild members are saved in a separate table because instead of in a
			// characters.guild field because:
			// * guilds need to be resaved independently, not just in CharacterSave
			// * kicked members' guilds are cleared automatically because we drop
			//   and then insert all members each time. otherwise we'd have to
			//   update the kicked member's guild field manually each time
			// * it's easier to remove / modify the guild feature if it's not hard-
			//   coded into the characters table
			[PrimaryKey] // important for performance: O(log n) instead of O(n)
			public string character { get; set; }
			// add index on guild to avoid full scans when loading guild members
			[Indexed]
			public string guild { get; set; }
			public int rank { get; set; }
		}

		class guild_info
		{
			// guild master is not in guild_info in case we need more than one later
			[PrimaryKey] // important for performance: O(log n) instead of O(n)
			public string name { get; set; }
			public string notice { get; set; }
		}

		/* guilds //////////////////////////////////////////////////////////////////
		public bool GuildExists(string guild)
		{
			return connection.FindWithQuery<guild_info>("SELECT * FROM guild_info WHERE name=?", guild) != null;
		}

		Guild LoadGuild(string guildName)
		{
			Guild guild = new Guild();

			// set name
			guild.name = guildName;

			// load guild info
			guild_info info = connection.FindWithQuery<guild_info>("SELECT * FROM guild_info WHERE name=?", guildName);
			if (info != null)
			{
				guild.notice = info.notice;
			}

			// load members list
			List<character_guild> rows = connection.Query<character_guild>("SELECT * FROM character_guild WHERE guild=?", guildName);
			GuildMember[] members = new GuildMember[rows.Count]; // avoid .ToList(). use array directly.
			for (int i = 0; i < rows.Count; ++i)
			{
				character_guild row = rows[i];

				GuildMember member = new GuildMember();
				member.name = row.character;
				member.rank = (GuildRank)row.rank;

				// is this character online right now? then use runtime data
				if (Character.onlineCharacters.TryGetValue(member.name, out Character character))
				{
					member.online = true;
				}
				else
				{
					member.online = false;
				}

				members[i] = member;
			}
			guild.members = members;
			return guild;
		}

		// only load guild when their first character logs in
        // => using NetworkManager.Awake to load all guilds.Where would work,
        //    but we would require lots of memory and it might take a long time.
        // => hooking into character loading to load guilds is a really smart solution,
        //    because we don't ever have to load guilds that aren't needed
        void LoadGuildOnDemand(CharacterGuild characterGuild)
        {
            string guildName = connection.ExecuteScalar<string>("SELECT guild FROM character_guild WHERE character=?", characterGuild.name);
            if (guildName != null)
            {
                // load guild on demand when the first character of that guild logs in
                // (= if it's not in GuildSystem.guilds yet)
                if (!GuildSystem.guilds.ContainsKey(guildName))
                {
                    Guild guild = LoadGuild(guildName);
                    GuildSystem.guilds[guild.name] = guild;
                    characterGuild.guild = guild;
                }
                // assign from already loaded guild
                else characterGuild.guild = GuildSystem.guilds[guildName];
            }
        }

		public void SaveGuild(Guild guild, bool useTransaction = true)
		{
			if (useTransaction) connection.BeginTransaction(); // transaction for performance

			// guild info
			connection.InsertOrReplace(new guild_info
			{
				name = guild.name,
				notice = guild.notice
			});

			// members list
			connection.Execute("DELETE FROM character_guild WHERE guild=?", guild.name);
			foreach (GuildMember member in guild.members)
			{
				connection.InsertOrReplace(new character_guild
				{
					character = member.name,
					guild = guild.name,
					rank = (int)member.rank
				});
			}

			if (useTransaction) connection.Commit(); // end transaction
		}

		public void RemoveGuild(string guild)
		{
			connection.BeginTransaction(); // transaction for performance
			connection.Execute("DELETE FROM guild_info WHERE name=?", guild);
			connection.Execute("DELETE FROM character_guild WHERE guild=?", guild);
			connection.Commit(); // end transaction
		}*/
	}
}