using SQLite;

namespace Server
{
	public partial class Database
	{
		class character_buffs
		{
			public string character { get; set; }
			public string name { get; set; }
			public int level { get; set; }
			public float buffTimeEnd { get; set; }
			// PRIMARY KEY (character, name) is created manually.
		}

		/*void SaveBuffs(CharacterSkills skills)
		{
			// buffs: remove old entries first, then add all new ones
			connection.Execute("DELETE FROM character_buffs WHERE character=?", skills.name);
			foreach (Buff buff in skills.buffs)
			{
				// buffTimeEnd is based on NetworkTime.time, which will be different
				// when restarting the server, so let's convert them to the
				// remaining time for easier save & load
				// note: this does NOT work when trying to save character data
				//       shortly before closing the editor or game because
				//       NetworkTime.time is 0 then.
				connection.InsertOrReplace(new character_buffs
				{
					character = skills.name,
					name = buff.name,
					level = buff.level,
					buffTimeEnd = buff.BuffTimeRemaining()
				});
			}
		}

		void LoadBuffs(CharacterSkills skills)
		{
			// load buffs
			// note: no check if we have learned the skill for that buff
			//       since buffs may come from other people too
			foreach (character_buffs row in connection.Query<character_buffs>("SELECT * FROM character_buffs WHERE character=?", skills.name))
			{
				if (ScriptableSkill.All.TryGetValue(row.name.GetStableHashCode(), out ScriptableSkill skillData))
				{
					// make sure that 1 <= level <= maxlevel (in case we removed a skill
					// level etc)
					int level = Mathf.Clamp(row.level, 1, skillData.maxLevel);
					Buff buff = new Buff((BuffSkill)skillData, level);
					// buffTimeEnd is based on NetworkTime.time, which will be
					// different when restarting a server, hence why we saved
					// them as just the remaining times. so let's convert them
					// back again.
					buff.buffTimeEnd = row.buffTimeEnd + NetworkTime.time;

					// add to synclist after we modify the buff otherwise it wont sync properly
					skills.buffs.Add(buff);
				}
				else Debug.LogWarning("LoadBuffs: skipped buff " + row.name + " for " + skills.name + " because it doesn't exist anymore. If it wasn't removed intentionally then make sure it's in the Resources folder.");
			}
		}*/
	}
}