using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Npgsql.Monitoring.Metrics;
using FishMMO.Database.Npgsql.Monitoring.Diagnostics;

namespace FishMMO.Database.Npgsql
{
	/// <summary>
	/// Factory interface for creating NpgsqlDbContext instances.
	/// Implementations must be thread-safe and create new contexts per call.
	/// DbContext instances are short-lived and should not be pooled.
	/// </summary>
	public interface INpgsqlDbContextFactory
	{
		/// <summary>
		/// Gets settings for BaseService execution (transient retries/backoff/idempotency validation).
		/// Loaded from appsettings.json by the factory.
		/// </summary>
		DatabaseServiceExecutionSettings ServiceExecutionSettings { get; }

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
		/// Creates a new DbContext instance.
		/// Each call creates a fresh context - safe for concurrent use.
		/// </summary>
		/// <returns>A new NpgsqlDbContext instance.</returns>
		NpgsqlDbContext CreateDbContext();

		/// <summary>
		/// Asynchronously creates a new DbContext instance.
		/// Each call creates a fresh context - safe for concurrent use.
		/// </summary>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>A new NpgsqlDbContext instance.</returns>
		Task<NpgsqlDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default);
	}
}