using System;

namespace FishMMO.Core
{
	/// <summary>
	/// A thing in the world: a character, an NPC, an ability in flight, a timed modifier, a corpse. Only an id;
	/// everything it is lives in the components stored against it.
	/// </summary>
	/// <remarks>
	/// The index is reused once an entity is destroyed, and the generation goes up each time, so a reference
	/// held past its entity's end is detected (<see cref="World.IsAlive"/>) instead of silently naming its
	/// successor. Generation 0 is never alive: <c>default(Entity)</c> is <see cref="Null"/>.
	/// </remarks>
	public readonly struct Entity : IEquatable<Entity>
	{
		/// <summary>No entity.</summary>
		public static readonly Entity Null = default;

		/// <summary>The slot in the world; reused after the entity is destroyed.</summary>
		public readonly int Index;

		/// <summary>Which use of the slot this is; 0 means no entity.</summary>
		public readonly int Generation;

		/// <summary>Builds a reference to slot <paramref name="index"/> at <paramref name="generation"/>.</summary>
		public Entity(int index, int generation)
		{
			Index = index;
			Generation = generation;
		}

		/// <summary>Whether this is <see cref="Null"/>.</summary>
		public bool IsNull => Generation == 0;

		/// <inheritdoc />
		public bool Equals(Entity other) => Index == other.Index && Generation == other.Generation;

		/// <inheritdoc />
		public override bool Equals(object obj) => obj is Entity other && Equals(other);

		/// <inheritdoc />
		public override int GetHashCode() => (Index * 397) ^ Generation;

		/// <summary>Same slot and generation.</summary>
		public static bool operator ==(Entity a, Entity b) => a.Equals(b);

		/// <summary>Different slot or generation.</summary>
		public static bool operator !=(Entity a, Entity b) => !a.Equals(b);

		/// <inheritdoc />
		public override string ToString() => IsNull ? "Entity(null)" : $"Entity({Index}:{Generation})";
	}
}
