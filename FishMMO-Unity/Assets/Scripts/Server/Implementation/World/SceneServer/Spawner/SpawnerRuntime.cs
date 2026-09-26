using System;
using System.Collections.Generic;
using FishMMO.Logging;
using FishMMO.Server.Core;
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
	/// What one attempt to spawn an object came to.
	/// </summary>
	/// <remarks>
	/// Named rather than a bool because the callers need to tell "the slot is spent" from "the
	/// object is still owed": see <see cref="SpawnerRuntime.ConsumesTimer"/>.
	/// </remarks>
	public enum SpawnOutcome
	{
		/// <summary>An object was spawned and is tracked.</summary>
		Spawned,
		/// <summary>
		/// An object was spawned, but its prefab has no <see cref="ISpawnable"/>, so the spawner can
		/// neither count it nor hear of its despawn.
		/// </summary>
		SpawnedUntracked,
		/// <summary>The spawner is already at its maximum.</summary>
		AtCapacity,
		/// <summary>Unique spawnables are on and every entry is already alive.</summary>
		NothingEligible,
		/// <summary>
		/// The chosen entry cannot be spawned as authored — empty, no network object, a prefab the
		/// network manager does not know — or there is no network manager.
		/// </summary>
		Misconfigured,
		/// <summary>The scene instance is not loaded.</summary>
		SceneNotLoaded,
		/// <summary>The pool produced nothing, or an exception interrupted the spawn and it was rolled back.</summary>
		Failed,
	}

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
		/// Test seam: stands in for the network half of a spawn, from the pooled retrieval through
		/// the network spawn, and returns the object made or null.
		/// </summary>
		/// <remarks>
		/// Null in production. A spawn that does not happen now keeps its timer (see
		/// <see cref="ConsumesTimer"/>), so a runtime with no network manager can no longer stand in
		/// for a successful one; tests that drive the bookkeeping of a spawn that works set this.
		/// When set, the network manager and scene checks are skipped: they belong to the half it
		/// replaces.
		/// </remarks>
		internal Func<SpawnableSettings, ISpawnable> NetworkSpawnOverride;

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
		/// Read back on despawn for the respawn cadence, by <see cref="UniqueSpawnables"/> to tell
		/// which entries are already alive, and by <see cref="Stop"/> to tell the pool which
		/// prefabs' instances went with the scene.
		/// </remarks>
		private readonly Dictionary<long, SpawnableSettings> settingsBySpawned = new Dictionary<long, SpawnableSettings>();

		/// <summary>
		/// Pending respawn deadlines, one per object owed, in seconds on <see cref="Scheduler"/>'s
		/// clock.
		/// </summary>
		private readonly List<double> respawnTimers = new List<double>();

		/// <summary>
		/// When, on <see cref="Scheduler"/>'s clock, this spawner next polls for respawns.
		/// </summary>
		private double nextRespawnCheckTime;

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
		/// Fault log for this spawner's respawn passes and start, created on the first failure.
		/// </summary>
		private RepeatingFaultLog faults;

		/// <summary>
		/// Spawn problems already logged, by outcome and entry, so each is logged once rather than
		/// at every check that meets it again. Created on the first.
		/// </summary>
		private HashSet<int> reportedProblems;

		/// <summary>
		/// The pack this spawner's NPCs belong to while any of them stands, or null. See
		/// <see cref="Pack"/>.
		/// </summary>
		private NPCGroup pack;

		/// <summary>
		/// True once <see cref="Start"/> has run and <see cref="Stop"/> has not.
		/// </summary>
		public bool Running { get; private set; }

		/// <summary>
		/// This spawner's current pack, or null when its pack is disabled or no member of it
		/// stands.
		/// </summary>
		/// <remarks>
		/// One pack per spawner at a time. Every NPC the spawner spawns joins it with its entry's
		/// role (<see cref="JoinPack"/>); members leave on death, despawn and pool reset; and when
		/// the last one leaves the pack is released, so the next spawn founds another. A respawn
		/// while any member stands therefore rejoins the pack it left.
		/// </remarks>
		public NPCGroup Pack => pack != null && !pack.Released ? pack : null;

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
			try
			{
				Populate();
			}
			finally
			{
				/* Enter the schedule whatever happened. A spawner that filled to its cap has no
				 * timers and is deliberately not queued at all; one whose initial spawns failed
				 * owes them as respawns and must be asked again. */
				Scheduler.Refresh(this);
			}
		}

		/// <summary>
		/// Pre-warms the pool, spawns the initial population and queues every slot left unfilled
		/// as a respawn, without entering the schedule.
		/// </summary>
		/// <remarks>
		/// <see cref="Start"/> is this plus the schedule. <see cref="SpawnerHost"/> calls it on its
		/// own so it can spread a scene's starts over several frames and put the whole scene into
		/// the schedule only once every spawner in it has its initial population — a respawn
		/// condition that names a sibling must never see that sibling before it has started.
		/// </remarks>
		/// <returns>
		/// The work done, for the host's start budget: one, plus the pooled instances the prewarm
		/// created, plus the spawns attempted.
		/// </returns>
		internal int Populate()
		{
			if (Running)
			{
				return 0;
			}
			Running = true;

			List<SpawnableSettings> spawnables = Definition.Spawnables;
			if (spawnables == null || spawnables.Count < 1)
			{
				return 1;
			}

			double now = Scheduler.Now;

			/* Spread the first check across the window rather than starting every spawner's
			 * clock together. A scene that loads hundreds of spawners on one frame would
			 * otherwise have them all poll on the same frame forever after. */
			nextRespawnCheckTime = now + UnityEngine.Random.Range(0.0f, Mathf.Max(0.0f, Definition.RespawnCheckIntervalMaximum));

			int work = 1 + PrewarmObjectPool();

			int initial = Mathf.Clamp(Definition.InitialSpawnCount, 0, Definition.MaxSpawnCount);
			try
			{
				for (int i = 0; i < initial; ++i)
				{
					++work;
					// An attempt that spawns nothing leaves its slot to the respawns queued below.
					SpawnObject();
				}
			}
			finally
			{
				/* Also when an initial spawn threw: the slots it and the ones after it would have
				 * filled are owed as respawns, so the spawner comes back rather than standing
				 * short for the life of the scene. */
				QueueUnfilledSlots(now);
			}
			return work;
		}

		/// <summary>
		/// Queues one respawn for every slot below the maximum that is neither alive nor already
		/// owed.
		/// </summary>
		private void QueueUnfilledSlots(double now)
		{
			List<SpawnableSettings> spawnables = Definition.Spawnables;
			for (int i = spawned.Count + respawnTimers.Count; i < Definition.MaxSpawnCount; ++i)
			{
				/* The entry is chosen only for its respawn delay; the respawn picks its own. An
				 * empty entry used to skip the slot altogether, which left a spawner with a blank
				 * in its list permanently below its maximum. Search on from it instead, as the
				 * unique-spawnable search does, and only a list with nothing in it queues nothing. */
				SpawnableSettings spawnableSettings = FirstAssignedFrom(GetSpawnIndex());
				if (spawnableSettings == null)
				{
					return;
				}

				respawnTimers.Add(GetNextRespawnTime(now, spawnableSettings));
			}
		}

		/// <summary>
		/// The first non-empty entry at or after <paramref name="index"/>, wrapping, or null.
		/// </summary>
		private SpawnableSettings FirstAssignedFrom(int index)
		{
			List<SpawnableSettings> spawnables = Definition.Spawnables;
			for (int offset = 0; offset < spawnables.Count; ++offset)
			{
				SpawnableSettings candidate = spawnables[(index + offset) % spawnables.Count];
				if (candidate != null)
				{
					return candidate;
				}
			}
			return null;
		}

		/// <summary>
		/// Stops the spawner: leaves the schedule and forgets its bookkeeping.
		/// </summary>
		/// <remarks>
		/// Called when the scene instance unloads. The objects themselves went with the scene:
		/// their owner reference is cleared so a pooled instance cannot hand itself back to a
		/// spawner that no longer runs, and <see cref="SpawnerPool"/> is told those instances no
		/// longer exist, so the next scene to reserve the prefab makes them again at its load
		/// rather than one at a time during play.
		/// </remarks>
		public void Stop()
		{
			Scheduler.Unregister(this);

			foreach (KeyValuePair<long, ISpawnable> pair in spawned)
			{
				ISpawnable spawnable = pair.Value;
				if (spawnable != null && ReferenceEquals(spawnable.Spawner, this))
				{
					spawnable.Spawner = null;
				}

				if (settingsBySpawned.TryGetValue(pair.Key, out SpawnableSettings settings) &&
					settings != null &&
					settings.NetworkObject != null)
				{
					SpawnerPool.Forget(settings.NetworkObject, 1);
				}
			}

			/* The pack ends with its spawner. Its members went with the scene, and their brains leave
			 * it as they are destroyed; dissolving it here also covers a host shutting down with the
			 * members still standing, and stops the pack being ticked for a spawner that is gone. */
			pack?.Dissolve();
			pack = null;

			spawned.Clear();
			settingsBySpawned.Clear();
			respawnTimers.Clear();
			nextRespawnCheckTime = 0.0;
			lastSpawnIndex = 0;
			cachedTotalSpawnChance = 0f;
			isCacheDirty = true;
			Running = false;
		}

		/// <summary>
		/// Adds a freshly spawned NPC's brain to this spawner's pack, founding the pack if none
		/// stands. Does nothing when the pack is disabled.
		/// </summary>
		/// <remarks>
		/// Called for every NPC the spawner spawns, after the network spawn: an attempt that is
		/// rolled back before it never joins. The role is the entry's
		/// <see cref="NPCSpawnableSettings.PackRole"/>; an entry of another kind joins with none.
		/// </remarks>
		/// <param name="brain">The spawned NPC's brain.</param>
		/// <param name="settings">The entry it was spawned from.</param>
		/// <returns>The pack it joined, or null.</returns>
		internal NPCGroup JoinPack(AIController brain, SpawnableSettings settings)
		{
			NPCPackSettings packSettings = Definition.Pack;
			if (brain == null || packSettings == null || !packSettings.Enabled)
			{
				return null;
			}

			if (pack == null || pack.Released)
			{
				pack = new NPCGroup(packSettings);
			}

			NPCGroupRole role = settings is NPCSpawnableSettings npcSettings ? npcSettings.PackRole : NPCGroupRole.None;
			return pack.AddMember(brain, role) ? pack : null;
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
		/// <returns>The number of instances created.</returns>
		private int PrewarmObjectPool()
		{
			if (!Definition.PrewarmPool || NetworkManager == null)
			{
				return 0;
			}

			int perPrefab = Mathf.Max(1, Definition.MaxSpawnCount + Definition.PrewarmHeadroom);
			int created = 0;

			List<SpawnableSettings> spawnables = Definition.Spawnables;
			for (int i = 0; i < spawnables.Count; ++i)
			{
				SpawnableSettings settings = spawnables[i];
				if (settings == null || settings.NetworkObject == null)
				{
					continue;
				}

				created += SpawnerPool.Reserve(NetworkManager, settings.NetworkObject, perPrefab);
			}
			return created;
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
			respawnTimers.Add(GetNextRespawnTime(Scheduler.Now, settings, spawnable));

			spawnable.Spawner = null;

			/* DespawnType.Pool returns the object to FishNet's pool rather than destroying it.
			 * Combined with the pre-warm above, a map's network objects are instantiated once at
			 * load and then recycled — and PersistentPool takes the pooled object out of this
			 * world scene, so the scene's unload cannot destroy what the pool is holding. The
			 * spawner-less fallbacks (NPC.ReturnToPool, Interactable.Despawn) pool the same way. */
			PersistentPool.Despawn(NetworkManager, spawnable.NetworkObject);

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
		/// <param name="deadline">When the respawn falls due, in seconds on <see cref="Scheduler"/>'s clock.</param>
		internal void AddRespawnTimer(double deadline)
		{
			respawnTimers.Add(deadline);
		}

		/// <summary>
		/// Calculates the next respawn time for an object.
		/// </summary>
		/// <remarks>
		/// The range comes from <see cref="SpawnableSettings.ResolveRespawnTimeRange"/> rather than
		/// from the settings' fields, so a subclass can answer with its prefab's own cadence when
		/// this spawner has not overridden it.
		/// </remarks>
		/// <param name="now">The current time on <see cref="Scheduler"/>'s clock.</param>
		/// <param name="spawnableSettings">The settings for the object, or null.</param>
		/// <param name="spawnable">The instance being despawned, when there is one.</param>
		/// <returns>When the object should respawn, on <see cref="Scheduler"/>'s clock.</returns>
		private double GetNextRespawnTime(double now, SpawnableSettings spawnableSettings, ISpawnable spawnable = null)
		{
			if (!TryResolveRespawnRange(spawnableSettings, spawnable, out float minimum, out float maximum))
			{
				// Nothing knows a cadence for this object — it was adopted rather than spawned
				// here and is not an NPC. The spawner's own initial respawn time is all that is
				// left to go on.
				return now + Definition.InitialRespawnTime;
			}

			/* When randomisation is off the MAXIMUM is the delay, which is what the
			 * RandomRespawnTime tooltip has always promised. It used to fall back to
			 * InitialRespawnTime instead — a different setting entirely, defaulting to zero — so
			 * turning randomisation off made a spawner respawn its objects instantly and ignore
			 * every respawn value authored anywhere. */
			float delay = Definition.RandomRespawnTime
				? DeterministicRNG.Shared.Range(minimum, maximum)
				: maximum;

			return now + delay;
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
		/// decides who is asked, not how often each one polls. A pass the budget cut short leaves
		/// the gate open, so the rest of it runs at the very next visit instead of an interval
		/// later.
		/// </remarks>
		/// <param name="now">The current time on <see cref="Scheduler"/>'s clock, read once by the scheduler.</param>
		/// <param name="budget">Spawns the scheduler can still afford this frame; reduced by each attempt.</param>
		/// <returns>True when the budget ran out with due respawns left.</returns>
		internal bool RunScheduledRespawn(double now, ref int budget)
		{
			if (now < nextRespawnCheckTime)
			{
				return false;
			}

			ScheduleNextRespawnCheck(now);

			bool cutShort = TryRespawn(now, ref budget);
			if (cutShort)
			{
				nextRespawnCheckTime = now;
			}
			return cutShort;
		}

		/// <summary>
		/// Picks the next respawn check time, re-randomised each pass so spawners that happen to
		/// align on one frame drift apart again instead of staying in lockstep.
		/// </summary>
		/// <param name="now">The current time on <see cref="Scheduler"/>'s clock.</param>
		private void ScheduleNextRespawnCheck(double now)
		{
			// Tolerate an inverted or negative range rather than never polling.
			float minimum = Mathf.Max(0.0f, Definition.RespawnCheckIntervalMinimum);
			float maximum = Mathf.Max(minimum, Definition.RespawnCheckIntervalMaximum);

			nextRespawnCheckTime = now + UnityEngine.Random.Range(minimum, maximum);
		}

		/// <summary>
		/// Attempts to respawn objects if their timers have elapsed and respawn conditions are met.
		/// </summary>
		/// <remarks>
		/// Public so anything holding a spawner can force an immediate attempt rather than waiting
		/// for its scheduled wake. Not budgeted: the caller asked for it now.
		/// </remarks>
		public void TryRespawn()
		{
			int unlimited = int.MaxValue;
			try
			{
				TryRespawn(Scheduler.Now, ref unlimited);
			}
			finally
			{
				// A direct caller is outside the schedule, so the wake it just invalidated has to be
				// replaced. The scheduler reschedules itself and does not reach this.
				Scheduler.Refresh(this);
			}
		}

		/// <summary>
		/// Whether a spawn attempt used up the respawn timer that asked for it.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The rule is whether the object is still owed. It is not when one entered the world —
		/// tracked or, for a prefab with no <see cref="ISpawnable"/>, not — or when there is no slot
		/// for it (the cap, or every unique entry alive; a death queues a fresh timer). It is when
		/// nothing entered the world for a reason that can pass: a misconfigured entry (a random
		/// or weighted spawner picks again next time), an unloaded scene, or a spawn that failed
		/// and was rolled back.
		/// </para>
		/// <para>
		/// The timer used to be removed before the attempt whatever came of it, so every failure
		/// left the spawner one short for the life of the scene — and a spawner with one bad entry
		/// among good ones bled a slot each time it picked the bad one, until it was empty.
		/// </para>
		/// </remarks>
		/// <param name="outcome">What the attempt came to.</param>
		/// <returns>True when the timer is spent.</returns>
		public static bool ConsumesTimer(SpawnOutcome outcome)
		{
			switch (outcome)
			{
				case SpawnOutcome.Spawned:
				case SpawnOutcome.SpawnedUntracked:
				case SpawnOutcome.AtCapacity:
				case SpawnOutcome.NothingEligible:
					return true;
				default:
					return false;
			}
		}

		/// <summary>
		/// Attempts to respawn every object whose timer has elapsed, within a budget.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Respawn conditions are evaluated <b>once</b> for the whole pass, not once per due timer.
		/// A condition is handed only the spawner, so its answer cannot differ between two timers
		/// in the same pass; asking repeatedly cost the most in exactly the case that produces many
		/// due timers at once — a group wiped together.
		/// </para>
		/// <para>
		/// Every due timer is then consumed, rather than one per call, up to the budget. A spawner
		/// is capable of refilling as fast as its deadlines allow; stopping after the first meant
		/// the refill rate was capped by however often this ran, which is a property of the tick
		/// and not something anybody authored. The budget only spreads a large refill over
		/// consecutive frames (see <see cref="SpawnerScheduler.SpawnsPerFrame"/>).
		/// </para>
		/// <para>
		/// An attempt that leaves the object owed (<see cref="ConsumesTimer"/>) puts its deadline
		/// back and ends the pass, since whatever stopped it would stop the rest the same way; the
		/// next check tries again. So does one that throws, before the exception leaves: the
		/// scheduler reports it and carries on with the other spawners.
		/// </para>
		/// </remarks>
		/// <param name="now">The time to evaluate deadlines against, on <see cref="Scheduler"/>'s clock.</param>
		/// <param name="budget">Spawn attempts still affordable; reduced by one per attempt.</param>
		/// <returns>True when the budget ran out with due timers left.</returns>
		internal bool TryRespawn(double now, ref int budget)
		{
			List<SpawnableSettings> spawnables = Definition.Spawnables;
			if (spawnables == null ||
				spawnables.Count < 1 ||
				respawnTimers.Count < 1)
			{
				return false;
			}

			// Clear the timers if we reach our maximum spawn count.
			if (spawned.Count >= Definition.MaxSpawnCount)
			{
				respawnTimers.Clear();
				return false;
			}

			// Nothing is due yet. Reached whenever a wake fires early, and on any direct call.
			bool anyDue = false;
			for (int i = 0; i < respawnTimers.Count; ++i)
			{
				if (now >= respawnTimers[i])
				{
					anyDue = true;
					break;
				}
			}
			if (!anyDue)
			{
				return false;
			}

			/* A refusal consumes no timer, so this spawner still has work and stays in the active
			 * list. It is re-tested on its own interval, which is what makes a camp come back once
			 * the boss guarding it dies. */
			if (!EvaluateRespawnConditions())
			{
				return false;
			}

			/* Iterate backwards. The body removes the entry it fires, and a forward loop that
			 * removes mid-iteration skips the following element. */
			for (int i = respawnTimers.Count - 1; i >= 0; --i)
			{
				if (now < respawnTimers[i])
				{
					continue;
				}

				if (budget <= 0)
				{
					return true;
				}
				--budget;

				/* Remove the timer BEFORE spawning. SpawnObject clears the whole timer list when it
				 * reaches MaxSpawnCount, and removing afterwards would index a list that had just
				 * been emptied. It goes back in below if the object is still owed. */
				double deadline = respawnTimers[i];
				respawnTimers.RemoveAt(i);

				SpawnOutcome outcome = SpawnOutcome.Failed;
				try
				{
					outcome = SpawnObject();
				}
				finally
				{
					if (!ConsumesTimer(outcome))
					{
						// Already due, so the next check tries it again.
						respawnTimers.Add(deadline);
					}
				}

				if (!ConsumesTimer(outcome))
				{
					return false;
				}

				// SpawnObject clears the list at the cap, which invalidates the loop index.
				if (spawned.Count >= Definition.MaxSpawnCount)
				{
					return false;
				}
			}
			return false;
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
		/// <remarks>
		/// <para>
		/// <b>Tracked or rolled back, never half-made.</b> Once an instance has come out of the
		/// pool, everything after it — the settings' <c>OnSpawned</c>, the move into the scene, the
		/// brain's preparation, the network spawn and the tracking — either completes or is undone
		/// in a <c>finally</c>: the owner reference is cleared and the instance goes back to the
		/// pool, despawned first if the network spawn had got that far. An exception used to leave
		/// the instance in the world untracked, so it was never despawned or pooled, and the
		/// spawner never counted it.
		/// </para>
		/// <para>
		/// A problem that stops the attempt before that point — a misconfigured entry, an unloaded
		/// scene — is logged once per spawner and entry, not at every check that meets it again.
		/// </para>
		/// </remarks>
		/// <returns>What the attempt came to; see <see cref="ConsumesTimer"/> for what each means for the respawn that asked.</returns>
		public SpawnOutcome SpawnObject()
		{
			List<SpawnableSettings> spawnables = Definition.Spawnables;
			if (spawnables == null || spawnables.Count < 1)
			{
				return ReportOnce(SpawnOutcome.Misconfigured, -1, "it has no spawnables.");
			}

			/* Hard cap. TryRespawn checks this before calling, but Start and any external caller
			 * do not, and exceeding the maximum is what turns a deterministic pool reservation back
			 * into unbounded growth. */
			if (spawned.Count >= Definition.MaxSpawnCount)
			{
				return SpawnOutcome.AtCapacity;
			}

			if (NetworkSpawnOverride == null)
			{
				if (NetworkManager == null)
				{
					return ReportOnce(SpawnOutcome.Misconfigured, -1, "there is no network manager to spawn with.");
				}
				if (!Scene.IsValid() || !Scene.isLoaded)
				{
					return ReportOnce(SpawnOutcome.SceneNotLoaded, -1, "its scene is not loaded.");
				}
			}

			int spawnIndex = GetSpawnIndex();

			/* One live instance per entry, when asked for. The spawn index is chosen from the
			 * configured spawn type alone and knows nothing about what is already alive, so without
			 * this a list of distinct individuals happily spawns the same one twice. */
			if (Definition.UniqueSpawnables &&
				!TryResolveUniqueSpawnIndex(ref spawnIndex))
			{
				return SpawnOutcome.NothingEligible;
			}

			SpawnableSettings spawnableSettings = spawnables[spawnIndex];
			if (spawnableSettings == null)
			{
				return ReportOnce(SpawnOutcome.Misconfigured, spawnIndex, $"spawnable {spawnIndex} is empty.");
			}

			if (NetworkSpawnOverride != null)
			{
				ISpawnable made = NetworkSpawnOverride(spawnableSettings);
				if (made == null)
				{
					return SpawnOutcome.Failed;
				}
				Track(made, spawnableSettings);
				// A stand-in that made a real NPC with a brain joins the pack as a spawned one does.
				JoinPack(made is Component component ? component.GetComponent<AIController>() : null, spawnableSettings);
				ClearTimersAtCapacity();
				return SpawnOutcome.Spawned;
			}

			if (spawnableSettings.NetworkObject == null)
			{
				return ReportOnce(SpawnOutcome.Misconfigured, spawnIndex, $"spawnable {spawnIndex} has no network object.");
			}

			Vector3 spawnPosition = ResolveSpawnPosition(spawnableSettings);

			NetworkObject prefab = NetworkManager.SpawnablePrefabs.GetObject(true, spawnableSettings.NetworkObject.PrefabId);
			if (prefab == null)
			{
				return ReportOnce(SpawnOutcome.Misconfigured, spawnIndex,
					$"spawnable {spawnIndex}'s prefab {spawnableSettings.NetworkObject.name} (id {spawnableSettings.NetworkObject.PrefabId}) is not among the network manager's spawnable prefabs.");
			}

			// Instantiate the object using object pooling.
			NetworkObject nob = NetworkManager.GetPooledInstantiated(spawnableSettings.NetworkObject.PrefabId, spawnableSettings.NetworkObject.SpawnableCollectionId, ObjectPoolRetrieveOption.MakeActive, null, spawnPosition, Definition.Rotation, null, true);
			if (nob == null)
			{
				return ReportOnce(SpawnOutcome.Failed, spawnIndex, $"the pool returned nothing for spawnable {spawnIndex}.");
			}

			ISpawnable nobSpawnable = null;
			AIController brain = null;
			bool completed = false;
			try
			{
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
						brain = brainHost.Prepare(npc, spawnPosition, npcSettings != null ? npcSettings.ArchetypeOverride : null);
					}
					else
					{
						Log.Warning("SpawnerRuntime", $"No AI brain host is running; {Definition.Name} spawned {npc.gameObject.name} without a brain.");
					}
				}

				// Set up the owner before the spawn, so the object's own start callbacks see it.
				nobSpawnable = nob.GetComponent<ISpawnable>();
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

				/* Into the pack last, once the NPC is really in the world: a spawn rolled back before
				 * this point never joined, and one rolled back after it leaves again through the
				 * despawn (AIController.ResetForPool). */
				JoinPack(brain, spawnableSettings);
				completed = true;
			}
			finally
			{
				if (!completed)
				{
					RollBackUnfinishedSpawn(nob, nobSpawnable);
				}
			}

			ClearTimersAtCapacity();

			if (nobSpawnable == null)
			{
				return ReportOnce(SpawnOutcome.SpawnedUntracked, spawnIndex,
					$"spawnable {spawnIndex} ({spawnableSettings.NetworkObject.name}) has no ISpawnable component, so it was spawned but cannot be counted or report its despawn; the slot is spent.");
			}
			return SpawnOutcome.Spawned;
		}

		/// <summary>
		/// At the cap, owed respawns are moot: drop them.
		/// </summary>
		private void ClearTimersAtCapacity()
		{
			if (spawned.Count >= Definition.MaxSpawnCount)
			{
				respawnTimers.Clear();
			}
		}

		/// <summary>
		/// Undoes a spawn that was interrupted after its instance came out of the pool.
		/// </summary>
		/// <remarks>
		/// Runs in a <c>finally</c> while an exception is on its way out, so it must not throw one
		/// of its own: that would replace the original, which is the one worth reading.
		/// </remarks>
		private void RollBackUnfinishedSpawn(NetworkObject nob, ISpawnable nobSpawnable)
		{
			try
			{
				if (nobSpawnable != null)
				{
					if (spawned.TryGetValue(nobSpawnable.ID, out ISpawnable tracked) && ReferenceEquals(tracked, nobSpawnable))
					{
						spawned.Remove(nobSpawnable.ID);
						settingsBySpawned.Remove(nobSpawnable.ID);
					}
					if (ReferenceEquals(nobSpawnable.Spawner, this))
					{
						nobSpawnable.Spawner = null;
					}
				}

				if (nob == null || NetworkManager == null)
				{
					return;
				}

				if (nob.IsSpawned)
				{
					// The despawn resets the brain and takes it out of its pack (NPC.OnServerDespawned).
					NetworkManager.ServerManager.Despawn(nob, DespawnType.Pool);
				}
				else
				{
					/* Never spawned, so FishNet raises no despawn: the brain prepared for it would stay
					 * on the host's tick, stepping an inactive pooled body. Retire it explicitly. */
					AIBrainHost.RetireBrainOf(NetworkManager, nob);
					NetworkManager.StorePooledInstantiated(nob, true);
				}
				PersistentPool.Keep(NetworkManager, nob);
			}
			catch (Exception ex)
			{
				Log.Error("SpawnerRuntime", $"Spawner '{Definition.Name}' in {SceneLabel()} could not roll back an interrupted spawn of {(nob != null ? nob.name : "an object")}: {ex}");
			}
		}

		/// <summary>
		/// Logs a spawn problem the first time this spawner meets it for this entry, and returns
		/// the outcome so a guard reads as one statement.
		/// </summary>
		/// <param name="outcome">The outcome being reported.</param>
		/// <param name="entryIndex">The spawnable entry concerned, or -1 for the spawner as a whole.</param>
		/// <param name="problem">What is wrong, as the end of a sentence.</param>
		private SpawnOutcome ReportOnce(SpawnOutcome outcome, int entryIndex, string problem)
		{
			int key = ((int)outcome << 16) | (entryIndex & 0xFFFF);
			reportedProblems ??= new HashSet<int>();
			if (!reportedProblems.Add(key))
			{
				return outcome;
			}

			string consequence = ConsumesTimer(outcome)
				? string.Empty
				: " The respawn is kept and tried again at each check; this is logged once.";
			string message = $"Spawner '{Definition.Name}' in {SceneLabel()} could not spawn: {problem}{consequence}";
			if (outcome == SpawnOutcome.Misconfigured || outcome == SpawnOutcome.SpawnedUntracked)
			{
				Log.Error("SpawnerRuntime", message);
			}
			else
			{
				Log.Warning("SpawnerRuntime", message);
			}
			return outcome;
		}

		/// <summary>
		/// Records a respawn pass or start that completed. Called by the scheduler and the host.
		/// </summary>
		internal void ReportPassSucceeded()
		{
			faults?.ReportSuccess();
		}

		/// <summary>
		/// Records a respawn pass or start that threw. Called by the scheduler and the host, which
		/// catch per spawner so one broken spawner cannot stop the rest.
		/// </summary>
		/// <param name="ex">The exception.</param>
		/// <param name="now">The current time on <see cref="Scheduler"/>'s clock.</param>
		internal void ReportPassFailed(Exception ex, double now)
		{
			faults ??= new RepeatingFaultLog("SpawnerRuntime", $"Spawner '{Definition.Name}' in {SceneLabel()}");
			faults.Report(ex, now);
		}

		private string SceneLabel()
		{
			return Scene.IsValid() ? $"{Scene.name} (handle {Scene.handle})" : "no scene";
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
