using System;
using System.Collections.Generic;

namespace FishMMO.Core
{
	/// <summary>A stat's identity (health, mana, move speed, <c>reputation.elves</c>), registered by name.</summary>
	public readonly struct StatId : IEquatable<StatId>
	{
		/// <summary>The registered number; 0 is no stat.</summary>
		public readonly int Value;

		internal StatId(int value)
		{
			Value = value;
		}

		/// <summary>Whether this names no stat.</summary>
		public bool IsNone => Value == 0;

		/// <summary>The stat named <paramref name="name"/>, registered on first use.</summary>
		public static StatId Of(string name) => StatRegistry.Get(name);

		/// <inheritdoc />
		public bool Equals(StatId other) => Value == other.Value;

		/// <inheritdoc />
		public override bool Equals(object obj) => obj is StatId other && Equals(other);

		/// <inheritdoc />
		public override int GetHashCode() => Value;

		/// <summary>Same stat.</summary>
		public static bool operator ==(StatId a, StatId b) => a.Value == b.Value;

		/// <summary>Different stat.</summary>
		public static bool operator !=(StatId a, StatId b) => a.Value != b.Value;

		/// <inheritdoc />
		public override string ToString() => StatRegistry.NameOf(this);
	}

	/// <summary>
	/// Stat names to numbers, for the whole process. Modules name their stats ("health", "mana",
	/// "reputation.elves") and never need each other's types to agree on one.
	/// </summary>
	public static class StatRegistry
	{
		private static readonly object gate = new object();
		private static readonly Dictionary<string, int> byName = new Dictionary<string, int>(StringComparer.Ordinal);
		private static readonly List<string> names = new List<string> { "(none)" };

		/// <summary>The stat named <paramref name="name"/>, registered on first use.</summary>
		public static StatId Get(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				throw new ArgumentException("A stat needs a name.", nameof(name));
			}
			lock (gate)
			{
				if (!byName.TryGetValue(name, out int id))
				{
					id = names.Count;
					names.Add(name);
					byName[name] = id;
				}
				return new StatId(id);
			}
		}

		/// <summary>The name <paramref name="id"/> was registered with.</summary>
		public static string NameOf(StatId id)
		{
			lock (gate)
			{
				return id.Value >= 0 && id.Value < names.Count ? names[id.Value] : $"(stat {id.Value})";
			}
		}
	}

	/// <summary>One stat on one entity, stored in its world's <see cref="StatPool"/>.</summary>
	public struct StatEntry
	{
		/// <summary>Which stat.</summary>
		public StatId Id;

		/// <summary>The value before modifiers.</summary>
		public float Base;

		/// <summary>The value after modifiers: (<see cref="Base"/> + every Add) × (1 + every Multiply), set by <see cref="StatSystem"/>.</summary>
		public float Value;

		/// <summary>For a resource (health, mana), how much is left, from 0 to <see cref="Value"/>.</summary>
		public float Current;

		/// <summary>Whether this stat is a resource with a <see cref="Current"/>.</summary>
		public bool IsResource;

		internal float AddSum;
		internal float MultiplySum;
	}

	/// <summary>How a modifier changes a stat.</summary>
	public enum ModifierOp : byte
	{
		/// <summary>Adds its amount to the base.</summary>
		Add = 0,
		/// <summary>Adds its amount to the percentage multiplier: 0.1 is +10%.</summary>
		Multiply = 1,
	}

	/// <summary>One change to one stat.</summary>
	public struct StatModifier
	{
		/// <summary>Where the stat was last found in its target's stats: a lookup cache, never read as truth.</summary>
		internal int Hint;

		/// <summary>The stat changed.</summary>
		public readonly StatId Stat;

		/// <summary>How it is changed.</summary>
		public readonly ModifierOp Op;

		/// <summary>By how much.</summary>
		public readonly float Amount;

		/// <summary>Builds a modifier.</summary>
		public StatModifier(StatId stat, ModifierOp op, float amount)
		{
			Stat = stat;
			Op = op;
			Amount = amount;
			Hint = 0;
		}
	}

	/// <summary>
	/// A set of modifiers applied to another entity's stats, on an entity of its own. A buff, an aura or an
	/// equipped item's bonuses are such entities; one that should end gets a <see cref="Lifetime"/>, and
	/// destroying the entity removes its modifiers.
	/// </summary>
	public struct ModifierSet
	{
		/// <summary>The entity whose stats are changed.</summary>
		public Entity Target;

		/// <summary>The changes. Shared, not copied: build a new array rather than editing one in use.</summary>
		public StatModifier[] Modifiers;
	}
}
