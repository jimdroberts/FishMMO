using System;
using FishMMO.Database.Npgsql.Monitoring.Metrics;

namespace FishMMO.Database.Npgsql.Monitoring.Health
{
	/// <summary>
	/// Extension methods for deriving connection pool health from an <see cref="INpgsqlDbContextFactory"/>.
	/// </summary>
	public static class DbContextFactoryHealthExtensions
	{
		/// <summary>
		/// Computes a point-in-time health assessment of the connection pool using runtime metrics.
		/// </summary>
		/// <param name="dbContextFactory">The DbContext factory that exposes pool metrics.</param>
		/// <param name="warningThreshold">Pool utilization percentage threshold for warning status.</param>
		/// <param name="criticalThreshold">Pool utilization percentage threshold for critical status.</param>
		/// <returns>A <see cref="PoolHealthResult"/> describing current pool health.</returns>
		/// <exception cref="ArgumentNullException"><paramref name="dbContextFactory"/> is null.</exception>
		/// <remarks>
		/// Thresholds are expressed as percentages of the configured pool size.
		/// Intended for telemetry/health endpoints.
		/// </remarks>
		public static PoolHealthResult GetConnectionPoolHealth(
			this INpgsqlDbContextFactory dbContextFactory,
			double warningThreshold = 70.0,
			double criticalThreshold = 85.0)
		{
			if (dbContextFactory == null)
				throw new ArgumentNullException(nameof(dbContextFactory));

			ConnectionPoolMetrics poolMetrics = dbContextFactory.PoolMetrics;
			int maxPoolSize = dbContextFactory.MaxPoolSize;

			return poolMetrics.GetPoolHealth(maxPoolSize, warningThreshold, criticalThreshold);
		}
	}
}