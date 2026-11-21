#if UNITY_EDITOR
namespace FishMMO.Shared
{
	/// <summary>The World Interactables module's Dashboard entries, in the Interactables group beside the core's merchants.</summary>
	internal sealed class WorldInteractablesDashboardCategories : IDashboardCategoryProvider
	{
		public int Order => 601;

		public void AddCategories(DashboardCategoryList list)
		{
			list.Add<DialogueTemplate>("Dialogues", "Interactables",
				"Assets/FishMMO/Modules/WorldInteractables/Templates/Dialogues",
				"FishMMO/Dialogue/Dialogue Template");

			list.Add<ContainerTemplate>("Containers", "Interactables",
				"Assets/FishMMO/Modules/WorldInteractables/Templates/Containers",
				"FishMMO/Character/Container");

			list.Add<GatheringNodeTemplate>("Gathering Nodes", "Interactables",
				"Assets/FishMMO/Modules/WorldInteractables/Templates/Gathering",
				"FishMMO/Interactable/Gathering Node");

			list.Add<ShrineTemplate>("Shrines", "Interactables",
				"Assets/FishMMO/Modules/WorldInteractables/Templates/Shrines",
				"FishMMO/Interactable/Shrine");

			list.Add<LoreObjectTemplate>("Lore Objects", "Interactables",
				"Assets/FishMMO/Modules/WorldInteractables/Templates/LoreObjects",
				"FishMMO/Interactable/Lore Object");

			list.Add<CapturePointTemplate>("Capture Points", "Interactables",
				"Assets/FishMMO/Modules/WorldInteractables/Templates/CapturePoints",
				"FishMMO/Interactable/Capture Point");
		}
	}
}
#endif
