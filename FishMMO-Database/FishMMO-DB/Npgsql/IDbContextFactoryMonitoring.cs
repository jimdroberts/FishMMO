using FishMMO.Database.Npgsql.Monitoring.Metrics;
using FishMMO.Database.Npgsql.Monitoring.Diagnostics;

namespace FishMMO.Database.Npgsql
{
	/// <summary>
	/// Optional monitoring interface for database context factories.
	/// Provides access to connection pool metrics and query performance tracking.
	/// </summary>
	/// <remarks>
	/// This interface follows the Interface Segregation Principle by separating
	/// monitoring concerns from core factory functionality. Consumers that don't
	/// need monitoring can depend on <see cref="IDbContextFactory"/> instead.
	/// </remarks>
	public interface IDbContextFactoryMonitoring
	{
		/// <summary>
		/// Gets the connection pool metrics for monitoring and diagnostics.
		/// </summary>
		ConnectionPoolMetrics PoolMetrics { get; }

		/// <summary>
		/// Gets the configured maximum pool size.
		/// </summary>
		int MaxPoolSize { get; }

		/// <summary>
		/// Gets the query performance tracker for operation-level monitoring.
		/// </summary>
		QueryPerformanceTracker PerformanceTracker { get; }

		/// <summary>
		/// Gets the retry policy configuration for transient failure handling.
		/// </summary>
		RetryPolicyConfiguration RetryPolicy { get; }
	}
}