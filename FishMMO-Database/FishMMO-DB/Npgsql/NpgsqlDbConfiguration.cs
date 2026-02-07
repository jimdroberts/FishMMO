using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using FishMMO.Database.Exceptions;
using FishMMO.Database.Npgsql.Monitoring.Diagnostics;
using Npgsql;

namespace FishMMO.Database.Npgsql
{
	/// <summary>
	/// Encapsulates all Npgsql database configuration loaded from appsettings.json.
	/// Immutable after construction - thread-safe for concurrent access.
	/// </summary>
	/// <remarks>
	/// This class follows the Single Responsibility Principle by handling only
	/// configuration loading and validation, separate from context creation.
	/// </remarks>
	public sealed class NpgsqlDbConfiguration
	{
		/// <summary>
		/// Gets the database schema name.
		/// </summary>
		public string Schema { get; }

		/// <summary>
		/// Gets the database name.
		/// </summary>
		public string Database { get; }

		/// <summary>
		/// Gets the database host address.
		/// </summary>
		public string Host { get; }

		/// <summary>
		/// Gets the database port number.
		/// </summary>
		public int Port { get; }

		/// <summary>
		/// Gets the database username.
		/// </summary>
		public string Username { get; }

		/// <summary>
		/// Gets the database password.
		/// </summary>
		public string Password { get; }

		/// <summary>
		/// Gets the minimum connection pool size.
		/// </summary>
		public int MinPoolSize { get; }

		/// <summary>
		/// Gets the maximum connection pool size.
		/// </summary>
		public int MaxPoolSize { get; }

		/// <summary>
		/// Gets the command timeout in seconds.
		/// </summary>
		public int CommandTimeout { get; }

		/// <summary>
		/// Gets the connection timeout in seconds.
		/// </summary>
		public int ConnectionTimeout { get; }

		/// <summary>
		/// Gets whether sensitive data logging is enabled.
		/// </summary>
		public bool EnableLogging { get; }

		/// <summary>
		/// Gets the query performance tracking configuration.
		/// </summary>
		public QueryPerformanceConfiguration PerformanceConfiguration { get; }

		/// <summary>
		/// Gets the retry policy configuration for transient failure handling.
		/// </summary>
		public RetryPolicyConfiguration RetryPolicy { get; }

		/// <summary>
		/// Gets the pre-built connection string.
		/// </summary>
		public string ConnectionString { get; }

		/// <summary>
		/// Initializes configuration from the specified path with default settings.
		/// </summary>
		/// <param name="configPath">Path to configuration directory containing appsettings.json.</param>
		public NpgsqlDbConfiguration(string configPath)
			: this(configPath, false, null)
		{
		}

		/// <summary>
		/// Initializes configuration from the specified path with optional overrides.
		/// </summary>
		/// <param name="configPath">Path to configuration directory containing appsettings.json.</param>
		/// <param name="enableLogging">Enable sensitive data logging for development.</param>
		/// <param name="commandTimeoutOverride">Optional command timeout override (null uses config value).</param>
		public NpgsqlDbConfiguration(string configPath, bool enableLogging, int? commandTimeoutOverride)
		{
			EnableLogging = enableLogging;

			string basePath = string.IsNullOrWhiteSpace(configPath)
				? AppDomain.CurrentDomain.BaseDirectory
				: configPath;

			IConfiguration configuration = new ConfigurationBuilder()
				.SetBasePath(basePath)
				.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
				.Build();

			var npgsqlSection = configuration.GetSection("Npgsql");

			Database = npgsqlSection["Database"] ?? "fish_mmo_postgresql";
			Schema = npgsqlSection["Schema"] ?? NpgsqlDbContext.DefaultSchema;

			ValidateUnquotedIdentifier("Npgsql:Database", Database);
			ValidateUnquotedIdentifier("Npgsql:Schema", Schema);

			Username = npgsqlSection["Username"] ?? "user";
			Password = npgsqlSection["Password"] ?? "pass";
			Host = npgsqlSection["Host"] ?? "127.0.0.1";

			string portString = npgsqlSection["Port"] ?? "5432";
			Port = int.TryParse(portString, out int port) && port > 0 ? port : 5432;

			MinPoolSize = int.TryParse(npgsqlSection["MinPoolSize"], out int minPool) ? minPool : 5;
			MaxPoolSize = int.TryParse(npgsqlSection["MaxPoolSize"], out int maxPool) ? maxPool : 100;
			ConnectionTimeout = int.TryParse(npgsqlSection["ConnectionTimeout"], out int connTimeout) ? connTimeout : 15;

			int configCommandTimeout = int.TryParse(npgsqlSection["CommandTimeout"], out int cmdTimeout) ? cmdTimeout : 10;
			CommandTimeout = commandTimeoutOverride ?? configCommandTimeout;

			ConnectionString = BuildConnectionString();
			PerformanceConfiguration = LoadPerformanceConfiguration(configuration);
			RetryPolicy = LoadRetryPolicyConfiguration(configuration);
		}

		/// <summary>
		/// Builds the Npgsql connection string from configuration values.
		/// </summary>
		/// <returns>The connection string.</returns>
		private string BuildConnectionString()
		{
			var builder = new NpgsqlConnectionStringBuilder
			{
				Host = Host,
				Port = Port,
				Database = Database,
				Username = Username,
				Password = Password,
				Pooling = true,
				MinPoolSize = MinPoolSize,
				MaxPoolSize = MaxPoolSize,
				Timeout = ConnectionTimeout,
				CommandTimeout = CommandTimeout,
			};

			return builder.ConnectionString;
		}

		/// <summary>
		/// Loads query performance tracking configuration.
		/// </summary>
		/// <param name="configuration">The configuration root.</param>
		/// <returns>The performance configuration.</returns>
		private static QueryPerformanceConfiguration LoadPerformanceConfiguration(IConfiguration configuration)
		{
			var config = new QueryPerformanceConfiguration();
			var section = configuration.GetSection("QueryPerformanceTracking");

			if (bool.TryParse(section["Enabled"], out bool enabled))
				config.Enabled = enabled;
			if (Enum.TryParse<TrackingLevel>(section["Level"], out var level))
				config.Level = level;
			if (double.TryParse(section["SlowQueryThresholdMs"], out double threshold))
				config.SlowQueryThresholdMs = threshold;
			if (double.TryParse(section["SampleRate"], out double sampleRate))
				config.SampleRate = sampleRate;

			return config;
		}

		/// <summary>
		/// Loads retry policy configuration.
		/// </summary>
		/// <param name="configuration">The configuration root.</param>
		/// <returns>The retry policy configuration.</returns>
		private static RetryPolicyConfiguration LoadRetryPolicyConfiguration(IConfiguration configuration)
		{
			var config = new RetryPolicyConfiguration();
			var section = configuration.GetSection("RetryPolicy");

			if (int.TryParse(section["MaxRetries"], out int maxRetries) && maxRetries >= 0)
				config.MaxRetries = maxRetries;
			if (int.TryParse(section["BaseDelayMs"], out int baseDelay) && baseDelay >= 0)
				config.BaseDelayMs = baseDelay;
			if (int.TryParse(section["MaxJitterMs"], out int maxJitter) && maxJitter >= 0)
				config.MaxJitterMs = maxJitter;

			return config;
		}

		/// <summary>
		/// Validates that a configuration value is a valid unquoted PostgreSQL identifier.
		/// </summary>
		/// <param name="settingPath">The configuration setting path for error messages.</param>
		/// <param name="value">The value to validate.</param>
		/// <exception cref="DatabaseException">Thrown when the value is not a valid identifier.</exception>
		private static void ValidateUnquotedIdentifier(string settingPath, string value)
		{
			if (DbContextExtensions.IsValidUnquotedIdentifier(value))
			{
				return;
			}

			throw new DatabaseException(
				$"Invalid configuration value for '{settingPath}': '{value}'. " +
				"The value must be a valid unquoted PostgreSQL identifier (snake_case only): " +
				"lowercase letters, digits, and underscores; starting with a letter or underscore.",
				errorCode: "INVALID_CONFIGURATION");
		}

		/// <summary>
		/// Gets the default configuration path using the application base directory parent.
		/// </summary>
		/// <returns>The default configuration path.</returns>
		public static string GetDefaultConfigPath()
		{
			return Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory)?.FullName
				?? AppDomain.CurrentDomain.BaseDirectory;
		}

		/// <summary>
		/// Creates a default configuration using the application base directory.
		/// </summary>
		/// <returns>A new configuration instance.</returns>
		public static NpgsqlDbConfiguration CreateDefault()
		{
			return new NpgsqlDbConfiguration(GetDefaultConfigPath());
		}
	}
}