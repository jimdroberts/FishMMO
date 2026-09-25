using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>Which tab of the leaderboard panel a board is listed under.</summary>
	public enum LeaderboardCategory : byte
	{
		/// <summary>Player versus player: arena ratings, lifetime PvP records.</summary>
		PvP = 0,
		/// <summary>Player versus environment: monsters slain, the world explored.</summary>
		PvE = 1,
	}

	/// <summary>Which stored number a board ranks characters by.</summary>
	/// <remarks>
	/// The server maps this onto the database layer's own source enum; the two are kept apart so
	/// the client assembly never references the database library.
	/// </remarks>
	public enum LeaderboardSource : byte
	{
		/// <summary>Not configured. A board with this source is never offered.</summary>
		None = 0,
		/// <summary>The ranked arena season rating. Current the moment a ranked match ends.</summary>
		ArenaSeasonRating = 1,
		/// <summary>A character attribute's value, such as PvP Rank. Current as of the last character save.</summary>
		CharacterAttribute = 2,
		/// <summary>An achievement's progress, such as Kills. Current as of the last character save.</summary>
		Achievement = 3,
	}

	/// <summary>
	/// One leaderboard: what it ranks, where it is listed, and how its score is labelled.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A board is data, not code. Every source is a number the game already keeps for its own
	/// reasons — a season rating, an attribute, an achievement's progress — so a new board is a new
	/// asset pointing at one of them, never a new column. Both peers load these from the shared
	/// addressables: the client lists them as tabs, and the server turns the one a player asks for
	/// into a database read.
	/// </para>
	/// <para>
	/// The identifier on the wire is the template's cached <see cref="CachedScriptableObject{T}.ID"/>,
	/// a hash of the asset name, so renaming an asset changes which board a client asks for. That
	/// is harmless here — nothing is stored against it — but it is why boards are looked up by ID
	/// and never by name.
	/// </para>
	/// </remarks>
	[CreateAssetMenu(fileName = "New Leaderboard", menuName = "FishMMO/Leaderboard/Leaderboard", order = 1)]
	public class LeaderboardTemplate : CachedScriptableObject<LeaderboardTemplate>, ICachedObject
	{
		[Tooltip("Name shown on the board's tab. Empty uses the asset name.")]
		public string DisplayName;

		[Tooltip("One line shown under the board's name.")]
		[TextArea]
		public string Description;

		[Tooltip("Which tab the board is listed under.")]
		public LeaderboardCategory Category;

		[Tooltip("Position within its category. Lower comes first; ties sort by name.")]
		public int SortOrder;

		[Tooltip("The stored number the board ranks by.")]
		public LeaderboardSource Source;

		[Tooltip("Character Attribute source: the attribute whose value is ranked.")]
		public CharacterAttributeTemplate AttributeTemplate;

		[Tooltip("Achievement source: the achievement whose progress is ranked.")]
		public AchievementTemplate AchievementTemplate;

		[Tooltip("Arena source: ranked games a character must finish this season to appear. Match your arenas' Placement Games, so a rating the player's own profile still hides as provisional is never ranked publicly.")]
		[Min(1)]
		public int MinimumGames = 10;

		[Tooltip("Header of the score column, e.g. Rating, Kills.")]
		public string ScoreLabel = "Score";

		/// <summary>The board's display name.</summary>
		public string ResolvedDisplayName => string.IsNullOrWhiteSpace(DisplayName) ? name : DisplayName;

		/// <summary>The score column's header.</summary>
		public string ResolvedScoreLabel => string.IsNullOrWhiteSpace(ScoreLabel) ? "Score" : ScoreLabel;

		/// <summary>Whether the board carries a win/loss record beside the score.</summary>
		public bool ShowsRecord => Source == LeaderboardSource.ArenaSeasonRating;

		/// <summary>
		/// The attribute or achievement template the board reads, or 0 for a source that has none
		/// (or a board missing its reference).
		/// </summary>
		public int SourceTemplateID
		{
			get
			{
				switch (Source)
				{
					case LeaderboardSource.CharacterAttribute:
						return AttributeTemplate != null ? AttributeTemplate.ID : 0;
					case LeaderboardSource.Achievement:
						return AchievementTemplate != null ? AchievementTemplate.ID : 0;
					default:
						return 0;
				}
			}
		}

		/// <summary>
		/// Whether the board names everything its source needs. An unconfigured board is left off
		/// the panel and refused by the server, rather than shown and answered with nothing.
		/// </summary>
		/// <param name="problem">What is missing, when false.</param>
		public bool IsConfigured(out string problem)
		{
			switch (Source)
			{
				case LeaderboardSource.ArenaSeasonRating:
					problem = null;
					return true;
				case LeaderboardSource.CharacterAttribute:
					problem = AttributeTemplate == null ? "an Attribute source with no Attribute Template" : null;
					return problem == null;
				case LeaderboardSource.Achievement:
					problem = AchievementTemplate == null ? "an Achievement source with no Achievement Template" : null;
					return problem == null;
				default:
					problem = "no Source";
					return false;
			}
		}

		/// <summary>
		/// Every configured board in one category, in display order: <see cref="SortOrder"/>, then
		/// name, then ID so the order is the same on every peer whatever order the assets loaded in.
		/// </summary>
		public static List<LeaderboardTemplate> InCategory(LeaderboardCategory category)
		{
			var boards = new List<LeaderboardTemplate>();
			Dictionary<int, LeaderboardTemplate> cache = GetCache<LeaderboardTemplate>();
			if (cache == null)
			{
				return boards;
			}
			foreach (LeaderboardTemplate board in cache.Values)
			{
				if (board != null && board.Category == category && board.IsConfigured(out _))
				{
					boards.Add(board);
				}
			}
			boards.Sort(CompareDisplayOrder);
			return boards;
		}

		/// <summary>
		/// The board an arena board's own leaderboard view shows: the first configured arena
		/// rating board in display order, or null when none is authored.
		/// </summary>
		public static LeaderboardTemplate FirstArenaRatingBoard()
		{
			LeaderboardTemplate best = null;
			Dictionary<int, LeaderboardTemplate> cache = GetCache<LeaderboardTemplate>();
			if (cache == null)
			{
				return null;
			}
			foreach (LeaderboardTemplate board in cache.Values)
			{
				if (board != null && board.Source == LeaderboardSource.ArenaSeasonRating &&
					(best == null || CompareDisplayOrder(board, best) < 0))
				{
					best = board;
				}
			}
			return best;
		}

		private static int CompareDisplayOrder(LeaderboardTemplate a, LeaderboardTemplate b)
		{
			int order = a.SortOrder.CompareTo(b.SortOrder);
			if (order != 0) return order;
			order = string.CompareOrdinal(a.ResolvedDisplayName, b.ResolvedDisplayName);
			if (order != 0) return order;
			return a.ID.CompareTo(b.ID);
		}
	}
}
