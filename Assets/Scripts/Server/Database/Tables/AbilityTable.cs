using SQLite;

namespace Server
{
	public partial class Database
	{
		class character_skills
		{
			public string character { get; set; }
			public int hash { get; set; }
			public int level { get; set; }
			public float castTimeEnd { get; set; }
			public float cooldownEnd { get; set; }
			// PRIMARY KEY (character, name) is created manually.
		}

		/*void SaveSkills(CharacterSkills skills)
		{
			// skills: remove old entries first, then add all new ones
			connection.Execute("DELETE FROM character_skills WHERE character=?", skills.name);
			foreach (Skill skill in skills.skills)
				if (skill.level > 0) // only learned skills to save queries/storage/time
				{
					// castTimeEnd and cooldownEnd are based on NetworkTime.time,
					// which will be different when restarting the server, so let's
					// convert them to the remaining time for easier save & load
					// note: this does NOT work when trying to save character data
					//       shortly before closing the editor or game because
					//       NetworkTime.time is 0 then.
					connection.InsertOrReplace(new character_skills
					{
						character = skills.name,//character name
						hash = skill.hash,//skill.name,
						level = skill.level,
						castTimeEnd = skill.CastTimeRemaining(),
						cooldownEnd = skill.CooldownRemaining()
					});
				}
		}
		
		void LoadSkills(CharacterSkills skills)
        {
            // (one big query is A LOT faster than querying each slot separately)
            List<character_skills> query = connection.Query<character_skills>("SELECT * FROM character_skills WHERE character=?", skills.name); //skills.name = character name
            foreach (character_skills row in query)
            {
                ScriptableSkill skillData;
                if (ScriptableSkill.All.TryGetValue(row.hash, out skillData))
                {
                    Skill skill = new Skill(skillData);

                    // make sure that 1 <= level <= maxlevel (in case we removed a skill
                    // level etc)
                    skill.level = Mathf.Clamp(row.level, 1, skill.maxLevel);

                    // castTimeEnd and cooldownEnd are based on NetworkTime.time
                    // which will be different when restarting a server, hence why
                    // we saved them as just the remaining times. so let's convert
                    // them back again.
                    skill.castTimeEnd = row.castTimeEnd + NetworkTime.time;
                    skill.cooldownEnd = row.cooldownEnd + NetworkTime.time;

                    // add to synclist after we modify the skill otherwise it wont sync properly
                    skills.skills.Add(skill);
                }
                else Debug.LogWarning("LoadSkills: skipped skill " + skillData.name + " because it doesn't exist anymore. If it wasn't removed intentionally then make sure it's in the Resources folder.");
            }
        }*/
	}
}