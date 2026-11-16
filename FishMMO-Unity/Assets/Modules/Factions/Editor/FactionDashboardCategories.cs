#if UNITY_EDITOR
namespace FishMMO.Shared
{
	/// <summary>The Factions module's Dashboard entries (they were in the Character categories; the module brings its own).</summary>
	internal sealed class FactionDashboardCategories : IDashboardCategoryProvider
	{
		public int Order => 110;

		public void AddCategories(DashboardCategoryList list)
		{
			list.Add<FactionTemplate>("Factions", "Character",
				"Assets/Modules/Factions/Templates",
				"FishMMO/Character/Faction/Faction");

			list.Add<FactionMatrixTemplate>("Faction Matrix", "Character",
				"Assets/Modules/Factions/Templates",
				"FishMMO/Character/Faction/Faction Matrix");
		}
	}
}
#endif
