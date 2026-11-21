#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>The Abilities module's entries in the dashboard's Character group: ability templates and ability events.</summary>
	internal sealed class AbilityDashboardCategories : IDashboardCategoryProvider
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
				DefaultAssetDirectory = "Assets/FishMMO/Modules/Abilities/Templates/Types",
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
				DefaultAssetDirectory = "Assets/FishMMO/Modules/Abilities/Templates/Events",
				ConcreteTypes = new List<(string, Type)>
				{
					("On Destroy Event", typeof(AbilityOnDestroyEvent)),
					("On Hit Event", typeof(AbilityOnHitEvent)),
					("On Pre Spawn Event", typeof(AbilityOnPreSpawnEvent)),
					("On Spawn Event", typeof(AbilityOnSpawnEvent)),
					("On Summon Event", typeof(AbilityOnSummonEvent)),
					("On Tick Event", typeof(AbilityOnTickEvent)),
				},
			});
		}
	}
}
#endif
