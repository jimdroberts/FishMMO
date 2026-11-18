#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared.Core;
using FishMMO.Server.Implementation.World.SceneServer.AI;

namespace FishMMO.Shared
{
	/// <summary>The dashboard sidebar's Biomes group (biome naming lives on the Biome templates).</summary>
	internal sealed class BiomeDashboardCategories : IDashboardCategoryProvider
	{
		/// <inheritdoc />
		public int Order => 200;

		/// <inheritdoc />
		public void AddCategories(DashboardCategoryList list)
		{
			list.Add(new TemplateCategory
			{
				DisplayName = "Biomes",
				Group = "Biomes",
				AssetType = typeof(FishMMO.Shared.Biomes.BiomeTemplate),
				DefaultAssetDirectory = "Assets/Templates/Entity/Biomes",
				CreateAssetMenuName = "FishMMO/Biomes/Biome",
				// Grouped by elevation tier, sea floor first; placement-only biomes (weight 0) last.
				GetGroupLabel = asset => asset is FishMMO.Shared.Biomes.BiomeTemplate biome
					? (biome.IsSelectable ? FishMMO.Shared.Biomes.BiomeTemplate.TierName(biome.ElevationTier) : "Placed Only")
					: "",
				GetSortOrder = asset => asset is FishMMO.Shared.Biomes.BiomeTemplate biome
					? (biome.IsSelectable ? biome.ElevationTier : 100)
					: 0,
			});

			list.Add<FishMMO.Shared.Biomes.ClimateSettings>("Climate Settings", "Biomes",
				"Assets/Templates/Entity/Biomes",
				"FishMMO/Biomes/Climate Settings");

			list.Add<FishMMO.Shared.Biomes.SceneBiomeMap>("Scene Biome Maps", "Biomes",
				"Assets/Templates/Entity/Biomes/Maps",
				"FishMMO/Biomes/Scene Biome Map");
			list.AddToolPage(DashboardToolAttribute.Biomes, "Biomes");
		}
	}
}
#endif
