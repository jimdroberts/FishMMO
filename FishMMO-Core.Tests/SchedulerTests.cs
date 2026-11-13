using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace FishMMO.Core.Tests
{
	public readonly struct Ping
	{
		public readonly int Value;

		public Ping(int value)
		{
			Value = value;
		}
	}

	public class SchedulerTests
	{
		private sealed class Probe : GameSystem
		{
			private readonly SystemPhase phase;
			private readonly int order;
			private readonly List<string> log;
			private readonly Action<World> body;

			public Probe(string name, SystemPhase phase, int order, List<string> log, Action<World> body = null)
			{
				ProbeName = name;
				this.phase = phase;
				this.order = order;
				this.log = log;
				this.body = body;
			}

			public string ProbeName { get; }
			public override SystemPhase Phase => phase;
			public override int Order => order;
			public override string Name => ProbeName;

			public override void Update(World world, in TickContext tick)
			{
				log.Add(ProbeName);
				body?.Invoke(world);
			}
		}

		private static TickContext Tick(uint n = 1, double time = 0, float dt = 0.05f) => new TickContext(n, time, dt, true);

		[Test]
		public void RunsByPhaseThenOrderThenInsertion()
		{
			var log = new List<string>();
			var scheduler = new SystemScheduler(new World());
			scheduler.Add(new Probe("persist", SystemPhase.Persistence, 0, log));
			scheduler.Add(new Probe("stats-late", SystemPhase.Stats, 50, log));
			scheduler.Add(new Probe("input", SystemPhase.Input, 0, log));
			scheduler.Add(new Probe("stats-a", SystemPhase.Stats, 0, log));
			scheduler.Add(new Probe("stats-b", SystemPhase.Stats, 0, log));
			scheduler.Tick(Tick());
			Assert.That(log, Is.EqualTo(new[] { "input", "stats-a", "stats-b", "stats-late", "persist" }));
		}

		[Test]
		public void PhaseCommandsApplyBeforeTheNextPhaseButNotWithinOne()
		{
			var log = new List<string>();
			var world = new World();
			Entity e = world.Create();
			bool seenSamePhase = true, seenNextPhase = false;
			var scheduler = new SystemScheduler(world);
			scheduler.Add(new Probe("add", SystemPhase.Effects, 0, log, w => w.Commands.Add(e, new Position())));
			scheduler.Add(new Probe("same", SystemPhase.Effects, 10, log, w => seenSamePhase = w.Has<Position>(e)));
			scheduler.Add(new Probe("next", SystemPhase.Stats, 0, log, w => seenNextPhase = w.Has<Position>(e)));
			scheduler.Tick(Tick());
			Assert.That(seenSamePhase, Is.False);
			Assert.That(seenNextPhase, Is.True);
		}

		[Test]
		public void EventsAreSeenLaterInTheTickAndClearedAfter()
		{
			var log = new List<string>();
			var world = new World();
			int seen = -1;
			var scheduler = new SystemScheduler(world);
			scheduler.Add(new Probe("raise", SystemPhase.Abilities, 0, log, w => w.Events<Ping>().Raise(new Ping(42))));
			scheduler.Add(new Probe("read", SystemPhase.Objectives, 0, log, w =>
			{
				ReadOnlySpan<Ping> pings = w.Events<Ping>().Current;
				seen = pings.Length == 1 ? pings[0].Value : -1;
			}));
			scheduler.Tick(Tick());
			Assert.That(seen, Is.EqualTo(42));
			Assert.That(world.Events<Ping>().Count, Is.Zero);
		}

		[Test]
		public void CommandsQueuedOutsideSystemsApplyOnTheNextTick()
		{
			var world = new World();
			Entity e = world.Create();
			world.Commands.Add(e, new Position());
			new SystemScheduler(world).Tick(Tick());
			Assert.That(world.Has<Position>(e), Is.True);
		}

		[Test]
		public void AddingTwiceIsIgnoredAndRemoveStopsASystem()
		{
			var log = new List<string>();
			var scheduler = new SystemScheduler(new World());
			var probe = new Probe("p", SystemPhase.Input, 0, log);
			scheduler.Add(probe);
			scheduler.Add(probe);
			scheduler.Tick(Tick());
			Assert.That(log.Count, Is.EqualTo(1));
			Assert.That(scheduler.Get<Probe>(), Is.SameAs(probe));
			scheduler.Remove(probe);
			scheduler.Tick(Tick());
			Assert.That(log.Count, Is.EqualTo(1));
		}

		[Test]
		public void GetOrAddInstallsOnce()
		{
			var scheduler = new SystemScheduler(new World());
			LifetimeSystem first = scheduler.GetOrAdd<LifetimeSystem>();
			Assert.That(scheduler.GetOrAdd<LifetimeSystem>(), Is.SameAs(first));
			Assert.That(scheduler.Systems.Count, Is.EqualTo(1));
		}

		private sealed class CountingProfiler : ISystemProfiler
		{
			public int Begins, Ends;
			public void Begin(GameSystem system) => Begins++;
			public void End(GameSystem system) => Ends++;
		}

		[Test]
		public void ProfilerBracketsEverySystemEvenWhenOneThrows()
		{
			var log = new List<string>();
			var profiler = new CountingProfiler();
			var scheduler = new SystemScheduler(new World()) { Profiler = profiler };
			scheduler.Add(new Probe("boom", SystemPhase.Input, 0, log, w => throw new InvalidOperationException("boom")));
			Assert.Throws<InvalidOperationException>(() => scheduler.Tick(Tick()));
			Assert.That(profiler.Begins, Is.EqualTo(1));
			Assert.That(profiler.Ends, Is.EqualTo(1));
		}
	}
}
