using System;
using System.Collections.Generic;

namespace FishMMO.Client
{
	/// <summary>Where a character's context menu was opened from.</summary>
	public enum CharacterMenuSource : byte
	{
		/// <summary>Right-clicking a player in the world.</summary>
		World = 0,
		/// <summary>A row of the guild roster.</summary>
		Guild = 1,
		/// <summary>A row of the party roster.</summary>
		Party = 2,
		/// <summary>A row of the friend list.</summary>
		Friends = 3,
	}

	/// <summary>The character a context menu is about: an id and a name, which is all a roster row has.</summary>
	public readonly struct CharacterMenuTarget
	{
		/// <summary>The character's database id.</summary>
		public readonly long CharacterID;
		/// <summary>The character's name, read when the menu was built; may be empty.</summary>
		public readonly string Name;
		/// <summary>The menu it is for.</summary>
		public readonly CharacterMenuSource Source;

		public CharacterMenuTarget(long characterID, string name, CharacterMenuSource source)
		{
			CharacterID = characterID;
			Name = name ?? string.Empty;
			Source = source;
		}
	}

	/// <summary>
	/// The actions modules add to a character's context menu: "Add Friend" from Social, and so on.
	/// </summary>
	/// <remarks>
	/// The same character can be reached three ways (in the world, in the guild roster, in the party roster), and an
	/// action such as befriending someone belongs in all three. The menus keep their own entries (Inspect, Message, the
	/// rank entries) and call <see cref="AppendTo"/> where module entries go, so a removed module leaves no entry naming
	/// it. A module registers once, before the first scene loads (<c>[RuntimeInitializeOnLoadMethod]</c>), as with
	/// <see cref="GameMenuEntries"/>.
	/// </remarks>
	public static class CharacterMenuEntries
	{
		/// <summary>One action.</summary>
		public sealed class Entry
		{
			/// <summary>Stable id; registering the same id again replaces the entry.</summary>
			public string Id;
			/// <summary>The entry's text.</summary>
			public string Label;
			/// <summary>Position among the module entries, lowest first; ties by id.</summary>
			public int Order;
			/// <summary>Whether the entry is offered for a target; null offers it always.</summary>
			public Func<CharacterMenuTarget, bool> Applies;
			/// <summary>What the entry does.</summary>
			public Action<CharacterMenuTarget> OnClick;
		}

		private static readonly List<Entry> entries = new List<Entry>();

		/// <summary>The entries in menu order.</summary>
		public static IReadOnlyList<Entry> All => entries;

		/// <summary>Adds (or replaces, by id) an action.</summary>
		public static void Register(string id, string label, int order, Action<CharacterMenuTarget> onClick, Func<CharacterMenuTarget, bool> applies = null)
		{
			if (string.IsNullOrEmpty(id) || onClick == null)
			{
				throw new ArgumentException("A character menu entry needs an id and an action.");
			}
			entries.RemoveAll(e => e.Id == id);
			entries.Add(new Entry { Id = id, Label = label ?? id, Order = order, Applies = applies, OnClick = onClick });
			entries.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : string.CompareOrdinal(a.Id, b.Id));
		}

		/// <summary>Removes an action; false when there was none with that id.</summary>
		public static bool Unregister(string id)
		{
			return entries.RemoveAll(e => e.Id == id) > 0;
		}

		/// <summary>Adds every entry that applies to <paramref name="target"/> to a menu being built.</summary>
		public static void AppendTo(List<(string label, Action callback)> menu, CharacterMenuTarget target)
		{
			if (menu == null)
			{
				return;
			}
			for (int i = 0; i < entries.Count; ++i)
			{
				Entry entry = entries[i];
				if (entry.Applies != null && !entry.Applies(target))
				{
					continue;
				}
				Action<CharacterMenuTarget> onClick = entry.OnClick;
				menu.Add((entry.Label, () => onClick(target)));
			}
		}
	}
}
