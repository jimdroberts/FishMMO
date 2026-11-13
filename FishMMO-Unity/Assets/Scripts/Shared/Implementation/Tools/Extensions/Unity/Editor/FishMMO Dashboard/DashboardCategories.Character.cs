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
	/// <summary>The dashboard sidebar's Character Templates group.</summary>
	internal sealed class CharacterDashboardCategories : IDashboardCategoryProvider
	{
		/// <inheritdoc />
		public int Order => 100;

		/// <inheritdoc />
		public void AddCategories(DashboardCategoryList list)
		{
			list.Add(new TemplateCategory
			{
				DisplayName = "Abilities",
				Group = "Character",
				AssetType = typeof(BaseAbilityTemplate),
				DefaultAssetDirectory = "Assets/Templates/Entity/Abilities/Types",
				ConcreteTypes = new List<(string, Type)>
				{
					("Ability", typeof(AbilityTemplate)),
					("Pet Ability", typeof(PetAbilityTemplate)),
					("Ability Type Override Event", typeof(AbilityTypeOverrideEventType)),
				},
			});

			list.Add(new TemplateCategory
			{
				DisplayName = "Ability Events",
				Group = "Character",
				AssetType = typeof(AbilityEvent),
				DefaultAssetDirectory = "Assets/Templates/Entity/Abilities/Events",
				ConcreteTypes = new List<(string, Type)>
				{
					("On Destroy Event", typeof(AbilityOnDestroyEvent)),
					("On Hit Event", typeof(AbilityOnHitEvent)),
					("On Pre Spawn Event", typeof(AbilityOnPreSpawnEvent)),
					("On Spawn Event", typeof(AbilityOnSpawnEvent)),
					("On Tick Event", typeof(AbilityOnTickEvent)),
				},
			});

			list.Add(new TemplateCategory
			{
				DisplayName = "Achievements",
				Group = "Character",
				AssetType = typeof(AchievementTemplate),
				DefaultAssetDirectory = "Assets/Templates/Entity/Achievements",
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

			list.Add<ArchetypeTemplate>("Archetypes", "Character",
				"Assets/Templates/Entity/Archetypes",
				"FishMMO/Character/Archetype/Archetype");

			list.Add(new TemplateCategory
			{
				DisplayName = "Buffs",
				Group = "Character",
				AssetType = typeof(BaseBuffTemplate),
				DefaultAssetDirectory = "Assets/Templates/Entity/Buffs",
				ConcreteTypes = new List<(string, Type)>
				{
					("Attribute Buff", typeof(AttributeBuffTemplate)),
					("Attribute Tick Buff", typeof(AttributeTickBuffTemplate)),
					("Composite Buff", typeof(CompositeBuffTemplate)),
					("Resource Tick Buff", typeof(ResourceTickBuffTemplate)),
					("State Buff", typeof(StateBuffTemplate)),
				},
			});

			list.Add<CharacterAttributeTemplate>("Character Attributes", "Character",
				"Assets/Templates/Entity/CharacterAttributes",
				"FishMMO/Character/Attribute/Character Attribute");

			list.Add<FactionTemplate>("Factions", "Character",
				"Assets/Templates/Entity/Factions",
				"FishMMO/Character/Faction/Faction");

			list.Add<FactionMatrixTemplate>("Faction Matrix", "Character",
				"Assets/Templates/Entity/Factions",
				"FishMMO/Character/Faction/Faction Matrix");

			list.Add<QuestTemplate>("Quests", "Character",
				"Assets/Templates/Entity/Quests",
				"FishMMO/Character/Quest/Quest");

			list.Add(new TemplateCategory
			{
				DisplayName = "Races",
				Group = "Character",
				AssetType = typeof(RaceTemplate),
				DefaultAssetDirectory = "Assets/Templates/Entity/Races",
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
