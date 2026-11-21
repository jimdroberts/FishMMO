#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>The dashboard sidebar's Character Templates group.</summary>
	internal sealed class CharacterDashboardCategories : IDashboardCategoryProvider
	{
		/// <inheritdoc />
		public int Order => 100;

		/// <inheritdoc />
		public void AddCategories(DashboardCategoryList list)
		{
			list.Add<CharacterAttributeTemplate>("Character Attributes", "Character",
				"Assets/FishMMO/Templates/Entity/CharacterAttributes",
				"FishMMO/Character/Attribute/Character Attribute");


			list.Add(new TemplateCategory
			{
				DisplayName = "Races",
				Group = "Character",
				AssetType = typeof(RaceTemplate),
				DefaultAssetDirectory = "Assets/FishMMO/Templates/Entity/Races",
				CreateAssetMenuName = "FishMMO/Character/Race/Race",
				// Playable races first, then every other race under its category.
				GetGroupLabel = asset => asset is RaceTemplate race
					? (race.Playable ? "Playable" : string.IsNullOrWhiteSpace(race.Category) ? "Uncategorised" : race.Category.Trim())
					: "",
				GetSortOrder = asset => asset is RaceTemplate race && race.Playable ? 0 : 1,
			});
		}
	}
}
#endif
