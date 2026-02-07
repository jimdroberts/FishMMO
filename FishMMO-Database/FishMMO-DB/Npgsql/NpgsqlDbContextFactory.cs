using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;
using FishMMO.Database.Npgsql.Monitoring.Metrics;
using FishMMO.Database.Npgsql.Monitoring.Diagnostics;
using FishMMO.Database.Npgsql.Services;

namespace FishMMO.Database.Npgsql
{
	/// <summary>
	/// Factory for creating NpgsqlDbContext instances with proper async configuration.
	/// Thread-safe and stateless - creates new DbContext per call.
	/// Implements both custom interface and IDesignTimeDbContextFactory for migrations.
	/// </summary>
	public class NpgsqlDbContextFactory : INpgsqlDbContextFactory, IDesignTimeDbContextFactory<NpgsqlDbContext>
	{
		/// <summary>
		/// Maximum time in milliseconds to wait for active contexts during disposal.
		/// </summary>
		private const int DisposeWaitTimeoutMs = 5000;

		/// <summary>
		/// Interval in milliseconds between polls when waiting for contexts to complete.
		/// </summary>
		private const int ShutdownPollIntervalMs = 50;

		private int disposed;
		private int shutdown;
		private int activeContextCount;
		private readonly NpgsqlDbConfiguration configuration;
		private readonly DbContextOptions<NpgsqlDbContext> cachedOptions;
		private readonly ConnectionPoolMetrics poolMetrics;
		private readonly QueryPerformanceTracker performanceTracker;

		/// <summary>
		/// Initializes a new instance of NpgsqlDbContextFactory with default configuration path.
		/// </summary>
		public NpgsqlDbContextFactory()
			: this(NpgsqlDbConfiguration.CreateDefault())
		{
		}

		/// <summary>
		/// Initializes a new instance of NpgsqlDbContextFactory with specified configuration path.
		/// </summary>
		/// <param name="configPath">Path to configuration directory containing appsettings.json.</param>
		public NpgsqlDbContextFactory(string configPath)
			: this(new NpgsqlDbConfiguration(configPath))
		{
		}

		/// <summary>
		/// Initializes a new instance of NpgsqlDbContextFactory with specified configuration and logging.
		/// </summary>
		/// <param name="configPath">Path to configuration directory containing appsettings.json.</param>
		/// <param name="enableLogging">Enable sensitive data logging for development.</param>
		public NpgsqlDbContextFactory(string configPath, bool enableLogging)
			: this(new NpgsqlDbConfiguration(configPath, enableLogging, null))
		{
		}

		/// <summary>
		/// Initializes a new instance of NpgsqlDbContextFactory with full configuration.
		/// </summary>
		/// <param name="configPath">Path to configuration directory containing appsettings.json.</param>
		/// <param name="enableLogging">Enable sensitive data logging for development.</param>
		/// <param name="commandTimeout">Command timeout in seconds (overrides config file value).</param>
		public NpgsqlDbContextFactory(string configPath, bool enableLogging, int commandTimeout)
			: this(new NpgsqlDbConfiguration(configPath, enableLogging, commandTimeout))
		{
		}

		/// <summary>
		/// Initializes a new instance of NpgsqlDbContextFactory with a pre-built configuration.
		/// </summary>
		/// <param name="configuration">The database configuration.</param>
		/// <exception cref="ArgumentNullException">Thrown when configuration is null.</exception>
		public NpgsqlDbContextFactory(NpgsqlDbConfiguration configuration)
		{
			this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

			poolMetrics = new ConnectionPoolMetrics();
			var connectionMetricsInterceptor = new ConnectionMetricsInterceptor(poolMetrics);

			var optionsBuilder = new DbContextOptionsBuilder<NpgsqlDbContext>()
				.UseNpgsql(configuration.ConnectionString, npgsqlOptions =>
				{
					npgsqlOptions.CommandTimeout(configuration.CommandTimeout);
				})
				.UseSnakeCaseNamingConvention()
				.AddInterceptors(connectionMetricsInterceptor);

			if (configuration.EnableLogging)
			{
				optionsBuilder.EnableSensitiveDataLogging(true);
			}

			cachedOptions = optionsBuilder.Options;
			performanceTracker = new QueryPerformanceTracker(configuration.PerformanceConfiguration);
		}

		/// <inheritdoc />
		public ConnectionPoolMetrics PoolMetrics => poolMetrics;

		/// <inheritdoc />
		public int MaxPoolSize => configuration.MaxPoolSize;

		/// <inheritdoc />
		public QueryPerformanceTracker PerformanceTracker => performanceTracker;

		/// <inheritdoc />
		public RetryPolicyConfiguration RetryPolicy => configuration.RetryPolicy;

		/// <summary>
		/// Creates a new DbContext instance. Thread-safe.
		/// Each call creates a fresh context with new options - safe for concurrent use.
		/// The factory tracks active contexts for graceful shutdown support.
		/// </summary>
		/// <returns>A new NpgsqlDbContext instance.</returns>
		public NpgsqlDbContext CreateDbContext()
		{
			if (Volatile.Read(ref shutdown) != 0)
				throw new ObjectDisposedException(nameof(NpgsqlDbContextFactory), "NpgsqlDbContextFactory has been shut down.");

			try
			{
				Interlocked.Increment(ref activeContextCount);
				var context = new NpgsqlDbContext(cachedOptions, configuration.Schema);
				context.Disposed += OnContextDisposed;
				return context;
			}
			catch (NpgsqlException npgsqlEx) when (IsPoolExhaustionException(npgsqlEx))
			{
				Interlocked.Decrement(ref activeContextCount);
				poolMetrics.RecordPoolExhaustion();
				poolMetrics.RecordConnectionError();
				throw;
			}
			catch
			{
				Interlocked.Decrement(ref activeContextCount);
				poolMetrics.RecordConnectionError();
				throw;
			}
		}

		/// <summary>
		/// Callback invoked when a context is disposed, decrements the active context count.
		/// </summary>
		private void OnContextDisposed(object sender, EventArgs e)
		{
			if (sender is NpgsqlDbContext context)
			{
				context.Disposed -= OnContextDisposed;
			}
			Interlocked.Decrement(ref activeContextCount);
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

			// Check inner PostgresException for too_many_connections
			var innerException = exception.InnerException;
			while (innerException != null)
			{
				if (innerException is PostgresException pgEx && pgEx.SqlState == PostgresSqlState.TooManyConnections)
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
		/// The cancellation token is checked before context creation.
		/// </summary>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>A new NpgsqlDbContext instance.</returns>
		/// <exception cref="OperationCanceledException">Thrown if cancellation is requested.</exception>
		public Task<NpgsqlDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (Volatile.Read(ref shutdown) != 0)
				throw new ObjectDisposedException(nameof(NpgsqlDbContextFactory), "NpgsqlDbContextFactory has been shut down.");

			return Task.FromResult(CreateDbContext());
		}

		/// <inheritdoc />
		public void Shutdown()
		{
			Interlocked.Exchange(ref shutdown, 1);
		}

		/// <inheritdoc />
		public Task ShutdownAsync(CancellationToken cancellationToken = default)
		{
			Shutdown();
			return Task.CompletedTask;
		}

		/// <summary>
		/// Initiates shutdown and waits for all active contexts to be disposed.
		/// </summary>
		/// <param name="timeout">Maximum time to wait for active contexts to complete.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>True if all contexts completed within the timeout; false if timed out.</returns>
		public async Task<bool> ShutdownGracefullyAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
		{
			Shutdown();

			var elapsed = Stopwatch.StartNew();

			while (Volatile.Read(ref activeContextCount) > 0)
			{
				cancellationToken.ThrowIfCancellationRequested();

				if (elapsed.Elapsed >= timeout)
				{
					return false;
				}

				await Task.Delay(ShutdownPollIntervalMs, cancellationToken).ConfigureAwait(false);
			}

			return true;
		}

		/// <summary>
		/// Gets the current number of active (not yet disposed) DbContext instances.
		/// </summary>
		public int ActiveContextCount => Volatile.Read(ref activeContextCount);

		/// <summary>
		/// IDesignTimeDbContextFactory implementation for EF Core migrations.
		/// Used by dotnet ef commands for database migrations.
		/// </summary>
		/// <param name="args">Command line arguments from migration tools.</param>
		/// <returns>A new NpgsqlDbContext instance.</returns>
		public NpgsqlDbContext CreateDbContext(string[] args)
		{
			if (Volatile.Read(ref shutdown) != 0)
				throw new ObjectDisposedException(nameof(NpgsqlDbContextFactory), "NpgsqlDbContextFactory has been shut down.");

			return CreateDbContext();
		}

		/// <summary>
		/// Disposes the factory and releases all resources.
		/// Calls Shutdown() to reject new context creation, waits briefly for active contexts to complete,
		/// then disposes monitoring resources.
		/// </summary>
		/// <remarks>
		/// This method will wait up to 5 seconds for active contexts to be disposed before proceeding.
		/// For longer waits, use <see cref="ShutdownGracefullyAsync"/> before calling Dispose.
		/// </remarks>
		public void Dispose()
		{
			if (Interlocked.Exchange(ref disposed, 1) != 0)
				return;

			GC.SuppressFinalize(this);
			Shutdown();

			// Wait briefly for active contexts to complete (consistent with ShutdownGracefullyAsync behavior)
			var elapsed = Stopwatch.StartNew();

			while (Volatile.Read(ref activeContextCount) > 0 && elapsed.ElapsedMilliseconds < DisposeWaitTimeoutMs)
			{
				Thread.Sleep(ShutdownPollIntervalMs);
			}

			performanceTracker.Dispose();
			poolMetrics.Reset();
		}

		/// <summary>
		/// Tests whether the database is reachable.
		/// Useful for startup validation or health checks.
		/// </summary>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>True if a connection can be established; false otherwise.</returns>
		public async Task<bool> CanConnectAsync(CancellationToken cancellationToken = default)
		{
			try
			{
				using var context = CreateDbContext();
				return await context.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false);
			}
			catch
			{
				return false;
			}
		}

		/// <summary>
		/// Asynchronously disposes the factory and releases all resources.
		/// Calls Shutdown() to reject new context creation, waits briefly for active contexts to complete,
		/// then disposes monitoring resources.
		/// </summary>
		/// <returns>A ValueTask representing the asynchronous dispose operation.</returns>
		public async ValueTask DisposeAsync()
		{
			if (Interlocked.Exchange(ref disposed, 1) != 0)
				return;

			GC.SuppressFinalize(this);
			Shutdown();

			var elapsed = Stopwatch.StartNew();

			while (Volatile.Read(ref activeContextCount) > 0 && elapsed.ElapsedMilliseconds < DisposeWaitTimeoutMs)
			{
				await Task.Delay(ShutdownPollIntervalMs).ConfigureAwait(false);
			}

			performanceTracker.Dispose();
			poolMetrics.Reset();
		}
	}
}