using System;

namespace FishMMO.Core
{
	/// <summary>
	/// An engine object's way into the simulation: the world and the entity that stand for it. A character
	/// holds one, so feature state lives as components on its entity rather than as fields on the character.
	/// </summary>
	public readonly struct EntityLink : IEquatable<EntityLink>
	{
		/// <summary>The world the entity lives in, or null for no link.</summary>
		public readonly World World;

		/// <summary>The entity.</summary>
		public readonly Entity Entity;

		/// <summary>Links <paramref name="entity"/> in <paramref name="world"/>.</summary>
		public EntityLink(World world, Entity entity)
		{
			World = world;
			Entity = entity;
		}

		/// <summary>No link.</summary>
		public static readonly EntityLink None = default;

		/// <summary>Whether the link names an entity that still exists.</summary>
		public bool IsValid => World != null && World.IsAlive(Entity);

		/// <summary>Whether the entity has a <typeparamref name="T"/>.</summary>
		public bool Has<T>() where T : struct => IsValid && World.Has<T>(Entity);

		/// <summary>The entity's <typeparamref name="T"/>, by reference. Throws when the link is gone or the entity has none.</summary>
		public ref T Get<T>() where T : struct
		{
			if (World == null)
			{
				throw new InvalidOperationException("This object has no entity: it is not spawned, or no Core world runs on its side.");
			}
			return ref World.Get<T>(Entity);
		}

		/// <summary>The entity's <typeparamref name="T"/> as a copy, or false when the link is gone or the entity has none.</summary>
		public bool TryGet<T>(out T component) where T : struct
		{
			if (World != null && World.IsAlive(Entity) && World.Store<T>().TryGetIndex(Entity, out int index))
			{
				component = World.Store<T>().At(index);
				return true;
			}
			component = default;
			return false;
		}

		/// <inheritdoc />
		public bool Equals(EntityLink other) => ReferenceEquals(World, other.World) && Entity == other.Entity;

		/// <inheritdoc />
		public override bool Equals(object obj) => obj is EntityLink other && Equals(other);

		/// <inheritdoc />
		public override int GetHashCode() => Entity.GetHashCode();

		/// <inheritdoc />
		public override string ToString() => World == null ? "EntityLink(none)" : $"EntityLink({Entity})";
	}
}
