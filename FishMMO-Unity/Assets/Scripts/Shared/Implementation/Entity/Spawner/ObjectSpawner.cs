using FishNet.Object;
using FishNet.Utility.Performance;
using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Manages spawning and respawning of networked objects in the game world. Supports various spawn types, respawn conditions, and object pooling.
	/// </summary>
	[RequireComponent(typeof(NetworkObject))]
	public class ObjectSpawner : NetworkBehaviour
	{
		/// <summary>
		/// Cached reference to the spawner's transform.
		/// </summary>
		[HideInInspector]
		public Transform Transform;

		/// <summary>
		/// If any of these conditions return true, the object will respawn. This list is checked first (logical OR).
		/// </summary>
		public List<BaseRespawnCondition> OrConditions = new List<BaseRespawnCondition>();

		/// <summary>
		/// All conditions must return true for the object to respawn. This list is checked second (logical AND).
		/// </summary>
		public List<BaseRespawnCondition> TrueConditions = new List<BaseRespawnCondition>();

		/// <summary>
		/// Flag to track if the spawn chance cache needs updating.
		/// </summary>
		[HideInInspector]
		public bool IsCacheDirty = true;

		/// <summary>
		/// The initial respawn time (in seconds) for spawned objects.
		/// </summary>
		public float InitialRespawnTime = 0.0f;

		/// <summary>
		/// The number of objects to spawn initially when the spawner starts.
		/// </summary>
		public int InitialSpawnCount = 0;

		/// <summary>
		/// The maximum number of objects that can be spawned by this spawner.
		/// </summary>
		[Tooltip("The maximum number of objects that can be spawned by this spawner.")]
		public int MaxSpawnCount = 1;

		/// <summary>
		/// When true, at most one live instance of each entry in <see cref="Spawnables"/> may exist
		/// at a time.
		/// </summary>
		/// <remarks>
		/// Off by default, because the normal case is a spawner filling a zone with several of the
		/// same creature. Turn it on where each entry names a distinct individual — a zone listing
		/// several named NPCs would otherwise draw the same one repeatedly and stand two copies of
		/// it side by side, since the spawn index is chosen without regard to what is already alive.
		///
		/// This caps each entry at one, not the spawner: MaxSpawnCount still governs the total, so
		/// with this on the effective ceiling is the smaller of MaxSpawnCount and the number of
		/// assigned spawnables.
		/// </remarks>
		[Tooltip("Allow at most one live instance of each spawnable. Off by default. Turn on when every entry is a distinct individual that should never be duplicated.")]
		public bool UniqueSpawnables = false;

		/// <summary>
		/// The type of spawn selection (Linear, Random, Weighted).
		/// </summary>
		public ObjectSpawnType SpawnType = ObjectSpawnType.Linear;

		/// <summary>
		/// When true, every prefab this spawner can produce is instantiated into the object pool
		/// at scene start, up to <see cref="MaxSpawnCount"/> each.
		/// </summary>
		/// <remarks>
		/// <para>
		/// This is what makes a map's memory footprint deterministic. Without it the pool fills
		/// lazily — the first NPC of each kind is instantiated the moment a player walks into range
		/// — so a freshly loaded map hitches as it is explored and only reaches its true heap size
		/// once every spawner has fired at least once. Neither behaviour can be planned against.
		/// </para>
		/// <para>
		/// Turn it off for spawners whose prefabs are large and rarely used, where paying the cost
		/// on demand is preferable to paying it always.
		/// </para>
		/// </remarks>
		[Header("Pooling")]
		[Tooltip("Instantiate this spawner's prefabs into the pool at scene start for a fixed memory footprint.")]
		public bool PrewarmPool = true;

		/// <summary>
		/// Extra instances reserved beyond <see cref="MaxSpawnCount"/>.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Slack, not a requirement. A corpse holds its spawner slot for the whole of its decay —
		/// <see cref="Despawn"/> is what frees the slot and starts the respawn clock, and a corpse
		/// does not reach it until the decay timer expires — so this spawner can never have more
		/// than <see cref="MaxSpawnCount"/> live instances and the reservation alone would do.
		/// </para>
		/// <para>
		/// The headroom is kept because the pool is shared: <see cref="ObjectSpawnerPool"/>
		/// de-duplicates reservations across every spawner using the same prefab, taking the
		/// largest single demand rather than the sum, so a prefab used by many spawners at once
		/// genuinely can need more instances than any one spawner reserved. This covers that
		/// without making the reservation quadratic.
		/// </para>
		/// </remarks>
		[Tooltip("Extra pooled instances beyond MaxSpawnCount, as slack for prefabs shared between spawners.")]
		[Min(0)]
		public int PrewarmHeadroom = 1;

		/// <summary>
		/// If true, a random respawn time is selected within the minimum and maximum range. Otherwise, the initial respawn time is used.
		/// </summary>
		/// <summary>
		/// If true, a random respawn time is selected within the minimum and maximum range. Otherwise, the initial respawn time is used.
		/// </summary>
		[Tooltip("If true a random number will be selected within the minimum and maximum range provided. Otherwise the maximum respawn time will be used.")]
		public bool RandomRespawnTime = true;

		/// <summary>
		/// Shortest delay between respawn checks, in seconds.
		/// </summary>
		[Tooltip("Shortest delay between respawn checks, in seconds. Respawn deadlines are wall-clock, so this only sets how soon after a deadline the object appears - it does not change respawn timing itself.")]
		public float RespawnCheckIntervalMinimum = 3.0f;

		/// <summary>
		/// Longest delay between respawn checks, in seconds.
		/// </summary>
		[Tooltip("Longest delay between respawn checks, in seconds. Each check picks a fresh random delay in this range so spawners do not all poll on the same frame.")]
		public float RespawnCheckIntervalMaximum = 6.0f;

		/// <summary>
		/// If true, a random spawn position is picked inside the bounding box using the current position as the center.
		/// </summary>
		[Tooltip("If true a random spawn position will be picked inside of the bounding box using the current position as the center.")]
		public bool RandomSpawnPosition = true;

		/// <summary>
		/// SphereCast radius used for spawning objects in the world.
		/// </summary>
		/// <summary>
		/// SphereCast radius used for spawning objects in the world.
		/// </summary>
		[Tooltip("SphereCast radius used for spawning objects in the world.")]
		public float SphereRadius = 0.5f;

		/// <summary>
		/// The size of the bounding box used for random spawn position selection.
		/// </summary>
		public Vector3 BoundingBoxSize = Vector3.one;

		/// <summary>
		/// The extents (half-size) of the bounding box, calculated from BoundingBoxSize.
		/// </summary>
		[HideInInspector]
		public Vector3 BoundingBoxExtents = Vector3.one;

		/// <summary>
		/// The list of spawnable settings used to configure each spawnable object.
		/// Supports polymorphic subclasses via <see cref="SerializeReference"/> for type-specific data injection.
		/// </summary>
		[SerializeReference, SubclassSelector]
		public List<SpawnableSettings> Spawnables;

		/// <summary>
		/// Dictionary of currently spawned objects, keyed by their unique ID.
		/// </summary>
		public Dictionary<long, ISpawnable> Spawned = new Dictionary<long, ISpawnable>();

		/// <summary>
		/// List of respawn timers for each spawnable object.
		/// </summary>
		public List<DateTime> SpawnableRespawnTimers = new List<DateTime>();

		/// <summary>
		/// Internal index for linear spawn selection.
		/// </summary>
		/// <summary>
		/// <see cref="Time.time"/> at which <see cref="Update"/> next polls for respawns.
		/// </summary>
		private float nextRespawnCheckTime = 0.0f;

		private int lastSpawnIndex = 0;

		/// <summary>
		/// Cached total spawn chance for weighted spawn selection.
		/// </summary>
		private float cachedTotalSpawnChance = 0f;

		/// <summary>
		/// Called when the network starts. Initializes spawner, validates spawnables, and spawns initial objects.
		/// </summary>
		public override void OnStartNetwork()
		{
			base.OnStartNetwork();

			if (!base.IsServerStarted || Spawnables == null || Spawnables.Count < 1)
			{
				enabled = false;
				return;
			}

			/* Spread the first check across the window rather than starting every spawner's
			 * clock together. A scene that loads hundreds of spawners on one frame would
			 * otherwise have them all poll on the same frame forever after. */
			nextRespawnCheckTime = Time.time + UnityEngine.Random.Range(0.0f, Mathf.Max(0.0f, RespawnCheckIntervalMaximum));

			Transform = transform;

			// Extents are always half of BoundingBoxSize
			BoundingBoxExtents = BoundingBoxSize * 0.5f;

			// Validate spawnables
			for (int i = 0; i < Spawnables.Count; ++i)
			{
				if (Spawnables[i] != null)
					Spawnables[i].OnValidate();
			}

			PrewarmObjectPool();

			InitialSpawnCount = InitialSpawnCount.Clamp(0, MaxSpawnCount);
			for (int i = 0; i < InitialSpawnCount; ++i)
			{
				SpawnObject();
			}
			for (int i = Spawned.Count; i < MaxSpawnCount; ++i)
			{
				SpawnableSettings spawnableSettings = Spawnables[GetSpawnIndex()];
				if (spawnableSettings == null)
				{
					continue;
				}

				// Get a new respawn time for each spawnable
				DateTime respawnTime = GetNextRespawnTime(spawnableSettings);

				// Add a new respawn time
				SpawnableRespawnTimers.Add(respawnTime);
			}
		}

		/// <summary>
		/// Instantiates this spawner's prefabs into the object pool ahead of time.
		/// </summary>
		/// <remarks>
		/// Reserves <see cref="MaxSpawnCount"/> plus <see cref="PrewarmHeadroom"/> of every prefab
		/// this spawner can select. <see cref="ObjectSpawnerPool"/> de-duplicates across spawners,
		/// so ten spawners sharing one prefab reserve the largest single demand rather than ten
		/// times it.
		/// </remarks>
		private void PrewarmObjectPool()
		{
			if (!PrewarmPool || NetworkManager == null)
			{
				return;
			}

			int perPrefab = Mathf.Max(1, MaxSpawnCount + PrewarmHeadroom);

			for (int i = 0; i < Spawnables.Count; ++i)
			{
				SpawnableSettings settings = Spawnables[i];
				if (settings == null || settings.NetworkObject == null)
				{
					continue;
				}

				ObjectSpawnerPool.Reserve(NetworkManager, settings.NetworkObject, perPrefab);
			}
		}

		/// <summary>
		/// Polls for respawns on an interval rather than every frame.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <see cref="TryRespawn"/> walks the whole timer list and evaluates every respawn
		/// condition, so running it per frame costs a scene full of spawners far more than it
		/// buys. Respawn deadlines are wall-clock <see cref="DateTime"/> values, so polling more
		/// slowly does not shift them - it only decides how soon after a deadline passes the
		/// object actually appears, bounded by the interval.
		/// </para>
		/// <para>
		/// The gate lives here rather than inside <see cref="TryRespawn"/> so that direct callers
		/// still get an immediate attempt.
		/// </para>
		/// </remarks>
		void Update()
		{
			if (Time.time < nextRespawnCheckTime)
			{
				return;
			}

			ScheduleNextRespawnCheck();

			TryRespawn();
		}

		/// <summary>
		/// Picks the next respawn check time, re-randomised each pass so spawners that happen to
		/// align on one frame drift apart again instead of staying in lockstep.
		/// </summary>
		private void ScheduleNextRespawnCheck()
		{
			// Tolerate an inverted or negative range in the inspector rather than never polling.
			float minimum = Mathf.Max(0.0f, RespawnCheckIntervalMinimum);
			float maximum = Mathf.Max(minimum, RespawnCheckIntervalMaximum);

			nextRespawnCheckTime = Time.time + UnityEngine.Random.Range(minimum, maximum);
		}

#if UNITY_EDITOR
		/// <summary>
		/// The color used to draw the spawner's gizmo in the editor.
		/// </summary>
		public Color GizmoColor = Color.red;

		/// <summary>
		/// Draws the spawner's bounding box or collider gizmo in the editor for visualization.
		/// </summary>
		void OnDrawGizmos()
		{
			Collider collider = gameObject.GetComponent<Collider>();
			if (collider != null)
			{
				collider.DrawGizmo(GizmoColor);
			}
			else
			{
				Gizmos.color = GizmoColor;
				Gizmos.DrawWireCube(transform.position, BoundingBoxSize);
				ColliderExtensions.DrawCenterMarker(transform.position, GizmoColor);
			}
		}
#endif

		/// <summary>
		/// Despawns the specified spawnable object, schedules its respawn, and removes it from the spawned dictionary.
		/// </summary>
		/// <param name="spawnable">The spawnable object to despawn.</param>
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
			if (!Spawned.Remove(spawnable.ID))
			{
				return;
			}

			// GetNextRespawnTime reads the settings, so capture the time before clearing them.
			// The spawnable is passed too: a settings reference can legitimately be null when the
			// object was adopted rather than spawned here, and the instance itself may still know
			// its own respawn cadence.
			DateTime respawnTime = GetNextRespawnTime(spawnable.SpawnableSettings, spawnable);
			SpawnableRespawnTimers.Add(respawnTime);

			// Clear references to the spawner and settings.
			spawnable.ObjectSpawner = null;
			spawnable.SpawnableSettings = null;

			/* DespawnType.Pool returns the object to FishNet's pool rather than destroying it.
			 * Combined with the pre-warm above, a map's network objects are instantiated once at
			 * load and then recycled for the lifetime of the scene. */
			if (spawnable.NetworkObject != null && spawnable.NetworkObject.IsSpawned)
			{
				ServerManager?.Despawn(spawnable.NetworkObject, DespawnType.Pool);
			}
		}

		/// <summary>
		/// Calculates the next respawn time for a spawnable object based on its settings and spawner configuration.
		/// </summary>
		/// <remarks>
		/// The range comes from <see cref="SpawnableSettings.ResolveRespawnTimeRange"/> rather than
		/// from the settings' fields, so a subclass can answer with its prefab's own cadence when
		/// this spawner has not overridden it. That is what lets an NPC prefab carry a sensible
		/// default and a spawner override it only where a placement genuinely differs.
		/// </remarks>
		/// <param name="spawnableSettings">The settings for the spawnable object, or null.</param>
		/// <param name="spawnable">The instance being despawned, when there is one.</param>
		/// <returns>The DateTime when the object should respawn.</returns>
		private DateTime GetNextRespawnTime(SpawnableSettings spawnableSettings, ISpawnable spawnable = null)
		{
			if (!TryResolveRespawnRange(spawnableSettings, spawnable, out float minimum, out float maximum))
			{
				// Nothing knows a cadence for this object — it was adopted rather than spawned
				// here and is not an NPC. The spawner's own initial respawn time is all that is
				// left to go on.
				return DateTime.UtcNow.Add(TimeSpan.FromSeconds(InitialRespawnTime));
			}

			/* When randomisation is off the MAXIMUM is the delay, which is what the
			 * RandomRespawnTime tooltip has always promised. It used to fall back to
			 * InitialRespawnTime instead — a different setting entirely, defaulting to zero — so
			 * turning randomisation off made a spawner respawn its objects instantly and ignore
			 * every respawn value authored anywhere. */
			float delay = RandomRespawnTime
				? DeterministicRNG.Shared.Range(minimum, maximum)
				: maximum;

			// Return the DateTime of when the object should respawn.
			return DateTime.UtcNow.Add(TimeSpan.FromSeconds(delay));
		}

		/// <summary>
		/// Finds a respawn delay range for an object, from its settings or from the object itself.
		/// </summary>
		/// <param name="spawnableSettings">The settings for the spawnable object, or null.</param>
		/// <param name="spawnable">The instance being despawned, when there is one.</param>
		/// <param name="minimum">Receives the shortest respawn delay in seconds.</param>
		/// <param name="maximum">Receives the longest respawn delay in seconds.</param>
		/// <returns>True when a range was found.</returns>
		private static bool TryResolveRespawnRange(SpawnableSettings spawnableSettings, ISpawnable spawnable, out float minimum, out float maximum)
		{
			if (spawnableSettings != null)
			{
				spawnableSettings.ResolveRespawnTimeRange(out minimum, out maximum);
				return true;
			}

			/* No settings, so ask the instance. An NPC adopted by this spawner rather than spawned
			 * through it still carries its own cadence, and honouring it is strictly better than
			 * the old behaviour of respawning such an object on InitialRespawnTime — which is
			 * zero unless someone set it. */
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
		/// Attempts to respawn objects if their timers have elapsed and respawn conditions are met.
		/// </summary>
		public void TryRespawn()
		{
			if (Spawnables == null ||
				Spawnables.Count < 1 ||
				SpawnableRespawnTimers.Count < 1)
			{
				return;
			}

			// Clear the spawnable timers if we reach our maximum spawn count.
			if (Spawned.Count >= MaxSpawnCount)
			{
				SpawnableRespawnTimers.Clear();
				return;
			}

			/* Iterate backwards. The body removes the entry it fires, and a forward loop that
			 * removes mid-iteration skips the following element. */
			for (int i = SpawnableRespawnTimers.Count - 1; i >= 0; --i)
			{
				DateTime respawnTime = SpawnableRespawnTimers[i];

				if (DateTime.UtcNow >= respawnTime)
				{
					bool shouldRespawn = true;

					// Check OR respawn conditions (any one must be true to allow respawn).
					if (OrConditions != null &&
						OrConditions.Count >= 1)
					{
						shouldRespawn = false;
						foreach (BaseRespawnCondition condition in OrConditions)
						{
							if (condition == null)
							{
								continue;
							}
							if (condition.OnCheckCondition(this))
							{
								shouldRespawn = true;
								break;
							}
						}
					}

					// Check AND respawn conditions (all must be true to allow respawn).
					if (shouldRespawn &&
						TrueConditions != null &&
						TrueConditions.Count >= 1)
					{
						foreach (BaseRespawnCondition condition in TrueConditions)
						{
							if (condition == null)
							{
								continue;
							}
							if (!condition.OnCheckCondition(this))
							{
								shouldRespawn = false;
								break;
							}
						}
					}

					// If all respawn conditions are met, spawn the object and remove its timer.
					if (shouldRespawn)
					{
						/* Remove the timer BEFORE spawning. SpawnObject clears the whole timer
						 * list when it reaches MaxSpawnCount, and the old order then tried to
						 * remove an index from a list that had just been emptied — the count
						 * guard turned that into a silent no-op, leaving a consumed timer in
						 * place whenever the spawner did not hit its cap. */
						SpawnableRespawnTimers.RemoveAt(i);

						SpawnObject();

						/* Keep draining rather than returning after the first spawn. Returning
						 * capped the spawner at one object per call, which was invisible while
						 * this ran every frame — a wiped ten-monster camp refilled in ten frames.
						 * Behind an interval it becomes one object per interval: a camp taking
						 * the better part of a minute to refill, and a hard ceiling on spawn rate
						 * that no authored MinimumRespawnTime can raise. */

						/* SpawnObject empties the timer list the moment Spawned reaches
						 * MaxSpawnCount, and this loop still holds an index into it. Stop on the
						 * cap, and bound-check besides, rather than read past the end. */
						if (Spawned.Count >= MaxSpawnCount ||
							i > SpawnableRespawnTimers.Count)
						{
							break;
						}
					}
				}
			}
		}

		/// <summary>
		/// Updates the cached total spawn chance for weighted spawn selection. Only recalculates if cache is dirty.
		/// </summary>
		private void UpdateTotalSpawnChanceCache()
		{
			if (Spawnables != null && Spawnables.Count > 0 && IsCacheDirty)
			{
				cachedTotalSpawnChance = 0f;
				foreach (var spawnableSettings in Spawnables)
				{
					// Empty slots are normal in scene-authored spawner lists (a sized list
					// whose entries have not all been assigned yet); skip them instead of
					// throwing out of the spawn tick.
					if (spawnableSettings == null)
					{
						continue;
					}
					cachedTotalSpawnChance += spawnableSettings.SpawnChance;
				}
				IsCacheDirty = false;
			}
		}

		/// <summary>
		/// Selects a spawnable index based on weighted random selection using spawn chances.
		/// </summary>
		/// <returns>The index of the selected spawnable.</returns>
		private int GetWeightedSpawnIndex()
		{
			if (Spawnables == null || Spawnables.Count < 1)
			{
				return 0;
			}

			UpdateTotalSpawnChanceCache();

			// Pick a random value between 0 and total spawn chance.
			float randomValue = DeterministicRNG.Shared.Range(0f, cachedTotalSpawnChance);

			float cumulativeChance = 0f;

			// Iterate through the spawnables and select one based on the random value.
			for (int i = 0; i < Spawnables.Count; ++i)
			{
				// Skip unassigned slots — see UpdateTotalSpawnChanceCache.
				if (Spawnables[i] == null)
				{
					continue;
				}
				cumulativeChance += Spawnables[i].SpawnChance;

				// If the random value is less than the cumulative chance, select this spawnable.
				if (randomValue <= cumulativeChance)
				{
					return i; // Return the index of the selected spawnable.
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
			if (Spawnables == null || Spawnables.Count < 1)
			{
				return 0;
			}

			int spawnIndex;
			switch (SpawnType)
			{
				case ObjectSpawnType.Linear:
					spawnIndex = lastSpawnIndex;
					++lastSpawnIndex;
					if (lastSpawnIndex >= Spawnables.Count)
					{
						lastSpawnIndex = 0;
					}
					break;
				case ObjectSpawnType.Random:
					spawnIndex = DeterministicRNG.Shared.Range(0, Spawnables.Count);
					break;
				case ObjectSpawnType.Weighted:
					spawnIndex = GetWeightedSpawnIndex();
					break;
				default:
					return 0;
			}
			// If the spawn index is greater than the number of spawnables, reset to 0.
			if (spawnIndex >= Spawnables.Count)
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

			foreach (ISpawnable spawned in Spawned.Values)
			{
				/* Reference equality against the settings object the spawn was created from, which
				 * SpawnObject assigns below. Comparing the prefab instead would treat two entries
				 * that share a prefab but differ in their overrides as the same spawnable. */
				if (spawned != null &&
					ReferenceEquals(spawned.SpawnableSettings, spawnableSettings))
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
			for (int offset = 0; offset < Spawnables.Count; ++offset)
			{
				int candidate = (spawnIndex + offset) % Spawnables.Count;

				SpawnableSettings candidateSettings = Spawnables[candidate];
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
			if (Spawnables == null || Spawnables.Count < 1)
			{
				return;
			}

			/* Hard cap. TryRespawn checks this before calling, but OnStartNetwork and any external
			 * caller do not, and exceeding the maximum is what turns a deterministic pool
			 * reservation back into unbounded growth. */
			if (Spawned.Count >= MaxSpawnCount)
			{
				return;
			}

			int spawnIndex = GetSpawnIndex();

			/* One live instance per entry, when asked for. The spawn index is chosen from the
			 * configured spawn type alone and knows nothing about what is already alive, so without
			 * this a list of distinct individuals happily spawns the same one twice. */
			if (UniqueSpawnables &&
				!TryResolveUniqueSpawnIndex(ref spawnIndex))
			{
				// Every assigned spawnable is already alive; nothing left that may be spawned.
				return;
			}

			SpawnableSettings spawnableSettings = Spawnables[spawnIndex];
			if (spawnableSettings == null ||
				spawnableSettings.NetworkObject == null)
			{
				return;
			}

			// Calculate spawn position.
			Vector3 spawnPosition = Transform.position;
			if (RandomSpawnPosition)
			{
				// Pick a random spawn position on top of the ground within the bounding box.
				PhysicsScene physicsScene = gameObject.scene.GetPhysicsScene();
				if (physicsScene != null)
				{
					// Get a random point at the top of the bounding box.
					Vector3 origin = new Vector3(DeterministicRNG.Shared.Range(-BoundingBoxExtents.x, BoundingBoxExtents.x),
												 BoundingBoxExtents.y,
												 DeterministicRNG.Shared.Range(-BoundingBoxExtents.z, BoundingBoxExtents.z));

					// Add the spawner position.
					origin += spawnPosition;

					if (physicsScene.SphereCast(origin, SphereRadius, Vector3.down, out RaycastHit hit, BoundingBoxSize.y, Constants.Layers.Obstruction, QueryTriggerInteraction.Ignore))
					{
						spawnPosition = hit.point;
						spawnPosition.y += spawnableSettings.YOffset;
					}
				}
			}

			// Get the prefab for the network object from the spawnable settings.
			NetworkObject prefab = NetworkManager.SpawnablePrefabs.GetObject(true, spawnableSettings.NetworkObject.PrefabId);

			if (prefab != null)
			{
				// Instantiate the object using object pooling.
				NetworkObject nob = NetworkManager.GetPooledInstantiated(spawnableSettings.NetworkObject.PrefabId, spawnableSettings.NetworkObject.SpawnableCollectionId, ObjectPoolRetrieveOption.MakeActive, null, spawnPosition, Transform.rotation, null, true);
				if (nob == null)
				{
					return;
				}

				// Delegate type-specific data injection to the settings subclass.
				spawnableSettings.OnSpawned(nob, this);

				// Move the spawned object to the correct scene.
				UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(nob.gameObject, this.gameObject.scene);

				/* Initialise the brain after the scene move. Initialize warps the NavMeshAgent onto
				 * the mesh at the spawn point, which is what a recycled NPC needs: it comes out of
				 * the pool with its agent re-enabled while the agent still believes it is standing
				 * wherever the previous occupant died. */
				IAIController aiController = nob.GetComponent<IAIController>();
				if (aiController != null)
				{
					aiController.Initialize(spawnPosition);
				}

				// Set up spawnable references before the spawn, so the object's own start
				// callbacks see the spawner that owns it.
				ISpawnable nobSpawnable = nob.GetComponent<ISpawnable>();
				if (nobSpawnable != null)
				{
					nobSpawnable.ObjectSpawner = this;
					nobSpawnable.SpawnableSettings = spawnableSettings;
				}

				// Spawn the object on the server.
				ServerManager.Spawn(nob, null, Transform.gameObject.scene);

				/* Tracked AFTER the spawn, because the key is the scene-object ID and that ID is
				 * assigned in OnStartServer — which runs inside ServerManager.Spawn. Registering
				 * beforehand filed every first-time instance under ID 0: repeated spawns
				 * overwrote one another, so Spawned.Count never approached MaxSpawnCount and the
				 * cap at the top of this method never engaged, while Despawn's
				 * Spawned.Remove(spawnable.ID) looked up the real (negative) ID, missed, and
				 * returned early — leaving the object spawned forever with no respawn queued.
				 * Pooled instances keep their ID across a recycle, so only the first spawn of
				 * each instance was affected, which is exactly the initial world population.
				 *
				 * Indexer, not Add. A pooled object keeps its scene-object ID across a recycle,
				 * so a stale entry left by an object that was despawned some other way would
				 * make Add throw and abort the spawn tick. */
				if (nobSpawnable != null)
				{
					Spawned[nobSpawnable.ID] = nobSpawnable;
				}

				// If we've reached the maximum spawn count, clear respawn timers.
				if (Spawned.Count >= MaxSpawnCount)
				{
					SpawnableRespawnTimers.Clear();
				}
			}
		}
	}
}