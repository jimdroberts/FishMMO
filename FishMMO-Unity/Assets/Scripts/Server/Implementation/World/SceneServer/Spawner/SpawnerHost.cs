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
	/// A plain class rather than a server behaviour so tests and simulations can run it on a bare
	/// FishNet server; <see cref="SpawnerSystem"/> is the production wrapper.
	/// </para>
	/// </remarks>
	public sealed class SpawnerHost
	{
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
		/// Running spawners by scene handle.
		/// </summary>
		private readonly Dictionary<int, List<SpawnerRuntime>> byScene = new Dictionary<int, List<SpawnerRuntime>>();

		/// <summary>
		/// Scenes that finished loading and start on the next <see cref="Tick"/>.
		/// </summary>
		private readonly List<Scene> pendingScenes = new List<Scene>();

		/// <summary>
		/// The scene manager subscribed to, kept so the subscription is released from the same one.
		/// </summary>
		private FishNetSceneManager sceneManager;

		/// <summary>
		/// Number of scene instances with running spawners. Diagnostics.
		/// </summary>
		public int SceneCount => byScene.Count;

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
			Scheduler.Clear();
		}

		/// <summary>
		/// Starts pending scenes and runs the respawn sweep. Call once per frame.
		/// </summary>
		/// <param name="nowUtc">The current UTC time.</param>
		/// <param name="nowTime">The current <see cref="UnityEngine.Time.time"/>.</param>
		public void Tick(DateTime nowUtc, float nowTime)
		{
			if (pendingScenes.Count > 0)
			{
				for (int i = 0; i < pendingScenes.Count; ++i)
				{
					Scene scene = pendingScenes[i];
					if (scene.IsValid() && scene.isLoaded)
					{
						StartScene(scene);
					}
				}
				pendingScenes.Clear();
			}

			Scheduler.Tick(nowUtc, nowTime);
		}

		/// <summary>
		/// Creates and starts the spawners for one loaded scene instance, from its baked table.
		/// </summary>
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
		/// Creates and starts the spawners of <paramref name="table"/> in one loaded scene instance.
		/// </summary>
		/// <param name="scene">The loaded scene instance.</param>
		/// <param name="table">The spawners to run in it.</param>
		/// <returns>The spawners started.</returns>
		public IReadOnlyList<SpawnerRuntime> StartScene(Scene scene, SceneSpawnTable table)
		{
			if (table == null || table.Spawners == null || table.Spawners.Count < 1)
			{
				return Array.Empty<SpawnerRuntime>();
			}

			if (byScene.TryGetValue(scene.handle, out List<SpawnerRuntime> existing))
			{
				// Unity reuses scene handles; whatever is here belongs to a scene that has gone.
				StopAll(existing);
				byScene.Remove(scene.handle);
			}

			List<SpawnerRuntime> spawners = new List<SpawnerRuntime>(table.Spawners.Count);
			for (int i = 0; i < table.Spawners.Count; ++i)
			{
				SpawnerDefinition definition = table.Spawners[i];
				spawners.Add(definition != null
					? new SpawnerRuntime(definition, scene, NetworkManager, Scheduler, spawners)
					: null);
			}
			byScene[scene.handle] = spawners;

			// Built in full before any starts, so a condition can name a later sibling.
			for (int i = 0; i < spawners.Count; ++i)
			{
				try
				{
					spawners[i]?.Start();
				}
				catch (Exception ex)
				{
					Log.Error("SpawnerHost", $"Spawner '{table.Spawners[i]?.Name}' in {scene.name} failed to start: {ex}");
				}
			}

			Log.Debug("SpawnerHost", $"Started {spawners.Count} spawner(s) in {scene.name} (handle {scene.handle}).");
			SpawnerPool.LogReservation(scene.name);

			return spawners;
		}

		/// <summary>
		/// Stops and drops the spawners of one scene instance.
		/// </summary>
		/// <param name="sceneHandle">The unloaded scene's handle.</param>
		public void StopScene(int sceneHandle)
		{
			for (int i = pendingScenes.Count - 1; i >= 0; --i)
			{
				if (pendingScenes[i].handle == sceneHandle)
				{
					pendingScenes.RemoveAt(i);
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
