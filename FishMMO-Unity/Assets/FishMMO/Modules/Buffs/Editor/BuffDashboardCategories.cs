#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>The Buffs module's entry in the dashboard's Character group.</summary>
	internal sealed class BuffDashboardCategories : IDashboardCategoryProvider
	{
		/// <inheritdoc />
		public int Order => 100;

		/// <inheritdoc />
		public void AddCategories(DashboardCategoryList list)
		{
			list.Add(new TemplateCategory
			{
				DisplayName = "Buffs",
				Group = "Character",
				AssetType = typeof(BaseBuffTemplate),
				DefaultAssetDirectory = "Assets/FishMMO/Modules/Buffs/Templates",
				ConcreteTypes = new List<(string, Type)>
				{
					("Attribute Buff", typeof(AttributeBuffTemplate)),
					("Attribute Tick Buff", typeof(AttributeTickBuffTemplate)),
					("Composite Buff", typeof(CompositeBuffTemplate)),
					("Resource Tick Buff", typeof(ResourceTickBuffTemplate)),
					("State Buff", typeof(StateBuffTemplate)),
				},
			});
		}
	}
}
#endif
