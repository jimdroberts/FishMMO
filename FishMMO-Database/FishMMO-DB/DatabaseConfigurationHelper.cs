using System;
using System.IO;
using Microsoft.Extensions.Configuration;

namespace FishMMO.Database
{
	public static class DatabaseConfigurationHelper
	{
		/// <summary>
		/// Determines the active environment using a tiered fallback system.
		/// Priority: FISHMMO_ENVIRONMENT > DOTNET_ENVIRONMENT > ASPNETCORE_ENVIRONMENT.
		/// </summary>
		public static string ResolveEnvironmentName()
		{
			string?[] candidates =
			[
				Environment.GetEnvironmentVariable("FISHMMO_ENVIRONMENT"),
				Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"),
				Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
			];

			foreach (string? candidate in candidates)
			{
				if (!string.IsNullOrWhiteSpace(candidate))
				{
					return candidate.Trim();
				}
			}

#if DEBUG
			return "Development";
#else
            return "Production";
#endif
		}

		/// <summary>
		/// Builds an <see cref="IConfiguration"/> instance suitable for design-time (EF Core tools).
		/// Loads <c>appsettings.json</c>, an optional environment-specific file and environment variables
		/// from <see cref="AppDomain.BaseDirectory"/>. The selected environment is read from
		/// <c>FISHMMO_ENVIRONMENT</c>, <c>DOTNET_ENVIRONMENT</c> or <c>ASPNETCORE_ENVIRONMENT</c>.
		/// </summary>
		/// <returns>A built <see cref="IConfiguration"/> instance.</returns>
		public static IConfiguration BuildDesignTimeConfiguration(string basePath = null)
		{
			string env = ResolveEnvironmentName();
			string bundledDir = !string.IsNullOrWhiteSpace(basePath) ? basePath : AppDomain.CurrentDomain.BaseDirectory;

			// Working-directory-first: operator override takes precedence over the bundled default.
			string Resolve(string fileName)
			{
				string local = Path.Combine(Directory.GetCurrentDirectory(), fileName);
				return File.Exists(local) ? local : Path.Combine(bundledDir, fileName);
			}

			// appsettings.json is optional — all non-sensitive settings have
		// sensible C# defaults in NpgsqlSettings, and credentials are resolved
		// exclusively from environment variables or /etc/fishmmo/db-secrets.env
		// via DatabaseSecrets.  appsettings.json exists only as a convenience
		// for overriding non-sensitive defaults (host, port, pool sizes, etc.).
		return new ConfigurationBuilder()
				.AddJsonFile(Resolve("appsettings.json"), optional: true)
				.AddJsonFile(Resolve($"appsettings.{env}.json"), optional: true)
				.AddEnvironmentVariables()
				.Build();
		}
	}
}