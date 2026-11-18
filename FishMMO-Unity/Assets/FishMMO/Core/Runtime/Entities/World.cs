using System;
using System.Collections.Generic;

namespace FishMMO.Core
{
	/// <summary>
	/// Every entity and component of one simulation: one per server process, one on the client. Systems
	/// (<see cref="GameSystem"/>) read and change it each tick through the <see cref="SystemScheduler"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Creating an entity is safe at any time, including while a system walks a store: a new entity has no
	/// components until something gives it one. Adding or removing components and destroying entities while
	/// stores are being walked is not: systems do those through <see cref="Commands"/>, which the scheduler
	/// applies at the end of each phase.
	/// </para>
	/// <para>
	/// Not thread-safe. The world belongs to the thread that ticks it.
	/// </para>
	/// </remarks>
	public sealed class World
	{
		private int[] generations = new int[64];
		private bool[] alive = new bool[64];
		private int[] free = new int[64];
		private int freeCount;
		private int nextIndex;
		private int aliveCount;

		private IComponentStore[] stores = new IComponentStore[32];
		private readonly List<IComponentStore> storeList = new List<IComponentStore>();
		private IEventStream[] streams = new IEventStream[16];
		private readonly List<IEventStream> streamList = new List<IEventStream>();

		/// <summary>Creates an empty world.</summary>
		public World()
		{
			Commands = new CommandBuffer(this);
		}

		/// <summary>Structural changes deferred to the end of the current phase.</summary>
		public CommandBuffer Commands { get; }

		/// <summary>
		/// True while a <see cref="SystemScheduler"/> is ticking this world: structural changes then go through
		/// <see cref="Commands"/>, since a system may be walking a store.
		/// </summary>
		public bool IsTicking { get; internal set; }

		/// <summary>How many entities are alive.</summary>
		public int EntityCount => aliveCount;

		/// <summary>Every store that exists so far, in creation order.</summary>
		public IReadOnlyList<IComponentStore> Stores => storeList;

		/// <summary>A new entity with no components.</summary>
		public Entity Create()
		{
			int index;
			if (freeCount > 0)
			{
				index = free[--freeCount];
			}
			else
			{
				index = nextIndex++;
				if (index >= generations.Length)
				{
					Array.Resize(ref generations, generations.Length * 2);
					Array.Resize(ref alive, alive.Length * 2);
				}
			}
			int generation = generations[index] + 1;
			if (generation <= 0)
			{
				generation = 1;   // wrapped after two billion uses of one slot
			}
			generations[index] = generation;
			alive[index] = true;
			aliveCount++;
			return new Entity(index, generation);
		}

		/// <summary>Whether <paramref name="entity"/> still exists.</summary>
		public bool IsAlive(Entity entity) =>
			!entity.IsNull && entity.Index < nextIndex && alive[entity.Index] && generations[entity.Index] == entity.Generation;

		/// <summary>
		/// Destroys <paramref name="entity"/> and every component it has, at once. Inside a system, use
		/// <see cref="CommandBuffer.Destroy"/> instead.
		/// </summary>
		public bool Destroy(Entity entity)
		{
			if (!IsAlive(entity))
			{
				return false;
			}
			for (int i = 0; i < storeList.Count; i++)
			{
				storeList[i].Remove(entity);
			}
			alive[entity.Index] = false;
			if (freeCount == free.Length)
			{
				Array.Resize(ref free, free.Length * 2);
			}
			free[freeCount++] = entity.Index;
			aliveCount--;
			return true;
		}

		/// <summary>The store of component type <typeparamref name="T"/>, created on first use.</summary>
		public ComponentStore<T> Store<T>() where T : struct
		{
			int id = ComponentType<T>.Id;
			if (id >= stores.Length)
			{
				Array.Resize(ref stores, Math.Max(stores.Length * 2, id + 1));
			}
			IComponentStore store = stores[id];
			if (store == null)
			{
				store = new ComponentStore<T>();
				stores[id] = store;
				storeList.Add(store);
			}
			return (ComponentStore<T>)store;
		}

		/// <summary>Gives <paramref name="entity"/> a component at once, replacing one it has. Inside a system, use <see cref="CommandBuffer.Add{T}"/>.</summary>
		public void Add<T>(Entity entity, in T component) where T : struct
		{
			if (!IsAlive(entity))
			{
				throw new InvalidOperationException($"{entity} is not alive.");
			}
			Store<T>().Set(entity, component);
		}

		/// <summary>Whether <paramref name="entity"/> has a <typeparamref name="T"/>.</summary>
		public bool Has<T>(Entity entity) where T : struct => Store<T>().Has(entity);

		/// <summary><paramref name="entity"/>'s <typeparamref name="T"/>, by reference. Throws when it has none.</summary>
		public ref T Get<T>(Entity entity) where T : struct => ref Store<T>().Get(entity);

		/// <summary>Removes <paramref name="entity"/>'s <typeparamref name="T"/> at once. Inside a system, use <see cref="CommandBuffer.Remove{T}"/>.</summary>
		public bool Remove<T>(Entity entity) where T : struct => Store<T>().Remove(entity);

		/// <summary>The stream of events of type <typeparamref name="T"/>, created on first use.</summary>
		public EventStream<T> Events<T>() where T : struct
		{
			int id = EventType<T>.Id;
			if (id >= streams.Length)
			{
				Array.Resize(ref streams, Math.Max(streams.Length * 2, id + 1));
			}
			IEventStream stream = streams[id];
			if (stream == null)
			{
				stream = new EventStream<T>();
				streams[id] = stream;
				streamList.Add(stream);
			}
			return (EventStream<T>)stream;
		}

		private readonly Dictionary<Type, object> resources = new Dictionary<Type, object>();

		/// <summary>
		/// The world's single <typeparamref name="T"/>, created on first use: state that belongs to the whole
		/// simulation rather than to an entity (an effect queue, a registry of handlers).
		/// </summary>
		public T Resource<T>() where T : class, new()
		{
			if (!resources.TryGetValue(typeof(T), out object value))
			{
				value = new T();
				resources[typeof(T)] = value;
			}
			return (T)value;
		}

		/// <summary>Makes <paramref name="value"/> the world's single <typeparamref name="T"/>, replacing any before it.</summary>
		public void SetResource<T>(T value) where T : class
		{
			resources[typeof(T)] = value ?? throw new ArgumentNullException(nameof(value));
		}

		/// <summary>The world's <typeparamref name="T"/>, or false when it has none (none is created).</summary>
		public bool TryGetResource<T>(out T value) where T : class
		{
			if (resources.TryGetValue(typeof(T), out object found))
			{
				value = (T)found;
				return true;
			}
			value = null;
			return false;
		}

		/// <summary>Empties every event stream: the end of a tick.</summary>
		public void ClearEvents()
		{
			for (int i = 0; i < streamList.Count; i++)
			{
				streamList[i].Clear();
			}
		}
	}
}
