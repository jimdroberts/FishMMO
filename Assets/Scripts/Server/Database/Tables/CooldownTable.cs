using SQLite;

namespace Server
{
	public partial class Database
	{
		class character_itemcooldowns
		{
			[PrimaryKey] // important for performance: O(log n) instead of O(n)
			public string character { get; set; }
			public string category { get; set; }
			public float cooldownEnd { get; set; }
		}

		/*void LoadItemCooldowns(Character character)
        {
            // then load cooldowns
            // (one big query is A LOT faster than querying each slot separately)
            foreach (character_itemcooldowns row in connection.Query<character_itemcooldowns>("SELECT * FROM character_itemcooldowns WHERE character=?", character.name))
            {
                // cooldownEnd is based on NetworkTime.time which will be different
                // when restarting a server, hence why we saved it as just the
                // remaining time. so let's convert it back again.
                character.itemCooldowns.Add(row.category, row.cooldownEnd + NetworkTime.time);
            }
        }

        void SaveItemCooldowns(Character character)
        {
            // equipment: remove old entries first, then add all new ones
            // (we could use UPDATE where slot=... but deleting everything makes
            //  sure that there are never any ghosts)
            connection.Execute("DELETE FROM character_itemcooldowns WHERE character=?", character.name);
            foreach (KeyValuePair<string, double> kvp in character.itemCooldowns)
            {
                // cooldownEnd is based on NetworkTime.time, which will be different
                // when restarting the server, so let's convert it to the remaining
                // time for easier save & load
                // note: this does NOT work when trying to save character data
                //       shortly before closing the editor or game because
                //       NetworkTime.time is 0 then.
                float cooldown = character.GetItemCooldown(kvp.Key);
                if (cooldown > 0)
                {
                    connection.InsertOrReplace(new character_itemcooldowns
                    {
                        character = character.name,
                        category = kvp.Key,
                        cooldownEnd = cooldown
                    });
                }
            }
        }*/
	}
}