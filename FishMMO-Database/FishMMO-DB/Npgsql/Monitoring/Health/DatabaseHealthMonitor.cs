using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FishMMO.Database.Npgsql.Monitoring.Health
{
	/// <summary>
	/// Monitors database health by performing connectivity checks and measuring response times.
	/// Thread-safe implementation suitable for Unity headless servers.
	/// Follows Single Responsibility Principle: solely responsible for database health monitoring.
	/// Follows Dependency Inversion Principle: depends on INpgsqlDbContextFactory abstraction.
	/// </summary>
	public sealed class DatabaseHealthMonitor
	{
		private readonly INpgsqlDbContextFactory dbContextFactory;
		private readonly TimeSpan warningThreshold;
		private readonly TimeSpan criticalThreshold;
		private readonly double poolWarningThreshold;
		private readonly double poolCriticalThreshold;
		private DateTime lastCheckTime;
		private HealthStatus lastStatus;
		private string lastMessage;
		private readonly object lockObject = new object();

		/// <summary>
		/// Initializes a new instance of the <see cref="DatabaseHealthMonitor"/> class.
		/// </summary>
		/// <param name="dbContextFactory">Factory for creating database contexts.</param>
		/// <param name="warningThresholdMs">Response time warning threshold in milliseconds (default: 100ms).</param>
		/// <param name="criticalThresholdMs">Response time critical threshold in milliseconds (default: 500ms).</param>
		/// <param name="poolWarningThreshold">Pool utilization warning threshold percentage (default: 70%).</param>
		/// <param name="poolCriticalThreshold">Pool utilization critical threshold percentage (default: 85%).</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public DatabaseHealthMonitor(
			INpgsqlDbContextFactory dbContextFactory,
			int warningThresholdMs = 100,
			int criticalThresholdMs = 500,
			double poolWarningThreshold = 70.0,
			double poolCriticalThreshold = 85.0)
		{
			this.dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
			this.warningThreshold = TimeSpan.FromMilliseconds(warningThresholdMs);
			this.criticalThreshold = TimeSpan.FromMilliseconds(criticalThresholdMs);
			this.poolWarningThreshold = poolWarningThreshold;
			this.poolCriticalThreshold = poolCriticalThreshold;
			this.lastStatus = HealthStatus.Unknown;
			this.lastMessage = "Not yet checked";
			this.lastCheckTime = DateTime.MinValue;
		}

		/// <summary>
		/// Gets the last known health status.
		/// Thread-safe property access.
		/// </summary>
		public HealthStatus LastStatus
		{
			get
			{
				lock (lockObject)
				{
					return lastStatus;
				}
			}
		}

		/// <summary>
		/// Gets the last health check message.
		/// Thread-safe property access.
		/// </summary>
		public string LastMessage
		{
			get
			{
				lock (lockObject)
				{
					return lastMessage;
				}
			}
		}

		/// <summary>
		/// Gets the time of the last health check.
		/// Thread-safe property access.
		/// </summary>
		public DateTime LastCheckTime
		{
			get
			{
				lock (lockObject)
				{
					return lastCheckTime;
				}
			}
		}

		/// <summary>
		/// Performs a synchronous health check on the database.
		/// Safe to call from Unity main thread.
		/// </summary>
		/// <returns>Health check result containing status and diagnostic information.</returns>
		/// <remarks>
		/// <para>
		/// This method performs a truly synchronous database call (no sync-over-async) to avoid
		/// thread-pool starvation under load.
		/// </para>
		/// <para><b>Security Warning:</b> The returned HealthCheckResult contains sensitive
		/// infrastructure details including database names, server addresses, and connection
		/// pool information. If exposing this result via public APIs or external systems,
		/// call <see cref="HealthCheckResult.Sanitize"/> to redact sensitive information.</para>
		/// <para>Example: <c>var sanitizedResult = CheckHealth().Sanitize();</c></para>
		/// </remarks>
		public HealthCheckResult CheckHealth()
		{
			var startTime = DateTime.UtcNow;
			var result = new HealthCheckResult();

			try
			{
				using var dbContext = dbContextFactory.CreateDbContext();
				var connection = dbContext.Database.GetDbConnection();

				if (connection.State != ConnectionState.Open)
				{
					connection.Open();
				}

				using var command = connection.CreateCommand();
				command.CommandText = "SELECT 1";
				command.CommandTimeout = 5;
				_ = command.ExecuteScalar();

				var elapsed = DateTime.UtcNow - startTime;
				result.ResponseTimeMs = elapsed.TotalMilliseconds;
				result.IsConnected = true;
				result.DatabaseName = connection.Database;
				result.ServerAddress = connection.DataSource;

				if (connection is NpgsqlConnection npgsqlConnection)
				{
					result.PoolInfo = GetPoolInfo(npgsqlConnection);
				}

				ExtractPoolMetrics(result);

				if (elapsed > criticalThreshold)
				{
					result.Status = HealthStatus.Degraded;
					result.Message = $"Database responding slowly: {elapsed.TotalMilliseconds:F2}ms";
				}
				else if (elapsed > warningThreshold)
				{
					result.Status = HealthStatus.Healthy;
					result.Message = $"Database healthy (warning: {elapsed.TotalMilliseconds:F2}ms)";
					result.HasWarning = true;
				}
				else
				{
					result.Status = HealthStatus.Healthy;
					result.Message = $"Database healthy: {elapsed.TotalMilliseconds:F2}ms";
				}

				UpdateLastCheckInfo(result.Status, result.Message);
				return result;
			}
			catch (NpgsqlException ex)
			{
				result.Status = HealthStatus.Unhealthy;
				result.Message = $"Database connection failed: {ex.Message}";
				result.IsConnected = false;
				result.ResponseTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds;
				result.ErrorCode = ex.ErrorCode.ToString();
				result.Exception = ex;
				UpdateLastCheckInfo(result.Status, result.Message);
				return result;
			}
			catch (Exception ex)
			{
				result.Status = HealthStatus.Unhealthy;
				result.Message = $"Health check failed: {ex.Message}";
				result.IsConnected = false;
				result.ResponseTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds;
				result.Exception = ex;
				UpdateLastCheckInfo(result.Status, result.Message);
				return result;
			}
		}

		/// <summary>
		/// Performs an asynchronous health check on the database.
		/// Executes a simple SELECT 1 query to verify connectivity and measure response time.
		/// </summary>
		/// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
		/// <returns>Health check result containing status and diagnostic information.</returns>
		/// <remarks>
		/// <para><b>Security Warning:</b> The returned HealthCheckResult contains sensitive
		/// infrastructure details including database names, server addresses, and connection
		/// pool information. If exposing this result via public APIs or external systems,
		/// call <see cref="HealthCheckResult.Sanitize"/> to redact sensitive information.</para>
		/// <para>Example: <c>var sanitizedResult = await CheckHealthAsync().Sanitize();</c></para>
		/// </remarks>
		public async Task<HealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default)
		{
			var startTime = DateTime.UtcNow;
			var result = new HealthCheckResult();

			try
			{
				using var dbContext = dbContextFactory.CreateDbContext();
				var connection = dbContext.Database.GetDbConnection();

				// Open connection if needed
				if (connection.State != ConnectionState.Open)
				{
					await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
				}

				// Execute simple ping query
				using var command = connection.CreateCommand();
				command.CommandText = "SELECT 1";
				command.CommandTimeout = 5;

				var queryResult = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
				var elapsed = DateTime.UtcNow - startTime;

				// Populate result
				result.ResponseTimeMs = elapsed.TotalMilliseconds;
				result.IsConnected = true;
				result.DatabaseName = connection.Database;
				result.ServerAddress = connection.DataSource;

				// Extract pool info if using Npgsql
				if (connection is NpgsqlConnection npgsqlConnection)
				{
					result.PoolInfo = GetPoolInfo(npgsqlConnection);
				}

				// Extract runtime pool metrics from factory
				ExtractPoolMetrics(result);

				// Determine status based on response time
				if (elapsed > criticalThreshold)
				{
					result.Status = HealthStatus.Degraded;
					result.Message = $"Database responding slowly: {elapsed.TotalMilliseconds:F2}ms";
				}
				else if (elapsed > warningThreshold)
				{
					result.Status = HealthStatus.Healthy;
					result.Message = $"Database healthy (warning: {elapsed.TotalMilliseconds:F2}ms)";
					result.HasWarning = true;
				}
				else
				{
					result.Status = HealthStatus.Healthy;
					result.Message = $"Database healthy: {elapsed.TotalMilliseconds:F2}ms";
				}

				// Cache last result in thread-safe manner
				UpdateLastCheckInfo(result.Status, result.Message);

				return result;
			}
			catch (OperationCanceledException)
			{
				result.Status = HealthStatus.Unhealthy;
				result.Message = "Health check cancelled or timed out";
				result.IsConnected = false;
				result.ResponseTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds;

				UpdateLastCheckInfo(result.Status, result.Message);

				return result;
			}
			catch (NpgsqlException ex)
			{
				result.Status = HealthStatus.Unhealthy;
				result.Message = $"Database connection failed: {ex.Message}";
				result.IsConnected = false;
				result.ResponseTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds;
				result.ErrorCode = ex.ErrorCode.ToString();
				result.Exception = ex;

				UpdateLastCheckInfo(result.Status, result.Message);

				return result;
			}
			catch (Exception ex)
			{
				result.Status = HealthStatus.Unhealthy;
				result.Message = $"Health check failed: {ex.Message}";
				result.IsConnected = false;
				result.ResponseTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds;
				result.Exception = ex;

				UpdateLastCheckInfo(result.Status, result.Message);

				return result;
			}
		}

		/// <summary>
		/// Updates the cached last check information in a thread-safe manner.
		/// </summary>
		/// <param name="status">The health status to cache.</param>
		/// <param name="message">The message to cache.</param>
		private void UpdateLastCheckInfo(HealthStatus status, string message)
		{
			lock (lockObject)
			{
				lastStatus = status;
				lastMessage = message;
				lastCheckTime = DateTime.UtcNow;
			}
		}

		/// <summary>
		/// Gets the current pool health status without performing a full health check.
		/// Useful for lightweight monitoring and alerting.
		/// </summary>
		/// <returns>The current pool health result.</returns>
		public PoolHealthResult GetPoolHealth()
		{
			if (dbContextFactory is NpgsqlDbContextFactory factory)
			{
				return factory.PoolMetrics.GetPoolHealth(
					factory.MaxPoolSize,
					poolWarningThreshold,
					poolCriticalThreshold);
			}

			return new PoolHealthResult
			{
				Status = PoolHealthStatus.Unknown,
				Message = "Pool health unavailable - factory type not supported"
			};
		}

		/// <summary>
		/// Extracts connection pool information from Npgsql connection.
		/// </summary>
		/// <param name="connection">The Npgsql connection instance.</param>
		/// <returns>Formatted string containing pool configuration information.</returns>
		private string GetPoolInfo(NpgsqlConnection connection)
		{
			try
			{
				var builder = new NpgsqlConnectionStringBuilder(connection.ConnectionString);
				return $"Pool: Min={builder.MinPoolSize}, Max={builder.MaxPoolSize}, " +
					   $"Timeout={builder.Timeout}s, CmdTimeout={builder.CommandTimeout}s";
			}
			catch
			{
				return "Pool info unavailable";
			}
		}

		/// <summary>
		/// Extracts runtime pool metrics from the factory and populates the health check result.
		/// Includes pool health assessment with configurable thresholds.
		/// </summary>
		/// <param name="result">The health check result to populate.</param>
		private void ExtractPoolMetrics(HealthCheckResult result)
		{
			try
			{
				if (dbContextFactory is NpgsqlDbContextFactory factory)
				{
					var metrics = factory.PoolMetrics;
					result.ActiveConnections = metrics.ActiveConnections;
					result.PeakConnections = metrics.PeakActiveConnections;
					result.TotalConnectionsCreated = metrics.TotalConnectionsCreated;
					result.ConnectionErrors = metrics.ConnectionErrors;
					result.PoolExhaustionCount = metrics.PoolExhaustionCount;
					result.PoolUtilizationPercent = metrics.GetUtilizationPercentage(factory.MaxPoolSize);

					// Assess pool health
					var poolHealth = metrics.GetPoolHealth(
						factory.MaxPoolSize,
						poolWarningThreshold,
						poolCriticalThreshold);

					result.PoolHealthStatus = poolHealth.Status;
					result.PoolHealthMessage = poolHealth.Message;
					result.PoolRequiresAction = poolHealth.RequiresAction;

					// Append runtime metrics to PoolInfo
					if (!string.IsNullOrEmpty(result.PoolInfo))
					{
						result.PoolInfo += $" | Active: {metrics.ActiveConnections}/{factory.MaxPoolSize} " +
										   $"({result.PoolUtilizationPercent:F1}%), Peak: {metrics.PeakActiveConnections}, " +
										   $"Errors: {metrics.ConnectionErrors}, Status: {poolHealth.Status}";
					}

					// Degrade overall health status if pool health is critical or unhealthy
					if (result.Status == HealthStatus.Healthy)
					{
						if (poolHealth.Status == PoolHealthStatus.Unhealthy ||
							(poolHealth.Status == PoolHealthStatus.Critical && poolHealth.RequiresAction))
						{
							result.Status = HealthStatus.Degraded;
							result.Message += $" | Pool: {poolHealth.Message}";
							result.HasWarning = true;
						}
						else if (poolHealth.Status == PoolHealthStatus.Warning)
						{
							result.HasWarning = true;
						}
					}
				}
			}
			catch
			{
				// Silently ignore metrics extraction failures
			}
		}
	}
}