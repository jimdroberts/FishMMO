#if UNITY_EDITOR
using UnityEngine.UIElements;

namespace FishMMO.Shared
{
	/// <summary>
	/// Full-width designer pages (<see cref="DashboardFullPages"/>): the World Design module's Solar System and World Atlas
	/// under World. The dashboard names no page; modules register theirs.
	/// </summary>
	public partial class FishMMODashboard
	{
		/// <summary>Adds each registered full page of <paramref name="group"/> as a category, where its group's provider adds its categories.</summary>
		public void RegisterFullPageCategories(string group)
		{
			foreach (DashboardFullPages.Page page in DashboardFullPages.All)
			{
				if (page.Group != group)
				{
					continue;
				}
				categories.Add(new TemplateCategory
				{
					DisplayName = page.Name,
					Group = page.Group,
					IsSpecial = true,
				});
			}
		}

		/// <summary>Opens the dashboard on a full page by name (a module sets up what the page should show first).</summary>
		public static void OpenFullPage(string name)
		{
			ShowWindow();
			FishMMODashboard window = GetWindow<FishMMODashboard>();
			int index = window.categories.FindIndex(c => c.DisplayName == name);
			if (index >= 0)
			{
				window.OnCategorySelected(index);
			}
		}

		private static bool IsFullPage(string displayName) => DashboardFullPages.TryGet(displayName, out _);

		/// <summary>Shows a full page across the inspector and entity columns.</summary>
		private void ShowFullPage(string displayName)
		{
			ClearInspector();

			VisualElement inspectorPanel = rootVisualElement.Q<VisualElement>("inspector-panel");
			VisualElement inspectorScroll = rootVisualElement.Q<VisualElement>("inspector-scroll");
			if (inspectorPanel == null || inspectorScroll == null || !DashboardFullPages.TryGet(displayName, out DashboardFullPages.Page page))
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
			fullPageHost.Add(page.Create());
		}
	}
}
#endif
