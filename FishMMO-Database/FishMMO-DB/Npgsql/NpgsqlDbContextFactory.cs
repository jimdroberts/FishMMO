using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Npgsql;
using FishMMO.Database.Npgsql.Monitoring.Metrics;
using FishMMO.Database.Npgsql.Monitoring.Diagnostics;

namespace FishMMO.Database.Npgsql
{
	/// <summary>
	/// Factory for creating NpgsqlDbContext instances with proper async configuration.
	/// Thread-safe and stateless - creates new DbContext per call.
	/// Implements both custom interface and IDesignTimeDbContextFactory for migrations.
	/// </summary>
	public class NpgsqlDbContextFactory : INpgsqlDbContextFactory, IDesignTimeDbContextFactory<NpgsqlDbContext>
	{
		private readonly string connectionString;
		private readonly string schema;
		private readonly bool enableLogging;
		private readonly int commandTimeout;
		private readonly DatabaseServiceExecutionSettings serviceExecutionSettings;
		private readonly ConnectionPoolMetrics poolMetrics;
		private readonly int maxPoolSize;
		private readonly QueryPerformanceTracker performanceTracker;

		/// <summary>
		/// Initializes a new instance of NpgsqlDbContextFactory with default configuration path.
		/// </summary>
		public NpgsqlDbContextFactory()
			: this(Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory)?.FullName ?? AppDomain.CurrentDomain.BaseDirectory, false, 10)
		{
		}

		/// <summary>
		/// Initializes a new instance of NpgsqlDbContextFactory with specified configuration path.
		/// </summary>
		/// <param name="configPath">Path to configuration directory containing appsettings.json.</param>
		public NpgsqlDbContextFactory(string configPath)
			: this(configPath, false, 10)
		{
		}

		/// <summary>
		/// Initializes a new instance of NpgsqlDbContextFactory with specified configuration and logging.
		/// </summary>
		/// <param name="configPath">Path to configuration directory containing appsettings.json.</param>
		/// <param name="enableLogging">Enable sensitive data logging for development.</param>
		public NpgsqlDbContextFactory(string configPath, bool enableLogging)
			: this(configPath, enableLogging, 10)
		{
		}

		/// <summary>
		/// Initializes a new instance of NpgsqlDbContextFactory with full configuration.
		/// </summary>
		/// <param name="configPath">Path to configuration directory containing appsettings.json.</param>
		/// <param name="enableLogging">Enable sensitive data logging for development.</param>
		/// <param name="commandTimeout">Command timeout in seconds (overrides config file value).</param>
		public NpgsqlDbContextFactory(string configPath, bool enableLogging, int commandTimeout)
		{
			this.enableLogging = enableLogging;
			this.commandTimeout = commandTimeout;

			// Load configuration once in constructor - immutable after initialization
			string basePath = string.IsNullOrWhiteSpace(configPath) ? AppDomain.CurrentDomain.BaseDirectory : configPath;

			IConfiguration configuration = new ConfigurationBuilder()
				.SetBasePath(basePath)
				.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
				.Build();

			serviceExecutionSettings = LoadServiceExecutionSettings(configuration);

			string database = configuration.GetSection("Npgsql")["Database"] ?? "fish_mmo_postgresql";
			schema = database;
			string userID = configuration.GetSection("Npgsql")["Username"] ?? "user";
			string password = configuration.GetSection("Npgsql")["Password"] ?? "pass";
			string host = configuration.GetSection("Npgsql")["Host"] ?? "127.0.0.1";
			string port = configuration.GetSection("Npgsql")["Port"] ?? "5432";

			// Read pooling configuration from settings
			int minPoolSize = 5;
			int maxPoolSize = 100;
			int configTimeout = 10;
			int connectionTimeout = 15; // Default connection timeout

			if (int.TryParse(configuration.GetSection("Npgsql")["MinPoolSize"], out int minSize))
				minPoolSize = minSize;
			if (int.TryParse(configuration.GetSection("Npgsql")["MaxPoolSize"], out int maxSize))
				maxPoolSize = maxSize;
			if (int.TryParse(configuration.GetSection("Npgsql")["CommandTimeout"], out int cfgTimeout))
				configTimeout = cfgTimeout;
			if (int.TryParse(configuration.GetSection("Npgsql")["ConnectionTimeout"], out int connTimeout))
				connectionTimeout = connTimeout;

			// Use provided timeout or fall back to config
			if (this.commandTimeout == 10 && configTimeout != 10)
				this.commandTimeout = configTimeout;

			// Build connection string with pooling and separate timeout configuration
			// Timeout = time to establish connection, Command Timeout = time for query execution
			connectionString =
				$"Host={host};Port={port};Database={database};Username={userID};Password={password};" +
				$"Pooling=true;Minimum Pool Size={minPoolSize};Maximum Pool Size={maxPoolSize};" +
				$"Timeout={connectionTimeout};Command Timeout={this.commandTimeout};";

			// Initialize pool metrics tracker
			this.maxPoolSize = maxPoolSize;
			poolMetrics = new ConnectionPoolMetrics();

			// Initialize query performance tracker with configuration from appsettings
			var perfConfig = new QueryPerformanceConfiguration();
			if (bool.TryParse(configuration.GetSection("QueryPerformanceTracking")["Enabled"], out bool perfEnabled))
				perfConfig.Enabled = perfEnabled;
			if (Enum.TryParse<TrackingLevel>(configuration.GetSection("QueryPerformanceTracking")["Level"], out var level))
				perfConfig.Level = level;
			if (double.TryParse(configuration.GetSection("QueryPerformanceTracking")["SlowQueryThresholdMs"], out double threshold))
				perfConfig.SlowQueryThresholdMs = threshold;
			if (double.TryParse(configuration.GetSection("QueryPerformanceTracking")["SampleRate"], out double sampleRate))
				perfConfig.SampleRate = sampleRate;

			performanceTracker = new QueryPerformanceTracker(perfConfig);
		}

		private static DatabaseServiceExecutionSettings LoadServiceExecutionSettings(IConfiguration configuration)
		{
			var settings = new DatabaseServiceExecutionSettings();
			var section = configuration.GetSection("DatabaseServiceExecution");

			if (int.TryParse(section["MaxTransientRetryCount"], out var maxTransientRetryCount) && maxTransientRetryCount >= 0)
				settings.MaxTransientRetryCount = maxTransientRetryCount;

			if (int.TryParse(section["BaseRetryDelayMs"], out var baseRetryDelayMs) && baseRetryDelayMs >= 0)
				settings.BaseRetryDelayMs = baseRetryDelayMs;

			if (int.TryParse(section["MaxRetryDelayMs"], out var maxRetryDelayMs) && maxRetryDelayMs >= 0)
				settings.MaxRetryDelayMs = maxRetryDelayMs;

			if (int.TryParse(section["MaxIdempotencyOperationNameLength"], out var maxIdempotencyOperationNameLength) && maxIdempotencyOperationNameLength > 0)
				settings.MaxIdempotencyOperationNameLength = maxIdempotencyOperationNameLength;

			if (int.TryParse(section["ProcessedRequestsRetentionDays"], out var processedRequestsRetentionDays) && processedRequestsRetentionDays >= 0)
				settings.ProcessedRequestsRetentionDays = processedRequestsRetentionDays;

			if (int.TryParse(section["ProcessedRequestsCleanupMaxRows"], out var processedRequestsCleanupMaxRows) && processedRequestsCleanupMaxRows > 0)
				settings.ProcessedRequestsCleanupMaxRows = processedRequestsCleanupMaxRows;

			if (int.TryParse(section["ProcessedRequestsCleanupMinIntervalMinutes"], out var processedRequestsCleanupMinIntervalMinutes) && processedRequestsCleanupMinIntervalMinutes >= 0)
				settings.ProcessedRequestsCleanupMinIntervalMinutes = processedRequestsCleanupMinIntervalMinutes;

			if (int.TryParse(section["ProcessedRequestsInProgressTimeoutMinutes"], out var processedRequestsInProgressTimeoutMinutes) && processedRequestsInProgressTimeoutMinutes >= 0)
				settings.ProcessedRequestsInProgressTimeoutMinutes = processedRequestsInProgressTimeoutMinutes;

			return settings;
		}

		/// <summary>
		/// Gets the connection pool metrics for monitoring and diagnostics.
		/// </summary>
		public ConnectionPoolMetrics PoolMetrics => poolMetrics;

		/// <inheritdoc />
		public DatabaseServiceExecutionSettings ServiceExecutionSettings => serviceExecutionSettings;

		/// <summary>
		/// Gets the configured maximum pool size.
		/// </summary>
		public int MaxPoolSize => maxPoolSize;

		/// <summary>
		/// Gets the query performance tracker for operation-level monitoring.
		/// </summary>
		public QueryPerformanceTracker PerformanceTracker => performanceTracker;

		/// <summary>
		/// Creates a new DbContext instance. Thread-safe.
		/// Each call creates a fresh context with new options - safe for concurrent use.
		/// </summary>
		/// <returns>A new NpgsqlDbContext instance.</returns>
		public NpgsqlDbContext CreateDbContext()
		{
			try
			{
				// Create fresh DbContextOptionsBuilder each time - no shared state
				var optionsBuilder = new DbContextOptionsBuilder<NpgsqlDbContext>()
					.UseNpgsql(connectionString, npgsqlOptions =>
					{
						npgsqlOptions.CommandTimeout(commandTimeout);
					});

				if (enableLogging)
				{
					optionsBuilder.EnableSensitiveDataLogging(true);
				}

				var context = new NpgsqlDbContext(optionsBuilder.Options, schema, poolMetrics);

				// Only track connection after successful creation
				poolMetrics.RecordConnectionCreated();

				return context;
			}
			catch (NpgsqlException npgsqlEx) when (IsPoolExhaustionException(npgsqlEx))
			{
				// Track both pool exhaustion and generic connection error
				poolMetrics.RecordPoolExhaustion();
				poolMetrics.RecordConnectionError();
				throw;
			}
			catch
			{
				// Track generic connection error
				poolMetrics.RecordConnectionError();
				throw;
			}
		}

		/// <summary>
		/// Determines if an NpgsqlException indicates connection pool exhaustion.
		/// </summary>
		/// <param name="exception">The Npgsql exception to check.</param>
		/// <returns>True if the exception indicates pool exhaustion; otherwise, false.</returns>
		private static bool IsPoolExhaustionException(NpgsqlException exception)
		{
			// IMPORTANT: Do not treat generic "timeout" as pool exhaustion.
			// Command/query timeouts are common and should not be counted as pool exhaustion.
			// Only match connection-acquisition/pool-specific timeout patterns.
			var message = exception.Message ?? string.Empty;

			if (message.Contains("timeout while getting a connection", StringComparison.OrdinalIgnoreCase) ||
				message.Contains("timeout waiting for a connection", StringComparison.OrdinalIgnoreCase) ||
				message.Contains("timeout waiting for connection", StringComparison.OrdinalIgnoreCase) ||
				message.Contains("timeout while getting a connection from pool", StringComparison.OrdinalIgnoreCase) ||
				(message.Contains("connection pool", StringComparison.OrdinalIgnoreCase) &&
					(message.Contains("exhaust", StringComparison.OrdinalIgnoreCase) || message.Contains("depleted", StringComparison.OrdinalIgnoreCase))) ||
				message.Contains("too many connections", StringComparison.OrdinalIgnoreCase) ||
				message.Contains("connection limit", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}

			// Check inner PostgresException for SqlState 53300 (too_many_connections)
			var innerException = exception.InnerException;
			while (innerException != null)
			{
				if (innerException is PostgresException pgEx && pgEx.SqlState == "53300")
				{
					return true;
				}
				innerException = innerException.InnerException;
			}

			return false;
		}

		/// <summary>
		/// Asynchronously creates a new DbContext instance.
		/// DbContext creation is CPU-bound, not I/O-bound, so this returns a completed task.
		/// </summary>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>A new NpgsqlDbContext instance.</returns>
		public Task<NpgsqlDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
		{
			// DbContext creation is CPU-bound, not I/O-bound
			// Return completed task with synchronous result
			return Task.FromResult(CreateDbContext());
		}

		/// <summary>
		/// IDesignTimeDbContextFactory implementation for EF Core migrations.
		/// Used by dotnet ef commands for database migrations.
		/// </summary>
		/// <param name="args">Command line arguments from migration tools.</param>
		/// <returns>A new NpgsqlDbContext instance.</returns>
		public NpgsqlDbContext CreateDbContext(string[] args)
		{
			return CreateDbContext();
		}
	}
}