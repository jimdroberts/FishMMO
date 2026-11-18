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
	/// <summary>The dashboard sidebar's ECA group.</summary>
	internal sealed class EcaDashboardCategories : IDashboardCategoryProvider
	{
		/// <inheritdoc />
		public int Order => 700;

		/// <inheritdoc />
		public void AddCategories(DashboardCategoryList list)
		{
			list.Add<Trigger>("Triggers", "ECA",
				"Assets/Templates/Entity/ECA",
				"FishMMO/ECA/Trigger");
		}
	}
}
#endif
