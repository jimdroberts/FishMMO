using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace FishMMO.Core
{
	/// <summary>
	/// How long each system took: the last tick and a smoothed average, in microseconds. Hand it to a
	/// scheduler as its <see cref="SystemScheduler.Profiler"/>; set <see cref="Inner"/> to chain another (the
	/// engine's own profiler markers).
	/// </summary>
	public sealed class SystemTimings : ISystemProfiler
	{
		private readonly Dictionary<GameSystem, Timing> timings = new Dictionary<GameSystem, Timing>();
		private readonly Stopwatch clock = Stopwatch.StartNew();

		/// <summary>A profiler told after this one begins and before it ends.</summary>
		public ISystemProfiler Inner { get; set; }

		/// <summary>The last and smoothed time of <paramref name="system"/>, in microseconds; zero before it has run.</summary>
		public (double Last, double Average, long Runs) Of(GameSystem system) =>
			timings.TryGetValue(system, out Timing t) ? (t.Last, t.Average, t.Runs) : (0d, 0d, 0L);

		/// <inheritdoc />
		public void Begin(GameSystem system)
		{
			if (!timings.TryGetValue(system, out Timing t))
			{
				t = new Timing();
				timings[system] = t;
			}
			t.Started = clock.ElapsedTicks;
			Inner?.Begin(system);
		}

		/// <inheritdoc />
		public void End(GameSystem system)
		{
			Inner?.End(system);
			if (timings.TryGetValue(system, out Timing t))
			{
				double us = (clock.ElapsedTicks - t.Started) * 1_000_000d / Stopwatch.Frequency;
				t.Last = us;
				t.Average = t.Runs == 0 ? us : t.Average * 0.95 + us * 0.05;
				t.Runs++;
			}
		}

		private sealed class Timing
		{
			public long Started;
			public double Last;
			public double Average;
			public long Runs;
		}
	}

	/// <summary>
	/// The worlds running in this process, for tools such as the Entity Inspector. A host registers its world
	/// when it starts and removes it when it stops; nothing in the simulation reads this.
	/// </summary>
	public static class LiveWorlds
	{
		private static readonly object gate = new object();
		private static readonly List<LiveWorld> worlds = new List<LiveWorld>();

		/// <summary>Every running world, as a copy.</summary>
		public static List<LiveWorld> All
		{
			get
			{
				lock (gate)
				{
					return new List<LiveWorld>(worlds);
				}
			}
		}

		/// <summary>Lists a world under <paramref name="name"/>.</summary>
		public static LiveWorld Register(string name, World world, SystemScheduler scheduler, SystemTimings timings = null)
		{
			var live = new LiveWorld(name, world ?? throw new ArgumentNullException(nameof(world)), scheduler, timings);
			lock (gate)
			{
				worlds.Add(live);
			}
			return live;
		}

		/// <summary>Stops listing a world.</summary>
		public static void Unregister(LiveWorld live)
		{
			lock (gate)
			{
				worlds.Remove(live);
			}
		}
	}

	/// <summary>A world listed in <see cref="LiveWorlds"/>.</summary>
	public sealed class LiveWorld
	{
		internal LiveWorld(string name, World world, SystemScheduler scheduler, SystemTimings timings)
		{
			Name = name;
			World = world;
			Scheduler = scheduler;
			Timings = timings;
		}

		/// <summary>What the host calls it ("Server: NetworkManager").</summary>
		public string Name { get; }

		/// <summary>The world.</summary>
		public World World { get; }

		/// <summary>Its systems, or null.</summary>
		public SystemScheduler Scheduler { get; }

		/// <summary>Its system timings, or null.</summary>
		public SystemTimings Timings { get; }
	}
}
