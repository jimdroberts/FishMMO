using System;
using System.Collections.Generic;

namespace FishMMO.Core
{
	/// <summary>A tag's identity (Stunned, InCombat, Swimming, Undead), registered by name.</summary>
	public readonly struct TagId : IEquatable<TagId>
	{
		/// <summary>The registered number; 0 is no tag.</summary>
		public readonly int Value;

		internal TagId(int value)
		{
			Value = value;
		}

		/// <summary>The tag named <paramref name="name"/>, registered on first use.</summary>
		public static TagId Of(string name) => TagRegistry.Get(name);

		/// <inheritdoc />
		public bool Equals(TagId other) => Value == other.Value;

		/// <inheritdoc />
		public override bool Equals(object obj) => obj is TagId other && Equals(other);

		/// <inheritdoc />
		public override int GetHashCode() => Value;

		/// <inheritdoc />
		public override string ToString() => TagRegistry.NameOf(this);
	}

	/// <summary>Tag names to numbers, for the whole process.</summary>
	public static class TagRegistry
	{
		private static readonly object gate = new object();
		private static readonly Dictionary<string, int> byName = new Dictionary<string, int>(StringComparer.Ordinal);
		private static readonly List<string> names = new List<string> { "(none)" };

		/// <summary>The tag named <paramref name="name"/>, registered on first use.</summary>
		public static TagId Get(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				throw new ArgumentException("A tag needs a name.", nameof(name));
			}
			lock (gate)
			{
				if (!byName.TryGetValue(name, out int id))
				{
					id = names.Count;
					names.Add(name);
					byName[name] = id;
				}
				return new TagId(id);
			}
		}

		/// <summary>The name <paramref name="id"/> was registered with.</summary>
		public static string NameOf(TagId id)
		{
			lock (gate)
			{
				return id.Value >= 0 && id.Value < names.Count ? names[id.Value] : $"(tag {id.Value})";
			}
		}
	}

	/// <summary>
	/// An entity's tags, each counted: two effects that both stun hold the tag until both are gone, so neither
	/// ending clears the other's.
	/// </summary>
	public sealed class TagTable
	{
		private readonly List<(TagId Tag, int Count)> tags = new List<(TagId, int)>(4);

		/// <summary>Whether the entity has <paramref name="tag"/>.</summary>
		public bool Has(TagId tag) => IndexOf(tag) >= 0;

		/// <summary>How many sources hold <paramref name="tag"/>.</summary>
		public int CountOf(TagId tag)
		{
			int index = IndexOf(tag);
			return index >= 0 ? tags[index].Count : 0;
		}

		/// <summary>Adds one hold on <paramref name="tag"/>.</summary>
		public void Add(TagId tag)
		{
			int index = IndexOf(tag);
			if (index >= 0)
			{
				tags[index] = (tag, tags[index].Count + 1);
			}
			else
			{
				tags.Add((tag, 1));
			}
		}

		/// <summary>Releases one hold on <paramref name="tag"/>; the tag goes when the last hold does.</summary>
		public void Remove(TagId tag)
		{
			int index = IndexOf(tag);
			if (index < 0)
			{
				return;
			}
			int left = tags[index].Count - 1;
			if (left <= 0)
			{
				tags.RemoveAt(index);
			}
			else
			{
				tags[index] = (tag, left);
			}
		}

		private int IndexOf(TagId tag)
		{
			for (int i = 0; i < tags.Count; i++)
			{
				if (tags[i].Tag.Value == tag.Value)
				{
					return i;
				}
			}
			return -1;
		}
	}

	/// <summary>The tags component: an entity's <see cref="TagTable"/>.</summary>
	public struct Tags
	{
		/// <summary>The entity's tags.</summary>
		public TagTable Table;

		/// <summary>A tags component with an empty table.</summary>
		public static Tags Create() => new Tags { Table = new TagTable() };
	}
}
