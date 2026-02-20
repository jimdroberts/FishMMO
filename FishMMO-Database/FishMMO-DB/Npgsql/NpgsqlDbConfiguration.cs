using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using FishMMO.Database.Exceptions;
using Npgsql;

namespace FishMMO.Database.Npgsql
{
	/// <summary>
	/// Encapsulates all Npgsql database configuration loaded from appsettings.json.
	/// Immutable after construction - thread-safe for concurrent access.
	/// </summary>
	public sealed class NpgsqlDbConfiguration
	{
		private const string FishMMOEnvironmentVariable = "FISHMMO_ENVIRONMENT";
		private const string DotNetEnvironmentVariable = "DOTNET_ENVIRONMENT";
		private const string AspNetCoreEnvironmentVariable = "ASPNETCORE_ENVIRONMENT";

		/// <summary>
		/// Gets strongly-typed Npgsql settings bound from configuration.
		/// </summary>
		public NpgsqlSettings Settings { get; }

		/// <summary>
		/// Gets the resolved runtime environment name used for layered config loading.
		/// </summary>
		public string EnvironmentName { get; }

		/// <summary>
		/// Gets a value indicating whether EF Core sensitive data logging is enabled.
		/// </summary>
		public bool EnableLogging { get; }

		/// <summary>
		/// Gets the pre-built PostgreSQL connection string.
		/// </summary>
		public string ConnectionString { get; }

		/// <summary>
		/// Gets the database schema name.
		/// </summary>
		public string Schema => Settings.Schema;

		/// <summary>
		/// Gets the database name.
		/// </summary>
		public string Database => Settings.Database;

		/// <summary>
		/// Gets the configured maximum connection pool size.
		/// </summary>
		public int MaxPoolSize => Settings.MaxPoolSize;

		/// <summary>
		/// Gets the command timeout in seconds.
		/// </summary>
		public int CommandTimeout => Settings.CommandTimeout;

		/// <summary>
		/// Gets query performance tracking settings.
		/// </summary>
		public FishMMO.Database.Npgsql.Monitoring.Diagnostics.QueryPerformanceConfiguration PerformanceConfiguration { get; }

		/// <summary>
		/// Gets transient retry policy settings.
		/// </summary>
		public RetryPolicyConfiguration RetryPolicy => Settings.RetryPolicy;

		/// <summary>
		/// Initializes configuration from a path using the standard building logic.
		/// </summary>
		/// <param name="configPath">Path containing appsettings files. If null/whitespace, uses current AppDomain base directory.</param>
		/// <param name="environmentName">Optional environment name override (for example Development or Production).</param>
		/// <param name="enableLogging">Whether EF Core sensitive data logging should be enabled.</param>
		/// <param name="commandTimeoutOverride">Optional command timeout override in seconds.</param>
		public NpgsqlDbConfiguration(string configPath, string environmentName = null, bool enableLogging = false, int? commandTimeoutOverride = null)
			: this(BuildConfiguration(
				string.IsNullOrWhiteSpace(configPath) ? AppDomain.CurrentDomain.BaseDirectory : configPath,
				ResolveEnvironmentName(environmentName)),
			  ResolveEnvironmentName(environmentName), enableLogging, commandTimeoutOverride)
		{
		}

		/// <summary>
		/// Initializes configuration from a path with logging and optional command timeout override.
		/// </summary>
		/// <param name="configPath">Path containing appsettings files.</param>
		/// <param name="enableLogging">Whether EF Core sensitive data logging should be enabled.</param>
		/// <param name="commandTimeoutOverride">Optional command timeout override in seconds.</param>
		public NpgsqlDbConfiguration(string configPath, bool enableLogging, int? commandTimeoutOverride)
			: this(configPath, environmentName: null, enableLogging: enableLogging, commandTimeoutOverride: commandTimeoutOverride)
		{
		}

		/// <summary>
		/// Initializes configuration directly from an existing IConfiguration instance.
		/// </summary>
		/// <param name="configuration">Configuration root with an Npgsql section.</param>
		/// <param name="environmentName">Optional environment name override. If null, resolved from environment variables/defaults.</param>
		/// <param name="enableLogging">Whether EF Core sensitive data logging should be enabled.</param>
		/// <param name="commandTimeoutOverride">Optional command timeout override in seconds.</param>
		public NpgsqlDbConfiguration(IConfiguration configuration, string environmentName = null, bool enableLogging = false, int? commandTimeoutOverride = null)
		{
			if (configuration == null) throw new ArgumentNullException(nameof(configuration));

			EnvironmentName = ResolveEnvironmentName(environmentName);
			EnableLogging = enableLogging;

			// Bind the entire Npgsql section into our Settings POCO (includes nested Performance/Retry)
			Settings = configuration.GetSection("Npgsql").Get<NpgsqlSettings>() ?? new NpgsqlSettings();

			// Apply overrides and validation
			if (commandTimeoutOverride.HasValue) Settings.CommandTimeout = commandTimeoutOverride.Value;

			ValidateUnquotedIdentifier("Npgsql:Database", Settings.Database);
			ValidateUnquotedIdentifier("Npgsql:Schema", Settings.Schema);

			PerformanceConfiguration = MapPerformanceConfiguration(Settings.QueryPerformanceTracking);
			ConnectionString = BuildConnectionString(Settings);
		}

		/// <summary>
		/// Maps app settings performance configuration into diagnostics runtime configuration.
		/// </summary>
		/// <param name="source">The app settings source configuration.</param>
		/// <returns>Mapped diagnostics configuration instance.</returns>
		private static FishMMO.Database.Npgsql.Monitoring.Diagnostics.QueryPerformanceConfiguration MapPerformanceConfiguration(
			FishMMO.Database.QueryPerformanceConfiguration source)
		{
			source ??= new FishMMO.Database.QueryPerformanceConfiguration();

			return new FishMMO.Database.Npgsql.Monitoring.Diagnostics.QueryPerformanceConfiguration
			{
				Enabled = source.Enabled,
				Level = source.Level,
				SlowQueryThresholdMs = source.SlowQueryThresholdMs,
				SampleRate = source.SampleRate
			};
		}

		/// <summary>
		/// Builds a connection string from typed Npgsql settings.
		/// </summary>
		/// <param name="s">Npgsql settings source.</param>
		/// <returns>Fully composed PostgreSQL connection string.</returns>
		private static string BuildConnectionString(NpgsqlSettings s)
		{
			var builder = new NpgsqlConnectionStringBuilder
			{
				Host = s.Host,
				Port = int.TryParse(s.Port, out int port) && port > 0 ? port : 5432,
				Database = s.Database,
				Username = s.Username,
				Password = s.Password,
				Pooling = true,
				MinPoolSize = s.MinPoolSize,
				MaxPoolSize = s.MaxPoolSize,
				Timeout = s.ConnectionTimeout,
				CommandTimeout = s.CommandTimeout,

				// Optimized for PgBouncer/Pooled environments
				NoResetOnClose = true,
				MaxAutoPrepare = 0
			};
			return builder.ConnectionString;
		}

		/// <summary>
		/// Validates that a setting is a valid unquoted PostgreSQL identifier (snake_case).
		/// </summary>
		/// <param name="settingPath">Configuration key path used in error messaging.</param>
		/// <param name="value">Identifier value to validate.</param>
		/// <exception cref="DatabaseException">Thrown when the identifier is null/empty or invalid.</exception>
		private static void ValidateUnquotedIdentifier(string settingPath, string value)
		{
			if (string.IsNullOrWhiteSpace(value) || !DbContextExtensions.IsValidUnquotedIdentifier(value))
			{
				throw new DatabaseException(
					$"Invalid configuration value for '{settingPath}': '{value}'. Must be snake_case identifier.",
					errorCode: "INVALID_CONFIGURATION");
			}
		}

		/// <summary>
		/// Builds the layered configuration root.
		/// Loading order: appsettings.json, appsettings.{Environment}.json, then environment variables.
		/// </summary>
		/// <param name="basePath">Base directory where appsettings files are located.</param>
		/// <param name="environmentName">Resolved environment name.</param>
		/// <returns>Materialized configuration root.</returns>
		private static IConfiguration BuildConfiguration(string basePath, string environmentName)
		{
			var builder = new ConfigurationBuilder()
				.SetBasePath(basePath)
				.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);

			if (!string.IsNullOrWhiteSpace(environmentName))
			{
				builder.AddJsonFile($"appsettings.{environmentName}.json", optional: true);
			}

			builder.AddEnvironmentVariables();
			return builder.Build();
		}

		/// <summary>
		/// Resolves the environment name from explicit parameter, FishMMO/.NET env vars, or build defaults.
		/// </summary>
		/// <param name="environmentName">Optional explicit environment name.</param>
		/// <returns>Resolved non-empty environment name.</returns>
		private static string ResolveEnvironmentName(string environmentName)
		{
			if (!string.IsNullOrWhiteSpace(environmentName)) return environmentName.Trim();

			return Environment.GetEnvironmentVariable(FishMMOEnvironmentVariable)?.Trim()
				?? Environment.GetEnvironmentVariable(DotNetEnvironmentVariable)?.Trim()
				?? Environment.GetEnvironmentVariable(AspNetCoreEnvironmentVariable)?.Trim()
#if DEBUG
				?? "Development";
#else
                ?? "Production";
#endif
		}

		/// <summary>
		/// Gets the default configuration path based on the current AppDomain base directory parent.
		/// </summary>
		/// <returns>Resolved default configuration directory path.</returns>
		public static string GetDefaultConfigPath() =>
			Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory)?.FullName ?? AppDomain.CurrentDomain.BaseDirectory;

		/// <summary>
		/// Creates a configuration instance using the default configuration path.
		/// </summary>
		/// <returns>A configured <see cref="NpgsqlDbConfiguration"/> instance.</returns>
		public static NpgsqlDbConfiguration CreateDefault() => new(GetDefaultConfigPath());
	}
}