#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>The Archetypes module's entry in the dashboard's Character group.</summary>
	internal sealed class ArchetypeDashboardCategories : IDashboardCategoryProvider
	{
		/// <inheritdoc />
		public int Order => 100;

		/// <inheritdoc />
		public void AddCategories(DashboardCategoryList list)
		{
			list.Add<ArchetypeTemplate>("Archetypes", "Character",
				"Assets/FishMMO/Templates/Entity/Archetypes",
				"FishMMO/Character/Archetype/Archetype");
		}
	}
}
#endif
