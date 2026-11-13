using System;
using System.Collections.Generic;
using FishMMO.Core;
using FishNet.Managing;
using FishNet.Managing.Timing;
using FishNet.Transporting;
using Unity.Profiling;

namespace FishMMO.Shared
{
	/// <summary>
	/// Runs a <see cref="FishMMO.Core.World"/> on the network tick: the adapter between FishNet and the
	/// engine-free simulation. One per <see cref="NetworkManager"/> and side (server, client), made on first use
	/// and stopped with that side's connection.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Its systems come from every installed <see cref="GameModule"/>'s side; the host names none.
	/// One subscription to <see cref="TimeManager.OnTick"/> ticks every Core system, in phase order, instead
	/// of each object subscribing for itself. Simulation time is the tick times the tick length, so it is a
	/// pure function of the shared clock.
	/// </para>
	/// <para>
	/// A system that throws is logged and the rest of the tick skipped; the exception does not escape into
	/// FishNet's tick, where it would stop every later subscriber.
	/// </para>
	/// </remarks>
	public sealed class CoreWorldHost
	{
		private static readonly Dictionary<(NetworkManager, SimulationSide), CoreWorldHost> hosts = new Dictionary<(NetworkManager, SimulationSide), CoreWorldHost>();

		private static ModuleCatalog catalog;

		private readonly NetworkManager networkManager;
		private TimeManager timeManager;
		private bool running;
		private LiveWorld listing;

		private CoreWorldHost(NetworkManager networkManager, SimulationSide side)
		{
			this.networkManager = networkManager;
			Side = side;
			World = new World();
			Timings = new SystemTimings { Inner = new MarkerProfiler() };
			Scheduler = new SystemScheduler(World) { Profiler = Timings };
			World.Resource<Signals>().HandlerFailed = (signal, ex) =>
				FishMMO.Logging.Log.Error("CoreWorldHost", $"A {signal.Name} listener threw; the other listeners still ran: {ex}");
			Install = Catalog.Install(World, Scheduler, side);
			for (int i = 0; i < Install.Problems.Count; i++)
			{
				FishMMO.Logging.Log.Warning("CoreWorldHost", Install.Problems[i]);
			}
		}

		/// <summary>
		/// Every module declared on a loaded assembly (<see cref="GameModuleAttribute"/>), checked and in
		/// dependency order. Read once per process: modules come with assemblies, which do not change while it runs.
		/// </summary>
		public static ModuleCatalog Catalog
		{
			get
			{
				if (catalog == null)
				{
					var problems = new List<string>();
					List<GameModule> found = ModuleCatalog.Discover(AppDomain.CurrentDomain.GetAssemblies(), problems);
					catalog = ModuleCatalog.Resolve(found, problems);
				}
				return catalog;
			}
		}

		/// <summary>The side this world simulates: <see cref="SimulationSide.Server"/> or <see cref="SimulationSide.Client"/>.</summary>
		public SimulationSide Side { get; }

		/// <summary>Which modules this world installed, and why any stayed out.</summary>
		public ModuleInstall Install { get; }

		/// <summary>How long each system took.</summary>
		public SystemTimings Timings { get; }

		/// <summary>The simulation.</summary>
		public World World { get; }

		/// <summary>The systems that tick it.</summary>
		public SystemScheduler Scheduler { get; }

		/// <summary>Simulation time of the latest tick, in seconds: the tick times the tick length.</summary>
		public double Now => timeManager != null ? timeManager.Tick * timeManager.TickDelta : 0d;

		/// <summary>Ticks run since the host started.</summary>
		public long TicksRun { get; private set; }

		/// <summary>
		/// The host for <paramref name="networkManager"/>'s server, started now if it is not yet running.
		/// False when that server is not running.
		/// </summary>
		public static bool TryGetOrStart(NetworkManager networkManager, out CoreWorldHost host) =>
			TryGetOrStart(networkManager, SimulationSide.Server, out host);

		/// <summary>
		/// The host for <paramref name="networkManager"/>'s <paramref name="side"/>, started now if it is not yet
		/// running. False when that side is not running.
		/// </summary>
		public static bool TryGetOrStart(NetworkManager networkManager, SimulationSide side, out CoreWorldHost host)
		{
			if (TryGet(networkManager, side, out host))
			{
				return true;
			}
			host = null;
			if (networkManager == null || networkManager.TimeManager == null || !IsSideStarted(networkManager, side))
			{
				return false;
			}
			host = new CoreWorldHost(networkManager, side);
			host.Start();
			return true;
		}

		/// <summary>The running server host for <paramref name="networkManager"/>, if there is one.</summary>
		public static bool TryGet(NetworkManager networkManager, out CoreWorldHost host) =>
			TryGet(networkManager, SimulationSide.Server, out host);

		/// <summary>The running host for <paramref name="networkManager"/>'s <paramref name="side"/>, if there is one.</summary>
		public static bool TryGet(NetworkManager networkManager, SimulationSide side, out CoreWorldHost host)
		{
			host = null;
			return networkManager != null && hosts.TryGetValue((networkManager, side), out host) && host.running;
		}

		private static bool IsSideStarted(NetworkManager networkManager, SimulationSide side) =>
			side == SimulationSide.Server ? networkManager.IsServerStarted :
			side == SimulationSide.Client && networkManager.IsClientStarted;

		private void Start()
		{
			hosts[(networkManager, Side)] = this;
			timeManager = networkManager.TimeManager;
			timeManager.OnTick += OnTick;
			if (Side == SimulationSide.Server)
			{
				networkManager.ServerManager.OnServerConnectionState += OnServerConnectionState;
			}
			else
			{
				networkManager.ClientManager.OnClientConnectionState += OnClientConnectionState;
			}
			listing = LiveWorlds.Register($"{(Side == SimulationSide.Server ? "Server" : "Client")}: {networkManager.name}", World, Scheduler, Timings);
			running = true;
		}

		/// <summary>Stops ticking and forgets the world. Done for you when the server stops.</summary>
		public void Stop()
		{
			if (!running)
			{
				return;
			}
			running = false;
			if (listing != null)
			{
				LiveWorlds.Unregister(listing);
				listing = null;
			}
			if (timeManager != null)
			{
				timeManager.OnTick -= OnTick;
			}
			if (networkManager != null && networkManager.ServerManager != null)
			{
				networkManager.ServerManager.OnServerConnectionState -= OnServerConnectionState;
			}
			if (networkManager != null && networkManager.ClientManager != null)
			{
				networkManager.ClientManager.OnClientConnectionState -= OnClientConnectionState;
			}
			if (hosts.TryGetValue((networkManager, Side), out CoreWorldHost current) && current == this)
			{
				hosts.Remove((networkManager, Side));
			}
		}

		private void OnClientConnectionState(ClientConnectionStateArgs args)
		{
			if (args.ConnectionState == LocalConnectionState.Stopped)
			{
				Stop();
			}
		}

		private void OnServerConnectionState(ServerConnectionStateArgs args)
		{
			if (args.ConnectionState == LocalConnectionState.Stopped && !networkManager.IsServerStarted)
			{
				Stop();
			}
		}

		private void OnTick()
		{
			uint tick = timeManager.Tick;
			double delta = timeManager.TickDelta;
			try
			{
				Scheduler.Tick(new TickContext(tick, tick * delta, (float)delta, Side == SimulationSide.Server));
				TicksRun++;
			}
			catch (Exception ex)
			{
				FishMMO.Logging.Log.Error("CoreWorldHost", $"A Core system threw on tick {tick}; the rest of the tick was skipped: {ex}");
			}
		}

		/// <summary>Shows each Core system in the Unity profiler as <c>Core.&lt;system&gt;</c>.</summary>
		private sealed class MarkerProfiler : ISystemProfiler
		{
			private readonly Dictionary<GameSystem, ProfilerMarker> markers = new Dictionary<GameSystem, ProfilerMarker>();

			public void Begin(GameSystem system)
			{
				if (!markers.TryGetValue(system, out ProfilerMarker marker))
				{
					marker = new ProfilerMarker("Core." + system.Name);
					markers[system] = marker;
				}
				marker.Begin();
			}

			public void End(GameSystem system)
			{
				if (markers.TryGetValue(system, out ProfilerMarker marker))
				{
					marker.End();
				}
			}
		}
	}
}
