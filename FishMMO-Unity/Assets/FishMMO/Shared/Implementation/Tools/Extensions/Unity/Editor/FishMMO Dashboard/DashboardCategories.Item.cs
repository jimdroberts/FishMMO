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
	/// <summary>The dashboard sidebar's Items group.</summary>
	internal sealed class ItemDashboardCategories : IDashboardCategoryProvider
	{
		/// <inheritdoc />
		public int Order => 400;

		/// <inheritdoc />
		public void AddCategories(DashboardCategoryList list)
		{
			list.Add(new TemplateCategory
			{
				DisplayName = "Items",
				Group = "Items",
				AssetType = typeof(BaseItemTemplate),
				DefaultAssetDirectory = "Assets/Templates/Entity/Items",
				ConcreteTypes = new List<(string, Type)>
				{
					("Weapon", typeof(WeaponTemplate)),
					("Armor", typeof(ArmorTemplate)),
				},
				GetGroupLabel = asset =>
				{
					EquippableItemTemplate equip = asset as EquippableItemTemplate;
					if (equip != null) return equip.Slot.ToString();
					if (asset is ConsumableTemplate) return "Consumable";
					return "Misc";
				},
				GetSortOrder = asset =>
				{
					EquippableItemTemplate equip = asset as EquippableItemTemplate;
					if (equip != null) return (int)equip.Slot;
					if (asset is ConsumableTemplate) return 100;
					return 200;
				},
			});

			list.Add<ItemAttributeTemplate>("Item Attributes", "Items",
				"Assets/Templates/Entity/Items",
				"FishMMO/Item/Item Attribute/Attribute");

			list.Add(new TemplateCategory
			{
				DisplayName = "Loot Tables",
				Group = "Items",
				AssetType = typeof(LootTableTemplate),
				DefaultAssetDirectory = "Assets/Templates/Entity/Items/LootTables",
				CreateAssetMenuName = "FishMMO/Character/Loot/Loot Table",
				/* Grouped by what the table is worth rather than by name. A designer opening this
				 * category is nearly always balancing — comparing what a tier of creature drops
				 * against its neighbours — and an alphabetical wall of tables makes exactly that
				 * comparison the hardest thing to do. */
				GetGroupLabel = asset =>
				{
					LootTableTemplate table = asset as LootTableTemplate;
					if (table == null) return "Unknown";
					if (table.MaximumCurrency <= 0) return "No Currency";
					if (table.MaximumCurrency < 100) return "Currency: Low";
					if (table.MaximumCurrency < 1000) return "Currency: Medium";
					return "Currency: High";
				},
				GetSortOrder = asset =>
				{
					LootTableTemplate table = asset as LootTableTemplate;
					return table != null ? table.MaximumCurrency : 0;
				},
			});
		}
	}
}
#endif
