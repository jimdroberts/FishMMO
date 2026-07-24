using System;
using System.Collections.Generic;
using System.IO;

namespace FishMMO.Database
{
	/// <summary>
	/// Secure resolver for database connection parameters.
	///
	/// Resolution order (first wins):
	/// 1. Environment variables (FISHMMO_DB_*)
	/// 2. Platform secrets file:
	///    - Linux:   /etc/fishmmo/db-secrets.env
	///    - Windows: %ProgramData%\FishMMO\db-secrets.env
	///
	/// The secrets file is a simple KEY=VALUE format (one per line,
	/// # comments and blank lines ignored).  On Linux it should be
	/// chmod 600 owned by the service user.
	///
	/// appsettings.json MUST NOT contain Username or Password fields.
	/// Those fields have been removed from the configuration model
	/// entirely — there is no IConfiguration fallback for credentials.
	/// </summary>
	public static class DatabaseSecrets
	{
		/// <summary>Environment variable name for the database host.</summary>
		public const string HostEnvVar = "FISHMMO_DB_HOST";

		/// <summary>Environment variable name for the database port.</summary>
		public const string PortEnvVar = "FISHMMO_DB_PORT";

		/// <summary>Environment variable name for the database name.</summary>
		public const string DbNameEnvVar = "FISHMMO_DB_NAME";

		/// <summary>Environment variable name for the database username.</summary>
		public const string UsernameEnvVar = "FISHMMO_DB_USERNAME";

		/// <summary>Environment variable name for the database password.</summary>
		public const string PasswordEnvVar = "FISHMMO_DB_PASSWORD";

		/// <summary>
		/// Gets the platform-specific default path to the secrets file.
		/// Linux: /etc/fishmmo/db-secrets.env
		/// Windows: %ProgramData%\FishMMO\db-secrets.env
		/// </summary>
		public static string DefaultSecretsFilePath
		{
			get
			{
				if (_defaultSecretsFilePath == null)
				{
					if (IsWindows())
					{
						string programData = Environment.GetEnvironmentVariable("ProgramData")
							?? @"C:\ProgramData";
						_defaultSecretsFilePath = Path.Combine(programData, "FishMMO", "db-secrets.env");
					}
					else
					{
						_defaultSecretsFilePath = "/etc/fishmmo/db-secrets.env";
					}
				}
				return _defaultSecretsFilePath;
			}
		}

		private static string? _defaultSecretsFilePath;

		private static bool IsWindows()
		{
			// netstandard2.1 doesn't have OperatingSystem.IsWindows().
			// Path.DirectorySeparatorChar is '\\' on Windows, '/' elsewhere.
			return Path.DirectorySeparatorChar == '\\';
		}

		/// <summary>
		/// Resolves the database username.
		/// </summary>
		/// <param name="secretsFilePath">
		/// Path to the secrets file. If null, <see cref="DefaultSecretsFilePath"/> is used.
		/// </param>
		/// <returns>The resolved username, or null if not configured.</returns>
		public static string? TryResolveUsername(string? secretsFilePath = null)
		{
			// Priority 1: Environment variable
			string? env = Environment.GetEnvironmentVariable(UsernameEnvVar);
			if (!string.IsNullOrEmpty(env))
				return env.Trim();

			// Priority 2: Secrets file
			Dictionary<string, string>? secrets = TryParseSecretsFile(
				secretsFilePath ?? DefaultSecretsFilePath);
			if (secrets != null &&
				secrets.TryGetValue(UsernameEnvVar, out string? fileValue) &&
				!string.IsNullOrEmpty(fileValue))
			{
				return fileValue.Trim();
			}

			return null;
		}

		/// <summary>
		/// Resolves the database password.
		/// </summary>
		/// <param name="secretsFilePath">
		/// Path to the secrets file. If null, <see cref="DefaultSecretsFilePath"/> is used.
		/// </param>
		/// <returns>The resolved password, or null if not configured.</returns>
		public static string? TryResolvePassword(string? secretsFilePath = null)
		{
			// Priority 1: Environment variable
			string? env = Environment.GetEnvironmentVariable(PasswordEnvVar);
			if (!string.IsNullOrEmpty(env))
				return env.Trim();

			// Priority 2: Secrets file
			Dictionary<string, string>? secrets = TryParseSecretsFile(
				secretsFilePath ?? DefaultSecretsFilePath);
			if (secrets != null &&
				secrets.TryGetValue(PasswordEnvVar, out string? fileValue) &&
				!string.IsNullOrEmpty(fileValue))
			{
				return fileValue.Trim();
			}

			return null;
		}

		/// <summary>
		/// Resolves the database host.
		/// </summary>
		/// <returns>The resolved host, or "127.0.0.1" if not configured.</returns>
		public static string TryResolveHost(string? secretsFilePath = null)
		{
			string? env = Environment.GetEnvironmentVariable(HostEnvVar);
			if (!string.IsNullOrEmpty(env))
				return env.Trim();

			Dictionary<string, string>? secrets = TryParseSecretsFile(
				secretsFilePath ?? DefaultSecretsFilePath);
			if (secrets != null &&
				secrets.TryGetValue(HostEnvVar, out string? fileValue) &&
				!string.IsNullOrEmpty(fileValue))
				return fileValue.Trim();

			return "127.0.0.1";
		}

		/// <summary>
		/// Resolves the database port.
		/// </summary>
		/// <returns>The resolved port, or 5432 if not configured.</returns>
		public static int TryResolvePort(string? secretsFilePath = null)
		{
			string? env = Environment.GetEnvironmentVariable(PortEnvVar);
			if (!string.IsNullOrEmpty(env) && int.TryParse(env.Trim(), out int port) && port > 0)
				return port;

			Dictionary<string, string>? secrets = TryParseSecretsFile(
				secretsFilePath ?? DefaultSecretsFilePath);
			if (secrets != null &&
				secrets.TryGetValue(PortEnvVar, out string? fileValue) &&
				!string.IsNullOrEmpty(fileValue) &&
				int.TryParse(fileValue.Trim(), out int filePort) && filePort > 0)
				return filePort;

			return 5432;
		}

		/// <summary>
		/// Resolves the database name.
		/// </summary>
		/// <returns>The resolved database name, or "fishmmo" if not configured.</returns>
		public static string TryResolveDbName(string? secretsFilePath = null)
		{
			string? env = Environment.GetEnvironmentVariable(DbNameEnvVar);
			if (!string.IsNullOrEmpty(env))
				return env.Trim();

			Dictionary<string, string>? secrets = TryParseSecretsFile(
				secretsFilePath ?? DefaultSecretsFilePath);
			if (secrets != null &&
				secrets.TryGetValue(DbNameEnvVar, out string? fileValue) &&
				!string.IsNullOrEmpty(fileValue))
				return fileValue.Trim();

			return "fishmmo";
		}

		/// <summary>
		/// Parses a simple KEY=VALUE secrets file.
		/// Ignores blank lines and lines starting with '#'.
		/// </summary>
		/// <returns>
		/// A case-insensitive dictionary of key-value pairs,
		/// or null if the file doesn't exist or can't be read.
		/// </returns>
		private static Dictionary<string, string>? TryParseSecretsFile(string path)
		{
			if (!File.Exists(path))
				return null;

			string[] lines;
			try
			{
				lines = File.ReadAllLines(path);
			}
			catch (IOException)
			{
				return null;
			}
			catch (UnauthorizedAccessException)
			{
				return null;
			}

			var secrets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

			foreach (string rawLine in lines)
			{
				string line = rawLine.Trim();

				// Skip empty lines and comments
				if (line.Length == 0 || line[0] == '#')
					continue;

				// Split on first '=' only (values may contain '=')
				int equalsIndex = line.IndexOf('=');
				if (equalsIndex < 0)
					continue;

				string key = line.Substring(0, equalsIndex).Trim();
				string value = line.Substring(equalsIndex + 1).Trim();

				if (key.Length > 0)
					secrets[key] = value;
			}

			return secrets;
		}
	}
}
