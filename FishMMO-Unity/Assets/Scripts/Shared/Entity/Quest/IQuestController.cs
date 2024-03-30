namespace FishMMO.Shared
{
	public interface IQuestController : ICharacterBehaviour
	{
		bool TryGetQuest(string name, out QuestInstance quest);
		void Acquire(QuestTemplate quest);
	}
}