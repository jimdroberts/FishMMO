#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace FishMMO.Shared
{
	/// <summary>
	/// Full-width designer pages modules add to the dashboard (the World Design module's Solar System and World Atlas):
	/// each is a category of its own that fills the inspector and entity columns (Phase 4 of the core and modules plan,
	/// 2026-11-18). The dashboard names none of them.
	/// </summary>
	public static class DashboardFullPages
	{
		/// <summary>One page: its category name, its group, its place in the group and how to make it.</summary>
		public readonly struct Page
		{
			public readonly string Name;
			public readonly string Group;
			public readonly int Order;
			public readonly Func<VisualElement> Create;

			public Page(string name, string group, int order, Func<VisualElement> create)
			{
				Name = name;
				Group = group;
				Order = order;
				Create = create;
			}
		}

		private static readonly List<Page> pages = new List<Page>();

		/// <summary>The registered pages, by order then name.</summary>
		public static IReadOnlyList<Page> All => pages;

		/// <summary>Registers a page; registering a name again replaces it.</summary>
		public static void Register(string name, string group, int order, Func<VisualElement> create)
		{
			if (string.IsNullOrEmpty(name) || create == null)
			{
				return;
			}
			pages.RemoveAll(p => p.Name == name);
			pages.Add(new Page(name, group ?? "World", order, create));
			pages.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : string.CompareOrdinal(a.Name, b.Name));
		}

		/// <summary>The page named <paramref name="name"/>, if one is registered.</summary>
		public static bool TryGet(string name, out Page page)
		{
			foreach (Page p in pages)
			{
				if (p.Name == name)
				{
					page = p;
					return true;
				}
			}
			page = default;
			return false;
		}
	}

	/// <summary>
	/// Long-running editor jobs modules report to the dashboard (the World Design module's biome art generator): while any
	/// is busy every tool button is locked, and the buttons are relocked whenever one starts or ends.
	/// </summary>
	public static class DashboardJobs
	{
		private static readonly Dictionary<string, Func<bool>> jobs = new Dictionary<string, Func<bool>>(StringComparer.Ordinal);

		/// <summary>Raised when a registered job starts or ends.</summary>
		public static event Action BusyChanged;

		/// <summary>Registers a job by id; registering an id again replaces it.</summary>
		public static void Register(string id, Func<bool> isBusy)
		{
			if (!string.IsNullOrEmpty(id) && isBusy != null)
			{
				jobs[id] = isBusy;
			}
		}

		/// <summary>True while any registered job is working.</summary>
		public static bool IsBusy
		{
			get
			{
				foreach (Func<bool> busy in jobs.Values)
				{
					if (busy())
					{
						return true;
					}
				}
				return false;
			}
		}

		/// <summary>A job calls this when it starts or ends.</summary>
		public static void NotifyChanged() => BusyChanged?.Invoke();
	}
}
#endif
