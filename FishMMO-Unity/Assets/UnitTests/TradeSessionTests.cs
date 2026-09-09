using NUnit.Framework;
using FishMMO.Server.Implementation.World.SceneServer;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The two-stage consent rules of a trade session (issue #144), as a truth table.
	/// </summary>
	/// <remarks>
	/// The exploit these guard against is swapping the goods at the last moment. Three
	/// mechanisms close it, and all three are pinned here: while both sides have CONFIRMED the
	/// table is frozen and every change is refused outright; any change that does get through
	/// (because the table was not frozen) clears both confirmations and both acceptances; and
	/// both confirming and accepting quote the state version they consent to, so a click in
	/// flight across a change lands as a no-op.
	/// </remarks>
	[TestFixture]
	public class TradeSessionTests
	{
		private const long Alice = 11;
		private const long Bob = 22;
		private const long Stranger = 33;

		private static TradeSession NewSession(int maxSlots = 8)
		{
			return new TradeSession(1, Alice, Bob, maxSlots);
		}

		private static TradeOffer Offer(int slot, long itemID = 100, int templateID = 7, uint amount = 1)
		{
			return new TradeOffer(slot, itemID, templateID, 0, amount);
		}

		/// <summary>Both sides declare their offers final: the table is frozen.</summary>
		private static void ConfirmBoth(TradeSession session)
		{
			LogAssert.AreEqual(TradeOfferRefusal.None, session.TryConfirm(Alice, true, session.Version), "Alice confirms");
			LogAssert.AreEqual(TradeOfferRefusal.None, session.TryConfirm(Bob, true, session.Version), "Bob confirms");
			LogAssert.IsTrue(session.IsLocked, "precondition: the table is locked");
		}

		/// <summary>The full run to both acceptances.</summary>
		private static void AcceptBoth(TradeSession session)
		{
			ConfirmBoth(session);
			LogAssert.AreEqual(TradeOfferRefusal.None, session.TryAccept(Alice, true, session.Version), "Alice accepts");
			LogAssert.AreEqual(TradeOfferRefusal.None, session.TryAccept(Bob, true, session.Version), "Bob accepts");
			LogAssert.IsTrue(session.BothAccepted, "precondition: both accepted");
		}

		// ── Parties ─────────────────────────────────────────────────────────────────────────

		[Test]
		public void PartyOf_ResolvesBothSidesAndNobodyElse()
		{
			TradeSession session = NewSession();

			LogAssert.AreSame(session.First, session.PartyOf(Alice), "Alice is the first party");
			LogAssert.AreSame(session.Second, session.PartyOf(Bob), "Bob is the second party");
			LogAssert.IsNull(session.PartyOf(Stranger), "a stranger is no party");

			LogAssert.AreSame(session.Second, session.PartnerOf(Alice), "Bob is Alice's partner");
			LogAssert.AreSame(session.First, session.PartnerOf(Bob), "Alice is Bob's partner");
			LogAssert.IsNull(session.PartnerOf(Stranger), "a stranger has no partner");

			LogAssert.IsTrue(session.Involves(Alice) && session.Involves(Bob) && !session.Involves(Stranger), "Involves matches PartyOf");
		}

		[Test]
		public void AStrangerCannotTouchTheTable()
		{
			TradeSession session = NewSession();

			LogAssert.AreEqual(TradeOfferRefusal.NotAParty, session.TryAddOffer(Stranger, Offer(0)), "offer");
			LogAssert.AreEqual(TradeOfferRefusal.NotAParty, session.TryRemoveOffer(Stranger, 0, out _), "withdraw");
			LogAssert.AreEqual(TradeOfferRefusal.NotAParty, session.TrySetCurrency(Stranger, 5), "currency");
			LogAssert.AreEqual(TradeOfferRefusal.NotAParty, session.TryConfirm(Stranger, true, session.Version), "confirm");
			LogAssert.AreEqual(TradeOfferRefusal.NotAParty, session.TryAccept(Stranger, true, session.Version), "accept");
		}

		// ── Stage one: confirm freezes the table ────────────────────────────────────────────

		[Test]
		public void OneConfirmation_DoesNotLockTheTable()
		{
			TradeSession session = NewSession();

			LogAssert.AreEqual(TradeOfferRefusal.None, session.TryConfirm(Alice, true, session.Version), "Alice confirms");

			LogAssert.IsFalse(session.IsLocked, "one side is not a lock");
			LogAssert.IsTrue(session.First.Confirmed && !session.Second.Confirmed, "only Alice has confirmed");
			LogAssert.AreEqual(TradeOfferRefusal.None, session.TryAddOffer(Bob, Offer(1)), "Bob may still change his offer");
		}

		[Test]
		public void WhileBothConfirmed_EveryChangeToTheTableIsRefused()
		{
			TradeSession session = NewSession();
			session.TryAddOffer(Alice, Offer(4));
			session.TrySetCurrency(Bob, 20);
			ConfirmBoth(session);
			uint locked = session.Version;

			LogAssert.AreEqual(TradeOfferRefusal.TableLocked, session.TryAddOffer(Alice, Offer(5)), "adding an item");
			LogAssert.AreEqual(TradeOfferRefusal.TableLocked, session.TryRemoveOffer(Alice, 4, out _), "withdrawing an item");
			LogAssert.AreEqual(TradeOfferRefusal.TableLocked, session.TrySetCurrency(Alice, 1), "changing currency");
			LogAssert.AreEqual(TradeOfferRefusal.TableLocked, session.TrySetCurrency(Bob, 0), "the other side, too");

			LogAssert.AreEqual(locked, session.Version, "a refused change does not move the version");
			LogAssert.AreEqual(1, session.First.Offers.Count, "Alice's item is untouched");
			LogAssert.AreEqual(20L, session.Second.Currency, "Bob's currency is untouched");
			LogAssert.IsTrue(session.IsLocked, "and the table is still locked");
		}

		[Test]
		public void Confirming_QuotingAStaleVersion_IsRefused()
		{
			TradeSession session = NewSession();
			uint seen = session.Version;

			// Bob changes the table after Alice saw it but before her confirmation arrives.
			session.TryAddOffer(Bob, Offer(1));

			LogAssert.AreEqual(TradeOfferRefusal.StaleVersion, session.TryConfirm(Alice, true, seen), "the in-flight confirmation lands on a changed table");
			LogAssert.IsFalse(session.First.Confirmed, "and confirms nothing");
		}

		[Test]
		public void AZeroVersion_NeverConfirmsAndNeverAccepts()
		{
			// A client that never filled the field in sends 0; the session starts at 1.
			TradeSession session = NewSession();
			LogAssert.AreEqual(TradeOfferRefusal.StaleVersion, session.TryConfirm(Alice, true, 0), "a zeroed confirm is refused");
			ConfirmBoth(session);
			LogAssert.AreEqual(TradeOfferRefusal.StaleVersion, session.TryAccept(Alice, true, 0), "a zeroed accept is refused");
		}

		[Test]
		public void AChange_WhileTheTableIsUnlocked_ClearsBothConfirmations()
		{
			TradeSession session = NewSession();
			LogAssert.AreEqual(TradeOfferRefusal.None, session.TryConfirm(Alice, true, session.Version), "Alice confirms");
			uint before = session.Version;

			// Bob has not confirmed, so the table is still his to change.
			LogAssert.AreEqual(TradeOfferRefusal.None, session.TryAddOffer(Bob, Offer(3)), "Bob adds an item");

			LogAssert.IsFalse(session.First.Confirmed, "Alice's confirmation is cleared by Bob's change");
			LogAssert.IsFalse(session.Second.Confirmed, "Bob's own is cleared too");
			LogAssert.AreEqual(before + 1, session.Version, "the version moved");
		}

		[Test]
		public void EveryKindOfChange_ClearsBothConfirmations()
		{
			TradeSession session = NewSession();

			session.TryAddOffer(Alice, Offer(2));
			session.TryConfirm(Alice, true, session.Version);
			LogAssert.AreEqual(TradeOfferRefusal.None, session.TryRemoveOffer(Alice, 2, out _), "withdrawing");
			LogAssert.IsFalse(session.First.Confirmed, "a withdrawal clears it");

			session.TryConfirm(Bob, true, session.Version);
			LogAssert.AreEqual(TradeOfferRefusal.None, session.TrySetCurrency(Bob, 50), "currency");
			LogAssert.IsFalse(session.Second.Confirmed, "a currency change clears it");

			session.TryConfirm(Alice, true, session.Version);
			LogAssert.AreEqual(TradeOfferRefusal.None, session.TrySetCurrency(Bob, 50), "re-stating the same amount is still a change");
			LogAssert.IsFalse(session.First.Confirmed, "and still clears the other side's confirmation");
		}

		// ── Stage two: accept is only reachable on a frozen table ───────────────────────────

		[Test]
		public void Accept_BeforeBothSidesConfirm_IsRefused()
		{
			TradeSession session = NewSession();

			LogAssert.AreEqual(TradeOfferRefusal.NotLocked, session.TryAccept(Alice, true, session.Version), "nobody has confirmed");

			session.TryConfirm(Alice, true, session.Version);
			LogAssert.AreEqual(TradeOfferRefusal.NotLocked, session.TryAccept(Alice, true, session.Version), "only one side has confirmed");
			LogAssert.IsFalse(session.First.Accepted, "nothing was accepted");

			session.TryConfirm(Bob, true, session.Version);
			LogAssert.AreEqual(TradeOfferRefusal.None, session.TryAccept(Alice, true, session.Version), "now the table is frozen");
			LogAssert.IsTrue(session.First.Accepted, "and the acceptance stands");
		}

		[Test]
		public void Accept_QuotingAStaleVersion_IsRefused()
		{
			TradeSession session = NewSession();
			ConfirmBoth(session);
			uint seen = session.Version;

			/* Alice revokes, changes the table, and both confirm again — the exact cycle a
			 * last-minute switch would need. Bob's accept, sent while he was looking at the
			 * old table, must not consent to the new one. */
			session.TryConfirm(Alice, false, 0);
			session.TryAddOffer(Alice, Offer(6));
			ConfirmBoth(session);

			LogAssert.AreNotEqual(seen, session.Version, "the table moved");
			LogAssert.AreEqual(TradeOfferRefusal.StaleVersion, session.TryAccept(Bob, true, seen), "the in-flight accept is refused");
			LogAssert.IsFalse(session.Second.Accepted, "and consents to nothing");
		}

		[Test]
		public void Revoking_UnlocksTheTable_ClearsBothAcceptances_AndKeepsThePartnersConfirmation()
		{
			TradeSession session = NewSession();
			AcceptBoth(session);

			LogAssert.AreEqual(TradeOfferRefusal.None, session.TryConfirm(Alice, false, 0), "revoking never fails on version");

			LogAssert.IsFalse(session.IsLocked, "the table is unlocked");
			LogAssert.IsFalse(session.First.Confirmed, "Alice is no longer confirmed");
			LogAssert.IsTrue(session.Second.Confirmed, "Bob's confirmation stands: he has not changed his mind about his own offer");
			LogAssert.IsFalse(session.First.Accepted || session.Second.Accepted, "BOTH acceptances are cleared: they were consent to a frozen table");
			LogAssert.AreEqual(TradeOfferRefusal.None, session.TryAddOffer(Alice, Offer(7)), "and Alice may change her offer again");
			LogAssert.IsFalse(session.Second.Confirmed, "which then clears Bob's confirmation as any change does");
		}

		[Test]
		public void WithdrawingAcceptance_LeavesTheLockAndThePartnersAcceptance()
		{
			TradeSession session = NewSession();
			AcceptBoth(session);

			LogAssert.AreEqual(TradeOfferRefusal.None, session.TryAccept(Alice, false, 0), "taking consent back is always allowed");

			LogAssert.IsFalse(session.First.Accepted, "Alice no longer accepts");
			LogAssert.IsTrue(session.Second.Accepted, "Bob's acceptance is untouched: nothing about the table changed");
			LogAssert.IsTrue(session.IsLocked, "and the table is still frozen");
		}

		// ── Offer rules ─────────────────────────────────────────────────────────────────────

		[Test]
		public void TheSameSlot_CannotBeOfferedTwice()
		{
			TradeSession session = NewSession();
			LogAssert.AreEqual(TradeOfferRefusal.None, session.TryAddOffer(Alice, Offer(4)), "first");
			LogAssert.AreEqual(TradeOfferRefusal.AlreadyOffered, session.TryAddOffer(Alice, Offer(4, amount: 3)), "second, same slot");
			LogAssert.AreEqual(1, session.First.Offers.Count, "one entry");
		}

		[Test]
		public void TheTable_IsCappedAtMaxOfferSlots()
		{
			TradeSession session = NewSession(maxSlots: 2);
			LogAssert.AreEqual(TradeOfferRefusal.None, session.TryAddOffer(Alice, Offer(0)), "1 of 2");
			LogAssert.AreEqual(TradeOfferRefusal.None, session.TryAddOffer(Alice, Offer(1)), "2 of 2");
			LogAssert.AreEqual(TradeOfferRefusal.TableFull, session.TryAddOffer(Alice, Offer(2)), "3 of 2");
			LogAssert.AreEqual(TradeOfferRefusal.None, session.TryAddOffer(Bob, Offer(0)), "the cap is per side");
		}

		[Test]
		public void WithdrawingWhatWasNeverOffered_IsRefused()
		{
			TradeSession session = NewSession();
			LogAssert.AreEqual(TradeOfferRefusal.NotOffered, session.TryRemoveOffer(Alice, 9, out _), "nothing at slot 9");
		}

		[Test]
		public void NegativeCurrency_IsRefusedAndChangesNothing()
		{
			TradeSession session = NewSession();
			session.TryConfirm(Alice, true, session.Version);
			uint before = session.Version;

			LogAssert.AreEqual(TradeOfferRefusal.NegativeCurrency, session.TrySetCurrency(Alice, -1), "negative refused");
			LogAssert.AreEqual(before, session.Version, "a refusal does not move the version");
			LogAssert.IsTrue(session.First.Confirmed, "a refusal does not clear consent");
		}

		[Test]
		public void WithdrawingAnOffer_HandsBackTheEntry_SoItsSlotCanBeUnlocked()
		{
			TradeSession session = NewSession();
			session.TryAddOffer(Bob, Offer(5));

			LogAssert.AreEqual(TradeOfferRefusal.None, session.TryRemoveOffer(Bob, 5, out TradeOffer removed), "Bob withdraws");
			LogAssert.AreEqual(5, removed.Slot, "the withdrawn offer is handed back");
			LogAssert.AreEqual(0, session.Second.Offers.Count, "the table is empty again");
		}

		// ── Phases ──────────────────────────────────────────────────────────────────────────

		[Test]
		public void Commit_RequiresTheLockAndBothAcceptances()
		{
			TradeSession session = NewSession();
			LogAssert.IsFalse(session.TryBeginCommit(), "nobody confirmed");

			ConfirmBoth(session);
			LogAssert.IsFalse(session.TryBeginCommit(), "locked, but nobody accepted");

			session.TryAccept(Alice, true, session.Version);
			LogAssert.IsFalse(session.TryBeginCommit(), "one accepted");

			session.TryAccept(Bob, true, session.Version);
			LogAssert.IsTrue(session.TryBeginCommit(), "both accepted on a frozen table");
			LogAssert.AreEqual(TradePhase.Committing, session.Phase, "now committing");
			LogAssert.IsFalse(session.TryBeginCommit(), "a second commit is refused");
		}

		[Test]
		public void ACommittingSession_RefusesEveryChange()
		{
			TradeSession session = NewSession();
			AcceptBoth(session);
			session.TryBeginCommit();

			LogAssert.AreEqual(TradeOfferRefusal.NotOpen, session.TryAddOffer(Alice, Offer(0)), "offer");
			LogAssert.AreEqual(TradeOfferRefusal.NotOpen, session.TryRemoveOffer(Alice, 0, out _), "withdraw");
			LogAssert.AreEqual(TradeOfferRefusal.NotOpen, session.TrySetCurrency(Alice, 1), "currency");
			LogAssert.AreEqual(TradeOfferRefusal.NotOpen, session.TryConfirm(Alice, false, 0), "even revoking: the exchange is decided");
			LogAssert.AreEqual(TradeOfferRefusal.NotOpen, session.TryAccept(Alice, false, 0), "and un-accepting");
		}

		[Test]
		public void Close_IsIdempotent()
		{
			TradeSession session = NewSession();
			session.Close();
			session.Close();
			LogAssert.AreEqual(TradePhase.Closed, session.Phase, "closed");
			LogAssert.IsFalse(session.TryBeginCommit(), "a closed session cannot commit");
		}

		// ── Wire shape ──────────────────────────────────────────────────────────────────────

		[Test]
		public void BuildStateFor_IsReceiverShaped()
		{
			TradeSession session = NewSession();
			session.TryAddOffer(Alice, Offer(2, itemID: 500, templateID: 9, amount: 4));
			session.TrySetCurrency(Bob, 75);
			session.TryConfirm(Bob, true, session.Version);

			TradeStateBroadcast forAlice = session.BuildStateFor(Alice);
			TradeStateBroadcast forBob = session.BuildStateFor(Bob);

			LogAssert.AreEqual(session.Version, forAlice.Version, "Alice sees the current version");
			LogAssert.AreEqual(1, forAlice.OwnOffer.Length, "Alice's own offer has her item");
			LogAssert.AreEqual(2, forAlice.OwnOffer[0].Slot, "with its slot");
			LogAssert.AreEqual(500L, forAlice.OwnOffer[0].ItemID, "its id");
			LogAssert.AreEqual(4u, forAlice.OwnOffer[0].Amount, "its amount");
			LogAssert.AreEqual(0, forAlice.PartnerOffer.Length, "Bob offered no items");
			LogAssert.AreEqual(75L, forAlice.PartnerCurrency, "Bob's currency is on Alice's partner side");
			LogAssert.IsTrue(forAlice.PartnerConfirmed && !forAlice.OwnConfirmed, "Bob confirmed, Alice has not");
			LogAssert.IsFalse(forAlice.OwnAccepted || forAlice.PartnerAccepted, "nobody has accepted");

			LogAssert.AreEqual(1, forBob.PartnerOffer.Length, "Bob sees Alice's item as the partner's");
			LogAssert.AreEqual(75L, forBob.OwnCurrency, "Bob's currency is on his own side");
			LogAssert.IsTrue(forBob.OwnConfirmed && !forBob.PartnerConfirmed, "the flags swap sides");

			LogAssert.AreEqual(default(TradeStateBroadcast).Version, session.BuildStateFor(Stranger).Version, "a stranger gets nothing");
		}

		[Test]
		public void BuildStateFor_CarriesBothAcceptancesOnAFrozenTable()
		{
			TradeSession session = NewSession();
			ConfirmBoth(session);
			session.TryAccept(Bob, true, session.Version);

			TradeStateBroadcast forAlice = session.BuildStateFor(Alice);
			LogAssert.IsTrue(forAlice.OwnConfirmed && forAlice.PartnerConfirmed, "both confirmations travel");
			LogAssert.IsTrue(forAlice.PartnerAccepted && !forAlice.OwnAccepted, "and Bob's acceptance is on the partner side");
		}
	}
}
