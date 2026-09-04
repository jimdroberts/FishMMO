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

		// ── Composer: rating band and balance ───────────────────────────────

		private static ArenaCandidate R(long id, int rating, long group = 0) => new ArenaCandidate(id, id * 10, group, rating);

		[Test]
		public void Compose_Band_ExcludesFarRatings()
		{
			// Anchor 1500; 2200 is outside a 300 band, so only three fit and no 2v2 forms.
			var candidates = new[] { R(1, 1500), R(2, 2200), R(3, 1600), R(4, 1400) };
			Assert.IsFalse(ArenaMatchComposer.TryCompose(candidates, 2, 2, new ArenaComposeOptions(300, false), out _));
			// A wider band admits them.
			Assert.IsTrue(ArenaMatchComposer.TryCompose(candidates, 2, 2, new ArenaComposeOptions(800, false), out _));
		}

		[Test]
		public void Compose_Band_AnchoredOnLongestWaiter()
		{
			// The first in line is the anchor even when everyone else clusters elsewhere.
			var candidates = new[] { R(1, 2400), R(2, 1500), R(3, 1500), R(4, 1500), R(5, 1500) };
			Assert.IsFalse(ArenaMatchComposer.TryCompose(candidates, 2, 2, new ArenaComposeOptions(200, false), out _));
		}

		[Test]
		public void Compose_Balance_SplitsHighAndLow()
		{
			// Two strong, two weak: balancing puts one strong on each side.
			var candidates = new[] { R(1, 2000), R(2, 2000), R(3, 1000), R(4, 1000) };
			Assert.IsTrue(ArenaMatchComposer.TryCompose(candidates, 2, 2, new ArenaComposeOptions(0, true), out List<ArenaSeat> seats));
			int team0 = 0, team1 = 0;
			foreach (ArenaSeat seat in seats)
			{
				int rating = seat.RowID <= 2 ? 2000 : 1000;
				if (seat.Team == 0) team0 += rating; else team1 += rating;
			}
			Assert.AreEqual(team0, team1);
		}

		[Test]
		public void Compose_Balance_KeepsGroupsTogether()
		{
			var candidates = new[] { R(1, 1900, group: 7), R(2, 1100, group: 7), R(3, 1500), R(4, 1500) };
			Assert.IsTrue(ArenaMatchComposer.TryCompose(candidates, 2, 2, new ArenaComposeOptions(0, true), out List<ArenaSeat> seats));
			int groupTeam = -1;
			foreach (ArenaSeat seat in seats)
			{
				if (seat.RowID == 1 || seat.RowID == 2)
				{
					if (groupTeam < 0) groupTeam = seat.Team;
					Assert.AreEqual(groupTeam, seat.Team, "a pre-made pair was split");
				}
			}
		}

		[Test]
		public void Compose_Balance_StillFirstCome()
		{
			// Six waiters, four seats: the first four play, whatever the ratings of the last two.
			var candidates = new[] { R(1, 1000), R(2, 1000), R(3, 1000), R(4, 1000), R(5, 3000), R(6, 3000) };
			Assert.IsTrue(ArenaMatchComposer.TryCompose(candidates, 2, 2, new ArenaComposeOptions(0, true), out List<ArenaSeat> seats));
			foreach (ArenaSeat seat in seats)
			{
				Assert.LessOrEqual(seat.RowID, 4);
			}
		}

		// ── Rating ──────────────────────────────────────────────────────────

		[Test]
		public void Rating_Expected_IsSymmetric()
		{
			Assert.AreEqual(0.5, ArenaRating.Expected(1500, 1500), 1e-9);
			Assert.AreEqual(1.0, ArenaRating.Expected(1700, 1500) + ArenaRating.Expected(1500, 1700), 1e-9);
			Assert.Greater(ArenaRating.Expected(1700, 1500), 0.5);
		}

		[Test]
		public void Rating_Delta_EqualOpponents_HalfK()
		{
			Assert.AreEqual(16, ArenaRating.Delta(1500, 1500, 1.0, 32));
			Assert.AreEqual(-16, ArenaRating.Delta(1500, 1500, 0.0, 32));
			Assert.AreEqual(0, ArenaRating.Delta(1500, 1500, 0.5, 32));
		}

		[Test]
		public void Rating_Delta_UpsetPaysMore()
		{
			int underdogWin = ArenaRating.Delta(1200, 1800, 1.0, 32);
			int favouriteWin = ArenaRating.Delta(1800, 1200, 1.0, 32);
			Assert.Greater(underdogWin, favouriteWin);
			Assert.GreaterOrEqual(favouriteWin, 1, "a win is never worth nothing");
		}

		[Test]
		public void Rating_KFactor_PlacementThenSettled()
		{
			Assert.AreEqual(64, ArenaRating.KFactor(0, 10, 32, 64));
			Assert.AreEqual(64, ArenaRating.KFactor(9, 10, 32, 64));
			Assert.AreEqual(32, ArenaRating.KFactor(10, 10, 32, 64));
			Assert.AreEqual(3, ArenaRating.PlacementGamesRemaining(7, 10));
			Assert.AreEqual(0, ArenaRating.PlacementGamesRemaining(12, 10));
		}

		[Test]
		public void Rating_Resolve_TeamMatesMoveTogether_AndFloorHolds()
		{
			var members = new List<(long, int, int, int)> { (1, 0, 1500, 20), (2, 0, 1500, 20), (3, 1, 1500, 20), (4, 1, 110, 20) };
			var result = ArenaRating.Resolve(members, winnerTeam: 0, placementGames: 10, k: 32, placementK: 64);
			Assert.AreEqual(4, result.Count);
			Assert.AreEqual(result[0].delta, result[1].delta, "team-mates get the same change");
			Assert.Greater(result[0].delta, 0);
			Assert.Less(result[2].delta, 0);
			Assert.GreaterOrEqual(result[3].newRating, ArenaRating.MinimumRating, "the floor holds");
		}

		[Test]
		public void Rating_Resolve_Draw_IsHalf()
		{
			var members = new List<(long, int, int, int)> { (1, 0, 1500, 20), (2, 1, 1500, 20) };
			var result = ArenaRating.Resolve(members, winnerTeam: -1, placementGames: 10, k: 32, placementK: 64);
			Assert.AreEqual(0, result[0].delta);
			Assert.AreEqual(0, result[1].delta);
		}

		[Test]
		public void Rating_Band_WidensWithWaitAndCaps()
		{
			Assert.AreEqual(0, ArenaRating.ResolveBand(0, 5, 100, 1000), "0 base disables the band");
			Assert.AreEqual(150, ArenaRating.ResolveBand(150, 5, 0, 1000));
			Assert.AreEqual(450, ArenaRating.ResolveBand(150, 5, 60, 1000));
			Assert.AreEqual(1000, ArenaRating.ResolveBand(150, 5, 6000, 1000));
		}

		// ── Dropped flags ───────────────────────────────────────────────────

		[Test]
		public void DroppedFlag_OwnerReturns_EnemyPicksUp_CarrierCannot()
		{
			Assert.AreEqual(ArenaFlagAction.Return, ArenaRules.ResolveDroppedFlagTouch(flagTeam: 0, actorTeam: 0, actorCarriesFlag: false));
			Assert.AreEqual(ArenaFlagAction.Return, ArenaRules.ResolveDroppedFlagTouch(flagTeam: 0, actorTeam: 0, actorCarriesFlag: true));
			Assert.AreEqual(ArenaFlagAction.PickUp, ArenaRules.ResolveDroppedFlagTouch(flagTeam: 0, actorTeam: 1, actorCarriesFlag: false));
			Assert.AreEqual(ArenaFlagAction.None, ArenaRules.ResolveDroppedFlagTouch(flagTeam: 0, actorTeam: 1, actorCarriesFlag: true));
			Assert.AreEqual(ArenaFlagAction.None, ArenaRules.ResolveDroppedFlagTouch(flagTeam: 0, actorTeam: -1, actorCarriesFlag: false));
		}
	}
}
