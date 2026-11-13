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

			list.Dashboard.RegisterSpawnTablesCategory();
			list.AddToolPage(DashboardToolAttribute.WorldMap, "World");
			list.Dashboard.RegisterWorldDesignCategories();
		}
	}
}
#endif
