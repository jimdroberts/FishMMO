using System;
using Microsoft.Extensions.Configuration;
using FishMMO.Database.Exceptions;
using Npgsql;

namespace FishMMO.Database.Npgsql
{
	/// <summary>
	/// Encapsulates validated Npgsql database configuration.
	/// Responsibility is limited to binding and validating a provided <see cref="IConfiguration"/> instance.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>WARNING — NoResetOnClose = true.</b> This configuration sets <c>NoResetOnClose = true</c>
	/// on the Npgsql connection string, which skips <c>DISCARD ALL</c> / <c>RESET ALL</c> when
	/// returning connections to the pool. This is a deliberate performance optimization, but it
	/// means the following session-level state will <b>NOT</b> be automatically reset:
	/// </para>
	/// <list type="bullet">
	///   <item><description><c>SET LOCAL</c> / <c>SET SESSION</c> / <c>SET ROLE</c></description></item>
	///   <item><description>Temporary tables created with <c>CREATE TEMP TABLE</c></description></item>
	///   <item><description>Prepared statements via <c>PREPARE</c> / <c>DEALLOCATE</c></description></item>
	///   <item><description>Advisory locks acquired at session scope</description></item>
	///   <item><description><c>LISTEN</c> / <c>UNLISTEN</c> subscriptions</description></item>
	///   <item><description><c>search_path</c> changes via <c>SET search_path</c></description></item>
	/// </list>
	/// <para>
	/// If any future code introduces session-level state, it MUST manually reset that state
	/// before returning the connection to the pool, or change <c>NoResetOnClose</c> to false.
	/// See the comment on the <c>NoResetOnClose = true</c> line in
	/// <see cref="BuildConnectionString"/> for details on the performance trade-off.
	/// </para>
	/// </remarks>
	public sealed class NpgsqlDbConfiguration
	{
		/// <summary>
		/// Gets the bound raw Npgsql settings.
		/// </summary>
		public NpgsqlSettings Settings { get; }

		/// <summary>
		/// Gets the finalized PostgreSQL connection string.
		/// </summary>
		public string ConnectionString { get; }

		/// <summary>
		/// Gets the resolved database username (from env vars or secrets file).
		/// </summary>
		public string? Username => _resolvedUsername;

		/// <summary>
		/// Gets a value indicating whether sensitive EF Core logging is enabled.
		/// </summary>
		public bool EnableLogging { get; }

		private readonly string? _resolvedUsername;
		private readonly string? _resolvedPassword;

		/// <summary>
		/// Gets the configured schema name.
		/// </summary>
		public string Schema => Settings.Schema;

		/// <summary>
		/// Gets the configured database name.
		/// </summary>
		public string Database => Settings.Database;

		/// <summary>
		/// Gets the configured maximum connection pool size.
		/// </summary>
		public int MaxPoolSize => Settings.MaxPoolSize;

		/// <summary>
		/// Gets the configured command timeout in seconds.
		/// </summary>
		public int CommandTimeout => Settings.CommandTimeout;

		/// <summary>
		/// Gets query performance tracking configuration mapped to monitoring diagnostics format.
		/// </summary>
		public FishMMO.Database.Npgsql.Monitoring.Diagnostics.QueryPerformanceConfiguration PerformanceConfiguration { get; }

		/// <summary>
		/// Gets retry policy settings used by services.
		/// </summary>
		public RetryPolicyConfiguration RetryPolicy => Settings.RetryPolicy;

		/// <summary>
		/// Initializes configuration from a pre-built IConfiguration instance.
		/// </summary>
		/// <param name="configuration">Configuration root containing an <c>Npgsql</c> section.</param>
		/// <param name="enableLogging">Whether to enable sensitive data logging support in dependent components.</param>
		/// <param name="commandTimeoutOverride">Optional command timeout override in seconds.</param>
		/// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration"/> is null.</exception>
		/// <exception cref="DatabaseException">Thrown when required identifiers or numeric settings are invalid.</exception>
		public NpgsqlDbConfiguration(IConfiguration configuration, bool enableLogging = false, int? commandTimeoutOverride = null)
		{
			if (configuration == null) throw new ArgumentNullException(nameof(configuration));

			EnableLogging = enableLogging;

			// Bind non-sensitive settings from the Npgsql section.
			// Username and Password are NOT in appsettings.json — they are
			// resolved exclusively from environment variables or the platform
			// secrets file via DatabaseSecrets.
			Settings = configuration.GetSection("Npgsql").Get<NpgsqlSettings>() ?? new NpgsqlSettings();

			// Resolve credentials from secure sources only.
			// No IConfiguration fallback — appsettings.json MUST NOT contain credentials.
			_resolvedUsername = DatabaseSecrets.TryResolveUsername();
			_resolvedPassword = DatabaseSecrets.TryResolvePassword();

			// Non-sensitive connection params: env vars / secrets file override appsettings.json.
			// This allows Docker/k8s deployments to configure the DB entirely via env vars.
			string? envHost = DatabaseSecrets.TryResolveHost();
			if (envHost != "127.0.0.1" || string.IsNullOrEmpty(Settings.Host))
				Settings.Host = envHost;

			int envPort = DatabaseSecrets.TryResolvePort();
			if (envPort != 5432 || string.IsNullOrEmpty(Settings.Port))
				Settings.Port = envPort.ToString();

			string envDb = DatabaseSecrets.TryResolveDbName();
			if (envDb != "fishmmo" || string.IsNullOrEmpty(Settings.Database))
				Settings.Database = envDb;

			// Apply runtime overrides
			if (commandTimeoutOverride.HasValue)
				Settings.CommandTimeout = commandTimeoutOverride.Value;

			// Business Logic Validation
			ValidateUnquotedIdentifier("Npgsql:Database", Settings.Database);
			ValidateUnquotedIdentifier("Npgsql:Schema", Settings.Schema);
			ValidateRange("Npgsql:ConnectionTimeout", Settings.ConnectionTimeout, minInclusive: 1);
			ValidateRange("Npgsql:CommandTimeout", Settings.CommandTimeout, minInclusive: 1);
			ValidateRange("Npgsql:MinPoolSize", Settings.MinPoolSize, minInclusive: 0);
			ValidateRange("Npgsql:MaxPoolSize", Settings.MaxPoolSize, minInclusive: 1);

			if (Settings.MinPoolSize > Settings.MaxPoolSize)
			{
				throw new DatabaseException(
					$"Invalid configuration values for 'Npgsql:MinPoolSize' and 'Npgsql:MaxPoolSize'. MinPoolSize ({Settings.MinPoolSize}) cannot be greater than MaxPoolSize ({Settings.MaxPoolSize}).",
					errorCode: "INVALID_CONFIGURATION");
			}

			PerformanceConfiguration = MapPerformanceConfiguration(Settings.QueryPerformanceTracking);
			ConnectionString = BuildConnectionString(Settings, _resolvedUsername, _resolvedPassword);
		}

		private static FishMMO.Database.Npgsql.Monitoring.Diagnostics.QueryPerformanceConfiguration MapPerformanceConfiguration(global::FishMMO.Database.QueryPerformanceConfiguration source)
		{
			source ??= new global::FishMMO.Database.QueryPerformanceConfiguration();
			return new FishMMO.Database.Npgsql.Monitoring.Diagnostics.QueryPerformanceConfiguration
			{
				Enabled = source.Enabled,
				Level = source.Level,
				SlowQueryThresholdMs = source.SlowQueryThresholdMs,
				SampleRate = source.SampleRate
			};
		}

		private static string BuildConnectionString(NpgsqlSettings s, string? username, string? password)
		{
			return new NpgsqlConnectionStringBuilder
			{
				Host = s.Host,
				Port = ResolvePort(s.Port),
				Database = s.Database,
				Username = username ?? string.Empty,
				Password = password ?? string.Empty,
				Pooling = true,
				MinPoolSize = s.MinPoolSize,
				MaxPoolSize = s.MaxPoolSize,
				Timeout = s.ConnectionTimeout,
				CommandTimeout = s.CommandTimeout,
				// WARNING: NoResetOnClose = true skips DISCARD ALL / RESET ALL when returning
				// connections to the pool. This is a deliberate performance optimization —
				// it avoids the per-connection round-trip cost of the reset sequence and
				// reduces time-to-first-query for pooled connections.
				//
				// In this codebase, no code path sets session-level state (SET LOCAL,
				// SET SESSION, SET ROLE, CREATE TEMP TABLE, search_path changes, LISTEN/
				// NOTIFY, or advisory locks on the session scope). All services in
				// BaseService create fresh DbContext instances per operation, and the
				// RetryPolicy always opens a new connection on retry.
				//
				// If any future code introduces session-level state, it MUST manually
				// reset that state before returning the connection to the pool, or change
				// NoResetOnClose to false. The performance cost of switching to false is:
				// one extra round-trip per connection checkout (~0.5-2 ms on LAN,
				// potentially more on WAN). Measure before changing.
				NoResetOnClose = true,
				// MaxAutoPrepare = 0 disables Npgsql's automatic prepared-statement
				// caching.  This codebase uses the EF Core compiled query pattern
				// (EF.CompileAsyncQuery) and raw SQL via ExecuteSqlRawAsync for the
				// majority of hot-path queries.  Prepared-statement caching offers
				// negligible benefit when queries are already compiled at the EF level,
				// and the dynamic-SQL nature of the BaseService retry strategy makes
				// cache hits unpredictable.  Setting MaxAutoPrepare to a non-zero value
				// (e.g. 50) would consume additional client-side memory tracking
				// statement plans that are rarely reused, with no measurable throughput
				// improvement.  If a future migration to parameterised queries changes
				// this trade-off, raise MaxAutoPrepare incrementally (start at 50) and
				// measure the impact on connection-open latency and memory usage.
				//
				// MaxAutoPrepare=0 disables automatic statement preparation to avoid Npgsql
				// prepared-statement memory accumulation from the dynamic SQL patterns used by
				// BaseService. Enable with a non-zero value if query patterns are stable.
				//
				// TODO: For compiled queries (EF.CompileAsyncQuery) that DO use stable
				// parameterised SQL, selectively enabling Npgsql auto-prepare could improve
				// execution time by avoiding per-call plan compilation.  When the compiled-query
				// hot paths are well-characterised, consider splitting the connection string:
				// one pool with MaxAutoPrepare=0 for dynamic-SQL services, and another with
				// MaxAutoPrepare >= 50 for services that rely heavily on compiled queries.
				// Measure connection-open latency and memory before and after the split.
				MaxAutoPrepare = 0
			}.ConnectionString;
		}

		private static void ValidateUnquotedIdentifier(string settingPath, string value)
		{
			if (string.IsNullOrWhiteSpace(value) || !DbContextExtensions.IsValidUnquotedIdentifier(value))
			{
				throw new DatabaseException(
					$"Invalid configuration value for '{settingPath}': '{value}'. Must be a snake_case identifier.",
					errorCode: "INVALID_CONFIGURATION");
			}
		}

		private static void ValidateRange(string settingPath, int value, int minInclusive)
		{
			if (value < minInclusive)
			{
				throw new DatabaseException(
					$"Invalid configuration value for '{settingPath}': '{value}'. Value must be greater than or equal to {minInclusive}.",
					errorCode: "INVALID_CONFIGURATION");
			}
		}

		private static int ResolvePort(string portValue)
		{
			if (int.TryParse(portValue, out int port) && port > 0)
			{
				return port;
			}
			Console.Error.WriteLine($"[FishMMO-DB] Port configuration '{portValue}' is invalid or missing; defaulting to 5432.");
			return 5432;
		}
	}
}