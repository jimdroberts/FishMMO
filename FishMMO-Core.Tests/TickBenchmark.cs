using System;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;

namespace FishMMO.Core.Tests
{
	/// <summary>
	/// Whether ticking many objects through one system beats ticking each object itself. The same work (refill
	/// three resources by a rate stat) three ways:
	/// <list type="bullet">
	/// <item>Per object, best case: one small class per object, called through an interface, fields inline.</item>
	/// <item>Per object, as today: resources and rates are objects of their own, found through dictionaries
	/// keyed by template id, like the attribute controller's.</item>
	/// <item>Core: <see cref="RegenerationSystem"/> walking the packed stores.</item>
	/// </list>
	/// Objects are allocated between throwaway allocations so they sit apart on the heap, as long-lived game
	/// objects do. This measures the shape of the loop, not the full regeneration rules.
	/// <para>Run: <c>dotnet test FishMMO-Core.Tests -c Release -- "NUnit.Where=cat == Benchmark"</c></para>
	/// </summary>
	[Explicit("Benchmark: run with -c Release and the Benchmark category.")]
	[Category("Benchmark")]
	public class TickBenchmark
	{
		private const float Dt = 0.05f;
		private const int Ticks = 200;
		private const int Rounds = 7;

		private interface ITickable
		{
			void Tick(float dt);
		}

		private sealed class FlatRegen : ITickable
		{
			public float Health = 50f, HealthMax = 1e9f, HealthRate = 1f;
			public float Mana = 50f, ManaMax = 1e9f, ManaRate = 1f;
			public float Stamina = 50f, StaminaMax = 1e9f, StaminaRate = 1f;

			public void Tick(float dt)
			{
				if (Health < HealthMax) Health = Math.Min(Health + HealthRate * dt, HealthMax);
				if (Mana < ManaMax) Mana = Math.Min(Mana + ManaRate * dt, ManaMax);
				if (Stamina < StaminaMax) Stamina = Math.Min(Stamina + StaminaRate * dt, StaminaMax);
			}
		}

		private sealed class Attribute
		{
			public float FinalValue;
		}

		private sealed class ResourceAttribute
		{
			public float Current, Max;

			public void Gain(float amount) => Current = Math.Min(Current + amount, Max);
		}

		private sealed class DictionaryRegen : ITickable
		{
			private static readonly int[] ResourceIds = { 1, 2, 3 };
			private static readonly int[] RateIds = { 11, 12, 13 };
			private readonly Dictionary<int, ResourceAttribute> resources = new Dictionary<int, ResourceAttribute>();
			private readonly Dictionary<int, Attribute> attributes = new Dictionary<int, Attribute>();
			private readonly Dictionary<int, float> lastObserved = new Dictionary<int, float>();

			public DictionaryRegen()
			{
				for (int i = 0; i < 3; i++)
				{
					resources[ResourceIds[i]] = new ResourceAttribute { Current = 50f, Max = 1e9f };
					attributes[RateIds[i]] = new Attribute { FinalValue = 1f };
					lastObserved[ResourceIds[i]] = 50f;
				}
			}

			public void Tick(float dt)
			{
				for (int i = 0; i < 3; i++)
				{
					if (resources.TryGetValue(ResourceIds[i], out ResourceAttribute resource) &&
						attributes.TryGetValue(RateIds[i], out Attribute rate) &&
						resource.Current < resource.Max)
					{
						resource.Gain(rate.FinalValue * dt);
						lastObserved[ResourceIds[i]] = resource.Current;
					}
				}
			}
		}

		private static readonly StatId[] Resources = { StatId.Of("bench.health"), StatId.Of("bench.mana"), StatId.Of("bench.stamina") };
		private static readonly StatId[] Rates = { StatId.Of("bench.health-regen"), StatId.Of("bench.mana-regen"), StatId.Of("bench.stamina-regen") };

		[TestCase(1_000)]
		[TestCase(10_000)]
		[TestCase(100_000)]
		public void RegenerationPerObjectAgainstSystem(int count)
		{
			var junk = new List<object>();
			var rng = new Random(1);

			var flat = new List<ITickable>(count);
			var dict = new List<ITickable>(count);
			var world = new World();
			var rates = new[]
			{
				new RegenerationRate(Resources[0], Rates[0]),
				new RegenerationRate(Resources[1], Rates[1]),
				new RegenerationRate(Resources[2], Rates[2]),
			};
			for (int i = 0; i < count; i++)
			{
				flat.Add(new FlatRegen());
				junk.Add(new byte[rng.Next(32, 512)]);
				dict.Add(new DictionaryRegen());
				junk.Add(new byte[rng.Next(32, 512)]);

				Entity e = world.Create();
				StatTable stats = world.AddStats(e, 6);
				for (int r = 0; r < 3; r++)
				{
					stats.Define(Resources[r], 1e9f, isResource: true, current: 50f);
					stats.Define(Rates[r], 1f);
				}
				world.Add(e, new Regeneration { Rates = rates });
				junk.Add(new byte[rng.Next(32, 512)]);
			}
			// Shuffle the per-object lists the way spawn and despawn order scatters a real scene's list.
			Shuffle(flat, rng);
			Shuffle(dict, rng);
			var system = new RegenerationSystem();

			double flatMs = Measure(() => { for (int i = 0; i < flat.Count; i++) flat[i].Tick(Dt); });
			double dictMs = Measure(() => { for (int i = 0; i < dict.Count; i++) dict[i].Tick(Dt); });
			double systemMs = Measure(() => system.Update(world, new TickContext(1, 0, Dt, true)));

			TestContext.Progress.WriteLine(
				$"[bench] regen x{count,7}: per-object flat {flatMs * 1000,8:F1} us/tick | per-object dictionaries {dictMs * 1000,8:F1} us/tick | Core system {systemMs * 1000,8:F1} us/tick | " +
				$"system vs dictionaries {dictMs / systemMs:F1}x, vs flat {flatMs / systemMs:F1}x");
			GC.KeepAlive(junk);
		}

		[TestCase(10_000)]
		public void StatRecalculationWithModifiers(int count)
		{
			var world = new World();
			StatId armour = StatId.Of("bench.armour");
			StatId speed = StatId.Of("bench.speed");
			var modifiers = new[] { new StatModifier(armour, ModifierOp.Add, 5f), new StatModifier(speed, ModifierOp.Multiply, 0.1f) };
			for (int i = 0; i < count; i++)
			{
				Entity e = world.Create();
				StatTable stats = world.AddStats(e, 8);
				for (int r = 0; r < 3; r++)
				{
					stats.Define(Resources[r], 100f, isResource: true);
					stats.Define(Rates[r], 1f);
				}
				stats.Define(armour, 10f);
				stats.Define(speed, 5f);
				for (int b = 0; b < 3; b++)
				{
					Entity buff = world.Create();
					world.Add(buff, new ModifierSet { Target = e, Modifiers = modifiers });
				}
			}
			var system = new StatSystem();
			double ms = Measure(() => system.Update(world, new TickContext(1, 0, Dt, true)));
			TestContext.Progress.WriteLine($"[bench] full stat recalculation: {count} entities x 8 stats, {count * 3} modifier sets: {ms * 1000:F1} us/tick");
		}

		/// <summary>An NPC as corpse decay sees it, with the per-NPC tick handler NPC.CorpseDecayTick has.</summary>
		private sealed class FakeNpc
		{
			public bool IsCorpse;
			public bool HasLoot = true;
			public bool Shortened;
			public float Timer;

			public void CorpseDecayTick()
			{
				if (!IsCorpse) return;
				if (!Shortened && !HasLoot)
				{
					Shortened = true;
					if (Timer > 5f) Timer = 5f;
				}
				Timer -= Dt;
				if (Timer <= 0f) Timer = 1e9f;   // stands in for ReturnToPool; keeps the count steady
			}
		}

		private struct FakeCorpse
		{
			public FakeNpc Npc;
			public bool Shortened;
		}

		private sealed class FakeCorpseSystem : GameSystem
		{
			public override SystemPhase Phase => SystemPhase.Death;
			public override int Order => -100;

			public override void Update(World world, in TickContext tick)
			{
				ComponentStore<FakeCorpse> corpses = world.Store<FakeCorpse>();
				ComponentStore<Lifetime> lifetimes = world.Store<Lifetime>();
				for (int i = 0; i < corpses.Count; i++)
				{
					ref FakeCorpse corpse = ref corpses.At(i);
					if (corpse.Shortened || corpse.Npc.HasLoot) continue;
					corpse.Shortened = true;
					if (lifetimes.TryGetIndex(corpses.EntityAt(i), out int at) && lifetimes.At(at).EndsAt > tick.Time + 5)
					{
						lifetimes.At(at).EndsAt = tick.Time + 5;
					}
				}
			}
		}

		/// <summary>
		/// Corpse decay three ways, on an <c>event Action</c> like FishNet's TimeManager.OnTick: every NPC
		/// subscribed at spawn (before), only corpses subscribed (the cheapest per-object fix), and corpses as
		/// Core entities ticked by one system. Also what one spawn plus one despawn costs the event.
		/// </summary>
		[TestCase(500, 0.02)]
		[TestCase(2_000, 0.02)]
		[TestCase(10_000, 0.02)]
		public void CorpseDecayPerNpcAgainstSystem(int npcs, double corpseShare)
		{
			int corpseCount = Math.Max(1, (int)(npcs * corpseShare));
			var rng = new Random(2);
			var all = new FakeNpc[npcs];
			for (int i = 0; i < npcs; i++)
			{
				all[i] = new FakeNpc { IsCorpse = i < corpseCount, Timer = 1e9f, HasLoot = rng.NextDouble() < 0.5 };
			}

			Action everyNpc = null;
			for (int i = 0; i < npcs; i++) everyNpc += all[i].CorpseDecayTick;
			Action corpsesOnly = null;
			for (int i = 0; i < corpseCount; i++) corpsesOnly += all[i].CorpseDecayTick;

			var world = new World();
			var scheduler = new SystemScheduler(world);
			scheduler.Add(new FakeCorpseSystem());
			scheduler.Add(new LifetimeSystem());
			for (int i = 0; i < corpseCount; i++)
			{
				Entity e = world.Create();
				world.Add(e, new Lifetime { EndsAt = 1e12 });
				world.Add(e, new FakeCorpse { Npc = all[i] });
			}
			uint t = 0;

			double everyMs = Measure(() => everyNpc());
			double corpsesMs = Measure(() => corpsesOnly());
			double coreMs = Measure(() => { t++; scheduler.Tick(new TickContext(t, t * Dt, Dt, true)); });

			// One NPC spawning and despawning against a busy event: Delegate.Combine/Remove copy the list.
			var newcomer = new FakeNpc();
			long before = GC.GetAllocatedBytesForCurrentThread();
			var watch = Stopwatch.StartNew();
			const int churn = 2_000;
			for (int i = 0; i < churn; i++)
			{
				everyNpc += newcomer.CorpseDecayTick;
				everyNpc -= newcomer.CorpseDecayTick;
			}
			watch.Stop();
			double churnUs = watch.Elapsed.TotalMilliseconds * 1000 / churn;
			long churnBytes = (GC.GetAllocatedBytesForCurrentThread() - before) / churn;

			TestContext.Progress.WriteLine(
				$"[bench] corpse decay, {npcs,6} NPCs, {corpseCount,4} corpses: every NPC on OnTick {everyMs * 1000,7:F2} us/tick | corpses only {corpsesMs * 1000,6:F2} | Core systems {coreMs * 1000,6:F2} | " +
				$"spawn+despawn on the busy event {churnUs,6:F2} us, {churnBytes,7} B garbage");
		}

		private static double Measure(Action tick)
		{
			for (int i = 0; i < Ticks; i++)
			{
				tick();   // warm up: JIT, caches
			}
			var results = new double[Rounds];
			var watch = new Stopwatch();
			for (int round = 0; round < Rounds; round++)
			{
				watch.Restart();
				for (int i = 0; i < Ticks; i++)
				{
					tick();
				}
				watch.Stop();
				results[round] = watch.Elapsed.TotalMilliseconds / Ticks;
			}
			Array.Sort(results);
			return results[Rounds / 2];
		}

		private static void Shuffle<T>(List<T> list, Random rng)
		{
			for (int i = list.Count - 1; i > 0; i--)
			{
				int j = rng.Next(i + 1);
				(list[i], list[j]) = (list[j], list[i]);
			}
		}
	}
}
