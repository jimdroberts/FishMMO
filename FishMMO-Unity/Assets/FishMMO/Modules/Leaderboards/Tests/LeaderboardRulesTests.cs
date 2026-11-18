using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Client;
using FishMMO.Database.Data;
using FishMMO.Database.Data.Enums;
using FishMMO.Server.Implementation.World.SceneServer;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The pure rules behind the leaderboards (issue #261): paging, how a reply is composed from a
	/// cached read, how a board maps onto a database query, board order, and how a panel paces its
	/// requests against the server's one-in-flight guard.
	/// </summary>
	/// <remarks>
	/// The SQL itself — eligibility, ties, bigint scores, offsets — is proved against PostgreSQL by
	/// the service's own probe, not here; nothing in this fixture touches a database.
	/// </remarks>
	[TestFixture]
	public class LeaderboardRulesTests
	{
		private static readonly DateTime Now = new DateTime(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);

		private readonly List<Object> created = new List<Object>();
		private readonly List<ICachedObject> cached = new List<ICachedObject>();

		[TearDown]
		public void TearDown()
		{
			foreach (ICachedObject c in cached)
			{
				c.RemoveFromCache();
			}
			cached.Clear();
			foreach (Object o in created)
			{
				Object.DestroyImmediate(o);
			}
			created.Clear();
		}

		private T Create<T>(string name) where T : ScriptableObject
		{
			T instance = ScriptableObject.CreateInstance<T>();
			instance.name = name;
			created.Add(instance);
			return instance;
		}

		private void Cache(ICachedObject obj, string name)
		{
			obj.AddToCache(name);
			cached.Add(obj);
		}

		// ── Paging ──────────────────────────────────────────────────────────────────────────

		[Test]
		public void PageCount_CoversTheBoard_UpToTheBrowsableDepth()
		{
			LogAssert.AreEqual(1, LeaderboardPaging.PageCount(0, 1000), "an empty board still has the page that says so");
			LogAssert.AreEqual(1, LeaderboardPaging.PageCount(25, 1000), "exactly one full page");
			LogAssert.AreEqual(2, LeaderboardPaging.PageCount(26, 1000), "one row over starts a second page");
			LogAssert.AreEqual(40, LeaderboardPaging.PageCount(5000, 1000), "a big board is browsable to rank 1,000 and no further");
			LogAssert.AreEqual(1, LeaderboardPaging.PageCount(5000, 3), "a depth below one page still allows the first page");
			LogAssert.AreEqual(int.MaxValue / LeaderboardPaging.PageSize + 1, LeaderboardPaging.PageCount(int.MaxValue, int.MaxValue), "and nothing overflows at the extremes");
		}

		[Test]
		public void Pages_AreClampedAndOffsetFromOne()
		{
			LogAssert.AreEqual(40, LeaderboardPaging.MaxPage(1000));
			LogAssert.AreEqual(1, LeaderboardPaging.ClampPage(0, 40), "no page zero");
			LogAssert.AreEqual(1, LeaderboardPaging.ClampPage(-5, 40), "or below");
			LogAssert.AreEqual(40, LeaderboardPaging.ClampPage(41, 40), "no page past the end");
			LogAssert.AreEqual(0, LeaderboardPaging.Offset(1), "page one starts at the top");
			LogAssert.AreEqual(50, LeaderboardPaging.Offset(3), "page three starts at the 51st row");
		}

		// ── Composing a reply ───────────────────────────────────────────────────────────────

		private static LeaderboardPageData Page(int offset, int total, params (int rank, long id, long score)[] rows)
		{
			var list = new List<LeaderboardRowData>();
			foreach (var r in rows)
			{
				list.Add(new LeaderboardRowData(r.rank, r.id, "C" + r.id, r.score, 0, 0));
			}
			return new LeaderboardPageData(0, string.Empty, total, offset, list, Now.AddSeconds(-12));
		}

		[Test]
		public void TheRequestersStanding_IsTakenFromThePage_WhenTheyAreOnIt()
		{
			LeaderboardPageData page = Page(0, 4, (1, 8, 5_000_000_000L), (2, 1, 100), (2, 2, 100), (4, 3, 50));

			LeaderboardStandingData? tied = LeaderboardSystem.FindOnPage(page, 2);
			LogAssert.IsTrue(tied.HasValue && tied.Value.Ranked, "character 2 is on the page");
			LogAssert.AreEqual(2, tied.Value.Rank, "with the rank its own row carries, shared with its tie");
			LogAssert.AreEqual(100L, tied.Value.Score);

			LogAssert.IsFalse(LeaderboardSystem.FindOnPage(page, 99).HasValue, "someone not on it is looked up separately");
			LogAssert.IsFalse(LeaderboardSystem.FindOnPage(null, 2).HasValue, "and no page means no row");
		}

		[Test]
		public void AReply_CarriesThePageTheReadsAgeAndTheRequestersStanding()
		{
			LeaderboardPageData page = Page(25, 1234, (26, 40, 900), (26, 41, 900), (28, 42, 850));
			var standing = new LeaderboardStandingData(true, 311, 120, 0, 0, Now);

			LeaderboardPageBroadcast reply = LeaderboardSystem.ComposePage(77, 2, page, standing, 1000, Now);

			LogAssert.AreEqual(77, reply.TemplateID);
			LogAssert.AreEqual(2, reply.Page);
			LogAssert.AreEqual(1234, reply.TotalRanked, "every character on the board is counted");
			LogAssert.AreEqual(40, reply.PageCount, "but only 1,000 ranks are browsable");
			LogAssert.AreEqual(12, reply.AgeSeconds, "the read's age travels with it");
			LogAssert.AreEqual(3, reply.Entries.Length);
			LogAssert.AreEqual(26, reply.Entries[1].Rank, "ranks come from the database, ties included");
			LogAssert.AreEqual("C41", reply.Entries[1].CharacterName);
			LogAssert.AreEqual(311, reply.YourRank, "a player far down the board is told where they are");
			LogAssert.AreEqual(120L, reply.YourScore);
			LogAssert.IsFalse(reply.Unavailable);
		}

		[Test]
		public void AnUnrankedRequester_IsToldSo_NotRankedZero()
		{
			LeaderboardPageBroadcast reply = LeaderboardSystem.ComposePage(1, 1, Page(0, 0), LeaderboardStandingData.Unranked(Now), 1000, Now);
			LogAssert.AreEqual(0, reply.YourRank, "0 is the wire's 'not on the board'");
			LogAssert.AreEqual(0L, reply.YourScore);
			LogAssert.AreEqual(0, reply.Entries.Length, "an empty board is an empty page, not a failure");
			LogAssert.AreEqual(1, reply.PageCount);
		}

		// ── Mapping a board onto a query ────────────────────────────────────────────────────

		[Test]
		public void EachSource_MapsOntoItsQuery()
		{
			LeaderboardTemplate arena = Create<LeaderboardTemplate>("Arena Board");
			arena.Source = LeaderboardSource.ArenaSeasonRating;
			arena.MinimumGames = 10;
			LogAssert.IsTrue(LeaderboardSystem.TryBuildQuery(arena, false, out LeaderboardQuery q));
			LogAssert.AreEqual(LeaderboardSourceKind.ArenaSeasonRating, q.Source);
			LogAssert.AreEqual(10, q.MinimumGames, "the placement floor is the board's");
			LogAssert.IsFalse(q.RankStaff, "staff are left off unless the server says otherwise");

			CharacterAttributeTemplate rank = Create<CharacterAttributeTemplate>("Test PvP Rank");
			Cache(rank, rank.name);
			LeaderboardTemplate attribute = Create<LeaderboardTemplate>("Attribute Board");
			attribute.Source = LeaderboardSource.CharacterAttribute;
			attribute.AttributeTemplate = rank;
			LogAssert.IsTrue(LeaderboardSystem.TryBuildQuery(attribute, true, out q));
			LogAssert.AreEqual(LeaderboardSourceKind.CharacterAttribute, q.Source);
			LogAssert.AreEqual(rank.ID, q.TemplateID, "the attribute's own cached ID, which is what the save wrote");
			LogAssert.IsTrue(q.RankStaff);

			AchievementTemplate kills = Create<AchievementTemplate>("Test Kills");
			Cache(kills, kills.name);
			LeaderboardTemplate achievement = Create<LeaderboardTemplate>("Achievement Board");
			achievement.Source = LeaderboardSource.Achievement;
			achievement.AchievementTemplate = kills;
			LogAssert.IsTrue(LeaderboardSystem.TryBuildQuery(achievement, false, out q));
			LogAssert.AreEqual(LeaderboardSourceKind.CharacterAchievement, q.Source);
			LogAssert.AreEqual(kills.ID, q.TemplateID);
		}

		[Test]
		public void ABoardMissingItsSource_IsRefused_NotReadAsTemplateZero()
		{
			LeaderboardTemplate noSource = Create<LeaderboardTemplate>("No Source");
			LogAssert.IsFalse(LeaderboardSystem.TryBuildQuery(noSource, false, out _), "no source");

			LeaderboardTemplate noReference = Create<LeaderboardTemplate>("No Reference");
			noReference.Source = LeaderboardSource.Achievement;
			LogAssert.IsFalse(LeaderboardSystem.TryBuildQuery(noReference, false, out _), "an achievement board with no achievement");

			AchievementTemplate uncached = Create<AchievementTemplate>("Never Loaded");
			noReference.AchievementTemplate = uncached;
			LogAssert.IsFalse(LeaderboardSystem.TryBuildQuery(noReference, false, out _), "a reference that never received an ID is refused, not read as template 0");

			LogAssert.IsFalse(LeaderboardSystem.TryBuildQuery(null, false, out _), "and no board at all");
		}

		// ── Board order ─────────────────────────────────────────────────────────────────────

		[Test]
		public void Boards_ListInAuthoredOrder_WithinTheirCategory()
		{
			LeaderboardTemplate b = Create<LeaderboardTemplate>("LB Test B");
			b.Source = LeaderboardSource.ArenaSeasonRating;
			b.SortOrder = 10;
			LeaderboardTemplate a = Create<LeaderboardTemplate>("LB Test A");
			a.Source = LeaderboardSource.ArenaSeasonRating;
			a.SortOrder = 10;
			LeaderboardTemplate first = Create<LeaderboardTemplate>("LB Test Z");
			first.Source = LeaderboardSource.ArenaSeasonRating;
			first.SortOrder = 0;
			LeaderboardTemplate pve = Create<LeaderboardTemplate>("LB Test PvE");
			pve.Source = LeaderboardSource.ArenaSeasonRating;
			pve.Category = LeaderboardCategory.PvE;
			LeaderboardTemplate broken = Create<LeaderboardTemplate>("LB Test Broken");
			broken.Source = LeaderboardSource.Achievement;
			foreach (LeaderboardTemplate t in new[] { b, a, first, pve, broken })
			{
				Cache(t, t.name);
			}

			List<LeaderboardTemplate> pvp = LeaderboardTemplate.InCategory(LeaderboardCategory.PvP).FindAll(t => created.Contains(t));
			LogAssert.AreEqual(3, pvp.Count, "the PvE board and the unconfigured one are not listed");
			LogAssert.AreSame(first, pvp[0], "sort order first");
			LogAssert.AreSame(a, pvp[1], "then name, so every peer agrees");
			LogAssert.AreSame(b, pvp[2]);
		}

		// ── Request pacing ──────────────────────────────────────────────────────────────────

		private static LeaderboardPageBroadcast Reply(int template, int page, bool unavailable = false)
		{
			return new LeaderboardPageBroadcast { TemplateID = template, Page = page, Unavailable = unavailable };
		}

		[Test]
		public void ARequest_IsSentOnce_AndOwedItsReply()
		{
			var requester = new LeaderboardRequester();
			var sent = new List<(int, int)>();
			requester.Want(5, 1);

			requester.Tick(10.0f, (t, p) => sent.Add((t, p)));
			requester.Tick(10.5f, (t, p) => sent.Add((t, p)));
			LogAssert.AreEqual(1, sent.Count, "one request, however many frames pass while it is owed");
			LogAssert.IsTrue(requester.Waiting);

			LogAssert.IsTrue(requester.Accept(Reply(5, 1)), "its reply is the page wanted");
			LogAssert.IsFalse(requester.Waiting);
			requester.Tick(20.0f, (t, p) => sent.Add((t, p)));
			LogAssert.AreEqual(1, sent.Count, "an answered page is not asked for again");
		}

		[Test]
		public void ClicksWhileWaiting_AreFoldedIntoTheNextRequest()
		{
			var requester = new LeaderboardRequester();
			var sent = new List<(int, int)>();
			void Send(int t, int p) => sent.Add((t, p));

			requester.Want(5, 1);
			requester.Tick(10.0f, Send);
			requester.Want(5, 2);
			requester.Want(5, 3);
			requester.Tick(10.1f, Send);
			LogAssert.AreEqual(1, sent.Count, "nothing is sent while page 1 is still owed; the server would drop it");

			LogAssert.IsFalse(requester.Accept(Reply(5, 1)), "page 1's reply is no longer what the panel shows");
			requester.Tick(10.2f, Send);
			LogAssert.AreEqual(1, sent.Count, "and the next request still waits out the minimum interval");
			requester.Tick(10.0f + LeaderboardRequester.MinIntervalSeconds + 0.01f, Send);
			LogAssert.AreEqual(2, sent.Count, "then one request goes");
			LogAssert.AreEqual((5, 3), sent[1], "for the page last asked for, not the ones clicked past");
		}

		[Test]
		public void ALostReply_IsAskedForAgain_ThenGivenUp()
		{
			var requester = new LeaderboardRequester();
			int sends = 0;
			requester.Want(5, 1);

			float t = 0f;
			for (int i = 0; i < 20; ++i)
			{
				requester.Tick(t, (_, __) => ++sends);
				t += LeaderboardRequester.RetrySeconds + 0.01f;
			}

			LogAssert.AreEqual(LeaderboardRequester.MaxAttempts, sends, "re-asked a bounded number of times");
			LogAssert.IsTrue(requester.GaveUp, "then reported, so the panel stops saying Loading");
			LogAssert.IsFalse(requester.Waiting);

			requester.Refresh();
			requester.Tick(t, (_, __) => ++sends);
			LogAssert.AreEqual(LeaderboardRequester.MaxAttempts + 1, sends, "selecting the board again tries again");
		}

		[Test]
		public void Replies_AreMatchedByBoardAndPage()
		{
			var requester = new LeaderboardRequester();
			requester.Want(5, 2);
			requester.Tick(0f, (_, __) => { });

			LogAssert.IsFalse(requester.Accept(Reply(6, 2)), "another board's page 2 is not this board's");
			LogAssert.IsFalse(requester.Accept(Reply(5, 1)), "this board's page 1 is not page 2");
			LogAssert.IsTrue(requester.Accept(Reply(5, 0, unavailable: true)), "an unavailable board answers every page of it");

			var other = new LeaderboardRequester();
			other.Want(5, 2);
			LogAssert.IsTrue(other.Accept(Reply(5, 2)), "a reply another panel asked for is the same rows, and is used");
		}
	}
}
