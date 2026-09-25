namespace FishMMO.Database.Data.Enums
{
	/// <summary>Which stored number a leaderboard ranks characters by.</summary>
	/// <remarks>
	/// Every source is a column the game already writes for its own reasons; a leaderboard only
	/// reads it. That is deliberate: a board that needed a column of its own would be a second
	/// copy of a number the game keeps somewhere else, and the two would drift.
	/// </remarks>
	public enum LeaderboardSourceKind : byte
	{
		/// <summary>Not a source. A query carrying it is refused.</summary>
		None = 0,

		/// <summary>
		/// <c>arena_rating.rating</c> in the active season. Written in the same work item that
		/// ends a ranked match, so it is current the moment the match is.
		/// </summary>
		ArenaSeasonRating = 1,

		/// <summary>
		/// <c>character_attributes.value</c> for one attribute template. Written by the character
		/// save, so it trails an online character by up to one save interval.
		/// </summary>
		CharacterAttribute = 2,

		/// <summary>
		/// <c>character_achievements.value</c> for one achievement template. Written by the
		/// character save, so it trails an online character by up to one save interval.
		/// </summary>
		CharacterAchievement = 3,
	}
}
