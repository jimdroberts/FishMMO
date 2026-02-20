using System;
using Microsoft.Extensions.Configuration;

namespace FishMMO.Database.Npgsql
{
	/// <summary>
	/// Builds <see cref="IConfiguration"/> instances for Npgsql-related components.
	/// Keeps configuration source resolution separate from configuration validation/data holders.
	/// </summary>
	public static class NpgsqlConfigurationLoader
	{
		private const string AppSettingsFileName = "appsettings.json";
		private const string EnvironmentPrefix = "appsettings.";
		private const string EnvironmentSuffix = ".json";

		/// <summary>
		/// Builds configuration from JSON files and environment variables.
		/// </summary>
		/// <param name="configPath">Optional base directory containing appsettings files. When null/empty, uses the current AppDomain base directory.</param>
		/// <param name="environmentName">
		/// Optional environment name. If null/empty, resolves from environment variables in priority order:
		/// <c>FISHMMO_ENVIRONMENT</c>, <c>DOTNET_ENVIRONMENT</c>, <c>ASPNETCORE_ENVIRONMENT</c>, then build default.
		/// </param>
		/// <returns>A built <see cref="IConfiguration"/> instance.</returns>
		public static IConfiguration Build(string? configPath = null, string? environmentName = null)
		{
			var basePath = string.IsNullOrWhiteSpace(configPath)
				? AppDomain.CurrentDomain.BaseDirectory
				: configPath;

			var resolvedEnvironment = ResolveEnvironmentName(environmentName);
			var environmentFile = string.Concat(EnvironmentPrefix, resolvedEnvironment, EnvironmentSuffix);

			return new ConfigurationBuilder()
				.SetBasePath(basePath)
				.AddJsonFile(AppSettingsFileName, optional: false, reloadOnChange: false)
				.AddJsonFile(environmentFile, optional: true, reloadOnChange: false)
				.AddEnvironmentVariables()
				.Build();
		}

		/// <summary>
		/// Resolves the active environment name.
		/// </summary>
		/// <param name="environmentName">Optional explicit environment override.</param>
		/// <returns>The resolved environment name.</returns>
		public static string ResolveEnvironmentName(string? environmentName = null)
		{
			if (!string.IsNullOrWhiteSpace(environmentName))
			{
				return environmentName;
			}

			var fishMmoEnvironment = Environment.GetEnvironmentVariable("FISHMMO_ENVIRONMENT");
			if (!string.IsNullOrWhiteSpace(fishMmoEnvironment))
			{
				return fishMmoEnvironment;
			}

			var dotnetEnvironment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
			if (!string.IsNullOrWhiteSpace(dotnetEnvironment))
			{
				return dotnetEnvironment;
			}

			var aspNetCoreEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
			if (!string.IsNullOrWhiteSpace(aspNetCoreEnvironment))
			{
				return aspNetCoreEnvironment;
			}

#if DEBUG
			return "Development";
#else
			return "Production";
#endif
		}
	}
}
