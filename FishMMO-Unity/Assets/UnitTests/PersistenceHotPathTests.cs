using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Data.Enums;
using FishMMO.Database.Npgsql.Services;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Server.Implementation;
using FishMMO.Server.Implementation.World.SceneServer;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs for the character and item persistence fixes of the 2026-09-25 server hot-path audit
	/// (H2, H3, H4, M20–M23, L22, L24): the rules that decide what is written, when a claim may be
	/// handed back, and what a batch's outcome means.
	/// </summary>
	/// <remarks>
	/// The rules that could be written as pure functions are tested as such; the journal is driven
	/// through reflection, as <c>ItemWriteSequenceTests</c> drives it; and the orderings that only a
	/// running server exercises — a release after the writes it covers, a save gate released on every
	/// exit — are source pins, in the style of <c>AuditFollowUpPinsTests</c>.
	/// </remarks>
	[TestFixture]
	public class PersistenceHotPathTests
	{
		private const string SavingPath = "Assets/Scripts/Server/Implementation/World/SceneServer/Character/CharacterSystem.Saving.cs";
		private const string CharacterSystemPath = "Assets/Scripts/Server/Implementation/World/SceneServer/Character/CharacterSystem.cs";
		private const string ServerBehaviourPath = "Assets/Scripts/Server/Implementation/ServerBehaviour.cs";

		#region Item snapshot content (H3)

		private static CharacterItemData Row(long id, ItemContainerType container, int slot, int template = 7, int seed = 0, uint amount = 1, long version = 1)
		{
			return new CharacterItemData(id, version, 18, container, template, slot, seed, amount);
		}

		private static readonly List<ItemContainerType> AllThree = new List<ItemContainerType>
		{
			ItemContainerType.Inventory, ItemContainerType.Bank, ItemContainerType.Equipment,
		};

		[Test]
		public void ItemSnapshotContent_TheSameRowsAtNewerVersions_Match()
		{
			/* The snapshot bumps every version as it captures, so versions never repeat. They say
			 * nothing about what the row holds and must not make an unchanged character look changed. */
			var confirmed = new List<CharacterItemData> { Row(5, ItemContainerType.Inventory, 0, version: 3), Row(6, ItemContainerType.Bank, 2, version: 9) };
			var current = new List<CharacterItemData> { Row(5, ItemContainerType.Inventory, 0, version: 4), Row(6, ItemContainerType.Bank, 2, version: 10) };

			LogAssert.IsTrue(ItemSnapshotContent.Matches(AllThree, confirmed, AllThree, current),
				"rows that differ only in version describe the same database");
		}

		[Test]
		public void ItemSnapshotContent_AnyWrittenFieldDiffering_DoesNotMatch()
		{
			var confirmed = new List<CharacterItemData> { Row(5, ItemContainerType.Inventory, 0, template: 7, seed: 11, amount: 3) };
			var variants = new[]
			{
				Row(6, ItemContainerType.Inventory, 0, template: 7, seed: 11, amount: 3),
				Row(5, ItemContainerType.Bank, 0, template: 7, seed: 11, amount: 3),
				Row(5, ItemContainerType.Inventory, 1, template: 7, seed: 11, amount: 3),
				Row(5, ItemContainerType.Inventory, 0, template: 8, seed: 11, amount: 3),
				Row(5, ItemContainerType.Inventory, 0, template: 7, seed: 12, amount: 3),
				Row(5, ItemContainerType.Inventory, 0, template: 7, seed: 11, amount: 4),
			};

			foreach (CharacterItemData variant in variants)
			{
				LogAssert.IsFalse(ItemSnapshotContent.Matches(AllThree, confirmed, AllThree, new List<CharacterItemData> { variant }),
					$"a change to id/container/slot/template/seed/amount must be written ({variant.ID}, {variant.Container}, {variant.Slot}, {variant.TemplateID}, {variant.Seed}, {variant.Amount})");
			}
		}

		[Test]
		public void ItemSnapshotContent_AnItemWithNoIdentity_NeverMatches()
		{
			/* Only a write issues an identity, so a character holding an unidentified item must be
			 * written even when every other field matches. */
			var rows = new List<CharacterItemData> { Row(0, ItemContainerType.Inventory, 0) };
			LogAssert.IsFalse(ItemSnapshotContent.Matches(AllThree, rows, AllThree, rows),
				"an unidentified item is never 'unchanged'");
		}

		[Test]
		public void ItemSnapshotContent_ARowAddedOrRemoved_OrAContainerMissing_DoesNotMatch()
		{
			var one = new List<CharacterItemData> { Row(5, ItemContainerType.Inventory, 0) };
			var two = new List<CharacterItemData> { Row(5, ItemContainerType.Inventory, 0), Row(6, ItemContainerType.Inventory, 1) };
			var noBank = new List<ItemContainerType> { ItemContainerType.Inventory, ItemContainerType.Equipment };

			LogAssert.IsFalse(ItemSnapshotContent.Matches(AllThree, one, AllThree, two), "an added item must be written");
			LogAssert.IsFalse(ItemSnapshotContent.Matches(AllThree, two, AllThree, one), "a removed item must be written");
			LogAssert.IsFalse(ItemSnapshotContent.Matches(AllThree, one, noBank, one),
				"a snapshot that speaks for different containers prunes differently");
		}

		[Test]
		public void ItemSnapshotContent_TheRecord_CarriesTheIdentitiesTheWriteIssued()
		{
			/* Otherwise the next comparison finds id 0 in the record, the write-back's id on the item,
			 * and writes the whole character again for nothing. */
			var written = new List<CharacterItemData> { Row(0, ItemContainerType.Inventory, 3), Row(9, ItemContainerType.Bank, 1) };
			var issued = new List<CharacterItemIdAssignment> { new CharacterItemIdAssignment(ItemContainerType.Inventory, 3, 77, 7) };

			List<CharacterItemData> record = ItemSnapshotContent.WithIssuedIdentities(written, issued);

			LogAssert.AreEqual(77L, record[0].ID, "the unidentified row takes the identity issued for its container and slot");
			LogAssert.AreEqual(9L, record[1].ID, "a row that had an identity keeps it");
			LogAssert.AreEqual(0L, written[0].ID, "the rows as sent are not modified");
		}

		#endregion

		#region Snapshot turns (H3)

		[Test]
		public void SnapshotSlices_EveryCharacterHasExactlyOneTurnPerInterval()
		{
			int slices = CharacterInventorySystem.ItemSnapshotSliceCount(60f);
			LogAssert.AreEqual(60, slices, "one slice per second of the interval");
			LogAssert.AreEqual(1, CharacterInventorySystem.ItemSnapshotSliceCount(0.1f), "never fewer than one slice");

			var seen = new HashSet<int>();
			for (long id = 1; id <= 600; ++id)
			{
				int slice = CharacterInventorySystem.ItemSnapshotSliceOf(id, slices);
				LogAssert.IsTrue(slice >= 0 && slice < slices, $"character {id} landed in slice {slice}");
				LogAssert.AreEqual(slice, CharacterInventorySystem.ItemSnapshotSliceOf(id, slices), "a character's turn does not move");
				seen.Add(slice);
			}
			LogAssert.AreEqual(slices, seen.Count, "consecutive ids spread over every slice rather than bunching on one frame");

			int negative = CharacterInventorySystem.ItemSnapshotSliceOf(-5, slices);
			LogAssert.IsTrue(negative >= 0 && negative < slices, "a negative id still lands in range");
		}

		#endregion

		#region Item write journal (H3, M22, M23, L22)

		private const long CharacterId = 18;

		private object journal;
		private Type journalType;

		[SetUp]
		public void SetUp()
		{
			journalType = typeof(CharacterInventorySystem).GetNestedType("ItemWriteJournal", BindingFlags.NonPublic);
			LogAssert.IsNotNull(journalType, "the item write journal must still exist.");
			journal = Activator.CreateInstance(journalType, nonPublic: true);
		}

		private object Call(string name, params object[] args)
		{
			MethodInfo method = journalType.GetMethod(name, BindingFlags.Public | BindingFlags.Instance);
			LogAssert.IsNotNull(method, $"ItemWriteJournal.{name} must still exist.");
			return method.Invoke(journal, args);
		}

		private long Next() => (long)Call("NextSequence", CharacterId);

		private bool Claim(long sequence, bool isSnapshot, long characterID = CharacterId) =>
			(bool)Call("TryClaimSequence", characterID, sequence, isSnapshot);

		private void RecordConfirmed(long sequence)
		{
			Call("RecordSnapshotConfirmed", CharacterId, sequence,
				new List<ItemContainerType>(AllThree),
				new List<CharacterItemData> { Row(5, ItemContainerType.Inventory, 0) });
		}

		private bool HasConfirmed()
		{
			var args = new object[] { CharacterId, null };
			return (bool)journalType.GetMethod("TryGetConfirmedSnapshot").Invoke(journal, args);
		}

		[Test]
		public void AFailedDepartureFlush_CanClaimItsOwnSequenceAgain()
		{
			/* The flush claims before its commit. When the commit then fails the flush is run again
			 * before the claim is released, and refusing that rerun as "superseded by itself" made a
			 * failed flush unrepeatable. */
			long flush = Next();
			LogAssert.IsTrue(Claim(flush, true), "the first attempt claims");
			LogAssert.IsTrue(Claim(flush, true), "a rerun of the same snapshot may claim its own sequence again");
		}

		[Test]
		public void ASnapshotRerun_IsStillRefusedOnceALaterWriteHasLanded()
		{
			long snapshot = Next();
			Claim(snapshot, true);
			long later = Next();
			Claim(later, false);

			LogAssert.IsFalse(Claim(snapshot, true),
				"a rerun must not restate a layout that a later write has already changed");
		}

		[Test]
		public void TheConfirmedSnapshot_IsRetiredByAnyLaterClaim_AndNotRecordedOverOne()
		{
			long snapshot = Next();
			Claim(snapshot, true);
			RecordConfirmed(snapshot);
			LogAssert.IsTrue(HasConfirmed(), "a committed snapshot that is still newest is recorded");

			long incremental = Next();
			Claim(incremental, false);
			LogAssert.IsFalse(HasConfirmed(), "a later write may have changed the rows, so the record is retired");

			RecordConfirmed(snapshot);
			LogAssert.IsFalse(HasConfirmed(),
				"a snapshot whose commit is confirmed after a later claim must not publish a record over it");
		}

		[Test]
		public void TheConfirmedSnapshot_IsNeverRecordedForADepartedCharacter()
		{
			long flush = Next();
			Call("ForgetCharacter", CharacterId);
			Claim(flush, true);
			RecordConfirmed(flush);

			LogAssert.IsFalse(HasConfirmed(), "a departed character has no periodic turn to compare against");
		}

		[Test]
		public void OnlyOneSnapshotPerCharacter_IsEverQueued()
		{
			LogAssert.IsTrue((bool)Call("TryMarkSnapshotOutstanding", CharacterId, 10L), "the first snapshot is marked");
			LogAssert.IsFalse((bool)Call("TryMarkSnapshotOutstanding", CharacterId, 11L), "a second is refused while the first is queued");

			Call("ClearSnapshotOutstanding", CharacterId, 9L);
			LogAssert.IsTrue((bool)Call("IsSnapshotOutstanding", CharacterId), "a stale finish must not clear a newer mark");

			Call("ClearSnapshotOutstanding", CharacterId, 10L);
			LogAssert.IsFalse((bool)Call("IsSnapshotOutstanding", CharacterId), "the snapshot's own finish clears it");
		}

		[Test]
		public void TheRepairQueue_TakesOnlyWhatIsDue_AndNoMoreThanTheBudget()
		{
			for (long id = 1; id <= 5; ++id)
			{
				Call("DeferReconcile", id, TimeSpan.Zero);
			}
			Call("DeferReconcile", 6L, TimeSpan.FromHours(1));

			int taken = 0;
			for (int pass = 0; pass < 3; ++pass)
			{
				var drained = (List<long>)Call("DrainReconcileRequests", 2);
				LogAssert.IsNotNull(drained, $"pass {pass} has due requests");
				LogAssert.IsTrue(drained.Count <= 2, $"pass {pass} took {drained.Count}, over its budget of 2");
				taken += drained.Count;
			}

			LogAssert.AreEqual(5, taken, "every due request is taken, a budget's worth a frame");
			LogAssert.IsNull(Call("DrainReconcileRequests", 2), "the request that is not yet due stays queued");
			LogAssert.AreEqual(1, (int)journalType.GetProperty("PendingReconcileCount").GetValue(journal), "and is still there");

			Call("ForgetCharacter", 6L);
			LogAssert.AreEqual(0, (int)journalType.GetProperty("PendingReconcileCount").GetValue(journal),
				"a character that leaves takes its queued repair with it");
		}

		#endregion

		#region Outcome rules (M20, M22, H4)

		[Test]
		public void AnOwnershipRefusal_IsFinalOnlyWhenTheClaimIsSomeoneElses()
		{
			LogAssert.AreEqual(ItemWriteOutcome.NotOwned,
				CharacterInventorySystem.ClassifyOwnershipRefusal(DatabaseResult.Failure(DatabaseErrorCodes.Forbidden, "x")),
				"a claim held elsewhere must never be retried over");
			LogAssert.AreEqual(ItemWriteOutcome.Retry,
				CharacterInventorySystem.ClassifyOwnershipRefusal(DatabaseResult.Failure(DatabaseErrorCodes.DatabaseError, "x", isTransient: true)),
				"a transient failure of the check proved nothing and is worth another attempt");
			LogAssert.AreEqual(ItemWriteOutcome.Rejected,
				CharacterInventorySystem.ClassifyOwnershipRefusal(DatabaseResult.Failure(DatabaseErrorCodes.NotFound, "x")),
				"a missing character fails the same way next time");
		}

		[Test]
		public void AnItemBatchesAttributeRows_MayBeSuperseded_ButNotFiltered()
		{
			/* M20: the periodic attribute save is one unkeyed statement now, so a pass captured after
			 * an equip can land first. Its rows are newer; the equip must not roll back for that. */
			DatabaseResult superseded = CharacterInventorySystem.RequireAttemptedWrite("attribute write", CharacterId,
				DatabaseResult<BulkWriteResult>.Success(new BulkWriteResult(4, 4, 1)));
			LogAssert.IsTrue(superseded.IsSuccess, "rows lost to a newer version of the same attribute are written");

			DatabaseResult filtered = CharacterInventorySystem.RequireAttemptedWrite("attribute write", CharacterId,
				DatabaseResult<BulkWriteResult>.Success(new BulkWriteResult(4, 3, 3)));
			LogAssert.IsFalse(filtered.IsSuccess, "a row the service never attempted is a failure");

			DatabaseResult failed = CharacterInventorySystem.RequireAttemptedWrite("attribute write", CharacterId,
				DatabaseResult<BulkWriteResult>.Failure(DatabaseErrorCodes.DatabaseError, "x", isTransient: true));
			LogAssert.IsFalse(failed.IsSuccess, "an error is a failure");
			LogAssert.IsTrue(failed.IsTransient, "and keeps its transience");
		}

		private static CharacterPersistRequest Request(long version, bool owned)
		{
			var data = new CharacterData(
				id: 18, name: "n", nameLowercase: "n", account: "a", selected: false, worldServerID: 1,
				sceneName: "s", sceneHandle: 1, bindScene: "b", bindX: 0, bindY: 0, bindZ: 0,
				instanceID: 0, instanceX: 0, instanceY: 0, instanceZ: 0,
				instanceRotX: 0, instanceRotY: 0, instanceRotZ: 0, instanceRotW: 1,
				raceID: 1, modelIndex: 0, x: 0, y: 0, z: 0, rotX: 0, rotY: 0, rotZ: 0, rotW: 1,
				accessLevel: 1, online: true, flags: 0, version: version,
				timeCreated: DateTime.UtcNow, lastSaved: DateTime.UtcNow);
			return new CharacterPersistRequest(data, owned ? new CharacterSessionLeaseData(18, 7, Mine) : (CharacterSessionLeaseData?)null);
		}

		private static readonly Guid Mine = new Guid("11111111-1111-1111-1111-111111111111");
		private static readonly Guid Theirs = new Guid("22222222-2222-2222-2222-222222222222");

		[Test]
		public void ABatchedSaveRow_ThatWasNotWritten_IsClassifiedAsTheSingleRowSaveWouldReportIt()
		{
			CharacterSessionState online = CharacterSessionState.Online;

			LogAssert.AreEqual(CharacterPersistOutcome.NotFound,
				CharacterService.ClassifyUnwritten(Request(11, true), false, false, 0, online, 0, Guid.Empty), "missing");
			LogAssert.AreEqual(CharacterPersistOutcome.NotFound,
				CharacterService.ClassifyUnwritten(Request(11, true), true, true, 10, online, 7, Mine), "deleted");
			LogAssert.AreEqual(CharacterPersistOutcome.OwnershipLost,
				CharacterService.ClassifyUnwritten(Request(11, true), true, false, 10, online, 9, Theirs), "claimed by another server");
			LogAssert.AreEqual(CharacterPersistOutcome.OwnershipLost,
				CharacterService.ClassifyUnwritten(Request(11, true), true, false, 10, CharacterSessionState.Offline, 0, Guid.Empty), "released");
			LogAssert.AreEqual(CharacterPersistOutcome.OwnershipLost,
				CharacterService.ClassifyUnwritten(Request(11, true), true, false, 50, online, 9, Theirs),
				"a lost claim is reported ahead of the stale version the new owner's saves also cause");
			LogAssert.AreEqual(CharacterPersistOutcome.Stale,
				CharacterService.ClassifyUnwritten(Request(11, true), true, false, 50, online, 7, Mine), "newer stored");
			LogAssert.AreEqual(CharacterPersistOutcome.Replayed,
				CharacterService.ClassifyUnwritten(Request(11, true), true, false, 11, online, 7, Mine),
				"the same version under our own claim is our own write, replayed");
			LogAssert.AreEqual(CharacterPersistOutcome.Stale,
				CharacterService.ClassifyUnwritten(Request(11, false), true, false, 11, online, 7, Mine),
				"without a claim an equal version proves nothing about who wrote it");
		}

		#endregion

		#region Async worker admission (M23)

		[Test]
		public void RequiredWork_IsAdmittedPastTheCap_ButStillWaitsForASlot_InItsLane()
		{
			var worker = new AsyncWorkerData();
			typeof(AsyncWorkerData).GetField("maxConcurrency", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(worker, 1);
			typeof(AsyncWorkerData).GetField("maxOutstandingItems", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(worker, 1);
			worker.InitializeOnce();

			var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			var order = new List<int>();
			var otherLaneRan = new ManualResetEventSlim(false);
			var allDone = new CountdownEvent(3);

			LogAssert.IsTrue(worker.Enqueue(async () =>
			{
				await release.Task;
				lock (order) { order.Add(1); }
				allDone.Signal();
			}, 42), "the first item fills the only slot and the whole allowance");

			LogAssert.IsFalse(worker.Enqueue(() => Task.CompletedTask, 43), "ordinary work is refused over the cap");

			LogAssert.AreEqual(AsyncWorkAdmission.AdmittedOverCapacity, worker.EnqueueRequired(() =>
			{
				lock (order) { order.Add(2); }
				allDone.Signal();
				return Task.CompletedTask;
			}, 42), "required work is admitted over the cap");

			worker.EnqueueRequired(() =>
			{
				otherLaneRan.Set();
				allDone.Signal();
				return Task.CompletedTask;
			}, 99);

			LogAssert.IsFalse(otherLaneRan.Wait(150),
				"over-cap work still waits for a concurrency slot — the old fallback ran it at once, outside the cap");

			release.SetResult(true);
			LogAssert.IsTrue(allDone.Wait(5000), "everything admitted runs once the slot frees");
			LogAssert.AreEqual(2, order.Count, "both lane items ran");
			LogAssert.AreEqual(1, order[0], "and in the order they were enqueued");
			LogAssert.AreEqual(2, order[1], "over-cap work keeps its place in its lane");

			worker.Clear();
			LogAssert.AreEqual(AsyncWorkAdmission.Refused, worker.EnqueueRequired(() => Task.CompletedTask, 1),
				"a pool that has stopped accepting refuses, so the caller falls back");
		}

		#endregion

		#region Source pins (H2, H4, M21, M22, M23)

		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path).Replace("\r\n", "\n");
		}

		private static string MethodBody(string source, string signature, string nextSymbol)
		{
			int start = source.IndexOf(signature, StringComparison.Ordinal);
			LogAssert.IsTrue(start >= 0, $"the source must still declare {signature}");
			int end = source.IndexOf(nextSymbol, start + signature.Length, StringComparison.Ordinal);
			LogAssert.IsTrue(end > start, $"the end of {signature} must be locatable");
			return source.Substring(start, end - start);
		}

		private static void AssertOrdered(string body, string first, string second, string why)
		{
			int a = body.IndexOf(first, StringComparison.Ordinal);
			int b = body.IndexOf(second, StringComparison.Ordinal);
			LogAssert.IsTrue(a >= 0, $"{first} must still be present");
			LogAssert.IsTrue(b >= 0, $"{second} must still be present");
			LogAssert.IsTrue(a < b, why);
		}

		[Test]
		public void PeriodicSave_ReleasesItsGateOnEveryExitThatDidNotHandItOn()
		{
			/* H2: one exception in the capture loop latched the gate and stopped every save until
			 * restart. The gate is now released in a finally unless the async save took it. */
			string body = MethodBody(ReadSource(SavingPath), "private void OnPeriodicSave(float deltaTime)", "private bool TryCapturePeriodicSnapshot(");

			AssertOrdered(body, "runtimeData.TryBeginSave()", "finally", "the gate is taken before the guarded block");
			AssertOrdered(body, "finally", "if (!handedOff)", "and released in the finally");
			AssertOrdered(body, "if (!handedOff)", "runtimeData.EndSave();", "unless the save that owns it was enqueued");
			LogAssert.IsTrue(body.Contains("TryCapturePeriodicSnapshot("), "each character is captured through the per-character catch");
		}

		[Test]
		public void PeriodicSave_WritesRowsInBatches_NotOneTransactionEach()
		{
			string body = MethodBody(ReadSource(SavingPath),
				"private async Task SaveAllCharactersAsync(", "private static CharacterPersistRequest ToPersistRequest(");

			LogAssert.IsTrue(body.Contains("PersistManyAsync("), "H4: the periodic rows go through the batched save");
			LogAssert.IsFalse(body.Contains("SaveCharacterAsync("), "and not through the single-row save in a loop");
		}

		[Test]
		public void Logout_KeepsTheClaimWhileTheItemFlushHasNotLanded()
		{
			/* M22: the flush used to be awaited, its failure swallowed, and the claim released anyway. */
			string body = MethodBody(ReadSource(SavingPath),
				"private async Task SaveAndReleaseCharacterAsync(", "/// Saves all characters asynchronously");

			AssertOrdered(body, "RunItemFlushWithRetryAsync(", "ReleaseCharacterSessionAsync(", "the flush is run before the release");
			AssertOrdered(body, "itemOutcome == ItemWriteOutcome.Retry", "ReleaseCharacterSessionAsync(",
				"and a flush that has not landed is handled before the release is reached");
			string retryBranch = MethodBody(body, "itemOutcome == ItemWriteOutcome.Retry", "return;");
			LogAssert.IsTrue(retryBranch.Contains("QueuePendingFlush("), "which hands the flush and the claim to the retry queue");
		}

		[Test]
		public void PendingFlush_ReleasesOnlyOnceItsFlushIsDone()
		{
			string body = MethodBody(ReadSource(SavingPath),
				"private async Task RunPendingFlushAsync(", "#endregion");

			LogAssert.IsTrue(body.Contains("session.HasValue && flushDone"), "the retry's release waits for the flush");
			LogAssert.IsTrue(body.Contains("lock (pending.Gate)"), "L24: the entry is only read and cleared under its lock");
		}

		[Test]
		public void ShutdownFlush_ReleasesEachCharacterOnlyAfterItsOwnWrites()
		{
			/* M21: every release used to wait for every save, so a slow database released nobody. */
			string source = ReadSource(CharacterSystemPath);
			string flush = MethodBody(source, "private async Task FlushForShutdownAsync(", "private async Task FlushAndReleaseForShutdownAsync(");
			AssertOrdered(flush, "PersistManyAsync(", "SaveSubEntitiesSequentiallyAsync(", "rows, then sub-entities");
			AssertOrdered(flush, "SaveSubEntitiesSequentiallyAsync(", "FlushAndReleaseForShutdownAsync(", "then each character's items and release");

			string lane = MethodBody(source, "private async Task FlushAndReleaseForShutdownAsync(", "#endregion");
			AssertOrdered(lane, "RunItemFlushWithRetryAsync(", "ReleaseCharacterSessionAsync(", "a character's release follows its own flush");
		}

		[Test]
		public void Persistence_OverTheWorkersCap_DoesNotRunOutsideIt()
		{
			string body = MethodBody(ReadSource(ServerBehaviourPath),
				"protected bool EnqueuePersistence(", "private static readonly SemaphoreSlim PersistenceFallbackGate");

			LogAssert.IsTrue(body.Contains("EnqueueRequired("), "M23: over-cap persistence is admitted by the worker, under its cap");
			LogAssert.IsTrue(body.Contains("PersistenceFallbackGate.WaitAsync("), "and the remaining fallback is bounded");
		}

		#endregion
	}
}
