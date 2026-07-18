using System;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using FishMMO.Database.Exceptions;
using Npgsql;

namespace FishMMO.Database.Npgsql
{
	/// <summary>
	/// Encapsulates validated Npgsql database configuration.
	/// Responsibility is limited to binding and validating a provided <see cref="IConfiguration"/> instance.
	/// </summary>
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
		/// Gets a value indicating whether sensitive EF Core logging is enabled.
		/// </summary>
		public bool EnableLogging { get; }

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

			// Bind directly from the Npgsql section
			Settings = configuration.GetSection("Npgsql").Get<NpgsqlSettings>() ?? new NpgsqlSettings();

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
			ConnectionString = BuildConnectionString(Settings);
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

		private static string BuildConnectionString(NpgsqlSettings s)
		{
			return new NpgsqlConnectionStringBuilder
			{
				Host = s.Host,
				Port = ResolvePort(s.Port),
				Database = s.Database,
				Username = s.Username,
				Password = s.Password,
				Pooling = true,
				MinPoolSize = s.MinPoolSize,
				MaxPoolSize = s.MaxPoolSize,
				Timeout = s.ConnectionTimeout,
				CommandTimeout = s.CommandTimeout,
				NoResetOnClose = true,
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
			Debug.WriteLine($"[FishMMO-DB] Port configuration '{portValue}' is invalid or missing; defaulting to 5432.");
			return 5432;
		}
	}
}