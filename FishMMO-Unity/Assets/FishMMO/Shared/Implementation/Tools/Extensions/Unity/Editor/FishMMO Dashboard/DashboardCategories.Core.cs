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
	/// <summary>The dashboard sidebar's Core group.</summary>
	internal sealed class CoreDashboardCategories : IDashboardCategoryProvider
	{
		/// <inheritdoc />
		public int Order => 0;

		/// <inheritdoc />
		public void AddCategories(DashboardCategoryList list)
		{
			list.Add(new TemplateCategory
			{
				DisplayName = "Build",
				Group = "Core",
				IsSpecial = true,
			});

			list.Add(new TemplateCategory
			{
				DisplayName = "Game Settings",
				Group = "Core",
				IsSpecial = true,
			});

			list.Add(new TemplateCategory
			{
				DisplayName = "Version",
				Group = "Core",
				IsSpecial = true,
			});

			list.Add(new TemplateCategory
			{
				DisplayName = "Patch Generator",
				Group = "Core",
				IsSpecial = true,
			});

			list.AddToolPage(DashboardToolAttribute.Validate, "Core");
			list.AddToolPage(DashboardToolAttribute.UnitTests, "Core");
			list.AddToolPage(DashboardToolAttribute.UITests, "Core");
			list.AddToolPage(DashboardToolAttribute.Maintenance, "Core");
		}
	}
}
#endif
