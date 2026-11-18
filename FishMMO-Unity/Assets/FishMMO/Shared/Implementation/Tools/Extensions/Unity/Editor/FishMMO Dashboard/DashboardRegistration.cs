#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Adds a group of entries to the FishMMO Dashboard's sidebar. Implemented once per group (Character, Items,
	/// Weather…) and found through the TypeCache, so the dashboard itself names no feature: a module that brings
	/// templates brings the provider that lists them.
	/// </summary>
	/// <remarks>
	/// A provider needs a public or internal parameterless constructor. Providers run in <see cref="Order"/>
	/// (then type name), and the sidebar shows groups in the order their first entry was added.
	/// </remarks>
	public interface IDashboardCategoryProvider
	{
		/// <summary>Where this provider's entries go in the sidebar; lower comes first.</summary>
		int Order { get; }

		/// <summary>Adds this provider's entries.</summary>
		void AddCategories(DashboardCategoryList list);
	}

	/// <summary>What an <see cref="IDashboardCategoryProvider"/> adds its sidebar entries to.</summary>
	public sealed class DashboardCategoryList
	{
		/// <summary>The dashboard being built, for entries that open its own pages and forms.</summary>
		public FishMMODashboard Dashboard { get; }

		internal DashboardCategoryList(FishMMODashboard dashboard)
		{
			Dashboard = dashboard;
		}

		/// <summary>Adds an entry.</summary>
		public void Add(TemplateCategory category) => Dashboard.AddCategoryEntry(category);

		/// <summary>Adds an entry listing every asset of type <typeparamref name="T"/>, created in <paramref name="defaultDirectory"/>.</summary>
		public void Add<T>(string displayName, string group, string defaultDirectory, string createAssetMenuName) where T : ScriptableObject
		{
			Add(new TemplateCategory
			{
				DisplayName = displayName,
				Group = group,
				AssetType = typeof(T),
				DefaultAssetDirectory = defaultDirectory,
				CreateAssetMenuName = createAssetMenuName,
			});
		}

		/// <summary>Adds the sidebar entry for a tool page (declared with <see cref="DashboardPageAttribute"/>).</summary>
		public void AddToolPage(string page, string group)
		{
			Add(new TemplateCategory
			{
				DisplayName = page,
				Group = group,
				IsSpecial = true,
			});
		}
	}

	/// <summary>Finds every <see cref="IDashboardCategoryProvider"/>.</summary>
	public static class DashboardCategoryProviders
	{
		/// <summary>Every provider, in the order they add their entries. Built afresh on each call; the dashboard asks once per rebuild.</summary>
		public static List<IDashboardCategoryProvider> All
		{
			get
			{
				var providers = new List<IDashboardCategoryProvider>();
				foreach (Type type in TypeCache.GetTypesDerivedFrom<IDashboardCategoryProvider>())
				{
					if (type.IsAbstract || type.IsInterface)
					{
						continue;
					}
					ConstructorInfo constructor = type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
					if (constructor == null)
					{
						Debug.LogWarning($"[FishMMODashboard] {type.FullName} has no parameterless constructor; its sidebar entries are skipped.");
						continue;
					}
					providers.Add((IDashboardCategoryProvider)constructor.Invoke(null));
				}
				providers.Sort((a, b) =>
				{
					int cmp = a.Order.CompareTo(b.Order);
					return cmp != 0 ? cmp : string.CompareOrdinal(a.GetType().FullName, b.GetType().FullName);
				});
				return providers;
			}
		}
	}

	/// <summary>
	/// Declares a FishMMO Dashboard tool page: a page that <see cref="DashboardToolAttribute"/> methods can name,
	/// with the description shown above its buttons. Assembly-level, so each module's editor assembly declares the
	/// pages its tools use and the dashboard keeps no list of its own.
	/// </summary>
	[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
	public sealed class DashboardPageAttribute : Attribute
	{
		/// <summary>The page's name, as tools name it.</summary>
		public string Name { get; }

		/// <summary>What the page is for, shown above its buttons.</summary>
		public string Description { get; set; }

		/// <summary>Declares a page.</summary>
		public DashboardPageAttribute(string name)
		{
			Name = name;
		}
	}

	/// <summary>Every tool page declared with <see cref="DashboardPageAttribute"/> in the loaded assemblies.</summary>
	public static class DashboardPages
	{
		private static Dictionary<string, string> descriptions;

		/// <summary>Every declared page's name.</summary>
		public static IReadOnlyCollection<string> Names
		{
			get
			{
				Ensure();
				return descriptions.Keys;
			}
		}

		/// <summary>Whether <paramref name="page"/> is a declared page.</summary>
		public static bool Exists(string page)
		{
			Ensure();
			return page != null && descriptions.ContainsKey(page);
		}

		/// <summary>The page's description, or false when it has none.</summary>
		public static bool TryGetDescription(string page, out string description)
		{
			Ensure();
			description = null;
			return page != null && descriptions.TryGetValue(page, out description) && !string.IsNullOrEmpty(description);
		}

		private static void Ensure()
		{
			if (descriptions != null)
			{
				return;
			}
			descriptions = new Dictionary<string, string>(StringComparer.Ordinal);
			foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
			{
				DashboardPageAttribute[] pages;
				try
				{
					pages = (DashboardPageAttribute[])assembly.GetCustomAttributes(typeof(DashboardPageAttribute), false);
				}
				catch (Exception)
				{
					continue;   // an assembly that cannot be inspected declares nothing
				}
				foreach (DashboardPageAttribute page in pages)
				{
					if (descriptions.TryGetValue(page.Name, out string existing) && !string.IsNullOrEmpty(existing))
					{
						continue;   // declared twice: the first description stands
					}
					descriptions[page.Name] = page.Description;
				}
			}
		}
	}
}
#endif
