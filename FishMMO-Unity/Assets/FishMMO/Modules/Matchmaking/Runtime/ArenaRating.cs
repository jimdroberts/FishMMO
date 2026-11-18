using System;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// The ranked arena's rating arithmetic. Pure, so it is testable and so the server has nothing
	/// to decide when it writes the result.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Elo, team-wise: each player is rated against the <em>average</em> rating of the opposing
	/// team(s), and every member of a team gets the same expected score, so a team's carry and its
	/// passenger move by the same amount — the result was the team's. The K-factor is larger during
	/// placement so a new player's first games move them quickly to where they belong, then settles.
	/// </para>
	/// <para>
	/// The matchmaking band widens with wait time: a high rating alone in the queue would otherwise
	/// never play. The band is measured from the longest-waiting candidate, so it is their patience
	/// that opens the field.
	/// </para>
	/// </remarks>
	public static class ArenaRating
	{
		/// <summary>Rating every character starts a season at.</summary>
		public const int DefaultRating = 1500;

		/// <summary>Lowest a rating can fall. Keeps the number meaningful and the board readable.</summary>
		public const int MinimumRating = 100;

		/// <summary>Expected score of a rating against an opponent's, in [0,1].</summary>
		public static double Expected(int rating, int opponentRating)
		{
			return 1.0 / (1.0 + Math.Pow(10.0, (opponentRating - rating) / 400.0));
		}

		/// <summary>The K-factor for a player: larger while placing, then the template's.</summary>
		public static int KFactor(int gamesPlayed, int placementGames, int k, int placementK)
		{
			return gamesPlayed < Math.Max(0, placementGames) ? Math.Max(1, placementK) : Math.Max(1, k);
		}

		/// <summary>Placement games left before the rating is shown as final. 0 once placed.</summary>
		public static int PlacementGamesRemaining(int gamesPlayed, int placementGames)
		{
			return Math.Max(0, placementGames - Math.Max(0, gamesPlayed));
		}

		/// <summary>
		/// The rating change for one player from one result.
		/// </summary>
		/// <param name="rating">Their rating before the match.</param>
		/// <param name="opponentAverage">Average rating of everyone on the other side(s).</param>
		/// <param name="score">1 for a win, 0.5 for a draw, 0 for a loss.</param>
		/// <param name="k">Their K-factor.</param>
		/// <returns>A signed change, rounded to the nearest point; a win is never worth 0.</returns>
		public static int Delta(int rating, int opponentAverage, double score, int k)
		{
			double expected = Expected(rating, opponentAverage);
			int delta = (int)Math.Round(k * (score - expected));
			if (score > 0.5 && delta < 1)
			{
				delta = 1;
			}
			else if (score < 0.5 && delta > -1)
			{
				delta = -1;
			}
			return delta;
		}

		/// <summary>Applies a change and clamps to the floor.</summary>
		public static int Apply(int rating, int delta)
		{
			return Math.Max(MinimumRating, rating + delta);
		}

		/// <summary>Score a team earned: 1 won, 0.5 drew, 0 lost.</summary>
		public static double ScoreFor(int team, int winnerTeam)
		{
			if (winnerTeam < 0)
			{
				return 0.5;
			}
			return team == winnerTeam ? 1.0 : 0.0;
		}

		/// <summary>
		/// Average rating of everyone not on the given team. Returns the default when no one is.
		/// </summary>
		public static int OpponentAverage(IReadOnlyList<(int team, int rating)> members, int team)
		{
			long sum = 0;
			int count = 0;
			foreach ((int t, int r) in members)
			{
				if (t != team)
				{
					sum += r;
					++count;
				}
			}
			return count == 0 ? DefaultRating : (int)(sum / count);
		}

		/// <summary>
		/// Rating changes for every member of a finished match.
		/// </summary>
		/// <param name="members">Each seat: character, team, rating before, games played before.</param>
		/// <param name="winnerTeam">Winning team, or -1 for a draw.</param>
		/// <param name="placementGames">Games at the placement K-factor.</param>
		/// <param name="k">Settled K-factor.</param>
		/// <param name="placementK">Placement K-factor.</param>
		/// <returns>Per character: the change and the new rating, in input order.</returns>
		public static List<(long characterId, int delta, int newRating)> Resolve(
			IReadOnlyList<(long characterId, int team, int rating, int games)> members,
			int winnerTeam,
			int placementGames,
			int k,
			int placementK)
		{
			var result = new List<(long, int, int)>(members?.Count ?? 0);
			if (members == null || members.Count == 0)
			{
				return result;
			}

			var teamRatings = new List<(int team, int rating)>(members.Count);
			foreach (var m in members)
			{
				teamRatings.Add((m.team, m.rating));
			}

			foreach (var m in members)
			{
				int opp = OpponentAverage(teamRatings, m.team);
				int kf = KFactor(m.games, placementGames, k, placementK);
				int delta = Delta(m.rating, opp, ScoreFor(m.team, winnerTeam), kf);
				result.Add((m.characterId, delta, Apply(m.rating, delta)));
			}
			return result;
		}

		/// <summary>
		/// The rating band the matchmaker searches within after a wait.
		/// </summary>
		/// <param name="baseBand">Band at zero wait. 0 disables banding entirely.</param>
		/// <param name="growthPerSecond">Points the band widens by per second waited.</param>
		/// <param name="waitedSeconds">Seconds the longest waiter has waited.</param>
		/// <param name="maxBand">Ceiling, or 0 for none.</param>
		public static int ResolveBand(int baseBand, int growthPerSecond, double waitedSeconds, int maxBand)
		{
			if (baseBand <= 0)
			{
				return 0;
			}
			double band = baseBand + Math.Max(0.0, waitedSeconds) * Math.Max(0, growthPerSecond);
			if (maxBand > 0 && band > maxBand)
			{
				band = maxBand;
			}
			return (int)Math.Ceiling(band);
		}
	}
}
