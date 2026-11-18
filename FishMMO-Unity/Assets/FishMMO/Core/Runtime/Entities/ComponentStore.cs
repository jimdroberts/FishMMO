using System;
using System.Threading;

namespace FishMMO.Core
{
	/// <summary>A number per component type, assigned on first use; indexes the world's store table.</summary>
	public static class ComponentTypeIds
	{
		private static int next = -1;

		/// <summary>How many component types have been seen.</summary>
		public static int Count => next + 1;

		internal static int Next() => Interlocked.Increment(ref next);
	}

	/// <summary>The id of component type <typeparamref name="T"/> (<see cref="ComponentTypeIds"/>).</summary>
	public static class ComponentType<T> where T : struct
	{
		/// <summary>This type's id; the same for the whole process.</summary>
		public static readonly int Id = ComponentTypeIds.Next();
	}

	/// <summary>What the world needs from every store, whatever its component type.</summary>
	public interface IComponentStore
	{
		/// <summary>The component type stored.</summary>
		Type ComponentType { get; }

		/// <summary>How many entities have one.</summary>
		int Count { get; }

		/// <summary>Whether <paramref name="entity"/> has one.</summary>
		bool Has(Entity entity);

		/// <summary>Removes <paramref name="entity"/>'s component, if it has one.</summary>
		bool Remove(Entity entity);

		/// <summary>The entity at a dense position (0 to <see cref="Count"/> - 1).</summary>
		Entity EntityAt(int index);

		/// <summary>The component at a dense position, boxed: for tools such as the Entity Inspector, never for systems.</summary>
		object GetBoxed(int index);
	}

	/// <summary>Told that <paramref name="entity"/>'s <paramref name="component"/> has left its store.</summary>
	public delegate void ComponentRemoved<T>(Entity entity, in T component) where T : struct;

	/// <summary>
	/// Every component of one type, packed: a dense array of components with the entity each belongs to, and a
	/// sparse table from entity index to position. Systems walk the dense arrays front to back.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Adding, getting and removing are constant time. Removing moves the last component into the hole, so
	/// positions are not stable across removals: hold an <see cref="Entity"/>, never a position. A system that
	/// removes while it walks uses the world's <see cref="CommandBuffer"/>, which applies removals after the
	/// phase.
	/// </para>
	/// <para>
	/// A component may hold references (an array of modifiers, a name); such a component is copied by value
	/// but its arrays are shared, so it should be changed through <see cref="Get"/>'s reference, never copied
	/// out, changed and set back.
	/// </para>
	/// </remarks>
	public sealed class ComponentStore<T> : IComponentStore where T : struct
	{
		private int[] sparse = new int[64];      // entity index -> dense position + 1; 0 = none
		private Entity[] entities = new Entity[16];
		private T[] data = new T[16];
		private int count;

		/// <summary>
		/// Told when a component leaves the store: removed, or its entity destroyed. For components that own
		/// something outside the store (a slice of a pool) to give it back. Not told when a component is replaced.
		/// </summary>
		public ComponentRemoved<T> Removed { get; set; }

		/// <inheritdoc />
		public Type ComponentType => typeof(T);

		/// <inheritdoc />
		public int Count => count;

		/// <summary>The components, packed; position i belongs to <see cref="Entities"/>[i].</summary>
		public Span<T> Components => new Span<T>(data, 0, count);

		/// <summary>The entities that have one, packed, in the same order as <see cref="Components"/>.</summary>
		public ReadOnlySpan<Entity> Entities => new ReadOnlySpan<Entity>(entities, 0, count);

		/// <inheritdoc />
		public Entity EntityAt(int index) => entities[index];

		/// <inheritdoc />
		public object GetBoxed(int index) => data[index];

		/// <summary>The component at a dense position, by reference.</summary>
		public ref T At(int index) => ref data[index];

		/// <inheritdoc />
		public bool Has(Entity entity) => IndexOf(entity) >= 0;

		/// <summary>The dense position of <paramref name="entity"/>'s component, or false when it has none.</summary>
		public bool TryGetIndex(Entity entity, out int index)
		{
			index = IndexOf(entity);
			return index >= 0;
		}

		/// <summary><paramref name="entity"/>'s component, by reference. Throws when it has none.</summary>
		public ref T Get(Entity entity)
		{
			int index = IndexOf(entity);
			if (index < 0)
			{
				throw new InvalidOperationException($"{entity} has no {typeof(T).Name}.");
			}
			return ref data[index];
		}

		/// <summary>Gives <paramref name="entity"/> the component, replacing one it already has.</summary>
		public void Set(Entity entity, in T value)
		{
			int index = IndexOf(entity);
			if (index >= 0)
			{
				data[index] = value;
				return;
			}
			if (entity.Index >= sparse.Length)
			{
				Array.Resize(ref sparse, Math.Max(sparse.Length * 2, entity.Index + 1));
			}
			if (count == data.Length)
			{
				Array.Resize(ref data, count * 2);
				Array.Resize(ref entities, count * 2);
			}
			data[count] = value;
			entities[count] = entity;
			sparse[entity.Index] = count + 1;
			count++;
		}

		/// <inheritdoc />
		public bool Remove(Entity entity)
		{
			int index = IndexOf(entity);
			if (index < 0)
			{
				return false;
			}
			Removed?.Invoke(entity, data[index]);
			int last = count - 1;
			if (index != last)
			{
				data[index] = data[last];
				entities[index] = entities[last];
				sparse[entities[index].Index] = index + 1;
			}
			data[last] = default;
			entities[last] = default;
			sparse[entity.Index] = 0;
			count--;
			return true;
		}

		private int IndexOf(Entity entity)
		{
			if (entity.IsNull || entity.Index >= sparse.Length)
			{
				return -1;
			}
			int slot = sparse[entity.Index] - 1;
			return slot >= 0 && entities[slot] == entity ? slot : -1;
		}
	}
}
