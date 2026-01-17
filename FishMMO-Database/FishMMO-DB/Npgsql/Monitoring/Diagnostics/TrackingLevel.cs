namespace FishMMO.Database.Npgsql.Monitoring.Diagnostics
{
	/// <summary>
	/// Defines the level of detail for query performance tracking.
	/// Higher levels provide more detail but incur greater overhead.
	/// </summary>
	public enum TrackingLevel
	{
		/// <summary>
		/// Tracking disabled - zero overhead.
		/// </summary>
		None = 0,

		/// <summary>
		/// Track only operation count and average execution time.
		/// Minimal overhead (~1%).
		/// </summary>
		Basic = 1,

		/// <summary>
		/// Track min/max/percentiles in addition to basic metrics.
		/// Low overhead (~2%).
		/// </summary>
		Standard = 2,

		/// <summary>
		/// Track detailed metrics with sampling.
		/// Medium overhead (~3-5%).
		/// </summary>
		Detailed = 3,

		/// <summary>
		/// Track everything with full logging of slow queries.
		/// High overhead (~5-15%).
		/// </summary>
		Full = 4
	}
}