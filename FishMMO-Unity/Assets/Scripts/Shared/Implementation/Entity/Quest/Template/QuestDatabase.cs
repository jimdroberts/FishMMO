using System;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// ScriptableObject database for storing and retrieving quest templates by name.
	/// </summary>
	[CreateAssetMenu(fileName = "New Quest Database", menuName = "FishMMO/Character/Quest/Database", order = 0)]
	public class QuestDatabase : ScriptableObject
	{
		/// <summary>
		/// Serializable dictionary mapping quest names to quest templates.
		/// </summary>
		[Serializable]
		public class QuestDictionary : SerializableDictionary<string, QuestTemplate> { }

		/// <summary>
		/// The internal dictionary of quests.
		/// </summary>
		[SerializeField]
		private QuestDictionary quests = new QuestDictionary();

		/// <summary>
		/// Public accessor for the quest dictionary.
		/// </summary>
		public QuestDictionary Quests { get { return this.quests; } }

		/// <summary>
		/// Retrieves a quest template by name from the database.
		/// Returns null if the quest is not found.
		/// </summary>
		/// <param name="name">The name of the quest to retrieve.</param>
		/// <returns>The quest template if found, otherwise null.</returns>
		public QuestTemplate GetQuest(string name)
		{
			this.quests.TryGetValue(name, out QuestTemplate quest);
			return quest;
		}
	}
}