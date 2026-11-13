using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;

[assembly: FishMMO.Core.GameModule(typeof(FishMMO.Core.Tests.DeclaredModule))]

namespace FishMMO.Core.Tests
{
	public struct Inventory
	{
		public int Slots;
	}

	public readonly struct Killed
	{
		public readonly Entity Victim;

		public Killed(Entity victim)
		{
			Victim = victim;
		}
	}

	public class ModuleTests
	{
		private sealed class TestModule : GameModule
		{
			private readonly string id;
			private readonly string[] dependencies;
			private readonly Action<ModuleBuilder> install;

			public TestModule(string id, Action<ModuleBuilder> install = null, params string[] dependencies)
			{
				this.id = id;
				this.install = install;
				this.dependencies = dependencies;
			}

			public override string Id => id;
			public override IReadOnlyList<string> Dependencies => dependencies;
			public override void Install(ModuleBuilder builder) => install?.Invoke(builder);
		}

		private static string[] Ids(ModuleCatalog catalog) => catalog.Modules.Select(m => m.Id).ToArray();

		[Test]
		public void CoreIsAlwaysFirstAndDependenciesComeBeforeDependents()
		{
			ModuleCatalog catalog = ModuleCatalog.Resolve(new GameModule[]
			{
				new TestModule("achievements", null, "items", "combat"),
				new TestModule("items"),
				new TestModule("combat", null, "items"),
			});
			Assert.That(catalog.Problems, Is.Empty);
			Assert.That(Ids(catalog), Is.EqualTo(new[] { "core", "items", "combat", "achievements" }));
		}

		[Test]
		public void AMissingDependencyLeavesOutTheModuleAndItsDependents()
		{
			ModuleCatalog catalog = ModuleCatalog.Resolve(new GameModule[]
			{
				new TestModule("trade", null, "items"),
				new TestModule("auction", null, "trade"),
				new TestModule("chat"),
			});
			Assert.That(Ids(catalog), Is.EqualTo(new[] { "core", "chat" }));
			Assert.That(catalog.Problems.Any(p => p.Contains("trade needs 'items'")), Is.True, string.Join("\n", catalog.Problems));
			Assert.That(catalog.Problems.Any(p => p.Contains("auction needs 'trade'")), Is.True, string.Join("\n", catalog.Problems));
		}

		[Test]
		public void CyclesAndDuplicateIdsAreReportedNotFatal()
		{
			ModuleCatalog catalog = ModuleCatalog.Resolve(new GameModule[]
			{
				new TestModule("a", null, "b"),
				new TestModule("b", null, "a"),
				new TestModule("c"),
				new DuplicateC(),
			});
			Assert.That(Ids(catalog), Is.EqualTo(new[] { "core", "c" }));
			Assert.That(catalog.Problems.Any(p => p.StartsWith("Dependency cycle")), Is.True);
			Assert.That(catalog.Problems.Any(p => p.Contains("id 'c' is already")), Is.True);
		}

		private sealed class DuplicateC : GameModule
		{
			public override string Id => "c";
			public override void Install(ModuleBuilder builder) { }
		}

		[Test]
		public void InstallAddsOnlyTheRequestedSidesSystems()
		{
			var world = new World();
			var scheduler = new SystemScheduler(world);
			ModuleCatalog catalog = ModuleCatalog.Resolve(new GameModule[]
			{
				new TestModule("ui", b => b.AddSystem(SimulationSide.Client, () => new Named("client-only"))),
				new TestModule("ai", b => b.AddSystem(SimulationSide.Server, () => new Named("server-only"))),
			});
			ModuleInstall install = catalog.Install(world, scheduler, SimulationSide.Server);
			string[] names = scheduler.Systems.Select(s => s.Name).ToArray();
			Assert.That(names, Does.Contain("server-only"));
			Assert.That(names, Does.Not.Contain("client-only"));
			Assert.That(names, Does.Contain(nameof(LifetimeSystem)), "Core's own systems come with every install");
			Assert.That(install.SystemsOf("ai"), Is.EqualTo(new[] { "server-only" }));
			Assert.That(world.TryGetResource(out ModuleInstall kept) && kept == install, Is.True);
		}

		[Test]
		public void AModuleThatThrowsIsReportedAndTheRestInstall()
		{
			var world = new World();
			var scheduler = new SystemScheduler(world);
			ModuleInstall install = ModuleCatalog.Resolve(new GameModule[]
			{
				new TestModule("broken", b => throw new InvalidOperationException("bad content")),
				new TestModule("fine", b => b.AddSystem(SimulationSide.Both, () => new Named("fine-system"))),
			}).Install(world, scheduler, SimulationSide.Server);
			Assert.That(install.Modules.Select(m => m.Id), Is.EqualTo(new[] { "core", "fine" }));
			Assert.That(install.Problems.Single(), Does.Contain("broken threw while installing"));
		}

		[Test]
		public void ArchetypesGetComponentsFromEveryInstalledModule()
		{
			var world = new World();
			var scheduler = new SystemScheduler(world);
			StatId health = StatId.Of("test.module-health");
			ModuleCatalog.Resolve(new GameModule[]
			{
				new TestModule("items", b => b.Archetype(Archetypes.Player).Add(new Inventory { Slots = 24 })),
				new TestModule("combat", b => b.Archetype(Archetypes.Player).Add("health 100", (w, e) => w.AddStats(e).Define(health, 100f, isResource: true))),
			}).Install(world, scheduler, SimulationSide.Server);

			Entity player = world.Spawn(Archetypes.Player);
			Assert.That(world.Get<Inventory>(player).Slots, Is.EqualTo(24));
			Assert.That(world.StatsOf(player).Current(health), Is.EqualTo(100f));

			Entity npc = world.Spawn(Archetypes.Npc);
			Assert.That(world.Has<Inventory>(npc), Is.False, "an NPC gets nothing the modules gave players");
			Assert.That(world.Resource<Archetypes>().PartsOf(Archetypes.Player).Select(p => p.Module), Is.EqualTo(new[] { "combat", "items" }),
				"modules with no dependency between them install in id order, the same on every machine");
			Assert.Throws<InvalidOperationException>(() => world.Spawn("no-such-kind"));
		}

		[Test]
		public void SpawningWhileTickingDefersComponentsToThePhaseEnd()
		{
			var world = new World();
			var scheduler = new SystemScheduler(world);
			ModuleCatalog.Resolve(new GameModule[]
			{
				new TestModule("items", b => b.Archetype(Archetypes.Player).Add(new Inventory { Slots = 8 })),
			}).Install(world, scheduler, SimulationSide.Server);
			Entity spawned = Entity.Null;
			bool hadAtOnce = true;
			scheduler.Add(new Lambda(SystemPhase.Input, w =>
			{
				spawned = w.Spawn(Archetypes.Player);
				hadAtOnce = w.Has<Inventory>(spawned);
			}));
			scheduler.Tick(new TickContext(1, 0, 0.05f, true));
			Assert.That(hadAtOnce, Is.False);
			Assert.That(world.Get<Inventory>(spawned).Slots, Is.EqualTo(8));
		}

		[Test]
		public void EventListenersFollowAnotherModulesFactsWithoutNamingIt()
		{
			var world = new World();
			var scheduler = new SystemScheduler(world);
			int kills = 0;
			ModuleCatalog.Resolve(new GameModule[]
			{
				new TestModule("achievements", b => b.OnEvent<Killed>(SimulationSide.Server, (w, e) => kills++)),
			}).Install(world, scheduler, SimulationSide.Server);

			scheduler.Add(new Lambda(SystemPhase.Death, w => w.Events<Killed>().Raise(new Killed(w.Create()))));
			scheduler.Tick(new TickContext(1, 0, 0.05f, true));
			Assert.That(kills, Is.EqualTo(1));

			// Raised from outside the simulation (an adapter) between ticks: seen in the next tick, once.
			world.Events<Killed>().Raise(new Killed(world.Create()));
			scheduler.Tick(new TickContext(2, 0.05, 0.05f, true));
			Assert.That(kills, Is.EqualTo(3));
		}

		[Test]
		public void PersistenceRoundTripsAndKeepsKeysNoModuleReads()
		{
			var world = new World();
			var scheduler = new SystemScheduler(world);
			ModuleCatalog.Resolve(new GameModule[]
			{
				new TestModule("items", b => b.Persist<Inventory>("items.inventory", 2,
					inv => inv.Slots.ToString(),
					(text, version) => new Inventory { Slots = int.Parse(text) * (version < 2 ? 2 : 1) })),
			}).Install(world, scheduler, SimulationSide.Server);
			PersistenceRegistry registry = world.Resource<PersistenceRegistry>();

			Entity a = world.Create();
			world.Add(a, new Inventory { Slots = 30 });
			List<PersistedComponent> saved = registry.Capture(world, a);
			Assert.That(saved.Single().Key, Is.EqualTo("items.inventory"));
			Assert.That(saved.Single().Version, Is.EqualTo(2));

			Entity b = world.Create();
			saved.Add(new PersistedComponent("housing.plot", 1, "{\"plot\":7}"));
			saved.Add(new PersistedComponent("items.inventory.old", 1, "x"));
			List<PersistedComponent> unread = registry.Restore(world, b, saved);
			Assert.That(world.Get<Inventory>(b).Slots, Is.EqualTo(30));
			Assert.That(unread.Select(u => u.Key), Is.EqualTo(new[] { "housing.plot", "items.inventory.old" }),
				"data of uninstalled modules comes back to be kept, not dropped");

			Entity c = world.Create();
			registry.Restore(world, c, new[] { new PersistedComponent("items.inventory", 1, "10") });
			Assert.That(world.Get<Inventory>(c).Slots, Is.EqualTo(20), "the read is told the version it was written at");

			Assert.That(registry.Capture(world, world.Create()), Is.Empty);
			Assert.Throws<InvalidOperationException>(() => registry.Register<Inventory>("items.inventory", 1, i => "", (t, v) => default));
		}

		[Test]
		public void DiscoverFindsModulesDeclaredOnAnAssembly()
		{
			var problems = new List<string>();
			List<GameModule> found = ModuleCatalog.Discover(new[] { typeof(ModuleTests).Assembly }, problems);
			Assert.That(found.Select(m => m.Id), Does.Contain("test.declared"));
			Assert.That(problems, Is.Empty);
		}

		[Test]
		public void TimingsRecordEachSystemAndChainToAnotherProfiler()
		{
			var world = new World();
			var inner = new Counter();
			var timings = new SystemTimings { Inner = inner };
			var scheduler = new SystemScheduler(world) { Profiler = timings };
			var system = new Lambda(SystemPhase.Input, w => { });
			scheduler.Add(system);
			scheduler.Tick(new TickContext(1, 0, 0.05f, true));
			scheduler.Tick(new TickContext(2, 0.05, 0.05f, true));
			Assert.That(timings.Of(system).Runs, Is.EqualTo(2));
			Assert.That(timings.Of(system).Last, Is.GreaterThanOrEqualTo(0d));
			Assert.That(inner.Count, Is.EqualTo(2));
		}

		[Test]
		public void LiveWorldsListAndForget()
		{
			var world = new World();
			LiveWorld live = LiveWorlds.Register("test", world, new SystemScheduler(world));
			Assert.That(LiveWorlds.All.Contains(live), Is.True);
			LiveWorlds.Unregister(live);
			Assert.That(LiveWorlds.All.Contains(live), Is.False);
		}

		private sealed class Counter : ISystemProfiler
		{
			public int Count;
			public void Begin(GameSystem system) { }
			public void End(GameSystem system) => Count++;
		}

		private sealed class Named : GameSystem
		{
			private readonly string name;
			public Named(string name) => this.name = name;
			public override SystemPhase Phase => SystemPhase.Input;
			public override string Name => name;
			public override void Update(World world, in TickContext tick) { }
		}

		private sealed class Lambda : GameSystem
		{
			private readonly SystemPhase phase;
			private readonly Action<World> body;

			public Lambda(SystemPhase phase, Action<World> body)
			{
				this.phase = phase;
				this.body = body;
			}

			public override SystemPhase Phase => phase;
			public override void Update(World world, in TickContext tick) => body(world);
		}
	}

	public sealed class DeclaredModule : GameModule
	{
		public override string Id => "test.declared";
		public override void Install(ModuleBuilder builder) { }
	}

	/// <summary>The phase 1c gate: Core names no feature, engine or network library.</summary>
	public class CoreBoundaryTests
	{
		[Test]
		public void CoreReferencesOnlyTheBaseLibrary()
		{
			AssemblyName[] references = typeof(World).Assembly.GetReferencedAssemblies();
			string[] foreign = references
				.Select(r => r.Name)
				.Where(n => n != "netstandard" && n != "mscorlib" && !n.StartsWith("System", StringComparison.Ordinal))
				.ToArray();
			Assert.That(foreign, Is.Empty, "FishMMO.Core must reference nothing but the base class library");
		}

		[Test]
		public void CoreAsmdefHasNoReferencesAndNoEngine()
		{
			string root = TestContext.CurrentContext.TestDirectory;
			string asmdef = null;
			for (DirectoryInfo dir = new DirectoryInfo(root); dir != null && asmdef == null; dir = dir.Parent)
			{
				string candidate = Path.Combine(dir.FullName, "FishMMO-Unity", "Assets", "Scripts", "Core", "Runtime", "FishMMO.Core.asmdef");
				if (File.Exists(candidate))
				{
					asmdef = candidate;
				}
			}
			Assert.That(asmdef, Is.Not.Null, "FishMMO.Core.asmdef not found above the test directory");
			string json = File.ReadAllText(asmdef);
			Assert.That(Regex.IsMatch(json, "\"references\"\\s*:\\s*\\[\\s*\\]"), Is.True, "Core references no other assembly");
			Assert.That(Regex.IsMatch(json, "\"noEngineReferences\"\\s*:\\s*true"), Is.True, "Core cannot see UnityEngine");
		}

		[Test]
		public void CoreSourceNamesNoFeatureOrEngine()
		{
			string root = TestContext.CurrentContext.TestDirectory;
			string core = null;
			for (DirectoryInfo dir = new DirectoryInfo(root); dir != null && core == null; dir = dir.Parent)
			{
				string candidate = Path.Combine(dir.FullName, "FishMMO-Unity", "Assets", "Scripts", "Core", "Runtime");
				if (Directory.Exists(candidate))
				{
					core = candidate;
				}
			}
			Assert.That(core, Is.Not.Null);
			var offenders = new List<string>();
			var forbidden = new Regex(@"^\s*using\s+(UnityEngine|UnityEditor|Unity\.|FishNet|FishMMO\.(Shared|Server|Client|Logging))\b", RegexOptions.Multiline);
			foreach (string file in Directory.GetFiles(core, "*.cs", SearchOption.AllDirectories))
			{
				if (forbidden.IsMatch(File.ReadAllText(file)))
				{
					offenders.Add(Path.GetFileName(file));
				}
			}
			Assert.That(offenders, Is.Empty);
		}
	}
}
