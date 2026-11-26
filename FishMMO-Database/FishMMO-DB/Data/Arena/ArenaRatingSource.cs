namespace FishMMO.Database.Data
{
	/// <summary>
	/// Where the arena former reads a candidate's rating from.
	/// </summary>
	/// <remarks>
	/// Unranked matches band and balance on the lifetime PvP Rank attribute, so the former needs
	/// the attribute's template id. Ranked matches use the season rating table, so it needs the
	/// season. Either may be left at zero to read every candidate as rating 0, which disables
	/// banding and makes balancing a no-op.
	/// </remarks>
	public readonly struct ArenaRatingSource
	{
		/// <summary>Season whose <c>arena_rating</c> rows supply the rating, or 0.</summary>
		public readonly long SeasonID;
		/// <summary>Template id of the character attribute that supplies the rating when <see cref="SeasonID"/> is 0, or 0.</summary>
		public readonly int AttributeTemplateID;
		/// <summary>Rating assumed for a character with no rating row: the rating everyone starts at (1500), for either source.</summary>
		public readonly int DefaultRating;

		public static readonly ArenaRatingSource None = default;

		public static ArenaRatingSource FromSeason(long seasonId, int defaultRating = 1500) => new ArenaRatingSource(seasonId, 0, defaultRating);
		/// <summary>
		/// The lifetime PvP Rank attribute. A character who has never had it saved has no row and is read at
		/// <paramref name="defaultRating"/>, which must be the attribute's initial value (it is an Elo rating).
		/// </summary>
		public static ArenaRatingSource FromAttribute(int attributeTemplateId, int defaultRating = 1500) => new ArenaRatingSource(0, attributeTemplateId, defaultRating);

		public ArenaRatingSource(long seasonID, int attributeTemplateID, int defaultRating)
		{
			SeasonID = seasonID;
			AttributeTemplateID = attributeTemplateID;
			DefaultRating = defaultRating;
		}
	}
}
