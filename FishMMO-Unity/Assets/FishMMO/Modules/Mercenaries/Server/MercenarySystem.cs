using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Transporting;
using FishNet.Utility.Performance;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Logging;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Server.Implementation.World.SceneServer.AI;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// The scene server's mercenaries (issue #135): hiring at a Mercenary, the party seat a mercenary takes, sending it to
	/// rest and bringing it back, releasing it, its stance, the gear its owner keeps for it, and its death.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>One per player, one per party, a seat each.</b> A hire inside a party is the leader's to make; without a party
	/// it forms one. The seat is taken in the database (<see cref="IPartySystem{TConnection}.TrySeatMercenaryAsync"/>), in
	/// the same locked count as a join, so a mercenary and an invited player can never both take the last seat.
	/// </para>
	/// <para>
	/// <b>Losing the seat rests it.</b> The seat lives on the owner's membership row, so an owner who leaves, is removed
	/// or joins another party took it with them; the once-a-second watch then sends the mercenary to rest, and the owner
	/// can bring it back from the Mercenary window (as leader, into a party with room).
	/// </para>
	/// <para>
	/// <b>Death releases it.</b> The record is deleted and the seat freed; the gear stays the owner's (it is in their own
	/// container) and goes on the next mercenary hired.
	/// </para>
	/// <para>
	/// Main thread for everything that touches the world; the async worker for the party and the database, keyed by the
	/// owner's character ID, so every write about one player's mercenary lands in order with that player's saves.
	/// </para>
	/// </remarks>
	[CreateAssetMenu(fileName = "MercenarySystem", menuName = "FishMMO/Server/SceneServer/Mercenary System", order = 1)]
	[RequiresDataContainer(typeof(MercenarySystemMainThreadQueueData))]
	[RequiresDataContainer(typeof(MercenarySystemRuntimeData))]
	[RequiresDataContainer(typeof(AsyncWorkerData))]
	public partial class MercenarySystem : ServerBehaviour
	{
		private const string LOG = "MercenarySystem";

		[Header("Hiring")]
		[Tooltip("The currency a hire is paid in (the Currency character attribute).")]
		[SerializeField] private CharacterAttributeTemplate currencyTemplate;

		[Header("Seat")]
		[Tooltip("Seconds between checks that every mercenary out still has its seat (its owner is still in that party).")]
		[SerializeField] private float seatCheckIntervalSeconds = 1f;

		[Tooltip("Seconds after a mercenary is seated before a missing seat sends it to rest (the party's own update lands first).")]
		[SerializeField] private float seatGraceSeconds = 5f;

		[Header("Main Thread Dispatch")]
		[Tooltip("Replies drained from the main-thread queue per frame.")]
		[SerializeField] private int maxMainThreadActionsPerFrame = 100;

		[Header("Ingress Protection")]
		[Tooltip("Minimum milliseconds between mercenary requests of one kind per connection.")]
		[SerializeField] private int ingressDebounceMilliseconds = 250;

		[Tooltip("Seconds between bounded ingress guard cleanup sweeps.")]
		[SerializeField] private float ingressSweepIntervalSeconds = 5f;

		[Tooltip("Seconds before stale ingress guard entries are removed.")]
		[SerializeField] private float ingressEntryTtlSeconds = 30f;

		[Tooltip("Maximum stale ingress guard entries removed per sweep.")]
		[SerializeField] private int ingressSweepMaxRemovals = 128;

		private enum IngressOperation : byte
		{
			Hire = 1,
			Dismiss = 2,
			Return = 3,
			Release = 4,
			Stance = 5,
			Gear = 6,
		}

		/// <summary>A mercenary that is out, by its owner's character ID.</summary>
		private sealed class LiveEntry
		{
			public Mercenary Mercenary;
			public double SeatedAt;
		}

		private readonly Dictionary<long, LiveEntry> live = new Dictionary<long, LiveEntry>();

		/// <summary>Mercenaries whose hire is being answered: each is hired by one player, so the others wait.</summary>
		private readonly HashSet<NPC> recruitersBeingHired = new HashSet<NPC>();
		private readonly List<long> scratchOwners = new List<long>();
		private double nextSeatCheck;
		private bool subscribedToDamage;

		private static double Now => MonotonicClock.NowSeconds;

		/// <summary>How many mercenaries are out on this server.</summary>
		public int LiveCount => live.Count;

		/// <inheritdoc />
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			if (Server == null)
			{
				Log.Error(LOG, "InitializeOnce: Server is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}
			if (!Server.DataContainerRegistry.TryGet<IMercenarySystemMainThreadQueueData>(out _) ||
				!Server.DataContainerRegistry.TryGet<IMercenarySystemRuntimeData>(out _))
			{
				Log.Error(LOG, "InitializeOnce: its data containers are missing");
				return ServerComponentInitializationStatus.FailedToGetDataContainer;
			}
			if (currencyTemplate == null)
			{
				Log.Warning(LOG, "No currency template is set: hires are refused.");
			}

			seatCheckIntervalSeconds = Mathf.Clamp(seatCheckIntervalSeconds, 0.1f, 10f);
			seatGraceSeconds = Mathf.Clamp(seatGraceSeconds, 0f, 60f);
			maxMainThreadActionsPerFrame = Mathf.Max(1, maxMainThreadActionsPerFrame);
			ingressDebounceMilliseconds = Mathf.Max(0, ingressDebounceMilliseconds);
			ingressSweepIntervalSeconds = Mathf.Max(0.25f, ingressSweepIntervalSeconds);
			ingressEntryTtlSeconds = Mathf.Max(1f, ingressEntryTtlSeconds);
			ingressSweepMaxRemovals = Mathf.Max(1, ingressSweepMaxRemovals);

			Server.NetworkWrapper.RegisterBroadcast<MercenaryHireBroadcast>(OnHire, true);
			Server.NetworkWrapper.RegisterBroadcast<MercenaryDismissBroadcast>(OnDismiss, true);
			Server.NetworkWrapper.RegisterBroadcast<MercenaryReturnBroadcast>(OnReturn, true);
			Server.NetworkWrapper.RegisterBroadcast<MercenaryReleaseBroadcast>(OnRelease, true);
			Server.NetworkWrapper.RegisterBroadcast<MercenaryStanceBroadcast>(OnStance, true);
			Server.NetworkWrapper.RegisterBroadcast<MercenaryEquipBroadcast>(OnEquip, true);
			Server.NetworkWrapper.RegisterBroadcast<MercenaryUnequipBroadcast>(OnUnequip, true);

			if (Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, Scene> characterSystem))
			{
				characterSystem.OnSpawnCharacter -= OnCharacterSpawned;
				characterSystem.OnSpawnCharacter += OnCharacterSpawned;
				characterSystem.OnDespawnCharacter -= OnCharacterDespawned;
				characterSystem.OnDespawnCharacter += OnCharacterDespawned;
			}
			else
			{
				Log.Warning(LOG, "No character system found: mercenaries are not restored or put away with their owners.");
			}

			ICharacterDamageController.OnKilled -= OnCharacterKilled;
			ICharacterDamageController.OnKilled += OnCharacterKilled;

			Log.Debug(LOG, "Initialized");
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <inheritdoc />
		public override void OnDeinitialize()
		{
			if (Server == null)
			{
				return;
			}
			DrainMainThreadQueue<IMercenarySystemMainThreadQueueData>(maxMainThreadActionsPerFrame, true);

			Server.NetworkWrapper.UnregisterBroadcast<MercenaryHireBroadcast>(OnHire);
			Server.NetworkWrapper.UnregisterBroadcast<MercenaryDismissBroadcast>(OnDismiss);
			Server.NetworkWrapper.UnregisterBroadcast<MercenaryReturnBroadcast>(OnReturn);
			Server.NetworkWrapper.UnregisterBroadcast<MercenaryReleaseBroadcast>(OnRelease);
			Server.NetworkWrapper.UnregisterBroadcast<MercenaryStanceBroadcast>(OnStance);
			Server.NetworkWrapper.UnregisterBroadcast<MercenaryEquipBroadcast>(OnEquip);
			Server.NetworkWrapper.UnregisterBroadcast<MercenaryUnequipBroadcast>(OnUnequip);

			if (Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, Scene> characterSystem))
			{
				characterSystem.OnSpawnCharacter -= OnCharacterSpawned;
				characterSystem.OnDespawnCharacter -= OnCharacterDespawned;
			}
			ICharacterDamageController.OnKilled -= OnCharacterKilled;
			SetDamageSubscription(false);
			live.Clear();
			recruitersBeingHired.Clear();

			if (Server.DataContainerRegistry.TryGet<IMercenarySystemRuntimeData>(out var runtimeData))
			{
				runtimeData.IngressGuard?.Clear();
			}
		}

		/// <inheritdoc />
		protected override void OnUpdate(float deltaTime)
		{
			DrainMainThreadQueue<IMercenarySystemMainThreadQueueData>(maxMainThreadActionsPerFrame, false);
			if (Server.DataContainerRegistry.TryGet<IMercenarySystemRuntimeData>(out var runtimeData))
			{
				runtimeData.IngressGuard.Sweep(ingressSweepIntervalSeconds, ingressEntryTtlSeconds, ingressSweepMaxRemovals);
			}

			double now = Now;
			if (now >= nextSeatCheck)
			{
				nextSeatCheck = now + seatCheckIntervalSeconds;
				CheckSeats(now);
			}
		}

		// ── Lifecycle ─────────────────────────────────────────────────────────────────────

		/// <summary>
		/// A player arrived (login or scene change): tell them their mercenary and its gear, and bring a mercenary that was
		/// out back to their side. Its seat is confirmed in the database as it comes out; a seat that was lost rests it.
		/// </summary>
		private void OnCharacterSpawned(NetworkConnection conn, IPlayerCharacter character, Scene scene)
		{
			if (character == null || character.ID <= 0 || !TryGetController(character, out MercenaryController controller))
			{
				return;
			}

			SendGear(character);
			SendState(character, controller);

			if (!controller.HasMercenary || controller.Resting)
			{
				return;
			}

			MercenaryKitTemplate kit = controller.Kit;
			long partyID = PartyOf(character, out _);
			if (kit == null || kit.Prefab == null || partyID <= 0)
			{
				// Its party is gone (or its kit is): it waits, resting, to be brought back.
				Rest(character, controller, null, notifyLeftParty: partyID <= 0 && kit != null);
				return;
			}

			if (SpawnMercenary(character, controller, kit, null, Quaternion.identity) == null)
			{
				return;
			}
			controller.SeatedPartyID = partyID;
			ConfirmSeatAsync(character, controller, partyID);
		}

		/// <summary>
		/// A player left the world: the mercenary leaves with them. Nothing is written here: the character's departure save,
		/// which ran first, captured the live mercenary, and its seat is kept while the owner is away.
		/// </summary>
		private void OnCharacterDespawned(NetworkConnection conn, IPlayerCharacter character)
		{
			if (character == null)
			{
				return;
			}
			if (live.TryGetValue(character.ID, out LiveEntry entry))
			{
				DespawnMercenary(character.ID, entry.Mercenary);
			}
		}

		/// <summary>A mercenary died: it is released, the seat freed, the gear kept. A death is a release nobody asked for.</summary>
		private void OnCharacterKilled(ICharacter killer, ICharacter victim)
		{
			if (!(victim is Mercenary mercenary))
			{
				return;
			}
			long ownerID = 0;
			foreach (KeyValuePair<long, LiveEntry> pair in live)
			{
				if (ReferenceEquals(pair.Value.Mercenary, mercenary))
				{
					ownerID = pair.Key;
					break;
				}
			}
			if (ownerID == 0)
			{
				return;
			}

			string name = mercenary.GameObject != null ? mercenary.GameObject.name : null;
			// Off the lists now; the combat system returns the body to the pool (Mercenary.Despawn, no corpse).
			UnlistMercenary(ownerID, mercenary);
			mercenary.TakeOffAll();

			if (TryGetResidentCharacter(ownerID, out IPlayerCharacter owner) && TryGetController(owner, out MercenaryController controller))
			{
				name = string.IsNullOrEmpty(controller.MercenaryName) ? name : controller.MercenaryName;
				long seatedParty = controller.SeatedPartyID;
				controller.Clear();
				controller.MarkDirty();
				SendState(owner, controller);
				SendResult(owner, MercenaryResult.Died, name);
				PersistRelease(ownerID, seatedParty);
			}
			else
			{
				PersistRelease(ownerID, 0);
			}

			// Should the death path not have pooled it (it always does), do it next frame rather than leave a body.
			TryEnqueueMainThread<IMercenarySystemMainThreadQueueData>(() =>
			{
				if (mercenary != null && mercenary.NetworkObject != null && mercenary.NetworkObject.IsSpawned)
				{
					mercenary.Despawn();
				}
			});
		}

		// ── The seat ──────────────────────────────────────────────────────────────────────

		/// <summary>
		/// Every mercenary out keeps its seat only while its owner is still in the party it was seated in. Past the grace
		/// after seating (the party's own update reaches the owner's controller a moment after the database), a mismatch
		/// rests it.
		/// </summary>
		private void CheckSeats(double now)
		{
			if (live.Count == 0)
			{
				return;
			}
			scratchOwners.Clear();
			foreach (KeyValuePair<long, LiveEntry> pair in live)
			{
				scratchOwners.Add(pair.Key);
			}
			for (int i = 0; i < scratchOwners.Count; ++i)
			{
				long ownerID = scratchOwners[i];
				if (!live.TryGetValue(ownerID, out LiveEntry entry))
				{
					continue;
				}
				if (!TryGetResidentCharacter(ownerID, out IPlayerCharacter owner) || !TryGetController(owner, out MercenaryController controller))
				{
					// Its owner is not here any more and nothing despawned it: put it away.
					DespawnMercenary(ownerID, entry.Mercenary);
					continue;
				}
				if (now - entry.SeatedAt < seatGraceSeconds)
				{
					continue;
				}
				if (!MercenaryRules.KeepsSeat(controller.SeatedPartyID, PartyOf(owner, out _)))
				{
					// The seat went with the owner's membership; nothing to free in the database.
					controller.SeatedPartyID = 0;
					Rest(owner, controller, entry.Mercenary, notifyLeftParty: true);
				}
			}
		}

		/// <summary>
		/// Seats an arriving mercenary again, idempotently: one that still holds its seat keeps it, one whose seat was lost
		/// while its owner was away (the party filled, another mercenary was seated) is sent to rest.
		/// </summary>
		private void ConfirmSeatAsync(IPlayerCharacter character, MercenaryController controller, long partyID)
		{
			long characterID = character.ID;
			int kitID = controller.KitID;
			string name = controller.MercenaryName;
			if (!Server.BehaviourRegistry.TryGet(out IPartySystem<NetworkConnection> partySystem))
			{
				return;
			}
			TryEnqueueAsyncWork(async () =>
			{
				PartyJoinOutcome outcome = await partySystem.TrySeatMercenaryAsync(characterID, partyID, kitID, name);
				if (outcome == PartyJoinOutcome.Joined || outcome == PartyJoinOutcome.Failed)
				{
					// Seated, or the database could not say: a fault is not evidence the seat is gone, so it stays out.
					return;
				}
				TryEnqueueMainThread<IMercenarySystemMainThreadQueueData>(() =>
				{
					if (!TryGetResidentCharacter(characterID, out IPlayerCharacter owner) || !TryGetController(owner, out MercenaryController current) ||
						current.KitID != kitID || current.Resting)
					{
						return;
					}
					current.SeatedPartyID = 0;
					Rest(owner, current, live.TryGetValue(characterID, out LiveEntry entry) ? entry.Mercenary : null, notifyLeftParty: false);
					SendResult(owner, outcome == PartyJoinOutcome.Full ? MercenaryResult.PartyFull : MercenaryResult.PartyHasMercenary, current.MercenaryName);
				});
			}, characterID);
		}

		// ── Spawning ──────────────────────────────────────────────────────────────────────

		/// <summary>
		/// Puts the player's mercenary at their side, wearing their mercenary gear, with its saved health and buffs, its
		/// name and face, and its owner's factions, then spawns it with no owning connection (its brain is the server's).
		/// Main thread.
		/// </summary>
		/// <param name="owner">The player it serves.</param>
		/// <param name="controller">Their mercenary state.</param>
		/// <param name="kit">Its kit.</param>
		/// <param name="at">Where it appears (a hire: where the Mercenary hired stood); null for beside the owner.</param>
		/// <param name="facing">Its facing when <paramref name="at"/> is given.</param>
		/// <returns>The mercenary, or null when the kit's prefab could not be made one.</returns>
		private Mercenary SpawnMercenary(IPlayerCharacter owner, MercenaryController controller, MercenaryKitTemplate kit, Vector3? at, Quaternion facing)
		{
			if (live.TryGetValue(owner.ID, out LiveEntry existing))
			{
				DespawnMercenary(owner.ID, existing.Mercenary);
			}

			Vector3 position = at ?? SpawnPointBeside(owner);
			Quaternion rotation = at.HasValue ? facing : owner.Transform.rotation;
			NetworkObject prefab = kit.Prefab;
			NetworkObject nob = Server.NetworkWrapper.NetworkManager.GetPooledInstantiated(prefab.PrefabId, prefab.SpawnableCollectionId,
				ObjectPoolRetrieveOption.Unset, null, position, rotation, null, true);
			Mercenary mercenary = nob != null ? nob.GetComponent<Mercenary>() : null;
			if (mercenary == null)
			{
				if (nob != null)
				{
					Server.NetworkWrapper.NetworkManager.StorePooledInstantiated(nob, true);
					PersistentPool.Keep(Server.NetworkWrapper.NetworkManager, nob);
				}
				Log.Error(LOG, $"The prefab of kit '{kit.name}' is not a Mercenary; nothing was spawned for character {owner.ID}.");
				return null;
			}

			mercenary.MercenaryOwner = owner;
			mercenary.Kit = kit;
			mercenary.Stance = controller.Stance;
			mercenary.SpawnSeedOverride = controller.AppearanceSeed;
			// The race of the one hired (a town's spawner may have made them another race than the kit's): their models and
			// their name are drawn from it, so without it the same seeds make a stranger. Set every time, the kit's own for
			// none, because a pooled body keeps the race its last wearer had.
			RaceTemplate race = controller.RaceTemplateID != 0 ? RaceTemplate.Get<RaceTemplate>(controller.RaceTemplateID) : null;
			if (race == null)
			{
				NPC kitNpc = prefab.GetComponent<NPC>();
				race = kitNpc != null ? kitNpc.RaceTemplate : null;
			}
			mercenary.SetRaceTemplateOnSpawn(race);
			SceneObjectNamer namer = mercenary.GetComponent<SceneObjectNamer>();
			if (namer != null)
			{
				namer.AdoptName(controller.NameState);
			}
			mercenary.PendingGear = GearItems(owner);
			mercenary.PersistedResources = controller.SavedResources;
			mercenary.PersistedBuffs = controller.SavedBuffs;

			SceneManager.MoveGameObjectToScene(mercenary.GameObject, owner.GameObject.scene);
			mercenary.GameObject.SetActive(true);

			// The brain once the object is active and in its scene, as for a pet: its first state touches the agent.
			if (AIBrainHost.TryGet(ServerManager.NetworkManager, out AIBrainHost brainHost))
			{
				AIController aiController = brainHost.Prepare(mercenary, position);
				if (aiController != null)
				{
					aiController.Target = null;
				}
			}
			else
			{
				Log.Warning(LOG, $"No AI brain host is running; {mercenary.GameObject.name} spawns without a brain.");
			}

			// Factions before the spawn, so the payload carries the owner's (see PetSystem.SpawnAndInitializePet).
			if (mercenary.TryGet(out IFactionController mercenaryFactions) && owner.TryGet(out IFactionController ownerFactions))
			{
				mercenaryFactions.CopyFrom(ownerFactions);
			}

			ServerManager.Spawn(mercenary.GameObject, null, owner.GameObject.scene);

			// Now live: what was saved has been put on it, and the next save reads it off it.
			controller.SavedResources = null;
			controller.SavedBuffs = null;
			controller.MarkDirty();

			live[owner.ID] = new LiveEntry() { Mercenary = mercenary, SeatedAt = Now };
			Mercenary.RegisterServer(owner.ID, mercenary);
			SetDamageSubscription(live.Count > 0);
			return mercenary;
		}

		/// <summary>A point a step behind and beside the owner, on the ground when there is ground under it.</summary>
		private static Vector3 SpawnPointBeside(IPlayerCharacter owner)
		{
			Transform t = owner.Transform;
			Vector3 point = t.position - t.forward * 1.5f + t.right * 1.0f;
			PhysicsScene physics = owner.GameObject.scene.GetPhysicsScene();
			if (physics.Raycast(point + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 8f, Constants.Layers.Ground, QueryTriggerInteraction.Ignore))
			{
				return hit.point;
			}
			return t.position;
		}

		/// <summary>The items in the player's mercenary gear.</summary>
		private static List<Item> GearItems(IPlayerCharacter owner)
		{
			var items = new List<Item>(MercenaryRules.GearSlotCount);
			if (owner.TryGet(out IMercenaryGear gear))
			{
				for (int i = 0; i < gear.Items.Count; ++i)
				{
					if (gear.Items[i] != null)
					{
						items.Add(gear.Items[i]);
					}
				}
			}
			return items;
		}

		/// <summary>Returns a live mercenary to the pool and takes it off the lists. Main thread.</summary>
		private void DespawnMercenary(long ownerID, Mercenary mercenary)
		{
			UnlistMercenary(ownerID, mercenary);
			if (mercenary != null)
			{
				mercenary.TakeOffAll();
				if (mercenary.NetworkObject != null && mercenary.NetworkObject.IsSpawned)
				{
					mercenary.Despawn();
				}
			}
		}

		private void UnlistMercenary(long ownerID, Mercenary mercenary)
		{
			if (live.TryGetValue(ownerID, out LiveEntry entry) && ReferenceEquals(entry.Mercenary, mercenary))
			{
				live.Remove(ownerID);
			}
			Mercenary.UnregisterServer(ownerID, mercenary);
			SetDamageSubscription(live.Count > 0);
		}

		/// <summary>
		/// Sends the mercenary to rest: its state is kept (to be restored when it returns), it leaves the world, and its
		/// owner is told. The seat is the caller's business: freed in the database on a dismissal, already gone when the
		/// owner left the party. Main thread.
		/// </summary>
		private void Rest(IPlayerCharacter owner, MercenaryController controller, Mercenary mercenary, bool notifyLeftParty)
		{
			if (mercenary != null)
			{
				MercenaryRecords.CaptureLive(mercenary, Server.NetworkWrapper.NetworkManager.TimeManager,
					out List<MercenaryPersistedResource> resources, out List<MercenaryPersistedBuff> buffs);
				controller.SavedResources = resources;
				controller.SavedBuffs = buffs;
				DespawnMercenary(owner.ID, mercenary);
			}
			controller.SetState(controller.KitID, controller.MercenaryName, true, controller.Stance);
			controller.MarkDirty();
			PersistRecord(owner, controller);
			SendState(owner, controller);
			if (notifyLeftParty)
			{
				SendResult(owner, MercenaryResult.RestedLeftParty, controller.MercenaryName);
			}
		}

		// ── Defending the owner ───────────────────────────────────────────────────────────

		/// <summary>Listens to the global damage event only while a mercenary is out (it is raised for every hit).</summary>
		private void SetDamageSubscription(bool wanted)
		{
			if (wanted == subscribedToDamage)
			{
				return;
			}
			if (wanted)
			{
				ICharacterDamageController.OnDamaged += OnCharacterDamaged;
			}
			else
			{
				ICharacterDamageController.OnDamaged -= OnCharacterDamaged;
			}
			subscribedToDamage = wanted;
		}

		/// <summary>
		/// The owner was struck: a defensive or aggressive mercenary that is not already fighting turns on the attacker,
		/// as a pet does. Passive ones stay out of it.
		/// </summary>
		private void OnCharacterDamaged(ICharacter attacker, ICharacter defender, int amount, DamageAttributeTemplate damageAttribute)
		{
			if (attacker == null || defender == null || ReferenceEquals(attacker, defender) ||
				!live.TryGetValue(defender.ID, out LiveEntry entry) || !(defender is IPlayerCharacter))
			{
				return;
			}
			Mercenary mercenary = entry.Mercenary;
			if (mercenary == null || ReferenceEquals(attacker, mercenary) || mercenary.Stance == PetStance.Passive ||
				!mercenary.TryGet(out ICharacterDamageController damage) || !damage.IsAlive ||
				!mercenary.TryGet(out IAIController ai))
			{
				return;
			}
			if (ai.CurrentState != null && ai.CurrentState == ai.AttackingState)
			{
				return;
			}
			if (IsValidTarget(mercenary, defender, attacker))
			{
				CommandAttack(mercenary, attacker);
			}
		}

		/// <summary>The pet target rules: alive, same scene, factions on both, and the harm rule (answering as the owner).</summary>
		private static bool IsValidTarget(Mercenary mercenary, ICharacter owner, ICharacter target)
		{
			if (target == null || ReferenceEquals(target, owner) || ReferenceEquals(target, mercenary))
			{
				return false;
			}
			if (!target.TryGet(out ICharacterDamageController damage) || !damage.IsAlive)
			{
				return false;
			}
			if (mercenary.GameObject.scene != target.GameObject.scene)
			{
				return false;
			}
			if (!mercenary.TryGet(out IFactionController _) || !target.TryGet(out IFactionController _))
			{
				return false;
			}
			return HarmRule.MayHarm(mercenary, target, out _);
		}

		/// <summary>Points the mercenary at a target and into its attacking state (set again after the change, as for a pet).</summary>
		private static void CommandAttack(Mercenary mercenary, ICharacter target)
		{
			if (!mercenary.TryGet(out IAIController ai))
			{
				return;
			}
			ai.Target = target.Transform;
			if (ai.AttackingState != null)
			{
				ai.ChangeState(ai.AttackingState);
				ai.Target = target.Transform;
			}
		}

		/// <summary>Breaks the mercenary off and back to its owner's heel.</summary>
		private static void Recall(Mercenary mercenary)
		{
			if (mercenary == null || !mercenary.TryGet(out IAIController ai))
			{
				return;
			}
			ai.Target = null;
			if (mercenary.TryGet(out IAbilityKnowledgeController abilities))
			{
				abilities.Interrupt(null);
			}
			if (ai.IdleState != null)
			{
				ai.ChangeState(ai.IdleState);
			}
		}

		// ── Lookups and messages ──────────────────────────────────────────────────────────

		private static bool TryGetController(IPlayerCharacter character, out MercenaryController controller)
		{
			controller = null;
			return character != null && character.TryGet(out IMercenaryController found) && (controller = found as MercenaryController) != null;
		}

		/// <summary>The player's party and rank on this server (0 and None without one).</summary>
		private static long PartyOf(IPlayerCharacter character, out PartyRank rank)
		{
			rank = PartyRank.None;
			if (character != null && character.TryGet(out IPartyController party) && party.ID > 0)
			{
				rank = party.Rank;
				return party.ID;
			}
			return 0;
		}

		private bool TryGetResidentCharacter(long characterID, out IPlayerCharacter character)
		{
			character = null;
			return Server != null &&
				Server.DataContainerRegistry.TryGet(out ICharacterMappingData<NetworkConnection> mappingData) &&
				mappingData.CharactersByID.TryGetValue(characterID, out character) &&
				character != null;
		}

		private void Send<T>(IPlayerCharacter character, T message) where T : struct, FishNet.Broadcast.IBroadcast
		{
			if (character?.Owner != null && character.Owner.IsActive)
			{
				Server.NetworkWrapper.Broadcast(character.Owner, message, true, Channel.Reliable);
			}
		}

		private void SendResult(IPlayerCharacter character, MercenaryResult result, string name)
		{
			Send(character, new MercenaryResultBroadcast() { Result = result, Name = name });
		}

		private void SendState(IPlayerCharacter character, MercenaryController controller)
		{
			Send(character, new MercenaryStateBroadcast()
			{
				KitID = controller.KitID,
				Name = controller.MercenaryName,
				Resting = controller.Resting,
				Stance = controller.Stance,
			});
		}

		/// <summary>The whole mercenary gear, to its owner.</summary>
		private void SendGear(IPlayerCharacter character)
		{
			if (!character.TryGet(out IMercenaryGear gear))
			{
				return;
			}
			var entries = new List<MercenaryGearEntry>(gear.Items.Count);
			for (int i = 0; i < gear.Items.Count; ++i)
			{
				Item item = gear.Items[i];
				if (item == null || item.Template == null)
				{
					continue;
				}
				entries.Add(new MercenaryGearEntry()
				{
					Slot = (byte)i,
					ItemID = item.ID,
					TemplateID = item.Template.ID,
					Seed = item.IsGenerated ? item.Generator.Seed : 0,
					Amount = item.IsStackable ? item.Stackable.Amount : 1u,
				});
			}
			Send(character, new MercenaryGearBroadcast() { Entries = entries.ToArray() });
		}

		private bool TryBeginIngressGuard(NetworkConnection conn, IngressOperation operation, out long guardKey)
		{
			guardKey = 0;
			return Server.DataContainerRegistry.TryGet<IMercenarySystemRuntimeData>(out var runtimeData) &&
				runtimeData.IngressGuard.TryBegin(conn.ClientId, (byte)operation, ingressDebounceMilliseconds, out guardKey);
		}

		private void EndIngressGuard(long guardKey)
		{
			if (Server?.DataContainerRegistry.TryGet<IMercenarySystemRuntimeData>(out var runtimeData) == true)
			{
				runtimeData.IngressGuard?.End(guardKey);
			}
		}
	}
}
