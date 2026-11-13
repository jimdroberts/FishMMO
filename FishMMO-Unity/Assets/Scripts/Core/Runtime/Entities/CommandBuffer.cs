using System;
using System.Collections.Generic;

namespace FishMMO.Core
{
	/// <summary>
	/// Structural changes asked for while systems walk the stores, applied together at the end of the phase
	/// (<see cref="SystemScheduler"/>): adding and removing components, destroying entities.
	/// </summary>
	/// <remarks>
	/// Playback applies every addition, then every removal, then every destruction, each in the order asked.
	/// So an entity given a component and destroyed in the same phase ends up destroyed, and a component added
	/// and removed in the same phase ends up removed. Changes to entities that died before playback are skipped.
	/// </remarks>
	public sealed class CommandBuffer
	{
		private readonly World world;
		private IPending[] adds = new IPending[16];
		private IPending[] removes = new IPending[16];
		private readonly List<IPending> dirtyAdds = new List<IPending>();
		private readonly List<IPending> dirtyRemoves = new List<IPending>();
		private readonly List<Entity> destroys = new List<Entity>();

		internal CommandBuffer(World world)
		{
			this.world = world;
		}

		/// <summary>Whether anything is waiting to be applied.</summary>
		public bool HasPending => dirtyAdds.Count > 0 || dirtyRemoves.Count > 0 || destroys.Count > 0;

		/// <summary>Gives <paramref name="entity"/> a component at the end of the phase.</summary>
		public void Add<T>(Entity entity, in T component) where T : struct
		{
			PendingAdds<T> pending = Slot<T, PendingAdds<T>>(ref adds, dirtyAdds);
			pending.Items.Add((entity, component));
		}

		/// <summary>Removes <paramref name="entity"/>'s <typeparamref name="T"/> at the end of the phase.</summary>
		public void Remove<T>(Entity entity) where T : struct
		{
			PendingRemoves<T> pending = Slot<T, PendingRemoves<T>>(ref removes, dirtyRemoves);
			pending.Items.Add(entity);
		}

		/// <summary>Destroys <paramref name="entity"/> at the end of the phase.</summary>
		public void Destroy(Entity entity)
		{
			destroys.Add(entity);
		}

		/// <summary>Applies everything asked for since the last playback.</summary>
		public void Playback()
		{
			for (int i = 0; i < dirtyAdds.Count; i++)
			{
				dirtyAdds[i].Apply(world);
			}
			dirtyAdds.Clear();
			for (int i = 0; i < dirtyRemoves.Count; i++)
			{
				dirtyRemoves[i].Apply(world);
			}
			dirtyRemoves.Clear();
			for (int i = 0; i < destroys.Count; i++)
			{
				world.Destroy(destroys[i]);
			}
			destroys.Clear();
		}

		private static TPending Slot<T, TPending>(ref IPending[] table, List<IPending> dirty)
			where T : struct
			where TPending : class, IPending, new()
		{
			int id = ComponentType<T>.Id;
			if (id >= table.Length)
			{
				Array.Resize(ref table, Math.Max(table.Length * 2, id + 1));
			}
			var pending = (TPending)table[id];
			if (pending == null)
			{
				pending = new TPending();
				table[id] = pending;
			}
			if (!pending.Queued)
			{
				pending.Queued = true;
				dirty.Add(pending);
			}
			return pending;
		}

		private interface IPending
		{
			bool Queued { get; set; }
			void Apply(World world);
		}

		private sealed class PendingAdds<T> : IPending where T : struct
		{
			public readonly List<(Entity Entity, T Component)> Items = new List<(Entity, T)>();
			public bool Queued { get; set; }

			public void Apply(World world)
			{
				ComponentStore<T> store = world.Store<T>();
				for (int i = 0; i < Items.Count; i++)
				{
					if (world.IsAlive(Items[i].Entity))
					{
						store.Set(Items[i].Entity, Items[i].Component);
					}
				}
				Items.Clear();
				Queued = false;
			}
		}

		private sealed class PendingRemoves<T> : IPending where T : struct
		{
			public readonly List<Entity> Items = new List<Entity>();
			public bool Queued { get; set; }

			public void Apply(World world)
			{
				ComponentStore<T> store = world.Store<T>();
				for (int i = 0; i < Items.Count; i++)
				{
					store.Remove(Items[i]);
				}
				Items.Clear();
				Queued = false;
			}
		}
	}
}
