using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FishMMO.Shared;
using NUnit.Framework;
using UnityEditor;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Pins the dashboard tool registry that replaced most <c>FishMMO/…</c> menu items.
	/// </summary>
	public class DashboardToolTests
	{
		private static List<MethodInfo> Tools()
		{
			return TypeCache.GetMethodsWithAttribute<DashboardToolAttribute>().ToList();
		}

		[Test]
		public void EveryTool_IsStaticParameterlessAndOnARealPage()
		{
			List<MethodInfo> tools = Tools();
			Assert.IsNotEmpty(tools);
			foreach (MethodInfo method in tools)
			{
				string name = $"{method.DeclaringType?.Name}.{method.Name}";
				DashboardToolAttribute tool = method.GetCustomAttribute<DashboardToolAttribute>();
				Assert.IsTrue(method.IsStatic, $"{name} must be static; the dashboard skips it otherwise");
				Assert.IsEmpty(method.GetParameters(), $"{name} must take no parameters; the dashboard skips it otherwise");
				Assert.That(DashboardToolAttribute.Pages, Does.Contain(tool.Page), $"{name} names page '{tool.Page}', which the dashboard does not have");
				Assert.IsFalse(string.IsNullOrWhiteSpace(tool.Label), $"{name} has no button label");
			}
		}

		[Test]
		public void NoTool_IsAlsoAFishMMOMenuItem()
		{
			/* The point of the move was a shorter FishMMO menu. A tool that kept its menu item as
			 * well is the clutter growing back. */
			foreach (MethodInfo method in Tools())
			{
				MenuItem menu = method.GetCustomAttribute<MenuItem>();
				Assert.IsTrue(menu == null || !menu.menuItem.StartsWith("FishMMO/", StringComparison.Ordinal),
					$"{method.DeclaringType?.Name}.{method.Name} is still on the menu as '{menu?.menuItem}'");
			}
		}

		[Test]
		public void EveryToolPage_HasAtLeastOneTool()
		{
			HashSet<string> used = new HashSet<string>(Tools().Select(m => m.GetCustomAttribute<DashboardToolAttribute>().Page));
			// The world map's tools live in an assembly the attribute cannot reach; the dashboard lists them itself.
			used.Add(DashboardToolAttribute.WorldMap);
#if UNITY_SERVER
			// The UI tools live in client-only editor assemblies, which the Server subtarget does not compile.
			used.Add(DashboardToolAttribute.UITests);
#endif
			foreach (string page in DashboardToolAttribute.Pages)
			{
				Assert.IsTrue(used.Contains(page), $"page '{page}' has no tools");
			}
		}

		[Test]
		public void TheMovedMenuItems_AreGone()
		{
			/* Every item Jim approved moving, by menu path. The remaining FishMMO/ items (QuickStart,
			 * Script Compilation, Restore Missing Generated Files, the dashboards) stay by decision. */
			string[] moved =
			{
				"FishMMO/Name Generator/", "FishMMO/Unit Tests/", "FishMMO/Validate/", "FishMMO/Validate Network Timing",
				"FishMMO/AI/", "FishMMO/Interactables/", "FishMMO/Spawners/", "FishMMO/World Map/",
				"FishMMO/Rebuild World Scene Details", "FishMMO/Behavior Tree Editor", "FishMMO/Dialogue Tree Editor",
				"FishMMO/Prediction/", "FishMMO/Housing/", "FishMMO/UI Toolkit/", "FishMMO/Templates/", "FishMMO/Mock Content/", "FishMMO/Test Scenes/",
			};
			foreach (MethodInfo method in TypeCache.GetMethodsWithAttribute<MenuItem>())
			{
				foreach (MenuItem menu in method.GetCustomAttributes<MenuItem>())
				{
					foreach (string path in moved)
					{
						Assert.IsFalse(menu.menuItem.StartsWith(path, StringComparison.Ordinal),
							$"{method.DeclaringType?.Name}.{method.Name} still adds '{menu.menuItem}'");
					}
				}
			}
		}
	}
}
