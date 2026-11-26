using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// The duel rating's maths: Elo between two players, from the result alone. Pure, so the game's tests check it without
	/// a database.
	/// </summary>
	public static class DuelRating
	{
		/// <summary>Where every character's first duel starts from.</summary>
		public const int DefaultRating = 1500;

		/// <summary>The lowest a rating can fall to.</summary>
		public const int Floor = 100;

		/// <summary>The default K factor: the most one duel can move a rating.</summary>
		public const int DefaultK = 32;

		/// <summary>The chance the first player wins, by the Elo curve (400 points = ten to one).</summary>
		public static double Expected(int rating, int opponentRating)
		{
			return 1.0 / (1.0 + Math.Pow(10.0, (opponentRating - rating) / 400.0));
		}

		/// <summary>
		/// The two rating changes of one duel: the winner's (never negative) and the loser's (never positive); for a draw,
		/// the first and second side's. Each is rounded on its own, and a loss never takes a rating below
		/// <see cref="Floor"/>.
		/// </summary>
		public static (int winnerChange, int loserChange) Changes(int winnerRating, int loserRating, bool draw, int k = DefaultK)
		{
			k = Math.Max(1, k);
			double winnerScore = draw ? 0.5 : 1.0;
			int winnerChange = (int)Math.Round(k * (winnerScore - Expected(winnerRating, loserRating)), MidpointRounding.AwayFromZero);
			int loserChange = (int)Math.Round(k * ((1.0 - winnerScore) - Expected(loserRating, winnerRating)), MidpointRounding.AwayFromZero);
			if (!draw)
			{
				winnerChange = Math.Max(0, winnerChange);
				loserChange = Math.Min(0, loserChange);
			}
			winnerChange = Math.Max(Floor - winnerRating, winnerChange);
			loserChange = Math.Max(Floor - loserRating, loserChange);
			return (winnerChange, loserChange);
		}
	}

	/// <summary>One character's duel record after a result.</summary>
	public readonly struct DuelRecordData
	{
		public readonly long CharacterID;
		public readonly int Rating;
		public readonly int RatingChange;
		public readonly int Wins;
		public readonly int Losses;
		public readonly int Draws;

		public DuelRecordData(long characterID, int rating, int ratingChange, int wins, int losses, int draws)
		{
			CharacterID = characterID;
			Rating = rating;
			RatingChange = ratingChange;
			Wins = wins;
			Losses = losses;
			Draws = draws;
		}
	}

	/// <summary>Both sides' records after one duel's result was written.</summary>
	public readonly struct DuelResultData
	{
		/// <summary>The winner's, or for a draw the first side's.</summary>
		public readonly DuelRecordData Winner;
		/// <summary>The loser's, or for a draw the second side's.</summary>
		public readonly DuelRecordData Loser;

		public DuelResultData(DuelRecordData winner, DuelRecordData loser)
		{
			Winner = winner;
			Loser = loser;
		}
	}
}
