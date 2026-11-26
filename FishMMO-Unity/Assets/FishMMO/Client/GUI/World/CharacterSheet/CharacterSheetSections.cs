using System;
using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>One line of a module's character sheet section: a label, its value, and what hovering it explains.</summary>
	public readonly struct CharacterSheetRow
	{
		/// <summary>The row's name, left.</summary>
		public readonly string Label;
		/// <summary>The row's value, right.</summary>
		public readonly string Value;
		/// <summary>The hover tooltip's text; empty for none.</summary>
		public readonly string Description;

		public CharacterSheetRow(string label, string value, string description = null)
		{
			Label = label ?? string.Empty;
			Value = value ?? string.Empty;
			Description = description ?? string.Empty;
		}
	}

	/// <summary>
	/// The sections modules add under the character sheet's attributes: "Duels" from Duels, and so on.
	/// </summary>
	/// <remarks>
	/// For numbers a module keeps itself rather than as character attributes (attributes list themselves, under their
	/// <see cref="CharacterAttributeTemplate.SheetCategory"/>). A module registers once, before the first scene loads
	/// (<c>[RuntimeInitializeOnLoadMethod]</c>), as with <see cref="CharacterMenuEntries"/>, and calls
	/// <see cref="NotifyChanged"/> when its numbers move; an open sheet rebuilds its rows. A removed module leaves no
	/// heading naming it.
	/// </remarks>
	public static class CharacterSheetSections
	{
		/// <summary>One section.</summary>
		public sealed class Section
		{
			/// <summary>Stable id; registering the same id again replaces the section.</summary>
			public string Id;
			/// <summary>The section's heading.</summary>
			public string Title;
			/// <summary>Position among the module sections, lowest first; ties by id.</summary>
			public int Order;
			/// <summary>Adds the rows for a subject; false (or no rows) leaves the section off the sheet.</summary>
			public Func<IPlayerCharacter, List<CharacterSheetRow>, bool> Fill;
		}

		private static readonly List<Section> sections = new List<Section>();

		/// <summary>The sections in sheet order.</summary>
		public static IReadOnlyList<Section> All => sections;

		/// <summary>Raised by <see cref="NotifyChanged"/>: a section's numbers moved.</summary>
		public static event Action Changed;

		/// <summary>Adds (or replaces, by id) a section.</summary>
		public static void Register(string id, string title, int order, Func<IPlayerCharacter, List<CharacterSheetRow>, bool> fill)
		{
			if (string.IsNullOrEmpty(id) || fill == null)
			{
				throw new ArgumentException("A character sheet section needs an id and a way to fill it.");
			}
			sections.RemoveAll(s => s.Id == id);
			sections.Add(new Section { Id = id, Title = title ?? id, Order = order, Fill = fill });
			sections.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : string.CompareOrdinal(a.Id, b.Id));
		}

		/// <summary>Removes a section; false when there was none with that id.</summary>
		public static bool Unregister(string id)
		{
			return sections.RemoveAll(s => s.Id == id) > 0;
		}

		/// <summary>Tells any open sheet a section's numbers moved.</summary>
		public static void NotifyChanged()
		{
			Changed?.Invoke();
		}
	}
}
