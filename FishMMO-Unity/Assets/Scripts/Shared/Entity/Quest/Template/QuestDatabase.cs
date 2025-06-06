using System;
using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Quest Database", menuName = "FishMMO/Character/Quest/Database", order = 0)]
	public class QuestDatabase : ScriptableObject
	{
		[Serializable]
		public class QuestDictionary : SerializableDictionary<string, QuestTemplate> { }

		[SerializeField]
		private QuestDictionary quests = new QuestDictionary();
		public QuestDictionary Quests { get { return this.quests; } }

		public QuestTemplate GetQuest(string name)
		{
			this.quests.TryGetValue(name, out QuestTemplate quest);
			return quest;
		}
	}
}