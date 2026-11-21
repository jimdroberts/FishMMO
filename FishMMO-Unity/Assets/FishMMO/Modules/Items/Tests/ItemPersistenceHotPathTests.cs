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
	/// The item half of the 2026-09-25 server hot-path audit proofs (H3, M20, M22, M23, L22): what an item snapshot writes,
	/// each character's snapshot turn, the item write journal, and what an item write's refusal or batch outcome means.
	/// Moved out of <c>PersistenceHotPathTests</c> with the inventory system (the Items module, 2026-11-20).
	/// </summary>
	/// <remarks>
	/// The rules that could be written as pure functions are tested as such; the journal is driven
	/// through reflection, as <c>ItemWriteSequenceTests</c> drives it.
	/// </remarks>
	[TestFixture]
	public class ItemPersistenceHotPathTests
	{
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

		#region Item write outcomes (M20, M22)

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

		#endregion
	}
}
