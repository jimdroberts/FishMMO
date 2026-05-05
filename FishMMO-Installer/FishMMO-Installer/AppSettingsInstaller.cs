using FishMMO.Database;
using FishMMO.Logging;
using Microsoft.Extensions.Configuration;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FishMMO.Installer
{
	/// <summary>
	/// Interactive wizard that creates or updates appsettings.json and related
	/// configuration files with secure file permissions (chmod 600 on Linux).
	/// Also generates environment-variable export files so passwords never need
	/// to live in plain-text JSON at rest.
	///
	/// Supported outputs:
	/// <list type="bullet">
	///   <item>appsettings.json — base configuration, chmod 600</item>
	///   <item>appsettings.Development.json / appsettings.Production.json — environment overrides, chmod 600</item>
	///   <item>fish shell snippet — ~/.config/fish/conf.d/fishmmo-secrets.fish, chmod 600</item>
	///   <item>systemd / .env file — fishmmo-secrets.env in target directory, chmod 600</item>
	/// </list>
	///
	/// .NET IConfiguration maps double-underscore environment variables to nested JSON keys:
	///   <c>Npgsql__Password</c>  →  <c>appsettings.json : Npgsql.Password</c>
	/// </summary>
	public static class AppSettingsInstaller
	{
		private static readonly JsonSerializerOptions PrettyJson = new JsonSerializerOptions
		{
			WriteIndented = true,
		};

		/// <summary>
		/// Entry point for the AppSettings secure setup wizard.
		/// </summary>
		public static async Task ConfigureAppSettings()
		{
			await Log.Info("FishMMOInstaller", "=== AppSettings Secure Configuration ===");

			string exeDir = InstallerProcessHelper.GetWorkingDirectory();
			string dbSubDir = Path.Combine(exeDir, "FishMMO-Database", "FishMMO-DB");

			string? targetDir = PromptTargetDirectory(exeDir, dbSubDir);
			if (targetDir == null) return;

			Console.WriteLine();
			Console.WriteLine("Select action:");
			Console.WriteLine("1 : Write / update appsettings.json");
			Console.WriteLine("2 : Write / update environment override (appsettings.<env>.json)");
			Console.WriteLine("3 : Generate secrets environment-variable file");
			Console.WriteLine("0 : Back");

			ConsoleKeyInfo key = Console.ReadKey(true);
			Console.WriteLine();

			switch (key.Key)
			{
				case ConsoleKey.D1:
					await WriteBaseAppSettings(targetDir);
					break;
				case ConsoleKey.D2:
					await WriteEnvironmentOverride(targetDir);
					break;
				case ConsoleKey.D3:
					await GenerateSecretsFile(targetDir);
					break;
				case ConsoleKey.D0:
				case ConsoleKey.NumPad0:
					return;
				default:
					if (key.KeyChar == '0') return;
					break;
			}
		}

		// ──────────────────────────────────────────────────────────────────────────
		//  Action: write appsettings.json
		// ──────────────────────────────────────────────────────────────────────────

		private static async Task WriteBaseAppSettings(string targetDir)
		{
			string filePath = Path.Combine(targetDir, "appsettings.json");
			AppSettings defaults = TryLoadExisting(filePath);

			await Log.Info("FishMMOInstaller", $"Configuring: {filePath}");
			Console.WriteLine("Press Enter to keep the current value shown in brackets.");
			Console.WriteLine();

			AppSettings settings = PromptAllSettings(defaults);
			await WriteSecureJsonFile(filePath, settings);

			await Log.Info("FishMMOInstaller", $"appsettings.json written and secured at: {filePath}");
		}

		// ──────────────────────────────────────────────────────────────────────────
		//  Action: write appsettings.<env>.json
		// ──────────────────────────────────────────────────────────────────────────

		private static async Task WriteEnvironmentOverride(string targetDir)
		{
			Console.WriteLine("Select environment:");
			Console.WriteLine("1 : Development");
			Console.WriteLine("2 : Production");
			Console.WriteLine("0 : Back");

			ConsoleKeyInfo key = Console.ReadKey(true);
			Console.WriteLine();

			string? envName = key.Key switch
			{
				ConsoleKey.D1 => "Development",
				ConsoleKey.D2 => "Production",
				_ => null,
			};

			if (envName == null) return;

			string filePath = Path.Combine(targetDir, $"appsettings.{envName}.json");
			AppSettings defaults = TryLoadExisting(filePath);

			await Log.Info("FishMMOInstaller", $"Configuring: {filePath}");
			Console.WriteLine($"Environment : {envName}");
			Console.WriteLine("Values here override the base appsettings.json at runtime.");
			Console.WriteLine("Press Enter to keep the current value shown in brackets.");
			Console.WriteLine();

			AppSettings settings = PromptAllSettings(defaults);
			await WriteSecureJsonFile(filePath, settings);

			await Log.Info("FishMMOInstaller", $"appsettings.{envName}.json written and secured at: {filePath}");
		}

		// ──────────────────────────────────────────────────────────────────────────
		//  Action: generate secrets env-var file
		// ──────────────────────────────────────────────────────────────────────────

		private static async Task GenerateSecretsFile(string targetDir)
		{
			Console.WriteLine("Select output format:");
			Console.WriteLine("1 : fish shell snippet  (~/.config/fish/conf.d/fishmmo-secrets.fish)");
			Console.WriteLine("2 : systemd / .env file (fishmmo-secrets.env in target directory)");
			Console.WriteLine("0 : Back");

			ConsoleKeyInfo key = Console.ReadKey(true);
			Console.WriteLine();

			if (key.Key == ConsoleKey.D0 || key.KeyChar == '0') return;
			if (key.Key != ConsoleKey.D1 && key.Key != ConsoleKey.D2) return;

			AppSettings defaults = TryLoadExisting(Path.Combine(targetDir, "appsettings.json"));

			Console.WriteLine("Enter secret values to export as environment variables.");
			Console.WriteLine("These override appsettings.json password fields at runtime.");
			Console.WriteLine("  (.NET IConfiguration: Npgsql__Password -> Npgsql:Password)");
			Console.WriteLine();

			string npgsqlPassword = InstallerProcessHelper.PromptForPassword(
				$"  PostgreSQL Password [{MaskSecret(defaults.Npgsql?.Password)}]: ");
			if (string.IsNullOrEmpty(npgsqlPassword))
				npgsqlPassword = defaults.Npgsql?.Password ?? string.Empty;

			string redisPassword = InstallerProcessHelper.PromptForPassword(
				$"  Redis Password (blank = none) [{MaskSecret(defaults.Redis?.Password)}]: ");
			if (string.IsNullOrEmpty(redisPassword))
				redisPassword = defaults.Redis?.Password ?? string.Empty;

			switch (key.Key)
			{
				case ConsoleKey.D1:
					await WriteFishSecretsSnippet(npgsqlPassword, redisPassword);
					break;
				case ConsoleKey.D2:
					await WriteSystemdEnvFile(targetDir, npgsqlPassword, redisPassword);
					break;
			}
		}

		private static async Task WriteFishSecretsSnippet(string npgsqlPassword, string redisPassword)
		{
			string homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			string fishConfDir = Path.Combine(homeDir, ".config", "fish", "conf.d");
			string filePath = Path.Combine(fishConfDir, "fishmmo-secrets.fish");

			Directory.CreateDirectory(fishConfDir);

			var sb = new StringBuilder();
			sb.AppendLine("# FishMMO database secrets — written by FishMMO Installer");
			sb.AppendLine("# These environment variables override appsettings.json values.");
			sb.AppendLine("# .NET IConfiguration maps double-underscore __ to JSON nesting:");
			sb.AppendLine("#   Npgsql__Password  ->  appsettings.json : Npgsql.Password");
			sb.AppendLine();
			sb.AppendLine($"set -gx Npgsql__Password \"{EscapeFishString(npgsqlPassword)}\"");
			if (!string.IsNullOrWhiteSpace(redisPassword))
			{
				sb.AppendLine($"set -gx Redis__Password  \"{EscapeFishString(redisPassword)}\"");
			}

			await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8);
			await ApplySecurePermissions(filePath);

			await Log.Info("FishMMOInstaller", $"Fish secrets snippet written to: {filePath}");
			Console.WriteLine();
			Console.WriteLine($"Written to: {filePath}");
			Console.WriteLine("New shells will pick it up automatically.");
			Console.WriteLine($"To activate now, run:  source \"{filePath}\"");
		}

		private static async Task WriteSystemdEnvFile(string targetDir, string npgsqlPassword, string redisPassword)
		{
			string filePath = Path.Combine(targetDir, "fishmmo-secrets.env");

			var sb = new StringBuilder();
			sb.AppendLine("# FishMMO database secrets — written by FishMMO Installer");
			sb.AppendLine("# Use with: EnvironmentFile=/path/to/fishmmo-secrets.env in a systemd service unit,");
			sb.AppendLine("# or env_file: in a docker-compose service definition.");
			sb.AppendLine("# .NET IConfiguration maps double-underscore __ to JSON nesting:");
			sb.AppendLine("#   Npgsql__Password  ->  appsettings.json : Npgsql.Password");
			sb.AppendLine();
			sb.AppendLine($"Npgsql__Password={EscapeEnvFileValue(npgsqlPassword)}");
			if (!string.IsNullOrWhiteSpace(redisPassword))
			{
				sb.AppendLine($"Redis__Password={EscapeEnvFileValue(redisPassword)}");
			}

			await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8);
			await ApplySecurePermissions(filePath);

			await Log.Info("FishMMOInstaller", $"Secrets env file written to: {filePath}");
			Console.WriteLine();
			Console.WriteLine($"Written to: {filePath}");
			Console.WriteLine("For systemd, add to your service unit [Service] section:");
			Console.WriteLine($"  EnvironmentFile={filePath}");
		}

		// ──────────────────────────────────────────────────────────────────────────
		//  JSON write + permissions
		// ──────────────────────────────────────────────────────────────────────────

		/// <summary>
		/// Merges the prompted <paramref name="settings"/> into the existing JSON at
		/// <paramref name="filePath"/>, preserving unmanaged sections such as
		/// <c>ConnectionPoolHealth</c>, <c>RetryPolicy</c>, and
		/// <c>QueryPerformanceTracking</c>. Applies chmod 600 on Linux.
		/// </summary>
		private static async Task WriteSecureJsonFile(string filePath, AppSettings settings)
		{
			// Read existing JSON to preserve sections we don't manage.
			JsonObject root;
			if (File.Exists(filePath))
			{
				try
				{
					string existing = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
					root = JsonNode.Parse(existing)?.AsObject() ?? BuildDefaultRoot();
				}
				catch
				{
					root = BuildDefaultRoot();
				}
			}
			else
			{
				// Seed a new file with the complete canonical structure.
				root = BuildDefaultRoot();
			}

			// Merge Npgsql section — only the keys we prompt for; sub-objects are left intact.
			JsonObject npgsql = EnsureObject(root, "Npgsql");
			npgsql["Database"] = JsonValue.Create(settings.Npgsql.Database);
			npgsql["Schema"] = JsonValue.Create(settings.Npgsql.Schema);
			npgsql["Username"] = JsonValue.Create(settings.Npgsql.Username);
			npgsql["Password"] = JsonValue.Create(settings.Npgsql.Password);
			npgsql["Host"] = JsonValue.Create(settings.Npgsql.Host);
			npgsql["Port"] = JsonValue.Create(settings.Npgsql.Port);
			npgsql["CommandTimeout"] = JsonValue.Create(settings.Npgsql.CommandTimeout);
			npgsql["ConnectionTimeout"] = JsonValue.Create(settings.Npgsql.ConnectionTimeout);
			npgsql["MinPoolSize"] = JsonValue.Create(settings.Npgsql.MinPoolSize);
			npgsql["MaxPoolSize"] = JsonValue.Create(settings.Npgsql.MaxPoolSize);

			// Merge Redis section.
			JsonObject redis = EnsureObject(root, "Redis");
			redis["Host"] = JsonValue.Create(settings.Redis.Host);
			redis["Port"] = JsonValue.Create(settings.Redis.Port);
			redis["Password"] = JsonValue.Create(settings.Redis.Password);

			string? dir = Path.GetDirectoryName(filePath);
			if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

			await File.WriteAllTextAsync(filePath, root.ToJsonString(PrettyJson), Encoding.UTF8);
			await ApplySecurePermissions(filePath);
		}

		/// <summary>
		/// Builds a <see cref="JsonObject"/> seeded with the canonical appsettings.json
		/// structure and placeholder values. Used when writing a brand-new file.
		/// </summary>
		private static JsonObject BuildDefaultRoot()
		{
			const string template =
				"""
				{
				  "Npgsql": {
				    "Database": "fish_mmo_postgresql",
				    "Schema": "public",
				    "Username": "user",
				    "Password": "change_me",
				    "Host": "127.0.0.1",
				    "Port": "5432",
				    "CommandTimeout": 10,
				    "ConnectionTimeout": 15,
				    "MinPoolSize": 5,
				    "MaxPoolSize": 100,
				    "RetryPolicy": {
				      "MaxRetries": 3,
				      "BaseDelayMs": 20,
				      "MaxJitterMs": 10
				    },
				    "QueryPerformanceTracking": {
				      "Enabled": false,
				      "Level": "Basic",
				      "SlowQueryThresholdMs": 1000,
				      "SampleRate": 0.1
				    }
				  },
				  "Redis": {
				    "Host": "127.0.0.1",
				    "Port": "6379",
				    "Password": ""
				  },
				  "ConnectionPoolHealth": {
				    "WarningThresholdPercent": 70,
				    "CriticalThresholdPercent": 85,
				    "MonitoringIntervalSeconds": 60
				  }
				}
				""";

			return JsonNode.Parse(template)?.AsObject() ?? new JsonObject();
		}

		/// <summary>
		/// Sets file permissions to 600 (owner read/write only) on Linux.
		/// No-ops silently on non-Linux platforms.
		/// </summary>
		private static async Task ApplySecurePermissions(string filePath)
		{
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
			{
				return;
			}

			try
			{
				File.SetUnixFileMode(filePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
				await Log.Info("FishMMOInstaller", $"Permissions set to 600 (owner read/write only) on: {filePath}");
			}
			catch (Exception ex)
			{
				await Log.Warning("FishMMOInstaller", $"Could not set file permissions on '{filePath}': {ex.Message}");
			}
		}

		// ──────────────────────────────────────────────────────────────────────────
		//  Prompt helpers
		// ──────────────────────────────────────────────────────────────────────────

		private static AppSettings PromptAllSettings(AppSettings defaults)
		{
			var s = new AppSettings();

			Console.WriteLine("--- PostgreSQL ---");
			s.Npgsql.Host = PromptString("  Host", defaults.Npgsql?.Host ?? "127.0.0.1");
			s.Npgsql.Port = PromptString("  Port", defaults.Npgsql?.Port ?? "5432");
			s.Npgsql.Database = PromptString("  Database", defaults.Npgsql?.Database ?? "fish_mmo_postgresql");
			s.Npgsql.Schema = PromptString("  Schema", defaults.Npgsql?.Schema ?? "public");
			s.Npgsql.Username = PromptString("  Username", defaults.Npgsql?.Username ?? "user");

			string pgPass = InstallerProcessHelper.PromptForPassword(
				$"  Password [{MaskSecret(defaults.Npgsql?.Password)}]: ");
			s.Npgsql.Password = string.IsNullOrEmpty(pgPass)
				? (defaults.Npgsql?.Password ?? string.Empty)
				: pgPass;

			s.Npgsql.CommandTimeout = PromptInt("  CommandTimeout (s)", defaults.Npgsql?.CommandTimeout ?? 10);
			s.Npgsql.ConnectionTimeout = PromptInt("  ConnectionTimeout (s)", defaults.Npgsql?.ConnectionTimeout ?? 15);
			s.Npgsql.MinPoolSize = PromptInt("  MinPoolSize", defaults.Npgsql?.MinPoolSize ?? 5);
			s.Npgsql.MaxPoolSize = PromptInt("  MaxPoolSize", defaults.Npgsql?.MaxPoolSize ?? 100);

			// Carry non-prompted nested objects through unchanged.
			if (defaults.Npgsql?.RetryPolicy != null)
				s.Npgsql.RetryPolicy = defaults.Npgsql.RetryPolicy;
			if (defaults.Npgsql?.QueryPerformanceTracking != null)
				s.Npgsql.QueryPerformanceTracking = defaults.Npgsql.QueryPerformanceTracking;

			Console.WriteLine();
			Console.WriteLine("--- Redis ---");
			s.Redis.Host = PromptString("  Host", defaults.Redis?.Host ?? "127.0.0.1");
			s.Redis.Port = PromptString("  Port", defaults.Redis?.Port ?? "6379");

			string redisPass = InstallerProcessHelper.PromptForPassword(
				$"  Password (blank = none) [{MaskSecret(defaults.Redis?.Password)}]: ");
			s.Redis.Password = string.IsNullOrEmpty(redisPass)
				? (defaults.Redis?.Password ?? string.Empty)
				: redisPass;

			Console.WriteLine();
			return s;
		}

		private static string? PromptTargetDirectory(string exeDir, string dbSubDir)
		{
			Console.WriteLine("Select target directory for configuration files:");
			Console.WriteLine($"1 : Executable dir  {exeDir}");
			Console.WriteLine($"2 : Database dir    {dbSubDir}");
			Console.WriteLine("0 : Back");

			ConsoleKeyInfo key = Console.ReadKey(true);
			Console.WriteLine();

			return key.Key switch
			{
				ConsoleKey.D1 => exeDir,
				ConsoleKey.D2 => dbSubDir,
				_ => null,
			};
		}

		private static string PromptString(string label, string defaultValue)
		{
			Console.Write($"{label} [{defaultValue}]: ");
			string? input = Console.ReadLine();
			return string.IsNullOrWhiteSpace(input) ? defaultValue : input.Trim();
		}

		private static int PromptInt(string label, int defaultValue)
		{
			while (true)
			{
				Console.Write($"{label} [{defaultValue}]: ");
				string? input = Console.ReadLine();
				if (string.IsNullOrWhiteSpace(input)) return defaultValue;
				if (int.TryParse(input.Trim(), out int result) && result > 0) return result;
				Console.WriteLine("  Enter a positive integer.");
			}
		}

		// ──────────────────────────────────────────────────────────────────────────
		//  Utility helpers
		// ──────────────────────────────────────────────────────────────────────────

		/// <summary>
		/// Loads existing settings from <paramref name="filePath"/> as defaults.
		/// Returns an empty <see cref="AppSettings"/> if the file is absent or unreadable.
		/// </summary>
		private static AppSettings TryLoadExisting(string filePath)
		{
			if (!File.Exists(filePath)) return new AppSettings();

			try
			{
				IConfiguration cfg = new ConfigurationBuilder()
					.SetBasePath(Path.GetDirectoryName(filePath) ?? string.Empty)
					.AddJsonFile(Path.GetFileName(filePath), optional: false)
					.Build();
				return cfg.Get<AppSettings>() ?? new AppSettings();
			}
			catch
			{
				return new AppSettings();
			}
		}

		/// <summary>
		/// Returns the child <see cref="JsonObject"/> at <paramref name="key"/>,
		/// creating it if absent or if the existing value is not an object.
		/// </summary>
		private static JsonObject EnsureObject(JsonObject parent, string key)
		{
			if (parent[key] is JsonObject existing) return existing;
			var obj = new JsonObject();
			parent[key] = obj;
			return obj;
		}

		/// <summary>Shows a masked preview of a secret (up to 8 asterisks).</summary>
		private static string MaskSecret(string? value)
			=> string.IsNullOrEmpty(value) ? "none" : new string('*', Math.Min(value.Length, 8));

		/// <summary>Escapes a value for embedding inside a fish double-quoted string.</summary>
		private static string EscapeFishString(string value)
			=> value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("$", "\\$");

		/// <summary>
		/// Escapes a value for systemd EnvironmentFile / docker-compose env_file format.
		/// Wraps in double-quotes when the value contains whitespace, <c>#</c>, or <c>'</c>.
		/// </summary>
		private static string EscapeEnvFileValue(string value)
		{
			if (value.Any(c => char.IsWhiteSpace(c) || c == '#' || c == '\''))
			{
				return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
			}

			return value;
		}
	}
}
