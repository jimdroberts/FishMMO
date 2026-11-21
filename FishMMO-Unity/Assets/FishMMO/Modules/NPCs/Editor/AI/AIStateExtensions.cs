#if UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// AI state types other modules add to the NPCs module's editor tools (2026-11-20: the Pets module's pet states): the
	/// dashboard's AI States category lists them, groups and sorts them, and the asset organizer files them.
	/// </summary>
	public static class AIStateExtensions
	{
		/// <summary>One added state type.</summary>
		public readonly struct Entry
		{
			public readonly string Name;
			public readonly Type Type;
			/// <summary>Its group in the dashboard's list, or null for the default (Combat for attacking states, else Movement).</summary>
			public readonly string Group;
			/// <summary>Its group's place in the list (Combat 0, Combat Positioning 20, Movement 30).</summary>
			public readonly int Order;
			/// <summary>The folder new assets of it are filed in, or null for the default.</summary>
			public readonly string Folder;

			public Entry(string name, Type type, string group, int order, string folder)
			{
				Name = name;
				Type = type;
				Group = group;
				Order = order;
				Folder = folder;
			}
		}

		/// <summary>The added types; modules add from an <c>[InitializeOnLoadMethod]</c>.</summary>
		public static readonly List<Entry> States = new List<Entry>();

		/// <summary>Adds a state type once.</summary>
		public static void Add(string name, Type type, string group, int order, string folder)
		{
			if (type == null || States.Exists(e => e.Type == type))
			{
				return;
			}
			States.Add(new Entry(name, type, group, order, folder));
		}

		/// <summary>The entry whose type <paramref name="assetType"/> is or derives from, if any.</summary>
		public static bool TryFind(Type assetType, out Entry entry)
		{
			foreach (Entry e in States)
			{
				if (assetType != null && e.Type.IsAssignableFrom(assetType))
				{
					entry = e;
					return true;
				}
			}
			entry = default;
			return false;
		}
	}
}
#endif
