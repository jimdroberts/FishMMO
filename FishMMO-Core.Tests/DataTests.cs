using System;
using NUnit.Framework;

namespace FishMMO.Core.Tests
{
	public class StatTests
	{
		private static readonly StatId Health = StatId.Of("test.health");
		private static readonly StatId Armour = StatId.Of("test.armour");
		private static readonly StatId Speed = StatId.Of("test.speed");

		private static TickContext Tick(double time = 0, float dt = 0.05f) => new TickContext(1, time, dt, true);

		private static (World, SystemScheduler, Entity) Setup()
		{
			var world = new World();
			var scheduler = new SystemScheduler(world);
			scheduler.Add(new StatSystem());
			scheduler.Add(new LifetimeSystem());
			scheduler.Add(new RegenerationSystem());
			scheduler.Add(new EffectSystem());
			Entity e = world.Create();
			StatTable stats = world.AddStats(e);
			stats.Define(Health, 100f, isResource: true);
			stats.Define(Armour, 10f);
			return (world, scheduler, e);
		}

		private static Entity Modify(World world, Entity target, params StatModifier[] modifiers)
		{
			Entity set = world.Create();
			world.Add(set, new ModifierSet { Target = target, Modifiers = modifiers });
			return set;
		}

		[Test]
		public void RegistryIsStableByName()
		{
			Assert.That(StatId.Of("test.health"), Is.EqualTo(Health));
			Assert.That(Health.ToString(), Is.EqualTo("test.health"));
			Assert.That(default(StatId).IsNone, Is.True);
			Assert.Throws<ArgumentException>(() => StatId.Of(""));
		}

		[Test]
		public void ValueIsBasePlusAddsTimesOnePlusMultiplies()
		{
			var (world, scheduler, e) = Setup();
			Modify(world, e, new StatModifier(Armour, ModifierOp.Add, 5f), new StatModifier(Armour, ModifierOp.Multiply, 0.5f));
			Modify(world, e, new StatModifier(Armour, ModifierOp.Add, 5f), new StatModifier(Armour, ModifierOp.Multiply, 0.5f));
			scheduler.Tick(Tick());
			Assert.That(world.StatsOf(e).Value(Armour), Is.EqualTo((10f + 10f) * 2f));
			Assert.That(world.StatsOf(e).Base(Armour), Is.EqualTo(10f));
		}

		[Test]
		public void DestroyingTheModifierEntityRemovesItsModifiers()
		{
			var (world, scheduler, e) = Setup();
			Entity buff = Modify(world, e, new StatModifier(Armour, ModifierOp.Add, 30f));
			scheduler.Tick(Tick());
			Assert.That(world.StatsOf(e).Value(Armour), Is.EqualTo(40f));
			world.Destroy(buff);
			scheduler.Tick(Tick());
			Assert.That(world.StatsOf(e).Value(Armour), Is.EqualTo(10f));
		}

		[Test]
		public void LosingMaximumHoldsTheResourceWithinIt()
		{
			var (world, scheduler, e) = Setup();
			Modify(world, e, new StatModifier(Health, ModifierOp.Multiply, -0.5f));
			scheduler.Tick(Tick());
			StatTable table = world.StatsOf(e);
			Assert.That(table.Value(Health), Is.EqualTo(50f));
			Assert.That(table.Current(Health), Is.EqualTo(50f));
		}

		[Test]
		public void ModifiersForMissingStatsOrDeadTargetsChangeNothing()
		{
			var (world, scheduler, e) = Setup();
			Modify(world, e, new StatModifier(Speed, ModifierOp.Add, 3f));
			Entity gone = world.Create();
			world.Destroy(gone);
			Modify(world, gone, new StatModifier(Armour, ModifierOp.Add, 3f));
			world.Add(world.Create(), new ModifierSet { Target = e, Modifiers = null });
			scheduler.Tick(Tick());
			StatTable table = world.StatsOf(e);
			Assert.That(table.Has(Speed), Is.False);
			Assert.That(table.Value(Armour), Is.EqualTo(10f));
		}

		[Test]
		public void ChangeCurrentHoldsBetweenZeroAndValueAndReportsTheRealChange()
		{
			var world = new World();
			StatTable table = world.AddStats(world.Create());
			table.Define(Health, 100f, isResource: true, current: 90f);
			Assert.That(table.ChangeCurrent(Health, 25f), Is.EqualTo(10f));
			Assert.That(table.ChangeCurrent(Health, -500f), Is.EqualTo(-100f));
			Assert.That(table.Current(Health), Is.Zero);
			Assert.That(table.ChangeCurrent(Armour, 5f), Is.Zero, "not a stat the table has");
			table.Define(Armour, 5f);
			Assert.That(table.SetCurrent(Armour, 1f), Is.False, "not a resource");
		}

		[Test]
		public void TableGrowsPastItsCapacity()
		{
			var world = new World();
			Entity e = world.Create();
			StatTable table = world.AddStats(e, 1);
			Entity neighbour = world.Create();
			world.AddStats(neighbour).Define(Armour, 7f);   // a slice right after, so growth must move
			for (int i = 0; i < 20; i++)
			{
				table.Define(StatId.Of("test.grow." + i), i);
			}
			Assert.That(table.Count, Is.EqualTo(20));
			Assert.That(table.Base(StatId.Of("test.grow.19")), Is.EqualTo(19f));
			Assert.That(table.Base(StatId.Of("test.grow.0")), Is.EqualTo(0f));
			Assert.That(world.StatsOf(e).Count, Is.EqualTo(20), "a view taken before the move still reads the moved slice");
			Assert.That(world.StatsOf(neighbour).Base(Armour), Is.EqualTo(7f), "moving one slice leaves its neighbour alone");
		}

		[Test]
		public void DestroyedEntitiesGiveTheirSlicesBackForReuse()
		{
			var world = new World();
			StatPool pool = StatPool.Of(world);
			Entity a = world.Create();
			world.AddStats(a).Define(Armour, 1f);
			int used = pool.UsedEntries;
			world.Destroy(a);
			Assert.That(pool.LiveSlices, Is.Zero);
			Entity b = world.Create();
			StatTable fresh = world.AddStats(b);
			Assert.That(pool.UsedEntries, Is.EqualTo(used), "the freed slice is reused, not a new one cut");
			Assert.That(fresh.Count, Is.Zero, "a reused slice starts empty");
			Assert.That(fresh.Has(Armour), Is.False);
		}

		[Test]
		public void ViewsOfGoneStatsThrowInsteadOfReadingTheirSuccessor()
		{
			var world = new World();
			Entity a = world.Create();
			StatTable old = world.AddStats(a);
			old.Define(Armour, 1f);
			world.Remove<Stats>(a);
			Entity b = world.Create();
			world.AddStats(b).Define(Armour, 99f);
			Assert.That(old.IsValid, Is.False);
			Assert.Throws<InvalidOperationException>(() => old.Value(Armour));
			Assert.That(default(StatTable).IsValid, Is.False);
			Assert.That(world.TryStatsOf(a, out _), Is.False);
		}

		[Test]
		public void AddStatsTwiceReturnsTheSameTable()
		{
			var world = new World();
			Entity e = world.Create();
			world.AddStats(e).Define(Armour, 3f);
			Assert.That(world.AddStats(e).Value(Armour), Is.EqualTo(3f));
			Assert.That(StatPool.Of(world).LiveSlices, Is.EqualTo(1));
		}

		[Test]
		public void ManyEntitiesKeepTheirOwnStats()
		{
			var world = new World();
			var all = new Entity[2000];
			for (int i = 0; i < all.Length; i++)
			{
				all[i] = world.Create();
				StatTable t = world.AddStats(all[i], 2);
				for (int k = 0; k <= i % 7; k++)
				{
					t.Define(StatId.Of("test.many." + k), i * 10 + k);
				}
				if (i % 3 == 0 && i > 0)
				{
					world.Destroy(all[i - 1]);   // churn: free slices while others grow
				}
			}
			for (int i = 0; i < all.Length; i++)
			{
				if (!world.IsAlive(all[i]))
				{
					continue;
				}
				StatTable t = world.StatsOf(all[i]);
				Assert.That(t.Count, Is.EqualTo(i % 7 + 1));
				Assert.That(t.Base(StatId.Of("test.many." + (i % 7))), Is.EqualTo(i * 10 + i % 7));
			}
		}
	}

	public class LifetimeTests
	{
		[Test]
		public void EndsAtItsTimeRaisingExpiredOnce()
		{
			var world = new World();
			var scheduler = new SystemScheduler(world);
			scheduler.Add(new LifetimeSystem());
			int expiredSeen = 0;
			scheduler.Add(new Watcher(w => expiredSeen += w.Events<Expired>().Count));
			Entity e = world.Create();
			world.Add(e, new Lifetime { EndsAt = 1.0 });

			scheduler.Tick(new TickContext(1, 0.95, 0.05f, true));
			Assert.That(world.IsAlive(e), Is.True);
			scheduler.Tick(new TickContext(2, 1.0, 0.05f, true));
			Assert.That(world.IsAlive(e), Is.False);
			scheduler.Tick(new TickContext(3, 1.05, 0.05f, true));
			Assert.That(expiredSeen, Is.EqualTo(1));
		}

		[Test]
		public void ExpiredEntityStillReadableInTheDeathPhase()
		{
			var world = new World();
			var scheduler = new SystemScheduler(world);
			scheduler.Add(new LifetimeSystem());
			float read = -1f;
			scheduler.Add(new Watcher(w =>
			{
				foreach (Expired ended in w.Events<Expired>().Current)
				{
					read = w.Get<Position>(ended.Entity).X;
				}
			}));
			Entity e = world.Create();
			world.Add(e, new Lifetime { EndsAt = 0 });
			world.Add(e, new Position { X = 9 });
			scheduler.Tick(new TickContext(1, 0, 0.05f, true));
			Assert.That(read, Is.EqualTo(9f));
			Assert.That(world.IsAlive(e), Is.False);
		}

		private sealed class Watcher : GameSystem
		{
			private readonly Action<World> body;
			public Watcher(Action<World> body) => this.body = body;
			public override SystemPhase Phase => SystemPhase.Death;
			public override int Order => 100;
			public override void Update(World world, in TickContext tick) => body(world);
		}
	}

	public class RegenerationTests
	{
		private static readonly StatId Mana = StatId.Of("test.mana");
		private static readonly StatId ManaRegen = StatId.Of("test.mana-regen");

		private static (World, SystemScheduler, Entity) Setup(float current)
		{
			var world = new World();
			var scheduler = new SystemScheduler(world);
			scheduler.Add(new RegenerationSystem());
			scheduler.Add(new StatSystem());
			Entity e = world.Create();
			StatTable stats = world.AddStats(e);
			stats.Define(Mana, 100f, isResource: true, current: current);
			stats.Define(ManaRegen, 10f);
			world.Add(e, new Regeneration { Rates = new[] { new RegenerationRate(Mana, ManaRegen) } });
			return (world, scheduler, e);
		}

		[Test]
		public void RefillsByRateTimesTickLength()
		{
			var (world, scheduler, e) = Setup(50f);
			for (uint t = 1; t <= 20; t++)
			{
				scheduler.Tick(new TickContext(t, t * 0.05, 0.05f, true));
			}
			Assert.That(world.StatsOf(e).Current(Mana), Is.EqualTo(60f).Within(1e-3f));
		}

		[Test]
		public void StopsAtTheResourcesValue()
		{
			var (world, scheduler, e) = Setup(99f);
			scheduler.Tick(new TickContext(1, 1, 1f, true));
			Assert.That(world.StatsOf(e).Current(Mana), Is.EqualTo(100f));
		}

		[Test]
		public void RateIsAStatSoModifiersChangeIt()
		{
			var (world, scheduler, e) = Setup(0f);
			Entity buff = world.Create();
			world.Add(buff, new ModifierSet { Target = e, Modifiers = new[] { new StatModifier(ManaRegen, ModifierOp.Multiply, 1f) } });
			scheduler.Tick(new TickContext(1, 1, 1f, true));
			Assert.That(world.StatsOf(e).Current(Mana), Is.EqualTo(20f), "the stat pass runs before the refill in the same tick");
		}

		[Test]
		public void SkipsEntitiesWithoutStatsOrRates()
		{
			var world = new World();
			var scheduler = new SystemScheduler(world);
			scheduler.Add(new RegenerationSystem());
			world.Add(world.Create(), new Regeneration { Rates = new[] { new RegenerationRate(Mana, ManaRegen) } });
			world.Add(world.Create(), new Regeneration());
			Assert.DoesNotThrow(() => scheduler.Tick(new TickContext(1, 1, 1f, true)));
		}
	}

	public class TagTests
	{
		[Test]
		public void TagsAreCountedSoOneSourceEndingKeepsAnothersHold()
		{
			TagId stunned = TagId.Of("test.stunned");
			var table = new TagTable();
			table.Add(stunned);
			table.Add(stunned);
			Assert.That(table.CountOf(stunned), Is.EqualTo(2));
			table.Remove(stunned);
			Assert.That(table.Has(stunned), Is.True);
			table.Remove(stunned);
			Assert.That(table.Has(stunned), Is.False);
			table.Remove(stunned);
			Assert.That(table.CountOf(stunned), Is.Zero);
		}

		[Test]
		public void RegistryIsStableByName()
		{
			Assert.That(TagId.Of("test.swimming"), Is.EqualTo(TagId.Of("test.swimming")));
			Assert.That(TagId.Of("test.swimming"), Is.Not.EqualTo(TagId.Of("test.flying")));
			Assert.That(TagId.Of("test.swimming").ToString(), Is.EqualTo("test.swimming"));
		}
	}

	public class EffectTests
	{
		private static readonly StatId Health = StatId.Of("test.health");
		private static readonly StatId Armour = StatId.Of("test.armour");

		private static (World, SystemScheduler, Entity) Setup()
		{
			var world = new World();
			var scheduler = new SystemScheduler(world);
			scheduler.Add(new EffectSystem());
			scheduler.Add(new StatSystem());
			scheduler.Add(new LifetimeSystem());
			Entity e = world.Create();
			StatTable stats = world.AddStats(e);
			stats.Define(Health, 100f, isResource: true);
			stats.Define(Armour, 10f);
			return (world, scheduler, e);
		}

		private static TickContext At(double time) => new TickContext((uint)(time * 20), time, 0.05f, true);

		[Test]
		public void ChangeResourceAppliesAndRaisesTheRealChange()
		{
			var (world, scheduler, e) = Setup();
			Entity attacker = world.Create();
			world.Resource<EffectQueue>().Enqueue(new Effect { Kind = CoreEffects.ChangeResource, Source = attacker, Target = e, Stat = Health, Amount = -130f });
			ResourceChanged seen = default;
			scheduler.Add(new Reader(w => { if (w.Events<ResourceChanged>().Count == 1) seen = w.Events<ResourceChanged>().Current[0]; }));
			scheduler.Tick(At(0));
			Assert.That(world.StatsOf(e).Current(Health), Is.Zero);
			Assert.That(seen.Change, Is.EqualTo(-100f));
			Assert.That(seen.Source, Is.EqualTo(attacker));
			Assert.That(world.Resource<EffectQueue>().Count, Is.Zero);
		}

		[Test]
		public void ApplyModifiersLastsForItsDuration()
		{
			var (world, scheduler, e) = Setup();
			world.Resource<EffectQueue>().Enqueue(new Effect
			{
				Kind = CoreEffects.ApplyModifiers,
				Target = e,
				Duration = 2.0,
				Modifiers = new[] { new StatModifier(Armour, ModifierOp.Add, 15f) },
			});
			scheduler.Tick(At(0));
			Assert.That(world.StatsOf(e).Value(Armour), Is.EqualTo(25f), "applied in the Effects phase, counted in the Stats phase of the same tick");
			scheduler.Tick(At(1.95));
			Assert.That(world.StatsOf(e).Value(Armour), Is.EqualTo(25f));
			scheduler.Tick(At(2.0));   // the set expires in this tick's Death phase
			scheduler.Tick(At(2.05));
			Assert.That(world.StatsOf(e).Value(Armour), Is.EqualTo(10f));
			Assert.That(world.Store<ModifierSet>().Count, Is.Zero);
		}

		[Test]
		public void UnknownKindsAreCountedAndDropped()
		{
			var (world, scheduler, e) = Setup();
			EffectQueue queue = world.Resource<EffectQueue>();
			queue.Enqueue(new Effect { Kind = EffectKind.Of("test.not-installed"), Target = e });
			queue.Enqueue(new Effect { Kind = default, Target = e });
			Assert.DoesNotThrow(() => scheduler.Tick(At(0)));
			Assert.That(queue.Unhandled, Is.EqualTo(2));
			Assert.That(queue.Count, Is.Zero);
		}

		[Test]
		public void HandlersMayQueueMoreEffectsThatRunInTheSameTick()
		{
			var (world, scheduler, e) = Setup();
			EffectKind lifesteal = EffectKind.Of("test.lifesteal");
			EffectQueue queue = world.Resource<EffectQueue>();
			queue.Register(lifesteal, (World w, in Effect fx, in TickContext tick) =>
			{
				w.Resource<EffectQueue>().Enqueue(new Effect { Kind = CoreEffects.ChangeResource, Target = fx.Target, Stat = Health, Amount = -fx.Amount });
				w.Resource<EffectQueue>().Enqueue(new Effect { Kind = CoreEffects.ChangeResource, Target = fx.Source, Stat = Health, Amount = fx.Amount });
			});
			Entity vampire = world.Create();
			StatTable stats = world.AddStats(vampire);
			stats.Define(Health, 100f, isResource: true, current: 50f);
			queue.Enqueue(new Effect { Kind = lifesteal, Source = vampire, Target = e, Amount = 20f });
			scheduler.Tick(At(0));
			Assert.That(world.StatsOf(e).Current(Health), Is.EqualTo(80f));
			Assert.That(world.StatsOf(vampire).Current(Health), Is.EqualTo(70f));
		}

		[Test]
		public void EffectsThatTriggerEachOtherForeverAreCutOff()
		{
			var (world, scheduler, e) = Setup();
			EffectKind echo = EffectKind.Of("test.echo");
			EffectQueue queue = world.Resource<EffectQueue>();
			queue.Register(echo, (World w, in Effect fx, in TickContext tick) => w.Resource<EffectQueue>().Enqueue(fx));
			scheduler.Get<EffectSystem>().MaxPerTick = 1000;
			queue.Enqueue(new Effect { Kind = echo, Target = e });
			Assert.DoesNotThrow(() => scheduler.Tick(At(0)));
			Assert.That(queue.Overflowed, Is.EqualTo(1));
			Assert.That(queue.Count, Is.Zero);
		}

		[Test]
		public void EffectsOnDeadTargetsDoNothing()
		{
			var (world, scheduler, e) = Setup();
			world.Destroy(e);
			EffectQueue queue = world.Resource<EffectQueue>();
			queue.Enqueue(new Effect { Kind = CoreEffects.ChangeResource, Target = e, Stat = Health, Amount = -5f });
			queue.Enqueue(new Effect { Kind = CoreEffects.ApplyModifiers, Target = e, Modifiers = new[] { new StatModifier(Armour, ModifierOp.Add, 1f) } });
			scheduler.Tick(At(0));
			Assert.That(world.Store<ModifierSet>().Count, Is.Zero);
			Assert.That(world.EntityCount, Is.Zero);
		}

		private sealed class Reader : GameSystem
		{
			private readonly Action<World> body;
			public Reader(Action<World> body) => this.body = body;
			public override SystemPhase Phase => SystemPhase.Effects;
			public override int Order => 100;
			public override void Update(World world, in TickContext tick) => body(world);
		}
	}
}
