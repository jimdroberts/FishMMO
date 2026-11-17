#if UNITY_EDITOR
namespace FishMMO.Shared
{
	/// <summary>The Quests module's Dashboard entry (it was in the Character categories; the module brings its own).</summary>
	internal sealed class QuestDashboardCategories : IDashboardCategoryProvider
	{
		public int Order => 120;

		public void AddCategories(DashboardCategoryList list)
		{
			list.Add<QuestTemplate>("Quests", "Character",
				"Assets/Modules/Quests/Templates",
				"FishMMO/Character/Quest/Quest");
		}
	}
}
#endif
