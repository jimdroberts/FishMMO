#if UNITY_EDITOR
using UnityEngine.UIElements;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.Shared
{
	/// <summary>
	/// World → Solar System and World → World Atlas: full-width designer pages.
	/// </summary>
	public partial class FishMMODashboard
	{
		private const string SOLAR_SYSTEM_CATEGORY = "Solar System";
		private const string WORLD_ATLAS_CATEGORY = "World Atlas";

		/// <summary>Adds the two world designers to the World group.</summary>
		private void RegisterWorldDesignCategories()
		{
			categories.Add(new TemplateCategory
			{
				DisplayName = SOLAR_SYSTEM_CATEGORY,
				Group = "World",
				IsSpecial = true,
			});
			categories.Add(new TemplateCategory
			{
				DisplayName = WORLD_ATLAS_CATEGORY,
				Group = "World",
				IsSpecial = true,
			});
		}

		/// <summary>Opens the dashboard on the World Atlas page, with a scene selected when given.</summary>
		public static void OpenWorldAtlas(string sceneName = null)
		{
			WorldAtlasPage.PendingSelection = sceneName;
			ShowWindow();
			FishMMODashboard window = GetWindow<FishMMODashboard>();
			int index = window.categories.FindIndex(c => c.DisplayName == WORLD_ATLAS_CATEGORY);
			if (index >= 0)
			{
				window.OnCategorySelected(index);
			}
		}

		private static bool IsWorldDesignPage(string displayName)
		{
			return displayName == SOLAR_SYSTEM_CATEGORY || displayName == WORLD_ATLAS_CATEGORY;
		}

		/// <summary>Shows a world designer across the inspector and entity columns.</summary>
		private void ShowWorldDesignPage(string displayName)
		{
			ClearInspector();

			VisualElement inspectorPanel = rootVisualElement.Q<VisualElement>("inspector-panel");
			VisualElement inspectorScroll = rootVisualElement.Q<VisualElement>("inspector-scroll");
			if (inspectorPanel == null || inspectorScroll == null)
			{
				return;
			}

			if (inspectorHeader != null)
			{
				inspectorHeader.text = displayName;
			}

			SetEntityPanelVisible(false);
			inspectorScroll.style.display = DisplayStyle.None;

			fullPageHost = new VisualElement();
			fullPageHost.style.flexGrow = 1;
			inspectorPanel.Add(fullPageHost);

			if (displayName == SOLAR_SYSTEM_CATEGORY)
			{
				fullPageHost.Add(new SolarSystemPage());
			}
			else
			{
				fullPageHost.Add(new WorldAtlasPage());
			}
		}
	}
}
#endif
