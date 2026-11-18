using System;
using System.Collections.Generic;

namespace FishMMO.Client
{
	/// <summary>
	/// The game menu's optional destinations, added by modules (Leaderboards adds "Leaderboards").
	/// </summary>
	/// <remarks>
	/// The menu's own buttons (Resume, Options, Channels, Dungeon, the quits) are in its UXML; a module's button is
	/// not, so removing the module removes the button with it and leaves nothing in the menu that names it. A module
	/// registers once, before the first scene loads (<c>[RuntimeInitializeOnLoadMethod]</c>); the menu lays the
	/// entries out in <see cref="Entry.Order"/> where its <c>menu-module-entries</c> element stands.
	/// </remarks>
	public static class GameMenuEntries
	{
		/// <summary>One menu destination.</summary>
		public sealed class Entry
		{
			/// <summary>Stable id; registering the same id again replaces the entry.</summary>
			public string Id;
			/// <summary>The button's text.</summary>
			public string Label;
			/// <summary>Position among the module entries, lowest first; ties by id.</summary>
			public int Order;
			/// <summary>What the button does. The menu stays open behind whatever it opens.</summary>
			public Action OnClick;
		}

		private static readonly List<Entry> entries = new List<Entry>();

		/// <summary>Raised when an entry is added, replaced or removed.</summary>
		public static event Action Changed;

		/// <summary>The entries in menu order.</summary>
		public static IReadOnlyList<Entry> All => entries;

		/// <summary>Adds (or replaces, by id) a menu destination.</summary>
		public static void Register(string id, string label, int order, Action onClick)
		{
			if (string.IsNullOrEmpty(id) || onClick == null)
			{
				throw new ArgumentException("A game menu entry needs an id and an action.");
			}
			entries.RemoveAll(e => e.Id == id);
			entries.Add(new Entry { Id = id, Label = label ?? id, Order = order, OnClick = onClick });
			entries.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : string.CompareOrdinal(a.Id, b.Id));
			Changed?.Invoke();
		}

		/// <summary>Removes a menu destination; false when there was none with that id.</summary>
		public static bool Unregister(string id)
		{
			bool removed = entries.RemoveAll(e => e.Id == id) > 0;
			if (removed)
			{
				Changed?.Invoke();
			}
			return removed;
		}
	}
}
