using System;
using System.Collections.Generic;
using System.Globalization;
using FishNet.Transporting.WebTransport;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Logging;
using FishMMO.Server.Core;
using FishMMO.Server.Implementation.World.SceneServer.AI;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// <c>/admin simulate &lt;count&gt;</c> and <c>/admin simclear</c>: simulated players for stress tests. Each is the
	/// "Simulated Player" NPC (SimulatedPlayerBuilder): a humanoid that keeps walking around where it was placed, streamed to
	/// every viewer under the PLAYER observer classification, so it costs a viewer what a player would (transform updates,
	/// broadcasts, the player visibility and byte budgets) and costs this server the simulation of a moving character.
	/// </summary>
	/// <remarks>
	/// <para>
	/// What they do not load: a client connection each (login, its own reconcile stream, encryption). That is a separate,
	/// external bot-client test.
	/// </para>
	/// <para>
	/// Spawned the way a boss spawns its adds: from the network manager's pool, moved into the admin's scene, brain prepared
	/// on the NavMesh at its spot (its home, which it wanders around), then spawned. Capped per command and per server so a
	/// typo cannot spawn a server to death. Listed in the staff console with every other /admin command.
	/// </para>
	/// </remarks>
	public partial class SceneServerSystem
	{
		private const string SimulatedPlayerPrefabName = "Simulated Player";
		private const int MaxSimulatedPlayersPerCommand = 200;
		private const int MaxSimulatedPlayers = 2000;
		private const float SimulatedPlayerSpawnRadius = 25f;

		private const int MaxSimulationRunSeconds = 3600;

		private readonly List<NetworkObject> simulatedPlayers = new List<NetworkObject>();

		/// <summary>A timed stress run: its simulated players, and what is measured over exactly its window.</summary>
		private sealed class SimulationRun
		{
			public IPlayerCharacter Admin;
			public string AdminName;
			public string SceneName;
			public readonly List<NetworkObject> Spawned = new List<NetworkObject>();
			public double StartedAt;
			public double EndsAt;
			public int Seconds;
			public int RealPlayersAtStart;
			public TransportTrafficSnapshot TrafficAtStart;
			/// <summary>Reconciles written and skipped as quiet, and player-ticks at the idle send interval, at the start.</summary>
			public long ReconcilesAtStart, QuietAtStart, PlayerTicksAtStart, IdleTicksAtStart;
			/// <summary>Per state group, idle owners' reconciles that changed (CharacterPredictionController.QuietMissCounts) at the start.</summary>
			public long[] QuietMissAtStart;
			/// <summary>Process CPU time at the window's start; null where it cannot be read.</summary>
			public TimeSpan? CpuAtStart;
			public ServerTrafficBreakdown Breakdown;
			/// <summary>Every packet's bytes over the run, for the compression estimate and its training sample.</summary>
			public PacketCompressionSampler Compression;
			public int Frames;
			public double FrameSeconds;
			public double LongestFrame;
			/// <summary>Garbage collections counted so far (per generation), to say whether the worst frame had one.</summary>
			public int Gc0, Gc1, Gc2;
			/// <summary>Which generation collected in the worst frame; -1 for none.</summary>
			public int WorstFrameGcGeneration = -1;
		}

		private SimulationRun simulationRun;

		/// <summary>
		/// This process's CPU time (user + system, all threads). Linux: /proc/self/stat, because the IL2CPP player's
		/// <c>Process.TotalProcessorTime</c> throws NotSupportedException ("file handle duplication"). Elsewhere the
		/// Process API, guarded. False when neither works.
		/// </summary>
		private static bool TryReadProcessCpuTime(out TimeSpan cpu)
		{
			cpu = TimeSpan.Zero;
			try
			{
				const string StatPath = "/proc/self/stat";
				if (System.IO.File.Exists(StatPath))
				{
					// Fields after the command name, which is in parentheses and may contain spaces: state is field 3,
					// utime 14 and stime 15, in clock ticks (USER_HZ, 100 on Linux x86-64 and arm64).
					string stat = System.IO.File.ReadAllText(StatPath);
					string[] fields = stat.Substring(stat.LastIndexOf(')') + 2).Split(' ');
					long ticks = long.Parse(fields[14 - 3], CultureInfo.InvariantCulture) + long.Parse(fields[15 - 3], CultureInfo.InvariantCulture);
					cpu = TimeSpan.FromSeconds(ticks / 100.0);
					return true;
				}
				cpu = System.Diagnostics.Process.GetCurrentProcess().TotalProcessorTime;
				return true;
			}
			catch (Exception)
			{
				return false;
			}
		}

		private IEnumerable<OperatorCommand> BuildAdminSimulationCommands()
		{
			return new List<OperatorCommand>
			{
				new OperatorCommand
				{
					Name = "simulate", Aliases = new[] { "simplayers", "bots" }, Category = "Testing",
					Summary = "Spawns simulated players that walk around near you. With seconds: removed then, with a traffic and CPU report.",
					Arguments = "count:Integer;seconds:Integer?",
					Run = SpawnSimulatedPlayers,
				},
				new OperatorCommand
				{
					Name = "simclear", Aliases = new[] { "clearsim", "clearbots" }, Category = "Testing",
					Summary = "Removes every simulated player on this scene server.",
					Run = (c, a) => ClearSimulatedPlayers(c),
				},
			};
		}

		private void SpawnSimulatedPlayers(IPlayerCharacter character, string arguments)
		{
			string[] words = (arguments ?? string.Empty).Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
			if (words.Length < 1 || !int.TryParse(words[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int requested) || requested < 1)
			{
				Reply(character, $"Give how many to spawn, 1 to {MaxSimulatedPlayersPerCommand}, and optionally for how many seconds: /admin simulate 50 120.");
				return;
			}
			int seconds = 0;
			if (words.Length > 1 && (!int.TryParse(words[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out seconds) || seconds < 1 || seconds > MaxSimulationRunSeconds))
			{
				Reply(character, $"The run length must be 1 to {MaxSimulationRunSeconds} seconds.");
				return;
			}
			if (seconds > 0 && (simulationRun != null || simulationRunPending))
			{
				Reply(character, simulationRun != null
					? $"A timed run is already measuring ({simulationRun.EndsAt - MonotonicClock.NowSeconds:0} s left). /admin simclear ends it early."
					: "A timed run's players are still spawning. /admin simclear stops it.");
				return;
			}
			PruneSimulatedPlayers();
			int count = Mathf.Min(requested, MaxSimulatedPlayersPerCommand, MaxSimulatedPlayers - simulatedPlayers.Count);
			if (count <= 0)
			{
				Reply(character, $"This server already runs {simulatedPlayers.Count} simulated players, the most allowed. /admin simclear removes them.");
				return;
			}

			NetworkManager networkManager = Server?.NetworkWrapper?.NetworkManager;
			if (networkManager == null || !AIBrainHost.TryGet(networkManager, out AIBrainHost host) || host.Catalogue == null)
			{
				Reply(character, "This server has no AI brain host; simulated players cannot walk.");
				return;
			}
			NetworkObject prefab = FindSimulatedPlayerPrefab(host.Catalogue);
			if (prefab == null)
			{
				Reply(character, $"No '{SimulatedPlayerPrefabName}' prefab in the brain catalogue. Build it: Dashboard → Maintenance → Testing → Build simulated player.");
				return;
			}

			Scene scene = character.GameObject.scene;
			Vector3 origin = character.Transform.position;
			SimulationRun run = seconds > 0 ? new SimulationRun
			{
				Admin = character,
				AdminName = character.CharacterName,
				SceneName = scene.name,
				Seconds = seconds,
			} : null;
			if (pendingSpawns != null)
			{
				Reply(character, $"Still spawning the last batch ({pendingSpawns.Remaining} to go).");
				return;
			}
			pendingSpawns = new PendingSimulatedSpawns
			{
				Admin = character,
				Requested = requested,
				Remaining = count,
				Origin = origin,
				Scene = scene,
				Run = run,
				NetworkManager = networkManager,
				Host = host,
				Prefab = prefab,
			};
			if (run != null)
			{
				simulationRunPending = true;
			}
			Reply(character, $"Spawning {count} simulated player(s), {SimulatedSpawnsPerFrame} a frame{(run != null ? $"; the {seconds} s measurement starts when the last is in" : string.Empty)}.");
		}

		/// <summary>Simulated players still to spawn, a few a frame, and what to do when they are all in.</summary>
		private sealed class PendingSimulatedSpawns
		{
			public IPlayerCharacter Admin;
			public int Requested;
			public int Remaining;
			public int Spawned;
			public Vector3 Origin;
			public Scene Scene;
			public SimulationRun Run;
			public NetworkManager NetworkManager;
			public AIBrainHost Host;
			public NetworkObject Prefab;
		}

		private PendingSimulatedSpawns pendingSpawns;
		/// <summary>True while a timed run's players are still spawning (no second run may start).</summary>
		private bool simulationRunPending;

		/// <summary>
		/// Spawns per frame. Fifty at once was one long frame of spawn work and a burst of spawn messages to every viewer;
		/// spread out, a crowd arrives over a few frames the way real players would.
		/// </summary>
		private const int SimulatedSpawnsPerFrame = 5;

		/// <summary>Spawns the next few of a pending batch; finishes the batch (and starts its run) when the last is in.</summary>
		private void SpawnPendingSimulatedPlayers()
		{
			PendingSimulatedSpawns batch = pendingSpawns;
			if (batch == null)
			{
				return;
			}
			if (batch.NetworkManager == null || !batch.Scene.IsValid() || !batch.Scene.isLoaded)
			{
				pendingSpawns = null;
				simulationRunPending = false;
				return;
			}
			for (int i = 0; i < SimulatedSpawnsPerFrame && batch.Remaining > 0; ++i)
			{
				batch.Remaining--;
				Vector2 offset = UnityEngine.Random.insideUnitCircle * SimulatedPlayerSpawnRadius;
				Vector3 position = batch.Origin + new Vector3(offset.x, 0f, offset.y);
				Quaternion rotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
				if (TrySpawnSimulatedPlayer(batch.NetworkManager, batch.Host, batch.Prefab, batch.Scene, position, rotation, out NetworkObject instance))
				{
					simulatedPlayers.Add(instance);
					batch.Run?.Spawned.Add(instance);
					batch.Spawned++;
				}
			}
			if (batch.Remaining > 0)
			{
				return;
			}
			pendingSpawns = null;
			simulationRunPending = false;
			FinishSpawning(batch);
		}

		/// <summary>The batch is all in: starts its timed run, or reports the spawn.</summary>
		private void FinishSpawning(PendingSimulatedSpawns batch)
		{
			IPlayerCharacter character = batch.Admin;
			NetworkManager networkManager = batch.NetworkManager;
			SimulationRun run = batch.Run;
			Scene scene = batch.Scene;
			int spawned = batch.Spawned;
			int requested = batch.Requested;
			int seconds = run != null ? run.Seconds : 0;
			if (run != null)
			{
				// The window opens only now, with everyone spawned: the spawn burst is not in the measurement.
				run.StartedAt = MonotonicClock.NowSeconds;
				run.EndsAt = run.StartedAt + seconds;
				run.RealPlayersAtStart = networkManager.ServerManager.Clients.Count;
				TransportTraffic.Capture(out run.TrafficAtStart);
				run.ReconcilesAtStart = CharacterPredictionController.ReconcilesSent;
				run.QuietAtStart = CharacterPredictionController.QuietReconcilesSkipped;
				run.PlayerTicksAtStart = scheduledPlayerTicks;
				run.IdleTicksAtStart = idleIntervalPlayerTicks;
				run.QuietMissAtStart = (long[])CharacterPredictionController.QuietMissCounts.Clone();
				run.CpuAtStart = TryReadProcessCpuTime(out TimeSpan cpuNow) ? cpuNow : (TimeSpan?)null;
				run.Gc0 = GC.CollectionCount(0);
				run.Gc1 = GC.CollectionCount(1);
				run.Gc2 = GC.CollectionCount(2);
				ObserverStreamingRegistry.ResetLongestPass();
				ServerTrafficBreakdown.ResetLongestReindex();
				run.Breakdown = ServerTrafficBreakdown.BeginWindow(networkManager);
				run.Compression = new PacketCompressionSampler();
				run.Compression.Start();
				simulationRun = run;
				Reply(character, $"Measuring {spawned} simulated player(s) for {seconds} s; the report follows here and in the server log. /admin simclear ends it early.");
				_ = Log.Info("SceneServerSystem", $"{character.CharacterName} started a {seconds} s simulation run of {spawned} in '{scene.name}'.");
				return;
			}
			string capped = spawned < requested ? $" (asked for {requested}; at most {MaxSimulatedPlayersPerCommand} a command and {MaxSimulatedPlayers} in all)" : string.Empty;
			Reply(character, $"Spawned {spawned} simulated player(s) within {SimulatedPlayerSpawnRadius:0} m of you{capped}. {simulatedPlayers.Count} running on this server.");
			_ = Log.Info("SceneServerSystem", $"{character.CharacterName} spawned {spawned} simulated players in '{scene.name}'; {simulatedPlayers.Count} running.");
		}

		/// <summary>One simulated player, spawned as a boss spawns an add; rolled back into the pool if anything interrupts it.</summary>
		private static bool TrySpawnSimulatedPlayer(NetworkManager networkManager, AIBrainHost host, NetworkObject prefab, Scene scene, Vector3 position, Quaternion rotation, out NetworkObject instance)
		{
			instance = networkManager.GetPooledInstantiated(prefab, position, rotation, true);
			if (instance == null)
			{
				return false;
			}
			bool completed = false;
			try
			{
				SceneManager.MoveGameObjectToScene(instance.gameObject, scene);
				NPC npc = instance.GetComponent<NPC>();
				if (npc != null)
				{
					// Warped onto the NavMesh at its spot, which becomes the home it wanders around.
					host.Prepare(npc, position);
				}
				networkManager.ServerManager.Spawn(instance, null, scene);
				completed = true;
			}
			finally
			{
				if (!completed)
				{
					if (instance.IsSpawned)
					{
						networkManager.ServerManager.Despawn(instance, DespawnType.Pool);
					}
					else
					{
						AIBrainHost.RetireBrainOf(networkManager, instance);
						networkManager.StorePooledInstantiated(instance, true);
					}
					instance = null;
				}
			}
			return completed;
		}

		private void ClearSimulatedPlayers(IPlayerCharacter character)
		{
			// A batch still spawning stops here: what it already spawned is despawned below.
			pendingSpawns = null;
			simulationRunPending = false;
			if (simulationRun != null)
			{
				FinishSimulationRun(early: true);
			}
			int removed = DespawnSimulatedPlayers();
			Reply(character, removed > 0 ? $"Removed {removed} simulated player(s)." : "No simulated players are running on this server.");
			if (removed > 0)
			{
				_ = Log.Info("SceneServerSystem", $"{character.CharacterName} removed {removed} simulated players.");
			}
		}

		/// <summary>Despawns every simulated player this server spawned (also on shutdown). Returns how many.</summary>
		private int DespawnSimulatedPlayers()
		{
			pendingSpawns = null;
			simulationRunPending = false;
			if (simulationRun != null)
			{
				// Shutting down mid-run: the report is logged with what was measured so far.
				FinishSimulationRun(early: true);
			}
			NetworkManager networkManager = Server?.NetworkWrapper?.NetworkManager;
			int removed = 0;
			foreach (NetworkObject instance in simulatedPlayers)
			{
				/* Only this command spawns the prefab, so a spawned instance in the list is still one of ours even if it died
				 * and came back from the pool. */
				if (instance != null && instance.IsSpawned && networkManager != null)
				{
					networkManager.ServerManager.Despawn(instance, DespawnType.Pool);
					removed++;
				}
			}
			simulatedPlayers.Clear();
			return removed;
		}

		/// <summary>Each frame: the run's frame timing, and its end on the second.</summary>
		private void UpdateSimulationRun(float deltaTime)
		{
			SpawnPendingSimulatedPlayers();
			SimulationRun run = simulationRun;
			if (run == null)
			{
				return;
			}
			run.Frames++;
			run.FrameSeconds += deltaTime;
			// A collection during this frame (counted since the last one), by the highest generation that ran.
			int gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2);
			int collected = gc2 != run.Gc2 ? 2 : gc1 != run.Gc1 ? 1 : gc0 != run.Gc0 ? 0 : -1;
			run.Gc0 = gc0;
			run.Gc1 = gc1;
			run.Gc2 = gc2;
			if (deltaTime > run.LongestFrame)
			{
				run.LongestFrame = deltaTime;
				run.WorstFrameGcGeneration = collected;
			}
			if (MonotonicClock.NowSeconds >= run.EndsAt)
			{
				FinishSimulationRun(early: false);
			}
		}

		/// <summary>
		/// Ends the timed run: measures, despawns its simulated players, and reports to the admin (when still online) and the
		/// log. Traffic is this scene server's whole outbound UDP, measured by msquic, per real player connected.
		/// </summary>
		private void FinishSimulationRun(bool early)
		{
			SimulationRun run = simulationRun;
			simulationRun = null;
			if (run == null)
			{
				return;
			}
			double seconds = Math.Max(1.0, MonotonicClock.NowSeconds - run.StartedAt);
			TransportTraffic.Capture(out TransportTrafficSnapshot end);
			string cpuText = run.CpuAtStart.HasValue && TryReadProcessCpuTime(out TimeSpan cpuEnd)
				? $"{100.0 * (cpuEnd - run.CpuAtStart.Value).TotalSeconds / seconds:0}% of one core"
				: "unavailable on this platform";
			NetworkManager networkManager = Server?.NetworkWrapper?.NetworkManager;
			int players = Math.Max(1, networkManager != null ? networkManager.ServerManager.Clients.Count : run.RealPlayersAtStart);

			long udpBytes = end.QuicSentBytes - run.TrafficAtStart.QuicSentBytes;
			long datagrams = end.UdpSentDatagrams - run.TrafficAtStart.UdpSentDatagrams;
			long wireBytes = end.WireSentBytes - run.TrafficAtStart.WireSentBytes;
			long appBytes = end.AppSentBytes - run.TrafficAtStart.AppSentBytes;
			// Upload: what the players send (inputs, the acks for the server's packets). Counts against the same monthly cap
			// when the host bills both directions.
			long udpRecvBytes = end.QuicRecvBytes - run.TrafficAtStart.QuicRecvBytes;
			long datagramsIn = end.UdpRecvDatagrams - run.TrafficAtStart.UdpRecvDatagrams;
			long wireRecvBytes = end.WireRecvBytes - run.TrafficAtStart.WireRecvBytes;
			long appRecvBytes = end.AppRecvBytes - run.TrafficAtStart.AppRecvBytes;
			long appRecvMessages = end.AppRecvMessages - run.TrafficAtStart.AppRecvMessages;
			long reconcilesSent = CharacterPredictionController.ReconcilesSent - run.ReconcilesAtStart;
			long quietSkipped = CharacterPredictionController.QuietReconcilesSkipped - run.QuietAtStart;
			long playerTicks = scheduledPlayerTicks - run.PlayerTicksAtStart;
			long idleTicks = idleIntervalPlayerTicks - run.IdleTicksAtStart;
			var summary = new List<string>();
			string full = run.Breakdown != null ? run.Breakdown.FinishWindow(6, summary) : string.Empty;
			var compressionSummary = new List<string>();
			string compressionSample = run.Compression != null ? run.Compression.Finish(compressionSummary) : string.Empty;

			int alive = 0;
			foreach (NetworkObject n in run.Spawned)
			{
				alive += n != null && n.IsSpawned ? 1 : 0;
			}
			var lines = new List<string>
			{
				$"Simulation {(early ? "ended early" : "done")}: {run.Spawned.Count} simulated ({alive} still up), {players} real player(s), {seconds:0} s in '{run.SceneName}'.",
				$"Server egress (measured): {wireBytes / seconds / 1000.0:0.0} kB/s wire, {datagrams / seconds:0} packets/s; per player {wireBytes / seconds / players / 1000.0:0.00} kB/s, {datagrams / seconds / players:0} pkt/s.",
				$"  UDP payload {udpBytes / seconds / 1000.0:0.0} kB/s, game data {appBytes / seconds / 1000.0:0.0} kB/s (overhead x{(appBytes > 0 ? (double)wireBytes / appBytes : 0):0.00}).",
				$"Server ingress (measured): {wireRecvBytes / seconds / 1000.0:0.0} kB/s wire, {datagramsIn / seconds:0} packets/s; per player {wireRecvBytes / seconds / players / 1000.0:0.00} kB/s, {datagramsIn / seconds / players:0} pkt/s.",
				$"  UDP payload {udpRecvBytes / seconds / 1000.0:0.0} kB/s, game data {appRecvBytes / seconds / 1000.0:0.0} kB/s in {appRecvMessages / seconds / players:0} msg/s per player; the rest is QUIC acks and headers.",
				$"Server CPU: {cpuText}; frames avg {1000.0 * run.FrameSeconds / Math.Max(1, run.Frames):0.0} ms, worst {1000.0 * run.LongestFrame:0.0} ms.",
				$"Worst frame: {(run.WorstFrameGcGeneration >= 0 ? $"a gen-{run.WorstFrameGcGeneration} GC ran in it" : "no GC")}; longest visibility pass {ObserverStreamingRegistry.LongestPassMilliseconds:0.0} ms; longest RPC-link reindex {ServerTrafficBreakdown.LongestReindexMilliseconds:0.0} ms.",
				$"Reconciles: {reconcilesSent / seconds / players:0.0}/s per player sent, {quietSkipped / seconds / players:0.0}/s skipped as quiet; " +
					$"players on the idle send interval {(playerTicks > 0 ? 100.0 * idleTicks / playerTicks : 0):0}% of the time.",
				$"Idle players' reconciles that changed anyway, by what changed: {DescribeQuietMisses(run.QuietMissAtStart)}.",
				$"Byte budget {(ObserverStreamingPolicy.ByteBudget > 0 ? $"{ObserverStreamingPolicy.ByteBudget} B/s per viewer" : "off")}; last pass left out {ObserverStreamingRegistry.LastPassByteEvicted}, slowed {ObserverStreamingRegistry.LastPassByteSlowed}.",
			};
			lines.AddRange(compressionSummary);
			lines.Add("Game data per player by kind (top):");
			lines.AddRange(summary);

			// Removed here so a timed run leaves nothing behind; any untimed simulated players stay.
			foreach (NetworkObject n in run.Spawned)
			{
				if (n != null && n.IsSpawned && networkManager != null)
				{
					networkManager.ServerManager.Despawn(n, DespawnType.Pool);
				}
				simulatedPlayers.Remove(n);
			}
			lines.Add($"Removed the run's {alive} simulated player(s).");

			_ = Log.Info("SceneServerSystem", $"Simulation run by {run.AdminName}:\n" + string.Join("\n", lines) + "\n" + full + compressionSample);
			IPlayerCharacter admin = run.Admin;
			if (admin != null && admin.NetworkObject != null && admin.NetworkObject.IsSpawned)
			{
				foreach (string line in lines)
				{
					Reply(admin, line.Length <= ChatBroadcast.MaxTextLength ? line : line.Substring(0, ChatBroadcast.MaxTextLength));
				}
			}
		}

		/// <summary>The groups that kept idle players' reconciles from being skipped over the run, largest first.</summary>
		private static string DescribeQuietMisses(long[] atStart)
		{
			var parts = new List<(string Name, long Count)>();
			for (int i = 0; i < CharacterPredictionController.QuietMissCounts.Length; ++i)
			{
				long count = CharacterPredictionController.QuietMissCounts[i] - (atStart != null && i < atStart.Length ? atStart[i] : 0);
				if (count > 0)
				{
					parts.Add((CharacterPredictionController.QuietMissNames[i], count));
				}
			}
			if (parts.Count == 0)
			{
				return "none";
			}
			parts.Sort((x, y) => y.Count.CompareTo(x.Count));
			return string.Join(", ", parts.ConvertAll(p => $"{p.Name} {p.Count}"));
		}

		/// <summary>Forgets entries that are gone (died into the pool, or destroyed).</summary>
		private void PruneSimulatedPlayers()
		{
			simulatedPlayers.RemoveAll(n => n == null || !n.IsSpawned);
		}

		private static NetworkObject FindSimulatedPlayerPrefab(AIBrainCatalogue catalogue)
		{
			foreach (AIBrainCatalogue.Entry entry in catalogue.Entries)
			{
				if (entry?.Prefab != null && entry.Prefab.name == SimulatedPlayerPrefabName)
				{
					return entry.Prefab;
				}
			}
			return null;
		}
	}
}
