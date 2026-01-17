using System.Collections.Generic;

namespace FishMMO.Database.Npgsql.Monitoring.Metrics
{
	/// <summary>
	/// Represents a summary of database operation metrics.
	/// Provides statistical information about query performance and success rates.
	/// Immutable snapshot of metrics at a point in time.
	/// </summary>
	public sealed class MetricsSummary
	{
		/// <summary>
		/// Gets or sets the total number of database queries executed.
		/// </summary>
		public long TotalQueries { get; set; }

		/// <summary>
		/// Gets or sets the number of successful database queries.
		/// </summary>
		public long SuccessfulQueries { get; set; }

		/// <summary>
		/// Gets or sets the number of failed database queries.
		/// </summary>
		public long FailedQueries { get; set; }

		/// <summary>
		/// Gets or sets the success rate as a percentage (0-100).
		/// </summary>
		public double SuccessRate { get; set; }

		/// <summary>
		/// Gets or sets the average response time in milliseconds.
		/// </summary>
		public double AverageResponseTimeMs { get; set; }

		/// <summary>
		/// Gets or sets the minimum response time in milliseconds.
		/// </summary>
		public double MinResponseTimeMs { get; set; }

		/// <summary>
		/// Gets or sets the maximum response time in milliseconds.
		/// </summary>
		public double MaxResponseTimeMs { get; set; }

		/// <summary>
		/// Gets or sets a dictionary containing error counts by error type.
		/// </summary>
		public Dictionary<string, long> ErrorCounts { get; set; }

		/// <summary>
		/// Initializes a new instance of the <see cref="MetricsSummary"/> class.
		/// </summary>
		public MetricsSummary()
		{
			ErrorCounts = new Dictionary<string, long>();
		}

		/// <summary>
		/// Returns a string representation of the metrics summary.
		/// </summary>
		/// <returns>A formatted string containing the key metrics information.</returns>
		public override string ToString()
		{
			return $"Queries: {TotalQueries} (Success: {SuccessfulQueries}, Failed: {FailedQueries}) | " +
				   $"Success Rate: {SuccessRate:F2}% | Avg: {AverageResponseTimeMs:F2}ms | " +
				   $"Min: {MinResponseTimeMs:F2}ms | Max: {MaxResponseTimeMs:F2}ms";
		}
	}
}