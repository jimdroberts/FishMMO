using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FishMMO.Database.Data;
using FishMMO.Server.Implementation.World.SceneServer;
using FishMMO.Shared;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The guild and party update pumps and the party vitals pump, after the 2026-09-25 server
	/// hot-path audit (findings H7, M15, M16, L1, L25).
	/// </summary>
	/// <remarks>
	/// The watermark rule and the buff-delivery ledger are pure, so they are exercised directly.
	/// What the pumps DO with them — read in bulk, multicast once, compare the controller's guild
	/// with the roster's — needs a server and a database to observe, so those are pinned as text
	/// scans in the style of <c>AuditFollowUpPinsTests</c>: what each exists to prevent is the old
	/// shape quietly coming back in a refactor.
	/// </remarks>
	[TestFixture]
	public class SocialUpdatePumpTests
	{
		private const string GuildSystemPath = "Assets/Scripts/Server/Implementation/World/SceneServer/Guild/GuildSystem.cs";
		private const string PartySystemPath = "Assets/Scripts/Server/Implementation/World/SceneServer/Party/PartySystem.cs";
		private const string GuildAuthorityPath = "Assets/Scripts/Server/Implementation/World/SceneServer/Guild/GuildSystem.Authority.cs";
		private const string GuildControllerPath = "Assets/Scripts/Shared/Implementation/Entity/Guild/GuildController.cs";
		private const string PartyPanelPath = "Assets/Scripts/Client/GUI/World/Party/UITKParty.cs";
		private const string LoginSnapshotPath = "Assets/Scripts/Server/Implementation/World/SceneServer/Character/CharacterSystem.Social.cs";

		private static readonly DateTime Now = new DateTime(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);

		#region UpdatePumpWatermark

		[Test]
		public void Classify_ARead_IsProcessed_WhateverItsAge()
		{
			DateTime horizon = Now.AddSeconds(-60);
			LogAssert.AreEqual(UpdatePumpWatermark.Outcome.Processed, UpdatePumpWatermark.Classify(true, Now, horizon));
			LogAssert.AreEqual(UpdatePumpWatermark.Outcome.Processed, UpdatePumpWatermark.Classify(true, horizon.AddHours(-1), horizon),
				"a snapshot that was read is delivered, however old its update");
		}

		[Test]
		public void Classify_AnUnreadUpdate_IsRetriedInsideTheHorizon_AndReleasedOutsideIt()
		{
			DateTime horizon = Now.AddSeconds(-60);
			LogAssert.AreEqual(UpdatePumpWatermark.Outcome.Retry, UpdatePumpWatermark.Classify(false, Now.AddSeconds(-1), horizon));
			LogAssert.AreEqual(UpdatePumpWatermark.Outcome.Retry, UpdatePumpWatermark.Classify(false, horizon, horizon),
				"the horizon itself is still inside it");
			LogAssert.AreEqual(UpdatePumpWatermark.Outcome.GiveUp, UpdatePumpWatermark.Classify(false, horizon.AddTicks(-1), horizon),
				"an update unreadable for longer than the horizon must stop holding the mark");
		}

		[Test]
		public void Hold_OnlyARetry_PullsTheMarkBack_AndNeverForward()
		{
			DateTime mark = Now.AddSeconds(-5);
			DateTime older = Now.AddSeconds(-30);
			DateTime newer = Now.AddSeconds(-1);

			LogAssert.AreEqual(older, UpdatePumpWatermark.Hold(mark, UpdatePumpWatermark.Outcome.Retry, older));
			LogAssert.AreEqual(mark, UpdatePumpWatermark.Hold(mark, UpdatePumpWatermark.Outcome.Retry, newer),
				"a retry newer than the mark is already covered by it");
			LogAssert.AreEqual(mark, UpdatePumpWatermark.Hold(mark, UpdatePumpWatermark.Outcome.Processed, older));
			LogAssert.AreEqual(mark, UpdatePumpWatermark.Hold(mark, UpdatePumpWatermark.Outcome.GiveUp, older),
				"a released update must not hold the mark — that is what pinned the guild pump (H7)");
		}

		[Test]
		public void RetryHorizon_IsTenTimesTheSkew_WithAMinuteFloor()
		{
			LogAssert.AreEqual(TimeSpan.FromSeconds(60), UpdatePumpWatermark.RetryHorizon(5.0f));
			LogAssert.AreEqual(TimeSpan.FromSeconds(60), UpdatePumpWatermark.RetryHorizon(0.0f));
			LogAssert.AreEqual(TimeSpan.FromSeconds(100), UpdatePumpWatermark.RetryHorizon(10.0f));
		}

		[Test]
		public void ProcessedRecords_OutliveEveryWindowInWhichTheirUpdateCanBeFetched()
		{
			/* An update stays fetchable while the mark can sit at or before it: the horizon, plus
			 * the skew the mark trails by, plus a pass — and the record is stamped by the database's
			 * clock but aged by this server's, which can add the skew again. A record swept inside
			 * that span turns the next fetch into a re-read and re-send. */
			foreach (float skew in new[] { 0.0f, 1.0f, 5.0f, 6.0f, 30.0f })
			{
				TimeSpan window = UpdatePumpWatermark.RetryHorizon(skew) + TimeSpan.FromSeconds(2 * skew + 2);
				LogAssert.IsTrue(UpdatePumpWatermark.ProcessedRecordLifetime(skew) > window,
					$"skew {skew}s: records must be kept for longer than {window.TotalSeconds}s");
			}
		}

		[Test]
		public void FetchStarted_TrailsTheClockByTheSkew_AndNeverLeadsIt()
		{
			LogAssert.AreEqual(Now.AddSeconds(-5), UpdatePumpWatermark.FetchStarted(Now, 5.0f));
			LogAssert.AreEqual(Now, UpdatePumpWatermark.FetchStarted(Now, -3.0f),
				"a negative allowance must not push the mark ahead of the clock");
			LogAssert.AreEqual(DateTimeKind.Utc, UpdatePumpWatermark.FetchStarted(DateTime.SpecifyKind(Now, DateTimeKind.Unspecified), 5.0f).Kind);
		}

		[Test]
		public void AGuildThatNeverReads_HoldsTheMarkOnlyUntilTheHorizon()
		{
			/* H7 as a sequence of passes. One guild's update at t0 can never be read. Under the old
			 * rule the mark stopped at t0 on every pass for ever; under this one it is held while the
			 * update is worth retrying and released once it is not. */
			const float skew = 5.0f;
			TimeSpan horizon = UpdatePumpWatermark.RetryHorizon(skew);
			DateTime t0 = Now;

			DateTime MarkAfterPass(DateTime passClock)
			{
				DateTime fetchStarted = UpdatePumpWatermark.FetchStarted(passClock, skew);
				UpdatePumpWatermark.Outcome outcome = UpdatePumpWatermark.Classify(false, t0, fetchStarted - horizon);
				return UpdatePumpWatermark.Hold(fetchStarted, outcome, t0);
			}

			LogAssert.AreEqual(t0, MarkAfterPass(t0.AddSeconds(10)), "held while the update is young");
			LogAssert.AreEqual(t0, MarkAfterPass(t0.AddSeconds(30)), "still held inside the horizon");

			DateTime late = t0.Add(horizon).AddSeconds(skew + 1);
			LogAssert.AreEqual(UpdatePumpWatermark.FetchStarted(late, skew), MarkAfterPass(late),
				"released past the horizon, so every guild behind it stops being re-read");
		}

		#endregion

		#region ObservedBuffDeliveryLedger

		[Test]
		public void Ledger_SendsToAnAudienceThatHasNotHadTheSet_ThenStops()
		{
			var ledger = new ObservedBuffDeliveryLedger();
			var audience = new List<long> { 1, 2 };

			LogAssert.IsTrue(ledger.NeedsSend(audience, 1, 42), "nobody has been sent anything yet");
			ledger.MarkSent(audience, 1, 42);
			LogAssert.IsFalse(ledger.NeedsSend(audience, 1, 42), "an unchanged set is omitted once everybody holds it");
			LogAssert.IsTrue(ledger.NeedsSend(audience, 1, 43), "a changed set goes out");
		}

		[Test]
		public void Ledger_AJoiner_IsSentAnUnchangedPermanentBuff()
		{
			/* L1: the record was keyed by the member described, so somebody joining the group after
			 * a permanent buff was last sent never received it. The ledger asks about each
			 * recipient, so the joiner's absence from the record sends the array. */
			var ledger = new ObservedBuffDeliveryLedger();
			ledger.MarkSent(new List<long> { 1, 2 }, 1, 42);

			LogAssert.IsTrue(ledger.NeedsSend(new List<long> { 1, 2, 3 }, 1, 42),
				"recipient 3 has never been sent member 1's set");

			ledger.MarkSent(new List<long> { 1, 2, 3 }, 1, 42);
			LogAssert.IsFalse(ledger.NeedsSend(new List<long> { 1, 2, 3 }, 1, 42));
		}

		[Test]
		public void Ledger_ARecipientWhoMissedAChange_IsSentItOnReturn()
		{
			var ledger = new ObservedBuffDeliveryLedger();
			ledger.MarkSent(new List<long> { 1, 2 }, 1, 42);

			// Recipient 2 is in another scene when member 1's buffs change.
			ledger.MarkSent(new List<long> { 1 }, 1, 43);

			LogAssert.IsTrue(ledger.NeedsSend(new List<long> { 1, 2 }, 1, 43),
				"recipient 2 still holds the old set");
		}

		[Test]
		public void Ledger_ForgettingARecipient_ResendsEverythingToThem_AndOnlyThem()
		{
			var ledger = new ObservedBuffDeliveryLedger();
			ledger.MarkSent(new List<long> { 1, 2 }, 1, 42);
			ledger.MarkSent(new List<long> { 1, 2 }, 2, 7);

			ledger.ForgetRecipient(2);

			LogAssert.IsTrue(ledger.NeedsSend(new List<long> { 2 }, 1, 42), "a forgotten client holds nothing");
			LogAssert.IsTrue(ledger.NeedsSend(new List<long> { 2 }, 2, 7));
			LogAssert.IsFalse(ledger.NeedsSend(new List<long> { 1 }, 1, 42), "other recipients are unaffected");
			LogAssert.AreEqual(1, ledger.RecipientCount);

			ledger.ForgetRecipient(99);
			LogAssert.AreEqual(1, ledger.RecipientCount, "forgetting a stranger is a no-op");

			ledger.Clear();
			LogAssert.AreEqual(0, ledger.RecipientCount);
			LogAssert.IsTrue(ledger.NeedsSend(new List<long> { 1 }, 1, 42));
		}

		[Test]
		public void Ledger_AnEmptyAudience_NeverNeedsASend()
		{
			var ledger = new ObservedBuffDeliveryLedger();
			LogAssert.IsFalse(ledger.NeedsSend(new List<long>(), 1, 42));
			LogAssert.IsFalse(ledger.NeedsSend(null, 1, 42));
		}

		#endregion

		#region Guild ladder helper

		[Test]
		public void LeaderRankOrderOf_IsTheHighestOrderThatExists()
		{
			var ladder = new List<GuildRankData>()
			{
				new GuildRankData(1, 1, 10, 1, "Member", 0),
				new GuildRankData(2, 1, 10, 4, "Leader", -1),
				new GuildRankData(3, 1, 10, 2, "Officer", 5),
			};

			LogAssert.AreEqual((byte)4, GuildSystem.LeaderRankOrderOf(ladder));
			LogAssert.AreEqual((byte)0, GuildSystem.LeaderRankOrderOf(new List<GuildRankData>()));
			LogAssert.AreEqual((byte)0, GuildSystem.LeaderRankOrderOf(null));
		}

		#endregion

		#region GuildRosterDelta

		private static GuildAddEntry Row(long id, string location = "Town", byte rank = 1, string officerNote = "", long lastOnline = 100)
		{
			return new GuildAddEntry()
			{
				CharacterID = id,
				RankOrder = rank,
				Location = location,
				RaceID = 3,
				PublicNote = "hello",
				OfficerNote = officerNote,
				LastOnlineUnixSeconds = lastOnline,
			};
		}

		[Test]
		public void RowChanged_SeesEveryDisplayedColumn()
		{
			GuildAddEntry before = Row(1);
			LogAssert.IsFalse(GuildRosterDelta.RowChanged(before, Row(1), true), "an identical row is unchanged");

			GuildAddEntry moved = Row(1, location: "Dungeon");
			LogAssert.IsTrue(GuildRosterDelta.RowChanged(before, moved, false), "a location change is seen by everybody");

			LogAssert.IsTrue(GuildRosterDelta.RowChanged(before, Row(1, rank: 2), false), "a rank change is seen by everybody");

			GuildAddEntry noted = before;
			noted.PublicNote = "bye";
			LogAssert.IsTrue(GuildRosterDelta.RowChanged(before, noted, false), "a public note is seen by everybody");

			GuildAddEntry raced = before;
			raced.RaceID = 4;
			LogAssert.IsTrue(GuildRosterDelta.RowChanged(before, raced, false));
		}

		[Test]
		public void RowChanged_AnOfficerNote_ChangesOnlyTheOfficerCopy()
		{
			GuildAddEntry before = Row(1, officerNote: "old");
			GuildAddEntry after = Row(1, officerNote: "new");

			LogAssert.IsTrue(GuildRosterDelta.RowChanged(before, after, officerAudience: true));
			LogAssert.IsFalse(GuildRosterDelta.RowChanged(before, after, officerAudience: false),
				"the public copy carries the note empty, so nothing it holds changed");
		}

		[Test]
		public void RowChanged_LastSeen_CountsOnlyWhileOffline()
		{
			LogAssert.IsFalse(GuildRosterDelta.RowChanged(Row(1, "Town", lastOnline: 100), Row(1, "Town", lastOnline: 200), true),
				"an online member's autosave moves last-seen and must not put them in every delta");
			LogAssert.IsTrue(GuildRosterDelta.RowChanged(Row(1, "Offline", lastOnline: 100), Row(1, "Offline", lastOnline: 200), true),
				"an offline member's last-seen is displayed, so it is sent");
			LogAssert.IsTrue(GuildRosterDelta.RowChanged(Row(1, "Offline", lastOnline: 100), Row(1, "offline", lastOnline: 200), true),
				"the offline label is compared without regard to case");
			LogAssert.IsTrue(GuildRosterDelta.RowChanged(Row(1, "Town", lastOnline: 100), Row(1, "Offline", lastOnline: 100), true),
				"going offline is a location change, which sends the row with its last-seen");
		}

		[Test]
		public void Diff_ReportsAddedChangedAndRemovedRows()
		{
			var previous = new Dictionary<long, GuildAddEntry>()
			{
				{ 1, Row(1) },
				{ 2, Row(2) },
				{ 3, Row(3) },
			};
			var current = new List<GuildAddEntry>() { Row(1), Row(2, location: "Dungeon"), Row(4) };
			var changed = new List<int>();
			var removed = new List<long>();

			GuildRosterDelta.Diff(previous, current, false, changed, removed);

			LogAssert.AreEqual("1,2", string.Join(",", changed), "row 2 changed (index 1) and row 4 is new (index 2); row 1 did not");
			LogAssert.AreEqual("3", string.Join(",", removed));

			GuildRosterDelta.Diff(null, current, false, changed, removed);
			LogAssert.AreEqual("0,1,2", string.Join(",", changed), "against no previous roster every row is new");
			LogAssert.AreEqual(0, removed.Count);
		}

		[Test]
		public void Choose_SendsWholeWithoutABaseline_NothingWhenUnchanged_AndWholeAboveHalf()
		{
			LogAssert.AreEqual(GuildRosterDelta.Delivery.Full, GuildRosterDelta.Choose(false, 0, 0, 10), "no baseline: nothing to apply a delta to");
			LogAssert.AreEqual(GuildRosterDelta.Delivery.None, GuildRosterDelta.Choose(true, 0, 0, 10));
			LogAssert.AreEqual(GuildRosterDelta.Delivery.Delta, GuildRosterDelta.Choose(true, 1, 0, 10));
			LogAssert.AreEqual(GuildRosterDelta.Delivery.Delta, GuildRosterDelta.Choose(true, 3, 2, 10), "exactly half is still a delta");
			LogAssert.AreEqual(GuildRosterDelta.Delivery.Full, GuildRosterDelta.Choose(true, 4, 2, 10), "above half goes whole");
			LogAssert.AreEqual(GuildRosterDelta.Delivery.Full, GuildRosterDelta.Choose(true, 0, 1, 0), "an emptied guild goes whole");
		}

		[Test]
		public void SameLadder_ComparesEveryRankInOrder()
		{
			GuildRankEntry[] a = { new GuildRankEntry() { RankOrder = 1, Name = "M", Permissions = 0 }, new GuildRankEntry() { RankOrder = 2, Name = "L", Permissions = 7 } };
			GuildRankEntry[] same = { new GuildRankEntry() { RankOrder = 1, Name = "M", Permissions = 0 }, new GuildRankEntry() { RankOrder = 2, Name = "L", Permissions = 7 } };
			GuildRankEntry[] renamed = { new GuildRankEntry() { RankOrder = 1, Name = "M", Permissions = 0 }, new GuildRankEntry() { RankOrder = 2, Name = "Boss", Permissions = 7 } };
			GuildRankEntry[] regranted = { new GuildRankEntry() { RankOrder = 1, Name = "M", Permissions = 1 }, new GuildRankEntry() { RankOrder = 2, Name = "L", Permissions = 7 } };

			LogAssert.IsTrue(GuildRosterDelta.SameLadder(a, same));
			LogAssert.IsFalse(GuildRosterDelta.SameLadder(a, renamed));
			LogAssert.IsFalse(GuildRosterDelta.SameLadder(a, regranted));
			LogAssert.IsFalse(GuildRosterDelta.SameLadder(a, new[] { a[0] }));
			LogAssert.IsFalse(GuildRosterDelta.SameLadder(a, null));
		}

		[Test]
		public void ProjectRosterEntry_EmptiesTheOfficerNote_OnlyForThePublicCopy()
		{
			GuildAddEntry full = Row(1, officerNote: "secret");
			LogAssert.AreEqual("secret", GuildSystem.ProjectRosterEntry(full, true).OfficerNote);
			LogAssert.AreEqual(string.Empty, GuildSystem.ProjectRosterEntry(full, false).OfficerNote);
			LogAssert.AreEqual("hello", GuildSystem.ProjectRosterEntry(full, false).PublicNote);
		}

		#endregion

		#region GuildRecipientBaselines

		[Test]
		public void Baselines_ARoster_IsHeldOnlyForItsGuildAndAudience()
		{
			var baselines = new GuildRecipientBaselines();
			LogAssert.IsFalse(baselines.HasRoster(7, 10, false), "nobody holds anything to begin with");

			baselines.MarkRoster(7, 10, false);
			LogAssert.IsTrue(baselines.HasRoster(7, 10, false));
			LogAssert.IsFalse(baselines.HasRoster(7, 10, true),
				"promoted into reading officer notes: the public copy is no baseline for the officer copy");
			LogAssert.IsFalse(baselines.HasRoster(7, 11, false), "another guild's roster is no baseline");
		}

		[Test]
		public void Baselines_ALadder_IsHeldOnlyForItsGenerationAndRank()
		{
			var baselines = new GuildRecipientBaselines();
			baselines.MarkLadder(7, 10, 5, 2);

			LogAssert.IsTrue(baselines.HasLadder(7, 10, 5, 2));
			LogAssert.IsFalse(baselines.HasLadder(7, 10, 6, 2), "the ladder moved");
			LogAssert.IsFalse(baselines.HasLadder(7, 10, 5, 3), "the viewer's own rank moved");
			LogAssert.IsFalse(baselines.HasLadder(7, 11, 5, 2), "another guild");
		}

		[Test]
		public void Baselines_Reset_ForgetsExactlyWhatTheClientMayHaveDropped()
		{
			var baselines = new GuildRecipientBaselines();
			baselines.MarkRoster(7, 10, false);
			baselines.MarkLadder(7, 10, 5, 2);
			baselines.MarkRoster(8, 10, true);
			baselines.MarkLadder(8, 10, 5, 3);

			baselines.ForgetLadder(7);
			LogAssert.IsTrue(baselines.HasRoster(7, 10, false), "a rank list sent elsewhere leaves the roster baseline alone");
			LogAssert.IsFalse(baselines.HasLadder(7, 10, 5, 2), "...and resets the ladder baseline");

			baselines.Forget(8, 11);
			LogAssert.IsTrue(baselines.HasRoster(8, 10, true) && baselines.HasLadder(8, 10, 5, 3),
				"leaving another guild does not forget this one");

			baselines.Forget(8, 10);
			LogAssert.IsFalse(baselines.HasRoster(8, 10, true) || baselines.HasLadder(8, 10, 5, 3), "leaving this guild forgets both");

			baselines.MarkLadder(7, 10, 5, 2);
			baselines.Forget(7);
			LogAssert.AreEqual(0, baselines.RosterCount + baselines.LadderCount, "a disconnect forgets everything");

			baselines.MarkRoster(9, 10, false);
			baselines.Clear();
			LogAssert.AreEqual(0, baselines.RosterCount);
		}

		#endregion

		#region Buff signature (L1)

		private static ObservedBuffEntry Buff(int template, int stacks, float remaining)
		{
			return new ObservedBuffEntry() { TemplateID = template, Stacks = stacks, RemainingSeconds = remaining, TotalSeconds = 30.0f };
		}

		[Test]
		public void BuffSignature_IgnoresTheCountdown_AndSeesARefresh()
		{
			var set = new List<ObservedBuffEntry>() { Buff(5, 1, 20.0f), Buff(9, 2, 0.0f) };
			var later = new List<ObservedBuffEntry>() { Buff(5, 1, 11.5f), Buff(9, 2, 0.0f) };
			var expiry = new List<long>() { 1000, -1 };

			int signature = PartySystem.ComputeObservedBuffSignature(set, expiry);
			LogAssert.AreEqual(signature, PartySystem.ComputeObservedBuffSignature(later, expiry),
				"a buff counting down is unchanged: the client counts it down itself (L1)");
			LogAssert.AreNotEqual(signature, PartySystem.ComputeObservedBuffSignature(set, new List<long>() { 1010, -1 }),
				"a refresh moves the expiry and is sent");
			LogAssert.AreNotEqual(signature, PartySystem.ComputeObservedBuffSignature(new List<ObservedBuffEntry>() { Buff(5, 2, 20.0f), Buff(9, 2, 0.0f) }, expiry),
				"a stack change is sent");
			LogAssert.AreEqual(0, PartySystem.ComputeObservedBuffSignature(new List<ObservedBuffEntry>(), new List<long>()));
			LogAssert.AreNotEqual(0, signature, "a real set never collides with the empty one");
		}

		[Test]
		public void BuffExpirySecond_IsWholeSecondsOfTheExpiryTick()
		{
			LogAssert.AreEqual(-1L, PartySystem.BuffExpirySecond(0u, 1.0 / 30.0), "a permanent buff has no expiry");
			LogAssert.AreEqual(100L, PartySystem.BuffExpirySecond(3000u, 1.0 / 30.0));
			LogAssert.AreEqual(100L, PartySystem.BuffExpirySecond(3029u, 1.0 / 30.0), "whole seconds");
			LogAssert.AreEqual(101L, PartySystem.BuffExpirySecond(3030u, 1.0 / 30.0));
			LogAssert.AreEqual(143165576L, PartySystem.BuffExpirySecond(4294967295u, 1.0 / 30.0),
				"double precision: a float step would lose whole seconds at large ticks");
			LogAssert.AreEqual(3000L, PartySystem.BuffExpirySecond(3000u, 0.0), "an unknown tick rate hashes the tick itself");
		}

		#endregion

		#region Pump pins

		[Test]
		public void GuildPump_ReadsInBulk_AndUsesTheSharedWatermarkRule()
		{
			string body = CodeOnly(MethodBody(ReadSource(GuildSystemPath), "private async Task FetchAndProcessGuildUpdatesAsync("));

			LogAssert.IsTrue(body.Contains("charGuildService.FetchManyAsync(pendingIDs)"), "rosters for every changed guild in one query (H7)");
			LogAssert.IsTrue(body.Contains("rankService.FetchManyAsync(pendingIDs)"), "ladders for every changed guild in one query (H7)");
			LogAssert.IsFalse(body.Contains("charGuildService.FetchManyAsync(update.GuildID)"), "no per-guild roster read in the pump");
			LogAssert.IsTrue(body.Contains("HasProcessedGuildUpdate("), "delivered updates are skipped, not re-sent");
			LogAssert.IsTrue(body.Contains("UpdatePumpWatermark.Classify("), "the retry horizon is the shared rule");
			LogAssert.IsTrue(body.Contains("UpdatePumpWatermark.FetchStarted("), "the mark is taken before the query, less the skew");
			LogAssert.IsTrue(body.Contains("MarkGuildUpdateProcessed("), "delivered updates are recorded");
		}

		[Test]
		public void GuildDelivery_ChecksTheRostersGuild_AndMulticastsOncePerAudience()
		{
			string source = ReadSource(GuildSystemPath);
			string body = CodeOnly(MethodBody(source, "private void ApplyGuildSnapshot("));
			string copy = CodeOnly(MethodBody(source, "private void DeliverRosterCopy("));

			LogAssert.IsTrue(body.Contains("guildController.ID != guildID"),
				"a member is refreshed and told only when their controller names THIS guild (L25)");
			LogAssert.IsFalse(body.Contains("guildController.ID < 1"),
				"\"in any guild\" is the wrong question: it reaches somebody who has moved guild");
			LogAssert.IsFalse(body.Contains("Broadcast(character.Owner") || copy.Contains("Broadcast(character.Owner"),
				"no per-member unicast of the roster or the rank list (M16)");
			LogAssert.IsTrue(copy.Contains("Broadcast(delta.Connections, new GuildRosterDeltaBroadcast()"), "a holder of the roster is sent a delta (M16)");
			LogAssert.IsTrue(copy.Contains("Broadcast(full.Connections, ProjectRoster("), "anybody else is sent the roster whole, once per copy");
			LogAssert.IsTrue(copy.Contains("guildRecipientBaselines.MarkRoster("), "a full roster sent is recorded as held");
			LogAssert.IsTrue(body.Contains("guildRecipientBaselines.HasLadder("), "the rank list goes only to a client without the current ladder (M16)");
			LogAssert.IsTrue(body.Contains("RemoveGuildCharacterTracker(guildID, memberID)"),
				"an evicted member leaves the local tracker, or the guild is polled for ever");

			int ladder = body.IndexOf("AdvanceDeliveredLadder(", StringComparison.Ordinal);
			int loop = body.IndexOf("foreach (CharacterGuildData member in dbMembers)", StringComparison.Ordinal);
			LogAssert.IsTrue(ladder >= 0 && loop > ladder,
				"the ladder's wire array is built once per guild, before the member loop, not inside it (M16)");
			LogAssert.IsFalse(body.Contains("BuildRankEntries("), "and never again inside the delivery");
		}

		[Test]
		public void GuildBaselines_AreForgottenWheneverTheClientMayHaveDroppedWhatTheyRecord()
		{
			string source = ReadSource(GuildSystemPath);

			LogAssert.IsTrue(CodeOnly(MethodBody(source, "public void CharacterSystem_OnDisconnect(")).Contains("guildRecipientBaselines.Forget(character.ID)"),
				"a disconnect forgets both baselines");
			LogAssert.IsTrue(CodeOnly(MethodBody(source, "private void ClearGuildStanding(")).Contains("guildRecipientBaselines.Forget(member.ID, guildID)"),
				"a cleared standing forgets this guild's baselines");
			LogAssert.IsTrue(CodeOnly(MethodBody(source, "public void RemoveGuildCharacterTracker(")).Contains("guildRecipientBaselines.Forget(characterID, guildID)"),
				"leaving, a kick and an eviction all untrack, and untracking forgets this guild's baselines");
			LogAssert.IsTrue(CodeOnly(MethodBody(source, "public void AddGuildCharacterTracker(")).Contains("guildRecipientBaselines.Forget(characterID)"),
				"joining or connecting starts from no baseline");
			LogAssert.IsTrue(CodeOnly(MethodBody(ReadSource(GuildAuthorityPath), "private void SendGuildRankList(")).Contains("guildRecipientBaselines.ForgetLadder(characterID)"),
				"a rank list sent by any other path resets that recipient's ladder baseline");
			LogAssert.IsTrue(CodeOnly(ReadSource(LoginSnapshotPath)).Contains("guildSystem.ForgetGuildDeliveryBaselines(characterID)"),
				"the login snapshot is a roster sent outside the pump, so it resets both baselines");
		}

		[Test]
		public void GuildController_AppliesADelta_WithoutRejoining()
		{
			string body = CodeOnly(MethodBody(ReadSource(GuildControllerPath), "public void OnClientGuildRosterDeltaBroadcastReceived("));

			LogAssert.IsTrue(body.Contains("msg.GuildID != ID"), "a delta for another guild is stale and ignored");
			LogAssert.IsFalse(body.Contains("OnClientGuildAddBroadcastReceived("),
				"not routed through the add handler, which treats our own row as a join");
			LogAssert.IsFalse(body.Contains("onGuildJoinTriggers"), "our own row changing is not a join");
			LogAssert.IsTrue(body.Contains("RankOrder = entry.RankOrder"), "our own row updates our rank order");
			LogAssert.IsTrue(body.Contains("OnAddGuildMember?.Invoke("), "each upsert is an add-or-update");
			LogAssert.IsTrue(body.Contains("OnRemoveGuildMember?.Invoke("), "each removal removes the row");
		}

		[Test]
		public void PartyPanel_RebasesItsCountdown_OnlyWhenAnArrayArrives()
		{
			string source = CodeOnly(ReadSource(PartyPanelPath));
			int branch = source.IndexOf("if (entry.BuffsChanged)", StringComparison.Ordinal);
			LogAssert.IsTrue(branch >= 0, "the array branch must still exist");

			string arrayBranch = MethodBody(source.Substring(branch), "if (entry.BuffsChanged)");
			LogAssert.IsTrue(arrayBranch.Contains("model.BuffsReceivedTime = now;"),
				"the countdown's starting point moves with the array (L1)");
			LogAssert.AreEqual(1, Occurrences(source, "model.BuffsReceivedTime = now;"),
				"and nowhere else: stamping it on every payload snaps timed icons back each second");
		}

		[Test]
		public void PartyPump_ReadsRostersAndOnlineMembersInBulk()
		{
			string source = ReadSource(PartySystemPath);
			string pump = CodeOnly(MethodBody(source, "private async Task FetchAndProcessPartyUpdatesAsync("));
			string absent = CodeOnly(MethodBody(source, "private async Task<PartyLeadershipRepair> RepairAbsentLeaderAsync("));
			string audit = CodeOnly(MethodBody(source, "private async Task AuditPartyLeadershipAsync("));

			LogAssert.IsTrue(pump.Contains("charPartyService.FetchManyAsync(pendingIDs)"), "one roster query per pump (M15)");
			LogAssert.IsFalse(pump.Contains("charPartyService.FetchManyAsync(update.PartyID)"), "no per-party roster read in the pump (M15)");
			LogAssert.IsTrue(pump.Contains("FetchOnlineMembersForLeadershipAsync("), "one online query per pump (M15)");
			LogAssert.IsFalse(absent.Contains("FetchOnlineMemberIdsAsync("),
				"the absent-leader repair is handed the online set; it must not query per party (M15)");
			LogAssert.IsTrue(audit.Contains("charPartyService.FetchManyAsync(ids)"), "the audit sweep reads in bulk too");
		}

		[Test]
		public void PartyVitals_AreOneMulticastPerSceneGroup_WithAPerRecipientBuffLedger()
		{
			string body = CodeOnly(MethodBody(ReadSource(PartySystemPath), "private void BroadcastSceneGroupVitals("));

			LogAssert.IsTrue(body.Contains("Server.NetworkWrapper.Broadcast(vitalsRecipients,"), "one multicast per scene group");
			LogAssert.IsFalse(body.Contains("Broadcast(members[i].Owner"), "no per-member unicast of the same payload");
			LogAssert.IsTrue(body.Contains("observedBuffLedger.NeedsSend(vitalsRecipientIDs"),
				"whether an array may be omitted is a question about each recipient (L1)");
		}

		#endregion

		#region Helpers

		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path).Replace("\r\n", "\n");
		}

		/// <summary>The source with comment lines dropped, so a scan sees code and not prose.</summary>
		private static string CodeOnly(string source)
		{
			string[] lines = source.Split('\n');
			StringBuilder code = new StringBuilder(source.Length);

			for (int i = 0; i < lines.Length; ++i)
			{
				string trimmed = lines[i].TrimStart();
				if (trimmed.StartsWith("//", StringComparison.Ordinal) ||
					trimmed.StartsWith("/*", StringComparison.Ordinal) ||
					trimmed.StartsWith("*", StringComparison.Ordinal))
				{
					continue;
				}

				code.Append(lines[i]).Append('\n');
			}

			return code.ToString();
		}

		private static int Occurrences(string source, string needle)
		{
			int count = 0;
			for (int i = source.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = source.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
			{
				++count;
			}
			return count;
		}

		/// <summary>A method's body, brace-matched from the first brace after its signature.</summary>
		private static string MethodBody(string source, string signature)
		{
			int start = source.IndexOf(signature, StringComparison.Ordinal);
			LogAssert.IsTrue(start >= 0, $"the source must still declare {signature}");

			int open = source.IndexOf('{', start);
			LogAssert.IsTrue(open > start, $"{signature} must have a body");

			int depth = 0;
			for (int i = open; i < source.Length; ++i)
			{
				if (source[i] == '{')
				{
					++depth;
				}
				else if (source[i] == '}')
				{
					--depth;
					if (depth == 0)
					{
						return source.Substring(open, i - open + 1);
					}
				}
			}

			LogAssert.Fail($"{signature}: unbalanced braces");
			return string.Empty;
		}

		#endregion
	}
}
