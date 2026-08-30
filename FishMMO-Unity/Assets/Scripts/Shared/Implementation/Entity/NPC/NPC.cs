using System;
using System.Collections.Generic;
using UnityEngine;
using FishNet.Component.Transforming;
using FishNet.Observing;
using FishNet.Connection;
using FishNet.Serializing;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Represents a non-player character (NPC) in the game. Handles attribute generation, network payloads, and spawning logic.
	/// </summary>
	[RequireComponent(typeof(AIController))]
	[RequireComponent(typeof(CharacterPredictionController))]
	[RequireComponent(typeof(AbilityController))]
	[RequireComponent(typeof(CooldownController))]
	[RequireComponent(typeof(BuffController))]
	[RequireComponent(typeof(CharacterAttributeController))]
	[RequireComponent(typeof(CharacterDamageController))]
	[RequireComponent(typeof(FactionController))]
	[RequireComponent(typeof(NetworkTransform))]
	[RequireComponent(typeof(NetworkObserver))]
	public class NPC : BaseCharacter, ISceneObject, ISpawnable, IInteractable, ILootableCorpse
	{
		/// <summary>
		/// Static random number generator for NPC attribute seed generation.
		/// </summary>
		private static DeterministicRNG npcSeedGenerator = new DeterministicRNG();

		/// <summary>
		/// Random number generator for this NPC, seeded for deterministic results.
		/// </summary>
		private DeterministicRNG npcRNG;

		/// <summary>
		/// Exposes the seeded RNG for deterministic AI decisions.
		/// All AI subsystems should use this instead of <see cref="DeterministicRNG.Shared"/>
		/// so that behaviour is reproducible given the same seed.
		/// </summary>
		public DeterministicRNG RNG => npcRNG;

		/// <summary>
		/// The seed used for RNG, synchronized over the network.
		/// </summary>
		[SerializeField, ShowReadonly]
		private int npcSeed = 0;

		/// <summary>
		/// Gender selected for this NPC's generated name and model set.
		/// </summary>
		[SerializeField, ShowReadonly]
		private CharacterGender npcGender = CharacterGender.Unspecified;

		/// <summary>
		/// If true, this NPC can be charmed by players.
		/// </summary>
		public bool IsCharmable;

		[Header("Respawn")]
		[Tooltip("Shortest time in seconds before this NPC respawns after its corpse decays. A spawner may override it.")]
		[Min(0f)]
		public float MinimumRespawnTime = 30f;

		/// <summary>
		/// Longest time in seconds before this NPC respawns after its corpse decays.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The prefab is the right home for these because respawn cadence is a property of the
		/// creature, not of the patch of ground it stands on: a wolf should come back at wolf pace
		/// wherever it is placed, and a rare spawn should stay rare at every spawner that can
		/// produce it. Before this, the values lived only on <see cref="NPCSpawnableSettings"/> and
		/// defaulted to zero — so any spawner whose author did not fill them in respawned its
		/// creatures instantly, forever.
		/// </para>
		/// <para>
		/// A spawner can still override the pair for a specific placement — see
		/// <see cref="NPCSpawnableSettings.MinimumRespawnTime"/>.
		/// </para>
		/// </remarks>
		[Tooltip("Longest time in seconds before this NPC respawns after its corpse decays. A spawner may override it.")]
		[Min(0f)]
		public float MaximumRespawnTime = 60f;

		[Header("Corpse Decay")]
		[Tooltip("Seconds the corpse remains visible after death before returning to the object pool.")]
		public float CorpseDecayDuration = 30f;

		/// <summary>
		/// Seconds an empty corpse remains before returning to the object pool.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A body with nothing on it is scenery. The full decay duration exists to give the people
		/// who earned the kill time to walk over and take their loot, and none of that applies once
		/// there is nothing left to take — or once it turns out there never was, which is the case
		/// for every NPC killed by another NPC, by the environment, or by a player with no loot
		/// table configured. Left on the full timer those bodies accumulate: a busy zone ends up
		/// carpeted in corpses that no one can interact with, each still spawned, still observed by
		/// every client in range, and still holding its spawner's slot.
		/// </para>
		/// <para>
		/// Only ever shortens a corpse's remaining life, never extends it, so setting this longer
		/// than <see cref="CorpseDecayDuration"/> simply has no effect.
		/// </para>
		/// <para>
		/// Keep it comfortably longer than the death animation, or bodies will pop out of the world
		/// mid-collapse.
		/// </para>
		/// </remarks>
		[Tooltip("Seconds an empty corpse remains before returning to the pool. Only ever shortens the decay, never extends it.")]
		[Min(0f)]
		public float EmptyCorpseDecayDuration = 5f;

		/// <summary>
		/// How close a player must stand to loot this NPC's corpse.
		/// </summary>
		[Tooltip("Distance within which a player may loot this NPC's corpse.")]
		public float CorpseInteractionRange = 4.0f;

		/// <summary>
		/// Milliseconds a player must wait between interactions with this corpse.
		/// </summary>
		private const double CORPSE_INTERACT_RATE_LIMIT = 60.0;

		/// <summary>
		/// Whether the NPC is currently in corpse state (dead but still visible).
		/// </summary>
		private bool isCorpse;

		/// <summary>
		/// Remaining seconds before the corpse returns to the object pool.
		/// </summary>
		private float corpseDecayTimer;

		/// <summary>
		/// True once the decay timer has been cut short because the corpse was found empty.
		/// </summary>
		/// <remarks>
		/// Latched so the clamp is applied once rather than re-evaluated against a shrinking
		/// timer every tick, and so <see cref="HasLoot"/> — which walks the slot list — is only
		/// asked until it first answers no.
		/// </remarks>
		private bool corpseDecayShortened;

		[Header("Loot")]
		[Tooltip("What this NPC's corpse may hold. Rolled once, on the server, at the moment of death.")]
		public LootTableTemplate LootTable;

		/// <summary>
		/// ECA triggers fired server-side when a player interacts with this NPC's corpse.
		/// </summary>
		[Tooltip("Triggers invoked server-side when a player loots this NPC's corpse.")]
		[SerializeField]
		private List<Trigger> onInteractTriggers = new List<Trigger>();

		/// <summary>
		/// The corpse's item slots. Emptied slots stay as nulls so indices remain stable.
		/// </summary>
		private readonly List<Item> lootItems = new List<Item>();

		/// <summary>
		/// Currency remaining on the corpse.
		/// </summary>
		private long lootCurrency;

		/// <summary>
		/// Character IDs allowed to loot this corpse, snapshotted from the damage controller's
		/// contribution list at the moment of death.
		/// </summary>
		private readonly HashSet<long> eligibleLooters = new HashSet<long>();

		/// <summary>
		/// Connections with this corpse's loot window open. Server-only.
		/// </summary>
		private readonly HashSet<NetworkConnection> lootViewers = new HashSet<NetworkConnection>();

		/// <summary>
		/// Squared corpse interaction range, resolved when the corpse is created.
		/// </summary>
		private float corpseInteractionRangeSqr;

		/// <summary>
		/// True when entering corpse state is what disabled this NPC's brain.
		/// </summary>
		/// <remarks>
		/// Tracked so <see cref="ResetState"/> restores only what the corpse path switched off.
		/// Unconditionally re-enabling the controller would override a prefab that deliberately
		/// ships with its brain disabled.
		/// </remarks>
		private bool aiDisabledByCorpse;

		/// <inheritdoc />
		public event Action<ILootableCorpse> OnCorpseExpired;

		/// <summary>
		/// Database of attribute bonuses for this NPC.
		/// </summary>
		public NPCAttributeDatabase AttributeBonuses;

		/// <summary>
		/// Ability templates this NPC can use. Populated in the inspector.
		/// Each template is learned as an <see cref="Ability"/> instance during
		/// <see cref="OnStartServer"/>, before clients receive the spawn payload.
		/// </summary>
		[Header("Abilities")]
		[Tooltip("Ability templates this NPC knows. Learned on server start.")]
		public List<AbilityTemplate> Abilities = new List<AbilityTemplate>();

		/// <summary>
		/// Reference to the spawner that created this NPC.
		/// </summary>
		[SerializeField, ShowReadonly]
		private ObjectSpawner objectSpawner;

		/// <summary>
		/// Reference to the spawner that created this NPC.
		/// </summary>
		public ObjectSpawner ObjectSpawner
		{
			get { return objectSpawner; }
			set { objectSpawner = value; }
		}

		/// <summary>
		/// Settings used when spawning this NPC.
		/// </summary>
		[SerializeReference, ShowReadonly]
		private SpawnableSettings spawnableSettings;

		/// <summary>
		/// Settings used when spawning this NPC.
		/// </summary>
		public SpawnableSettings SpawnableSettings
		{
			get { return spawnableSettings; }
			set { spawnableSettings = value; }
		}

#if UNITY_EDITOR
		/// <summary>
		/// Keeps the respawn range orderable in the inspector.
		/// </summary>
		/// <remarks>
		/// An inverted range is not merely odd: the RNG returns the minimum whenever
		/// <c>min &gt;= max</c>, so a prefab authored 60-to-30 would silently respawn at a fixed
		/// 60 seconds and never vary. Clamping here makes that impossible to author by accident.
		/// </remarks>
		protected virtual void OnValidate()
		{
			if (CorpseDecayDuration < 0f)
			{
				CorpseDecayDuration = 0f;
			}
			if (EmptyCorpseDecayDuration < 0f)
			{
				EmptyCorpseDecayDuration = 0f;
			}
			if (MinimumRespawnTime < 0f)
			{
				MinimumRespawnTime = 0f;
			}
			if (MaximumRespawnTime < MinimumRespawnTime)
			{
				MaximumRespawnTime = MinimumRespawnTime;
			}
		}
#endif

		/// <summary>
		/// Called when the NPC is awakened. Handles name cleanup and registration.
		/// </summary>
		public override void OnAwake()
		{
			base.OnAwake();

			// Set the loaded flag to allow controllers to check if the NPC is fully loaded and in the world. 
			// This is important for proper attribute clamping and preventing actions before the NPC is fully initialized.
			EnableFlags(CharacterFlags.IsLoaded);

#if !UNITY_SERVER
			// Remove (Clone) from the GameObject name for clarity in the editor.
			GameObject.name = GameObject.name.Replace("(Clone)", "");
			if (CharacterNameLabel != null)
			{
				CharacterNameLabel.text = GameObject.name;
			}
#endif
		}

		/// <summary>
		/// Called when the server starts for this NPC. Runs on every spawn including pool reuse.
		/// Re-rolls the seed, RNG, gender, and name. Then applies attribute bonuses and learns abilities.
		/// Spawner overrides (AttributeBonuses, CorpseDecayDuration) are injected before this runs.
		/// </summary>
		/// <remarks>
		/// Deliberately not wrapped in <c>#if UNITY_SERVER</c>. FishNet only calls this on a peer
		/// that is actually running a server, so the compile-time gate bought nothing — and it cost
		/// a great deal: in an editor or host build (where UNITY_SERVER is undefined) the override
		/// did not exist, so no NPC ever rolled its RNG, applied its attribute bonuses, or learned
		/// a single ability. NPC combat could not be exercised anywhere except a dedicated server
		/// build.
		/// </remarks>
		public override void OnStartServer()
		{
			base.OnStartServer();

			/* Registration moved here from OnAwake's #if UNITY_SERVER arm, which is undefined when
			 * the scene server runs from the editor — so no NPC ever entered the registry there,
			 * every NPC kept ID 0, and the interaction path could not resolve a single one. This
			 * is the same compile-time gate already removed from this method's body below, and it
			 * cost the same thing twice. Re-registering a pooled NPC keeps its existing ID. */
			SceneObject.Register(this);

			// Re-roll seed and RNG on every spawn (pool reuse).
			// ResetState clears these when the object returns to the pool.
			npcSeed = npcSeedGenerator.Next();
			npcRNG = new DeterministicRNG(npcSeed);

			// Regenerate gender and name for model selection and display.
			SceneObjectNamer sceneObjectNamer = GetComponent<SceneObjectNamer>();
			if (sceneObjectNamer != null)
			{
				npcGender = sceneObjectNamer.EnsureGeneratedGender();
			}

			AddNPCAttributes(true);
			ApplyInstanceDifficulty();
			LearnNPCAbilities();

			// Subscribe to the server tick for corpse decay timer.
			base.TimeManager.OnTick += CorpseDecayTick;
		}

		/// <summary>
		/// Called when the NPC is destroyed. Unregisters from the scene object registry.
		/// </summary>
		public override void OnDestroying()
		{
			SceneObject.Unregister(this);
		}

		/// <summary>
		/// Resets the NPC's state for object pool reuse. Clears RNG, spawner references, and client-side tracking.
		/// </summary>
		/// <param name="asServer">Whether the reset is performed on the server.</param>
		public override void ResetState(bool asServer)
		{
			// Unsubscribe from tick to prevent stale timer during pool idle.
			// Unconditional, to match the now-unconditional subscription in OnStartServer.
			if (base.TimeManager != null)
				base.TimeManager.OnTick -= CorpseDecayTick;

			NotifyCorpseExpired();

			isCorpse = false;
			corpseDecayTimer = 0f;
			corpseDecayShortened = false;
			corpseInteractionRangeSqr = 0f;
			OnCorpseExpired = null;

			// Give the brain back, if the corpse path is what took it away.
			if (aiDisabledByCorpse)
			{
				AIController ai = GetComponent<AIController>();
				if (ai != null)
				{
					ai.enabled = true;
				}
				aiDisabledByCorpse = false;
			}

			/* Loot is per-death state and must not survive into the next occupant of this pool
			 * slot. Anything still here was never taken — the corpse decayed with items on it —
			 * so it is dropped rather than granted. */
			lootItems.Clear();
			lootCurrency = 0;
			eligibleLooters.Clear();
			lootViewers.Clear();

			base.ResetState(asServer);

#if !UNITY_SERVER
			ClientCharacters.Remove(ID);
#endif

			/* Reset flags to the baseline a freshly spawned NPC expects.
			 *
			 * This is the only place it can happen. OnAwake sets IsLoaded and runs exactly once
			 * per pooled instance, while Despawn clears it on every death — so from its second
			 * life onwards a recycled NPC came back permanently unloaded, which
			 * CharacterResourceAttribute.ClampCurrentValue reads to mean "do not clamp to
			 * FinalValue" and therefore left its health able to sit above its own maximum.
			 * IsDead is cleared here for the same reason: Kill now sets it, and a corpse returned
			 * to the pool still carrying it would come back dead and unkillable. */
			Flags = 0;
			EnableFlags(CharacterFlags.IsLoaded);

			npcRNG = null;
			npcSeed = 0;
			npcGender = CharacterGender.Unspecified;
			ObjectSpawner = null;
			SpawnableSettings = null;
		}

		/// <summary>
		/// Reads the NPC's payload from the network, including ID and attribute seed. Applies attributes and sets up model.
		/// </summary>
		/// <param name="connection">The network connection.</param>
		/// <param name="reader">The network reader.</param>
		public override void ReadPayload(NetworkConnection connection, Reader reader)
		{
			ID = reader.ReadInt64();
			SceneObject.Register(this, true);

			// Read the attribute seed for deterministic attribute generation.
			npcSeed = reader.ReadInt32();
			npcGender = (CharacterGender)reader.ReadUInt8Unpacked();

			/* Read and apply flags before anything below builds the model. The animation
			 * controller poses an already-dead character when it acquires its animator, and that
			 * acquisition is driven by the model instantiation further down — so the flag has to
			 * be in place first or the corpse comes up standing. */
			Flags = reader.ReadInt32();

			// Instantiate the client side NPC RNG with the received seed.
			npcRNG = new DeterministicRNG(npcSeed);

			//Log.Debug($"Received NPC RNG Seed {npcSeed}");

			/* Clients still draw the attribute values from npcRNG so the model index drawn below
			 * matches the server's stream, but they must NOT apply them: CharacterAttributeController
			 * has already read the server's ExternalModifier (bonus + instance difficulty + buffs)
			 * from its own payload block, which precedes this one on every NPC prefab, and
			 * SetModifier would replace it with the bonus alone. */
			AddNPCAttributes(false);

#if !UNITY_SERVER
			ClientCharacters[ID] = this;

			// FactionController stores a reference to the RaceTemplate.
			if (this.TryGet(out IFactionController factionController))
			{
				RaceTemplate raceTemplate = factionController.RaceTemplate;
				int modelIndex = -1;
				int modelCount = raceTemplate == null ? 0 : raceTemplate.GetModelCount(npcGender);
				if (modelCount > 0)
				{
					// Pick a random model for this NPC using the RNG.
					modelIndex = npcRNG.Next(0, modelCount);

					InstantiateRaceModelFromIndex(raceTemplate, modelIndex, npcGender);
				}
			}
#endif
		}

		/// <summary>
		/// Writes the NPC's payload to the network, including ID and attribute seed. Ensures deterministic attribute generation on clients.
		/// </summary>
		/// <param name="connection">The network connection.</param>
		/// <param name="writer">The network writer.</param>
		public override void WritePayload(NetworkConnection connection, Writer writer)
		{
			writer.WriteInt64(ID);

			// Write the seed for clients to use for determinism.
			writer.WriteInt32(npcSeed);
			SceneObjectNamer sceneObjectNamer = GetComponent<SceneObjectNamer>();
			npcGender = sceneObjectNamer == null ? CharacterGender.Unspecified : sceneObjectNamer.EnsureGeneratedGender();
			writer.WriteUInt8Unpacked((byte)npcGender);

			/* Flags, so a client that starts observing an NPC that is ALREADY a corpse poses it
			 * correctly. The observers RPC covers only clients who were watching at the moment of
			 * death; PlayerCharacter has carried its flags in the payload for exactly this reason
			 * and NPCs did not, which is why walking up to an existing corpse showed it standing. */
			writer.WriteInt32(Flags);

			//Log.Debug($"Writing NPC RNG Seed {npcSeed}");
		}

		/// <summary>
		/// Enters corpse state or returns to pool. On first call after death, the NPC
		/// becomes a corpse (visible, immobile, immortal, and lootable) for CorpseDecayDuration
		/// seconds. After the timer expires, the object is returned to FishNet's pool for reuse.
		/// </summary>
		/// <remarks>
		/// Called from the server's <c>OnKilled</c> subscriber. Everything it does is server state;
		/// clients learn that the NPC is a corpse from <see cref="CharacterFlags.IsDead"/>, which
		/// travels both live (an observers RPC from the damage controller) and in the spawn payload
		/// for anyone who arrives afterwards.
		/// </remarks>
		public virtual void Despawn()
		{
			/* Server only, explicitly. Everything below is authoritative state — the loot roll,
			 * the contributor snapshot, the decay clock — and a client has no business holding
			 * any of it. Nothing reaches here on a client today, since the only caller is the
			 * server's OnKilled subscriber and ObjectSpawner disables itself off the server; the
			 * guard is here so that stays true of any future caller rather than by coincidence.
			 * Clients derive corpse state from the replicated dead flag instead — see IsCorpse. */
			if (!base.IsServerStarted) return;

			if (isCorpse) return;

			/* IsLoaded is what gates resource clamping, so a corpse must not carry it — but note
			 * that ResetState is what puts it back for the next occupant of this pool slot.
			 * Clearing it here and restoring it in OnAwake, as this used to, meant every recycled
			 * NPC after its first death came back permanently unloaded. */
			DisableFlags(CharacterFlags.IsLoaded);

			// Enter corpse state -- stay spawned so clients see the death animation.
			isCorpse = true;
			corpseDecayTimer = CorpseDecayDuration;
			corpseDecayShortened = false;
			corpseInteractionRangeSqr = CorpseInteractionRange * CorpseInteractionRange;

			// Disable AI so the corpse does not move or fight.
			AIController ai = GetComponent<AIController>();
			if (ai != null)
			{
				/* Remembered, because nothing else would ever switch it back on. Update() is what
				 * drives the whole brain — LOD scheduling, state dispatch, target selection — and
				 * a disabled MonoBehaviour does not receive it. Before this, the first death of a
				 * pooled NPC disabled its controller permanently: every later occupant of that
				 * pool slot spawned, stood still, and never thought again. */
				aiDisabledByCorpse = ai.enabled;
				ai.enabled = false;

				/* A corpse holds no grudges. Beyond being wrong, a populated threat table keeps
				 * AggressionState.HasAggression true, which is exactly the flag
				 * AggressionDispatcher uses to decide who is worth delivering heal and kill
				 * events to — so every corpse in the scene would be walked and handed every such
				 * event for the whole of its decay. */
				ai.AggressionState?.Clear();
			}

			// Prevent the corpse from being killed again.
			if (TryGet(out ICharacterDamageController dc))
			{
				dc.Immortal = true;
			}

			BuildCorpseLoot(dc);
		}

		/// <summary>
		/// Rolls the loot table and snapshots who is allowed to take from it.
		/// </summary>
		/// <remarks>
		/// Both halves happen exactly once, here, because both must be settled before the first
		/// player can possibly interact. Rolling lazily on first open would let the contents depend
		/// on who opened it first; taking the contributor list later would lose it, since the
		/// combat timer expires contributions and a corpse is out of combat by definition.
		/// </remarks>
		/// <param name="damageController">This NPC's damage controller, which owns the contributor list.</param>
		private void BuildCorpseLoot(ICharacterDamageController damageController)
		{
			lootItems.Clear();
			lootCurrency = 0;
			eligibleLooters.Clear();

			/* Consume the contributors even when there is no loot table. The list is a link back
			 * from every contributor to this NPC, and leaving it in place would keep a pooled
			 * corpse attached to players who have long since moved on. */
			if (damageController != null &&
				damageController.TryConsumeContributors(out List<long> contributors) &&
				contributors != null)
			{
				for (int i = 0; i < contributors.Count; ++i)
				{
					eligibleLooters.Add(contributors[i]);
				}
			}

			if (LootTable == null || eligibleLooters.Count < 1)
			{
				// Nobody earned rights, so there is nothing worth rolling — an unlootable pile is
				// just a spawn payload every observer pays for.
				return;
			}

			/* Loot is rolled at the difficulty of the scene the NPC died in.
			 *
			 * Read here rather than remembered from spawn deliberately: what a corpse is worth is
			 * decided when it is made, and a scene's rules are fixed for its whole life, so the two
			 * cannot disagree. The open world publishes no rules and gets 1 and 1. */
			DungeonDifficultyRegistry.GetLootMultipliers(
				gameObject.scene.handle,
				out float lootQuantityMultiplier,
				out float lootCurrencyMultiplier);

			LootTable.Roll(npcRNG, lootItems, out int rolledCurrency, lootQuantityMultiplier, lootCurrencyMultiplier);
			lootCurrency = rolledCurrency;
		}

		/// <summary>
		/// Returns the NPC to the object pool immediately. Called when the corpse
		/// decay timer expires or on server shutdown.
		/// </summary>
		/// <remarks>
		/// Routes through the owning <see cref="ObjectSpawner"/> so a respawn is scheduled, and
		/// despawns directly when there is no spawner. The fallback matters: an NPC placed by
		/// script or adopted rather than spawned has no spawner, and the null-conditional this
		/// replaced meant such an NPC's corpse never decayed at all — it sat in the world as an
		/// immortal, AI-disabled, permanently lootable body that nothing would ever collect.
		/// </remarks>
		public void ReturnToPool()
		{
			// Before the despawn, while the scene object ID a client is holding still resolves.
			NotifyCorpseExpired();

			isCorpse = false;
			corpseDecayTimer = 0f;

			ObjectSpawner spawner = ObjectSpawner;
			if (spawner != null)
			{
				spawner.Despawn(this);
				return;
			}

			if (base.IsServerStarted && NetworkObject != null && NetworkObject.IsSpawned)
			{
				NetworkManager.ServerManager.Despawn(NetworkObject, FishNet.Object.DespawnType.Pool);
			}
		}

		/// <summary>
		/// Tells anyone with this corpse's loot window open that it is going away.
		/// </summary>
		private void NotifyCorpseExpired()
		{
			if (lootViewers.Count < 1 && OnCorpseExpired == null)
			{
				return;
			}

			try
			{
				OnCorpseExpired?.Invoke(this);
			}
			catch (Exception ex)
			{
				FishMMO.Logging.Log.Error("NPC", $"An OnCorpseExpired subscriber threw for corpse {ID}: {ex}");
			}

			lootViewers.Clear();
		}

		/// <summary>
		/// Called each server tick to advance the corpse decay timer.
		/// </summary>
		private void CorpseDecayTick()
		{
			if (!isCorpse) return;

			/* Cut the timer short the moment the body is empty.
			 *
			 * Done here, on the tick, rather than at each of the places that remove loot — taking
			 * an item, taking currency, taking everything, and rolling a table that produced
			 * nothing are four separate paths, and a fifth would be easy to add without
			 * remembering this. Evaluating the condition instead of trusting callers to report it
			 * means every one of them is covered, including the case where the corpse was never
			 * lootable in the first place.
			 *
			 * It also sidesteps the ordering hazard that per-call-site shortening would carry: a
			 * take that fails to reach the looter's inventory puts the item straight back, and
			 * shortening at the moment of removal would leave a still-full corpse on the short
			 * timer. Both halves of that happen inside one broadcast handler, so no tick can
			 * observe the gap. */
			if (!corpseDecayShortened && !HasLoot)
			{
				corpseDecayShortened = true;

				// Clamp, never extend — a longer empty duration than the full one is a
				// configuration mistake, not an instruction to keep the body around.
				float emptyDuration = Mathf.Max(0f, EmptyCorpseDecayDuration);
				if (corpseDecayTimer > emptyDuration)
				{
					corpseDecayTimer = emptyDuration;
				}
			}

			corpseDecayTimer -= (float)base.TimeManager.TickDelta;
			if (corpseDecayTimer <= 0f)
				ReturnToPool();
		}

		// ───── Corpse Loot ──────────────────────────────────────────────────

		/// <summary>
		/// True while this NPC is a lootable corpse.
		/// </summary>
		/// <remarks>
		/// Answers from whichever source is authoritative for the peer asking. The server owns
		/// <see cref="isCorpse"/> outright; a client has no copy of it and reads the replicated
		/// dead flag instead, which arrives live by observers RPC and in the spawn payload for
		/// anyone who turns up later. Without the client arm this property is simply false on
		/// every client, and the corpse is never offered as an interaction target at all.
		/// </remarks>
		public bool IsCorpse => base.IsServerStarted ? isCorpse : IsFlagged(CharacterFlags.IsDead);

		/// <inheritdoc />
		public IReadOnlyList<Item> LootItems => lootItems;

		/// <inheritdoc />
		public long LootCurrency => lootCurrency;

		/// <inheritdoc />
		public IReadOnlyCollection<NetworkConnection> LootViewers => lootViewers;

		/// <inheritdoc />
		public bool HasLoot
		{
			get
			{
				if (lootCurrency > 0)
				{
					return true;
				}
				for (int i = 0; i < lootItems.Count; ++i)
				{
					if (lootItems[i] != null)
					{
						return true;
					}
				}
				return false;
			}
		}

		/// <inheritdoc />
		public bool IsEligibleLooter(long characterID)
		{
			return eligibleLooters.Contains(characterID);
		}

		/// <inheritdoc />
		public bool TryTakeLootItem(int slot, out Item item)
		{
			item = null;

			if (!isCorpse ||
				slot < 0 ||
				slot >= lootItems.Count)
			{
				return false;
			}

			item = lootItems[slot];
			if (item == null)
			{
				return false;
			}

			/* Emptied rather than removed. Compacting the list would renumber every later slot,
			 * and a second looter's in-flight request names a slot by index — it would land on
			 * whatever slid into that position. */
			lootItems[slot] = null;
			return true;
		}

		/// <inheritdoc />
		public bool ReturnLootItem(Item item, int slot)
		{
			if (item == null ||
				slot < 0 ||
				slot >= lootItems.Count ||
				lootItems[slot] != null)
			{
				return false;
			}

			lootItems[slot] = item;
			return true;
		}

		/// <inheritdoc />
		public bool TryTakeLootCurrency(long maximum, out long amount)
		{
			amount = 0;

			if (!isCorpse || maximum < 1 || lootCurrency < 1)
			{
				return false;
			}

			amount = lootCurrency < maximum ? lootCurrency : maximum;
			lootCurrency -= amount;
			return true;
		}

		/// <inheritdoc />
		public void ReturnLootCurrency(long amount)
		{
			if (amount > 0)
			{
				lootCurrency += amount;
			}
		}

		/// <inheritdoc />
		public void AddLootViewer(NetworkConnection connection)
		{
			if (connection != null)
			{
				lootViewers.Add(connection);
			}
		}

		/// <inheritdoc />
		public void RemoveLootViewer(NetworkConnection connection)
		{
			if (connection != null)
			{
				lootViewers.Remove(connection);
			}
		}

		// ───── Interactable ─────────────────────────────────────────────────

		/// <inheritdoc />
		public List<Trigger> OnInteractTriggers => onInteractTriggers;

		/// <inheritdoc />
		/// <remarks>
		/// Unlike every other interactable, an NPC with no triggers is not a misconfiguration:
		/// looting is wired directly into the interaction handler precisely so that a creature
		/// with an empty list is still lootable. Triggers here are the optional extras — an
		/// achievement, a quest update, a line of dialogue on the body — so an empty list is
		/// silent.
		/// </remarks>
		public bool ExecuteOnInteract(EventData eventData)
		{
			if (onInteractTriggers == null || onInteractTriggers.Count < 1)
			{
				return false;
			}

			bool fired = false;
			for (int i = 0; i < onInteractTriggers.Count; ++i)
			{
				Trigger trigger = onInteractTriggers[i];
				if (trigger == null)
				{
					Log.Warning("NPC", $"'{Name}' has an empty entry at index {i} of its OnInteract triggers; skipping it.");
					continue;
				}
				trigger.Execute(eventData);
				fired = true;
			}
			return fired;
		}

		/// <summary>
		/// The interaction title. Empty while alive, so a living NPC is not advertised as
		/// interactable and its world label is left to the naming components.
		/// </summary>
		public virtual string Title => IsFlagged(CharacterFlags.IsDead) ? "Corpse" : string.Empty;

		/// <summary>
		/// The colour of the corpse title in the world UI.
		/// </summary>
		public virtual Color TitleColor => TinyColor.ToUnityColor(TinyColor.slateGrey);

		/// <inheritdoc />
		public bool InRange(Transform transform)
		{
			if (transform == null || Transform == null)
			{
				return false;
			}
			// Resolved on death rather than in Awake, so an inspector change or a spawner override
			// to CorpseInteractionRange takes effect on the very next corpse.
			float rangeSqr = corpseInteractionRangeSqr > 0f
				? corpseInteractionRangeSqr
				: CorpseInteractionRange * CorpseInteractionRange;
			return (Transform.position - transform.position).sqrMagnitude < rangeSqr;
		}

		/// <summary>
		/// Returns true when the given player may open this NPC's loot window.
		/// </summary>
		/// <remarks>
		/// Runs on both sides and answers differently on each, deliberately. The client knows only
		/// that the NPC is dead, which is all it needs to decide whether to send a request; the
		/// server additionally holds the contributor snapshot and is the only place eligibility is
		/// actually enforced. A client that lies here gets a refusal, not loot.
		/// <para>
		/// Pure, like every other <see cref="IInteractable.CanInteract"/>. The corpse rate limit is
		/// spent through <see cref="TryConsumeInteractRateLimit"/> instead.
		/// </para>
		/// </remarks>
		public virtual bool CanInteract(IPlayerCharacter character)
		{
			if (character == null)
			{
				return false;
			}

			if (!IsCorpse)
			{
				return false;
			}

			if (!InRange(character.Transform))
			{
				return false;
			}

			if (base.IsServerStarted &&
				(!IsEligibleLooter(character.ID) || !HasLoot))
			{
				return false;
			}

			return true;
		}

		/// <inheritdoc />
		public bool TryConsumeInteractRateLimit(IPlayerCharacter character)
		{
			if (character == null)
			{
				return false;
			}
			if (character.NextInteractTime >= DateTime.UtcNow)
			{
				return false;
			}
			character.NextInteractTime = DateTime.UtcNow.AddMilliseconds(CORPSE_INTERACT_RATE_LIMIT);
			return true;
		}

		/// <inheritdoc />
		public double InteractRateLimit => CORPSE_INTERACT_RATE_LIMIT;

		/// <summary>
		/// Creates <see cref="Ability"/> instances from the inspector-configured
		/// <see cref="Abilities"/> list and teaches them to the NPC's <see cref="AbilityController"/>.
		/// Uses the template's <see cref="AbilityTemplate.ID"/> as the ability instance ID so that
		/// cooldown tracking, activation, and network serialization all work correctly.
		/// Called during <see cref="OnStartServer"/> before <c>WritePayload</c> broadcasts to clients.
		/// </summary>
		private void LearnNPCAbilities()
		{
			if (Abilities == null || Abilities.Count < 1)
			{
				return;
			}
			if (!this.TryGet(out IAbilityController abilityController))
			{
				return;
			}

			for (int i = 0; i < Abilities.Count; i++)
			{
				AbilityTemplate template = Abilities[i];
				if (template == null)
				{
					continue;
				}

				// Use the template ID as the ability instance ID.
				// NPCs don't craft abilities so there's no DB-assigned ID.
				Ability ability = new Ability((long)template.ID, template);
				abilityController.LearnAbility(ability);
			}
		}

		/// <summary>
		/// Scales this NPC for the difficulty of the dungeon instance it spawned into.
		/// Server only, and only inside an instance that declares rules.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Runs after <see cref="AddNPCAttributes"/>, on top of whatever the spawner and the
		/// prefab already decided. The order is the point: a difficulty describes how much harder
		/// this dungeon is than itself, not what its enemies are, so it multiplies the finished
		/// figures rather than replacing any of them. A zone that varies its NPCs by spawner keeps
		/// that variation on every difficulty.
		/// </para>
		/// <para>
		/// Resource attributes are scaled as a group because they are the one group the code can
		/// identify. Everything else is named explicitly by the difficulty — there is no built-in
		/// notion of which attribute means damage, and guessing at one would be wrong for any
		/// build that spreads it across several attributes or calls it something else.
		/// </para>
		/// <para>
		/// A resource's current value is raised with its maximum. Scaling only the ceiling would
		/// spawn every enemy in a hard dungeon already wounded, in exact proportion to how much
		/// harder the dungeon was supposed to be.
		/// </para>
		/// </remarks>
		private void ApplyInstanceDifficulty()
		{
			if (!DungeonDifficultyRegistry.TryGet(gameObject.scene.handle, out DungeonDifficultyDefinition difficulty) ||
				difficulty == null ||
				!this.TryGet(out ICharacterAttributeController attributeController))
			{
				return;
			}

			if (difficulty.EnemyResourceMultiplier != 1.0f && difficulty.EnemyResourceMultiplier > 0.0f)
			{
				foreach (CharacterResourceAttribute resource in attributeController.ResourceAttributes.Values)
				{
					if (resource == null)
					{
						continue;
					}

					int current = resource.Value;
					int scaled = Mathf.Max(1, Mathf.RoundToInt(current * difficulty.EnemyResourceMultiplier));
					/* Named, so it can be released. Dungeon scaling had NO reversal at all: it was
					 * added once at spawn and survived until RestoreTemplateBaseline zeroed every
					 * modifier on the way back into the pool.
					 *
					 * Id ZERO: this is the sheet-wide multiplier and names no template, which is what
					 * keeps it distinct from the per-template entries written below. */
					resource.SetSource(ModifierSource.DungeonScaling(), scaled - current);
					/* Filled from the SETTLED final value, not from the local `scaled`. A resource
					 * this loop touches may also be named by EnemyAttributeScalars below, or carry an
					 * NpcBonus, and `scaled` accounts for none of that — it would leave the NPC
					 * spawning on a fraction of its own maximum. */
					resource.SetCurrentValue(resource.FinalValue);
				}
			}

			if (difficulty.EnemyAttributeScalars == null)
			{
				return;
			}

			for (int i = 0; i < difficulty.EnemyAttributeScalars.Count; ++i)
			{
				DungeonAttributeScalar scalar = difficulty.EnemyAttributeScalars[i];
				if (scalar == null ||
					scalar.Template == null ||
					scalar.Multiplier == 1.0f ||
					scalar.Multiplier <= 0.0f)
				{
					continue;
				}

				/* Keyed by the template this entry NAMES, so it sits alongside the sheet-wide
				 * resource multiplier written above rather than replacing it. Both were keyed
				 * DungeonScaling with id zero, and SetSource states a contribution rather than adding
				 * to it — so a resource singled out for extra scaling silently lost the group
				 * multiplier and came out weaker than one that was not mentioned at all. Two entries
				 * also mean two things a designer can reason about independently. */
				if (attributeController.TryGetAttribute(scalar.Template, out CharacterAttribute attribute))
				{
					int current = attribute.Value;
					attribute.SetSource(ModifierSource.DungeonScaling(scalar.Template.ID),
						Mathf.RoundToInt(current * scalar.Multiplier) - current);
				}
				else if (attributeController.TryGetResourceAttribute(scalar.Template, out CharacterResourceAttribute resource))
				{
					/* A resource named explicitly as well as covered by the group multiplier is
					 * scaled twice, and that is intended: the group figure is "everything tougher"
					 * and a named entry is "this one especially". The two contributions ADD, which is
					 * what the separate keys restore — x2 group and x1.5 named leave the resource at
					 * 2.5x its base, the behaviour this had before the ledger collapsed them. */
					int current = resource.Value;
					int scaled = Mathf.Max(1, Mathf.RoundToInt(current * scalar.Multiplier));
					resource.SetSource(ModifierSource.DungeonScaling(scalar.Template.ID), scaled - current);
					// The settled maximum, which now includes the group multiplier as well as this entry.
					resource.SetCurrentValue(resource.FinalValue);
				}
			}
		}

		/// <summary>
		/// Applies attribute bonuses to this NPC using the attribute database and random generator.
		/// </summary>
		private void AddNPCAttributes(bool asServer)
		{
			if (npcRNG == null ||
				AttributeBonuses == null ||
				AttributeBonuses.Attributes == null ||
				!this.TryGet(out ICharacterAttributeController attributeController))
			{
				return;
			}

			for (int entryIndex = 0; entryIndex < AttributeBonuses.Attributes.Count; ++entryIndex)
			{
				NPCAttribute attribute = AttributeBonuses.Attributes[entryIndex];
				int value;
				if (attribute.IsRandom)
				{
					value = npcRNG.Next(attribute.Min, attribute.Max);
				}
				else
				{
					value = attribute.Max;
				}

				if (!asServer)
				{
					// RNG consumed above; the modifier itself arrived in the attribute payload.
					continue;
				}

				/* Keyed by the template this bonus NAMES *and* its position in the list.
				 * AttributeBonuses is authored and nothing stops it naming one template twice — a
				 * designer splitting a roll into a flat part and a scalar part, say. The template
				 * alone does not tell those two apart: they key the same entry, the second replaces
				 * the first, and half the roll vanishes with no warning. The index is what differs. */
				if (attributeController.TryGetAttribute(attribute.Template, out CharacterAttribute characterAttribute))
				{
					int old = characterAttribute.Value;
					ModifierSource source = ModifierSource.NpcBonus(attribute.Template.ID, entryIndex);

					if (attribute.IsScalar)
					{
						int newValue = characterAttribute.Value.GetPercentOf(value);
						characterAttribute.SetSource(source, newValue - old);
					}
					else
					{
						characterAttribute.SetSource(source, value - old);
					}
				}
				else if (attributeController.TryGetResourceAttribute(attribute.Template, out CharacterResourceAttribute characterResourceAttribute))
				{
					int old = characterResourceAttribute.Value;
					ModifierSource source = ModifierSource.NpcBonus(attribute.Template.ID, entryIndex);

					if (attribute.IsScalar)
					{
						int newValue = characterResourceAttribute.Value.GetPercentOf(value);
						int modifier = newValue - old;

						characterResourceAttribute.SetSource(source, modifier);
						if (asServer)
						{
							/* The settled maximum, not this entry's own arithmetic: the same resource
							 * may also carry dungeon scaling, and filling to `newValue` would spawn
							 * the NPC on a fraction of the maximum its health bar reports. */
							characterResourceAttribute.SetCurrentValue(characterResourceAttribute.FinalValue);
						}
					}
					else
					{
						int modifier = value - old;

						characterResourceAttribute.SetSource(source, modifier);
						if (asServer)
						{
							characterResourceAttribute.SetCurrentValue(characterResourceAttribute.FinalValue);
						}
					}
				}
			}
		}
	}
}