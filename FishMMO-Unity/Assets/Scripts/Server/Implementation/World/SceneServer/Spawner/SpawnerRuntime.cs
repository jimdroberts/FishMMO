using System;
using System.Collections.Generic;
using FishMMO.Logging;
using FishMMO.Server.Implementation.World.SceneServer.AI;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Utility.Performance;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Server.Implementation.World.SceneServer.Spawner
{
	/// <summary>
	/// One running spawner: a baked <see cref="SpawnerDefinition"/> placed in one loaded instance
	/// of its scene. Spawns and respawns networked objects, with pooling, conditions and timers.
	/// </summary>
	/// <remarks>
	/// Created by <see cref="SpawnerHost"/> when a world scene finishes loading on the server and
	/// dropped when it unloads. Stacked instances of one scene each get their own set. The spawned
	/// objects hold this as their <see cref="ISpawnOwner"/>; nothing on a client ever sees it.
	/// </remarks>
	public sealed class SpawnerRuntime : ISpawnOwner
	{
		/// <summary>
		/// What this spawner produces, and how.
		/// </summary>
		public SpawnerDefinition Definition { get; }

		/// <summary>
		/// The scene instance objects are spawned into.
		/// </summary>
		public Scene Scene { get; }

		/// <summary>
		/// The network manager that spawns and pools the objects.
		/// </summary>
		public NetworkManager NetworkManager { get; }

		/// <summary>
		/// The scheduler this spawner reports its outstanding work to.
		/// </summary>
		public SpawnerScheduler Scheduler { get; }

		/// <summary>
		/// This spawner's position in <see cref="Scheduler"/>'s active list, or
		/// <see cref="SpawnerScheduler.NotActive"/> when it has no work outstanding.
		/// </summary>
		/// <remarks>
		/// Stored here so the scheduler can drop a spawner without searching for it. Owned by the
		/// scheduler; nothing else should write it.
		/// </remarks>
		public int SchedulerIndex = SpawnerScheduler.NotActive;

		/// <summary>
		/// The other spawners of the same scene instance, by table index, for conditions that
		/// depend on them.
		/// </summary>
		private readonly IReadOnlyList<SpawnerRuntime> siblings;

		/// <summary>
		/// Currently spawned objects, keyed by their scene-object ID.
		/// </summary>
		private readonly Dictionary<long, ISpawnable> spawned = new Dictionary<long, ISpawnable>();

		/// <summary>
		/// The settings each spawned object was produced from, keyed like <see cref="spawned"/>.
		/// </summary>
		/// <remarks>
		/// The spawner's bookkeeping, not the object's: a spawned entity knows only its owner.
		/// Read back on despawn for the respawn cadence, and by <see cref="UniqueSpawnables"/>
		/// to tell which entries are already alive.
		/// </remarks>
		private readonly Dictionary<long, SpawnableSettings> settingsBySpawned = new Dictionary<long, SpawnableSettings>();

		/// <summary>
		/// Pending respawn deadlines, one per object owed.
		/// </summary>
		private readonly List<DateTime> respawnTimers = new List<DateTime>();

		/// <summary>
		/// <see cref="Time.time"/> at which this spawner next polls for respawns.
		/// </summary>
		private float nextRespawnCheckTime;

		/// <summary>
		/// Internal index for linear spawn selection.
		/// </summary>
		private int lastSpawnIndex;

		/// <summary>
		/// Cached total spawn chance for weighted spawn selection.
		/// </summary>
		private float cachedTotalSpawnChance;

		/// <summary>
		/// Whether <see cref="cachedTotalSpawnChance"/> must be recomputed.
		/// </summary>
		private bool isCacheDirty = true;

		/// <summary>
		/// True once <see cref="Start"/> has run and <see cref="Stop"/> has not.
		/// </summary>
		public bool Running { get; private set; }

		/// <summary>
		/// The objects currently alive (or lying as corpses) that this spawner produced.
		/// </summary>
		public IReadOnlyCollection<ISpawnable> Spawned => spawned.Values;

		/// <summary>
		/// Number of objects currently spawned.
		/// </summary>
		public int SpawnedCount => spawned.Count;

		/// <summary>
		/// Number of respawns owed.
		/// </summary>
		public int PendingRespawnCount => respawnTimers.Count;

		/// <summary>
		/// Creates a spawner. Nothing is spawned until <see cref="Start"/>.
		/// </summary>
		/// <param name="definition">What to spawn.</param>
		/// <param name="scene">The scene instance to spawn into.</param>
		/// <param name="networkManager">The server's network manager.</param>
		/// <param name="scheduler">The scheduler that drives respawns.</param>
		/// <param name="siblings">The scene instance's spawners by table index; may include this one.</param>
		public SpawnerRuntime(SpawnerDefinition definition, Scene scene, NetworkManager networkManager, SpawnerScheduler scheduler, IReadOnlyList<SpawnerRuntime> siblings = null)
		{
			Definition = definition ?? throw new ArgumentNullException(nameof(definition));
			Scene = scene;
			NetworkManager = networkManager;
			Scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
			this.siblings = siblings;
		}

		/// <summary>
		/// The spawner at <paramref name="index"/> in the same scene instance's table, or null.
		/// </summary>
		public SpawnerRuntime GetSibling(int index)
		{
			if (siblings == null || index < 0 || index >= siblings.Count)
			{
				return null;
			}
			return siblings[index];
		}

		/// <summary>
		/// Pre-warms the pool, spawns the initial population and schedules the rest.
		/// </summary>
		public void Start()
		{
			if (Running)
			{
				return;
			}
			Running = true;

			List<SpawnableSettings> spawnables = Definition.Spawnables;
			if (spawnables == null || spawnables.Count < 1)
			{
				return;
			}

			/* Spread the first check across the window rather than starting every spawner's
			 * clock together. A scene that loads hundreds of spawners on one frame would
			 * otherwise have them all poll on the same frame forever after. */
			nextRespawnCheckTime = Time.time + UnityEngine.Random.Range(0.0f, Mathf.Max(0.0f, Definition.RespawnCheckIntervalMaximum));

			PrewarmObjectPool();

			int initial = Mathf.Clamp(Definition.InitialSpawnCount, 0, Definition.MaxSpawnCount);
			for (int i = 0; i < initial; ++i)
			{
				SpawnObject();
			}
			for (int i = spawned.Count; i < Definition.MaxSpawnCount; ++i)
			{
				SpawnableSettings spawnableSettings = spawnables[GetSpawnIndex()];
				if (spawnableSettings == null)
				{
					continue;
				}

				respawnTimers.Add(GetNextRespawnTime(spawnableSettings));
			}

			/* Enter the schedule. A spawner that filled to its cap above has no timers and is
			 * deliberately not queued at all. */
			Scheduler.Refresh(this);
		}

		/// <summary>
		/// Stops the spawner: leaves the schedule and forgets its bookkeeping.
		/// </summary>
		/// <remarks>
		/// Called when the scene instance unloads. The objects themselves go with the scene; their
		/// owner reference is cleared so a pooled instance cannot hand itself back to a spawner that
		/// no longer runs.
		/// </remarks>
		public void Stop()
		{
			Scheduler.Unregister(this);

			foreach (ISpawnable spawnable in spawned.Values)
			{
				if (spawnable != null && ReferenceEquals(spawnable.Spawner, this))
				{
					spawnable.Spawner = null;
				}
			}

			spawned.Clear();
			settingsBySpawned.Clear();
			respawnTimers.Clear();
			nextRespawnCheckTime = 0f;
			lastSpawnIndex = 0;
			cachedTotalSpawnChance = 0f;
			isCacheDirty = true;
			Running = false;
		}

		/// <summary>
		/// Instantiates this spawner's prefabs into the object pool ahead of time.
		/// </summary>
		/// <remarks>
		/// Reserves <see cref="SpawnerDefinition.MaxSpawnCount"/> plus
		/// <see cref="SpawnerDefinition.PrewarmHeadroom"/> of every prefab this spawner can select.
		/// <see cref="SpawnerPool"/> de-duplicates across spawners, so ten spawners sharing one
		/// prefab reserve the largest single demand rather than ten times it.
		/// </remarks>
		private void PrewarmObjectPool()
		{
			if (!Definition.PrewarmPool || NetworkManager == null)
			{
				return;
			}

			int perPrefab = Mathf.Max(1, Definition.MaxSpawnCount + Definition.PrewarmHeadroom);

			List<SpawnableSettings> spawnables = Definition.Spawnables;
			for (int i = 0; i < spawnables.Count; ++i)
			{
				SpawnableSettings settings = spawnables[i];
				if (settings == null || settings.NetworkObject == null)
				{
					continue;
				}

				SpawnerPool.Reserve(NetworkManager, settings.NetworkObject, perPrefab);
			}
		}

		/// <inheritdoc />
		/// <remarks>
		/// Schedules a respawn for the object and returns it to the pool. Reached from the object
		/// itself (a corpse decaying, a pickup taken) through <see cref="ISpawnable.Despawn"/>.
		/// </remarks>
		public void Despawn(ISpawnable spawnable)
		{
			if (spawnable == null)
			{
				return;
			}

			/* Only schedule a respawn for something this spawner is actually tracking. Despawn can
			 * be reached twice for one object — a corpse decaying while the death handler also
			 * fires — and each extra pass used to queue another respawn timer, so a spawner slowly
			 * accumulated phantom timers and over-spawned once they came due. */
			if (!spawned.Remove(spawnable.ID))
			{
				return;
			}

			settingsBySpawned.TryGetValue(spawnable.ID, out SpawnableSettings settings);
			settingsBySpawned.Remove(spawnable.ID);

			// The spawnable is passed too: the settings can legitimately be missing when the object
			// was adopted rather than spawned here, and the instance may still know its own cadence.
			respawnTimers.Add(GetNextRespawnTime(settings, spawnable));

			spawnable.Spawner = null;

			/* DespawnType.Pool returns the object to FishNet's pool rather than destroying it.
			 * Combined with the pre-warm above, a map's network objects are instantiated once at
			 * load and then recycled for the lifetime of the scene. */
			NetworkObject networkObject = spawnable.NetworkObject;
			if (networkObject != null && networkObject.IsSpawned && NetworkManager != null)
			{
				NetworkManager.ServerManager.Despawn(networkObject, DespawnType.Pool);
			}

			/* The death is the event the whole schedule turns on. The timer just added may fall
			 * before the wake already queued for this spawner, so the queued one is superseded
			 * rather than trusted. */
			Scheduler.Refresh(this);
		}

		/// <summary>
		/// Records an object as belonging to this spawner without spawning it.
		/// </summary>
		/// <remarks>
		/// The seam tests use to give a spawner live objects without a network stack; production
		/// code reaches the same bookkeeping through <see cref="SpawnObject"/>.
		/// </remarks>
		internal void Track(ISpawnable spawnable, SpawnableSettings settings)
		{
			if (spawnable == null)
			{
				return;
			}
			spawnable.Spawner = this;
			spawned[spawnable.ID] = spawnable;
			if (settings != null)
			{
				settingsBySpawned[spawnable.ID] = settings;
			}
			else
			{
				settingsBySpawned.Remove(spawnable.ID);
			}
		}

		/// <summary>
		/// Adds a respawn deadline directly. Test seam; see <see cref="Track"/>.
		/// </summary>
		internal void AddRespawnTimer(DateTime deadlineUtc)
		{
			respawnTimers.Add(deadlineUtc);
		}

		/// <summary>
		/// Calculates the next respawn time for an object.
		/// </summary>
		/// <remarks>
		/// The range comes from <see cref="SpawnableSettings.ResolveRespawnTimeRange"/> rather than
		/// from the settings' fields, so a subclass can answer with its prefab's own cadence when
		/// this spawner has not overridden it.
		/// </remarks>
		/// <param name="spawnableSettings">The settings for the object, or null.</param>
		/// <param name="spawnable">The instance being despawned, when there is one.</param>
		/// <returns>When the object should respawn.</returns>
		private DateTime GetNextRespawnTime(SpawnableSettings spawnableSettings, ISpawnable spawnable = null)
		{
			if (!TryResolveRespawnRange(spawnableSettings, spawnable, out float minimum, out float maximum))
			{
				// Nothing knows a cadence for this object — it was adopted rather than spawned
				// here and is not an NPC. The spawner's own initial respawn time is all that is
				// left to go on.
				return DateTime.UtcNow.Add(TimeSpan.FromSeconds(Definition.InitialRespawnTime));
			}

			/* When randomisation is off the MAXIMUM is the delay, which is what the
			 * RandomRespawnTime tooltip has always promised. It used to fall back to
			 * InitialRespawnTime instead — a different setting entirely, defaulting to zero — so
			 * turning randomisation off made a spawner respawn its objects instantly and ignore
			 * every respawn value authored anywhere. */
			float delay = Definition.RandomRespawnTime
				? DeterministicRNG.Shared.Range(minimum, maximum)
				: maximum;

			return DateTime.UtcNow.Add(TimeSpan.FromSeconds(delay));
		}

		/// <summary>
		/// Finds a respawn delay range for an object, from its settings or from the object itself.
		/// </summary>
		/// <param name="spawnableSettings">The settings for the object, or null.</param>
		/// <param name="spawnable">The instance being despawned, when there is one.</param>
		/// <param name="minimum">Receives the shortest respawn delay in seconds.</param>
		/// <param name="maximum">Receives the longest respawn delay in seconds.</param>
		/// <returns>True when a range was found.</returns>
		internal static bool TryResolveRespawnRange(SpawnableSettings spawnableSettings, ISpawnable spawnable, out float minimum, out float maximum)
		{
			if (spawnableSettings != null)
			{
				spawnableSettings.ResolveRespawnTimeRange(out minimum, out maximum);
				return true;
			}

			/* No settings, so ask the instance. An NPC adopted by this spawner rather than spawned
			 * through it still carries its own cadence, and honouring it is strictly better than
			 * respawning such an object on InitialRespawnTime — which is zero unless someone set
			 * it. */
			NPC npc = spawnable != null && spawnable.NetworkObject != null
				? spawnable.NetworkObject.GetComponent<NPC>()
				: null;

			if (npc == null)
			{
				minimum = 0f;
				maximum = 0f;
				return false;
			}

			minimum = Mathf.Max(0f, npc.MinimumRespawnTime);
			maximum = Mathf.Max(minimum, npc.MaximumRespawnTime);
			return true;
		}

		/// <summary>
		/// Whether this spawner has anything left to respawn.
		/// </summary>
		/// <remarks>
		/// Decides membership of <see cref="Scheduler"/>'s active list, and so decides whether this
		/// spawner costs anything at all. A spawner at its cap or with no pending timers is not
		/// walked, which is the whole point: the cost of respawning scales with how much of the
		/// world is in motion rather than with how much of it exists.
		/// </remarks>
		/// <returns>True when a respawn is outstanding.</returns>
		public bool HasRespawnWork()
		{
			return Definition.Spawnables != null &&
				Definition.Spawnables.Count >= 1 &&
				respawnTimers.Count >= 1 &&
				spawned.Count < Definition.MaxSpawnCount;
		}

		/// <summary>
		/// Runs this spawner's respawn check if its own interval has elapsed.
		/// </summary>
		/// <remarks>
		/// Called by <see cref="SpawnerScheduler"/> on every sweep that reaches this spawner while
		/// it has work outstanding. The interval gate is still this spawner's own — the scheduler
		/// decides who is asked, not how often each one polls.
		/// </remarks>
		/// <param name="nowUtc">The time to evaluate respawn deadlines against.</param>
		/// <param name="nowTime">The current <see cref="Time.time"/>, read once by the scheduler.</param>
		internal void RunScheduledRespawn(DateTime nowUtc, float nowTime)
		{
			if (nowTime < nextRespawnCheckTime)
			{
				return;
			}

			ScheduleNextRespawnCheck(nowTime);

			TryRespawn(nowUtc);
		}

		/// <summary>
		/// Picks the next respawn check time, re-randomised each pass so spawners that happen to
		/// align on one frame drift apart again instead of staying in lockstep.
		/// </summary>
		/// <param name="nowTime">The current <see cref="Time.time"/>.</param>
		private void ScheduleNextRespawnCheck(float nowTime)
		{
			// Tolerate an inverted or negative range rather than never polling.
			float minimum = Mathf.Max(0.0f, Definition.RespawnCheckIntervalMinimum);
			float maximum = Mathf.Max(minimum, Definition.RespawnCheckIntervalMaximum);

			nextRespawnCheckTime = nowTime + UnityEngine.Random.Range(minimum, maximum);
		}

		/// <summary>
		/// Attempts to respawn objects if their timers have elapsed and respawn conditions are met.
		/// </summary>
		/// <remarks>
		/// Public so anything holding a spawner can force an immediate attempt rather than waiting
		/// for its scheduled wake.
		/// </remarks>
		public void TryRespawn()
		{
			TryRespawn(DateTime.UtcNow);

			// A direct caller is outside the schedule, so the wake it just invalidated has to be
			// replaced. The scheduler reschedules itself and does not reach this.
			Scheduler.Refresh(this);
		}

		/// <summary>
		/// Attempts to respawn every object whose timer has elapsed.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Respawn conditions are evaluated <b>once</b> for the whole pass, not once per due timer.
		/// A condition is handed only the spawner, so its answer cannot differ between two timers
		/// in the same pass; asking repeatedly cost the most in exactly the case that produces many
		/// due timers at once — a group wiped together.
		/// </para>
		/// <para>
		/// Every due timer is then consumed, rather than one per call. A spawner is capable of
		/// refilling as fast as its deadlines allow; stopping after the first meant the refill rate
		/// was capped by however often this ran, which is a property of the tick and not something
		/// anybody authored.
		/// </para>
		/// </remarks>
		/// <param name="nowUtc">The time to evaluate deadlines against.</param>
		internal void TryRespawn(DateTime nowUtc)
		{
			List<SpawnableSettings> spawnables = Definition.Spawnables;
			if (spawnables == null ||
				spawnables.Count < 1 ||
				respawnTimers.Count < 1)
			{
				return;
			}

			// Clear the timers if we reach our maximum spawn count.
			if (spawned.Count >= Definition.MaxSpawnCount)
			{
				respawnTimers.Clear();
				return;
			}

			// Nothing is due yet. Reached whenever a wake fires early, and on any direct call.
			bool anyDue = false;
			for (int i = 0; i < respawnTimers.Count; ++i)
			{
				if (nowUtc >= respawnTimers[i])
				{
					anyDue = true;
					break;
				}
			}
			if (!anyDue)
			{
				return;
			}

			/* A refusal consumes no timer, so this spawner still has work and stays in the active
			 * list. It is re-tested on its own interval, which is what makes a camp come back once
			 * the boss guarding it dies. */
			if (!EvaluateRespawnConditions())
			{
				return;
			}

			/* Iterate backwards. The body removes the entry it fires, and a forward loop that
			 * removes mid-iteration skips the following element. */
			for (int i = respawnTimers.Count - 1; i >= 0; --i)
			{
				if (nowUtc < respawnTimers[i])
				{
					continue;
				}

				/* Remove the timer BEFORE spawning. SpawnObject clears the whole timer list when it
				 * reaches MaxSpawnCount, and removing afterwards would index a list that had just
				 * been emptied. */
				respawnTimers.RemoveAt(i);

				SpawnObject();

				// SpawnObject clears the list at the cap, which invalidates the loop index.
				if (spawned.Count >= Definition.MaxSpawnCount)
				{
					return;
				}
			}
		}

		/// <summary>
		/// Evaluates the OR and AND respawn condition sets for this spawner.
		/// </summary>
		/// <returns>True when a respawn is permitted.</returns>
		internal bool EvaluateRespawnConditions()
		{
			// Check OR respawn conditions (any one must be true to allow respawn).
			List<RespawnCondition> orConditions = Definition.OrConditions;
			if (orConditions != null && orConditions.Count >= 1)
			{
				bool any = false;
				for (int i = 0; i < orConditions.Count; ++i)
				{
					RespawnCondition condition = orConditions[i];
					if (condition != null && condition.OnCheckCondition(this))
					{
						any = true;
						break;
					}
				}
				if (!any)
				{
					return false;
				}
			}

			// Check AND respawn conditions (all must be true to allow respawn).
			List<RespawnCondition> andConditions = Definition.TrueConditions;
			if (andConditions != null && andConditions.Count >= 1)
			{
				for (int i = 0; i < andConditions.Count; ++i)
				{
					RespawnCondition condition = andConditions[i];
					if (condition != null && !condition.OnCheckCondition(this))
					{
						return false;
					}
				}
			}

			return true;
		}

		/// <summary>
		/// Updates the cached total spawn chance for weighted spawn selection. Only recalculates if cache is dirty.
		/// </summary>
		private void UpdateTotalSpawnChanceCache()
		{
			List<SpawnableSettings> spawnables = Definition.Spawnables;
			if (spawnables != null && spawnables.Count > 0 && isCacheDirty)
			{
				cachedTotalSpawnChance = 0f;
				for (int i = 0; i < spawnables.Count; ++i)
				{
					// Empty slots are normal in authored spawner lists; skip them instead of
					// throwing out of the spawn tick.
					if (spawnables[i] == null)
					{
						continue;
					}
					cachedTotalSpawnChance += spawnables[i].SpawnChance;
				}
				isCacheDirty = false;
			}
		}

		/// <summary>
		/// Selects a spawnable index based on weighted random selection using spawn chances.
		/// </summary>
		/// <returns>The index of the selected spawnable.</returns>
		private int GetWeightedSpawnIndex()
		{
			List<SpawnableSettings> spawnables = Definition.Spawnables;
			if (spawnables == null || spawnables.Count < 1)
			{
				return 0;
			}

			UpdateTotalSpawnChanceCache();

			float randomValue = DeterministicRNG.Shared.Range(0f, cachedTotalSpawnChance);

			float cumulativeChance = 0f;
			for (int i = 0; i < spawnables.Count; ++i)
			{
				if (spawnables[i] == null)
				{
					continue;
				}
				cumulativeChance += spawnables[i].SpawnChance;

				if (randomValue <= cumulativeChance)
				{
					return i;
				}
			}
			// In case something goes wrong, return the first spawnable as a fallback.
			return 0;
		}

		/// <summary>
		/// Gets the index of the next spawnable to use, based on the configured spawn type.
		/// </summary>
		/// <returns>The index of the selected spawnable.</returns>
		public int GetSpawnIndex()
		{
			List<SpawnableSettings> spawnables = Definition.Spawnables;
			if (spawnables == null || spawnables.Count < 1)
			{
				return 0;
			}

			int spawnIndex;
			switch (Definition.SpawnType)
			{
				case ObjectSpawnType.Linear:
					spawnIndex = lastSpawnIndex;
					++lastSpawnIndex;
					if (lastSpawnIndex >= spawnables.Count)
					{
						lastSpawnIndex = 0;
					}
					break;
				case ObjectSpawnType.Random:
					spawnIndex = DeterministicRNG.Shared.Range(0, spawnables.Count);
					break;
				case ObjectSpawnType.Weighted:
					spawnIndex = GetWeightedSpawnIndex();
					break;
				default:
					return 0;
			}
			if (spawnIndex >= spawnables.Count)
			{
				spawnIndex = 0;
			}
			return spawnIndex;
		}

		/// <summary>
		/// True when a live instance spawned from these settings already exists.
		/// </summary>
		/// <param name="spawnableSettings">The settings to look for among the live objects.</param>
		/// <returns>True when one is already alive.</returns>
		private bool IsSpawnableLive(SpawnableSettings spawnableSettings)
		{
			if (spawnableSettings == null)
			{
				return false;
			}

			/* Reference equality against the settings object the spawn was created from. Comparing
			 * the prefab instead would treat two entries that share a prefab but differ in their
			 * overrides as the same spawnable. */
			foreach (SpawnableSettings live in settingsBySpawned.Values)
			{
				if (ReferenceEquals(live, spawnableSettings))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Moves <paramref name="spawnIndex"/> onto a spawnable that has no live instance.
		/// </summary>
		/// <param name="spawnIndex">The chosen index, adjusted in place when it is already taken.</param>
		/// <returns>False when every assigned spawnable is already alive.</returns>
		/// <remarks>
		/// Searches forward from the chosen index rather than re-rolling, so the configured spawn
		/// type still decides where the search starts and a full list cannot spin.
		/// </remarks>
		private bool TryResolveUniqueSpawnIndex(ref int spawnIndex)
		{
			List<SpawnableSettings> spawnables = Definition.Spawnables;
			for (int offset = 0; offset < spawnables.Count; ++offset)
			{
				int candidate = (spawnIndex + offset) % spawnables.Count;

				SpawnableSettings candidateSettings = spawnables[candidate];
				if (candidateSettings == null ||
					candidateSettings.NetworkObject == null)
				{
					continue;
				}

				if (!IsSpawnableLive(candidateSettings))
				{
					spawnIndex = candidate;
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Spawns a new object in the world using the selected spawnable settings and position logic.
		/// </summary>
		public void SpawnObject()
		{
			List<SpawnableSettings> spawnables = Definition.Spawnables;
			if (spawnables == null || spawnables.Count < 1 || NetworkManager == null)
			{
				return;
			}

			/* Hard cap. TryRespawn checks this before calling, but Start and any external caller
			 * do not, and exceeding the maximum is what turns a deterministic pool reservation back
			 * into unbounded growth. */
			if (spawned.Count >= Definition.MaxSpawnCount)
			{
				return;
			}

			if (!Scene.IsValid() || !Scene.isLoaded)
			{
				return;
			}

			int spawnIndex = GetSpawnIndex();

			/* One live instance per entry, when asked for. The spawn index is chosen from the
			 * configured spawn type alone and knows nothing about what is already alive, so without
			 * this a list of distinct individuals happily spawns the same one twice. */
			if (Definition.UniqueSpawnables &&
				!TryResolveUniqueSpawnIndex(ref spawnIndex))
			{
				return;
			}

			SpawnableSettings spawnableSettings = spawnables[spawnIndex];
			if (spawnableSettings == null ||
				spawnableSettings.NetworkObject == null)
			{
				return;
			}

			Vector3 spawnPosition = ResolveSpawnPosition(spawnableSettings);

			NetworkObject prefab = NetworkManager.SpawnablePrefabs.GetObject(true, spawnableSettings.NetworkObject.PrefabId);
			if (prefab == null)
			{
				return;
			}

			// Instantiate the object using object pooling.
			NetworkObject nob = NetworkManager.GetPooledInstantiated(spawnableSettings.NetworkObject.PrefabId, spawnableSettings.NetworkObject.SpawnableCollectionId, ObjectPoolRetrieveOption.MakeActive, null, spawnPosition, Definition.Rotation, null, true);
			if (nob == null)
			{
				return;
			}

			// Delegate type-specific data injection to the settings subclass.
			spawnableSettings.OnSpawned(nob, this);

			// Move the spawned object to the correct scene.
			SceneManager.MoveGameObjectToScene(nob.gameObject, Scene);

			/* Prepare the brain after the scene move. Initialising warps the NavMeshAgent onto the
			 * mesh at the spawn point, which is what a recycled NPC needs: it comes out of the pool
			 * while the agent still believes it is standing wherever the previous occupant died. */
			NPC npc = nob.GetComponent<NPC>();
			if (npc != null)
			{
				if (AIBrainHost.TryGet(NetworkManager, out AIBrainHost brainHost))
				{
					NPCSpawnableSettings npcSettings = spawnableSettings as NPCSpawnableSettings;
					brainHost.Prepare(npc, spawnPosition, npcSettings != null ? npcSettings.ArchetypeOverride : null);
				}
				else
				{
					Log.Warning("SpawnerRuntime", $"No AI brain host is running; {Definition.Name} spawned {npc.gameObject.name} without a brain.");
				}
			}

			// Set up the owner before the spawn, so the object's own start callbacks see it.
			ISpawnable nobSpawnable = nob.GetComponent<ISpawnable>();
			if (nobSpawnable != null)
			{
				nobSpawnable.Spawner = this;
			}

			NetworkManager.ServerManager.Spawn(nob, null, Scene);

			/* Tracked AFTER the spawn, because the key is the scene-object ID and that ID is
			 * assigned in OnStartServer — which runs inside ServerManager.Spawn. Registering
			 * beforehand filed every first-time instance under ID 0: repeated spawns overwrote one
			 * another, so the count never approached MaxSpawnCount and the cap at the top of this
			 * method never engaged, while Despawn looked up the real (negative) ID, missed, and
			 * returned early — leaving the object spawned forever with no respawn queued. Pooled
			 * instances keep their ID across a recycle, so only the first spawn of each instance was
			 * affected, which is exactly the initial world population.
			 *
			 * Indexer, not Add. A pooled object keeps its scene-object ID across a recycle, so a
			 * stale entry left by an object that was despawned some other way would make Add throw
			 * and abort the spawn tick. */
			if (nobSpawnable != null)
			{
				Track(nobSpawnable, spawnableSettings);
			}

			// If we've reached the maximum spawn count, clear respawn timers.
			if (spawned.Count >= Definition.MaxSpawnCount)
			{
				respawnTimers.Clear();
			}
		}

		/// <summary>
		/// Picks where an object appears: the spawner's position, or a random point on the ground
		/// inside its bounding box.
		/// </summary>
		private Vector3 ResolveSpawnPosition(SpawnableSettings spawnableSettings)
		{
			Vector3 spawnPosition = Definition.Position;
			if (!Definition.RandomSpawnPosition)
			{
				return spawnPosition;
			}

			PhysicsScene physicsScene = Scene.GetPhysicsScene();
			if (!physicsScene.IsValid())
			{
				return spawnPosition;
			}

			Vector3 extents = Definition.BoundingBoxExtents;

			// A random point at the top of the bounding box, then down onto the ground.
			Vector3 origin = new Vector3(DeterministicRNG.Shared.Range(-extents.x, extents.x),
										 extents.y,
										 DeterministicRNG.Shared.Range(-extents.z, extents.z));
			origin += spawnPosition;

			if (physicsScene.SphereCast(origin, Definition.SphereRadius, Vector3.down, out RaycastHit hit, Definition.BoundingBoxSize.y, Constants.Layers.Obstruction, QueryTriggerInteraction.Ignore))
			{
				spawnPosition = hit.point;
				spawnPosition.y += spawnableSettings.YOffset;
			}
			return spawnPosition;
		}
	}
}
