using SQLite;

namespace Server
{
	public partial class Database
	{
		class character_quests
		{
			public string character { get; set; }
			public string name { get; set; }
			public int progress { get; set; }
			public bool completed { get; set; }
			// PRIMARY KEY (character, name) is created manually.
		}

		/*void SaveQuests(CharacterQuests quests)
		{
			// quests: remove old entries first, then add all new ones
			connection.Execute("DELETE FROM character_quests WHERE character=?", quests.name);
			foreach (Quest quest in quests.quests)
			{
				connection.InsertOrReplace(new character_quests
				{
					character = quests.name,
					name = quest.name,
					progress = quest.progress,
					completed = quest.completed
				});
			}
		}

		void LoadQuests(CharacterQuests quests)
		{
			// load quests
			foreach (character_quests row in connection.Query<character_quests>("SELECT * FROM character_quests WHERE character=?", quests.name))
			{
				ScriptableQuest questData;
				if (ScriptableQuest.All.TryGetValue(row.name.GetStableHashCode(), out questData))
				{
					Quest quest = new Quest(questData);
					quest.progress = row.progress;
					quest.completed = row.completed;
					quests.quests.Add(quest);
				}
				else Debug.LogWarning("LoadQuests: skipped quest " + row.name + " for " + quests.name + " because it doesn't exist anymore. If it wasn't removed intentionally then make sure it's in the Resources folder.");
			}
		}*/
	}
}