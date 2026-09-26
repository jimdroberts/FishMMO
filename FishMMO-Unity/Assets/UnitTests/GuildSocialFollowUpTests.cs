using System;
using NUnit.Framework;
using FishMMO.Shared;
using FishMMO.Server.Implementation.World.SceneServer;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Guild follow-ups to the hot-path fix pass: join triggers only on a real join (O15), the
	/// pump's existence reads (O19), and the rank-ladder publish grouped by rank (O20).
	/// </summary>
	[TestFixture]
	public class GuildSocialFollowUpTests
	{
		private const string GuildSystemPath = "Assets/Scripts/Server/Implementation/World/SceneServer/Guild/GuildSystem.cs";
		private const string GuildAuthorityPath = "Assets/Scripts/Server/Implementation/World/SceneServer/Guild/GuildSystem.Authority.cs";
		private const string GuildControllerPath = "Assets/Scripts/Shared/Implementation/Entity/Guild/GuildController.cs";

		// ── O15: join triggers ────────────────────────────────────────────────

		[Test]
		public void AJoinNoticeIsAJoinOnlyForAGuildNotAlreadyHeld()
		{
			LogAssert.IsTrue(GuildController.IsNewMembership(0, 7), "guildless, told of guild 7: a join");
			LogAssert.IsTrue(GuildController.IsNewMembership(3, 7), "in guild 3, told of guild 7: a join");
			LogAssert.IsFalse(GuildController.IsNewMembership(7, 7), "already in guild 7: not a join");
			LogAssert.IsFalse(GuildController.IsNewMembership(0, 0), "no guild announced: not a join");
			LogAssert.IsFalse(GuildController.IsNewMembership(7, 0), "no guild announced: not a join");
		}

		[Test]
		public void ARosterNeverFiresTheJoinTriggers()
		{
			string code = SourceScanPins.ReadCode(GuildControllerPath);
			Func<string, string> check = c =>
			{
				string roster = SourceScanPins.Body(c, "public void OnClientGuildAddMultipleBroadcastReceived(");
				string row = SourceScanPins.Body(c, "private void ApplyRosterRow(");
				string notice = SourceScanPins.Body(c, "public void OnClientGuildAddBroadcastReceived(");
				if (roster == null || row == null || notice == null)
				{
					return "a guild add handler is missing";
				}
				if (roster.Contains("OnClientGuildAddBroadcastReceived(") || roster.Contains("isJoinNotice: true"))
				{
					return "a roster's rows go through the join notice again";
				}
				if (!roster.Contains("isJoinNotice: false"))
				{
					return "a roster's rows no longer say they are not a join notice";
				}
				if (!notice.Contains("isJoinNotice: true"))
				{
					return "the join notice no longer says it is one";
				}
				if (!row.Contains("isJoinNotice && IsNewMembership(ID, msg.GuildID)"))
				{
					return "the join is no longer decided from the notice and the guild held BEFORE the ID is overwritten";
				}
				return SourceScanPins.InOrder(row, "IsNewMembership(ID, msg.GuildID)", "ID = msg.GuildID;", "if (joined)", "onGuildJoinTriggers");
			};
			SourceScanPins.HoldsAndFires("O15 roster", code, check,
				SourceScanPins.Replace("}, isJoinNotice: false);", "}, isJoinNotice: true);"),
				"every roster firing the join triggers for our own row");
		}

		// ── O19: existence reads ──────────────────────────────────────────────

		[Test]
		public void VanishedGuildsAreThoseAskedAboutAndNotFound()
		{
			var vanished = GuildSystem.VanishedGuilds(new long[] { 1, 2, 3, 0, -4 }, new long[] { 2 });
			LogAssert.AreEqual(2, vanished.Count, "1 and 3; a non-ID is never reported gone");
			LogAssert.IsTrue(vanished.Contains(1) && vanished.Contains(3));
			LogAssert.AreEqual(0, GuildSystem.VanishedGuilds(new long[] { 5 }, new long[] { 5 }).Count, "all present: none gone");
			LogAssert.AreEqual(0, GuildSystem.VanishedGuilds(null, new long[] { 5 }).Count, "nothing asked: none gone");
			LogAssert.AreEqual(1, GuildSystem.VanishedGuilds(new long[] { 5 }, null).Count, "nothing found: everything asked is gone");
		}

		[Test]
		public void ThePumpNoLongerAsksAfterEveryTrackedGuild()
		{
			string code = SourceScanPins.ReadCode(GuildSystemPath);
			Func<string, string> check = c =>
			{
				string pump = SourceScanPins.Body(c, "private async Task FetchAndProcessGuildUpdatesAsync(");
				if (pump == null)
				{
					return "the pump is missing";
				}
				if (pump.Contains("FetchExistingIdsAsync(guildIds)"))
				{
					return "the pump reads existence for every tracked guild on every pass";
				}
				if (!pump.Contains("FetchVanishedGuildsAsync(emptyRosters)"))
				{
					return "the pump no longer checks the guilds whose roster came back empty";
				}
				string loop = pump.Substring(pump.IndexOf("FetchVanishedGuildsAsync(emptyRosters)", StringComparison.Ordinal));
				return SourceScanPins.InOrder(loop, "vanishedGuilds.Contains(guildID)", "FetchOrSeedLadderAsync(");
			};
			SourceScanPins.HoldsAndFires("O19 pump", code, check,
				SourceScanPins.InsertBefore("if (fetchResult.Data.Updates == null || fetchResult.Data.Updates.Count < 1)",
					"_ = TryGetDbService(out IGuildService every) ? every.FetchExistingIdsAsync(guildIds) : null;\n"),
				"an existence read for every tracked guild per pass");
		}

		[Test]
		public void ADisbandElsewhereIsFoundByTheSlowSweep()
		{
			string code = SourceScanPins.ReadCode(GuildSystemPath);
			Func<string, string> check = c =>
			{
				string init = SourceScanPins.Body(c, "public override ServerComponentInitializationStatus InitializeOnce(");
				string deinit = SourceScanPins.Body(c, "public override void OnDeinitialize(");
				string sweep = SourceScanPins.Body(c, "private async Task SweepVanishedGuildsAsync(");
				if (init == null || deinit == null || sweep == null)
				{
					return "the sweep or the lifecycle is missing";
				}
				if (!init.Contains("RegisterPeriodicCallback(guildExistenceSweepSeconds, OnPeriodicGuildExistenceSweep)"))
				{
					return "the sweep is not registered";
				}
				if (!deinit.Contains("UnregisterPeriodicCallback(OnPeriodicGuildExistenceSweep)"))
				{
					return "the sweep is not unregistered";
				}
				string order = SourceScanPins.InOrder(sweep, "FetchExistingIdsAsync(guildIds)", "if (!existing.IsSuccess", "guildExistenceSweepFaults.ReportSuccess()", "VanishedGuilds(", "ClearLocalGuildMembers(");
				if (order != null)
				{
					return order;
				}
				return sweep.Contains("guildExistenceSweepInFlight, 0)") ? null : "the sweep's in-flight flag is never released";
			};
			SourceScanPins.HoldsAndFires("O19 sweep", code, check,
				SourceScanPins.Replace("periodicSystem.RegisterPeriodicCallback(guildExistenceSweepSeconds, OnPeriodicGuildExistenceSweep);", string.Empty),
				"no sweep, so a guild disbanded on another server is never noticed");
		}

		// ── O20: the ladder publish ───────────────────────────────────────────

		[Test]
		public void TheLadderPublishSendsOneCopyPerRankAndRecordsWhatItSent()
		{
			string authority = SourceScanPins.ReadCode(GuildAuthorityPath);
			Func<string, string> check = c =>
			{
				string publish = SourceScanPins.Body(c, "private async Task PublishGuildRankLadderAsync(");
				string deliver = SourceScanPins.Body(c, "private void DeliverPublishedLadder(");
				if (publish == null || deliver == null)
				{
					return "the publish or its delivery is missing";
				}
				if (publish.Contains("SendGuildRankList(") || deliver.Contains("SendGuildRankList("))
				{
					return "the publish sends each member their own message again";
				}
				if (deliver.Contains("Broadcast(owner") || deliver.Contains("Broadcast(member.Owner"))
				{
					return "the publish unicasts a rank list";
				}
				if (deliver.Contains("ForgetLadder("))
				{
					return "the publish forgets the baselines it could record";
				}
				string order = SourceScanPins.InOrder(deliver, "AdvanceDeliveredLadder(", "guildController.ID != guildID", "guildRecipientBaselines.HasLadder(", "RankListAudience(membership.Rank)", "DeliverRankListAudiences(");
				return order;
			};
			SourceScanPins.HoldsAndFires("O20 publish", authority, check,
				SourceScanPins.Replace("DeliverRankListAudiences(guildID, ladder, rankEntries, generation, leaderRankOrder);",
					"foreach (var m in roster) { SendGuildRankList(null, default); }"),
				"one message per member");
		}

		[Test]
		public void ThePumpAndThePublishRecordTheirRankListsTheSameWay()
		{
			string code = SourceScanPins.ReadCode(GuildSystemPath);
			Func<string, string> check = c =>
			{
				string helper = SourceScanPins.Body(c, "private void DeliverRankListAudiences(");
				string snapshot = SourceScanPins.Body(c, "private void ApplyGuildSnapshot(");
				if (helper == null || snapshot == null)
				{
					return "the shared rank-list delivery is missing";
				}
				if (!snapshot.Contains("DeliverRankListAudiences(guildID, ladder, rankEntries, ladderGeneration, leaderRankOrder)"))
				{
					return "the pump no longer delivers rank lists through the shared helper";
				}
				return SourceScanPins.InOrder(helper, "Broadcast(audience.Value.Connections, new GuildRankListBroadcast()", "guildRecipientBaselines.MarkLadder(");
			};
			SourceScanPins.HoldsAndFires("O20 shared", code, check,
				SourceScanPins.Replace("guildRecipientBaselines.MarkLadder(audience.Value.CharacterIDs[i], guildID, generation, audience.Key);", string.Empty),
				"a rank list sent without its baseline recorded");
		}
	}
}
