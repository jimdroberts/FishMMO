namespace FishMMO.Shared
{
	/// <summary>
	/// Interface for controllers that manage quest instances and acquisition for a character.
	/// </summary>
	public interface IQuestController : ICharacterBehaviour
	{
		/// <summary>
		/// Attempts to retrieve a quest instance by name.
		/// </summary>
		/// <param name="name">The name of the quest to look up.</param>
		/// <param name="quest">The found quest instance, or null if not found.</param>
		/// <returns>True if the quest is found, false otherwise.</returns>
		bool TryGetQuest(string name, out QuestInstance quest);

		/// <summary>
		/// Acquires (accepts) a new quest for the character.
		/// </summary>
		/// <param name="quest">The quest template to acquire.</param>
		void Acquire(QuestTemplate quest);
	}
}