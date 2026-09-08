using NUnit.Framework;
using FishMMO.Server.Implementation.World.SceneServer;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The acceptance rules of a trade session (issue #144), as a truth table.
	/// </summary>
	/// <remarks>
	/// The exploit these guard against is swapping the goods after the other party has said
	/// yes. Two mechanisms close it and both are pinned here: every change to either table
	/// clears BOTH acceptances and bumps the version, and an accept that quotes any version
	/// but the current one is refused.
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

		private static void AcceptBoth(TradeSession session)
		{
			LogAssert.AreEqual(TradeOfferRefusal.None, session.TryAccept(Alice, true, session.Version), "Alice accepts the current version");
			LogAssert.AreEqual(TradeOfferRefusal.None, session.TryAccept(Bob, true, session.Version), "Bob accepts the current version");
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
			LogAssert.AreEqual(TradeOfferRefusal.NotAParty, session.TryAccept(Stranger, true, session.Version), "accept");
		}

		// ── The invariant: any change clears both acceptances ───────────────────────────────

		[Test]
		public void AddingAnOffer_ClearsBothAcceptancesAndBumpsTheVersion()
		{
			TradeSession session = NewSession();
			AcceptBoth(session);
			uint before = session.Version;

			LogAssert.AreEqual(TradeOfferRefusal.None, session.TryAddOffer(Alice, Offer(3)), "Alice adds an item");

			LogAssert.IsFalse(session.First.Accepted, "Alice's acceptance is cleared by her own change");
			LogAssert.IsFalse(session.Second.Accepted, "Bob's acceptance is cleared by Alice's change");
			LogAssert.AreEqual(before + 1, session.Version, "the version moved");
		}

		[Test]
		public void WithdrawingAnOffer_ClearsBothAcceptances()
		{
			TradeSession session = NewSession();
			session.TryAddOffer(Bob, Offer(5));
			AcceptBoth(session);

			LogAssert.AreEqual(TradeOfferRefusal.None, session.TryRemoveOffer(Bob, 5, out TradeOffer removed), "Bob withdraws");
			LogAssert.AreEqual(5, removed.Slot, "the withdrawn offer is handed back so its slot can be unlocked");
			LogAssert.IsFalse(session.First.Accepted || session.Second.Accepted, "both acceptances cleared");
			LogAssert.AreEqual(0, session.Second.Offers.Count, "the table is empty again");
		}

		[Test]
		public void SettingCurrency_ClearsBothAcceptances_EvenToTheSameAmount()
		{
			TradeSession session = NewSession();
			session.TrySetCurrency(Alice, 50);
			AcceptBoth(session);
			uint before = session.Version;

			LogAssert.AreEqual(TradeOfferRefusal.None, session.TrySetCurrency(Alice, 50), "re-stating the same amount is still a change");

			LogAssert.IsFalse(session.First.Accepted || session.Second.Accepted, "both acceptances cleared");
			LogAssert.AreEqual(before + 1, session.Version, "the version moved");
			LogAssert.AreEqual(50L, session.First.Currency, "the amount stands");
		}

		[Test]
		public void NegativeCurrency_IsRefusedAndChangesNothing()
		{
			TradeSession session = NewSession();
			AcceptBoth(session);
			uint before = session.Version;

			LogAssert.AreEqual(TradeOfferRefusal.NegativeCurrency, session.TrySetCurrency(Alice, -1), "negative refused");
			LogAssert.AreEqual(before, session.Version, "a refusal does not move the version");
			LogAssert.IsTrue(session.BothAccepted, "a refusal does not clear acceptances");
		}

		// ── Version-gated accept ────────────────────────────────────────────────────────────

		[Test]
		public void Accept_QuotingAStaleVersion_IsRefused()
		{
			TradeSession session = NewSession();
			uint seen = session.Version;

			// Bob changes the table after Alice saw it but before her accept arrives.
			session.TryAddOffer(Bob, Offer(1));

			LogAssert.AreEqual(TradeOfferRefusal.StaleVersion, session.TryAccept(Alice, true, seen), "the in-flight accept lands on a changed table");
			LogAssert.IsFalse(session.First.Accepted, "and consents to nothing");
		}

		[Test]
		public void Accept_QuotingTheCurrentVersion_Stands()
		{
			TradeSession session = NewSession();
			session.TryAddOffer(Bob, Offer(1));

			LogAssert.AreEqual(TradeOfferRefusal.None, session.TryAccept(Alice, true, session.Version), "current version accepted");
			LogAssert.IsTrue(session.First.Accepted, "Alice has accepted");
			LogAssert.IsFalse(session.BothAccepted, "Bob has not");
		}

		[Test]
		public void AZeroVersion_NeverMatches()
		{
			// A client that never filled the field in sends 0; the session starts at 1.
			TradeSession session = NewSession();
			LogAssert.AreEqual(TradeOfferRefusal.StaleVersion, session.TryAccept(Alice, true, 0), "a zeroed accept is refused");
		}

		[Test]
		public void WithdrawingAcceptance_NeverFailsOnVersion()
		{
			TradeSession session = NewSession();
			AcceptBoth(session);

			LogAssert.AreEqual(TradeOfferRefusal.None, session.TryAccept(Alice, false, 0), "taking consent back is always allowed");
			LogAssert.IsFalse(session.First.Accepted, "Alice no longer accepts");
			LogAssert.IsTrue(session.Second.Accepted, "Bob's acceptance is untouched: withdrawing is not a change to the table");
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

		// ── Phases ──────────────────────────────────────────────────────────────────────────

		[Test]
		public void Commit_RequiresBothAcceptances()
		{
			TradeSession session = NewSession();
			LogAssert.IsFalse(session.TryBeginCommit(), "nobody accepted");

			session.TryAccept(Alice, true, session.Version);
			LogAssert.IsFalse(session.TryBeginCommit(), "one accepted");

			session.TryAccept(Bob, true, session.Version);
			LogAssert.IsTrue(session.TryBeginCommit(), "both accepted");
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
			LogAssert.AreEqual(TradeOfferRefusal.NotOpen, session.TryAccept(Alice, false, 0), "even withdrawing acceptance: the exchange is decided");
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
			session.TryAccept(Bob, true, session.Version);

			TradeStateBroadcast forAlice = session.BuildStateFor(Alice);
			TradeStateBroadcast forBob = session.BuildStateFor(Bob);

			LogAssert.AreEqual(session.Version, forAlice.Version, "Alice sees the current version");
			LogAssert.AreEqual(1, forAlice.OwnOffer.Length, "Alice's own offer has her item");
			LogAssert.AreEqual(2, forAlice.OwnOffer[0].Slot, "with its slot");
			LogAssert.AreEqual(500L, forAlice.OwnOffer[0].ItemID, "its id");
			LogAssert.AreEqual(4u, forAlice.OwnOffer[0].Amount, "its amount");
			LogAssert.AreEqual(0, forAlice.PartnerOffer.Length, "Bob offered no items");
			LogAssert.AreEqual(75L, forAlice.PartnerCurrency, "Bob's currency is on Alice's partner side");
			LogAssert.IsTrue(forAlice.PartnerAccepted && !forAlice.OwnAccepted, "Bob accepted, Alice has not");

			LogAssert.AreEqual(1, forBob.PartnerOffer.Length, "Bob sees Alice's item as the partner's");
			LogAssert.AreEqual(75L, forBob.OwnCurrency, "Bob's currency is on his own side");
			LogAssert.IsTrue(forBob.OwnAccepted && !forBob.PartnerAccepted, "the flags swap sides");

			LogAssert.AreEqual(default(TradeStateBroadcast).Version, session.BuildStateFor(Stranger).Version, "a stranger gets nothing");
		}
	}
}
