using FishMMO.Shared;
using NUnit.Framework;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Pins the group finder's pure rules: how big a group it forms, who it refuses at the door,
	/// and what it does with a matched player who cannot travel yet.
	/// </summary>
	/// <remarks>
	/// These are the decisions the server's matching pump delegates to
	/// <see cref="GroupFinderRules"/>, tested here without a server, a database or a connection.
	/// Each test states one row of the truth table the remarks on the rule describe.
	/// </remarks>
	public class GroupFinderRulesTests
	{
		private static DungeonDifficultyDefinition Difficulty(bool enabled = true, int finderSize = 0, int minimumParty = 1)
		{
			return new DungeonDifficultyDefinition
			{
				GroupFinderEnabled = enabled,
				GroupFinderSize = finderSize,
				MinimumPartySize = minimumParty,
			};
		}

		// ── ResolveGroupSize ────────────────────────────────────────────────

		[Test]
		public void GroupSize_NoTemplate_IsOff()
		{
			Assert.AreEqual(0, GroupFinderRules.ResolveGroupSize(null, 5));
		}

		[Test]
		public void GroupSize_Disabled_IsOff()
		{
			Assert.AreEqual(0, GroupFinderRules.ResolveGroupSize(Difficulty(enabled: false), 5));
		}

		[Test]
		public void GroupSize_Default_FillsToCapacity()
		{
			Assert.AreEqual(5, GroupFinderRules.ResolveGroupSize(Difficulty(), 5));
		}

		[Test]
		public void GroupSize_AuthorPinned_IsUsed()
		{
			Assert.AreEqual(3, GroupFinderRules.ResolveGroupSize(Difficulty(finderSize: 3), 5));
		}

		[Test]
		public void GroupSize_AuthorPinnedAboveCapacity_IsCapped()
		{
			Assert.AreEqual(5, GroupFinderRules.ResolveGroupSize(Difficulty(finderSize: 8), 5));
		}

		[Test]
		public void GroupSize_AuthorPinnedBelowMinimumParty_IsRaised()
		{
			// The run would refuse a party of two at the door, so the finder does not form one.
			Assert.AreEqual(4, GroupFinderRules.ResolveGroupSize(Difficulty(finderSize: 2, minimumParty: 4), 5));
		}

		[Test]
		public void GroupSize_NeverBelowTwo()
		{
			Assert.AreEqual(2, GroupFinderRules.ResolveGroupSize(Difficulty(finderSize: 1), 5));
		}

		[Test]
		public void GroupSize_CapacityOfOne_IsOff()
		{
			// A solo dungeon has nothing to match; the entrance's Open serves it.
			Assert.AreEqual(0, GroupFinderRules.ResolveGroupSize(Difficulty(), 1));
		}

		[Test]
		public void GroupSize_MinimumPartyAboveCapacity_IsOff()
		{
			// Unopenable by anyone; the finder must not queue people for it.
			Assert.AreEqual(0, GroupFinderRules.ResolveGroupSize(Difficulty(minimumParty: 6), 5));
		}

		// ── ResolveQueueRefusal ─────────────────────────────────────────────

		[Test]
		public void QueueRefusal_FinderOff_IsNotAvailable_WhateverTheState()
		{
			Assert.AreEqual(GroupFinderRefusalReason.NotAvailable, GroupFinderRules.ResolveQueueRefusal(0, isInInstance: true, inPartyWithOthers: true));
		}

		[Test]
		public void QueueRefusal_InInstance_BeatsParty()
		{
			Assert.AreEqual(GroupFinderRefusalReason.InInstance, GroupFinderRules.ResolveQueueRefusal(5, isInInstance: true, inPartyWithOthers: true));
		}

		[Test]
		public void QueueRefusal_InPartyWithOthers()
		{
			Assert.AreEqual(GroupFinderRefusalReason.InParty, GroupFinderRules.ResolveQueueRefusal(5, isInInstance: false, inPartyWithOthers: true));
		}

		[Test]
		public void QueueRefusal_Free_IsNone()
		{
			Assert.AreEqual(GroupFinderRefusalReason.None, GroupFinderRules.ResolveQueueRefusal(5, isInInstance: false, inPartyWithOthers: false));
		}

		// ── ResolveWaitingCancel ────────────────────────────────────────────

		[Test]
		public void WaitingCancel_AtEntranceAndFree_StaysQueued()
		{
			Assert.AreEqual(GroupFinderRefusalReason.None, GroupFinderRules.ResolveWaitingCancel(isInInstance: false, inParty: false, nearEntrance: true));
		}

		[Test]
		public void WaitingCancel_WalkedAway_IsLeftEntrance()
		{
			Assert.AreEqual(GroupFinderRefusalReason.LeftEntrance, GroupFinderRules.ResolveWaitingCancel(isInInstance: false, inParty: false, nearEntrance: false));
		}

		[Test]
		public void WaitingCancel_JoinedParty_BeatsPosition()
		{
			Assert.AreEqual(GroupFinderRefusalReason.JoinedParty, GroupFinderRules.ResolveWaitingCancel(isInInstance: false, inParty: true, nearEntrance: false));
		}

		[Test]
		public void WaitingCancel_EnteredInstance_BeatsEverything()
		{
			Assert.AreEqual(GroupFinderRefusalReason.EnteredInstance, GroupFinderRules.ResolveWaitingCancel(isInInstance: true, inParty: true, nearEntrance: false));
		}

		// ── ResolveMatchedTransfer ──────────────────────────────────────────

		[Test]
		public void MatchedTransfer_Free_TransfersRegardlessOfTime()
		{
			Assert.AreEqual(GroupFinderRules.MatchedTransferAction.Transfer, GroupFinderRules.ResolveMatchedTransfer(true, 0.0, 60.0));
			Assert.AreEqual(GroupFinderRules.MatchedTransferAction.Transfer, GroupFinderRules.ResolveMatchedTransfer(true, 600.0, 60.0));
		}

		[Test]
		public void MatchedTransfer_Busy_WithinGrace_Waits()
		{
			Assert.AreEqual(GroupFinderRules.MatchedTransferAction.Wait, GroupFinderRules.ResolveMatchedTransfer(false, 59.9, 60.0));
		}

		[Test]
		public void MatchedTransfer_Busy_AtGrace_GivesUp()
		{
			Assert.AreEqual(GroupFinderRules.MatchedTransferAction.GiveUp, GroupFinderRules.ResolveMatchedTransfer(false, 60.0, 60.0));
		}

		// ── Rules summary ───────────────────────────────────────────────────

		[Test]
		public void RulesSummary_MentionsPinnedFinderSizeOnly()
		{
			StringAssert.Contains("Find Group forms parties of 3.", Difficulty(finderSize: 3).BuildRulesSummary());
			StringAssert.DoesNotContain("Find Group", Difficulty().BuildRulesSummary());
			StringAssert.DoesNotContain("Find Group", Difficulty(enabled: false, finderSize: 3).BuildRulesSummary());
		}
	}
}
