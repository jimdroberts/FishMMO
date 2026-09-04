using System.Collections.Generic;
using FishMMO.Database.Data;
using FishMMO.Shared;
using NUnit.Framework;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Pins the arena's pure rules: how matches are composed from the queue, when a match is over
	/// and who won, how players place, what a result does to a rank, how teams are seated, and how
	/// the team registry decides who may hit whom.
	/// </summary>
	public class ArenaRulesTests
	{
		private static ArenaTemplate Template(int teamCount = 2, params int[] teamSizes)
		{
			var template = UnityEngine.ScriptableObject.CreateInstance<ArenaTemplate>();
			template.TeamCount = teamCount;
			template.Formats = new List<ArenaFormat>();
			foreach (int size in teamSizes)
			{
				template.Formats.Add(new ArenaFormat { TeamSize = size });
			}
			return template;
		}

		// ── Composer ────────────────────────────────────────────────────────

		private static ArenaCandidate C(long id, long group = 0) => new ArenaCandidate(id, id * 10, group);

		[Test]
		public void Compose_TooFew_Fails()
		{
			Assert.IsFalse(ArenaMatchComposer.TryCompose(new[] { C(1), C(2), C(3) }, 2, 2, out _));
		}

		[Test]
		public void Compose_Solos_FillTeamsInOrder()
		{
			Assert.IsTrue(ArenaMatchComposer.TryCompose(new[] { C(1), C(2), C(3), C(4) }, 2, 2, out List<ArenaSeat> seats));
			Assert.AreEqual(4, seats.Count);
			// First-fit: 1 and 2 on team 0, 3 and 4 on team 1.
			Assert.AreEqual(0, seats.Find(s => s.RowID == 1).Team);
			Assert.AreEqual(0, seats.Find(s => s.RowID == 2).Team);
			Assert.AreEqual(1, seats.Find(s => s.RowID == 3).Team);
			Assert.AreEqual(1, seats.Find(s => s.RowID == 4).Team);
		}

		[Test]
		public void Compose_PremadeGroup_StaysTogether()
		{
			// A pre-made pair queued second still lands on one team.
			var candidates = new[] { C(1), C(2, group: 7), C(3, group: 7), C(4) };
			Assert.IsTrue(ArenaMatchComposer.TryCompose(candidates, 2, 2, out List<ArenaSeat> seats));
			int teamOf2 = seats.Find(s => s.RowID == 2).Team;
			Assert.AreEqual(teamOf2, seats.Find(s => s.RowID == 3).Team);
			Assert.AreNotEqual(teamOf2, seats.Find(s => s.RowID == 1).Team);
			Assert.AreNotEqual(teamOf2, seats.Find(s => s.RowID == 4).Team);
		}

		[Test]
		public void Compose_GroupLargerThanTeam_IsSkipped()
		{
			// The trio cannot be seated in 2v2; the four solos behind them can.
			var candidates = new[] { C(1, 9), C(2, 9), C(3, 9), C(4), C(5), C(6), C(7) };
			Assert.IsTrue(ArenaMatchComposer.TryCompose(candidates, 2, 2, out List<ArenaSeat> seats));
			CollectionAssert.AreEquivalent(new long[] { 4, 5, 6, 7 }, seats.ConvertAll(s => s.RowID));
		}

		[Test]
		public void Compose_NeverReturnsUnevenTeams()
		{
			// Two trios and one solo cannot make 2v2 evenly... but two trios make 3v3.
			var candidates = new[] { C(1, 1), C(2, 1), C(3, 1), C(4, 2), C(5, 2), C(6, 2), C(7) };
			Assert.IsFalse(ArenaMatchComposer.TryCompose(candidates, 2, 2, out _));
			Assert.IsTrue(ArenaMatchComposer.TryCompose(candidates, 2, 3, out List<ArenaSeat> seats));
			Assert.AreEqual(6, seats.Count);
			Assert.IsFalse(seats.Exists(s => s.RowID == 7));
		}

		// ── Match size and pre-made fit ─────────────────────────────────────

		[Test]
		public void MatchSize_FromFormat_CappedByScene()
		{
			ArenaTemplate t = Template(2, 1, 3, 8);
			Assert.AreEqual(2, ArenaRules.ResolveMatchSize(t, 0, 50));
			Assert.AreEqual(6, ArenaRules.ResolveMatchSize(t, 1, 50));
			Assert.AreEqual(0, ArenaRules.ResolveMatchSize(t, 2, 10), "16 players do not fit a 10-player scene");
			Assert.AreEqual(0, ArenaRules.ResolveMatchSize(t, 5, 50), "unknown format");
			Assert.AreEqual(0, ArenaRules.ResolveMatchSize(null, 0, 50));
		}

		[Test]
		public void GroupFitsFormat_OnlySameSizeOrSmaller()
		{
			Assert.IsTrue(ArenaRules.GroupFitsFormat(2, 2));
			Assert.IsTrue(ArenaRules.GroupFitsFormat(2, 4));
			Assert.IsFalse(ArenaRules.GroupFitsFormat(3, 2));
		}

		// ── Outcome ─────────────────────────────────────────────────────────

		[Test]
		public void Outcome_ScoreLimit_EndsAtOnce()
		{
			Assert.IsTrue(ArenaRules.ResolveOutcome(new[] { 20, 5 }, 20, false, 2, out int winner));
			Assert.AreEqual(0, winner);
		}

		[Test]
		public void Outcome_NotYet()
		{
			Assert.IsFalse(ArenaRules.ResolveOutcome(new[] { 3, 5 }, 20, false, 2, out _));
		}

		[Test]
		public void Outcome_TimeUp_HigherWins_TieDraws()
		{
			Assert.IsTrue(ArenaRules.ResolveOutcome(new[] { 3, 5 }, 20, true, 2, out int winner));
			Assert.AreEqual(1, winner);
			Assert.IsTrue(ArenaRules.ResolveOutcome(new[] { 4, 4 }, 20, true, 2, out winner));
			Assert.AreEqual(-1, winner);
		}

		[Test]
		public void Outcome_Walkover_WhenOneTeamLeft()
		{
			Assert.IsTrue(ArenaRules.ResolveOutcome(new[] { 0, 9 }, 20, false, 1, out int winner));
			Assert.AreEqual(-2, winner, "-2 asks the caller to name the team still standing");
		}

		// ── Placements ──────────────────────────────────────────────────────

		[Test]
		public void Placements_ByScoreThenKillsThenFewerDeaths_IgnoringTeam()
		{
			var members = new List<ArenaPlacement>
			{
				new ArenaPlacement { CharacterID = 1, Team = 0, Score = 5, Kills = 5, Deaths = 2 },
				new ArenaPlacement { CharacterID = 2, Team = 1, Score = 7, Kills = 7, Deaths = 9 },
				new ArenaPlacement { CharacterID = 3, Team = 1, Score = 5, Kills = 5, Deaths = 1 },
			};
			List<ArenaPlacement> placed = ArenaRules.ResolvePlacements(members);
			Assert.AreEqual(2, placed[0].CharacterID);
			Assert.AreEqual(1, placed[0].Place);
			Assert.AreEqual(3, placed[1].CharacterID, "fewer deaths breaks the tie");
			Assert.AreEqual(1, placed[2].CharacterID);
			Assert.AreEqual(3, placed[2].Place);
		}

		// ── Rank ────────────────────────────────────────────────────────────

		[Test]
		public void Rank_WinLossDraw_NeverBelowZero()
		{
			Assert.AreEqual(10, ArenaRules.ResolveRankDelta(0, 0, 0, 10, 5));
			Assert.AreEqual(-5, ArenaRules.ResolveRankDelta(40, 1, 0, 10, 5));
			Assert.AreEqual(-3, ArenaRules.ResolveRankDelta(3, 1, 0, 10, 5), "a rank of 3 loses only 3");
			Assert.AreEqual(0, ArenaRules.ResolveRankDelta(40, 1, -1, 10, 5), "a draw moves nothing");
		}

		// ── Capture the Flag ────────────────────────────────────────────────

		[Test]
		public void Flag_EnemyTakesHomeFlag()
		{
			Assert.AreEqual(ArenaFlagAction.PickUp, ArenaRules.ResolveFlagInteraction(standTeam: 0, ArenaFlagState.Home, actorTeam: 1, actorCarriesFlag: false));
		}

		[Test]
		public void Flag_EnemyCannotTakeCarriedFlag_OrASecondOne()
		{
			Assert.AreEqual(ArenaFlagAction.None, ArenaRules.ResolveFlagInteraction(0, ArenaFlagState.Carried, 1, false));
			Assert.AreEqual(ArenaFlagAction.None, ArenaRules.ResolveFlagInteraction(0, ArenaFlagState.Home, 1, actorCarriesFlag: true));
		}

		[Test]
		public void Flag_OwnerCapturesOnlyWhileOwnFlagIsHome()
		{
			Assert.AreEqual(ArenaFlagAction.Capture, ArenaRules.ResolveFlagInteraction(0, ArenaFlagState.Home, 0, actorCarriesFlag: true));
			Assert.AreEqual(ArenaFlagAction.None, ArenaRules.ResolveFlagInteraction(0, ArenaFlagState.Carried, 0, actorCarriesFlag: true));
			Assert.AreEqual(ArenaFlagAction.None, ArenaRules.ResolveFlagInteraction(0, ArenaFlagState.Home, 0, actorCarriesFlag: false));
		}

		[Test]
		public void Flag_UnseatedDoesNothing()
		{
			Assert.AreEqual(ArenaFlagAction.None, ArenaRules.ResolveFlagInteraction(0, ArenaFlagState.Home, -1, false));
		}

		// ── King of the Hill ────────────────────────────────────────────────

		[Test]
		public void ControlPoint_ProgressAccumulates_ThenFlips()
		{
			ArenaControlPointResult r = ArenaRules.ResolveControlPointInteraction(ownerTeam: -1, progressTeam: -1, progress: 0, actorTeam: 1, interactionsToCapture: 3);
			Assert.IsFalse(r.Captured); Assert.AreEqual(1, r.ProgressTeam); Assert.AreEqual(1, r.Progress); Assert.AreEqual(-1, r.OwnerTeam);
			r = ArenaRules.ResolveControlPointInteraction(-1, 1, 1, 1, 3);
			Assert.IsFalse(r.Captured); Assert.AreEqual(2, r.Progress);
			r = ArenaRules.ResolveControlPointInteraction(-1, 1, 2, 1, 3);
			Assert.IsTrue(r.Captured); Assert.AreEqual(1, r.OwnerTeam); Assert.AreEqual(-1, r.ProgressTeam); Assert.AreEqual(0, r.Progress);
		}

		[Test]
		public void ControlPoint_ContestRestartsProgress()
		{
			ArenaControlPointResult r = ArenaRules.ResolveControlPointInteraction(-1, 1, 2, actorTeam: 0, interactionsToCapture: 3);
			Assert.IsFalse(r.Captured); Assert.AreEqual(0, r.ProgressTeam); Assert.AreEqual(1, r.Progress);
		}

		[Test]
		public void ControlPoint_OwnerTouchingItDoesNothing()
		{
			ArenaControlPointResult r = ArenaRules.ResolveControlPointInteraction(ownerTeam: 1, progressTeam: -1, progress: 0, actorTeam: 1, interactionsToCapture: 3);
			Assert.IsFalse(r.Captured); Assert.AreEqual(1, r.OwnerTeam); Assert.AreEqual(0, r.Progress);
		}

		// ── Spawns ──────────────────────────────────────────────────────────

		[Test]
		public void TeamSpawns_ByPrefix_FallBackToAll()
		{
			var keys = new[] { "Team1_A", "Team1_B", "Team2_A", "Centre" };
			CollectionAssert.AreEquivalent(new[] { "Team1_A", "Team1_B" }, ArenaRules.ResolveTeamSpawnKeys(keys, "Team1"));
			CollectionAssert.AreEquivalent(keys, ArenaRules.ResolveTeamSpawnKeys(keys, "Team9"));
			CollectionAssert.AreEquivalent(keys, ArenaRules.ResolveTeamSpawnKeys(keys, null));
		}
	}
}
