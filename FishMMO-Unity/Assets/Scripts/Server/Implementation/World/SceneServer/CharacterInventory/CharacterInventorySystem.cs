using FishNet.Connection;
using FishNet.Transporting;
using FishMMO.Shared;
using FishMMO.Logging;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared.Core;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using UnityEngine;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Manages player character inventory, equipment, and bank operations with validation and database persistence.
	/// </summary>
	[CreateAssetMenu(fileName = "CharacterInventorySystem", menuName = "FishMMO/Server/SceneServer/Character Inventory System", order = 1)]
	[RequiresDataContainer(typeof(CharacterInventorySystemRuntimeData))]
	[RequiresDataContainer(typeof(AsyncWorkerData))]
	public class CharacterInventorySystem : ServerBehaviour, ICharacterInventorySystem
	{
		/// <summary>
		/// Debounce window in milliseconds applied to inventory ingress operations.
		/// </summary>
		[Header("Ingress Protection")]
		[Tooltip("Minimum milliseconds between identical inventory requests from the same connection")]
		[SerializeField] private int ingressDebounceMilliseconds = 60;

		/// <summary>
		/// Interval in seconds between bounded ingress-guard cleanup sweeps.
		/// </summary>
		[Tooltip("Seconds between bounded ingress guard cleanup sweeps")]
		[SerializeField] private float ingressSweepIntervalSeconds = 5.0f;

		/// <summary>
		/// Guard entry time-to-live in seconds.
		/// </summary>
		[Tooltip("Seconds before stale ingress guard entries are removed")]
		[SerializeField] private float ingressEntryTtlSeconds = 30.0f;

		/// <summary>
		/// Maximum number of stale guard entries removed per sweep pass.
		/// </summary>
		[Tooltip("Maximum stale ingress guard entries removed per sweep")]
		[SerializeField] private int ingressSweepMaxRemovals = 128;

		/// <summary>
		/// Maximum entries in the ingress tracker dictionaries before new requests are rejected.
		/// </summary>

		/// <summary>
		/// Global per-connection rate limit in milliseconds across all inventory operations.
		/// </summary>
		private const int GlobalPerConnectionRateMilliseconds = 15;

		/// <summary>
		/// Seconds between full item snapshots of every resident character.
		/// </summary>
		[Header("Item Snapshot")]
		[Tooltip("Seconds between full inventory/bank/equipment snapshots of every resident character")]
		[SerializeField] private float itemSnapshotIntervalSeconds = 60.0f;

		/// <summary>
		/// Seconds remaining until the next snapshot sweep.
		/// </summary>
		private float itemSnapshotTimer;

		/// <summary>
		/// Operation codes used by ingress guards.
		/// </summary>
		private enum IngressOperation : byte
		{
			/// <summary>
			/// Remove an item from the inventory.
			/// </summary>
			InventoryRemove = 1,
			/// <summary>
			/// Swap two item slots within the inventory.
			/// </summary>
			InventorySwap = 2,
			/// <summary>
			/// Equip an item from a container.
			/// </summary>
			EquipmentEquip = 3,
			/// <summary>
			/// Unequip an item to a container.
			/// </summary>
			EquipmentUnequip = 4,
			/// <summary>
			/// Remove an item from the bank.
			/// </summary>
			BankRemove = 5,
			/// <summary>
			/// Swap two item slots within the bank.
			/// </summary>
			BankSwap = 6,
		}

		/// <summary>
		/// Initializes the character inventory system, registering broadcast handlers for inventory, equipment, and bank actions.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			if (Server == null)
			{
				Log.Error("CharacterInventorySystem", "InitializeOnce: Server is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (Server.Database == null ||
				Server.Database.ServiceRegistry == null)
			{
				Log.Error("CharacterInventorySystem", "InitializeOnce: Database or ServiceRegistry is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			var registry = Server.Database.ServiceRegistry;
			if (!registry.TryGet<ICharacterInventoryService>(out _) ||
				!registry.TryGet<ICharacterBankService>(out _) ||
				!registry.TryGet<ICharacterEquipmentService>(out _) ||
				!registry.TryGet<ICharacterAttributeService>(out _))
			{
				Log.Error("CharacterInventorySystem", "InitializeOnce: One or more required database services could not be resolved");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (!Server.DataContainerRegistry.TryGet<ICharacterInventorySystemRuntimeData>(out var runtimeData))
			{
				Log.Error("CharacterInventorySystem", "InitializeOnce: ICharacterInventorySystemRuntimeData not found");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			// Inventory broadcasts
			Server.NetworkWrapper.RegisterBroadcast<InventoryRemoveItemBroadcast>(OnServerInventoryRemoveItemBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<InventorySwapItemSlotsBroadcast>(OnServerInventorySwapItemSlotsBroadcastReceived, true);

			// Equipment broadcasts
			Server.NetworkWrapper.RegisterBroadcast<EquipmentEquipItemBroadcast>(OnServerEquipmentEquipItemBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<EquipmentUnequipItemBroadcast>(OnServerEquipmentUnequipItemBroadcastReceived, true);

			// Bank broadcasts
			Server.NetworkWrapper.RegisterBroadcast<BankRemoveItemBroadcast>(OnServerBankRemoveItemBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<BankSwapItemSlotsBroadcast>(OnServerBankSwapItemSlotsBroadcastReceived, true);

			ingressDebounceMilliseconds = Mathf.Max(0, ingressDebounceMilliseconds);
			ingressSweepIntervalSeconds = Mathf.Max(0.25f, ingressSweepIntervalSeconds);
			ingressEntryTtlSeconds = Mathf.Max(1.0f, ingressEntryTtlSeconds);
			ingressSweepMaxRemovals = Mathf.Max(1, ingressSweepMaxRemovals);

			// A snapshot is cheap relative to a save tick but it is not free, so refuse a pathological
			// interval rather than letting a mis-set inspector field turn it into a per-frame sweep.
			itemSnapshotIntervalSeconds = Mathf.Max(5.0f, itemSnapshotIntervalSeconds);
			itemSnapshotTimer = itemSnapshotIntervalSeconds;

			// The logout snapshot has to run while the character is still resident, which is exactly
			// what OnDespawnCharacter gives us: CharacterSystem raises it after it has captured its
			// own save data but before the NetworkObject is despawned.
			if (Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, UnityEngine.SceneManagement.Scene> characterSystem))
			{
				characterSystem.OnDespawnCharacter += CharacterSystem_OnDespawnCharacter;
			}

			Log.Debug("CharacterInventorySystem", "Initialized");
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Cleans up the character inventory system, unregistering broadcast handlers for inventory, equipment, and bank actions.
		/// </summary>
		public override void OnDeinitialize()
		{
			if (Server == null)
			{
				Log.Error("CharacterInventorySystem", "OnDeinitialize: Server is null");
				return;
			}

			// Inventory broadcasts
			Server.NetworkWrapper.UnregisterBroadcast<InventoryRemoveItemBroadcast>(OnServerInventoryRemoveItemBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<InventorySwapItemSlotsBroadcast>(OnServerInventorySwapItemSlotsBroadcastReceived);

			// Equipment broadcasts
			Server.NetworkWrapper.UnregisterBroadcast<EquipmentEquipItemBroadcast>(OnServerEquipmentEquipItemBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<EquipmentUnequipItemBroadcast>(OnServerEquipmentUnequipItemBroadcastReceived);

			// Bank broadcasts
			Server.NetworkWrapper.UnregisterBroadcast<BankRemoveItemBroadcast>(OnServerBankRemoveItemBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<BankSwapItemSlotsBroadcast>(OnServerBankSwapItemSlotsBroadcastReceived);

			if (Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, UnityEngine.SceneManagement.Scene> characterSystem))
			{
				characterSystem.OnDespawnCharacter -= CharacterSystem_OnDespawnCharacter;
			}

			if (Server.DataContainerRegistry.TryGet<ICharacterInventorySystemRuntimeData>(out var runtimeData))
			{
				runtimeData.IngressGuard?.Clear();
			}

			// Sequence numbers, applied-watermarks and cached ownership triples all describe the
			// run that is ending. Carrying them into a re-initialize would let a stale watermark
			// silently suppress the first writes of the new one.
			itemWriteJournal.Clear();
		}

		/// <summary>
		/// Sweeps stale ingress guard entries and drives the periodic item snapshot.
		/// </summary>
		protected override void OnUpdate(float deltaTime)
		{
			if (Server.DataContainerRegistry.TryGet<ICharacterInventorySystemRuntimeData>(out var runtimeData))
			{
				runtimeData.IngressGuard.Sweep(ingressSweepIntervalSeconds, ingressEntryTtlSeconds, ingressSweepMaxRemovals);
			}

			itemSnapshotTimer -= deltaTime;
			if (itemSnapshotTimer <= 0.0f)
			{
				itemSnapshotTimer = itemSnapshotIntervalSeconds;
				SnapshotAllResidentCharacterItems();
			}

			// A rolled-back item transaction leaves memory ahead of the database. The repair has to
			// happen here rather than on the worker thread that discovered it, because capturing a
			// container is a main-thread operation.
			DrainReconcileRequests();
		}

		#region Atomic Item Persistence

		/// <summary>
		/// Per-character bookkeeping for item writes: capture ordering, the last ownership triple
		/// this server saw, and the repair queue.
		/// </summary>
		/// <remarks>
		/// Reached from the main thread (capture) and from async worker threads (apply), so every
		/// member takes the lock. The maps are small — one entry per resident character — and
		/// <see cref="ForgetCharacter"/> is called when a character's despawn batch is captured.
		/// </remarks>
		private sealed class ItemWriteJournal
		{
			private readonly object gate = new object();
			private readonly Dictionary<long, long> nextSequence = new Dictionary<long, long>();
			private readonly Dictionary<long, long> lastAppliedAny = new Dictionary<long, long>();
			private readonly Dictionary<long, long> lastAppliedSnapshot = new Dictionary<long, long>();
			private readonly Dictionary<long, CharacterSessionInfo> lastKnownLease = new Dictionary<long, CharacterSessionInfo>();
			private readonly HashSet<long> reconcileRequests = new HashSet<long>();

			/// <summary>
			/// Allocates the next capture sequence number for a character. Main thread only, which is
			/// what makes the numbers a faithful record of the order the mutations happened in.
			/// </summary>
			public long NextSequence(long characterID)
			{
				lock (gate)
				{
					nextSequence.TryGetValue(characterID, out long current);
					current++;
					nextSequence[characterID] = current;
					return current;
				}
			}

			/// <summary>
			/// Decides whether a batch captured at <paramref name="sequence"/> may still be applied.
			/// </summary>
			/// <remarks>
			/// <para>
			/// THE ORDERING PROBLEM. <c>EnqueuePersistence</c> is FIFO per entity key, but only while
			/// the bounded queue has room: when it is full the work runs on the thread pool instead,
			/// deliberately, so that persistence is never dropped. That fallback is what breaks the
			/// order, and it breaks it precisely when the server is busiest.
			/// </para>
			/// <para>
			/// The dangerous reordering is the one involving a snapshot, because a snapshot prunes and
			/// upserts UNGATED — it is authoritative by design. A snapshot captured at sequence 5 that
			/// lands after an incremental captured at 6 would delete the row that 6 wrote, resurrecting
			/// whatever 5 believed. So:
			/// </para>
			/// <list type="bullet">
			///   <item><description>
			///     A SNAPSHOT is skipped if any later-captured write has already been applied. Nothing
			///     is lost: the state it would have written is older than what is already there, and
			///     the next snapshot re-states the truth in full.
			///   </description></item>
			///   <item><description>
			///     An INCREMENTAL is skipped only if a later-captured SNAPSHOT has already been
			///     applied — a snapshot captured afterwards necessarily contains everything the
			///     incremental would have said. It is deliberately NOT skipped merely because a later
			///     incremental landed first: two incrementals can touch different slots, and dropping
			///     one because the other arrived early would lose a delete and resurrect an item.
			///   </description></item>
			/// </list>
			/// <para>
			/// This is a per-process guard against the local queue reordering itself. Cross-process
			/// ordering is not its job and it does not attempt it — that is what the session
			/// ownership assertion is for.
			/// </para>
			/// </remarks>
			/// <remarks>
			/// This is a PRE-FILTER, not the decision. It is read outside the transaction and can be
			/// stale by the time the batch reaches the database; <see cref="TryClaimSequence"/> makes
			/// the binding decision under the character row lock. Kept because rejecting a doomed
			/// batch here saves opening a transaction for it.
			/// </remarks>
			public bool ShouldApply(long characterID, long sequence, bool isSnapshot)
			{
				lock (gate)
				{
					if (isSnapshot)
					{
						return !lastAppliedAny.TryGetValue(characterID, out long appliedAny) || appliedAny < sequence;
					}
					return !lastAppliedSnapshot.TryGetValue(characterID, out long appliedSnapshot) || appliedSnapshot < sequence;
				}
			}

			/// <summary>
			/// Tests <see cref="ShouldApply"/> and, if it passes, immediately claims the sequence as
			/// applied — as one indivisible step.
			/// </summary>
			/// <remarks>
			/// <para>
			/// WHY THIS IS NOT "CHECK, THEN WRITE, THEN MARK". That arrangement had a hole wide
			/// enough to resurrect a deleted item. A snapshot would pass <see cref="ShouldApply"/>,
			/// then block on the character row lock while a NEWER incremental write took the lock,
			/// committed and marked itself; the snapshot would then be let through on the strength of
			/// a test that was already out of date, and its ungated prune-and-upsert would put back
			/// the row the incremental had just deleted. The test has to be made under the same
			/// mutual exclusion as the write it authorises, which means after
			/// <c>AssertOwnershipAsync</c> has taken the row lock, and it has to publish its result
			/// before the lock is released.
			/// </para>
			/// <para>
			/// CLAIMING BEFORE THE COMMIT IS DELIBERATE AND IS SAFE. A batch that claims and then
			/// fails to commit has raised the watermark for a write that never landed. The only
			/// batches that can be affected are ones captured EARLIER (a lower sequence), because a
			/// later batch always has a higher sequence and still passes; and the failing batch
			/// requests a reconcile snapshot, which is captured later still and therefore supersedes
			/// anything the skipped older batch would have said. So the cost of a false claim is
			/// bounded by one reconcile, whereas the cost of claiming after the commit is a
			/// resurrected item.
			/// </para>
			/// </remarks>
			public bool TryClaimSequence(long characterID, long sequence, bool isSnapshot)
			{
				lock (gate)
				{
					if (isSnapshot)
					{
						if (lastAppliedAny.TryGetValue(characterID, out long appliedAny) && appliedAny >= sequence)
						{
							return false;
						}
					}
					else if (lastAppliedSnapshot.TryGetValue(characterID, out long appliedSnapshot) && appliedSnapshot >= sequence)
					{
						return false;
					}

					if (!lastAppliedAny.TryGetValue(characterID, out long currentAny) || currentAny < sequence)
					{
						lastAppliedAny[characterID] = sequence;
					}
					if (isSnapshot &&
						(!lastAppliedSnapshot.TryGetValue(characterID, out long currentSnapshot) || currentSnapshot < sequence))
					{
						lastAppliedSnapshot[characterID] = sequence;
					}
					return true;
				}
			}

			/// <summary>
			/// Remembers the ownership triple currently held for a character.
			/// </summary>
			/// <remarks>
			/// <c>CharacterSystem.SaveAndDespawnCharacter</c> removes the token from
			/// <c>SessionTokens</c> before it raises <c>OnDespawnCharacter</c>, so the logout flush has
			/// nothing left to quote unless it was kept here first.
			/// </remarks>
			public void RememberLease(long characterID, CharacterSessionInfo info)
			{
				lock (gate)
				{
					lastKnownLease[characterID] = info;
				}
			}

			/// <summary>
			/// Retrieves the last ownership triple seen for a character.
			/// </summary>
			public bool TryGetLease(long characterID, out CharacterSessionInfo info)
			{
				lock (gate)
				{
					return lastKnownLease.TryGetValue(characterID, out info);
				}
			}

			/// <summary>
			/// Queues a character for an authoritative re-write of all three containers.
			/// </summary>
			public void RequestReconcile(long characterID)
			{
				lock (gate)
				{
					reconcileRequests.Add(characterID);
				}
			}

			/// <summary>
			/// Takes and clears the pending repair queue.
			/// </summary>
			public List<long> DrainReconcileRequests()
			{
				lock (gate)
				{
					if (reconcileRequests.Count == 0)
					{
						return null;
					}
					var drained = new List<long>(reconcileRequests);
					reconcileRequests.Clear();
					return drained;
				}
			}

			/// <summary>
			/// Drops every trace of a character. The sequence counter goes with it: a character that
			/// comes back has been reloaded from the database, so nothing captured before it left is
			/// comparable with anything captured after.
			/// </summary>
			public void ForgetCharacter(long characterID)
			{
				lock (gate)
				{
					nextSequence.Remove(characterID);
					lastAppliedAny.Remove(characterID);
					lastAppliedSnapshot.Remove(characterID);
					lastKnownLease.Remove(characterID);
					reconcileRequests.Remove(characterID);
				}
			}

			/// <summary>
			/// Clears all state.
			/// </summary>
			public void Clear()
			{
				lock (gate)
				{
					nextSequence.Clear();
					lastAppliedAny.Clear();
					lastAppliedSnapshot.Clear();
					lastKnownLease.Clear();
					reconcileRequests.Clear();
				}
			}
		}

		/// <summary>
		/// One slot vacancy to write, with the version that authorises it.
		/// </summary>
		private readonly struct ItemSlotDelete
		{
			/// <summary>Slot index being vacated.</summary>
			public readonly int Slot;

			/// <summary>Version the delete is authorised against. See the SLOT VERSIONING CONTRACT on the services.</summary>
			public readonly long Version;

			/// <summary>Initializes a slot vacancy.</summary>
			public ItemSlotDelete(int slot, long version)
			{
				Slot = slot;
				Version = version;
			}
		}

		/// <summary>
		/// One logical item operation, captured on the main thread and applied to the database as a
		/// single transaction that either fully lands or fully rolls back.
		/// </summary>
		/// <remarks>
		/// <para>
		/// WHAT THIS REPLACES. A cross-container move used to be three independently enqueued work
		/// items — persist the source container, delete the vacated source slots, persist the
		/// destination — and each ran in its own transaction. They were ordered but not atomic, so a
		/// crash, a disconnect, or a database hiccup between any two of them left the move half
		/// applied: the item deleted from the bank and never written to the inventory (destroyed), or
		/// written to the inventory and never deleted from the bank (duplicated on next login).
		/// Everything a single operation touches now travels in one of these and commits together.
		/// </para>
		/// <para>
		/// A batch is immutable from the moment it leaves the main thread. It holds DTOs, never live
		/// <c>Item</c> references, because by the time the worker runs the player may have moved the
		/// items, logged out, or had the object pooled.
		/// </para>
		/// </remarks>
		private sealed class ItemWriteBatch
		{
			/// <summary>Owning character.</summary>
			public long CharacterID;

			/// <summary>
			/// Ownership triple captured at the same instant as the data. Proving ownership with the
			/// lease held WHEN THE MUTATION HAPPENED — rather than whatever the server holds when the
			/// write eventually runs — is what makes the check mean anything.
			/// </summary>
			public CharacterSessionLeaseData Lease;

			/// <summary>Per-character capture order. See <see cref="ItemWriteJournal.TryClaimSequence"/>.</summary>
			public long Sequence;

			/// <summary>Whether this batch is a full authoritative snapshot rather than an increment.</summary>
			public bool IsSnapshot;

			/// <summary>Short description used in log lines when the batch fails.</summary>
			public string Operation;

			/// <summary>Inventory rows to upsert; for a snapshot, the container's entire contents.</summary>
			public List<CharacterInventoryData> InventoryWrites;

			/// <summary>Inventory slots to vacate. Unused by snapshots, which prune instead.</summary>
			public List<ItemSlotDelete> InventoryDeletes;

			/// <summary>Bank rows to upsert; for a snapshot, the container's entire contents.</summary>
			public List<CharacterBankData> BankWrites;

			/// <summary>Bank slots to vacate.</summary>
			public List<ItemSlotDelete> BankDeletes;

			/// <summary>Equipment rows to upsert; for a snapshot, the container's entire contents.</summary>
			public List<CharacterEquipmentData> EquipmentWrites;

			/// <summary>Equipment slots to vacate.</summary>
			public List<ItemSlotDelete> EquipmentDeletes;

			/// <summary>Attribute rows to upsert. Equipping changes stats, and the stat write belongs to the same operation.</summary>
			public List<CharacterAttributeData> AttributeWrites;

			/// <summary>True when the batch would write nothing at all.</summary>
			/// <remarks>
			/// A SNAPSHOT is judged on whether its lists are PRESENT, not on whether they hold
			/// anything. "This container is empty" is a real statement that must still prune the
			/// character's rows — a player who empties their bank would otherwise keep every bank
			/// row forever, because the batch that was supposed to delete them looked like a no-op.
			/// An INCREMENTAL is judged on content, because an empty list there genuinely asks for
			/// nothing.
			/// </remarks>
			public bool IsEmpty =>
				IsSnapshot
					? InventoryWrites == null &&
						BankWrites == null &&
						EquipmentWrites == null &&
						(AttributeWrites == null || AttributeWrites.Count == 0)
					: (InventoryWrites == null || InventoryWrites.Count == 0) &&
						(InventoryDeletes == null || InventoryDeletes.Count == 0) &&
						(BankWrites == null || BankWrites.Count == 0) &&
						(BankDeletes == null || BankDeletes.Count == 0) &&
						(EquipmentWrites == null || EquipmentWrites.Count == 0) &&
						(EquipmentDeletes == null || EquipmentDeletes.Count == 0) &&
						(AttributeWrites == null || AttributeWrites.Count == 0);

			/// <summary>Adds inventory rows to upsert.</summary>
			public void AddInventoryWrites(List<CharacterInventoryData> dtos)
			{
				if (dtos == null || dtos.Count == 0) return;
				(InventoryWrites ??= new List<CharacterInventoryData>(dtos.Count)).AddRange(dtos);
			}

			/// <summary>Adds one inventory row to upsert.</summary>
			public void AddInventoryWrite(CharacterInventoryData dto)
			{
				(InventoryWrites ??= new List<CharacterInventoryData>(1)).Add(dto);
			}

			/// <summary>Marks an inventory slot vacant.</summary>
			public void AddInventoryDelete(int slot, long version)
			{
				(InventoryDeletes ??= new List<ItemSlotDelete>(1)).Add(new ItemSlotDelete(slot, version));
			}

			/// <summary>Adds bank rows to upsert.</summary>
			public void AddBankWrites(List<CharacterBankData> dtos)
			{
				if (dtos == null || dtos.Count == 0) return;
				(BankWrites ??= new List<CharacterBankData>(dtos.Count)).AddRange(dtos);
			}

			/// <summary>Adds one bank row to upsert.</summary>
			public void AddBankWrite(CharacterBankData dto)
			{
				(BankWrites ??= new List<CharacterBankData>(1)).Add(dto);
			}

			/// <summary>Marks a bank slot vacant.</summary>
			public void AddBankDelete(int slot, long version)
			{
				(BankDeletes ??= new List<ItemSlotDelete>(1)).Add(new ItemSlotDelete(slot, version));
			}

			/// <summary>Adds one equipment row to upsert.</summary>
			public void AddEquipmentWrite(CharacterEquipmentData dto)
			{
				(EquipmentWrites ??= new List<CharacterEquipmentData>(1)).Add(dto);
			}

			/// <summary>Marks an equipment slot vacant.</summary>
			public void AddEquipmentDelete(int slot, long version)
			{
				(EquipmentDeletes ??= new List<ItemSlotDelete>(1)).Add(new ItemSlotDelete(slot, version));
			}

			/// <summary>Adds attribute rows to upsert.</summary>
			public void AddAttributeWrites(List<CharacterAttributeData> dtos)
			{
				if (dtos == null || dtos.Count == 0) return;
				(AttributeWrites ??= new List<CharacterAttributeData>(dtos.Count)).AddRange(dtos);
			}
		}

		/// <summary>
		/// Per-character write ordering, ownership caching and repair queue. Created in
		/// <c>InitializeOnce</c> and cleared in <c>OnDeinitialize</c>.
		/// </summary>
		private ItemWriteJournal itemWriteJournal = new ItemWriteJournal();

		/// <summary>
		/// THE MEMORY / DATABASE INVARIANT — read this before changing anything below.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>The in-memory containers are authoritative. The database is a replica that must
		/// converge to them. Memory is never rolled back by the persistence layer.</b>
		/// </para>
		/// <para>
		/// The handlers mutate the containers synchronously, on the main thread, and only then
		/// capture a batch and hand it to the worker. That ordering is not an accident and the
		/// alternative was considered and rejected twice over:
		/// </para>
		/// <list type="number">
		///   <item><description>
		///     MUTATING MEMORY ONLY AFTER THE COMMIT would hold every item operation open across a
		///     database round trip. The containers are replicated <c>SyncObject</c>s and the client
		///     releases its per-slot pending lock on the echo, so the whole client contract (see the
		///     item-UI pass) would have to change; worse, the slots would stay mutable during the
		///     wait, which is a far larger dupe surface than the one being closed.
		///   </description></item>
		///   <item><description>
		///     RESTORING A CAPTURED PRE-IMAGE on failure is worse still. The failure arrives
		///     asynchronously, by which time the player may have performed further operations on
		///     those very slots. Replaying a stale pre-image over them destroys or duplicates items
		///     on its own — the repair becomes the bug.
		///   </description></item>
		/// </list>
		/// <para>
		/// So: the database transaction is all-or-nothing, and when it rolls back the database is
		/// left exactly as it was before the operation while memory has moved on. That divergence is
		/// resolved in memory's favour by <see cref="ItemWriteJournal.RequestReconcile"/>, which
		/// schedules an immediate authoritative snapshot — prune plus ungated upsert of all three
		/// containers, itself in one transaction. Nothing is duplicated, because memory holds exactly
		/// one copy of the item; nothing is lost, because the snapshot re-states the whole truth.
		/// </para>
		/// <para>
		/// THE RESIDUAL, stated plainly: if the process dies between the memory mutation and the
		/// commit, the operation is lost — the database still holds the pre-operation state. The item
		/// is exactly where it was before the player moved it. That is the honest limit of an
		/// authoritative-memory design, and it fails in the only acceptable direction: a lost action,
		/// never a lost or duplicated item.
		/// </para>
		/// <para>
		/// One consequence worth naming: a rolled-back batch is NOT retried. Retrying it would
		/// re-apply a state that memory may have moved past. The reconcile snapshot supersedes it and
		/// is always at least as current.
		/// </para>
		/// </remarks>
		private ItemWriteBatch BeginItemBatch(long characterID, string operation, bool isSnapshot = false)
		{
			return new ItemWriteBatch()
			{
				CharacterID = characterID,
				Lease = ResolveSessionLease(characterID),
				Sequence = itemWriteJournal.NextSequence(characterID),
				IsSnapshot = isSnapshot,
				Operation = operation,
			};
		}

		/// <summary>
		/// Reads the ownership triple this server currently holds for a character. Main thread only.
		/// </summary>
		/// <remarks>
		/// Falls back to the last triple seen because <c>CharacterSystem.SaveAndDespawnCharacter</c>
		/// removes the token from <c>SessionTokens</c> before raising <c>OnDespawnCharacter</c>, and
		/// the logout flush hangs off that event. An invalid (default) result is not fatal: the
		/// ownership assertion still refuses to write over a session someone else holds, it simply
		/// cannot additionally prove the write is ours.
		/// </remarks>
		private CharacterSessionLeaseData ResolveSessionLease(long characterID)
		{
			if (Server != null &&
				Server.DataContainerRegistry.TryGet(out ICharacterMappingData<NetworkConnection> mappingData) &&
				mappingData.SessionTokens.TryGetValue(characterID, out CharacterSessionInfo live))
			{
				itemWriteJournal.RememberLease(characterID, live);
				return new CharacterSessionLeaseData(characterID, live.ServerID, live.Token);
			}

			if (itemWriteJournal.TryGetLease(characterID, out CharacterSessionInfo remembered))
			{
				return new CharacterSessionLeaseData(characterID, remembered.ServerID, remembered.Token);
			}

			return default;
		}

		/// <summary>
		/// Hands a completed batch to the async worker as a single unit of work.
		/// </summary>
		/// <returns>
		/// <c>true</c> when the batch was enqueued normally. <c>false</c> means the bounded queue was
		/// full and the work is running on the thread-pool fallback instead — it has NOT been
		/// discarded, which is why the client is told <c>ServerBusy</c> ("outcome unknown") rather
		/// than that the operation failed.
		/// </returns>
		private bool EnqueueItemBatch(ItemWriteBatch batch)
		{
			if (batch == null || batch.IsEmpty)
			{
				// Nothing to write is a success: the operation changed no persisted state.
				return true;
			}
			return EnqueuePersistence(() => ApplyItemBatchAsync(batch), batch.CharacterID);
		}

		/// <summary>
		/// Applies one batch inside a single database transaction. Worker thread.
		/// </summary>
		private async Task ApplyItemBatchAsync(ItemWriteBatch batch)
		{
			try
			{
				var registry = Server?.Database?.ServiceRegistry;
				if (registry == null)
				{
					await Log.Error("CharacterInventorySystem", "ApplyItemBatchAsync: Database service registry is unavailable");
					return;
				}

				if (!registry.TryGet<IUnitOfWorkService>(out var unitOfWorkService) ||
					!registry.TryGet<ICharacterSessionOwnershipService>(out var ownershipService))
				{
					await Log.Error("CharacterInventorySystem", "ApplyItemBatchAsync: Failed to resolve IUnitOfWorkService or ICharacterSessionOwnershipService");
					return;
				}

				// Cheap pre-test only, so an already-superseded batch does not cost a transaction.
				// It is NOT the decision — that is made below, under the ownership row lock.
				if (!itemWriteJournal.ShouldApply(batch.CharacterID, batch.Sequence, batch.IsSnapshot))
				{
					await Log.Debug("CharacterInventorySystem", $"ApplyItemBatchAsync: skipped superseded {batch.Operation} (CharID={batch.CharacterID}, Seq={batch.Sequence}, Snapshot={batch.IsSnapshot})");
					return;
				}

				DatabaseResult<IUnitOfWork> beginResult = await unitOfWorkService.BeginAsync();
				if (!beginResult.IsSuccess || beginResult.Data == null)
				{
					await Log.Warning("CharacterInventorySystem", $"ApplyItemBatchAsync: could not begin unit of work for {batch.Operation} (CharID={batch.CharacterID}): {beginResult.ErrorCode} - {beginResult.ErrorMessage}");
					itemWriteJournal.RequestReconcile(batch.CharacterID);
					return;
				}

				await using IUnitOfWork unitOfWork = beginResult.Data;

				// Ownership first, and inside the transaction: it takes a row lock on the character
				// that a competing claim must wait behind, so it is a guarantee for the whole
				// transaction rather than an observation about the moment it ran.
				DatabaseResult ownership = await ownershipService.AssertOwnershipAsync(batch.CharacterID, batch.Lease, allowUnclaimed: true);
				if (!ownership.IsSuccess)
				{
					await unitOfWork.RollbackAsync();

					// Deliberately NOT reconciled. Another server is authoritative for this character
					// now; rewriting our in-memory copy over its state is exactly the duplication the
					// guard exists to prevent. Our containers are the stale ones.
					await Log.Warning("CharacterInventorySystem", $"ApplyItemBatchAsync: refused {batch.Operation} for character {batch.CharacterID}: {ownership.ErrorCode} - {ownership.ErrorMessage}");
					return;
				}

				// THE ORDERING DECISION, made here and nowhere else. The row lock taken above is what
				// gives it meaning: every batch for this character passes through this point one at a
				// time, so the watermark this reads is the watermark of every batch that has already
				// been authorised — not a snapshot of the past that another batch has since moved on
				// from. See ItemWriteJournal.TryClaimSequence.
				if (!itemWriteJournal.TryClaimSequence(batch.CharacterID, batch.Sequence, batch.IsSnapshot))
				{
					await unitOfWork.RollbackAsync();

					// Superseded, not failed: a write captured later has already landed, and it is
					// strictly more current than this one. No reconcile — there is nothing to repair.
					await Log.Debug("CharacterInventorySystem", $"ApplyItemBatchAsync: skipped superseded {batch.Operation} (CharID={batch.CharacterID}, Seq={batch.Sequence}, Snapshot={batch.IsSnapshot})");
					return;
				}

				DatabaseResult applied = await ApplyBatchStepsAsync(registry, batch);
				if (!applied.IsSuccess)
				{
					await unitOfWork.RollbackAsync();
					await Log.Warning("CharacterInventorySystem", $"ApplyItemBatchAsync: rolled back {batch.Operation} for character {batch.CharacterID}: {applied.ErrorCode} - {applied.ErrorMessage}");
					itemWriteJournal.RequestReconcile(batch.CharacterID);
					return;
				}

				DatabaseResult commit = await unitOfWork.CommitAsync();
				if (!commit.IsSuccess)
				{
					await Log.Warning("CharacterInventorySystem", $"ApplyItemBatchAsync: commit failed for {batch.Operation} (CharID={batch.CharacterID}): {commit.ErrorCode} - {commit.ErrorMessage}");
					itemWriteJournal.RequestReconcile(batch.CharacterID);
					return;
				}

				if (unitOfWork.DisposeFault != null)
				{
					await Log.Warning("CharacterInventorySystem", $"ApplyItemBatchAsync: unit of work disposal reported {unitOfWork.DisposeFault.Value.ErrorCode} - {unitOfWork.DisposeFault.Value.ErrorMessage}");
				}
			}
			catch (Exception ex)
			{
				await Log.Error("CharacterInventorySystem", $"ApplyItemBatchAsync failed for {batch?.Operation} (CharID={batch?.CharacterID}): {ex}");
				if (batch != null)
				{
					itemWriteJournal.RequestReconcile(batch.CharacterID);
				}
			}
		}

		/// <summary>
		/// Issues every statement in a batch against the ambient unit of work.
		/// </summary>
		/// <remarks>
		/// Stops at the first failure and reports it. The caller rolls the whole transaction back, so
		/// there is no need — and no way — for a later step to observe a partial application.
		/// <para>
		/// Vacancies are written before occupancies within each table. Nothing in the current handler
		/// set relies on it (a swap never deletes and writes the same slot), but it is the order that
		/// stays correct if one ever does, and the reverse order would fail the unique index on
		/// <c>(character_id, slot)</c> rather than doing something subtle.
		/// </para>
		/// </remarks>
		private static async Task<DatabaseResult> ApplyBatchStepsAsync(IDatabaseServiceRegistry registry, ItemWriteBatch batch)
		{
			if (batch.IsSnapshot)
			{
				// A snapshot list that is present but empty is a legitimate statement — "this
				// container holds nothing" — and must still prune. A null list means the character
				// has no such container and it is left entirely alone.
				if (batch.InventoryWrites != null)
				{
					if (!registry.TryGet<ICharacterInventoryService>(out var inventoryService))
					{
						return DatabaseResult.Failure(DatabaseErrorCodes.InvalidConfiguration, "ICharacterInventoryService is not registered.");
					}
					DatabaseResult result = await inventoryService.SaveSnapshotAsync(batch.CharacterID, batch.InventoryWrites);
					if (!result.IsSuccess) return result;
				}
				if (batch.BankWrites != null)
				{
					if (!registry.TryGet<ICharacterBankService>(out var bankService))
					{
						return DatabaseResult.Failure(DatabaseErrorCodes.InvalidConfiguration, "ICharacterBankService is not registered.");
					}
					DatabaseResult result = await bankService.SaveSnapshotAsync(batch.CharacterID, batch.BankWrites);
					if (!result.IsSuccess) return result;
				}
				if (batch.EquipmentWrites != null)
				{
					if (!registry.TryGet<ICharacterEquipmentService>(out var equipmentService))
					{
						return DatabaseResult.Failure(DatabaseErrorCodes.InvalidConfiguration, "ICharacterEquipmentService is not registered.");
					}
					DatabaseResult result = await equipmentService.SaveSnapshotAsync(batch.CharacterID, batch.EquipmentWrites);
					if (!result.IsSuccess) return result;
				}
			}
			else
			{
				if (batch.InventoryDeletes != null || batch.InventoryWrites != null)
				{
					if (!registry.TryGet<ICharacterInventoryService>(out var inventoryService))
					{
						return DatabaseResult.Failure(DatabaseErrorCodes.InvalidConfiguration, "ICharacterInventoryService is not registered.");
					}
					if (batch.InventoryDeletes != null)
					{
						foreach (ItemSlotDelete vacancy in batch.InventoryDeletes)
						{
							DatabaseResult result = await inventoryService.DeleteAsync(batch.CharacterID, vacancy.Slot, vacancy.Version);
							if (!result.IsSuccess) return result;
						}
					}
					if (batch.InventoryWrites != null && batch.InventoryWrites.Count > 0)
					{
						DatabaseResult result = await inventoryService.PersistAsync(batch.InventoryWrites);
						if (!result.IsSuccess) return result;
					}
				}

				if (batch.BankDeletes != null || batch.BankWrites != null)
				{
					if (!registry.TryGet<ICharacterBankService>(out var bankService))
					{
						return DatabaseResult.Failure(DatabaseErrorCodes.InvalidConfiguration, "ICharacterBankService is not registered.");
					}
					if (batch.BankDeletes != null)
					{
						foreach (ItemSlotDelete vacancy in batch.BankDeletes)
						{
							DatabaseResult result = await bankService.DeleteAsync(batch.CharacterID, vacancy.Slot, vacancy.Version);
							if (!result.IsSuccess) return result;
						}
					}
					if (batch.BankWrites != null && batch.BankWrites.Count > 0)
					{
						DatabaseResult result = await bankService.PersistAsync(batch.BankWrites);
						if (!result.IsSuccess) return result;
					}
				}

				if (batch.EquipmentDeletes != null || batch.EquipmentWrites != null)
				{
					if (!registry.TryGet<ICharacterEquipmentService>(out var equipmentService))
					{
						return DatabaseResult.Failure(DatabaseErrorCodes.InvalidConfiguration, "ICharacterEquipmentService is not registered.");
					}
					if (batch.EquipmentDeletes != null)
					{
						foreach (ItemSlotDelete vacancy in batch.EquipmentDeletes)
						{
							DatabaseResult result = await equipmentService.DeleteAsync(batch.CharacterID, vacancy.Slot, vacancy.Version);
							if (!result.IsSuccess) return result;
						}
					}
					if (batch.EquipmentWrites != null && batch.EquipmentWrites.Count > 0)
					{
						foreach (CharacterEquipmentData dto in batch.EquipmentWrites)
						{
							DatabaseResult<long> result = await equipmentService.PersistAsync(dto);
							if (!result.IsSuccess)
							{
								return DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
							}
						}
					}
				}
			}

			// Attributes ride along with the item write rather than in a transaction of their own.
			// Equipping is one operation from the player's point of view, and half of it landing —
			// the item equipped with none of its stats, or the stats without the item — is a state
			// nothing else in the server knows how to repair.
			if (batch.AttributeWrites != null && batch.AttributeWrites.Count > 0)
			{
				if (!registry.TryGet<ICharacterAttributeService>(out var attributeService))
				{
					return DatabaseResult.Failure(DatabaseErrorCodes.InvalidConfiguration, "ICharacterAttributeService is not registered.");
				}
				DatabaseResult result = await attributeService.PersistAsync(batch.AttributeWrites);
				if (!result.IsSuccess) return result;
			}

			return DatabaseResult.Success();
		}

		/// <summary>
		/// Re-captures and re-writes every container of each character queued for repair. Main thread.
		/// </summary>
		private void DrainReconcileRequests()
		{
			List<long> pending = itemWriteJournal.DrainReconcileRequests();
			if (pending == null)
			{
				return;
			}

			if (!Server.DataContainerRegistry.TryGet(out ICharacterMappingData<NetworkConnection> mappingData))
			{
				return;
			}

			foreach (long characterID in pending)
			{
				if (mappingData.CharactersByID.TryGetValue(characterID, out IPlayerCharacter character))
				{
					SnapshotCharacterItems(character);
				}
				// A character that is no longer resident needs no repair from us: its logout flush
				// already ran, or another server owns it and will write its own truth.
			}
		}

		#endregion

		#region Item Snapshot

		/// <summary>
		/// Writes a full item snapshot for every character currently resident on this scene server.
		/// </summary>
		/// <remarks>
		/// <para>
		/// THE POINT OF THIS: until now the incremental per-slot writes issued by the broadcast
		/// handlers below were the ONLY record of a character's items. Neither the periodic save nor
		/// the logout save touched inventory, bank or equipment. So any incremental write that was
		/// silently rejected — a stale version, a dropped async work item, a handler that returned
		/// early — was not a glitch that the next save would paper over; it was permanent loss the
		/// moment the player logged out. A snapshot on a timer downgrades every one of those failures
		/// to something that survives at most one snapshot interval.
		/// </para>
		/// <para>
		/// IT CANNOT BE A DUPE VECTOR. The snapshot is not additive: <c>SaveSnapshotAsync</c> prunes
		/// every row for the character whose slot is not in the snapshot, in the same transaction as
		/// the upsert. So it writes exactly the set of items the server believes in — no more, and no
		/// fewer — and running it twice is idempotent. The one arrangement that could duplicate an
		/// item is the same item appearing in two containers at once, which is the bug fixed in
		/// EquipmentController.Equip, not something the snapshot introduces.
		/// </para>
		/// <para>
		/// INTERACTION WITH THE SLOT VERSIONING FIX: the snapshot's upsert is deliberately not
		/// version-gated, because version gating is the mechanism that makes writes disappear and this
		/// is the backstop for exactly that. Ordering against the incremental writes is preserved
		/// because both are enqueued under the same per-character entity key, which the async worker
		/// guarantees to process FIFO. The DTO builders bump <c>item.Version</c> as they always have,
		/// so a later incremental write still beats the snapshot on the gated path.
		/// </para>
		/// <para>
		/// ORDERING AGAINST INCREMENTAL WRITES. The snapshot's upsert is deliberately NOT
		/// version-gated — gating is the mechanism that makes writes disappear, and this is the
		/// backstop for exactly that — so a snapshot that lands out of order would happily undo a
		/// newer incremental write. Two things stop it. Every batch carries a main-thread capture
		/// sequence and <see cref="ItemWriteJournal.TryClaimSequence"/> refuses a snapshot once any
		/// later-captured write has committed; and the whole snapshot (all three containers) is one
		/// transaction, so the three tables can never describe different moments in time.
		/// </para>
		/// <para>
		/// SESSION GUARD. A snapshot in flight during a scene-server handover can no longer land on
		/// the destination's state: every batch quotes the ownership triple held when it was
		/// captured, and <c>ICharacterSessionOwnershipService.AssertOwnershipAsync</c> refuses it
		/// inside the same transaction once the claim has moved.
		/// </para>
		/// </remarks>
		private void SnapshotAllResidentCharacterItems()
		{
			if (!Initialized || Server == null)
			{
				return;
			}

			if (!Server.DataContainerRegistry.TryGet(out ICharacterMappingData<NetworkConnection> mappingData))
			{
				return;
			}

			foreach (IPlayerCharacter character in mappingData.CharactersByID.Values)
			{
				SnapshotCharacterItems(character);
			}
		}

		/// <summary>
		/// Captures and enqueues a full item snapshot for one character. Main thread only.
		/// </summary>
		/// <param name="character">The character to snapshot.</param>
		private void SnapshotCharacterItems(IPlayerCharacter character)
		{
			if (character == null || character.ID <= 0)
			{
				return;
			}

			long characterID = character.ID;

			// Every DTO list is built here, synchronously, on the main thread. The async work item
			// must never touch a live container: by the time it runs the player may have moved items,
			// logged out, or had the object pooled.
			// All three containers travel in ONE batch, under ONE sequence number and ONE
			// transaction. Three separate snapshots could interleave with an incremental
			// write between them and leave the three tables describing different moments.
			ItemWriteBatch batch = BeginItemBatch(characterID, "ItemSnapshot", isSnapshot: true);
			if (character.TryGet(out IInventoryController inventoryController))
			{
				batch.InventoryWrites = BuildInventorySnapshot(characterID, inventoryController);
			}
			if (character.TryGet(out IBankController bankController))
			{
				batch.BankWrites = BuildBankSnapshot(characterID, bankController);
			}
			if (character.TryGet(out IEquipmentController equipmentController))
			{
				batch.EquipmentWrites = BuildEquipmentSnapshot(characterID, equipmentController);
			}
			EnqueueItemBatch(batch);
		}

		/// <summary>
		/// Snapshots a character's items as it leaves this scene server.
		/// </summary>
		/// <remarks>
		/// CharacterSystem raises OnDespawnCharacter from SaveAndDespawnCharacter while the character
		/// object is still live, so the containers are still readable here. This is the logout half of
		/// the snapshot: without it, everything the player did since the last periodic sweep would
		/// still depend entirely on the incremental writes having landed.
		/// </remarks>
		private void CharacterSystem_OnDespawnCharacter(NetworkConnection conn, IPlayerCharacter character)
		{
			SnapshotCharacterItems(character);

			// The flush batch is already captured — it holds its own copy of the DTOs, its own
			// sequence number and its own ownership triple — so dropping the journal entry now
			// cannot affect it.
			//
			// Resetting the sequence counter here is safe ONLY because of the ownership assertion.
			// If this character comes back to this same scene server, it comes back under a NEW
			// session token, so any batch still in flight from the previous visit quotes a triple
			// that no longer matches and is refused rather than applied out of order. Without that
			// guard this reset would be a resurrection bug, which is why the two changes belong
			// together and neither should be removed on its own.
			if (character != null && character.ID > 0)
			{
				itemWriteJournal.ForgetCharacter(character.ID);
			}
		}

		/// <summary>
		/// Builds the inventory snapshot DTO list from a live container. Main thread only.
		/// </summary>
		private List<CharacterInventoryData> BuildInventorySnapshot(long characterID, IItemContainer container)
		{
			var dtos = new List<CharacterInventoryData>(container.Items.Count);
			for (int i = 0; i < container.Items.Count; ++i)
			{
				Item item = container.Items[i];
				if (item == null || item.Template == null)
				{
					continue;
				}
				// Trust the list position, not item.Slot. They agree in normal operation, but the
				// snapshot's whole job is to be right when something else has gone wrong, and a slot
				// field that disagrees with its container would otherwise write the item twice.
				item.Slot = i;
				dtos.Add(BuildInventoryItemData(characterID, item));
			}
			return dtos;
		}

		/// <summary>
		/// Builds the bank snapshot DTO list from a live container. Main thread only.
		/// </summary>
		private List<CharacterBankData> BuildBankSnapshot(long characterID, IItemContainer container)
		{
			var dtos = new List<CharacterBankData>(container.Items.Count);
			for (int i = 0; i < container.Items.Count; ++i)
			{
				Item item = container.Items[i];
				if (item == null || item.Template == null)
				{
					continue;
				}
				item.Slot = i;
				dtos.Add(BuildBankItemData(characterID, item));
			}
			return dtos;
		}

		/// <summary>
		/// Builds the equipment snapshot DTO list from a live container. Main thread only.
		/// </summary>
		private List<CharacterEquipmentData> BuildEquipmentSnapshot(long characterID, IItemContainer container)
		{
			var dtos = new List<CharacterEquipmentData>(container.Items.Count);
			for (int i = 0; i < container.Items.Count; ++i)
			{
				Item item = container.Items[i];
				if (item == null || item.Template == null)
				{
					continue;
				}
				item.Slot = i;
				dtos.Add(BuildEquipmentItemData(characterID, item));
			}
			return dtos;
		}

		#endregion

		#region Client Failure Notification

		/// <summary>
		/// Tells a client that the item operation it asked for did not happen.
		/// </summary>
		/// <remarks>
		/// Every item handler in this file declines requests by returning, and there were roughly two
		/// dozen such returns. The client had already moved its own view of the slot, or was holding a
		/// drag, and nothing ever came back — so a refused operation left a stale slot on screen until
		/// the panel was rebuilt or the player relogged.
		/// <para>
		/// The handlers below therefore send this from their <c>finally</c> rather than at each return
		/// site. That is deliberate: a per-return-site call is a list that the next edit forgets to
		/// extend, whereas "the handler exited without broadcasting success" is a property that stays
		/// true for returns nobody has written yet.
		/// </para>
		/// <para>
		/// The message carries no item identity — only the operation, a coarse reason, and the slot
		/// indices the client itself sent. It is an instruction to resync those slots, never a source
		/// of truth about them.
		/// </para>
		/// </remarks>
		private void SendItemOperationFailed(NetworkConnection conn, ItemOperationType operation, ItemOperationFailureReason reason, InventoryType container, int slot, int secondarySlot = -1)
		{
			if (conn == null || !conn.IsActive)
			{
				return;
			}

			Server?.NetworkWrapper?.Broadcast(conn, new ItemOperationFailedBroadcast()
			{
				Operation = operation,
				Reason = reason,
				Container = container,
				Slot = slot,
				SecondarySlot = secondarySlot,
			}, true, Channel.Reliable);
		}

		#endregion

		/// <summary>
		/// Attempts to acquire ingress debounce and in-flight guard for a connection operation.
		/// </summary>
		private bool TryBeginIngressGuard(int connectionId, IngressOperation operation, out long guardKey)
		{
			if (!Server.DataContainerRegistry.TryGet<ICharacterInventorySystemRuntimeData>(out var runtimeData))
			{
				guardKey = 0;
				return false;
			}
			return runtimeData.IngressGuard.TryBegin(connectionId, (byte)operation, ingressDebounceMilliseconds, out guardKey, GlobalPerConnectionRateMilliseconds);
		}

		/// <summary>
		/// Releases an ingress in-flight guard key.
		/// </summary>
		private void EndIngressGuard(long guardKey)
		{
			if (Server.DataContainerRegistry.TryGet<ICharacterInventorySystemRuntimeData>(out var runtimeData))
			{
				runtimeData.IngressGuard.End(guardKey);
			}
		}

		/// <summary>
		/// Swaps two items within the same container and collects the affected items.
		/// </summary>
		/// <param name="container">Item container to swap items in.</param>
		/// <param name="fromIndex">Source slot index.</param>
		/// <param name="toIndex">Target slot index.</param>
		/// <param name="affectedItems">Out: list of items whose slots changed.</param>
		/// <returns>True if swap succeeded, false otherwise.</returns>
		public bool SwapContainerItems(IItemContainer container, int fromIndex, int toIndex, out List<Item> affectedItems)
		{
			return SwapContainerItems(container, fromIndex, toIndex, out affectedItems, out int _);
		}

		/// <summary>
		/// Swaps two items within the same container, reporting both the affected items and any
		/// slot the swap left empty.
		/// </summary>
		/// <remarks>
		/// The vacated slot is the whole reason this overload exists. Item rows are keyed
		/// <c>(character_id, slot)</c>, so a move onto an EMPTY slot writes the item at its new
		/// index and leaves the row at the old index untouched — the same item persisted twice.
		/// Only the write side was emitted here before, because <paramref name="affectedItems"/>
		/// collects non-null items and a move-to-empty produces exactly one of them, which reads
		/// like a complete description of the change and is not.
		/// <para>
		/// The 60-second snapshot prunes the stale row, so this never survived a clean shutdown —
		/// but "duplicated until the next snapshot, permanently if the scene server dies first" is
		/// not a guarantee worth relying on when the delete costs one list entry.
		/// </para>
		/// </remarks>
		/// <param name="container">Item container to swap items in.</param>
		/// <param name="fromIndex">Source slot index.</param>
		/// <param name="toIndex">Target slot index.</param>
		/// <param name="affectedItems">Out: list of items whose slots changed.</param>
		/// <param name="vacatedSlot">Out: slot the swap emptied, or -1 when both slots stay filled.</param>
		/// <returns>True if swap succeeded, false otherwise.</returns>
		public bool SwapContainerItems(IItemContainer container, int fromIndex, int toIndex, out List<Item> affectedItems, out int vacatedSlot)
		{
			affectedItems = null;
			vacatedSlot = -1;
			if (container != null &&
				container.SwapItemSlots(fromIndex, toIndex, out Item fromItem, out Item toItem))
			{
				affectedItems = new List<Item>(2);
				if (fromItem != null) affectedItems.Add(fromItem);
				if (toItem != null) affectedItems.Add(toItem);

				// Exactly one of the two can be empty: SwapItemSlots refuses only on invalid or
				// locked slots, and a swap of two empty slots is a no-op the handlers reject by
				// requiring From != To. Whichever side had no item is now the vacated one.
				if (toItem == null)
				{
					vacatedSlot = fromIndex;
				}
				else if (fromItem == null)
				{
					vacatedSlot = toIndex;
				}
				return true;
			}
			return false;
		}

		/// <summary>
		/// Swaps items between two containers and collects affected items and deleted slots.
		/// </summary>
		/// <param name="from">Source item container.</param>
		/// <param name="to">Target item container.</param>
		/// <param name="fromIndex">Source slot index.</param>
		/// <param name="toIndex">Target slot index.</param>
		/// <param name="affectedFromItems">Out: items placed into the source container.</param>
		/// <param name="deletedFromSlots">Out: slot indices vacated in the source container.</param>
		/// <param name="affectedToItems">Out: items placed into the destination container.</param>
		/// <returns>True if swap succeeded, false otherwise.</returns>
		public bool SwapContainerItems(IItemContainer from, IItemContainer to, int fromIndex, int toIndex,
			out List<Item> affectedFromItems, out List<long> deletedFromSlots, out List<Item> affectedToItems)
		{
			affectedFromItems = null;
			deletedFromSlots = null;
			affectedToItems = null;

			// same container... do the quick swap
			if (from == to)
			{
				if (SwapContainerItems(from, fromIndex, toIndex, out affectedFromItems))
				{
					return true;
				}
				return false;
			}
			// Pre-flight the locks before anything is mutated. SetItemSlot now refuses a locked
			// slot, and this method writes to both containers in sequence — so without this check a
			// locked destination would leave the source already emptied and the item nowhere.
			// Refusing up front keeps the operation all-or-nothing.
			if (from != null &&
				to != null &&
				(from.IsSlotLocked(fromIndex) || to.IsSlotLocked(toIndex)))
			{
				return false;
			}

			if (from != null &&
				to != null &&
				from.TryGetItem(fromIndex, out Item fromItem))
			{
				affectedFromItems = new List<Item>(1);
				deletedFromSlots = new List<long>(1);
				affectedToItems = new List<Item>(1);

				// Capture original state for rollback in case of an unexpected exception.
				Item originalFromItem = fromItem;
				Item originalToItem = null;
				bool hadToItem = to.TryGetItem(toIndex, out Item toItem);

				try
				{
					// check if we need to swap items
					if (hadToItem)
					{
						originalToItem = toItem;
						// put the target item in the old container
						from.SetItemSlot(toItem, fromIndex);
						affectedFromItems.Add(toItem);
					}
					// the slot we want to move the item to is empty
					else
					{
						// remove the item from the old container
						from.SetItemSlot(null, fromIndex);
						deletedFromSlots.Add(fromItem.Slot);
					}
					// put the item in the new container
					to.SetItemSlot(fromItem, toIndex);
					affectedToItems.Add(fromItem);
				}
				catch (Exception ex)
				{
					// Rollback: restore both containers to their original state.
					from.SetItemSlot(originalFromItem, fromIndex);
					if (hadToItem)
					{
						to.SetItemSlot(originalToItem, toIndex);
					}
					affectedFromItems.Clear();
					deletedFromSlots.Clear();
					affectedToItems.Clear();
					Log.Error("CharacterInventorySystem", $"Cross-container swap failed, rolled back: {ex}");
					return false;
				}
				return true;
			}
			return false;
		}

		/// <summary>
		/// Handles broadcast to remove an item from the player's inventory, updates the database and notifies the client.
		/// </summary>
		/// <param name="conn">Network connection of the client.</param>
		/// <param name="msg">InventoryRemoveItemBroadcast message.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		private void OnServerInventoryRemoveItemBroadcastReceived(NetworkConnection conn, InventoryRemoveItemBroadcast msg, Channel channel)
		{
			if (conn == null ||
				conn.FirstObject == null)
			{
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.InventoryRemove, out long guardKey))
			{
				// Refused by the ingress debounce, or an identical request is still in flight.
				// The client is holding a pending lock on this slot; with no reply it holds it forever.
				SendItemOperationFailed(conn, ItemOperationType.InventoryRemove, ItemOperationFailureReason.Throttled, InventoryType.Inventory, msg.Slot, -1);
				return;
			}

			// Set at the point where this handler acknowledges success. Everything else — every
			// validation return above and below, and any return a later edit adds — falls through
			// to the failure notification in the finally block.
			bool succeeded = false;
			ItemOperationFailureReason failureReason = ItemOperationFailureReason.Rejected;

			try
			{

				IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();
				if (character != null &&
					CharacterStateValidation.CanAct(character) &&
					character.TryGet(out IInventoryController inventoryController))
				{
					// Validate slot bounds before processing
					if (!inventoryController.IsValidSlot(msg.Slot))
					{
						return;
					}

					Item item = inventoryController.RemoveItem(msg.Slot);
					if (item == null)
					{
						return;
					}

					long characterID = character.ID;
					int slot = msg.Slot;
					item.Version++;

					ItemWriteBatch batch = BeginItemBatch(characterID, "InventoryRemove");
					batch.AddInventoryDelete(slot, item.Version);
					if (EnqueueItemBatch(batch))
					{
						Server.NetworkWrapper.Broadcast(conn, msg, true, Channel.Reliable);
						succeeded = true;
					}
					else
					{
						SendServerBusy(conn);
						failureReason = ItemOperationFailureReason.ServerBusy;
					}
				}
			}
			finally
			{
				EndIngressGuard(guardKey);

				// One notification per refused request, from the one place every exit path passes
				// through. A per-return-site call is a list the next edit forgets to extend.
				if (!succeeded)
				{
					SendItemOperationFailed(conn, ItemOperationType.InventoryRemove, failureReason, InventoryType.Inventory, msg.Slot, -1);
				}
			}
		}

		/// <summary>
		/// Handles broadcast to swap item slots in the player's inventory or bank, updates the database and notifies the client.
		/// </summary>
		/// <param name="conn">Network connection of the client.</param>
		/// <param name="msg">InventorySwapItemSlotsBroadcast message.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		private void OnServerInventorySwapItemSlotsBroadcastReceived(NetworkConnection conn, InventorySwapItemSlotsBroadcast msg, Channel channel)
		{
			if (conn == null ||
				conn.FirstObject == null)
			{
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.InventorySwap, out long guardKey))
			{
				// Refused by the ingress debounce, or an identical request is still in flight.
				// The client is holding a pending lock on this slot; with no reply it holds it forever.
				SendItemOperationFailed(conn, ItemOperationType.InventorySwap, ItemOperationFailureReason.Throttled, msg.FromInventory, msg.From, msg.To);
				return;
			}

			// Set at the point where this handler acknowledges success. Everything else — every
			// validation return above and below, and any return a later edit adds — falls through
			// to the failure notification in the finally block.
			bool succeeded = false;
			ItemOperationFailureReason failureReason = ItemOperationFailureReason.Rejected;

			try
			{

				IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();
				if (character == null ||
					!CharacterStateValidation.CanAct(character) ||
					!character.TryGet(out IInventoryController inventoryController))
				{
					return;
				}

				long characterID = character.ID;

				switch (msg.FromInventory)
				{
					case InventoryType.Inventory:
						// Validate slot bounds before processing
						if (!inventoryController.IsValidSlot(msg.From) || !inventoryController.IsValidSlot(msg.To))
						{
							return;
						}
						// swap the items in the inventory
						if (msg.To != msg.From &&
							SwapContainerItems(inventoryController, msg.From, msg.To, out List<Item> invAffected, out int invVacated))
						{
							ItemWriteBatch batch = BeginItemBatch(characterID, "InventorySwap");
							batch.AddInventoryWrites(BuildInventoryItemDataList(characterID, invAffected));
							// A move onto an empty slot writes the item at its new index and would
							// otherwise leave the old row behind — the same item in two slots.
							if (invVacated >= 0)
							{
								batch.AddInventoryDelete(invVacated, long.MaxValue);
							}
							if (EnqueueItemBatch(batch))
							{
								// tell the client we succeeded
								Server.NetworkWrapper.Broadcast(conn, msg, true, Channel.Reliable);
								succeeded = true;
							}
							else
							{
								SendServerBusy(conn);
								failureReason = ItemOperationFailureReason.ServerBusy;
							}
						}
						break;
					case InventoryType.Equipment:
						break;
					case InventoryType.Bank:
						{
							if (!character.TryGet(out IBankController bankController))
							{
								return;
							}

							// Validate banker scene object
							if (!ValidateBankerSceneObject(bankController.LastInteractableID, character))
							{
								return;
							}

							// Validate slot bounds before processing
							if (!bankController.IsValidSlot(msg.From) || !inventoryController.IsValidSlot(msg.To))
							{
								return;
							}

							if (SwapContainerItems(bankController, inventoryController, msg.From, msg.To,
								out List<Item> fromItems, out List<long> deletedSlots, out List<Item> toItems))
							{
								// ONE batch for the whole withdraw: the bank rows that stayed behind, the bank
								// slots that were vacated, and the inventory rows that received the item.
								ItemWriteBatch batch = BeginItemBatch(characterID, "BankWithdraw");
								if (fromItems != null && fromItems.Count > 0)
								{
									batch.AddBankWrites(BuildBankItemDataList(characterID, fromItems));
								}
								if (deletedSlots != null)
								{
									foreach (long slot in deletedSlots)
									{
										// A vacated slot has no item whose version could authorise the delete, so
										// long.MaxValue reads as "unconditional". Since the CRIT-2 fix that hard
										// deletes the row rather than leaving a poisoned tombstone behind.
										batch.AddBankDelete((int)slot, long.MaxValue);
									}
								}
								if (toItems != null && toItems.Count > 0)
								{
									batch.AddInventoryWrites(BuildInventoryItemDataList(characterID, toItems));
								}

								if (EnqueueItemBatch(batch))
								{
									// Tell the client
									Server.NetworkWrapper.Broadcast(conn, msg, true, Channel.Reliable);
									succeeded = true;
								}
								else
								{
									SendServerBusy(conn);
									failureReason = ItemOperationFailureReason.ServerBusy;
								}
							}
						}
						break;
					default: break;
				}
			}
			finally
			{
				EndIngressGuard(guardKey);

				// One notification per refused request, from the one place every exit path passes
				// through. A per-return-site call is a list the next edit forgets to extend.
				if (!succeeded)
				{
					SendItemOperationFailed(conn, ItemOperationType.InventorySwap, failureReason, msg.FromInventory, msg.From, msg.To);
				}
			}
		}

		/// <summary>
		/// Handles broadcast to equip an item from inventory or bank, updates the database and notifies the client.
		/// </summary>
		/// <param name="conn">Network connection of the client.</param>
		/// <param name="msg">EquipmentEquipItemBroadcast message.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		private void OnServerEquipmentEquipItemBroadcastReceived(NetworkConnection conn, EquipmentEquipItemBroadcast msg, Channel channel)
		{
			if (conn == null ||
				conn.FirstObject == null)
			{
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.EquipmentEquip, out long guardKey))
			{
				// Refused by the ingress debounce, or an identical request is still in flight.
				// The client is holding a pending lock on this slot; with no reply it holds it forever.
				SendItemOperationFailed(conn, ItemOperationType.EquipmentEquip, ItemOperationFailureReason.Throttled, msg.FromInventory, msg.InventoryIndex, msg.Slot);
				return;
			}

			// Set at the point where this handler acknowledges success. Everything else — every
			// validation return above and below, and any return a later edit adds — falls through
			// to the failure notification in the finally block.
			bool succeeded = false;
			ItemOperationFailureReason failureReason = ItemOperationFailureReason.Rejected;

			try
			{

				IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();
				if (character == null ||
					!CharacterStateValidation.CanAct(character) ||
					!character.TryGet(out IEquipmentController equipmentController))
				{
					return;
				}

				// Validate that the target equipment slot is a defined enum value.
				if (!Enum.IsDefined(typeof(ItemSlot), (byte)msg.Slot))
				{
					return;
				}

				long characterID = character.ID;

				switch (msg.FromInventory)
				{
					case InventoryType.Inventory:
						if (character.TryGet(out IInventoryController inventoryController) &&
							inventoryController.IsValidSlot(msg.InventoryIndex) &&
							inventoryController.TryGetItem(msg.InventoryIndex, out Item inventoryItem))
						{
							if (!equipmentController.Equip(inventoryItem, msg.InventoryIndex, inventoryController, (ItemSlot)msg.Slot))
							{
								return;
							}

							// ONE batch: the inventory slot the item left, the equipment slot it arrived
							// in, and the attribute rows the equip changed. Equipping is a single operation
							// from the player's point of view and half of it landing — the item equipped
							// with none of its stats, or the stats without the item — is a state nothing
							// else in the server knows how to repair.
							ItemWriteBatch batch = BeginItemBatch(characterID, "EquipFromInventory");

							// did we replace an already equipped item?
							if (inventoryController.TryGetItem(msg.InventoryIndex, out Item prevItem))
							{
								batch.AddInventoryWrite(BuildInventoryItemData(characterID, prevItem));
							}
							// the inventory slot the item came from is now empty
							else
							{
								batch.AddInventoryDelete(msg.InventoryIndex, long.MaxValue);
							}

							batch.AddEquipmentWrite(BuildEquipmentItemData(characterID, inventoryItem));
							batch.AddAttributeWrites(BuildAttributeDataList(character));

							if (EnqueueItemBatch(batch))
							{
								Server.NetworkWrapper.Broadcast(conn, msg, true, Channel.Reliable);
								succeeded = true;
							}
							else
							{
								SendServerBusy(conn);
								failureReason = ItemOperationFailureReason.ServerBusy;
							}
						}
						break;
					case InventoryType.Equipment:
						return;
					case InventoryType.Bank:
						{
							if (!character.TryGet(out IBankController bankController))
							{
								return;
							}

							// validate banker scene object
							if (!ValidateBankerSceneObject(bankController.LastInteractableID, character))
							{
								return;
							}

							// Validate slot bounds before processing
							if (!bankController.IsValidSlot(msg.InventoryIndex))
							{
								return;
							}

							if (bankController.TryGetItem(msg.InventoryIndex, out Item bankItem))
							{
								if (!equipmentController.Equip(bankItem, msg.InventoryIndex, bankController, (ItemSlot)msg.Slot))
								{
									return;
								}

								// ONE batch: the bank slot the item left, the equipment slot it arrived in,
								// and the attribute rows the equip changed.
								ItemWriteBatch batch = BeginItemBatch(characterID, "EquipFromBank");

								// did we replace an already equipped item?
								if (bankController.TryGetItem(msg.InventoryIndex, out Item prevItem))
								{
									batch.AddBankWrite(BuildBankItemData(characterID, prevItem));
								}
								// the bank slot the item came from is now empty
								else
								{
									batch.AddBankDelete(msg.InventoryIndex, long.MaxValue);
								}

								batch.AddEquipmentWrite(BuildEquipmentItemData(characterID, bankItem));
								batch.AddAttributeWrites(BuildAttributeDataList(character));

								if (EnqueueItemBatch(batch))
								{
									Server.NetworkWrapper.Broadcast(conn, msg, true, Channel.Reliable);
									succeeded = true;
								}
								else
								{
									SendServerBusy(conn);
									failureReason = ItemOperationFailureReason.ServerBusy;
								}
							}
						}
						break;
					default: return;
				}
			}
			finally
			{
				EndIngressGuard(guardKey);

				// One notification per refused request, from the one place every exit path passes
				// through. A per-return-site call is a list the next edit forgets to extend.
				if (!succeeded)
				{
					SendItemOperationFailed(conn, ItemOperationType.EquipmentEquip, failureReason, msg.FromInventory, msg.InventoryIndex, msg.Slot);
				}
			}
		}

		/// <summary>
		/// Handles broadcast to unequip an item to inventory or bank, updates the database and notifies the client.
		/// </summary>
		/// <param name="conn">Network connection of the client.</param>
		/// <param name="msg">EquipmentUnequipItemBroadcast message.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		private void OnServerEquipmentUnequipItemBroadcastReceived(NetworkConnection conn, EquipmentUnequipItemBroadcast msg, Channel channel)
		{
			if (conn == null ||
				conn.FirstObject == null)
			{
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.EquipmentUnequip, out long guardKey))
			{
				// Refused by the ingress debounce, or an identical request is still in flight.
				// The client is holding a pending lock on this slot; with no reply it holds it forever.
				SendItemOperationFailed(conn, ItemOperationType.EquipmentUnequip, ItemOperationFailureReason.Throttled, msg.ToInventory, msg.Slot, -1);
				return;
			}

			// Set at the point where this handler acknowledges success. Everything else — every
			// validation return above and below, and any return a later edit adds — falls through
			// to the failure notification in the finally block.
			bool succeeded = false;
			ItemOperationFailureReason failureReason = ItemOperationFailureReason.Rejected;

			try
			{

				IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();
				if (character == null ||
					!CharacterStateValidation.CanAct(character) ||
					!character.TryGet(out IEquipmentController equipmentController))
				{
					return;
				}

				// Validate that the equipment slot is a defined enum value.
				if (!Enum.IsDefined(typeof(ItemSlot), (byte)msg.Slot))
				{
					return;
				}

				long characterID = character.ID;

				switch (msg.ToInventory)
				{
					case InventoryType.Inventory:
						if (character.TryGet(out IInventoryController inventoryController) &&
							equipmentController.TryGetItem(msg.Slot, out Item toInventory))
						{
							// save the old slot index so we can delete the item
							int oldSlot = toInventory.Slot;

							// if we found the item we should unequip it
							if (!equipmentController.Unequip(inventoryController, msg.Slot, out List<Item> modifiedItems))
							{
								return;
							}

							// see if we have successfully added the item
							if (modifiedItems == null ||
								modifiedItems.Count < 1)
							{
								return;
							}

							// ONE batch: the inventory rows that received the item, the equipment slot it
							// left, and the stat change unequipping caused. Persisting the arrival without
							// the departure duplicates the item on next login; the reverse destroys it.
							ItemWriteBatch batch = BeginItemBatch(characterID, "UnequipToInventory");
							batch.AddInventoryWrites(BuildInventoryItemDataList(characterID, modifiedItems));
							batch.AddEquipmentDelete(oldSlot, long.MaxValue);
							batch.AddAttributeWrites(BuildAttributeDataList(character));

							if (EnqueueItemBatch(batch))
							{
								Server.NetworkWrapper.Broadcast(conn, msg, true, Channel.Reliable);
								succeeded = true;
							}
							else
							{
								SendServerBusy(conn);
								failureReason = ItemOperationFailureReason.ServerBusy;
							}
						}
						break;
					case InventoryType.Equipment:
						break;
					case InventoryType.Bank:
						{
							if (!character.TryGet(out IBankController bankController))
							{
								return;
							}

							// validate banker scene object
							if (!ValidateBankerSceneObject(bankController.LastInteractableID, character))
							{
								return;
							}

							if (equipmentController.TryGetItem(msg.Slot, out Item toBank))
							{
								int oldSlot = toBank.Slot;

								if (!equipmentController.Unequip(bankController, msg.Slot, out List<Item> modifiedItems))
								{
									return;
								}

								// see if we have successfully added the item
								if (modifiedItems == null ||
									modifiedItems.Count < 1)
								{
									return;
								}

								// ONE batch: the bank rows that received the item, the equipment slot it
								// left, and the stat change unequipping caused.
								ItemWriteBatch batch = BeginItemBatch(characterID, "UnequipToBank");
								batch.AddBankWrites(BuildBankItemDataList(characterID, modifiedItems));
								batch.AddEquipmentDelete(oldSlot, long.MaxValue);
								batch.AddAttributeWrites(BuildAttributeDataList(character));

								if (EnqueueItemBatch(batch))
								{
									Server.NetworkWrapper.Broadcast(conn, msg, true, Channel.Reliable);
									succeeded = true;
								}
								else
								{
									SendServerBusy(conn);
									failureReason = ItemOperationFailureReason.ServerBusy;
								}
							}
						}
						break;
					default: return;
				}
			}
			finally
			{
				EndIngressGuard(guardKey);

				// One notification per refused request, from the one place every exit path passes
				// through. A per-return-site call is a list the next edit forgets to extend.
				if (!succeeded)
				{
					SendItemOperationFailed(conn, ItemOperationType.EquipmentUnequip, failureReason, msg.ToInventory, msg.Slot, -1);
				}
			}
		}

		/// <summary>
		/// Handles broadcast to remove an item from the player's bank, updates the database and notifies the client.
		/// </summary>
		/// <param name="conn">Network connection of the client.</param>
		/// <param name="msg">BankRemoveItemBroadcast message.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		private void OnServerBankRemoveItemBroadcastReceived(NetworkConnection conn, BankRemoveItemBroadcast msg, Channel channel)
		{
			if (conn == null ||
				conn.FirstObject == null)
			{
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.BankRemove, out long guardKey))
			{
				// Refused by the ingress debounce, or an identical request is still in flight.
				// The client is holding a pending lock on this slot; with no reply it holds it forever.
				SendItemOperationFailed(conn, ItemOperationType.BankRemove, ItemOperationFailureReason.Throttled, InventoryType.Bank, msg.Slot, -1);
				return;
			}

			// Set at the point where this handler acknowledges success. Everything else — every
			// validation return above and below, and any return a later edit adds — falls through
			// to the failure notification in the finally block.
			bool succeeded = false;
			ItemOperationFailureReason failureReason = ItemOperationFailureReason.Rejected;

			try
			{

				IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();
				if (character != null &&
					CharacterStateValidation.CanAct(character) &&
					character.TryGet(out IBankController bankController))
				{
					// validate banker scene object before allowing bank removal
					if (!ValidateBankerSceneObject(bankController.LastInteractableID, character))
					{
						return;
					}

					// Validate slot bounds before processing
					if (!bankController.IsValidSlot(msg.Slot))
					{
						return;
					}

					Item item = bankController.RemoveItem(msg.Slot);
					if (item == null)
					{
						return;
					}

					long characterID = character.ID;
					int slot = msg.Slot;
					item.Version++;

					ItemWriteBatch batch = BeginItemBatch(characterID, "BankRemove");
					batch.AddBankDelete(slot, item.Version);
					if (EnqueueItemBatch(batch))
					{
						Server.NetworkWrapper.Broadcast(conn, msg, true, Channel.Reliable);
						succeeded = true;
					}
					else
					{
						SendServerBusy(conn);
						failureReason = ItemOperationFailureReason.ServerBusy;
					}
				}
			}
			finally
			{
				EndIngressGuard(guardKey);

				// One notification per refused request, from the one place every exit path passes
				// through. A per-return-site call is a list the next edit forgets to extend.
				if (!succeeded)
				{
					SendItemOperationFailed(conn, ItemOperationType.BankRemove, failureReason, InventoryType.Bank, msg.Slot, -1);
				}
			}
		}

		/// <summary>
		/// Handles broadcast to swap item slots in the player's bank, updates the database and notifies the client.
		/// </summary>
		/// <param name="conn">Network connection of the client.</param>
		/// <param name="msg">BankSwapItemSlotsBroadcast message.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		private void OnServerBankSwapItemSlotsBroadcastReceived(NetworkConnection conn, BankSwapItemSlotsBroadcast msg, Channel channel)
		{
			if (conn == null ||
				conn.FirstObject == null)
			{
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.BankSwap, out long guardKey))
			{
				// Refused by the ingress debounce, or an identical request is still in flight.
				// The client is holding a pending lock on this slot; with no reply it holds it forever.
				SendItemOperationFailed(conn, ItemOperationType.BankSwap, ItemOperationFailureReason.Throttled, msg.FromInventory, msg.From, msg.To);
				return;
			}

			// Set at the point where this handler acknowledges success. Everything else — every
			// validation return above and below, and any return a later edit adds — falls through
			// to the failure notification in the finally block.
			bool succeeded = false;
			ItemOperationFailureReason failureReason = ItemOperationFailureReason.Rejected;

			try
			{

				IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();
				if (character == null ||
					!CharacterStateValidation.CanAct(character) ||
					!character.TryGet(out IBankController bankController))
				{
					return;
				}

				// validate banker scene object
				if (!ValidateBankerSceneObject(bankController.LastInteractableID, character))
				{
					return;
				}

				long characterID = character.ID;

				switch (msg.FromInventory)
				{
					case InventoryType.Inventory:
						if (character.TryGet(out IInventoryController inventoryController) &&
							inventoryController.IsValidSlot(msg.From) &&
							bankController.IsValidSlot(msg.To) &&
							SwapContainerItems(inventoryController, bankController, msg.From, msg.To,
								out List<Item> fromItems, out List<long> deletedSlots, out List<Item> toItems))
						{
							// ONE batch for the whole deposit: the inventory rows that stayed behind, the
							// inventory slots that were vacated, and the bank rows that received the item.
							// These used to be three independently enqueued transactions, so a crash between
							// any two of them destroyed the item (deleted here, never written there) or
							// duplicated it (written there, never deleted here).
							ItemWriteBatch batch = BeginItemBatch(characterID, "BankDeposit");
							if (fromItems != null && fromItems.Count > 0)
							{
								batch.AddInventoryWrites(BuildInventoryItemDataList(characterID, fromItems));
							}
							if (deletedSlots != null)
							{
								foreach (long slot in deletedSlots)
								{
									// A vacated slot has no item whose version could authorise the delete, so
									// long.MaxValue reads as "unconditional". Since the CRIT-2 fix that hard
									// deletes the row rather than leaving a poisoned tombstone behind.
									batch.AddInventoryDelete((int)slot, long.MaxValue);
								}
							}
							if (toItems != null && toItems.Count > 0)
							{
								batch.AddBankWrites(BuildBankItemDataList(characterID, toItems));
							}

							if (EnqueueItemBatch(batch))
							{
								// tell the client
								Server.NetworkWrapper.Broadcast(conn, msg, true, Channel.Reliable);
								succeeded = true;
							}
							else
							{
								SendServerBusy(conn);
								failureReason = ItemOperationFailureReason.ServerBusy;
							}
						}
						break;
					case InventoryType.Equipment:
						break;
					case InventoryType.Bank:
						// Validate slot bounds before processing
						if (!bankController.IsValidSlot(msg.From) || !bankController.IsValidSlot(msg.To))
						{
							return;
						}
						// swap the items in the bank
						if (msg.To != msg.From &&
							SwapContainerItems(bankController, msg.From, msg.To, out List<Item> bankAffected, out int bankVacated))
						{
							ItemWriteBatch batch = BeginItemBatch(characterID, "BankSwap");
							batch.AddBankWrites(BuildBankItemDataList(characterID, bankAffected));
							// Same reasoning as the inventory swap above.
							if (bankVacated >= 0)
							{
								batch.AddBankDelete(bankVacated, long.MaxValue);
							}
							if (EnqueueItemBatch(batch))
							{
								// tell the client we succeeded
								Server.NetworkWrapper.Broadcast(conn, msg, true, Channel.Reliable);
								succeeded = true;
							}
							else
							{
								SendServerBusy(conn);
								failureReason = ItemOperationFailureReason.ServerBusy;
							}
						}
						break;
					default: break;
				}
			}
			finally
			{
				EndIngressGuard(guardKey);

				// One notification per refused request, from the one place every exit path passes
				// through. A per-return-site call is a list the next edit forgets to extend.
				if (!succeeded)
				{
					SendItemOperationFailed(conn, ItemOperationType.BankSwap, failureReason, msg.FromInventory, msg.From, msg.To);
				}
			}
		}

		/// <summary>
		/// Validates that the banker scene object is present, in the correct scene, and the character is in range.
		/// </summary>
		/// <param name="sceneObjectID">ID of the scene object to validate.</param>
		/// <param name="character">Player character interacting with the banker.</param>
		/// <returns>True if validation succeeds, false otherwise.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private bool ValidateBankerSceneObject(long sceneObjectID, IPlayerCharacter character)
		{
			if (!SceneObject.Objects.TryGetValue(sceneObjectID, out ISceneObject sceneObject))
			{
				Log.Debug("CharacterInventorySystem", $"Missing SceneObject ID:{sceneObjectID}");
				return false;
			}
			if (sceneObject.GameObject.scene.handle != character.GameObject.scene.handle)
			{
				Log.Debug("CharacterInventorySystem", "Object scene mismatch.");
				return false;
			}
			IInteractable interactable = sceneObject.GameObject.GetComponent<IInteractable>();
			if (interactable == null ||
				!interactable.InRange(character.Transform))
			{
				Log.Debug("CharacterInventorySystem", $"{character.CharacterName} is not in range of {sceneObject.GameObject.name}!");
				return false;
			}
			Banker banker = interactable as Banker;
			if (banker == null)
			{
				Log.Debug("CharacterInventorySystem", $"{sceneObject.GameObject.name} is not a banker!");
				return false;
			}
			return true;
		}

		#region DTO Builders

		/// <summary>
		/// Builds a CharacterInventoryData DTO from a live Item instance.
		/// Increments item.Version for sequence-based optimistic concurrency.
		/// Must be called on the main thread.
		/// </summary>
		/// <param name="characterID">Owning character identifier.</param>
		/// <param name="item">Runtime item instance to serialize.</param>
		/// <returns>Inventory DTO ready for persistence.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private CharacterInventoryData BuildInventoryItemData(long characterID, Item item)
		{
			item.Version++;
			return new CharacterInventoryData(
				id: item.ID,
				version: item.Version,
				characterID: characterID,
				templateID: item.Template.ID,
				slot: item.Slot,
				seed: item.IsGenerated ? item.Generator.Seed : 0,
				amount: item.IsStackable ? item.Stackable.Amount : 1
			);
		}

		/// <summary>
		/// Builds a list of CharacterInventoryData DTOs from live Item instances.
		/// Increments each item.Version for sequence-based optimistic concurrency.
		/// Must be called on the main thread.
		/// </summary>
		/// <param name="characterID">Owning character identifier.</param>
		/// <param name="items">Runtime item instances to serialize.</param>
		/// <returns>List of inventory DTOs ready for persistence.</returns>
		private List<CharacterInventoryData> BuildInventoryItemDataList(long characterID, List<Item> items)
		{
			var dtos = new List<CharacterInventoryData>(items.Count);
			foreach (Item item in items)
			{
				if (item == null) continue;
				dtos.Add(BuildInventoryItemData(characterID, item));
			}
			return dtos;
		}

		/// <summary>
		/// Builds a CharacterBankData DTO from a live Item instance.
		/// Increments item.Version for sequence-based optimistic concurrency.
		/// Must be called on the main thread.
		/// </summary>
		/// <param name="characterID">Owning character identifier.</param>
		/// <param name="item">Runtime item instance to serialize.</param>
		/// <returns>Bank DTO ready for persistence.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private CharacterBankData BuildBankItemData(long characterID, Item item)
		{
			item.Version++;
			return new CharacterBankData(
				id: item.ID,
				version: item.Version,
				characterID: characterID,
				templateID: item.Template.ID,
				slot: item.Slot,
				seed: item.IsGenerated ? item.Generator.Seed : 0,
				amount: item.IsStackable ? item.Stackable.Amount : 1
			);
		}

		/// <summary>
		/// Builds a list of CharacterBankData DTOs from live Item instances.
		/// Increments each item.Version for sequence-based optimistic concurrency.
		/// Must be called on the main thread.
		/// </summary>
		/// <param name="characterID">Owning character identifier.</param>
		/// <param name="items">Runtime item instances to serialize.</param>
		/// <returns>List of bank DTOs ready for persistence.</returns>
		private List<CharacterBankData> BuildBankItemDataList(long characterID, List<Item> items)
		{
			var dtos = new List<CharacterBankData>(items.Count);
			foreach (Item item in items)
			{
				if (item == null) continue;
				dtos.Add(BuildBankItemData(characterID, item));
			}
			return dtos;
		}

		/// <summary>
		/// Builds a CharacterEquipmentData DTO from a live Item instance.
		/// Increments item.Version for sequence-based optimistic concurrency.
		/// Must be called on the main thread.
		/// </summary>
		/// <param name="characterID">Owning character identifier.</param>
		/// <param name="item">Runtime item instance to serialize.</param>
		/// <returns>Equipment DTO ready for persistence.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private CharacterEquipmentData BuildEquipmentItemData(long characterID, Item item)
		{
			item.Version++;
			return new CharacterEquipmentData(
				id: item.ID,
				version: item.Version,
				characterID: characterID,
				templateID: item.Template.ID,
				slot: item.Slot,
				seed: item.IsGenerated ? item.Generator.Seed : 0,
				amount: item.IsStackable ? item.Stackable.Amount : 1
			);
		}

		/// <summary>
		/// Builds a list of CharacterAttributeData DTOs from the character's attribute controllers.
		/// Increments each attribute's Version for sequence-based optimistic concurrency.
		/// Must be called on the main thread.
		/// </summary>
		/// <param name="character">Character providing attribute controller data.</param>
		/// <returns>List of attribute DTOs ready for persistence.</returns>
		private List<CharacterAttributeData> BuildAttributeDataList(IPlayerCharacter character)
		{
			var dtos = new List<CharacterAttributeData>();
			if (!character.TryGet(out ICharacterAttributeController attributeController))
			{
				return dtos;
			}

			foreach (var kvp in attributeController.Attributes)
			{
				kvp.Value.Version++;
				dtos.Add(new CharacterAttributeData(
					id: 0,
					version: kvp.Value.Version,
					characterID: character.ID,
					templateID: kvp.Key,
					value: kvp.Value.Value,
					currentValue: 0.0f
				));
			}
			foreach (var kvp in attributeController.ResourceAttributes)
			{
				kvp.Value.Version++;
				dtos.Add(new CharacterAttributeData(
					id: 0,
					version: kvp.Value.Version,
					characterID: character.ID,
					templateID: kvp.Key,
					value: kvp.Value.Value,
					currentValue: kvp.Value.CurrentValue
				));
			}
			return dtos;
		}

		#endregion

	}
}