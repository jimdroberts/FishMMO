using System;
using System.Reflection;
using FishMMO.Server.Implementation.World.SceneServer;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs for the capture sequence that decides whether an item write is still worth applying.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The journal skips a write that a later one has already superseded. That decision compares a
	/// batch's capture sequence against a per-character watermark, so the two have to move together:
	/// if sequences can restart while a watermark survives, every write in the next session is
	/// judged superseded by a session that has already ended, and is dropped.
	/// </para>
	/// <para>
	/// That is not hypothetical. It was observed live: a character equipped an item, and the server
	/// logged
	/// </para>
	/// <code>
	/// ApplyItemBatchAsync: skipped superseded EquipFromInventory (CharID=18, Seq=1, Snapshot=False)
	/// ApplyItemBatchAsync: skipped superseded ItemSnapshot       (CharID=18, Seq=2, Snapshot=True)
	/// </code>
	/// <para>
	/// Sequences 1 and 2 -- the first two of the session -- both discarded, and the database left
	/// untouched while the client showed the item equipped. Silent divergence between server memory
	/// and the database is the ground state that item duplication reports grow out of, because the
	/// next write to land publishes whichever side happens to win.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class ItemWriteSequenceTests
	{
		private const long CharacterId = 18;

		private object journal;
		private MethodInfo nextSequence;
		private MethodInfo shouldApply;
		private MethodInfo tryClaimSequence;
		private MethodInfo forgetCharacter;

		[SetUp]
		public void SetUp()
		{
			Type type = typeof(CharacterInventorySystem).GetNestedType(
				"ItemWriteJournal",
				BindingFlags.NonPublic);

			LogAssert.IsNotNull(type, "the item write journal must still exist.");

			journal = Activator.CreateInstance(type, nonPublic: true);
			nextSequence = type.GetMethod("NextSequence");
			shouldApply = type.GetMethod("ShouldApply");
			tryClaimSequence = type.GetMethod("TryClaimSequence");
			forgetCharacter = type.GetMethod("ForgetCharacter");

			LogAssert.IsNotNull(nextSequence, "NextSequence must still exist.");
			LogAssert.IsNotNull(shouldApply, "ShouldApply must still exist.");
			LogAssert.IsNotNull(tryClaimSequence, "TryClaimSequence must still exist.");
			LogAssert.IsNotNull(forgetCharacter, "ForgetCharacter must still exist.");
		}

		private long Next() => (long)nextSequence.Invoke(journal, new object[] { CharacterId });

		private bool ShouldApply(long sequence, bool isSnapshot) =>
			(bool)shouldApply.Invoke(journal, new object[] { CharacterId, sequence, isSnapshot });

		private bool Claim(long sequence, bool isSnapshot) =>
			(bool)tryClaimSequence.Invoke(journal, new object[] { CharacterId, sequence, isSnapshot });

		private void Forget() => forgetCharacter.Invoke(journal, new object[] { CharacterId });

		/// <summary>
		/// Plays a session: some writes, then the despawn flush, which is captured and only
		/// afterwards applied.
		/// </summary>
		/// <remarks>
		/// The order matters and is the whole point. ForgetCharacter runs when the flush is
		/// CAPTURED, while the flush claims its sequence later, on the worker thread -- so the claim
		/// lands after the forget and puts the watermark back.
		/// </remarks>
		private void PlayASessionAndLogOut()
		{
			Claim(Next(), false);
			Claim(Next(), false);

			long flush = Next();
			Forget();
			Claim(flush, true);
		}

		[Test]
		public void AfterALogout_TheNextSessionsFirstWriteIsStillApplied()
		{
			/* The live failure. Every write of the new session was discarded as superseded by a
			 * session that had already ended, so nothing the player did was stored. */
			PlayASessionAndLogOut();

			long first = Next();

			LogAssert.IsTrue(ShouldApply(first, false),
				"the first write after logging back in must not be treated as superseded");
		}

		[Test]
		public void AfterALogout_TheNextSessionsSnapshotIsStillApplied()
		{
			/* The snapshot is the repair path -- it re-states the character's items in full, so it
			 * is what would otherwise correct a dropped incremental. Losing it too is why the
			 * divergence persisted rather than being cleaned up by the next periodic save. */
			PlayASessionAndLogOut();

			Next();
			long snapshot = Next();

			LogAssert.IsTrue(ShouldApply(snapshot, true),
				"the snapshot that would repair the divergence must not be dropped as well");
		}

		[Test]
		public void AfterALogout_TheNextSessionsWriteCanActuallyClaim()
		{
			// ShouldApply is only the pre-filter; the binding decision is the claim.
			PlayASessionAndLogOut();

			LogAssert.IsTrue(Claim(Next(), false),
				"the write must be able to claim its sequence, not merely pass the pre-filter");
		}

		[Test]
		public void ASequenceIsNeverReissuedAfterALogout()
		{
			/* The root cause stated directly. A number that has already been used as a watermark
			 * must never be handed out again, or the comparison is meaningless. */
			PlayASessionAndLogOut();

			long reissued = Next();

			LogAssert.IsTrue(reissued > 3,
				$"sequence {reissued} was already used before the logout and must not be reissued");
		}

		// --- What the journal is FOR must keep working ----------------------------------------

		[Test]
		public void ASupersededSnapshotIsStillSkipped()
		{
			/* The guard's real job. A snapshot prunes and upserts ungated, so one that lands after a
			 * newer incremental would delete the row that incremental wrote. */
			long stale = Next();
			long newer = Next();
			Claim(newer, false);

			LogAssert.IsFalse(ShouldApply(stale, true),
				"a snapshot older than an applied write must still be skipped");
		}

		[Test]
		public void AnIncrementalIsSkippedOnlyByALaterSnapshot()
		{
			/* Two incrementals can touch different slots, so one must not be dropped merely because
			 * the other landed first -- that would lose a delete and resurrect an item. */
			long first = Next();
			long second = Next();
			Claim(second, false);

			LogAssert.IsTrue(ShouldApply(first, false),
				"an earlier incremental is not superseded by a later incremental");

			long snapshot = Next();
			Claim(snapshot, true);

			LogAssert.IsFalse(ShouldApply(first, false),
				"but a later snapshot does supersede it");
		}

		[Test]
		public void WithinOneSession_SequencesStillIncrease()
		{
			LogAssert.IsTrue(Next() < Next(), "capture order must remain observable");
		}
	}
}
