using System;
using System.Collections.Generic;
using System.Globalization;
using FishNet.Transporting.WebTransport;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;
using UnityEngine;
using UnityEngine.AI;
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
	/// Modes, for bandwidth tests: <c>walk</c> (the default) wanders; <c>combat</c> splits them into two teams that fight
	/// with real combat NPCs' kits and brains, so casts, projectiles, combat reports, buffs and health bars are in the
	/// traffic; a radius spreads them out to test streaming range and the per-viewer budgets instead of one dense crowd;
	/// <c>chat</c> has each say a line in local chat every 20 to 40 s, as a talkative player would.
	/// </para>
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
	public partial class StaffSystem
	{
		private const string SimulatedPlayerPrefabName = "Simulated Player";
		private const int MaxSimulatedPlayersPerCommand = 200;
		private const int MaxSimulatedPlayers = 2000;
		private const float SimulatedPlayerSpawnRadius = 25f;
		private const int MinSimulatedSpawnRadius = 5;
		private const int MaxSimulatedSpawnRadius = 1000;
		/// <summary>Seconds before a fallen combat simulated player is replaced, so a fight keeps its numbers.</summary>
		private const double SimulatedRespawnSeconds = 6.0;
		/// <summary>How often the teams are paired up, the dead counted and the chatter run.</summary>
		private const double SimulatedUpkeepSeconds = 1.0;
		private const float SimulatedChatMinSeconds = 20f;
		private const float SimulatedChatMaxSeconds = 40f;

		internal enum SimulatedActivity { Walk, Combat }

		/// <summary>What a batch of simulated players does: how they behave, how far out they stand, whether they talk.</summary>
		internal sealed class SimulatedSettings
		{
			public SimulatedActivity Activity = SimulatedActivity.Walk;
			public float Radius = SimulatedPlayerSpawnRadius;
			public bool Chat;

			public string Describe() =>
				$"{(Activity == SimulatedActivity.Combat ? "combat, two teams" : "walking")}, within {Radius:0} m{(Chat ? ", chatting" : string.Empty)}";
		}

		/// <summary>One simulated player the upkeep looks after: its team, its chatter, its replacement when it falls.</summary>
		private sealed class SimulatedMember
		{
			public NetworkObject Instance;
			public NPC Npc;
			public AIController Ai;
			public SimulatedSettings Settings;
			public int Number;
			public int Team;
			public Vector3 Origin;
			public Scene Scene;
			public SimulationRun Run;
			public double NextChatAt;
			public double DiedAt = -1.0;
		}

		/// <summary>A real combat NPC's ability kit and brain, which a combat simulated player borrows.</summary>
		private sealed class SimulatedCombatKit
		{
			public AIArchetypeTemplate Archetype;
			public List<CachedScriptableObject> Abilities;
		}

		private readonly List<SimulatedMember> simulatedMembers = new List<SimulatedMember>();
		private List<SimulatedCombatKit> simulatedCombatKits;
		private int simulatedNumber;
		private double nextSimulatedUpkeepAt;

		private static readonly string[] SimulatedChatLines =
		{
			"Anyone heading to the market?",
			"Watch the ridge, there are wolves about.",
			"Selling iron ore, fair price.",
			"Has anyone seen the ferryman?",
			"Need one more for the crypt.",
			"The smith is out of nails again.",
			"Fine weather for the road.",
			"Who left the gate open?",
			"Looking for a guild that raids.",
			"Mind the bridge, it creaks.",
			"Bring torches, the mine is dark.",
			"Thanks for the heal, friend.",
		};

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
			public SimulatedSettings Settings;
			/// <summary>Combat upkeep and chatter over the window: pairings made, deaths, replacements, chat lines said.</summary>
			public long Pairings, Deaths, Respawns, ChatLines;
			public int RealPlayersAtStart;
			public TransportTrafficSnapshot TrafficAtStart;
			/// <summary>Reconciles written and skipped as quiet, and player-ticks at the idle send interval, at the start.</summary>
			public long ReconcilesAtStart, QuietAtStart, PlayerTicksAtStart, IdleTicksAtStart;
			/// <summary>Hit claims granted and refused, and casts held or cancelled for missing input, when the window opened.</summary>
			public long HitClaimsGrantedAtStart, HitClaimsRefusedAtStart, HitClaimsAgreedAtStart, TickGuardRefusedAtStart, BlankCastTicksAtStart, BlankCastCancelsAtStart;
			/// <summary>Per state group, idle owners' reconciles that changed (CharacterPredictionController.QuietMissCounts) at the start.</summary>
			public long[] QuietMissAtStart;
			/// <summary>Process CPU time at the window's start; null where it cannot be read.</summary>
			public TimeSpan? CpuAtStart;
			/// <summary>Reconcile write counters at the start (ReconcileWireStats).</summary>
			public ReconcileWireStats.Snapshot ReconcileAtStart;
			/// <summary>NetworkTransformFrames counters at the start: updates written, rotation-only, queued, payload and frame bytes, frames.</summary>
			public long[] FramesAtStart;
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
		/// Where the players' reconcile bytes went (full snapshots against deltas, each delta section's share) and what the
		/// run's spawn messages were made of (header, behaviour payloads and the biggest of them, RPC links, SyncTypes).
		/// </summary>
		/// <summary>NetworkTransformFrames' counters, in the order SimulationRun.FramesAtStart keeps them.</summary>
		private static long[] FrameCounters()
		{
			return new[]
			{
				FishNet.Component.Transforming.NetworkTransformFrames.UpdatesWritten,
				FishNet.Component.Transforming.NetworkTransformFrames.RotationOnlyUpdates,
				FishNet.Component.Transforming.NetworkTransformFrames.UpdatesQueued,
				FishNet.Component.Transforming.NetworkTransformFrames.PayloadBytesQueued,
				FishNet.Component.Transforming.NetworkTransformFrames.FrameBytesWritten,
				FishNet.Component.Transforming.NetworkTransformFrames.FramesWritten,
				CombatEffectAudience.FilteredOut,
			};
		}

		private static List<string> DescribeReconcileAndSpawnBytes(SimulationRun run, double seconds)
		{
			var lines = new List<string>();
			if (run.FramesAtStart != null)
			{
				long[] counters = FrameCounters();
				long written = counters[0] - run.FramesAtStart[0], rotationOnly = counters[1] - run.FramesAtStart[1];
				long queued = counters[2] - run.FramesAtStart[2], payload = counters[3] - run.FramesAtStart[3];
				long frameBytes = counters[4] - run.FramesAtStart[4], frames = counters[5] - run.FramesAtStart[5];
				if (queued > 0)
				{
					lines.Add($"Movement updates: {written / seconds:0}/s written ({(written > 0 ? 100.0 * rotationOnly / written : 0):0}% rotation only, " +
						$"sent from {FishNet.Component.Transforming.NetworkTransform.RotationSensitivity:0.##} degree(s)), {queued / seconds:0}/s to observers; " +
						$"avg {payload / (double)queued:0.0} B data + {(frameBytes - payload) / (double)queued:0.0} B framing per update ({frames / seconds:0} frames/s).");
				}
				long combatFiltered = counters[6] - run.FramesAtStart[6];
				lines.Add(ObserverStreamingPolicy.CombatEffectRange > 0f
					? $"Combat effects: {combatFiltered / seconds:0.0}/s cast and ability-object messages kept from observers beyond {ObserverStreamingPolicy.CombatEffectRange:0} m."
					: "Combat effects: sent to every observer (ObserverCombatEffectRange 0).");
			}
			ReconcileWireStats.Snapshot now = ReconcileWireStats.Take();
			ReconcileWireStats.Snapshot start = run.ReconcileAtStart;
			long fulls = now.FullCount - start.FullCount, fullBytes = now.FullBytes - start.FullBytes;
			long deltas = now.DeltaCount - start.DeltaCount, deltaBytes = now.DeltaBytes - start.DeltaBytes;
			lines.Add($"Reconcile writes: {fulls} full (avg {(fulls > 0 ? fullBytes / (double)fulls : 0):0} B, {fullBytes / 1024.0:0} KiB), " +
				$"{deltas} deltas (avg {(deltas > 0 ? deltaBytes / (double)deltas : 0):0} B, {deltaBytes / 1024.0:0} KiB).");
			if (deltaBytes > 0 && start.SectionBytes != null)
			{
				var shares = new List<string>();
				for (int i = 0; i < ReconcileWireStats.SectionCount; i++)
				{
					long bytes = now.SectionBytes[i] - start.SectionBytes[i];
					if (bytes > 0)
					{
						shares.Add($"{(ReconcileWireStats.Section)i} {100.0 * bytes / deltaBytes:0}%");
					}
				}
				lines.Add("  Delta bytes by section: " + string.Join(", ", shares) + ".");
			}

			SpawnWireStats.Enabled = false;
			long spawns = SpawnWireStats.Count;
			if (spawns > 0)
			{
				lines.Add($"Spawns written: {spawns}, avg {(SpawnWireStats.HeaderBytes + SpawnWireStats.PayloadBytes + SpawnWireStats.RpcLinkBytes + SpawnWireStats.SyncTypeBytes) / (double)spawns:0} B: " +
					$"header {SpawnWireStats.HeaderBytes / (double)spawns:0}, payload {SpawnWireStats.PayloadBytes / (double)spawns:0}, " +
					$"RPC links {SpawnWireStats.RpcLinkBytes / (double)spawns:0}, SyncTypes {SpawnWireStats.SyncTypeBytes / (double)spawns:0}.");
				var byBehaviour = new List<KeyValuePair<Type, long>>(SpawnWireStats.PayloadBytesByBehaviour);
				byBehaviour.Sort((a, b) => b.Value.CompareTo(a.Value));
				var top = new List<string>();
				for (int i = 0; i < byBehaviour.Count && i < 8; i++)
				{
					top.Add($"{byBehaviour[i].Key.Name} {byBehaviour[i].Value / (double)spawns:0}");
				}
				lines.Add("  Payload per spawn by behaviour (B): " + string.Join(", ", top) + ".");
			}
			return lines;
		}

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
					Summary = "Spawns simulated players: walk or combat (two teams), within a radius, chat optional. Timed: a traffic and CPU report.",
					Arguments = "count:Integer;seconds:Integer?;mode:Choice=walk,combat?;radius:Integer?;chat:Choice=chat?",
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
			if (!TryParseSimulationOptions(words, out int seconds, out SimulatedSettings settings, out string problem))
			{
				Reply(character, problem);
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
				Settings = settings,
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
				Settings = settings,
			};
			if (run != null)
			{
				simulationRunPending = true;
			}
			Reply(character, $"Spawning {count} simulated player(s), {settings.Describe()}, {SimulatedSpawnsPerFrame} a frame{(run != null ? $"; the {seconds} s measurement starts when the last is in" : string.Empty)}.");
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
			public SimulatedSettings Settings;
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
				int number = ++simulatedNumber;
				if (SpawnSimulatedMember(batch.NetworkManager, batch.Host, batch.Prefab, batch.Scene, batch.Origin, batch.Settings, number, number % 2, batch.Run) != null)
				{
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
				run.HitClaimsGrantedAtStart = AbilityController.HitClaimsGranted;
				run.HitClaimsAgreedAtStart = AbilityController.HitClaimsAgreed;
				run.TickGuardRefusedAtStart = FishNet.Object.Prediction.ReplicateTickGuard.Refused;
				run.HitClaimsRefusedAtStart = AbilityController.HitClaimsRefused;
				run.BlankCastTicksAtStart = AbilityController.BlankCastTicks;
				run.BlankCastCancelsAtStart = AbilityController.BlankCastCancels;
				run.PlayerTicksAtStart = sceneServer.ScheduledPlayerTicks;
				run.IdleTicksAtStart = sceneServer.IdleIntervalPlayerTicks;
				run.QuietMissAtStart = (long[])CharacterPredictionController.QuietMissCounts.Clone();
				run.CpuAtStart = TryReadProcessCpuTime(out TimeSpan cpuNow) ? cpuNow : (TimeSpan?)null;
				run.Gc0 = GC.CollectionCount(0);
				run.Gc1 = GC.CollectionCount(1);
				run.Gc2 = GC.CollectionCount(2);
				ObserverStreamingRegistry.ResetLongestPass();
				ServerTrafficBreakdown.ResetLongestReindex();
				run.ReconcileAtStart = ReconcileWireStats.Take();
				run.FramesAtStart = FrameCounters();
				SpawnWireStats.Reset();
				SpawnWireStats.Enabled = true;
				run.Breakdown = ServerTrafficBreakdown.BeginWindow(networkManager);
				run.Compression = new PacketCompressionSampler();
				run.Compression.Start();
				simulationRun = run;
				Reply(character, $"Measuring {spawned} simulated player(s) for {seconds} s; the report follows here and in the server log. /admin simclear ends it early.");
				_ = Log.Info("SceneServerSystem", $"{character.CharacterName} started a {seconds} s simulation run of {spawned} ({run.Settings.Describe()}) in '{scene.name}'.");
				return;
			}
			string capped = spawned < requested ? $" (asked for {requested}; at most {MaxSimulatedPlayersPerCommand} a command and {MaxSimulatedPlayers} in all)" : string.Empty;
			Reply(character, $"Spawned {spawned} simulated player(s), {batch.Settings.Describe()}{capped}. {simulatedPlayers.Count} running on this server.");
			_ = Log.Info("SceneServerSystem", $"{character.CharacterName} spawned {spawned} simulated players in '{scene.name}'; {simulatedPlayers.Count} running.");
		}

		/// <summary>One simulated player, spawned as a boss spawns an add; rolled back into the pool if anything interrupts it.</summary>
		private static bool TrySpawnSimulatedPlayer(NetworkManager networkManager, AIBrainHost host, NetworkObject prefab, Scene scene, Vector3 position, Quaternion rotation, SimulatedCombatKit kit, out NetworkObject instance)
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
					/* The kit is set on every spawn, not only a combat one: the instance comes from a pool, and a walker must
					 * not keep the kit a fighter left on it. Equipped when the spawn starts it on the server. */
					NPC authored = prefab.GetComponent<NPC>();
					List<CachedScriptableObject> abilities = kit != null ? kit.Abilities : authored != null ? authored.Abilities : null;
					npc.Abilities = abilities != null ? new List<CachedScriptableObject>(abilities) : new List<CachedScriptableObject>();
					// Warped onto the NavMesh at its spot, which becomes the home it wanders around; a fighter thinks as its kit's NPC does.
					host.Prepare(npc, position, kit?.Archetype);
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
			simulatedMembers.Clear();
			return removed;
		}

		/// <summary>Each frame: the run's frame timing, and its end on the second.</summary>
		private void UpdateSimulationRun(float deltaTime)
		{
			SpawnPendingSimulatedPlayers();
			UpkeepSimulatedMembers();
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
			long claimsGranted = AbilityController.HitClaimsGranted - run.HitClaimsGrantedAtStart;
			long claimsRefused = AbilityController.HitClaimsRefused - run.HitClaimsRefusedAtStart;
			long claimsAgreed = AbilityController.HitClaimsAgreed - run.HitClaimsAgreedAtStart;
			long tickGuardRefused = FishNet.Object.Prediction.ReplicateTickGuard.Refused - run.TickGuardRefusedAtStart;
			long blankCastTicks = AbilityController.BlankCastTicks - run.BlankCastTicksAtStart;
			long blankCastCancels = AbilityController.BlankCastCancels - run.BlankCastCancelsAtStart;
			long playerTicks = sceneServer.ScheduledPlayerTicks - run.PlayerTicksAtStart;
			long idleTicks = sceneServer.IdleIntervalPlayerTicks - run.IdleTicksAtStart;
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
				$"Simulation {(early ? "ended early" : "done")}: {run.Spawned.Count} simulated ({alive} still up; {run.Settings?.Describe()}), {players} real player(s), {seconds:0} s in '{run.SceneName}'.",
				$"Simulated activity: {run.Pairings} pairings, {run.Deaths} fell, {run.Respawns} replaced; {run.ChatLines} chat lines ({run.ChatLines / seconds:0.00}/s).",
				$"Server egress (measured): {wireBytes / seconds / 1000.0:0.0} kB/s wire, {datagrams / seconds:0} packets/s; per player {wireBytes / seconds / players / 1000.0:0.00} kB/s, {datagrams / seconds / players:0} pkt/s.",
				$"  UDP payload {udpBytes / seconds / 1000.0:0.0} kB/s, game data {appBytes / seconds / 1000.0:0.0} kB/s (overhead x{(appBytes > 0 ? (double)wireBytes / appBytes : 0):0.00}).",
				$"Server ingress (measured): {wireRecvBytes / seconds / 1000.0:0.0} kB/s wire, {datagramsIn / seconds:0} packets/s; per player {wireRecvBytes / seconds / players / 1000.0:0.00} kB/s, {datagramsIn / seconds / players:0} pkt/s.",
				$"  UDP payload {udpRecvBytes / seconds / 1000.0:0.0} kB/s, game data {appRecvBytes / seconds / 1000.0:0.0} kB/s in {appRecvMessages / seconds / players:0} msg/s per player; the rest is QUIC acks and headers.",
				$"Server CPU: {cpuText}; frames avg {1000.0 * run.FrameSeconds / Math.Max(1, run.Frames):0.0} ms, worst {1000.0 * run.LongestFrame:0.0} ms.",
				$"Worst frame: {(run.WorstFrameGcGeneration >= 0 ? $"a gen-{run.WorstFrameGcGeneration} GC ran in it" : "no GC")}; longest visibility pass {ObserverStreamingRegistry.LongestPassMilliseconds:0.0} ms; longest RPC-link reindex {ServerTrafficBreakdown.LongestReindexMilliseconds:0.0} ms.",
				$"Reconciles: {reconcilesSent / seconds / players:0.0}/s per player sent, {quietSkipped / seconds / players:0.0}/s skipped as quiet; " +
					$"players on the idle send interval {(playerTicks > 0 ? 100.0 * idleTicks / playerTicks : 0):0}% of the time.",
				$"Idle players' reconciles that changed anyway, by what changed: {DescribeQuietMisses(run.QuietMissAtStart)}.",
				// Granted = a hit the owner saw that the server's own rewound sweep had not made; refused = outside what it could have seen.
				$"Hit claims: {claimsAgreed} agreed (the server had made the hit), {claimsGranted} granted, {claimsRefused} refused; inputs refused by the tick guard {tickGuardRefused}; casts held for late input on {blankCastTicks} tick(s), {blankCastCancels} cancelled for input that never came.",
				$"Byte budget {(ObserverStreamingPolicy.ByteBudget > 0 ? $"{ObserverStreamingPolicy.ByteBudget} B/s per viewer" : "off")}; last pass left out {ObserverStreamingRegistry.LastPassByteEvicted}, slowed {ObserverStreamingRegistry.LastPassByteSlowed}.",
			};
			lines.AddRange(compressionSummary);
			lines.AddRange(DescribeReconcileAndSpawnBytes(run, seconds));
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
			simulatedMembers.RemoveAll(m => m.Run == run);
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

		/// <summary>
		/// Reads what follows the count, in any order: up to two numbers (the run's seconds, 0 for untimed, then the radius in
		/// metres) and the words <c>walk</c>, <c>combat</c> and <c>chat</c>.
		/// </summary>
		internal static bool TryParseSimulationOptions(string[] words, out int seconds, out SimulatedSettings settings, out string problem)
		{
			seconds = 0;
			settings = new SimulatedSettings();
			problem = null;
			int numbers = 0;
			for (int i = 1; i < words.Length; ++i)
			{
				string word = words[i].ToLowerInvariant();
				if (int.TryParse(word, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
				{
					if (numbers == 0)
					{
						if (value < 0 || value > MaxSimulationRunSeconds)
						{
							problem = $"The run length must be 0 (untimed) to {MaxSimulationRunSeconds} seconds.";
							return false;
						}
						seconds = value;
					}
					else if (numbers == 1)
					{
						if (value < MinSimulatedSpawnRadius || value > MaxSimulatedSpawnRadius)
						{
							problem = $"The radius must be {MinSimulatedSpawnRadius} to {MaxSimulatedSpawnRadius} metres.";
							return false;
						}
						settings.Radius = value;
					}
					else
					{
						problem = SimulationUsage;
						return false;
					}
					numbers++;
					continue;
				}
				switch (word)
				{
					case "walk":
						settings.Activity = SimulatedActivity.Walk;
						break;
					case "combat":
					case "fight":
						settings.Activity = SimulatedActivity.Combat;
						break;
					case "chat":
						settings.Chat = true;
						break;
					default:
						problem = SimulationUsage;
						return false;
				}
			}
			return true;
		}

		private const string SimulationUsage =
			"/admin simulate <count> [seconds, 0 untimed] [walk|combat] [radius m] [chat], e.g. /admin simulate 100 120 combat 150 chat.";

		/// <summary>
		/// A point on the NavMesh within the radius of the origin, the ground's height found by the NavMesh rather than
		/// assumed level with the admin. False when none of a few tries lands on it (open sea, say).
		/// </summary>
		private static bool TryPickSimulatedSpawnPoint(Vector3 origin, float radius, out Vector3 point)
		{
			float search = Mathf.Clamp(10f + radius * 0.2f, 10f, 200f);
			for (int attempt = 0; attempt < 6; ++attempt)
			{
				Vector2 offset = UnityEngine.Random.insideUnitCircle * radius;
				Vector3 candidate = origin + new Vector3(offset.x, 0f, offset.y);
				if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, search, AIController.LandAreas))
				{
					point = hit.position;
					return true;
				}
			}
			point = origin;
			return false;
		}

		/// <summary>
		/// The kits of the catalogue's combat NPCs (the "Enemy - " archetypes with abilities), one per archetype, found once.
		/// </summary>
		private List<SimulatedCombatKit> SimulatedCombatKits(AIBrainCatalogue catalogue)
		{
			if (simulatedCombatKits != null)
			{
				return simulatedCombatKits;
			}
			simulatedCombatKits = new List<SimulatedCombatKit>();
			if (catalogue == null)
			{
				return simulatedCombatKits;
			}
			foreach (AIBrainCatalogue.Entry entry in catalogue.Entries)
			{
				if (entry?.Prefab == null || entry.Archetype == null || !entry.Archetype.name.StartsWith("Enemy - ", StringComparison.Ordinal))
				{
					continue;
				}
				NPC authored = entry.Prefab.GetComponent<NPC>();
				if (authored == null || authored.Abilities == null || authored.Abilities.Count == 0 ||
					simulatedCombatKits.Exists(k => k.Archetype == entry.Archetype))
				{
					continue;
				}
				simulatedCombatKits.Add(new SimulatedCombatKit { Archetype = entry.Archetype, Abilities = authored.Abilities });
			}
			return simulatedCombatKits;
		}

		/// <summary>Spawns one simulated player with its batch's settings and starts looking after it. Null when it could not.</summary>
		private SimulatedMember SpawnSimulatedMember(NetworkManager networkManager, AIBrainHost host, NetworkObject prefab, Scene scene,
			Vector3 origin, SimulatedSettings settings, int number, int team, SimulationRun run)
		{
			if (!TryPickSimulatedSpawnPoint(origin, settings.Radius, out Vector3 position))
			{
				return null;
			}
			SimulatedCombatKit kit = null;
			if (settings.Activity == SimulatedActivity.Combat)
			{
				List<SimulatedCombatKit> kits = SimulatedCombatKits(host.Catalogue);
				// Roles spread across both teams: consecutive numbers alternate teams, so each team gets every role in turn.
				kit = kits.Count > 0 ? kits[(number / 2) % kits.Count] : null;
			}
			Quaternion rotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
			if (!TrySpawnSimulatedPlayer(networkManager, host, prefab, scene, position, rotation, kit, out NetworkObject instance))
			{
				return null;
			}
			NPC npc = instance.GetComponent<NPC>();
			/* Aggression is the game's own way to strike a neutral: both teams are one faction, and the harm rule otherwise lets
			 * every blow land for nothing. Set either way, since a pooled instance may carry a fighter's. */
			if (npc != null && npc.TryGet(out IFactionController faction))
			{
				faction.TrySetAggression(settings.Activity == SimulatedActivity.Combat, out _);
			}
			SimulatedMember member = new SimulatedMember
			{
				Instance = instance,
				Npc = npc,
				Ai = instance.GetComponent<AIController>(),
				Settings = settings,
				Number = number,
				Team = team,
				Origin = origin,
				Scene = scene,
				Run = run,
				NextChatAt = MonotonicClock.NowSeconds + UnityEngine.Random.Range(1f, SimulatedChatMaxSeconds),
			};
			simulatedPlayers.Add(instance);
			run?.Spawned.Add(instance);
			if (settings.Activity == SimulatedActivity.Combat || settings.Chat)
			{
				simulatedMembers.Add(member);
			}
			return member;
		}

		private static bool IsSimulatedMemberUp(SimulatedMember member)
		{
			return member.Instance != null && member.Instance.IsSpawned && member.Npc != null && !member.Npc.IsFlagged(CharacterFlags.IsDead);
		}

		/// <summary>
		/// Once a second: pairs every idle fighter with the nearest one of the other team, replaces the fallen after a few
		/// seconds, and lets the chatty speak.
		/// </summary>
		private void UpkeepSimulatedMembers()
		{
			if (simulatedMembers.Count == 0)
			{
				return;
			}
			double now = MonotonicClock.NowSeconds;
			if (now < nextSimulatedUpkeepAt)
			{
				return;
			}
			nextSimulatedUpkeepAt = now + SimulatedUpkeepSeconds;

			NetworkManager networkManager = Server?.NetworkWrapper?.NetworkManager;
			AIBrainHost host = null;
			NetworkObject prefab = null;
			if (networkManager != null && AIBrainHost.TryGet(networkManager, out host) && host.Catalogue != null)
			{
				prefab = FindSimulatedPlayerPrefab(host.Catalogue);
			}

			for (int i = simulatedMembers.Count - 1; i >= 0; --i)
			{
				SimulatedMember member = simulatedMembers[i];
				if (IsSimulatedMemberUp(member))
				{
					continue;
				}
				if (member.DiedAt < 0.0)
				{
					member.DiedAt = now;
					if (member.Run != null)
					{
						member.Run.Deaths++;
					}
					/* Out of the list now: the corpse is the death system's to decay and pool, and a clear must not despawn
					 * an instance that system still owns. */
					simulatedPlayers.Remove(member.Instance);
					member.Run?.Spawned.Remove(member.Instance);
				}
				if (member.Settings.Activity != SimulatedActivity.Combat)
				{
					// Only a fight replaces its fallen; anything else that died is simply no longer looked after.
					simulatedMembers.RemoveAt(i);
					continue;
				}
				if (now - member.DiedAt < SimulatedRespawnSeconds)
				{
					continue;
				}
				simulatedMembers.RemoveAt(i);
				if (prefab == null || !member.Scene.IsValid() || !member.Scene.isLoaded || simulatedPlayers.Count >= MaxSimulatedPlayers)
				{
					continue;
				}
				SimulatedMember replacement = SpawnSimulatedMember(networkManager, host, prefab, member.Scene, member.Origin, member.Settings,
					++simulatedNumber, member.Team, member.Run);
				if (replacement != null && member.Run != null)
				{
					member.Run.Respawns++;
				}
			}

			for (int i = 0; i < simulatedMembers.Count; ++i)
			{
				SimulatedMember member = simulatedMembers[i];
				if (!IsSimulatedMemberUp(member))
				{
					continue;
				}
				if (member.Settings.Activity == SimulatedActivity.Combat && member.Ai != null && member.Ai.Target == null)
				{
					SimulatedMember enemy = NearestSimulatedEnemy(member);
					if (enemy != null)
					{
						member.Ai.OnThreatReceived(enemy.Npc);
						if (member.Run != null)
						{
							member.Run.Pairings++;
						}
					}
				}
				if (member.Settings.Chat && now >= member.NextChatAt)
				{
					member.NextChatAt = now + UnityEngine.Random.Range(SimulatedChatMinSeconds, SimulatedChatMaxSeconds);
					SaySimulatedLine(member);
				}
			}
		}

		/// <summary>The nearest standing member of the other team in the same scene, within reach of the batch's spread.</summary>
		private SimulatedMember NearestSimulatedEnemy(SimulatedMember of)
		{
			Vector3 position = of.Instance.transform.position;
			float reach = of.Settings.Radius * 2f + 30f;
			float best = reach * reach;
			SimulatedMember nearest = null;
			for (int i = 0; i < simulatedMembers.Count; ++i)
			{
				SimulatedMember candidate = simulatedMembers[i];
				if (candidate == of || candidate.Team == of.Team || candidate.Settings.Activity != SimulatedActivity.Combat ||
					candidate.Scene != of.Scene || !IsSimulatedMemberUp(candidate))
				{
					continue;
				}
				float distance = (candidate.Instance.transform.position - position).sqrMagnitude;
				if (distance < best)
				{
					best = distance;
					nearest = candidate;
				}
			}
			return nearest;
		}

		/// <summary>
		/// One line of local chat, sent as a player's Say is: once, to everyone observing the speaker. Sender 0, so clients show
		/// it without asking the server for a name, and sent here rather than through the chat channels so it never reaches the
		/// chat log.
		/// </summary>
		private void SaySimulatedLine(SimulatedMember member)
		{
			HashSet<FishNet.Connection.NetworkConnection> observers = member.Instance.Observers;
			if (observers == null || observers.Count == 0 || Server?.NetworkWrapper == null)
			{
				return;
			}
			string line = SimulatedChatLines[UnityEngine.Random.Range(0, SimulatedChatLines.Length)];
			Server.NetworkWrapper.Broadcast(observers, new ChatBroadcast
			{
				Channel = ChatChannel.Say,
				SenderID = 0,
				Text = $"[Sim {member.Number}] {line}",
			}, true, FishNet.Transporting.Channel.Reliable);
			if (member.Run != null)
			{
				member.Run.ChatLines++;
			}
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
