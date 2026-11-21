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
	/// <summary>The dashboard sidebar's World group.</summary>
	internal sealed class WorldDashboardCategories : IDashboardCategoryProvider
	{
		/// <inheritdoc />
		public int Order => 800;

		/// <inheritdoc />
		public void AddCategories(DashboardCategoryList list)
		{
			list.Add<WorldSceneDetailsCache>("World Scene Details", "World",
				"Assets/Prefabs/Shared",
				"FishMMO/World Scene Details");

			list.AddToolPage(DashboardToolAttribute.WorldMap, "World");
			list.Dashboard.RegisterFullPageCategories("World");
		}
	}
}
#endif
