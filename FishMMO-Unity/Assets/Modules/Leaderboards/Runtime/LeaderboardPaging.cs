namespace FishMMO.Shared
{
	/// <summary>
	/// The paging arithmetic both peers agree on. Pure, so it is tested without a server.
	/// </summary>
	/// <remarks>
	/// Pages are 1-based and fixed-size. The page size is a constant rather than a setting because
	/// the server keys its cache by page: every player asking for page 2 of a board is asking for
	/// the same rows, and shares one database read for them, only while everyone's pages are cut
	/// at the same places.
	/// </remarks>
	public static class LeaderboardPaging
	{
		/// <summary>Rows per page.</summary>
		public const int PageSize = 25;

		/// <summary>
		/// Pages a board of <paramref name="totalRanked"/> characters spans, capped at the deepest
		/// page the server lets anyone browse to. Never below 1: an empty board still has a page,
		/// the one that says it is empty.
		/// </summary>
		/// <param name="totalRanked">Characters on the board.</param>
		/// <param name="maxBrowsableRank">Deepest position anyone may page to; at least one page's worth.</param>
		public static int PageCount(int totalRanked, int maxBrowsableRank)
		{
			long browsable = System.Math.Min(System.Math.Max(0, totalRanked), System.Math.Max(PageSize, maxBrowsableRank));
			long pages = (browsable + PageSize - 1) / PageSize;
			return pages < 1 ? 1 : (int)pages;
		}

		/// <summary>The deepest page anyone may ask for, whatever the board's size.</summary>
		public static int MaxPage(int maxBrowsableRank)
		{
			return PageCount(int.MaxValue, maxBrowsableRank);
		}

		/// <summary>A requested page, clamped to 1..<paramref name="pageCount"/>.</summary>
		public static int ClampPage(int page, int pageCount)
		{
			if (pageCount < 1) pageCount = 1;
			if (page < 1) return 1;
			return page > pageCount ? pageCount : page;
		}

		/// <summary>Zero-based position of a page's first row.</summary>
		public static int Offset(int page)
		{
			return (page < 1 ? 0 : page - 1) * PageSize;
		}
	}
}
