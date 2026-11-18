using System;
using System.Collections.Generic;

namespace FishMMO.Core
{
	/// <summary>
	/// The stats component: which slice of the world's <see cref="StatPool"/> holds the entity's stats. Made
	/// by <see cref="StatWorld.AddStats"/>; read through <see cref="StatWorld.StatsOf"/>. Never built by hand
	/// or copied onto another entity: the slice belongs to one entity and is given back when it goes.
	/// </summary>
	public struct Stats
	{
		internal int Slot;
		internal int Generation;

		/// <summary>Whether this names a slice (a <c>default</c> Stats names none).</summary>
		public bool IsValid => Slot > 0;
	}

	/// <summary>
	/// Every entity's stats in one world, packed into one array: each entity owns a contiguous slice, so a
	/// system that walks every entity's stats reads memory front to back instead of chasing a table per entity.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A slice has room for a power of two of stats; defining one more moves the slice to a larger one. A slice
	/// given back (its entity destroyed or its stats removed) is reused by the next slice of its size.
	/// </para>
	/// <para>
	/// The pool learns of removals through the stats store's <see cref="ComponentStore{T}.Removed"/>; get it
	/// with <see cref="Of"/>, which connects the two, rather than <see cref="World.Resource{T}"/>.
	/// </para>
	/// </remarks>
	public sealed class StatPool
	{
		private const int MinimumCapacity = 4;

		internal StatEntry[] Entries = new StatEntry[256];
		private int used;

		internal int[] Starts = new int[64];
		internal int[] Counts = new int[64];
		private int[] capacities = new int[64];
		internal int[] Generations = new int[64];
		private int slotCount = 1;   // slot 0 is no slice
		private readonly Stack<int> freeSlots = new Stack<int>();
		private readonly List<int>[] freeSlices = new List<int>[31];
		private bool connected;
		private int liveSlots;

		/// <summary>The world's pool, connected to its stats store so destroyed entities give their slices back.</summary>
		public static StatPool Of(World world)
		{
			StatPool pool = world.Resource<StatPool>();
			if (!pool.connected)
			{
				pool.connected = true;
				world.Store<Stats>().Removed += pool.OnStatsRemoved;
			}
			return pool;
		}

		/// <summary>How many entities' stats the pool holds.</summary>
		public int LiveSlices => liveSlots;

		/// <summary>How much of the array is in use or waiting for reuse, in stats.</summary>
		public int UsedEntries => used;

		/// <summary>A new empty slice with room for <paramref name="capacity"/> stats before it must move.</summary>
		public Stats Allocate(int capacity = 8)
		{
			int slot;
			if (freeSlots.Count > 0)
			{
				slot = freeSlots.Pop();
			}
			else
			{
				slot = slotCount++;
				if (slot >= Starts.Length)
				{
					int size = Starts.Length * 2;
					Array.Resize(ref Starts, size);
					Array.Resize(ref Counts, size);
					Array.Resize(ref capacities, size);
					Array.Resize(ref Generations, size);
				}
			}
			int rounded = RoundCapacity(capacity);
			Starts[slot] = TakeSlice(rounded);
			Counts[slot] = 0;
			capacities[slot] = rounded;
			int generation = Generations[slot] + 1;
			Generations[slot] = generation <= 0 ? 1 : generation;
			liveSlots++;
			return new Stats { Slot = slot, Generation = Generations[slot] };
		}

		/// <summary>Gives a slice back. Done for you when an entity's stats leave the store.</summary>
		public void Release(in Stats stats)
		{
			if (!IsCurrent(stats.Slot, stats.Generation))
			{
				return;
			}
			int slot = stats.Slot;
			GiveSlice(Starts[slot], capacities[slot]);
			Counts[slot] = 0;
			capacities[slot] = 0;
			Generations[slot]++;   // tables still naming this slice now fail instead of reading its successor
			freeSlots.Push(slot);
			liveSlots--;
		}

		/// <summary>A view of <paramref name="stats"/>'s slice.</summary>
		public StatTable Table(in Stats stats) => new StatTable(this, stats.Slot, stats.Generation);

		internal bool IsCurrent(int slot, int generation) =>
			slot > 0 && slot < slotCount && Generations[slot] == generation && capacities[slot] > 0;

		internal bool IsCurrent(in Stats stats) => IsCurrent(stats.Slot, stats.Generation);

		/// <summary>The position in <see cref="Entries"/> of stat <paramref name="id"/> in <paramref name="slot"/>, or -1.</summary>
		internal int Find(int slot, StatId id)
		{
			StatEntry[] entries = Entries;
			int start = Starts[slot];
			int end = start + Counts[slot];
			for (int i = start; i < end; i++)
			{
				if (entries[i].Id.Value == id.Value)
				{
					return i;
				}
			}
			return -1;
		}

		/// <summary>
		/// <see cref="Find(int, StatId)"/>, trying position <paramref name="hint"/> first and remembering where
		/// the stat was found. Entities made from the same template define their stats in the same order, so a
		/// hint kept in shared data (a rate, a modifier) nearly always lands, and the search is skipped. Any hint
		/// is safe: a wrong one only costs the search.
		/// </summary>
		internal int Find(int slot, StatId id, ref int hint)
		{
			int start = Starts[slot];
			int guess = hint;
			if ((uint)guess < (uint)Counts[slot] && Entries[start + guess].Id.Value == id.Value)
			{
				return start + guess;
			}
			int at = Find(slot, id);
			if (at >= 0)
			{
				hint = at - start;
			}
			return at;
		}

		/// <summary>Appends an entry to <paramref name="slot"/>, moving the slice when it is full; returns its position.</summary>
		internal int Append(int slot)
		{
			if (Counts[slot] == capacities[slot])
			{
				int larger = capacities[slot] * 2;
				int moved = TakeSlice(larger);
				Array.Copy(Entries, Starts[slot], Entries, moved, Counts[slot]);
				GiveSlice(Starts[slot], capacities[slot]);
				Starts[slot] = moved;
				capacities[slot] = larger;
			}
			return Starts[slot] + Counts[slot]++;
		}

		private void OnStatsRemoved(Entity entity, in Stats stats) => Release(stats);

		private int TakeSlice(int capacity)
		{
			List<int> free = freeSlices[Bucket(capacity)];
			if (free != null && free.Count > 0)
			{
				int start = free[free.Count - 1];
				free.RemoveAt(free.Count - 1);
				return start;
			}
			if (used + capacity > Entries.Length)
			{
				Array.Resize(ref Entries, Math.Max(Entries.Length * 2, used + capacity));
			}
			int at = used;
			used += capacity;
			return at;
		}

		private void GiveSlice(int start, int capacity)
		{
			Array.Clear(Entries, start, capacity);
			int bucket = Bucket(capacity);
			(freeSlices[bucket] ??= new List<int>()).Add(start);
		}

		private static int RoundCapacity(int capacity)
		{
			int rounded = MinimumCapacity;
			while (rounded < capacity)
			{
				rounded *= 2;
			}
			return rounded;
		}

		private static int Bucket(int capacity)
		{
			int bucket = 0;
			while ((1 << bucket) < capacity)
			{
				bucket++;
			}
			return bucket;
		}
	}

	/// <summary>
	/// A view of one entity's stats. Cheap to make and safe to keep: it finds the slice afresh on each call, so
	/// it stays right when the slice moves, and throws once the entity's stats are gone.
	/// </summary>
	public readonly struct StatTable
	{
		private readonly StatPool pool;
		private readonly int slot;
		private readonly int generation;

		internal StatTable(StatPool pool, int slot, int generation)
		{
			this.pool = pool;
			this.slot = slot;
			this.generation = generation;
		}

		/// <summary>Whether the stats still exist.</summary>
		public bool IsValid => pool != null && pool.IsCurrent(slot, generation);

		/// <summary>How many stats the entity has.</summary>
		public int Count => Check().Counts[slot];

		/// <summary>The stat at a position (0 to <see cref="Count"/> - 1), by reference. Defining a stat may move it; take it again after.</summary>
		public ref StatEntry At(int index)
		{
			StatPool p = Check();
			if ((uint)index >= (uint)p.Counts[slot])
			{
				throw new ArgumentOutOfRangeException(nameof(index));
			}
			return ref p.Entries[p.Starts[slot] + index];
		}

		/// <summary>
		/// Gives the entity stat <paramref name="id"/> with a base value; a resource starts full unless
		/// <paramref name="current"/> says otherwise. Redefining keeps the stat's place.
		/// </summary>
		public void Define(StatId id, float baseValue, bool isResource = false, float? current = null)
		{
			if (id.IsNone)
			{
				throw new ArgumentException("No stat.", nameof(id));
			}
			StatPool p = Check();
			int at = p.Find(slot, id);
			if (at < 0)
			{
				at = p.Append(slot);
			}
			p.Entries[at] = new StatEntry
			{
				Id = id,
				Base = baseValue,
				Value = baseValue,
				IsResource = isResource,
				Current = isResource ? Math.Min(current ?? baseValue, baseValue) : 0f,
			};
		}

		/// <summary>Whether the entity has stat <paramref name="id"/>.</summary>
		public bool Has(StatId id) => Check().Find(slot, id) >= 0;

		/// <summary>The stat's value after modifiers, or 0 when the entity does not have it.</summary>
		public float Value(StatId id)
		{
			StatPool p = Check();
			int at = p.Find(slot, id);
			return at >= 0 ? p.Entries[at].Value : 0f;
		}

		/// <summary>The stat's base value, or 0 when the entity does not have it.</summary>
		public float Base(StatId id)
		{
			StatPool p = Check();
			int at = p.Find(slot, id);
			return at >= 0 ? p.Entries[at].Base : 0f;
		}

		/// <summary>Sets the stat's base value; its <see cref="StatEntry.Value"/> follows at the next recalculation.</summary>
		public bool SetBase(StatId id, float baseValue)
		{
			StatPool p = Check();
			int at = p.Find(slot, id);
			if (at < 0)
			{
				return false;
			}
			p.Entries[at].Base = baseValue;
			return true;
		}

		/// <summary>How much of a resource is left, or 0 when the entity does not have it.</summary>
		public float Current(StatId id)
		{
			StatPool p = Check();
			int at = p.Find(slot, id);
			return at >= 0 ? p.Entries[at].Current : 0f;
		}

		/// <summary>Sets how much of a resource is left, held between 0 and its value. False when the entity has no such resource.</summary>
		public bool SetCurrent(StatId id, float current)
		{
			StatPool p = Check();
			int at = p.Find(slot, id);
			if (at < 0 || !p.Entries[at].IsResource)
			{
				return false;
			}
			p.Entries[at].Current = Clamp(current, 0f, p.Entries[at].Value);
			return true;
		}

		/// <summary>Adds to (or, negative, takes from) a resource, held between 0 and its value; returns the change actually made.</summary>
		public float ChangeCurrent(StatId id, float amount)
		{
			StatPool p = Check();
			int at = p.Find(slot, id);
			if (at < 0 || !p.Entries[at].IsResource)
			{
				return 0f;
			}
			ref StatEntry entry = ref p.Entries[at];
			float before = entry.Current;
			entry.Current = Clamp(before + amount, 0f, entry.Value);
			return entry.Current - before;
		}

		/// <summary>The position of stat <paramref name="id"/> (0 to <see cref="Count"/> - 1), or -1.</summary>
		public int IndexOf(StatId id)
		{
			StatPool p = Check();
			int at = p.Find(slot, id);
			return at >= 0 ? at - p.Starts[slot] : -1;
		}

		internal static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;

		private StatPool Check()
		{
			if (pool == null || !pool.IsCurrent(slot, generation))
			{
				throw new InvalidOperationException("These stats no longer exist: their entity was destroyed or its stats removed.");
			}
			return pool;
		}
	}

	/// <summary>Stats on a <see cref="World"/>.</summary>
	public static class StatWorld
	{
		/// <summary>
		/// Gives <paramref name="entity"/> an empty stats table with room for <paramref name="capacity"/> stats
		/// (more can be defined; the slice moves), or returns the one it has. At once: not while a system walks
		/// the stats store.
		/// </summary>
		public static StatTable AddStats(this World world, Entity entity, int capacity = 8)
		{
			StatPool pool = StatPool.Of(world);
			ComponentStore<Stats> store = world.Store<Stats>();
			if (store.TryGetIndex(entity, out int index) && pool.IsCurrent(store.At(index)))
			{
				return pool.Table(store.At(index));
			}
			if (!world.IsAlive(entity))
			{
				throw new InvalidOperationException($"{entity} is not alive.");
			}
			Stats stats = pool.Allocate(capacity);
			store.Set(entity, stats);
			return pool.Table(stats);
		}

		/// <summary><paramref name="entity"/>'s stats. Throws when it has none.</summary>
		public static StatTable StatsOf(this World world, Entity entity) =>
			StatPool.Of(world).Table(world.Store<Stats>().Get(entity));

		/// <summary><paramref name="entity"/>'s stats, or false when it has none.</summary>
		public static bool TryStatsOf(this World world, Entity entity, out StatTable table)
		{
			StatPool pool = StatPool.Of(world);
			ComponentStore<Stats> store = world.Store<Stats>();
			if (store.TryGetIndex(entity, out int index) && pool.IsCurrent(store.At(index)))
			{
				table = pool.Table(store.At(index));
				return true;
			}
			table = default;
			return false;
		}
	}
}
