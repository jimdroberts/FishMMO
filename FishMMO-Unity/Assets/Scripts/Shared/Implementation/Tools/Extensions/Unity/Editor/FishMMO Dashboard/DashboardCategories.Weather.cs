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
	/// <summary>The dashboard sidebar's Weather group.</summary>
	internal sealed class WeatherDashboardCategories : IDashboardCategoryProvider
	{
		/// <inheritdoc />
		public int Order => 900;

		/// <inheritdoc />
		public void AddCategories(DashboardCategoryList list)
		{
			list.Add<FishMMO.Shared.Weather.WeatherSubstance>("Weather Substances", "Weather",
				FishMMO.Shared.WorldDesign.WeatherContentGenerator.SubstancesFolder,
				"FishMMO/Weather/Substance");
			list.AddToolPage(DashboardToolAttribute.Weather, "Weather");
		}
	}
}
#endif
