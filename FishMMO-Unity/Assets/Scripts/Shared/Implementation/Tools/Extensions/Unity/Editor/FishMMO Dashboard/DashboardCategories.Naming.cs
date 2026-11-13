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
	/// <summary>The dashboard sidebar's Naming group (race naming lives on the Race templates, in the Character group).</summary>
	internal sealed class NamingDashboardCategories : IDashboardCategoryProvider
	{
		/// <inheritdoc />
		public int Order => 300;

		/// <inheritdoc />
		public void AddCategories(DashboardCategoryList list)
		{
			list.Add<FishMMO.Shared.NameGeneration.NameModifierTemplate>("Name Modifiers", "Naming",
				"Assets/Templates/Entity/Naming/Modifiers",
				"FishMMO/Naming/Name Modifier");

			list.Add<FishMMO.Shared.NameGeneration.TitlePoolTemplate>("Title Pools", "Naming",
				"Assets/Templates/Entity/Naming/Title Pools",
				"FishMMO/Naming/Title Pool");

			list.Add<FishMMO.Shared.NameGeneration.NameGrammarTemplate>("Name Grammar", "Naming",
				"Assets/Templates/Entity/Naming",
				"FishMMO/Naming/Name Grammar");

			list.Dashboard.AddNameGenerationCategory();
		}
	}
}
#endif
