using System;
using System.Diagnostics;
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
	/// Factory for creating <see cref="NpgsqlDbContext"/> instances.
	/// Thread-safe and intended for singleton registration.
	/// Implements <see cref="IDesignTimeDbContextFactory{TContext}"/> for EF Core tooling.
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
		/// Initializes a new instance of <see cref="NpgsqlDbContextFactory"/> for EF Core design-time usage.
		/// Loads configuration from the current AppDomain base directory.
		/// </summary>
		public NpgsqlDbContextFactory()
			: this(BuildDesignTimeConfiguration())
		{
		}

		/// <summary>
		/// Initializes a new instance of <see cref="NpgsqlDbContextFactory"/> from a pre-built <see cref="IConfiguration"/>.
		/// </summary>
		/// <param name="configuration">Configuration root containing an <c>Npgsql</c> section.</param>
		/// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration"/> is <c>null</c>.</exception>
		public NpgsqlDbContextFactory(IConfiguration configuration)
			: this(new NpgsqlDbConfiguration(configuration))
		{
		}

		/// <summary>
		/// Initializes a new instance of <see cref="NpgsqlDbContextFactory"/> from a pre-built <see cref="IConfiguration"/>.
		/// </summary>
		/// <param name="configuration">Configuration root containing an <c>Npgsql</c> section.</param>
		/// <param name="enableLogging">Enable sensitive data logging for development.</param>
		/// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration"/> is <c>null</c>.</exception>
		public NpgsqlDbContextFactory(IConfiguration configuration, bool enableLogging)
			: this(new NpgsqlDbConfiguration(configuration, enableLogging))
		{
		}

		/// <summary>
		/// Initializes a new instance of <see cref="NpgsqlDbContextFactory"/> from a pre-built <see cref="IConfiguration"/>.
		/// </summary>
		/// <param name="configuration">Configuration root containing an <c>Npgsql</c> section.</param>
		/// <param name="enableLogging">Enable sensitive data logging for development.</param>
		/// <param name="commandTimeout">Command timeout in seconds (overrides config file value).</param>
		/// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration"/> is <c>null</c>.</exception>
		public NpgsqlDbContextFactory(IConfiguration configuration, bool enableLogging, int commandTimeout)
			: this(new NpgsqlDbConfiguration(configuration, enableLogging, commandTimeout))
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
			performanceTracker = new QueryPerformanceTracker(NormalizePerformanceConfiguration(configuration.PerformanceConfiguration));
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
			ThrowIfDisposedOrShutdown();

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
		/// Callback invoked when a context created by this factory is disposed.
		/// Detaches the event handler and decrements the active context count tracked by the factory.
		/// </summary>
		/// <param name="sender">The object that raised the event; expected to be an <see cref="NpgsqlDbContext"/>.</param>
		/// <param name="e">Event arguments (unused).</param>
		private void OnContextDisposed(object sender, EventArgs e)
		{
			if (sender is NpgsqlDbContext context)
			{
				context.Disposed -= OnContextDisposed;
			}
			Interlocked.Decrement(ref activeContextCount);
		}

		/// <summary>
		/// Determines whether the provided <see cref="NpgsqlException"/> indicates connection pool exhaustion
		/// or another connection-limit condition.
		/// </summary>
		/// <param name="exception">The <see cref="NpgsqlException"/> to inspect. Cannot be <c>null</c>.</param>
		/// <returns><c>true</c> when the exception likely indicates pool exhaustion or connection limits; otherwise <c>false</c>.</returns>
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
				if (innerException is PostgresException pgEx && string.Equals(pgEx.SqlState, "53300", StringComparison.Ordinal))
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

		/// <summary>
		/// Throws <see cref="ObjectDisposedException"/> when the factory has been disposed or shutdown.
		/// Used as a guard at the beginning of public operations to enforce lifecycle rules.
		/// </summary>
		/// <exception cref="ObjectDisposedException">Thrown when the factory is disposed or shutdown.</exception>
		private void ThrowIfDisposedOrShutdown()
		{
			if (Volatile.Read(ref disposed) != 0 || Volatile.Read(ref shutdown) != 0)
			{
				throw new ObjectDisposedException(nameof(NpgsqlDbContextFactory), "NpgsqlDbContextFactory has been shut down.");
			}
		}

		/// <summary>
		/// Normalizes a performance configuration object into the diagnostics <see cref="QueryPerformanceConfiguration"/> type
		/// used by internal monitoring components.
		/// </summary>
		/// <param name="configurationValue">An object that may be either a diagnostics <see cref="QueryPerformanceConfiguration"/>
		/// or the legacy <c>FishMMO.Database.QueryPerformanceConfiguration</c> instance.</param>
		/// <returns>A diagnostics <see cref="QueryPerformanceConfiguration"/> instance (never <c>null</c>).</returns>
		private static FishMMO.Database.Npgsql.Monitoring.Diagnostics.QueryPerformanceConfiguration NormalizePerformanceConfiguration(object configurationValue)
		{
			if (configurationValue is FishMMO.Database.Npgsql.Monitoring.Diagnostics.QueryPerformanceConfiguration diagnosticsConfiguration)
			{
				return diagnosticsConfiguration;
			}

			if (configurationValue is global::FishMMO.Database.QueryPerformanceConfiguration legacyConfiguration)
			{
				return new FishMMO.Database.Npgsql.Monitoring.Diagnostics.QueryPerformanceConfiguration
				{
					Enabled = legacyConfiguration.Enabled,
					Level = legacyConfiguration.Level,
					SlowQueryThresholdMs = legacyConfiguration.SlowQueryThresholdMs,
					SampleRate = legacyConfiguration.SampleRate
				};
			}

			return new FishMMO.Database.Npgsql.Monitoring.Diagnostics.QueryPerformanceConfiguration();
		}

		/// <summary>
		/// Builds an <see cref="IConfiguration"/> instance suitable for design-time (EF Core tools).
		/// Loads <c>appsettings.json</c>, an optional environment-specific file and environment variables
		/// from <see cref="AppDomain.BaseDirectory"/>. The selected environment is read from
		/// <c>FISHMMO_ENVIRONMENT</c>, <c>DOTNET_ENVIRONMENT</c> or <c>ASPNETCORE_ENVIRONMENT</c>.
		/// </summary>
		/// <returns>A built <see cref="IConfiguration"/> instance.</returns>
		private static IConfiguration BuildDesignTimeConfiguration()
		{
			string env = Environment.GetEnvironmentVariable("FISHMMO_ENVIRONMENT") ??
						 Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ??
						 Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ??
						 "Development";

			return new ConfigurationBuilder()
				.SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
				.AddJsonFile("appsettings.json", optional: false)
				.AddJsonFile($"appsettings.{env}.json", optional: true)
				.AddEnvironmentVariables()
				.Build();
		}
	}
}