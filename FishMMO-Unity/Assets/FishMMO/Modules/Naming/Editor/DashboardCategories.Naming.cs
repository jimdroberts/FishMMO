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
		/// <summary>The name generator's page.</summary>
		public const string NameGenerationPage = "Name Generation";

		/// <summary>The name generator hosted in the dashboard's page; its window object goes when the page leaves the panel.</summary>
		private static VisualElement CreateNameGenerationPage()
		{
			var host = new VisualElement();
			host.style.flexGrow = 1;
			var window = ScriptableObject.CreateInstance<FishMMO.Shared.NameGeneration.Editor.NameGeneratorWindow>();
			window.hideFlags = HideFlags.HideAndDontSave;
			window.BuildUI(host);
			host.RegisterCallback<DetachFromPanelEvent>(_ =>
			{
				if (window != null)
				{
					UnityEngine.Object.DestroyImmediate(window);
				}
			});
			return host;
		}

		/// <inheritdoc />
		public int Order => 300;

		/// <inheritdoc />
		public void AddCategories(DashboardCategoryList list)
		{
			list.Add<FishMMO.Shared.NameGeneration.NameModifierTemplate>("Name Modifiers", "Naming",
				"Assets/FishMMO/Modules/Naming/Templates/Modifiers",
				"FishMMO/Naming/Name Modifier");

			list.Add<FishMMO.Shared.NameGeneration.TitlePoolTemplate>("Title Pools", "Naming",
				"Assets/FishMMO/Modules/Naming/Templates/Title Pools",
				"FishMMO/Naming/Title Pool");

			list.Add<FishMMO.Shared.NameGeneration.NameGrammarTemplate>("Name Grammar", "Naming",
				"Assets/FishMMO/Modules/Naming/Templates",
				"FishMMO/Naming/Name Grammar");

			// The name generator, full width (the dashboard names no page; this module registers its own).
			DashboardFullPages.Register(NameGenerationPage, "Naming", 0, CreateNameGenerationPage);
			list.Dashboard.RegisterFullPageCategories("Naming");
		}
	}
}
#endif
