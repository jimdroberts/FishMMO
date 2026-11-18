using System;
using System.Collections.Generic;

namespace FishMMO.Core
{
	/// <summary>
	/// Runs a world's systems once per tick, phase by phase, applying each phase's queued structural changes when
	/// it ends and clearing the tick's events at the end.
	/// </summary>
	public sealed class SystemScheduler
	{
		private readonly List<GameSystem> systems = new List<GameSystem>();
		private bool sorted = true;

		/// <summary>Creates a scheduler for <paramref name="world"/>.</summary>
		public SystemScheduler(World world)
		{
			World = world ?? throw new ArgumentNullException(nameof(world));
		}

		/// <summary>The world this scheduler ticks.</summary>
		public World World { get; }

		/// <summary>Told when each system starts and ends; null for none.</summary>
		public ISystemProfiler Profiler { get; set; }

		/// <summary>Every system, in the order they run.</summary>
		public IReadOnlyList<GameSystem> Systems
		{
			get
			{
				EnsureSorted();
				return systems;
			}
		}

		/// <summary>Adds a system.</summary>
		public void Add(GameSystem system)
		{
			if (system == null)
			{
				throw new ArgumentNullException(nameof(system));
			}
			if (systems.Contains(system))
			{
				return;
			}
			systems.Add(system);
			sorted = false;
			system.OnAdded(World);
		}

		/// <summary>Removes a system.</summary>
		public bool Remove(GameSystem system) => systems.Remove(system);

		/// <summary>The first system of type <typeparamref name="T"/>, or null.</summary>
		public T Get<T>() where T : GameSystem
		{
			for (int i = 0; i < systems.Count; i++)
			{
				if (systems[i] is T match)
				{
					return match;
				}
			}
			return null;
		}

		/// <summary>The first system of type <typeparamref name="T"/>, added first when there is none.</summary>
		public T GetOrAdd<T>() where T : GameSystem, new()
		{
			T system = Get<T>();
			if (system == null)
			{
				system = new T();
				Add(system);
			}
			return system;
		}

		/// <summary>Runs one tick: every system in phase order, each phase's changes applied as it ends, then events cleared.</summary>
		public void Tick(in TickContext tick)
		{
			EnsureSorted();
			World world = World;
			ISystemProfiler profiler = Profiler;
			world.IsTicking = true;
			try
			{
				RunPhases(world, profiler, tick);
			}
			finally
			{
				world.IsTicking = false;
			}
			world.Commands.Playback();   // changes queued outside any system since the last tick
			world.ClearEvents();
		}

		private void RunPhases(World world, ISystemProfiler profiler, in TickContext tick)
		{
			int i = 0;
			while (i < systems.Count)
			{
				SystemPhase phase = systems[i].Phase;
				for (; i < systems.Count && systems[i].Phase == phase; i++)
				{
					GameSystem system = systems[i];
					profiler?.Begin(system);
					try
					{
						system.Update(world, tick);
					}
					finally
					{
						profiler?.End(system);
					}
				}
				world.Commands.Playback();
			}
		}

		private void EnsureSorted()
		{
			if (sorted)
			{
				return;
			}
			// Stable: systems with the same phase and order keep the order they were added in.
			var indexed = new List<(GameSystem System, int Added)>(systems.Count);
			for (int k = 0; k < systems.Count; k++)
			{
				indexed.Add((systems[k], k));
			}
			indexed.Sort((a, b) =>
			{
				int cmp = a.System.Phase.CompareTo(b.System.Phase);
				if (cmp != 0) return cmp;
				cmp = a.System.Order.CompareTo(b.System.Order);
				return cmp != 0 ? cmp : a.Added.CompareTo(b.Added);
			});
			for (int k = 0; k < indexed.Count; k++)
			{
				systems[k] = indexed[k].System;
			}
			sorted = true;
		}
	}
}
