using System;
using System.Collections.Generic;
using FishMMO.Logging;
using FishNet.Managing;
using FishNet.Managing.Scened;
using UnityEngine.SceneManagement;
using FishNetSceneManager = FishNet.Managing.Scened.SceneManager;

namespace FishMMO.Server.Implementation.World.SceneServer.Spawner
{
	/// <summary>
	/// Runs the baked spawners of every world scene instance loaded under one
	/// <see cref="FishNet.Managing.NetworkManager"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// When a scene finishes loading on the server, its <see cref="SceneSpawnTable"/> is looked up
	/// by name and one <see cref="SpawnerRuntime"/> is created per definition, for that scene
	/// instance alone; stacked instances of one scene each get their own. When the scene unloads,
	/// its spawners are stopped and dropped.
	/// </para>
	/// <para>
	/// <b>Started a frame late, on purpose.</b> The scene server's own load handler publishes the
	/// instance's difficulty rules — which every NPC reads as it spawns — and unloads scenes whose
	/// request has gone away. Both listen to the same FishNet event as this host, in whatever order
	/// the systems happened to initialise. Deferring the start to the next update makes the order
	/// irrelevant: by then the rules are published, and an orphan is already gone.
	/// </para>
	/// <para>
	/// <b>And spread over frames.</b> Starting a spawner prewarms its pool and spawns its initial
	/// population — instantiation, a ground cast, a NavMesh warp and a network spawn per object —
	/// and a scene used to start every spawner it has on one frame: three hundred spawners with
	/// three initial spawns each is nine hundred spawns and the prewarm behind them. Starts now
	/// draw on <see cref="StartWorkPerFrame"/>, a budget of objects made per frame, scene after
	/// scene in the order they loaded. A scene enters the respawn schedule only once every one of
	/// its spawners has started, so a respawn condition naming a sibling never sees that sibling
	/// before its initial population exists — which starting them all on one frame used to
	/// guarantee by accident.
	/// </para>
	/// <para>
	/// A plain class rather than a server behaviour so tests and simulations can run it on a bare
	/// FishNet server; <see cref="SpawnerSystem"/> is the production wrapper.
	/// </para>
	/// </remarks>
	public sealed class SpawnerHost
	{
		/// <summary>
		/// Default for <see cref="StartWorkPerFrame"/>.
		/// </summary>
		public const int DefaultStartWorkPerFrame = 32;

		/// <summary>
		/// One scene instance whose spawners are being started, a few per frame.
		/// </summary>
		private sealed class SceneStart
		{
			public Scene Scene;
			public List<SpawnerRuntime> Spawners;
			/// <summary>The next spawner to start.</summary>
			public int Next;
		}

		/// <summary>
		/// The network manager whose scenes this host populates.
		/// </summary>
		public NetworkManager NetworkManager { get; }

		/// <summary>
		/// Every world scene's baked spawn table.
		/// </summary>
		public SpawnTableCatalogue Catalogue { get; set; }

		/// <summary>
		/// The scheduler that drives every spawner this host runs.
		/// </summary>
		public SpawnerScheduler Scheduler { get; } = new SpawnerScheduler();

		/// <summary>
		/// True between <see cref="Start"/> and <see cref="Stop"/>.
		/// </summary>
		public bool Running { get; private set; }

		/// <summary>
		/// Objects spawner starts may make per frame: pooled instances the prewarm creates plus
		/// initial spawns, plus one per spawner.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A token bucket. Each frame adds this much, up to this much; each start spends what it
		/// actually did, which can take the balance below zero — one spawner's start is never cut
		/// in half — and the frames after it start nothing until the debt is paid.
		/// </para>
		/// <para>
		/// <b>It cannot starve a scene.</b> The debt a start can run up is bounded by that one
		/// spawner's own prewarm and initial count, the balance recovers by this much every frame,
		/// and whenever it is positive the next spawner in load order starts. Every queued spawner
		/// therefore starts within a bounded number of frames, and every queued scene enters the
		/// schedule. Respawns have their own budget
		/// (<see cref="SpawnerScheduler.SpawnsPerFrame"/>), so a run of scene loads cannot hold
		/// them up either, nor they the loads. At least 1.
		/// </para>
		/// </remarks>
		public int StartWorkPerFrame
		{
			get => startWorkPerFrame;
			set => startWorkPerFrame = Math.Max(1, value);
		}

		private int startWorkPerFrame = DefaultStartWorkPerFrame;

		/// <summary>The start budget's balance; negative while a heavy start is being paid off.</summary>
		private int startBalance;

		/// <summary>
		/// Running spawners by scene handle.
		/// </summary>
		private readonly Dictionary<int, List<SpawnerRuntime>> byScene = new Dictionary<int, List<SpawnerRuntime>>();

		/// <summary>
		/// Scenes that finished loading and are picked up on the next <see cref="Tick"/>.
		/// </summary>
		private readonly List<Scene> pendingScenes = new List<Scene>();

		/// <summary>
		/// Scenes whose spawners are being started, in load order.
		/// </summary>
		private readonly List<SceneStart> starting = new List<SceneStart>();

		/// <summary>
		/// The scene manager subscribed to, kept so the subscription is released from the same one.
		/// </summary>
		private FishNetSceneManager sceneManager;

		/// <summary>
		/// Number of scene instances with running spawners. Diagnostics.
		/// </summary>
		public int SceneCount => byScene.Count;

		/// <summary>
		/// Number of scene instances whose spawners are still being started. Diagnostics and tests.
		/// </summary>
		public int StartingSceneCount => starting.Count;

		/// <summary>
		/// Creates a host. Nothing happens until <see cref="Start"/>.
		/// </summary>
		/// <param name="networkManager">The server's network manager.</param>
		/// <param name="catalogue">Every world scene's baked spawn table.</param>
		public SpawnerHost(NetworkManager networkManager, SpawnTableCatalogue catalogue)
		{
			NetworkManager = networkManager;
			Catalogue = catalogue;
		}

		/// <summary>
		/// Starts following scene loads and unloads.
		/// </summary>
		public void Start()
		{
			if (Running || NetworkManager == null)
			{
				return;
			}

			sceneManager = NetworkManager.SceneManager;
			if (sceneManager != null)
			{
				sceneManager.OnLoadEnd += OnLoadEnd;
				sceneManager.OnUnloadEnd += OnUnloadEnd;
			}
			Running = true;
		}

		/// <summary>
		/// Stops every spawner and stops following scenes.
		/// </summary>
		public void Stop()
		{
			if (!Running)
			{
				return;
			}
			Running = false;

			if (sceneManager != null)
			{
				sceneManager.OnLoadEnd -= OnLoadEnd;
				sceneManager.OnUnloadEnd -= OnUnloadEnd;
				sceneManager = null;
			}

			foreach (List<SpawnerRuntime> spawners in byScene.Values)
			{
				StopAll(spawners);
			}
			byScene.Clear();
			pendingScenes.Clear();
			starting.Clear();
			startBalance = 0;
			Scheduler.Clear();
		}

		/// <summary>
		/// Picks up newly loaded scenes, advances their starts within the budget, and runs the
		/// respawn sweep. Call once per frame.
		/// </summary>
		/// <param name="now">The current time on <see cref="SpawnerScheduler.Now"/>'s clock.</param>
		public void Tick(double now)
		{
			if (pendingScenes.Count > 0)
			{
				for (int i = 0; i < pendingScenes.Count; ++i)
				{
					Scene scene = pendingScenes[i];
					if (scene.IsValid() && scene.isLoaded)
					{
						QueueScene(scene);
					}
				}
				pendingScenes.Clear();
			}

			AdvanceStarts(now);

			Scheduler.Tick(now);
		}

		/// <summary>
		/// Creates the spawners for one loaded scene instance and queues their starts.
		/// </summary>
		private void QueueScene(Scene scene)
		{
			if (Catalogue == null || !Catalogue.TryGet(scene.name, out SceneSpawnTable table))
			{
				return;
			}
			QueueScene(scene, table);
		}

		/// <summary>
		/// Creates the spawners of <paramref name="table"/> for a scene instance and queues their
		/// starts, exactly as a load seen through FishNet does. Internal for tests.
		/// </summary>
		internal void QueueScene(Scene scene, SceneSpawnTable table)
		{
			List<SpawnerRuntime> spawners = CreateSpawners(scene, table);
			if (spawners == null)
			{
				return;
			}

			starting.Add(new SceneStart
			{
				Scene = scene,
				Spawners = spawners,
			});
		}

		/// <summary>
		/// Starts queued spawners while the start budget is positive, and hands each scene whose
		/// spawners have all started to the scheduler.
		/// </summary>
		private void AdvanceStarts(double now)
		{
			if (starting.Count < 1)
			{
				return;
			}

			startBalance = Math.Min(startBalance + startWorkPerFrame, startWorkPerFrame);

			while (starting.Count > 0)
			{
				SceneStart scene = starting[0];
				if (scene.Next >= scene.Spawners.Count)
				{
					starting.RemoveAt(0);
					FinishScene(scene.Scene, scene.Spawners);
					continue;
				}

				if (startBalance <= 0)
				{
					return;
				}

				SpawnerRuntime spawner = scene.Spawners[scene.Next++];
				if (spawner == null)
				{
					continue;
				}

				startBalance -= Math.Max(1, PopulateIsolated(spawner, now));
			}
		}

		/// <summary>
		/// Starts one spawner's population, reporting a failure to that spawner's fault log so the
		/// rest of the scene still starts.
		/// </summary>
		/// <returns>The work done, as <see cref="SpawnerRuntime.Populate"/> counts it.</returns>
		private static int PopulateIsolated(SpawnerRuntime spawner, double now)
		{
			try
			{
				int work = spawner.Populate();
				spawner.ReportPassSucceeded();
				return work;
			}
			catch (Exception ex)
			{
				// Populate queued the unfilled slots as respawns before this left it.
				spawner.ReportPassFailed(ex, now);
				return 1;
			}
		}

		/// <summary>
		/// Enters a scene's started spawners into the respawn schedule.
		/// </summary>
		private void FinishScene(Scene scene, List<SpawnerRuntime> spawners)
		{
			for (int i = 0; i < spawners.Count; ++i)
			{
				if (spawners[i] != null)
				{
					Scheduler.Refresh(spawners[i]);
				}
			}

			Log.Debug("SpawnerHost", $"Started {spawners.Count} spawner(s) in {scene.name} (handle {scene.handle}).");
			SpawnerPool.LogReservation(scene.name);
		}

		/// <summary>
		/// Creates and starts the spawners for one loaded scene instance, from its baked table, all
		/// at once.
		/// </summary>
		/// <remarks>
		/// For tests, simulations and tools that want a scene populated now. A scene the host sees
		/// load through FishNet is started over several frames instead; see the class remarks.
		/// </remarks>
		/// <param name="scene">The loaded scene instance.</param>
		/// <returns>The spawners started, or an empty list when the scene has none.</returns>
		public IReadOnlyList<SpawnerRuntime> StartScene(Scene scene)
		{
			if (Catalogue == null || !Catalogue.TryGet(scene.name, out SceneSpawnTable table))
			{
				return Array.Empty<SpawnerRuntime>();
			}
			return StartScene(scene, table);
		}

		/// <summary>
		/// Creates and starts the spawners of <paramref name="table"/> in one loaded scene instance,
		/// all at once.
		/// </summary>
		/// <param name="scene">The loaded scene instance.</param>
		/// <param name="table">The spawners to run in it.</param>
		/// <returns>The spawners started.</returns>
		public IReadOnlyList<SpawnerRuntime> StartScene(Scene scene, SceneSpawnTable table)
		{
			List<SpawnerRuntime> spawners = CreateSpawners(scene, table);
			if (spawners == null)
			{
				return Array.Empty<SpawnerRuntime>();
			}

			double now = Scheduler.Now;
			for (int i = 0; i < spawners.Count; ++i)
			{
				if (spawners[i] != null)
				{
					PopulateIsolated(spawners[i], now);
				}
			}
			FinishScene(scene, spawners);

			return spawners;
		}

		/// <summary>
		/// Creates one runtime per definition in <paramref name="table"/> for a scene instance and
		/// registers them under its handle. Nothing is started.
		/// </summary>
		/// <returns>The runtimes by table index (null where the table has a hole), or null when the table is empty.</returns>
		private List<SpawnerRuntime> CreateSpawners(Scene scene, SceneSpawnTable table)
		{
			if (table == null || table.Spawners == null || table.Spawners.Count < 1)
			{
				return null;
			}

			// Unity reuses scene handles; whatever is here belongs to a scene that has gone.
			StopScene(scene.handle, dropPending: false);

			List<SpawnerRuntime> spawners = new List<SpawnerRuntime>(table.Spawners.Count);
			for (int i = 0; i < table.Spawners.Count; ++i)
			{
				SpawnerDefinition definition = table.Spawners[i];
				spawners.Add(definition != null
					? new SpawnerRuntime(definition, scene, NetworkManager, Scheduler, spawners)
					: null);
			}
			// Built in full before any starts, so a condition can name a later sibling.
			byScene[scene.handle] = spawners;
			return spawners;
		}

		/// <summary>
		/// Stops and drops the spawners of one scene instance.
		/// </summary>
		/// <param name="sceneHandle">The unloaded scene's handle.</param>
		public void StopScene(int sceneHandle)
		{
			StopScene(sceneHandle, dropPending: true);
		}

		private void StopScene(int sceneHandle, bool dropPending)
		{
			if (dropPending)
			{
				for (int i = pendingScenes.Count - 1; i >= 0; --i)
				{
					if (pendingScenes[i].handle == sceneHandle)
					{
						pendingScenes.RemoveAt(i);
					}
				}
			}

			for (int i = starting.Count - 1; i >= 0; --i)
			{
				if (starting[i].Scene.handle == sceneHandle)
				{
					starting.RemoveAt(i);
				}
			}

			if (byScene.TryGetValue(sceneHandle, out List<SpawnerRuntime> spawners))
			{
				StopAll(spawners);
				byScene.Remove(sceneHandle);
			}
		}

		/// <summary>
		/// The spawners running in a scene instance, if any.
		/// </summary>
		public bool TryGetScene(int sceneHandle, out IReadOnlyList<SpawnerRuntime> spawners)
		{
			bool found = byScene.TryGetValue(sceneHandle, out List<SpawnerRuntime> list);
			spawners = list;
			return found;
		}

		private static void StopAll(List<SpawnerRuntime> spawners)
		{
			for (int i = 0; i < spawners.Count; ++i)
			{
				spawners[i]?.Stop();
			}
		}

		private void OnLoadEnd(SceneLoadEndEventArgs args)
		{
			if (!args.QueueData.AsServer || args.LoadedScenes == null)
			{
				return;
			}

			for (int i = 0; i < args.LoadedScenes.Length; ++i)
			{
				pendingScenes.Add(args.LoadedScenes[i]);
			}
		}

		private void OnUnloadEnd(SceneUnloadEndEventArgs args)
		{
			if (!args.QueueData.AsServer || args.UnloadedScenesV2 == null)
			{
				return;
			}

			for (int i = 0; i < args.UnloadedScenesV2.Count; ++i)
			{
				StopScene(args.UnloadedScenesV2[i].Handle);
			}
		}
	}
}
