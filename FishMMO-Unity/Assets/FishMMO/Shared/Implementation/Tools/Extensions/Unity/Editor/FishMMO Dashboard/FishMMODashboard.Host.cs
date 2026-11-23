#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine.UIElements;

namespace FishMMO.Shared
{
	/// <summary>
	/// What a module's category may do to the dashboard (when the NPC prefab designer became the NPCs
	/// module's): draw into the inspector area, keep an editor alive with it, report a status, reload its list and select
	/// an entry, and rescan when the project changes.
	/// </summary>
	public partial class FishMMODashboard
	{
		/// <summary>The category on screen, or null.</summary>
		private TemplateCategory CurrentCategory()
		{
			return selectedCategoryIndex >= 0 && selectedCategoryIndex < categories.Count ? categories[selectedCategoryIndex] : null;
		}

		/// <summary>Asks every category that caches its list to forget it.</summary>
		private void InvalidateCategoryCaches()
		{
			foreach (TemplateCategory category in categories)
			{
				category.Invalidate?.Invoke();
			}
		}

		/// <summary>
		/// Unity's notice that assets were created, deleted, moved or imported. A category with a cached list (NPC prefabs
		/// are made in many places: the Project window, a duplicate, a generator, a git pull) shows them without anyone
		/// pressing anything. Unity refreshes the asset database when the editor regains focus, so external changes arrive
		/// here too.
		/// </summary>
		private void OnProjectChange()
		{
			// CreateGUI has not run yet, or the window is being torn down.
			if (categories.Count == 0 || entityList == null)
			{
				return;
			}

			InvalidateCategoryCaches();

			TemplateCategory current = CurrentCategory();
			if (current == null || current.Invalidate == null)
			{
				BuildCategoryList();
				return;
			}

			// Reload the cached list in place, keeping the selection where the selected entry still exists.
			UnityEngine.Object selected = SelectedEntry;
			selectedAssetIndex = -1;
			LoadAssetsForCategory(current);
			RefreshEntityList();
			BuildCategoryList();

			if (selected == null)
			{
				return;
			}
			int index = filteredAssets.IndexOf(selected);
			if (index >= 0)
			{
				selectedAssetIndex = index;
				BuildEntityList();
			}
			else
			{
				ClearInspector();
			}
		}

		/// <summary>The inspector area a category's <see cref="TemplateCategory.ShowInspector"/> draws into.</summary>
		public VisualElement InspectorArea => inspectorContent;

		/// <summary>Clears the inspector area (destroying the editors kept with it) and sets its header.</summary>
		public void BeginInspector(string header)
		{
			ClearInspector();
			if (inspectorHeader != null)
			{
				inspectorHeader.text = header;
			}
		}

		/// <summary>Keeps an editor alive until the inspector is next cleared, which destroys it.</summary>
		public void KeepEditor(Editor editor)
		{
			if (editor != null)
			{
				activeEditors.Add(editor);
			}
		}

		/// <summary>Shows a line in the status bar.</summary>
		public void ShowStatus(string message) => SetStatus(message);

		/// <summary>A titled section in the dashboard's constants style.</summary>
		public VisualElement NewSection(string title) => CreateConstantsSection(title);

		/// <summary>Adds a read-only key/value row to a section.</summary>
		public void AddRow(VisualElement section, string key, string value) => AddConstantRow(section, key, value);

		/// <summary>The entry selected in the list, or null.</summary>
		public UnityEngine.Object SelectedEntry =>
			selectedAssetIndex >= 0 && selectedAssetIndex < filteredAssets.Count ? filteredAssets[selectedAssetIndex] : null;

		/// <summary>Adds the dashboard tool buttons declared for <paramref name="page"/> to the inspector area; false when it has none.</summary>
		public bool AddToolButtons(string page) => AddToolSections(page);

		/// <summary>True when the category named <paramref name="categoryName"/> is on screen.</summary>
		public bool IsShowing(string categoryName) => CurrentCategory()?.DisplayName == categoryName;

		/// <summary>
		/// Rebuilds the sidebar counts and, when the category named <paramref name="categoryName"/> is on screen, reloads its
		/// list and shows its landing page (after a bake that replaced its entries, say).
		/// </summary>
		public void ReloadToLanding(string categoryName)
		{
			BuildCategoryList();
			TemplateCategory current = CurrentCategory();
			if (current == null || current.DisplayName != categoryName)
			{
				return;
			}
			selectedAssetIndex = -1;
			LoadAssetsForCategory(current);
			RefreshEntityList();
			ShowCategoryLanding(current);
		}

		/// <summary>
		/// <see cref="ReloadToLanding"/>, but selecting <paramref name="entry"/> when it is still listed (a rebake that kept
		/// the entry it was opened from).
		/// </summary>
		public void ReloadToEntry(string categoryName, UnityEngine.Object entry)
		{
			BuildCategoryList();
			TemplateCategory current = CurrentCategory();
			if (current == null || current.DisplayName != categoryName)
			{
				return;
			}
			LoadAssetsForCategory(current);
			RefreshEntityList();
			int index = entry != null ? filteredAssets.IndexOf(entry) : -1;
			if (index >= 0)
			{
				OnAssetSelected(index);
			}
			else
			{
				selectedAssetIndex = -1;
				ShowCategoryLanding(current);
			}
		}

		/// <summary>Reloads the category on screen and selects the entry at <paramref name="assetPath"/>, if it is listed.</summary>
		public void ReloadAndSelect(string assetPath)
		{
			ReloadCurrentCategory();
			for (int i = 0; i < filteredAssets.Count; i++)
			{
				if (AssetDatabase.GetAssetPath(filteredAssets[i]) == assetPath)
				{
					OnAssetSelected(i);
					break;
				}
			}
		}
	}
}
#endif
