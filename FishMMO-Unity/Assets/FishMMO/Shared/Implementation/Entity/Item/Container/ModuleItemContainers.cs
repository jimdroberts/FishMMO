using System;
using System.Collections.Generic;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Item containers a module adds to a character and keeps in the character's item table, beyond the three the core
	/// knows (inventory, equipment, bank): the Mercenaries module's mercenary gear. The character load places a row of
	/// such a container through here, and the item snapshot writes the container with the others, in the same
	/// transaction, so an item moved between the bags and a module container is never in both or in neither.
	/// </summary>
	/// <remarks>
	/// A module registers once per process, before any character loads (a <c>RuntimeInitializeOnLoadMethod</c>). Its
	/// container type must be a member of both <see cref="InventoryType"/> and the database's <c>ItemContainerType</c>
	/// (<c>ItemContainerTypeParityTests</c>). Items in such a container are only held: nothing is equipped onto the
	/// character from them.
	/// </remarks>
	public static class ModuleItemContainers
	{
		private struct Entry
		{
			public InventoryType Type;
			public Func<ICharacter, IItemContainer> Resolve;
		}

		private static readonly List<Entry> entries = new List<Entry>();

		/// <summary>How many module containers are registered.</summary>
		public static int Count => entries.Count;

		/// <summary>The container type registered at <paramref name="index"/>.</summary>
		public static InventoryType TypeAt(int index) => entries[index].Type;

		/// <summary>
		/// Registers how a character's container of <paramref name="type"/> is found. A second registration of the same
		/// type replaces the first. The core's own three types cannot be registered.
		/// </summary>
		public static void Register(InventoryType type, Func<ICharacter, IItemContainer> resolve)
		{
			if (resolve == null)
			{
				throw new ArgumentNullException(nameof(resolve));
			}
			if (type == InventoryType.Inventory || type == InventoryType.Equipment || type == InventoryType.Bank)
			{
				throw new ArgumentException($"{type} is a core container; only module containers register here.", nameof(type));
			}
			for (int i = 0; i < entries.Count; ++i)
			{
				if (entries[i].Type == type)
				{
					entries[i] = new Entry() { Type = type, Resolve = resolve };
					return;
				}
			}
			entries.Add(new Entry() { Type = type, Resolve = resolve });
		}

		/// <summary>The character's container of <paramref name="type"/>, or null when none is registered or it has none.</summary>
		public static IItemContainer Resolve(ICharacter character, InventoryType type)
		{
			if (character == null)
			{
				return null;
			}
			for (int i = 0; i < entries.Count; ++i)
			{
				if (entries[i].Type == type)
				{
					return entries[i].Resolve(character);
				}
			}
			return null;
		}

		/// <summary>The character's container registered at <paramref name="index"/>, or null.</summary>
		public static IItemContainer ResolveAt(ICharacter character, int index)
		{
			return character != null && index >= 0 && index < entries.Count ? entries[index].Resolve(character) : null;
		}
	}
}
