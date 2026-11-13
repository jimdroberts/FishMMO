using System;
using System.Collections.Generic;
using System.Reflection;

namespace FishMMO.Core
{
	/// <summary>Where a system runs.</summary>
	[Flags]
	public enum SimulationSide
	{
		/// <summary>Nowhere.</summary>
		None = 0,
		/// <summary>On the server's world.</summary>
		Server = 1,
		/// <summary>On a client's world.</summary>
		Client = 2,
		/// <summary>On both.</summary>
		Both = Server | Client,
	}

	/// <summary>
	/// One installable piece of the game: what it adds to a world. Core reads every installed module and asks
	/// each, in dependency order, to describe itself to a <see cref="ModuleBuilder"/>; Core never names a module.
	/// </summary>
	/// <remarks>
	/// A module is installed by declaring it on its assembly with <see cref="GameModuleAttribute"/>; removing
	/// the assembly (or the attribute) uninstalls it. It needs a public parameterless constructor.
	/// </remarks>
	public abstract class GameModule
	{
		private static readonly string[] none = new string[0];

		/// <summary>The module's id, unique among installed modules (<c>items</c>, <c>npc.corpses</c>).</summary>
		public abstract string Id { get; }

		/// <summary>The module's version; raised when its saved data changes shape.</summary>
		public virtual int Version => 1;

		/// <summary>The ids of the modules this one needs. Each is installed first; a missing one keeps this one out.</summary>
		public virtual IReadOnlyList<string> Dependencies => none;

		/// <summary>Describes what the module adds: systems, archetype components, effect handlers, saved components.</summary>
		public abstract void Install(ModuleBuilder builder);

		/// <inheritdoc />
		public override string ToString() => $"{Id} v{Version}";
	}

	/// <summary>Installs a <see cref="GameModule"/>: <c>[assembly: GameModule(typeof(MyModule))]</c>.</summary>
	[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
	public sealed class GameModuleAttribute : Attribute
	{
		/// <summary>The module's type.</summary>
		public Type ModuleType { get; }

		/// <summary>Installs <paramref name="moduleType"/>.</summary>
		public GameModuleAttribute(Type moduleType)
		{
			ModuleType = moduleType;
		}
	}

	/// <summary>Core's own module: the systems behind its data building blocks.</summary>
	public sealed class CoreModule : GameModule
	{
		/// <summary>The id every module may depend on.</summary>
		public const string ModuleId = "core";

		/// <inheritdoc />
		public override string Id => ModuleId;

		/// <inheritdoc />
		public override void Install(ModuleBuilder builder)
		{
			builder.AddSystem<EffectSystem>(SimulationSide.Both);
			builder.AddSystem<StatSystem>(SimulationSide.Both);
			builder.AddSystem<RegenerationSystem>(SimulationSide.Both);
			builder.AddSystem<LifetimeSystem>(SimulationSide.Both);
			builder.Archetype(Archetypes.Player);
			builder.Archetype(Archetypes.Npc);
			builder.Archetype(Archetypes.AbilityObject);
			builder.Archetype(Archetypes.Interactable);
		}
	}

	/// <summary>What a <see cref="GameModule"/> describes itself to.</summary>
	public sealed class ModuleBuilder
	{
		private readonly ModuleInstall install;

		internal ModuleBuilder(GameModule module, ModuleInstall install)
		{
			Module = module;
			this.install = install;
		}

		/// <summary>The module being installed.</summary>
		public GameModule Module { get; }

		/// <summary>The side being installed, for a module that adds different things to each.</summary>
		public SimulationSide Side => install.Side;

		/// <summary>The world being installed into.</summary>
		public World World => install.World;

		/// <summary>Adds system <typeparamref name="T"/> on <paramref name="side"/>. A system already added is not added twice.</summary>
		public void AddSystem<T>(SimulationSide side) where T : GameSystem, new()
		{
			if ((side & install.Side) != 0 && install.Scheduler.Get<T>() == null)
			{
				install.Scheduler.Add(new T());
				install.Record(Module, typeof(T).Name);
			}
		}

		/// <summary>Adds the system <paramref name="create"/> makes, on <paramref name="side"/>.</summary>
		public void AddSystem(SimulationSide side, Func<GameSystem> create)
		{
			if ((side & install.Side) == 0)
			{
				return;
			}
			GameSystem system = create() ?? throw new InvalidOperationException($"Module {Module.Id} made a null system.");
			install.Scheduler.Add(system);
			install.Record(Module, system.Name);
		}

		/// <summary>The archetype named <paramref name="name"/>, defined if it is new, to add starting components to.</summary>
		public ArchetypeBuilder Archetype(string name)
		{
			Archetypes archetypes = install.World.Resource<Archetypes>();
			archetypes.Define(name);
			return new ArchetypeBuilder(archetypes, name, Module.Id);
		}

		/// <summary>Handles effects of <paramref name="kind"/> with <paramref name="handler"/>.</summary>
		public void Effect(EffectKind kind, EffectHandler handler)
		{
			install.World.Resource<EffectQueue>().Register(kind, handler);
		}

		/// <summary>Saves and loads component <typeparamref name="T"/> under <paramref name="key"/> (see <see cref="PersistenceRegistry"/>).</summary>
		public void Persist<T>(string key, int version, Func<T, string> write, Func<string, int, T> read) where T : struct
		{
			install.World.Resource<PersistenceRegistry>().Register(key, version, write, read, Module.Id);
		}

		/// <summary>
		/// Calls <paramref name="handler"/> at once whenever a signal of type <typeparamref name="T"/> is
		/// published on <paramref name="side"/> (<see cref="Signals"/>): for facts whose consequence cannot wait
		/// for the next tick.
		/// </summary>
		public void OnSignal<T>(SimulationSide side, SignalHandler<T> handler) where T : struct
		{
			if ((side & install.Side) != 0)
			{
				install.World.Resource<Signals>().Subscribe(handler);
				install.Record(Module, $"on {typeof(T).Name} (signal)");
			}
		}

		/// <summary>
		/// Calls <paramref name="listener"/> for each event of type <typeparamref name="T"/> raised before
		/// <paramref name="phase"/> in each tick (and between ticks): the way a module follows another's facts,
		/// such as achievements counting kills, without either naming the other.
		/// </summary>
		public void OnEvent<T>(SimulationSide side, Action<World, T> listener, SystemPhase phase = SystemPhase.Objectives) where T : struct
		{
			AddSystem(side, () => new EventListenerSystem<T>(Module.Id, phase, listener));
		}
	}

	/// <summary>Adds starting components to one archetype.</summary>
	public readonly struct ArchetypeBuilder
	{
		private readonly Archetypes archetypes;
		private readonly string name;
		private readonly string module;

		internal ArchetypeBuilder(Archetypes archetypes, string name, string module)
		{
			this.archetypes = archetypes;
			this.name = name;
			this.module = module;
		}

		/// <summary>Every entity of the archetype starts with <paramref name="component"/>.</summary>
		public ArchetypeBuilder Add<T>(T component) where T : struct
		{
			archetypes.Add(name, module, typeof(T).Name, (world, entity) =>
			{
				if (world.IsTicking)
				{
					world.Commands.Add(entity, component);
				}
				else
				{
					world.Add(entity, component);
				}
			});
			return this;
		}

		/// <summary>
		/// Every entity of the archetype starts with its own component from <paramref name="create"/>: for a
		/// component holding a list or other reference that entities must not share.
		/// </summary>
		public ArchetypeBuilder Add<T>(Func<T> create) where T : struct
		{
			if (create == null)
			{
				throw new ArgumentNullException(nameof(create));
			}
			archetypes.Add(name, module, typeof(T).Name, (world, entity) =>
			{
				T component = create();
				if (world.IsTicking)
				{
					world.Commands.Add(entity, component);
				}
				else
				{
					world.Add(entity, component);
				}
			});
			return this;
		}

		/// <summary>Every entity of the archetype is set up by <paramref name="initialize"/> (stats, for one).</summary>
		public ArchetypeBuilder Add(string description, Action<World, Entity> initialize)
		{
			archetypes.Add(name, module, description, initialize);
			return this;
		}
	}

	/// <summary>Runs a module's event listener (<see cref="ModuleBuilder.OnEvent{T}"/>).</summary>
	internal sealed class EventListenerSystem<T> : GameSystem where T : struct
	{
		private readonly string module;
		private readonly SystemPhase phase;
		private readonly Action<World, T> listener;

		public EventListenerSystem(string module, SystemPhase phase, Action<World, T> listener)
		{
			this.module = module;
			this.phase = phase;
			this.listener = listener ?? throw new ArgumentNullException(nameof(listener));
		}

		public override SystemPhase Phase => phase;

		public override string Name => $"{module}: on {typeof(T).Name}";

		public override void Update(World world, in TickContext tick)
		{
			ReadOnlySpan<T> raised = world.Events<T>().Current;
			for (int i = 0; i < raised.Length; i++)
			{
				listener(world, raised[i]);
			}
		}
	}

	/// <summary>One world's installation: which modules went in, what each added, and why any stayed out.</summary>
	public sealed class ModuleInstall
	{
		private readonly Dictionary<string, List<string>> added = new Dictionary<string, List<string>>(StringComparer.Ordinal);

		internal ModuleInstall(World world, SystemScheduler scheduler, SimulationSide side)
		{
			World = world;
			Scheduler = scheduler;
			Side = side;
		}

		/// <summary>The world.</summary>
		public World World { get; }

		/// <summary>Its systems.</summary>
		public SystemScheduler Scheduler { get; }

		/// <summary>The side installed.</summary>
		public SimulationSide Side { get; }

		/// <summary>The modules installed, in install order.</summary>
		public IReadOnlyList<GameModule> Modules { get; internal set; } = new GameModule[0];

		/// <summary>Why modules were left out, and modules that threw while installing.</summary>
		public IReadOnlyList<string> Problems { get; internal set; } = new string[0];

		/// <summary>The systems <paramref name="moduleId"/> added.</summary>
		public IReadOnlyList<string> SystemsOf(string moduleId) =>
			added.TryGetValue(moduleId, out List<string> list) ? list : (IReadOnlyList<string>)new string[0];

		internal void Record(GameModule module, string system)
		{
			if (!added.TryGetValue(module.Id, out List<string> list))
			{
				list = new List<string>();
				added[module.Id] = list;
			}
			list.Add(system);
		}
	}

	/// <summary>
	/// The installed modules, checked and put in dependency order: a module whose dependency is missing, or
	/// that sits in a dependency cycle, or whose id another already has, is left out with the reason.
	/// </summary>
	public sealed class ModuleCatalog
	{
		private ModuleCatalog(List<GameModule> modules, List<string> problems)
		{
			Modules = modules;
			Problems = problems;
		}

		/// <summary>The modules to install, every dependency before its dependents. Core's own module is first.</summary>
		public IReadOnlyList<GameModule> Modules { get; }

		/// <summary>Why modules were left out.</summary>
		public IReadOnlyList<string> Problems { get; }

		/// <summary>Every module declared with <see cref="GameModuleAttribute"/> on <paramref name="assemblies"/>.</summary>
		public static List<GameModule> Discover(IEnumerable<Assembly> assemblies, List<string> problems = null)
		{
			var found = new List<GameModule>();
			foreach (Assembly assembly in assemblies)
			{
				object[] attributes;
				try
				{
					attributes = assembly.GetCustomAttributes(typeof(GameModuleAttribute), false);
				}
				catch (Exception ex)
				{
					problems?.Add($"{assembly.GetName().Name}: could not read its modules ({ex.GetType().Name}).");
					continue;
				}
				foreach (GameModuleAttribute attribute in attributes)
				{
					Type type = attribute.ModuleType;
					if (type == null || type.IsAbstract || !typeof(GameModule).IsAssignableFrom(type))
					{
						problems?.Add($"{assembly.GetName().Name}: {type?.FullName ?? "null"} is not a GameModule.");
						continue;
					}
					try
					{
						found.Add((GameModule)Activator.CreateInstance(type));
					}
					catch (Exception ex)
					{
						problems?.Add($"{type.FullName}: could not be created ({ex.GetType().Name}: {ex.Message}).");
					}
				}
			}
			return found;
		}

		/// <summary>Checks <paramref name="modules"/> and orders them; Core's own module is added when absent.</summary>
		public static ModuleCatalog Resolve(IEnumerable<GameModule> modules, IEnumerable<string> earlierProblems = null)
		{
			var problems = new List<string>();
			if (earlierProblems != null)
			{
				problems.AddRange(earlierProblems);
			}

			var byId = new Dictionary<string, GameModule>(StringComparer.Ordinal);
			var declared = new List<GameModule> { new CoreModule() };
			if (modules != null)
			{
				declared.AddRange(modules);
			}
			foreach (GameModule module in declared)
			{
				if (module == null || string.IsNullOrEmpty(module.Id))
				{
					problems.Add($"{module?.GetType().FullName ?? "null"}: a module needs an id.");
					continue;
				}
				if (byId.TryGetValue(module.Id, out GameModule first))
				{
					if (first.GetType() != module.GetType())
					{
						problems.Add($"{module.GetType().FullName}: id '{module.Id}' is already {first.GetType().FullName}'s; left out.");
					}
					continue;
				}
				byId[module.Id] = module;
			}

			var ordered = new List<GameModule>();
			var state = new Dictionary<string, int>(StringComparer.Ordinal);   // 1 visiting, 2 done, 3 left out
			var ids = new List<string>(byId.Keys);
			ids.Sort(StringComparer.Ordinal);
			ids.Remove(CoreModule.ModuleId);
			ids.Insert(0, CoreModule.ModuleId);   // deterministic, Core first
			foreach (string id in ids)
			{
				Visit(id, byId, state, ordered, problems, new List<string>());
			}
			return new ModuleCatalog(ordered, problems);
		}

		private static bool Visit(string id, Dictionary<string, GameModule> byId, Dictionary<string, int> state,
			List<GameModule> ordered, List<string> problems, List<string> path)
		{
			if (state.TryGetValue(id, out int seen))
			{
				if (seen == 1)
				{
					int start = path.IndexOf(id);
					problems.Add($"Dependency cycle: {string.Join(" -> ", path.GetRange(start, path.Count - start))} -> {id}; left out.");
					return false;
				}
				return seen == 2;
			}
			GameModule module = byId[id];
			state[id] = 1;
			path.Add(id);
			bool ok = true;
			foreach (string dependency in module.Dependencies ?? new string[0])
			{
				if (string.IsNullOrEmpty(dependency) || dependency == CoreModule.ModuleId)
				{
					continue;
				}
				if (!byId.ContainsKey(dependency))
				{
					problems.Add($"{id} needs '{dependency}', which is not installed; left out.");
					ok = false;
				}
				else if (!Visit(dependency, byId, state, ordered, problems, path))
				{
					if (state[dependency] == 3)
					{
						problems.Add($"{id} needs '{dependency}', which was left out; left out.");
					}
					ok = false;
				}
			}
			path.RemoveAt(path.Count - 1);
			state[id] = ok ? 2 : 3;
			if (ok)
			{
				ordered.Add(module);
			}
			return ok;
		}

		/// <summary>
		/// Installs every module's <paramref name="side"/> into <paramref name="world"/> and its
		/// <paramref name="scheduler"/>. A module that throws is reported and the rest still install.
		/// </summary>
		public ModuleInstall Install(World world, SystemScheduler scheduler, SimulationSide side)
		{
			var install = new ModuleInstall(world, scheduler, side);
			var installed = new List<GameModule>(Modules.Count);
			var problems = new List<string>(Problems);
			foreach (GameModule module in Modules)
			{
				try
				{
					module.Install(new ModuleBuilder(module, install));
					installed.Add(module);
				}
				catch (Exception ex)
				{
					problems.Add($"{module.Id} threw while installing: {ex.GetType().Name}: {ex.Message}");
				}
			}
			install.Modules = installed;
			install.Problems = problems;
			world.SetResource(install);
			return install;
		}
	}
}
