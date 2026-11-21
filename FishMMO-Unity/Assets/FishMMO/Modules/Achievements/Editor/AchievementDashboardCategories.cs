#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>The Achievements module's entry in the dashboard's Character group.</summary>
	internal sealed class AchievementDashboardCategories : IDashboardCategoryProvider
	{
		/// <inheritdoc />
		public int Order => 100;

		/// <inheritdoc />
		public void AddCategories(DashboardCategoryList list)
		{
			list.Add(new TemplateCategory
			{
				DisplayName = "Achievements",
				Group = "Character",
				AssetType = typeof(AchievementTemplate),
				DefaultAssetDirectory = "Assets/FishMMO/Modules/Achievements/Templates",
				CreateAssetMenuName = "FishMMO/Character/Achievement/Achievement",
				GetGroupLabel = asset =>
				{
					AchievementTemplate ach = asset as AchievementTemplate;
					return ach != null ? ach.Category.ToString() : "Unknown";
				},
				GetSortOrder = asset =>
				{
					AchievementTemplate ach = asset as AchievementTemplate;
					return ach != null ? (int)ach.Category : 0;
				},
			});
		}
	}
}
#endif
