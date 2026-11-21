#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>The Commerce module's entry in the dashboard's Interactables group: merchants.</summary>
	internal sealed class CommerceDashboardCategories : IDashboardCategoryProvider
	{
		/// <inheritdoc />
		public int Order => 600;

		/// <inheritdoc />
		public void AddCategories(DashboardCategoryList list)
		{
			list.Add<MerchantTemplate>("Merchants", "Interactables",
				"Assets/FishMMO/Modules/Commerce/Templates/NPCs",
				"FishMMO/Character/Merchant");
		}
	}
}
#endif
