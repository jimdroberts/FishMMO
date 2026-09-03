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
	[RequiresDataContainer(typeof(CharacterInventorySystemMainThreadQueueData))]
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
			/// <summary>
			/// Split part of a stack into an inventory slot.
			/// </summary>
			InventorySplit = 7,
			/// <summary>
			/// Split part of a stack into a bank slot.
			/// </summary>
			BankSplit = 8,
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
			if (!registry.TryGet<ICharacterItemService>(out _) ||
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
			Server.NetworkWrapper.RegisterBroadcast<InventorySplitItemBroadcast>(OnServerInventorySplitItemBroadcastReceived, true);

			/* No equipment broadcasts. Equip and unequip are replicate INPUT now — applied by the
			 * EquipmentController inside the owner's replicate tick on both peers — and this system
			 * hears about them through IEquipmentController.OnServerEquipmentChanged, subscribed per
			 * character below. See EquipmentBroadcasts.cs for why the acknowledgements had to go. */

			// Bank broadcasts
			Server.NetworkWrapper.RegisterBroadcast<BankRemoveItemBroadcast>(OnServerBankRemoveItemBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<BankSwapItemSlotsBroadcast>(OnServerBankSwapItemSlotsBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<BankSplitItemBroadcast>(OnServerBankSplitItemBroadcastReceived, true);

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
				characterSystem.OnSpawnCharacter += CharacterSystem_OnSpawnCharacter;
				characterSystem.OnDespawnCharacter += CharacterSystem_OnDespawnCharacter;
			}

			/* The gates and hooks shared code needs from the server. The equipment controller asks
			 * the validator before applying a replicated request; the ECA give/remove actions hand
			 * their changes to the hooks so they are persisted and the owner is told. */
			EquipmentController.ServerRequestValidator = ValidateEquipmentRequest;
			ServerItemHooks.GrantInventoryItem = GrantInventoryItemFromAction;
			ServerItemHooks.InventoryChanged = InventoryChangedByAction;

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
			Server.NetworkWrapper.UnregisterBroadcast<InventorySplitItemBroadcast>(OnServerInventorySplitItemBroadcastReceived);

			// Bank broadcasts
			Server.NetworkWrapper.UnregisterBroadcast<BankRemoveItemBroadcast>(OnServerBankRemoveItemBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<BankSwapItemSlotsBroadcast>(OnServerBankSwapItemSlotsBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<BankSplitItemBroadcast>(OnServerBankSplitItemBroadcastReceived);

			if (Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, UnityEngine.SceneManagement.Scene> characterSystem))
			{
				characterSystem.OnSpawnCharacter -= CharacterSystem_OnSpawnCharacter;
				characterSystem.OnDespawnCharacter -= CharacterSystem_OnDespawnCharacter;
			}

			if (EquipmentController.ServerRequestValidator == ValidateEquipmentRequest)
			{
				EquipmentController.ServerRequestValidator = null;
			}
			if (ServerItemHooks.GrantInventoryItem == GrantInventoryItemFromAction)
			{
				ServerItemHooks.GrantInventoryItem = null;
			}
			if (ServerItemHooks.InventoryChanged == InventoryChangedByAction)
			{
				ServerItemHooks.InventoryChanged = null;
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

			// Database-assigned item identities, applied to the live Item objects. Same reason: the
			// write happens on a worker and every container here is main-thread only.
			DrainMainThreadQueue<ICharacterInventorySystemMainThreadQueueData>(maxIdentityWriteBacksPerFrame, drainAll: false);
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
			/* One counter for the journal rather than one per character.
			 *
			 * Per character, it had to be reset by ForgetCharacter to bound memory, and that reset
			 * raced the very flush that triggered it: ForgetCharacter runs when the despawn flush is
			 * CAPTURED, while the flush claims its sequence later on the worker thread. The claim
			 * therefore landed after the forget and put the watermark back -- so the next session
			 * started issuing sequence 1 against a watermark left by the previous one, and every
			 * write it made was discarded as superseded by a session that had already ended.
			 *
			 * Observed live: an equip logged 'skipped superseded ... Seq=1' followed by 'skipped
			 * superseded ItemSnapshot ... Seq=2', and the database kept none of it while the client
			 * showed the item equipped.
			 *
			 * Sequences only ever need to be comparable within one character, so making them global
			 * costs nothing and removes the reset entirely. ForgetCharacter still drops everything it
			 * held for the character; there is simply no longer a counter that can go backwards. */
			private long nextSequence;
			private readonly Dictionary<long, long> lastAppliedAny = new Dictionary<long, long>();
			private readonly Dictionary<long, long> lastAppliedSnapshot = new Dictionary<long, long>();
			private readonly Dictionary<long, CharacterSessionInfo> lastKnownLease = new Dictionary<long, CharacterSessionInfo>();
			private readonly HashSet<long> reconcileRequests = new HashSet<long>();

			/// <summary>Consecutive reconcile requests per character since its last successful snapshot.</summary>
			private readonly Dictionary<long, int> reconcileFailures = new Dictionary<long, int>();

			/// <summary>Earliest time a character's next repair snapshot may be captured.</summary>
			private readonly Dictionary<long, DateTime> reconcileNotBeforeUtc = new Dictionary<long, DateTime>();

			/// <summary>Longest wait between repair attempts for one character.</summary>
			private static readonly TimeSpan MaxReconcileBackoff = TimeSpan.FromSeconds(30);

			/// <summary>
			/// Allocates the next capture sequence number. Main thread only, which is what makes the
			/// numbers a faithful record of the order the mutations happened in.
			/// </summary>
			/// <remarks>
			/// Journal-wide and never reset, so a number is issued at most once for the life of the
			/// process. Only comparisons within one character are meaningful, so the gaps another
			/// character's writes leave in the run carry no meaning and are not a problem.
			/// </summary>
			public long NextSequence(long characterID)
			{
				lock (gate)
				{
					return ++nextSequence;
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
			/// <remarks>
			/// Every request is a failure report, and repeated failures back off. Without this a
			/// database that stayed down, or a row the snapshot could never write, produced a fresh
			/// full snapshot EVERY FRAME: the failure requested a reconcile, the drain captured one
			/// next frame, it failed, and round again — a write storm against the thing that was
			/// already failing. The wait doubles from one second to thirty and resets on the first
			/// snapshot that lands.
			/// </remarks>
			public void RequestReconcile(long characterID)
			{
				lock (gate)
				{
					reconcileRequests.Add(characterID);

					reconcileFailures.TryGetValue(characterID, out int failures);
					failures = Math.Min(failures + 1, 16);
					reconcileFailures[characterID] = failures;

					double seconds = Math.Min(MaxReconcileBackoff.TotalSeconds, Math.Pow(2, failures - 1));
					reconcileNotBeforeUtc[characterID] = DateTime.UtcNow.AddSeconds(seconds);
				}
			}

			/// <summary>
			/// Records that a snapshot for the character committed, ending its backoff.
			/// </summary>
			public void NoteSnapshotSucceeded(long characterID)
			{
				lock (gate)
				{
					reconcileFailures.Remove(characterID);
					reconcileNotBeforeUtc.Remove(characterID);
				}
			}

			/// <summary>
			/// Takes the repair requests that are due. Requests still backing off stay queued.
			/// </summary>
			public List<long> DrainReconcileRequests()
			{
				lock (gate)
				{
					if (reconcileRequests.Count == 0)
					{
						return null;
					}

					DateTime now = DateTime.UtcNow;
					List<long> drained = null;
					foreach (long characterID in reconcileRequests)
					{
						if (reconcileNotBeforeUtc.TryGetValue(characterID, out DateTime notBefore) && now < notBefore)
						{
							continue;
						}
						(drained ??= new List<long>()).Add(characterID);
					}
					if (drained != null)
					{
						foreach (long characterID in drained)
						{
							reconcileRequests.Remove(characterID);
						}
					}
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
					lastAppliedAny.Remove(characterID);
					lastAppliedSnapshot.Remove(characterID);
					lastKnownLease.Remove(characterID);
					reconcileRequests.Remove(characterID);
					reconcileFailures.Remove(characterID);
					reconcileNotBeforeUtc.Remove(characterID);
				}
			}

			/// <summary>
			/// Clears all state.
			/// </summary>
			public void Clear()
			{
				lock (gate)
				{
					lastAppliedAny.Clear();
					lastAppliedSnapshot.Clear();
					lastKnownLease.Clear();
					reconcileRequests.Clear();
					reconcileFailures.Clear();
					reconcileNotBeforeUtc.Clear();
				}
			}
		}

		/// <summary>
		/// One item to remove from the database, with the version that authorises it.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Addressed by the ITEM, not by the slot it was sitting in. That is the change the single
		/// <c>character_item</c> table bought: a row is an item, so a delete names the thing that
		/// ceased to exist rather than the hole it left.
		/// </para>
		/// <para>
		/// <b>Most of the deletes this replaced were not deletions at all.</b> Under the per-slot
		/// schema, moving an item — between two inventory slots, into an equipment socket, out to
		/// the bank — vacated a row that had to be deleted separately, because the destination was a
		/// different row (often in a different table). Those call sites are gone: a move is now an
		/// UPDATE of the item's own row, carried by the ordinary write that states its new container
		/// and slot. What remains here is only the genuine case, an item that was destroyed.
		/// </para>
		/// </remarks>
		private readonly struct ItemDelete
		{
			/// <summary>Identity of the item being removed.</summary>
			public readonly long ItemID;

			/// <summary>Version the delete is authorised against.</summary>
			public readonly long Version;

			/// <summary>Initializes an item removal.</summary>
			public ItemDelete(long itemID, long version)
			{
				ItemID = itemID;
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

			/// <summary>Item rows to upsert; for a snapshot, the whole of every container in <see cref="SnapshotContainers"/>.</summary>
			public List<CharacterItemData> ItemWrites;

			/// <summary>Items that ceased to exist. Unused by snapshots, which prune instead.</summary>
			public List<ItemDelete> ItemDeletes;

			/// <summary>
			/// For a snapshot, the containers it speaks for. Null for an incremental batch.
			/// </summary>
			/// <remarks>
			/// A snapshot prunes, so it has to say what it read. A character missing one of its three
			/// controllers must not have that container emptied on the strength of a list nobody
			/// built — which is exactly what "an empty list means empty container" would do.
			/// </remarks>
			public List<ItemContainerType> SnapshotContainers;

			/// <summary>Attribute rows to upsert. Equipping changes stats, and the stat write belongs to the same operation.</summary>
			public List<CharacterAttributeData> AttributeWrites;

			/// <summary>
			/// Identities the database minted for items that had never been written, filled in by
			/// the worker and applied back on the main thread.
			/// </summary>
			/// <remarks>
			/// Written once by the worker after a successful commit and read once by the main thread
			/// after that, with the queue hand-off between them — so no lock is needed, and the
			/// batch is still immutable as far as anything else is concerned.
			/// </remarks>
			public IReadOnlyList<CharacterItemIdAssignment> AssignedIdentities;

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
					? (SnapshotContainers == null || SnapshotContainers.Count == 0) &&
						(AttributeWrites == null || AttributeWrites.Count == 0)
					: (ItemWrites == null || ItemWrites.Count == 0) &&
						(ItemDeletes == null || ItemDeletes.Count == 0) &&
						(AttributeWrites == null || AttributeWrites.Count == 0);

			/// <summary>Adds item rows to upsert.</summary>
			public void AddItemWrites(List<CharacterItemData> dtos)
			{
				if (dtos == null || dtos.Count == 0) return;
				(ItemWrites ??= new List<CharacterItemData>(dtos.Count)).AddRange(dtos);
			}

			/// <summary>Adds one item row to upsert.</summary>
			public void AddItemWrite(CharacterItemData dto)
			{
				(ItemWrites ??= new List<CharacterItemData>(1)).Add(dto);
			}

			/// <summary>
			/// Records that an item no longer exists.
			/// </summary>
			/// <remarks>
			/// An item that was never persisted has no row to remove, and asking to delete identity
			/// zero would be a request the service refuses anyway. Dropping it here keeps the batch
			/// free of statements that cannot succeed.
			/// </remarks>
			public void AddItemDelete(long itemID, long version)
			{
				if (itemID <= 0) return;
				(ItemDeletes ??= new List<ItemDelete>(1)).Add(new ItemDelete(itemID, version));
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
		/// <summary>
		/// How many queued identity write-backs are applied per frame.
		/// </summary>
		/// <remarks>
		/// One action per batch that created items, and a batch creates a handful at most, so this
		/// is a back-pressure ceiling rather than a rate that is ever reached. It exists because
		/// the drain runs on the main thread and an unbounded drain would let a burst of item
		/// grants stall a frame.
		/// </remarks>
		private const int maxIdentityWriteBacksPerFrame = 64;

		private ItemWriteJournal itemWriteJournal = new ItemWriteJournal();

		/// <summary>
		/// Stand-in for a snapshot whose containers are all empty.
		/// </summary>
		/// <remarks>
		/// "Every container I read holds nothing" is a real statement that must still prune, so the
		/// snapshot cannot be skipped just because it has no rows — it needs a list, and an empty
		/// one allocated per call would be pure waste.
		/// </remarks>
		private static readonly List<CharacterItemData> EmptyItemWrites = new List<CharacterItemData>();

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
		private ItemWriteBatch BeginItemBatch(long characterID, string operation, bool isSnapshot = false, CharacterSessionLeaseData? explicitLease = null)
		{
			return new ItemWriteBatch()
			{
				CharacterID = characterID,
				Lease = explicitLease ?? ResolveSessionLease(characterID),
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

		/// <inheritdoc />
		public bool TryGrantItem(IPlayerCharacter character, Item item, InventoryType container)
		{
			if (character == null || item == null || item.Template == null)
			{
				return false;
			}

			IItemContainer target = ResolveContainer(character, container.ToContainerType());
			if (target == null ||
				!target.TryAddItem(item, out List<Item> modifiedItems) ||
				modifiedItems == null ||
				modifiedItems.Count == 0)
			{
				return false;
			}

			BroadcastSlots(character, container, modifiedItems);
			TryPersistGrantedItems(character, modifiedItems, container, "ItemGrant");
			return true;
		}

		/// <summary>
		/// Sends the owner the current contents of the slots these items occupy.
		/// </summary>
		/// <remarks>
		/// The same set-item message every other path uses, so the client has one way of learning
		/// what a slot holds. An item that has not been persisted yet goes out with id 0, and the
		/// client treats that as "waiting"; the identity write-back re-sends the slot with the
		/// assigned id.
		/// </remarks>
		private void BroadcastSlots(IPlayerCharacter character, InventoryType container, IReadOnlyList<Item> items)
		{
			if (character.Owner == null || items == null || items.Count == 0)
			{
				return;
			}

			switch (container)
			{
				case InventoryType.Inventory:
				{
					var updates = new List<InventorySetItemBroadcast>(items.Count);
					for (int i = 0; i < items.Count; ++i)
					{
						Item item = items[i];
						if (item == null || item.Template == null || item.Slot < 0)
						{
							continue;
						}
						updates.Add(new InventorySetItemBroadcast()
						{
							InstanceID = item.ID,
							TemplateID = item.Template.ID,
							Slot = item.Slot,
							Seed = item.IsGenerated ? item.Generator.Seed : 0,
							StackSize = item.IsStackable ? item.Stackable.Amount : 0,
						});
					}
					if (updates.Count > 0)
					{
						Server.NetworkWrapper.Broadcast(character.Owner, new InventorySetMultipleItemsBroadcast()
						{
							Items = updates.ToArray(),
						}, true, Channel.Reliable);
					}
					break;
				}
				case InventoryType.Bank:
				{
					var updates = new List<BankSetItemBroadcast>(items.Count);
					for (int i = 0; i < items.Count; ++i)
					{
						Item item = items[i];
						if (item == null || item.Template == null || item.Slot < 0)
						{
							continue;
						}
						updates.Add(new BankSetItemBroadcast()
						{
							InstanceID = item.ID,
							TemplateID = item.Template.ID,
							Slot = item.Slot,
							Seed = item.IsGenerated ? item.Generator.Seed : 0,
							StackSize = item.IsStackable ? item.Stackable.Amount : 0,
						});
					}
					if (updates.Count > 0)
					{
						Server.NetworkWrapper.Broadcast(character.Owner, new BankSetMultipleItemsBroadcast()
						{
							Items = updates.ToArray(),
						}, true, Channel.Reliable);
					}
					break;
				}
				default:
					// Equipment has no set-item message; the reconcile carries its sockets.
					break;
			}
		}

		/// <summary>Tells the owner that inventory slots emptied.</summary>
		private void BroadcastRemovedSlots(IPlayerCharacter character, IReadOnlyList<RemovedItemRecord> removed)
		{
			if (character.Owner == null || removed == null)
			{
				return;
			}
			for (int i = 0; i < removed.Count; ++i)
			{
				if (removed[i].Slot < 0)
				{
					continue;
				}
				Server.NetworkWrapper.Broadcast(character.Owner, new InventoryRemoveItemBroadcast()
				{
					Slot = removed[i].Slot,
				}, true, Channel.Reliable);
			}
		}

		/// <summary>Tells the owner that one slot of a container emptied.</summary>
		/// <remarks>
		/// <see cref="BroadcastRemovedSlots"/> speaks only for the inventory, because the ECA
		/// remove action that feeds it only ever touches the inventory. A merge can empty a bank
		/// slot too, so this one takes the container.
		/// </remarks>
		private void BroadcastEmptiedSlot(IPlayerCharacter character, InventoryType container, int slot)
		{
			if (character.Owner == null || slot < 0)
			{
				return;
			}

			switch (container)
			{
				case InventoryType.Inventory:
					Server.NetworkWrapper.Broadcast(character.Owner, new InventoryRemoveItemBroadcast()
					{
						Slot = slot,
					}, true, Channel.Reliable);
					break;
				case InventoryType.Bank:
					Server.NetworkWrapper.Broadcast(character.Owner, new BankRemoveItemBroadcast()
					{
						Slot = slot,
					}, true, Channel.Reliable);
					break;
				default:
					break;
			}
		}

		/// <inheritdoc />
		public bool TryPersistGrantedItems(IPlayerCharacter character, List<Item> modifiedItems, InventoryType container, string operation)
		{
			if (character == null || modifiedItems == null || modifiedItems.Count == 0)
			{
				// Nothing to write is a success: the grant changed no persisted state.
				return true;
			}

			/* Lock every slot whose item has no identity yet, until the write comes back with one.
			 * The lock is the existing per-slot mechanism consumables use: a locked slot cannot be
			 * swapped, removed, merged into, equipped from or consumed, so the item cannot be
			 * captured by a second batch under id 0 — which is what inserted a second row for one
			 * item — and the identity write-back can trust that the slot still holds the item it
			 * was written for. ApplyAssignedIdentities unlocks. */
			IItemContainer target = ResolveContainer(character, container.ToContainerType());
			if (target != null)
			{
				for (int i = 0; i < modifiedItems.Count; ++i)
				{
					Item item = modifiedItems[i];
					if (item != null && item.ID <= 0 && item.Slot >= 0)
					{
						target.LockSlot(item.Slot);
					}
				}
			}

			ItemWriteBatch batch = BeginItemBatch(character.ID, operation);
			batch.AddItemWrites(BuildItemDataList(character.ID, modifiedItems, container.ToContainerType()));
			return EnqueueItemBatch(batch);
		}

		/// <summary>The ECA give action's grant: the funnel, addressed to the inventory.</summary>
		private bool GrantInventoryItemFromAction(ICharacter character, Item item)
		{
			return character is IPlayerCharacter playerCharacter && TryGrantItem(playerCharacter, item, InventoryType.Inventory);
		}

		/// <summary>
		/// Persists and reports inventory changes made by an ECA action.
		/// </summary>
		private void InventoryChangedByAction(ICharacter character, IReadOnlyList<Item> changed, IReadOnlyList<RemovedItemRecord> removed)
		{
			if (character is not IPlayerCharacter playerCharacter)
			{
				return;
			}

			PersistInventoryChanges(playerCharacter, changed, removed);
			BroadcastSlots(playerCharacter, InventoryType.Inventory, changed);
			BroadcastRemovedSlots(playerCharacter, removed);
		}

		/// <inheritdoc />
		public void PersistInventoryChanges(IPlayerCharacter character, IReadOnlyList<Item> changed, IReadOnlyList<RemovedItemRecord> removed)
		{
			if (character == null)
			{
				return;
			}

			ItemWriteBatch batch = BeginItemBatch(character.ID, "InventoryChange");
			if (changed != null)
			{
				for (int i = 0; i < changed.Count; ++i)
				{
					if (changed[i] != null)
					{
						batch.AddItemWrite(BuildItemData(character.ID, changed[i], ItemContainerType.Inventory));
					}
				}
			}
			if (removed != null)
			{
				for (int i = 0; i < removed.Count; ++i)
				{
					batch.AddItemDelete(removed[i].ItemID, removed[i].Version);
				}
			}
			if (!EnqueueItemBatch(batch) && character.Owner != null)
			{
				SendServerBusy(character.Owner);
			}
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

				if (batch.IsSnapshot)
				{
					// A landed snapshot is the repair; whatever was backing off is repaired.
					itemWriteJournal.NoteSnapshotSucceeded(batch.CharacterID);
				}

				/* Only after the commit. An identity written back from a transaction that then
				 * rolled back would name a row that does not exist, and the next write would quote
				 * it as an existing item — an upsert that updates nothing and reports a stale
				 * version forever. */
				if (batch.AssignedIdentities != null && batch.AssignedIdentities.Count > 0)
				{
					IReadOnlyList<CharacterItemIdAssignment> assignments = batch.AssignedIdentities;
					long characterID = batch.CharacterID;
					if (!TryEnqueueMainThread<ICharacterInventorySystemMainThreadQueueData>(
							() => ApplyAssignedIdentities(characterID, assignments)))
					{
						// The queue is full. The items keep id 0, so the next write creates fresh
						// rows for them and the snapshot after that assigns identities again — churn,
						// not loss. Worth a line because sustained back-pressure here means the main
						// thread is stalling.
						await Log.Warning("CharacterInventorySystem",
							$"ApplyItemBatchAsync: could not queue {assignments.Count} item identity write-back(s) for character {characterID}; they will be reassigned on a later write.");
					}
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
		/// Writes database-assigned identities onto the live items. Main thread only.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Verified against the slot, not trusted.</b> The assignment names a container and a slot
		/// as they were when the batch was captured, and the player has had a round trip to move
		/// things since. Applying an id to whatever now occupies that slot would hand one item's
		/// identity to another — which is the exact failure the whole single-table change exists to
		/// remove. The item is only accepted when it still has no identity of its own and its
		/// template matches the row that was written.
		/// </para>
		/// <para>
		/// An item that fails those checks is left at id 0. It is not lost: the next write creates a
		/// row for it and offers an identity again, and the periodic snapshot does the same. The cost
		/// of declining is one more round trip; the cost of guessing is a mis-keyed item.
		/// </para>
		/// <para>
		/// <b>The owning client is told, too.</b> Its copy of a granted item still carries the
		/// InstanceID 0 and seed 0 the grant broadcast quoted, so until now its tooltip lied and a
		/// generated item displayed attributes rolled from the wrong seed until a relog rebuilt it
		/// from the row. Re-sending the slot with the identity the database issued is the same
		/// set-item message the grant used, so the client needs no new handling — SetItemSlot
		/// replaces the slot's item wholesale.
		/// </para>
		/// </remarks>
		/// <param name="characterID">The character the batch belonged to.</param>
		/// <param name="assignments">Container, slot and the identity the database issued.</param>
		private void ApplyAssignedIdentities(long characterID, IReadOnlyList<CharacterItemIdAssignment> assignments)
		{
			if (assignments == null || assignments.Count == 0)
			{
				return;
			}

			if (Server == null ||
				!Server.DataContainerRegistry.TryGet(out ICharacterMappingData<NetworkConnection> mappingData) ||
				!mappingData.CharactersByID.TryGetValue(characterID, out IPlayerCharacter character) ||
				character == null)
			{
				// The character left this scene server between the write and the drain. Its items
				// went with it, and the server that has it now will assign identities of its own.
				return;
			}

			List<InventorySetItemBroadcast> inventoryUpdates = null;
			List<BankSetItemBroadcast> bankUpdates = null;

			for (int i = 0; i < assignments.Count; ++i)
			{
				CharacterItemIdAssignment assignment = assignments[i];
				if (assignment.ID <= 0)
				{
					continue;
				}

				IItemContainer container = ResolveContainer(character, assignment.Container);
				if (container == null ||
					!container.TryGetItem(assignment.Slot, out Item item) ||
					item == null)
				{
					continue;
				}

				if (item.ID != 0)
				{
					// Already identified by an earlier write-back. The slot is no longer waiting.
					container.UnlockSlot(assignment.Slot);
					continue;
				}

				/* The template is the check the slot alone cannot make. The slot is locked while its
				 * item awaits an identity, so nothing should have moved in — but an id handed to the
				 * wrong item would let one row describe two items, and a comparison is cheaper than
				 * the investigation that follows. */
				if (item.Template == null || (assignment.TemplateID != 0 && item.Template.ID != assignment.TemplateID))
				{
					Log.Warning("CharacterInventorySystem",
						$"Identity {assignment.ID} was written for template {assignment.TemplateID} at {assignment.Container} slot {assignment.Slot}, " +
						$"but that slot now holds template {item.Template?.ID}; leaving it unassigned.");
					continue;
				}

				if (!item.AssignPersistentID(assignment.ID))
				{
					continue;
				}

				// The row exists; the item is usable.
				container.UnlockSlot(assignment.Slot);

				/* AssignPersistentID has just derived the real seed (when the item had none), so
				 * the values quoted here are the ones the item will be reloaded with. Equipment has
				 * no set-item broadcast, and should never need one: an item cannot be equipped
				 * without an identity, so a socket only ever holds identified items. */
				switch (assignment.Container)
				{
					case ItemContainerType.Inventory:
						(inventoryUpdates ??= new List<InventorySetItemBroadcast>(assignments.Count)).Add(new InventorySetItemBroadcast()
						{
							InstanceID = item.ID,
							TemplateID = item.Template.ID,
							Slot = item.Slot,
							Seed = item.IsGenerated ? item.Generator.Seed : 0,
							StackSize = item.IsStackable ? item.Stackable.Amount : 0,
						});
						break;
					case ItemContainerType.Bank:
						(bankUpdates ??= new List<BankSetItemBroadcast>(assignments.Count)).Add(new BankSetItemBroadcast()
						{
							InstanceID = item.ID,
							TemplateID = item.Template.ID,
							Slot = item.Slot,
							Seed = item.IsGenerated ? item.Generator.Seed : 0,
							StackSize = item.IsStackable ? item.Stackable.Amount : 0,
						});
						break;
					default:
						break;
				}
			}

			if (character.Owner == null)
			{
				// Resident but disconnected (logout in progress). The relog load path reads the row
				// directly, so there is nobody to tell and nothing left stale.
				return;
			}

			if (inventoryUpdates != null)
			{
				Server.NetworkWrapper.Broadcast(character.Owner, new InventorySetMultipleItemsBroadcast()
				{
					Items = inventoryUpdates.ToArray(),
				}, true, Channel.Reliable);
			}
			if (bankUpdates != null)
			{
				Server.NetworkWrapper.Broadcast(character.Owner, new BankSetMultipleItemsBroadcast()
				{
					Items = bankUpdates.ToArray(),
				}, true, Channel.Reliable);
			}
		}

		/// <summary>
		/// Resolves one of a character's containers by its persistence container type.
		/// </summary>
		private static IItemContainer ResolveContainer(IPlayerCharacter character, ItemContainerType container)
		{
			switch (container)
			{
				case ItemContainerType.Inventory:
					return character.TryGet(out IInventoryController inventory) ? inventory : null;
				case ItemContainerType.Bank:
					return character.TryGet(out IBankController bank) ? bank : null;
				case ItemContainerType.Equipment:
					return character.TryGet(out IEquipmentController equipment) ? equipment : null;
				default:
					return null;
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
			bool hasItemWork = batch.IsSnapshot
				? batch.SnapshotContainers != null && batch.SnapshotContainers.Count > 0
				: (batch.ItemDeletes != null && batch.ItemDeletes.Count > 0) ||
					(batch.ItemWrites != null && batch.ItemWrites.Count > 0);

			if (hasItemWork)
			{
				if (!registry.TryGet<ICharacterItemService>(out var itemService))
				{
					return DatabaseResult.Failure(DatabaseErrorCodes.InvalidConfiguration, "ICharacterItemService is not registered.");
				}

				if (batch.IsSnapshot)
				{
					/* The snapshot names the containers it read, so a character missing one of its
					 * controllers leaves that container's rows alone rather than having them pruned
					 * on the strength of a list nobody built. */
					DatabaseResult<IReadOnlyList<CharacterItemIdAssignment>> result =
						await itemService.SaveSnapshotAsync(
							batch.CharacterID,
							batch.SnapshotContainers,
							batch.ItemWrites ?? EmptyItemWrites);

					if (!result.IsSuccess)
					{
						return DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
					}

					/* Identities the database minted for items that had never been written. They are
					 * reported back to the main thread rather than applied here: this runs on a
					 * worker, and Item is not thread safe. Until the write-back lands the item's
					 * attribute-ledger key is still zero, which is why it is a queued follow-up
					 * rather than something the next snapshot can be relied on to repeat. */
					if (result.Data != null && result.Data.Count > 0)
					{
						batch.AssignedIdentities = result.Data;
					}
				}
				else
				{
					/* Deletes first. Nothing in the current handler set needs the ordering — an item
					 * that is destroyed is never also written — but it is the order that stays
					 * correct if one ever does, because a destroyed item's row must not outlive the
					 * statement that replaced whatever took its slot. */
					if (batch.ItemDeletes != null)
					{
						foreach (ItemDelete removal in batch.ItemDeletes)
						{
							DatabaseResult result = await itemService.DeleteItemAsync(batch.CharacterID, removal.ItemID, removal.Version);
							if (!result.IsSuccess) return result;
						}
					}

					if (batch.ItemWrites != null && batch.ItemWrites.Count > 0)
					{
						/* Identified rows go through the bulk upsert, as ONE statement. That is what
						 * makes a swap persistable at all: the (character, container, slot) index is
						 * checked per row, so writing the two halves of a swap one row at a time put
						 * the first row onto the slot the second still held and tripped it every time.
						 * The bulk path parks the rows it is about to update first, inside this same
						 * transaction, and then writes the final layout.
						 *
						 * Rows with no identity yet go one at a time, because each of those writes
						 * returns the identity the database assigned and a bulk upsert cannot report
						 * one per row. They cannot collide with a swap: an unidentified item's slot is
						 * locked until its identity lands, so nothing swaps it. */
						List<CharacterItemData> identified = null;
						List<CharacterItemData> unidentified = null;
						foreach (CharacterItemData dto in batch.ItemWrites)
						{
							if (dto.ID > 0)
							{
								(identified ??= new List<CharacterItemData>(batch.ItemWrites.Count)).Add(dto);
							}
							else
							{
								(unidentified ??= new List<CharacterItemData>(1)).Add(dto);
							}
						}

						if (identified != null)
						{
							DatabaseResult bulk = RequireCompleteWrite("item write", batch.CharacterID,
								await itemService.PersistAsync(identified));
							if (!bulk.IsSuccess) return bulk;
						}

						if (unidentified != null)
						{
							List<CharacterItemIdAssignment> assigned = null;
							foreach (CharacterItemData dto in unidentified)
							{
								DatabaseResult<long> result = await itemService.PersistAsync(dto);
								if (!result.IsSuccess)
								{
									return DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
								}
								if (result.Data > 0)
								{
									(assigned ??= new List<CharacterItemIdAssignment>(1))
										.Add(new CharacterItemIdAssignment(dto.Container, dto.Slot, result.Data, dto.TemplateID));
								}
							}
							if (assigned != null)
							{
								batch.AssignedIdentities = assigned;
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
				DatabaseResult attributeResult = RequireCompleteWrite("attribute write", batch.CharacterID,
					await attributeService.PersistAsync(batch.AttributeWrites));
				if (!attributeResult.IsSuccess) return attributeResult;
			}

			return DatabaseResult.Success();
		}

		/// <summary>
		/// Collapses a bulk write's outcome into a plain result, treating anything short of a
		/// complete write as a failure.
		/// </summary>
		/// <remarks>
		/// Item containers are addressed by slot, so a batch is a layout rather than a set of
		/// independent rows: "the sword is now in slot 5" only makes sense alongside "slot 3 is
		/// now empty". Half a layout duplicates an item or loses one, and neither is a state the
		/// rest of the server knows how to repair — so a short write has to surface as a failure
		/// and let the batch machinery deal with it, exactly as an error would.
		/// </remarks>
		/// <param name="operation">What was being written, for the message.</param>
		/// <param name="characterID">The owning character.</param>
		/// <param name="result">The write's outcome.</param>
		/// <returns>Success only when every supplied row was written.</returns>
		private static DatabaseResult RequireCompleteWrite(string operation, long characterID, DatabaseResult<BulkWriteResult> result)
		{
			if (!result.IsSuccess)
			{
				return DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
			}

			BulkWriteResult write = result.Data;
			if (!write.IsComplete)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.StaleState,
					$"Character {characterID} {operation} was incomplete: {write}. A partly written container " +
					"cannot be trusted, so the batch is failed rather than left half-applied.",
					isTransient: false);
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
			ItemWriteBatch batch = CaptureSnapshotBatch(character, explicitLease: null);
			if (batch != null)
			{
				EnqueueItemBatch(batch);
			}
		}

		/// <inheritdoc />
		public Func<Task> CaptureDespawnFlush(IPlayerCharacter character, CharacterSessionInfo? lease)
		{
			CharacterSessionLeaseData? explicitLease = null;
			if (lease.HasValue && character != null)
			{
				explicitLease = new CharacterSessionLeaseData(character.ID, lease.Value.ServerID, lease.Value.Token);
			}

			ItemWriteBatch batch = CaptureSnapshotBatch(character, explicitLease);
			if (batch == null)
			{
				return null;
			}
			return () => ApplyItemBatchAsync(batch);
		}

		/// <summary>
		/// Builds the one-transaction, all-containers snapshot batch for a character. Main thread only.
		/// </summary>
		private ItemWriteBatch CaptureSnapshotBatch(IPlayerCharacter character, CharacterSessionLeaseData? explicitLease)
		{
			if (character == null || character.ID <= 0)
			{
				return null;
			}

			long characterID = character.ID;

			// Every DTO list is built here, synchronously, on the main thread. The async work item
			// must never touch a live container: by the time it runs the player may have moved items,
			// logged out, or had the object pooled.
			// All three containers travel in ONE batch, under ONE sequence number and ONE
			// transaction. Three separate snapshots could interleave with an incremental
			// write between them and leave the three tables describing different moments.
			ItemWriteBatch batch = BeginItemBatch(characterID, "ItemSnapshot", isSnapshot: true, explicitLease: explicitLease);

			/* The containers this snapshot speaks for. A container whose controller is missing is
			 * NOT listed, so its rows are left alone rather than pruned on the strength of a list
			 * nobody built — the same distinction the three separate snapshot calls used to draw by
			 * leaving a null list. */
			var containers = new List<ItemContainerType>(3);
			List<CharacterItemData> rows = null;

			if (character.TryGet(out IInventoryController inventoryController))
			{
				containers.Add(ItemContainerType.Inventory);
				rows = BuildContainerSnapshot(characterID, inventoryController, ItemContainerType.Inventory, rows);
			}
			if (character.TryGet(out IBankController bankController))
			{
				containers.Add(ItemContainerType.Bank);
				rows = BuildContainerSnapshot(characterID, bankController, ItemContainerType.Bank, rows);
			}
			if (character.TryGet(out IEquipmentController equipmentController))
			{
				containers.Add(ItemContainerType.Equipment);
				rows = BuildContainerSnapshot(characterID, equipmentController, ItemContainerType.Equipment, rows);
			}

			/* One instance in two slots is a duplicated item in memory, and a snapshot that wrote
			 * both rows would fail on the primary key — every time, forever, with the backoff the
			 * only thing between that and a write storm. Keep the first, say so loudly, and let
			 * the row describe the one place the item can legitimately be. */
			if (rows != null && rows.Count > 1)
			{
				var seen = new HashSet<long>();
				for (int i = rows.Count - 1; i >= 0; --i)
				{
					long id = rows[i].ID;
					if (id <= 0)
					{
						continue;
					}
					if (!seen.Add(id))
					{
						Log.Error("CharacterInventorySystem",
							$"Character {characterID}: item {id} appears in more than one slot ({rows[i].Container} slot {rows[i].Slot} dropped from the snapshot). " +
							"The in-memory containers hold the same instance twice; this is an item duplication bug upstream of persistence.");
						rows.RemoveAt(i);
					}
				}
			}

			batch.SnapshotContainers = containers;
			batch.ItemWrites = rows;
			return batch;
		}

		/// <summary>
		/// Attaches this system to a character's item-changing controllers as it spawns.
		/// </summary>
		/// <remarks>
		/// Equip, unequip and consumable use are applied inside the character's own controllers —
		/// the first two inside the replicate tick — and this is how their persistence reaches the
		/// batch machinery. Per character rather than through a static hook so a pooled controller
		/// cannot keep reporting for its previous occupant.
		/// </remarks>
		private void CharacterSystem_OnSpawnCharacter(NetworkConnection conn, IPlayerCharacter character, UnityEngine.SceneManagement.Scene scene)
		{
			if (character == null)
			{
				return;
			}
			if (character.TryGet(out IEquipmentController equipment))
			{
				equipment.OnServerEquipmentChanged -= Equipment_OnServerEquipmentChanged;
				equipment.OnServerEquipmentChanged += Equipment_OnServerEquipmentChanged;
			}
			if (character.TryGet(out IAbilityController abilities))
			{
				abilities.OnConsumableItemChanged -= Ability_OnConsumableItemChanged;
				abilities.OnConsumableItemChanged += Ability_OnConsumableItemChanged;
			}
		}

		/// <summary>
		/// Forgets a character's write bookkeeping as it leaves this scene server.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The logout snapshot is NOT captured here any more. CharacterSystem captures it through
		/// <see cref="CaptureDespawnFlush"/> before it enqueues the save-and-release, and awaits it
		/// before the release — so it is ordered against the session hand-off rather than racing it
		/// on a different worker lane. An evicted character (one another server already owns) goes
		/// through this event too, and for that one there is deliberately nothing to write.
		/// </para>
		/// <para>
		/// This drops the character's watermarks but NOT the capture counter, which is journal-wide
		/// and never reset. That asymmetry is deliberate and load-bearing: the flush captured before
		/// this call claims its sequence later, on the worker thread, so its claim lands after this
		/// and writes a watermark back. A counter that restarted here would then issue sequence 1
		/// against that watermark and the whole of the next session would be discarded as
		/// superseded -- which is exactly what it did.
		/// </para>
		/// </remarks>
		private void CharacterSystem_OnDespawnCharacter(NetworkConnection conn, IPlayerCharacter character)
		{
			if (character == null)
			{
				return;
			}
			if (character.TryGet(out IEquipmentController equipment))
			{
				equipment.OnServerEquipmentChanged -= Equipment_OnServerEquipmentChanged;
			}
			if (character.TryGet(out IAbilityController abilities))
			{
				abilities.OnConsumableItemChanged -= Ability_OnConsumableItemChanged;
			}
			if (character.ID > 0)
			{
				itemWriteJournal.ForgetCharacter(character.ID);
			}
		}

		/// <summary>
		/// Builds the snapshot rows for one container. Main thread only.
		/// </summary>
		private List<CharacterItemData> BuildContainerSnapshot(long characterID, IItemContainer container, ItemContainerType containerType, List<CharacterItemData> into)
		{
			into ??= new List<CharacterItemData>(container.Items.Count);
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
				into.Add(BuildItemData(characterID, item, containerType));
			}
			return into;
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

		#region Stack Split And Merge

		/// <summary>
		/// Merges the stack in <paramref name="fromIndex"/> into a matching stack in
		/// <paramref name="toIndex"/>, when that is what a drop onto it should do. Issue #198.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Called by the swap handlers ahead of the swap. Returns false when the two slots are NOT
		/// a merge — the destination is empty, holds something else, or is full — and the caller
		/// then swaps as it always did. Returns true when the drop was a merge, whether or not it
		/// went through; <paramref name="succeeded"/> and <paramref name="failureReason"/> carry
		/// the outcome the handler reports from its <c>finally</c>.
		/// </para>
		/// <para>
		/// <b>Not echoed.</b> The client applies an echoed swap by exchanging two slots, which is
		/// the wrong thing for a merge, so the outcome goes out as a set-slot message for the
		/// receiving stack and either a set-slot or a remove for the donor. Those go out whether or
		/// not the batch was queued normally: the containers have already changed and the client
		/// has to be told what they now hold — a <c>ServerBusy</c> on top of them means only that
		/// the acknowledgement is uncertain, as it does everywhere else.
		/// </para>
		/// <para>
		/// Split and merge are a pair (see <see cref="ItemStackTransfer"/>): between them a player
		/// can move any quantity anywhere, and quantity a split strands is quantity a merge can
		/// recombine.
		/// </para>
		/// </remarks>
		private bool TryMergeStacks(NetworkConnection conn, IPlayerCharacter character,
			IItemContainer from, InventoryType fromType, int fromIndex,
			IItemContainer to, InventoryType toType, int toIndex,
			string operation, out bool succeeded, out ItemOperationFailureReason failureReason)
		{
			succeeded = false;
			failureReason = ItemOperationFailureReason.Rejected;

			if (from == null || to == null ||
				!from.TryGetItem(fromIndex, out Item donor) ||
				!to.TryGetItem(toIndex, out Item receiver) ||
				!ItemStackTransfer.CanMergeInto(receiver, donor))
			{
				// Not a merge; the caller swaps.
				return false;
			}

			if (!ItemStackTransfer.TryMerge(from, fromIndex, to, toIndex, out Item source, out Item destination, out bool sourceEmptied))
			{
				// A merge the pre-flight refused (a locked slot). Nothing changed, and the swap
				// would refuse for the same reason, so it is reported rather than retried.
				return true;
			}

			long characterID = character.ID;
			ItemWriteBatch batch = BeginItemBatch(characterID, operation);
			batch.AddItemWrite(BuildItemData(characterID, destination, toType.ToContainerType()));
			if (sourceEmptied)
			{
				// The donor ceased to exist: a real delete, addressed by the item.
				source.Version++;
				batch.AddItemDelete(source.ID, source.Version);
			}
			else
			{
				batch.AddItemWrite(BuildItemData(characterID, source, fromType.ToContainerType()));
			}
			bool enqueued = EnqueueItemBatch(batch);

			BroadcastSlots(character, toType, new List<Item>(1) { destination });
			if (sourceEmptied)
			{
				BroadcastEmptiedSlot(character, fromType, fromIndex);
			}
			else
			{
				BroadcastSlots(character, fromType, new List<Item>(1) { source });
			}

			if (enqueued)
			{
				succeeded = true;
			}
			else
			{
				SendServerBusy(conn);
				failureReason = ItemOperationFailureReason.ServerBusy;
			}
			return true;
		}

		/// <summary>
		/// Handles a request to split part of a stack into an inventory slot. Issue #198.
		/// </summary>
		private void OnServerInventorySplitItemBroadcastReceived(NetworkConnection conn, InventorySplitItemBroadcast msg, Channel channel)
		{
			HandleSplitRequest(conn, IngressOperation.InventorySplit, ItemOperationType.InventorySplit,
				msg.FromInventory, msg.From, InventoryType.Inventory, msg.To, msg.Amount);
		}

		/// <summary>
		/// Handles a request to split part of a stack into a bank slot. Issue #198.
		/// </summary>
		private void OnServerBankSplitItemBroadcastReceived(NetworkConnection conn, BankSplitItemBroadcast msg, Channel channel)
		{
			HandleSplitRequest(conn, IngressOperation.BankSplit, ItemOperationType.BankSplit,
				msg.FromInventory, msg.From, InventoryType.Bank, msg.To, msg.Amount);
		}

		/// <summary>
		/// Splits <paramref name="amount"/> off a stack into another slot, persists both halves as
		/// one transaction and tells the owner what the two slots now hold.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A new operation rather than a variation on swap: every existing item message moves or
		/// exchanges whole slots, and none changes an amount. The rules — at least one, less than
		/// the stack holds, onto an empty slot or a matching stack with room, both slots unlocked —
		/// live in <see cref="ItemStackTransfer.TrySplit"/>, which refuses before writing anything,
		/// so a refused split leaves the original stack untouched.
		/// </para>
		/// <para>
		/// <b>The split half has no identity until its row lands.</b> It is handled exactly as a
		/// granted item is: its slot is locked until the identity write-back arrives, the set-slot
		/// message goes out with id 0 so the client shows it as waiting, and
		/// <see cref="ApplyAssignedIdentities"/> unlocks the slot and re-sends it with the id the
		/// database issued. Both rows travel in one batch, so the database either shows the split
		/// or shows the stack as it was.
		/// </para>
		/// <para>
		/// The banker has to be in range whenever either end is a bank slot, as for a deposit or a
		/// withdrawal.
		/// </para>
		/// </remarks>
		private void HandleSplitRequest(NetworkConnection conn, IngressOperation ingress, ItemOperationType operation,
			InventoryType fromInventory, int from, InventoryType toInventory, int to, uint amount)
		{
			if (conn == null ||
				conn.FirstObject == null)
			{
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, ingress, out long guardKey))
			{
				// Refused by the ingress debounce, or an identical request is still in flight.
				// The client is holding a pending lock on both slots; with no reply it holds them forever.
				SendItemOperationFailed(conn, operation, ItemOperationFailureReason.Throttled, fromInventory, from, to);
				return;
			}

			// Set at the point where this handler acknowledges success. Everything else — every
			// validation return below, and any return a later edit adds — falls through to the
			// failure notification in the finally block.
			bool succeeded = false;
			ItemOperationFailureReason failureReason = ItemOperationFailureReason.Rejected;

			try
			{
				IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();
				if (character == null ||
					!CharacterStateValidation.CanAct(character))
				{
					return;
				}

				// Only the two grid containers hold stacks; an equipment socket never does.
				if (fromInventory != InventoryType.Inventory && fromInventory != InventoryType.Bank)
				{
					return;
				}

				IItemContainer source = ResolveContainer(character, fromInventory.ToContainerType());
				IItemContainer target = ResolveContainer(character, toInventory.ToContainerType());
				if (source == null || target == null)
				{
					return;
				}

				if ((fromInventory == InventoryType.Bank || toInventory == InventoryType.Bank) &&
					(!character.TryGet(out IBankController bankController) ||
					 !ValidateBankerSceneObject(bankController.LastInteractableID, character)))
				{
					return;
				}

				// Validate slot bounds before processing
				if (!source.IsValidSlot(from) || !target.IsValidSlot(to))
				{
					return;
				}

				if (!ItemStackTransfer.TrySplit(source, from, target, to, amount,
					out Item remainder, out Item taken, out bool created))
				{
					return;
				}

				long characterID = character.ID;

				if (created)
				{
					/* Locked until the identity write-back, as TryPersistGrantedItems does for a
					 * granted item: nothing may swap, merge into or split a stack the database has
					 * not written, or a second batch would capture it under id 0 and insert a
					 * second row for one item. ApplyAssignedIdentities unlocks. */
					target.LockSlot(to);
				}

				ItemWriteBatch batch = BeginItemBatch(characterID, "StackSplit");
				batch.AddItemWrite(BuildItemData(characterID, remainder, fromInventory.ToContainerType()));
				batch.AddItemWrite(BuildItemData(characterID, taken, toInventory.ToContainerType()));
				bool enqueued = EnqueueItemBatch(batch);

				// Set-slot messages for both halves, never an echo: the client has never seen the
				// split half and cannot construct it from two indices.
				BroadcastSlots(character, fromInventory, new List<Item>(1) { remainder });
				BroadcastSlots(character, toInventory, new List<Item>(1) { taken });

				if (enqueued)
				{
					succeeded = true;
				}
				else
				{
					SendServerBusy(conn);
					failureReason = ItemOperationFailureReason.ServerBusy;
				}
			}
			finally
			{
				EndIngressGuard(guardKey);

				// One notification per refused request, from the one place every exit path passes
				// through. A per-return-site call is a list the next edit forgets to extend.
				if (!succeeded)
				{
					SendItemOperationFailed(conn, operation, failureReason, fromInventory, from, to);
				}
			}
		}

		#endregion

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
					// A real destruction, so a real delete — addressed by the item, not the hole.
					batch.AddItemDelete(item.ID, item.Version);
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
						if (msg.To == msg.From)
						{
							return;
						}
						// A stack dropped onto a matching stack merges instead of swapping. Issue #198.
						if (TryMergeStacks(conn, character,
							inventoryController, InventoryType.Inventory, msg.From,
							inventoryController, InventoryType.Inventory, msg.To,
							"InventoryMerge", out succeeded, out failureReason))
						{
							break;
						}
						// swap the items in the inventory
						if (SwapContainerItems(inventoryController, msg.From, msg.To, out List<Item> invAffected, out int _))
						{
							ItemWriteBatch batch = BeginItemBatch(characterID, "InventorySwap");
							/* No delete for the vacated slot. A row is an item now, so writing the
							 * item at its new index UPDATES the row it already had — there is no old
							 * row to leave behind. The vacated-slot delete this replaces existed
							 * solely because the row was keyed by slot. */
							batch.AddItemWrites(BuildItemDataList(characterID, invAffected, ItemContainerType.Inventory));
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

							// A stack withdrawn onto a matching stack merges instead of swapping. Issue #198.
							if (TryMergeStacks(conn, character,
								bankController, InventoryType.Bank, msg.From,
								inventoryController, InventoryType.Inventory, msg.To,
								"BankWithdrawMerge", out succeeded, out failureReason))
							{
								break;
							}

							if (SwapContainerItems(bankController, inventoryController, msg.From, msg.To,
								out List<Item> fromItems, out List<long> _, out List<Item> toItems))
							{
								// ONE batch for the whole withdraw: the bank rows that stayed behind, the bank
								// slots that were vacated, and the inventory rows that received the item.
								ItemWriteBatch batch = BeginItemBatch(characterID, "BankWithdraw");
								if (fromItems != null && fromItems.Count > 0)
								{
									batch.AddItemWrites(BuildItemDataList(characterID, fromItems, ItemContainerType.Bank));
								}
								/* The vacated bank slots need no statement of their own. Every item
								 * that left one is in toItems and is written below with its new
								 * container and slot, which updates the row it already had. Under the
								 * per-slot schema the destination was a different row in a different
								 * table, which is the only reason a delete was ever needed here. */
								if (toItems != null && toItems.Count > 0)
								{
									batch.AddItemWrites(BuildItemDataList(characterID, toItems, ItemContainerType.Inventory));
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

		#region Equipment

		/// <summary>
		/// The server-side gate the equipment controller consults before applying a replicated
		/// request: may this character act, and may it reach the named container right now?
		/// </summary>
		/// <remarks>
		/// The bank half is what a broadcast handler used to check before every bank-sourced equip
		/// and bank-bound unequip. A replicated request has no handler, so the rule is installed
		/// into the controller instead. A refusal here leaves the socket unchanged, and the owner,
		/// which predicted the move, is corrected by the next reconcile.
		/// </remarks>
		private bool ValidateEquipmentRequest(ICharacter character, InventoryType container)
		{
			if (character is not IPlayerCharacter playerCharacter ||
				!CharacterStateValidation.CanAct(playerCharacter))
			{
				return false;
			}
			if (container == InventoryType.Bank)
			{
				return playerCharacter.TryGet(out IBankController bankController) &&
					ValidateBankerSceneObject(bankController.LastInteractableID, playerCharacter);
			}
			return true;
		}

		/// <summary>
		/// Persists an equip or unequip the server has just applied, and tells the owner where an
		/// unequipped item landed.
		/// </summary>
		/// <remarks>
		/// ONE batch: the socket, the container slot on the other side of the move, and the
		/// attribute rows the change altered. Equipping is a single operation from the player's
		/// point of view and half of it landing — the item equipped with none of its stats, or the
		/// stats without the item — is a state nothing else in the server knows how to repair.
		/// </remarks>
		private void Equipment_OnServerEquipmentChanged(IEquipmentController equipment, EquipmentChange change)
		{
			if (equipment?.Character is not IPlayerCharacter character || change.Item == null)
			{
				return;
			}

			long characterID = character.ID;
			ItemContainerType containerType = change.ContainerType.ToContainerType();

			if (change.Kind == EquipmentRequestKind.Equip)
			{
				ItemWriteBatch batch = BeginItemBatch(characterID, "Equip");
				if (change.DisplacedItem != null)
				{
					batch.AddItemWrite(BuildItemData(characterID, change.DisplacedItem, containerType));
				}
				batch.AddItemWrite(BuildItemData(characterID, change.Item, ItemContainerType.Equipment));
				batch.AddAttributeWrites(BuildAttributeDataList(character));
				if (!EnqueueItemBatch(batch) && character.Owner != null)
				{
					SendServerBusy(character.Owner);
				}
				return;
			}

			if (change.Kind == EquipmentRequestKind.Unequip)
			{
				ItemWriteBatch batch = BeginItemBatch(characterID, "Unequip");
				if (change.ModifiedItems != null && change.ModifiedItems.Count > 0)
				{
					batch.AddItemWrites(BuildItemDataList(characterID, change.ModifiedItems, containerType));
				}
				else
				{
					batch.AddItemWrite(BuildItemData(characterID, change.Item, containerType));
				}
				/* No equipment delete. The item that left the socket is written above with its new
				 * container, which moves its row. */
				batch.AddAttributeWrites(BuildAttributeDataList(character));
				bool enqueued = EnqueueItemBatch(batch);

				if (character.Owner != null)
				{
					/* The slot the item landed in. The owner chose one too, from its own copy of
					 * the container, and the two copies can differ by a grant that landed on one
					 * side first; the owner moves the item by identity if they do. */
					Server.NetworkWrapper.Broadcast(character.Owner, new EquipmentUnequipItemBroadcast()
					{
						ItemID = change.Item.ID,
						Slot = (byte)change.Socket,
						ToInventory = change.ContainerType,
						ToSlot = change.ContainerIndex,
					}, true, Channel.Reliable);

					if (!enqueued)
					{
						SendServerBusy(character.Owner);
					}
				}
			}
		}

		/// <summary>
		/// Persists a consumable's effect on the inventory row that held it.
		/// </summary>
		/// <remarks>
		/// Consumable use runs inside the replicate tick and used to be recorded only by the next
		/// full snapshot, so a crash refunded every potion drunk since. A destroyed item is a real
		/// delete, addressed by the item; a reduced stack is a row update.
		/// </remarks>
		private void Ability_OnConsumableItemChanged(ICharacter character, Item item, int slot, bool destroyed)
		{
			if (character is not IPlayerCharacter playerCharacter || item == null)
			{
				return;
			}

			ItemWriteBatch batch = BeginItemBatch(playerCharacter.ID, destroyed ? "ConsumableDestroyed" : "ConsumableUsed");
			if (destroyed)
			{
				item.Version++;
				batch.AddItemDelete(item.ID, item.Version);
			}
			else
			{
				batch.AddItemWrite(BuildItemData(playerCharacter.ID, item, ItemContainerType.Inventory));
			}
			EnqueueItemBatch(batch);
		}

		#endregion

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
					// A real destruction, so a real delete — addressed by the item, not the hole.
					batch.AddItemDelete(item.ID, item.Version);
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
						if (!character.TryGet(out IInventoryController inventoryController) ||
							!inventoryController.IsValidSlot(msg.From) ||
							!bankController.IsValidSlot(msg.To))
						{
							return;
						}
						// A stack deposited onto a matching stack merges instead of swapping. Issue #198.
						if (TryMergeStacks(conn, character,
							inventoryController, InventoryType.Inventory, msg.From,
							bankController, InventoryType.Bank, msg.To,
							"BankDepositMerge", out succeeded, out failureReason))
						{
							break;
						}
						if (SwapContainerItems(inventoryController, bankController, msg.From, msg.To,
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
								batch.AddItemWrites(BuildItemDataList(characterID, fromItems, ItemContainerType.Inventory));
							}
							if (deletedSlots != null)
							{
								foreach (long slot in deletedSlots)
								{
									// A vacated slot has no item whose version could authorise the delete, so
									// long.MaxValue reads as "unconditional". Since the CRIT-2 fix that hard
									// deletes the row rather than leaving a poisoned tombstone behind.
									/* No delete: the item that left this inventory slot is written
									 * below as a bank row, which moves its row. */
								}
							}
							if (toItems != null && toItems.Count > 0)
							{
								batch.AddItemWrites(BuildItemDataList(characterID, toItems, ItemContainerType.Bank));
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
						if (msg.To == msg.From)
						{
							return;
						}
						// A stack dropped onto a matching stack merges instead of swapping. Issue #198.
						if (TryMergeStacks(conn, character,
							bankController, InventoryType.Bank, msg.From,
							bankController, InventoryType.Bank, msg.To,
							"BankMerge", out succeeded, out failureReason))
						{
							break;
						}
						// swap the items in the bank
						if (SwapContainerItems(bankController, msg.From, msg.To, out List<Item> bankAffected, out int bankVacated))
						{
							ItemWriteBatch batch = BeginItemBatch(characterID, "BankSwap");
							batch.AddItemWrites(BuildItemDataList(characterID, bankAffected, ItemContainerType.Bank));
							// Same reasoning as the inventory swap above.
							if (bankVacated >= 0)
							{
								/* No delete for the vacated slot — the item is written above at its
								 * new index, which updates the row it already had. */
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
			/* A despawned interactable stays registered and stays in its scene — FishNet's pool
			 * just deactivates what it stores — so the ID keeps resolving after the object has
			 * gone. Without this the banker's stale ID authorised bank access from anywhere. */
			if (sceneObject.GameObject == null || !sceneObject.GameObject.activeInHierarchy)
			{
				Log.Debug("CharacterInventorySystem", $"SceneObject ID:{sceneObjectID} is despawned.");
				return false;
			}
			if (sceneObject.GameObject.scene.handle != character.GameObject.scene.handle)
			{
				Log.Debug("CharacterInventorySystem", "Object scene mismatch.");
				return false;
			}
			/* Resolved through the shared rule and gated on CanInteract. GetComponent returned
			 * whichever IInteractable the prefab's component order yielded — a banker NPC carries
			 * both a Banker and the NPC that is its own corpse — and InRange skips the corpse gate,
			 * so a killed banker still opened the vault. */
			IInteractable interactable = InteractableResolver.Resolve(sceneObject);
			IBanker banker = interactable as IBanker;
			if (banker == null)
			{
				Log.Debug("CharacterInventorySystem", $"{sceneObject.GameObject.name} is not a banker!");
				return false;
			}
			if (!interactable.CanInteract(character))
			{
				Log.Debug("CharacterInventorySystem", $"{character.CharacterName} cannot interact with {sceneObject.GameObject.name}!");
				return false;
			}
			return true;
		}

		#region DTO Builders

		/// <summary>
		/// Builds a persistence row from a live Item.
		/// Increments item.Version for sequence-based optimistic concurrency.
		/// Must be called on the main thread.
		/// </summary>
		/// <remarks>
		/// One builder for all three containers. There used to be three, differing only in the type
		/// they constructed — which is what three tables bought, and what one table gives back. The
		/// container is now a value the row carries rather than a choice of destination.
		/// </remarks>
		/// <param name="characterID">Owning character identifier.</param>
		/// <param name="item">Runtime item instance to serialize.</param>
		/// <param name="container">Which container the item is in.</param>
		/// <returns>Item row ready for persistence.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private CharacterItemData BuildItemData(long characterID, Item item, ItemContainerType container)
		{
			item.Version++;
			return new CharacterItemData(
				// Zero for an item the database has not written yet. The write returns the identity
				// it assigns and ApplyAssignedIdentities puts it back on this object.
				id: item.ID,
				version: item.Version,
				characterID: characterID,
				container: container,
				templateID: item.Template.ID,
				slot: item.Slot,
				seed: item.IsGenerated ? item.Generator.Seed : 0,
				amount: item.IsStackable ? item.Stackable.Amount : 1
			);
		}

		/// <summary>
		/// Builds persistence rows from live Items.
		/// Increments each item.Version for sequence-based optimistic concurrency.
		/// Must be called on the main thread.
		/// </summary>
		/// <param name="characterID">Owning character identifier.</param>
		/// <param name="items">Runtime item instances to serialize.</param>
		/// <param name="container">Which container the items are in.</param>
		/// <returns>List of item rows ready for persistence.</returns>
		private List<CharacterItemData> BuildItemDataList(long characterID, List<Item> items, ItemContainerType container)
		{
			var dtos = new List<CharacterItemData>(items.Count);
			foreach (Item item in items)
			{
				if (item == null) continue;
				dtos.Add(BuildItemData(characterID, item, container));
			}
			return dtos;
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

			/* Version++ AND MarkPersistPending, together — the pair is what makes the dirty flag
			 * work. This used to bump the version alone, which quietly broke the OTHER save path:
			 * CharacterSystem.AppendAttributeData records the version it stamped and clears the mark
			 * only when the confirmation quotes that same version back. A bump from here moved the
			 * attribute past the version an in-flight save was waiting on, so its MarkPersisted
			 * never matched, the attribute stayed dirty forever, and the periodic save wrote it on
			 * every pass from then on — the exact cost the flag exists to avoid.
			 *
			 * Unlike that path this one does NOT skip a clean attribute. Equipping changes stats,
			 * and the stat write belongs to the same transaction as the item write; a batch that
			 * silently dropped the attributes because they happened to be clean would commit half
			 * the operation. */
			foreach (var kvp in attributeController.Attributes)
			{
				kvp.Value.Version++;
				kvp.Value.MarkPersistPending(kvp.Value.Version);
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
				kvp.Value.MarkPersistPending(kvp.Value.Version);
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