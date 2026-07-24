using FishMMO.Database;
using FishMMO.Logging;
using Microsoft.Extensions.Configuration;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
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
	/// Supported components:
	/// <list type="bullet">
	///   <item>Database — FishMMO-DB (Npgsql)</item>
	///   <item>IPFetch Web Server — WebServer.HttpPort + ConnectionStrings.NpgsqlConnection</item>
	///   <item>Patcher Web Server — WebServer.HttpPort + Patches.DirectoryName</item>
	///   <item>WebGL Web Server — WebServer.HttpPort</item>
	///   <item>Discord Bot — Discord.Token + Discord.DefaultGuildId + ConnectionStrings.Npgsql + rate-limiting</item>
	///   <item>CMS Server — ConnectionStrings.DefaultConnection</item>
	/// </list>
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

		private const string ClientGateSecretEnvVar = "FISHMMO_CLIENT_GATE_SECRET";

		/// <summary>FishMMO monorepo root, auto-detected from assembly location.</summary>
		private static string FishMMODevRoot => InstallationConstants.FishMMOMonorepoRoot;

		// ──────────────────────────────────────────────────────────────────────────
		//  Entry point — component selection
		// ──────────────────────────────────────────────────────────────────────────

		/// <summary>
		/// Entry point for the AppSettings secure setup wizard.
		/// </summary>
		public static async Task ConfigureAppSettings()
		{
			await Log.Info("FishMMOInstaller", "=== AppSettings Secure Configuration ===");

			Console.WriteLine("Select component to configure:");
			Console.WriteLine("1 : Database                  (FishMMO-DB / FishMMO-Setup)");
			Console.WriteLine("2 : IPFetch Web Server         (Login gate + IP fetch)");
			Console.WriteLine("3 : Patcher Web Server         (Patch distribution)");
			Console.WriteLine("4 : WebGL Web Server           (WebGL client host)");
			Console.WriteLine("5 : Discord Bot                (Chat bridge)");
			Console.WriteLine("6 : CMS Server                 (Content management)");
			Console.WriteLine("0 : Back");

			ConsoleKeyInfo ckey = Console.ReadKey(true);
			Console.WriteLine();

			switch (ckey.Key)
			{
				case ConsoleKey.D1:
					await ConfigureDatabaseComponent();
					break;
				case ConsoleKey.D2:
					await ConfigureWebServerComponent("IPFetch", "IPFetchASP.NET", "IpFetchServer",
						defaultPort: 8080, hasNpgsqlDsn: true, hasPatches: false);
					break;
				case ConsoleKey.D3:
					await ConfigureWebServerComponent("Patcher", "PatcherASP.NET", "Patcher",
						defaultPort: 8090, hasNpgsqlDsn: false, hasPatches: true);
					break;
				case ConsoleKey.D4:
					await ConfigureWebServerComponent("WebGL", "WebGLServerASP.NET", "WebGLServer",
						defaultPort: 8000, hasNpgsqlDsn: false, hasPatches: false);
					break;
				case ConsoleKey.D5:
					await ConfigureDiscordBotComponent();
					break;
				case ConsoleKey.D6:
					await ConfigureCmsComponent();
					break;
				case ConsoleKey.D0:
				case ConsoleKey.NumPad0:
					return;
				default:
					if (ckey.KeyChar == '0') return;
					break;
			}
		}

		// ──────────────────────────────────────────────────────────────────────────
		//  Component: Database (FishMMO-DB + FishMMO-Setup)
		// ──────────────────────────────────────────────────────────────────────────

		private static async Task ConfigureDatabaseComponent()
		{
			string defaultDir = Path.Combine(FishMMODevRoot, "FishMMO-Database", "FishMMO-DB");
			string? targetDir = PromptComponentDirectory(defaultDir);
			if (targetDir == null) return;

			await RunActionMenu("Database", targetDir,
				writeBase: WriteBaseAppSettings,
				writeEnvOverride: WriteEnvironmentOverride,
				generateSecrets: GenerateDatabaseSecretsFile);
		}

		// ──────────────────────────────────────────────────────────────────────────
		//  Action: write appsettings.json (Database)
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
		//  Action: write appsettings.<env>.json (Database)
		// ──────────────────────────────────────────────────────────────────────────

		private static async Task WriteEnvironmentOverride(string targetDir)
		{
			string? envName = PromptEnvironmentName();
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
		//  Action: generate secrets env-var file (Database)
		// ──────────────────────────────────────────────────────────────────────────

		private static async Task GenerateDatabaseSecretsFile(string targetDir)
		{
			Console.WriteLine("Select output format:");
			Console.WriteLine("1 : fish shell snippet  (~/.config/fish/conf.d/fishmmo-secrets.fish)");
			Console.WriteLine("2 : systemd / .env file (fishmmo-secrets.env in target directory)");
				Console.WriteLine("3 : PowerShell / CMD snippet  (%USERPROFILE%\\fishmmo-secrets.ps1 or .cmd)");
			Console.WriteLine("0 : Back");

			ConsoleKeyInfo key = Console.ReadKey(true);
			Console.WriteLine();

			if (key.Key == ConsoleKey.D0 || key.KeyChar == '0') return;
			if (key.Key != ConsoleKey.D1 && key.Key != ConsoleKey.D2 && key.Key != ConsoleKey.D3) return;

			AppSettings defaults = TryLoadExisting(Path.Combine(targetDir, "appsettings.json"));

			Console.WriteLine("Enter secret values to export as environment variables.");
			Console.WriteLine("These override appsettings.json password fields at runtime.");
			Console.WriteLine("  (.NET IConfiguration: Npgsql__Password -> Npgsql:Password)");
			Console.WriteLine();

			string npgsqlPassword = InstallerProcessHelper.PromptForPassword(
				$"  PostgreSQL Password [{MaskSecret(defaults.Npgsql?.Password)}]: ");
			if (string.IsNullOrEmpty(npgsqlPassword))
				npgsqlPassword = defaults.Npgsql?.Password ?? string.Empty;


			var secrets = new Dictionary<string, string>
			{
				["Npgsql__Password"] = npgsqlPassword,
			};

			switch (key.Key)
			{
				case ConsoleKey.D1:
					await WriteFishSecretsSnippet(secrets);
					break;
				case ConsoleKey.D2:
					await WriteSystemdEnvFile(targetDir, secrets);
					break;
			}
		}

		// ──────────────────────────────────────────────────────────────────────────
		//  Component: Web Servers (IPFetch, Patcher, WebGL)
		// ──────────────────────────────────────────────────────────────────────────

		private static async Task ConfigureWebServerComponent(
			string displayName, string folderGroup, string folderName,
			int defaultPort, bool hasNpgsqlDsn, bool hasPatches)
		{
			string defaultDir = Path.Combine(FishMMODevRoot, "FishMMO-WebServers", folderGroup, folderName);
			string? targetDir = PromptComponentDirectory(defaultDir);
			if (targetDir == null) return;

			await RunActionMenu($"{displayName} Web Server", targetDir,
				writeBase: async dir =>
					await WriteWebServerSettings(dir, "appsettings.json", defaultPort, hasNpgsqlDsn, hasPatches),
				writeEnvOverride: async dir =>
				{
					string? envName = PromptEnvironmentName();
					if (envName == null) return;
					await WriteWebServerSettings(dir, $"appsettings.{envName}.json", defaultPort, hasNpgsqlDsn, hasPatches);
				},
				generateSecrets: async dir =>
					await GenerateWebServerSecretsFile(dir, hasNpgsqlDsn));
		}

		private static async Task WriteWebServerSettings(
			string targetDir, string fileName, int defaultPort, bool hasNpgsqlDsn, bool hasPatches)
		{
			string filePath = Path.Combine(targetDir, fileName);

			// Load existing values as defaults.
			int existingPort = defaultPort;
			string? existingDsn = null;
			string? existingPatchesDir = null;

			if (File.Exists(filePath))
			{
				try
				{
					string text = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
					JsonObject? existing = JsonNode.Parse(text)?.AsObject();
					if (existing != null)
					{
						if (existing["WebServer"]?["HttpPort"] is JsonNode portNode
							&& int.TryParse(portNode.ToString(), out int p))
							existingPort = p;
						existingDsn = existing["ConnectionStrings"]?["NpgsqlConnection"]?.GetValue<string>();
						existingPatchesDir = existing["Patches"]?["DirectoryName"]?.GetValue<string>();
					}
				}
				catch { /* use defaults */ }
			}

			await Log.Info("FishMMOInstaller", $"Configuring: {filePath}");
			Console.WriteLine("Press Enter to keep the current value shown in brackets.");
			Console.WriteLine();

			Console.WriteLine("--- Web Server ---");
			int httpPort = PromptInt("  HttpPort", existingPort);

			string? npgsqlDsn = null;
			if (hasNpgsqlDsn)
				npgsqlDsn = PromptNpgsqlDsn(existingDsn);

			string? patchesDir = null;
			if (hasPatches)
			{
				Console.WriteLine();
				Console.WriteLine("--- Patches ---");
				patchesDir = PromptString("  Patches directory (relative or absolute)",
					existingPatchesDir ?? "Patches");
			}

			// Merge into existing JSON, preserving unmanaged keys.
			JsonObject root = await LoadOrCreateJsonObject(filePath);

			JsonObject webServer = EnsureObject(root, "WebServer");
			webServer["HttpPort"] = JsonValue.Create(httpPort);

			if (npgsqlDsn != null)
			{
				JsonObject connStrings = EnsureObject(root, "ConnectionStrings");
				connStrings["NpgsqlConnection"] = JsonValue.Create(npgsqlDsn);
			}

			if (patchesDir != null)
			{
				JsonObject patches = EnsureObject(root, "Patches");
				patches["DirectoryName"] = JsonValue.Create(patchesDir);
			}

			await WriteJsonObjectSecure(filePath, root);
			await Log.Info("FishMMOInstaller", $"{fileName} written and secured at: {filePath}");
		}

		private static async Task GenerateWebServerSecretsFile(string targetDir, bool hasNpgsqlDsn)
		{
			Console.WriteLine("Select output format:");
			Console.WriteLine("1 : fish shell snippet  (~/.config/fish/conf.d/fishmmo-secrets.fish)");
			Console.WriteLine("2 : systemd / .env file (fishmmo-secrets.env in target directory)");
				Console.WriteLine("3 : PowerShell / CMD snippet  (%USERPROFILE%\\fishmmo-secrets.ps1 or .cmd)");
			Console.WriteLine("0 : Back");

			ConsoleKeyInfo key = Console.ReadKey(true);
			Console.WriteLine();
			if (key.Key == ConsoleKey.D0 || key.KeyChar == '0') return;
			if (key.Key != ConsoleKey.D1 && key.Key != ConsoleKey.D2 && key.Key != ConsoleKey.D3) return;

			var secrets = new Dictionary<string, string>
			{
				[ClientGateSecretEnvVar] = GenerateClientGateSecret(),
			};

			if (hasNpgsqlDsn)
			{
				Console.WriteLine("Enter the PostgreSQL connection string secret for this web server.");
				string dsn = PromptNpgsqlDsn(existingDsn: null);
				secrets["ConnectionStrings__NpgsqlConnection"] = dsn;
			}

			switch (key.Key)
			{
				case ConsoleKey.D1: await WriteFishSecretsSnippet(secrets); break;
				case ConsoleKey.D2: await WriteSystemdEnvFile(targetDir, secrets); break;
				case ConsoleKey.D3: await WriteWindowsSecretsSnippet(secrets); break;
			}
		}

		private static string GenerateClientGateSecret()
		{
			byte[] secretBytes = new byte[32];
			RandomNumberGenerator.Fill(secretBytes);
			try
			{
				return Convert.ToBase64String(secretBytes);
			}
			finally
			{
				CryptographicOperations.ZeroMemory(secretBytes);
			}
		}

		// ──────────────────────────────────────────────────────────────────────────
		//  Component: Discord Bot
		// ──────────────────────────────────────────────────────────────────────────

		private static async Task ConfigureDiscordBotComponent()
		{
			string defaultDir = Path.Combine(FishMMODevRoot, "FishMMO-DiscordBot", "FishMMO-DiscordBot");
			string? targetDir = PromptComponentDirectory(defaultDir);
			if (targetDir == null) return;

			await RunActionMenu("Discord Bot", targetDir,
				writeBase: async dir => await WriteDiscordBotSettings(dir, "appsettings.json"),
				writeEnvOverride: async dir =>
				{
					string? envName = PromptEnvironmentName();
					if (envName == null) return;
					await WriteDiscordBotSettings(dir, $"appsettings.{envName}.json");
				},
				generateSecrets: GenerateDiscordBotSecretsFile);
		}

		private static async Task WriteDiscordBotSettings(string targetDir, string fileName)
		{
			string filePath = Path.Combine(targetDir, fileName);

			// Load existing values as defaults.
			string? existingToken = null;
			ulong existingGuildId = 0;
			string? existingDsn = null;
			int existingPollInterval = 5;
			int existingMaxMsgLen = 2000;
			int existingMaxPerWindow = 10;
			int existingWindowSec = 60;

			if (File.Exists(filePath))
			{
				try
				{
					string text = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
					JsonObject? existing = JsonNode.Parse(text)?.AsObject();
					if (existing != null)
					{
						existingToken = existing["Discord"]?["Token"]?.GetValue<string>();
						if (existing["Discord"]?["DefaultGuildId"] is JsonNode gidNode
							&& ulong.TryParse(gidNode.ToString(), out ulong gid))
							existingGuildId = gid;
						existingDsn = existing["ConnectionStrings"]?["Npgsql"]?.GetValue<string>();
						if (existing["ChatPollingIntervalSeconds"] is JsonNode ciNode
							&& int.TryParse(ciNode.ToString(), out int ci))
							existingPollInterval = ci;
						if (existing["BridgeMessageMaxLength"] is JsonNode mlNode
							&& int.TryParse(mlNode.ToString(), out int ml))
							existingMaxMsgLen = ml;
						if (existing["RateLimiting"]?["MaxMessagesPerWindow"] is JsonNode mpwNode
							&& int.TryParse(mpwNode.ToString(), out int mpw))
							existingMaxPerWindow = mpw;
						if (existing["RateLimiting"]?["WindowSeconds"] is JsonNode wsNode
							&& int.TryParse(wsNode.ToString(), out int ws))
							existingWindowSec = ws;
					}
				}
				catch { /* use defaults */ }
			}

			await Log.Info("FishMMOInstaller", $"Configuring: {filePath}");
			Console.WriteLine("Press Enter to keep the current value shown in brackets.");
			Console.WriteLine();

			Console.WriteLine("--- Discord ---");
			string token = InstallerProcessHelper.PromptForPassword(
				$"  Bot Token [{MaskSecret(existingToken)}]: ");
			if (string.IsNullOrEmpty(token)) token = existingToken ?? string.Empty;
			ulong guildId = PromptUlong("  Default Guild ID", existingGuildId);

			string npgsqlDsn = PromptNpgsqlDsn(existingDsn);

			Console.WriteLine();
			Console.WriteLine("--- Chat Bridge ---");
			int pollInterval = PromptInt("  ChatPollingIntervalSeconds", existingPollInterval);
			int maxMsgLen = PromptInt("  BridgeMessageMaxLength", existingMaxMsgLen);

			Console.WriteLine();
			Console.WriteLine("--- Rate Limiting ---");
			int maxPerWindow = PromptInt("  MaxMessagesPerWindow", existingMaxPerWindow);
			int windowSec = PromptInt("  WindowSeconds", existingWindowSec);

			// Merge into existing JSON, preserving unmanaged keys.
			JsonObject root = await LoadOrCreateJsonObject(filePath);

			JsonObject discord = EnsureObject(root, "Discord");
			discord["Token"] = JsonValue.Create(token);
			discord["DefaultGuildId"] = JsonValue.Create(guildId);

			JsonObject connStrings = EnsureObject(root, "ConnectionStrings");
			connStrings["Npgsql"] = JsonValue.Create(npgsqlDsn);

			root["ChatPollingIntervalSeconds"] = JsonValue.Create(pollInterval);
			root["BridgeMessageMaxLength"] = JsonValue.Create(maxMsgLen);

			JsonObject rateLimiting = EnsureObject(root, "RateLimiting");
			rateLimiting["MaxMessagesPerWindow"] = JsonValue.Create(maxPerWindow);
			rateLimiting["WindowSeconds"] = JsonValue.Create(windowSec);

			await WriteJsonObjectSecure(filePath, root);
			await Log.Info("FishMMOInstaller", $"{fileName} written and secured at: {filePath}");
		}

		private static async Task GenerateDiscordBotSecretsFile(string targetDir)
		{
			Console.WriteLine("Select output format:");
			Console.WriteLine("1 : fish shell snippet  (~/.config/fish/conf.d/fishmmo-secrets.fish)");
			Console.WriteLine("2 : systemd / .env file (fishmmo-secrets.env in target directory)");
				Console.WriteLine("3 : PowerShell / CMD snippet  (%USERPROFILE%\\fishmmo-secrets.ps1 or .cmd)");
			Console.WriteLine("0 : Back");

			ConsoleKeyInfo key = Console.ReadKey(true);
			Console.WriteLine();
			if (key.Key == ConsoleKey.D0 || key.KeyChar == '0') return;
			if (key.Key != ConsoleKey.D1 && key.Key != ConsoleKey.D2 && key.Key != ConsoleKey.D3) return;

			Console.WriteLine("Enter Discord Bot secret values to export as environment variables.");
			Console.WriteLine();

			string token = InstallerProcessHelper.PromptForPassword("  Discord Bot Token: ");
			string dsn = PromptNpgsqlDsn(existingDsn: null);

			var secrets = new Dictionary<string, string>
			{
				["Discord__Token"] = token,
				["ConnectionStrings__Npgsql"] = dsn,
			};

			switch (key.Key)
			{
				case ConsoleKey.D1: await WriteFishSecretsSnippet(secrets); break;
				case ConsoleKey.D2: await WriteSystemdEnvFile(targetDir, secrets); break;
				case ConsoleKey.D3: await WriteWindowsSecretsSnippet(secrets); break;
			}
		}

		// ──────────────────────────────────────────────────────────────────────────
		//  Component: CMS Server
		// ──────────────────────────────────────────────────────────────────────────

		private static async Task ConfigureCmsComponent()
		{
			string defaultDir = Path.Combine(FishMMODevRoot, "FishMMO-CMS", "FishMMO-CMS.Server");
			string? targetDir = PromptComponentDirectory(defaultDir);
			if (targetDir == null) return;

			await RunActionMenu("CMS Server", targetDir,
				writeBase: async dir => await WriteCmsSettings(dir, "appsettings.json"),
				writeEnvOverride: async dir =>
				{
					string? envName = PromptEnvironmentName();
					if (envName == null) return;
					await WriteCmsSettings(dir, $"appsettings.{envName}.json");
				},
				generateSecrets: GenerateCmsSecretsFile);
		}

		private static async Task WriteCmsSettings(string targetDir, string fileName)
		{
			string filePath = Path.Combine(targetDir, fileName);

			string? existingDsn = null;
			if (File.Exists(filePath))
			{
				try
				{
					string text = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
					JsonObject? existing = JsonNode.Parse(text)?.AsObject();
					existingDsn = existing?["ConnectionStrings"]?["DefaultConnection"]?.GetValue<string>();
				}
				catch { /* use defaults */ }
			}

			await Log.Info("FishMMOInstaller", $"Configuring: {filePath}");
			Console.WriteLine("Press Enter to keep the current value shown in brackets.");
			Console.WriteLine();

			string npgsqlDsn = PromptNpgsqlDsn(existingDsn);

			JsonObject root = await LoadOrCreateJsonObject(filePath);

			JsonObject connStrings = EnsureObject(root, "ConnectionStrings");
			connStrings["DefaultConnection"] = JsonValue.Create(npgsqlDsn);

			await WriteJsonObjectSecure(filePath, root);
			await Log.Info("FishMMOInstaller", $"{fileName} written and secured at: {filePath}");
		}

		private static async Task GenerateCmsSecretsFile(string targetDir)
		{
			Console.WriteLine("Select output format:");
			Console.WriteLine("1 : fish shell snippet  (~/.config/fish/conf.d/fishmmo-secrets.fish)");
			Console.WriteLine("2 : systemd / .env file (fishmmo-secrets.env in target directory)");
				Console.WriteLine("3 : PowerShell / CMD snippet  (%USERPROFILE%\\fishmmo-secrets.ps1 or .cmd)");
			Console.WriteLine("0 : Back");

			ConsoleKeyInfo key = Console.ReadKey(true);
			Console.WriteLine();
			if (key.Key == ConsoleKey.D0 || key.KeyChar == '0') return;
			if (key.Key != ConsoleKey.D1 && key.Key != ConsoleKey.D2 && key.Key != ConsoleKey.D3) return;

			Console.WriteLine("Enter the CMS database connection string secret.");
			Console.WriteLine();
			string dsn = PromptNpgsqlDsn(existingDsn: null);

			var secrets = new Dictionary<string, string>
			{
				["ConnectionStrings__DefaultConnection"] = dsn,
			};

			switch (key.Key)
			{
				case ConsoleKey.D1: await WriteFishSecretsSnippet(secrets); break;
				case ConsoleKey.D2: await WriteSystemdEnvFile(targetDir, secrets); break;
				case ConsoleKey.D3: await WriteWindowsSecretsSnippet(secrets); break;
			}
		}

		// ──────────────────────────────────────────────────────────────────────────
		//  Shared action-menu runner
		// ──────────────────────────────────────────────────────────────────────────

		private static async Task RunActionMenu(
			string componentName,
			string targetDir,
			Func<string, Task> writeBase,
			Func<string, Task> writeEnvOverride,
			Func<string, Task> generateSecrets)
		{
			Console.WriteLine($"Configure {componentName} at: {targetDir}");
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
				case ConsoleKey.D1: await writeBase(targetDir); break;
				case ConsoleKey.D2: await writeEnvOverride(targetDir); break;
				case ConsoleKey.D3: await generateSecrets(targetDir); break;
				case ConsoleKey.D0:
				case ConsoleKey.NumPad0:
					return;
				default:
					if (key.KeyChar == '0') return;
					break;
			}
		}

		// ──────────────────────────────────────────────────────────────────────────
		//  Secrets file writers (generalized — accept dict of envVar → value)
		// ──────────────────────────────────────────────────────────────────────────

		private static async Task WriteFishSecretsSnippet(Dictionary<string, string> secrets)
		{
			string homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			string fishConfDir = Path.Combine(homeDir, ".config", "fish", "conf.d");
			string filePath = Path.Combine(fishConfDir, "fishmmo-secrets.fish");

			Directory.CreateDirectory(fishConfDir);

			var sb = new StringBuilder();
			sb.AppendLine("# FishMMO secrets — written by FishMMO Installer");
			sb.AppendLine("# These environment variables override appsettings.json values.");
			sb.AppendLine("# .NET IConfiguration maps double-underscore __ to JSON nesting:");
			sb.AppendLine("#   Npgsql__Password  ->  appsettings.json : Npgsql.Password");
			sb.AppendLine();
			foreach (KeyValuePair<string, string> kvp in secrets)
			{
				if (!string.IsNullOrEmpty(kvp.Value))
					sb.AppendLine($"set -gx {kvp.Key} \"{EscapeFishString(kvp.Value)}\"");
			}

			await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8);
			await ApplySecurePermissions(filePath);

			await Log.Info("FishMMOInstaller", $"Fish secrets snippet written to: {filePath}");
			Console.WriteLine();
			Console.WriteLine($"Written to: {filePath}");
			Console.WriteLine("New shells will pick it up automatically.");
			Console.WriteLine($"To activate now, run:  source \"{filePath}\"");
		}

				private static async Task WriteWindowsSecretsSnippet(Dictionary<string, string> secrets)
		{
			string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

			// Write PowerShell profile snippet
			string psDir = Path.Combine(userProfile, "Documents", "WindowsPowerShell");
			Directory.CreateDirectory(psDir);
			string psPath = Path.Combine(psDir, "fishmmo-secrets.ps1");

			var ps = new StringBuilder();
			ps.AppendLine("# FishMMO secrets — written by FishMMO Installer");
			ps.AppendLine("# These environment variables override appsettings.json values.");
			ps.AppendLine("# .NET IConfiguration maps double-underscore __ to JSON nesting:");
			ps.AppendLine("#   Npgsql__Password  ->  appsettings.json : Npgsql.Password");
			ps.AppendLine();
			foreach (var kvp in secrets)
			{
				if (!string.IsNullOrEmpty(kvp.Value))
					ps.AppendLine($"$env:{kvp.Key} = \"{EscapePSString(kvp.Value)}\"");
			}
			ps.AppendLine();
			ps.AppendLine("# To auto-load, add this line to your PowerShell profile ($PROFILE):");
			ps.AppendLine($"#   . \"{psPath.Replace("\\", "\\")}\"");
			await File.WriteAllTextAsync(psPath, ps.ToString(), Encoding.UTF8);
			await Log.Info("FishMMOInstaller", $"PowerShell secrets snippet: {psPath}");

			// Write CMD batch snippet
			string cmdPath = Path.Combine(userProfile, "fishmmo-secrets.cmd");
			var cmd = new StringBuilder();
			cmd.AppendLine("@echo off");
			cmd.AppendLine("REM FishMMO secrets — written by FishMMO Installer");
			cmd.AppendLine("REM Run this script to set environment variables for the current CMD session.");
			cmd.AppendLine("REM For persistent settings, use: setx KEY VALUE");
			cmd.AppendLine();
			foreach (var kvp in secrets)
			{
				if (!string.IsNullOrEmpty(kvp.Value))
					cmd.AppendLine($"set {kvp.Key}={EscapeCmdString(kvp.Value)}");
			}
			await File.WriteAllTextAsync(cmdPath, cmd.ToString(), Encoding.UTF8);
			await Log.Info("FishMMOInstaller", $"CMD secrets snippet: {cmdPath}");

			Console.WriteLine();
			Console.WriteLine($"PowerShell: {psPath}");
			Console.WriteLine($"CMD:        {cmdPath}");
			Console.WriteLine("For persistent Windows environment variables, run from an elevated CMD:");
			Console.WriteLine("  setx FISHMMO_CLIENT_GATE_SECRET \"your-secret-here\"");
		}

		/// <summary>Escapes a value for PowerShell double-quoted string.</summary>
		private static string EscapePSString(string value) => value.Replace("\"", "`\"").Replace("$", "`$");

		/// <summary>Escapes a value for CMD set command (caret-escape special chars).</summary>
		private static string EscapeCmdString(string value) =>
			value.Replace("%", "%%").Replace("^", "^^").Replace("&", "^&")
				.Replace("<", "^<").Replace(">", "^>").Replace("|", "^|");

		private static async Task WriteSystemdEnvFile(string targetDir, Dictionary<string, string> secrets)
		{
			string filePath = Path.Combine(targetDir, "fishmmo-secrets.env");

			var sb = new StringBuilder();
			sb.AppendLine("# FishMMO secrets — written by FishMMO Installer");
			sb.AppendLine("# Use with: EnvironmentFile=/path/to/fishmmo-secrets.env in a systemd service unit,");
			sb.AppendLine("# or env_file: in a docker-compose service definition.");
			sb.AppendLine("# .NET IConfiguration maps double-underscore __ to JSON nesting:");
			sb.AppendLine("#   Npgsql__Password  ->  appsettings.json : Npgsql.Password");
			sb.AppendLine();
			foreach (KeyValuePair<string, string> kvp in secrets)
			{
				if (!string.IsNullOrEmpty(kvp.Value))
					sb.AppendLine($"{kvp.Key}={EscapeEnvFileValue(kvp.Value)}");
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
				    "Database": "fishmmo",
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
			s.Npgsql.Database = PromptString("  Database", defaults.Npgsql?.Database ?? "fishmmo");
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
			return s;
		}

		private static string? PromptComponentDirectory(string defaultDir)
		{
			Console.WriteLine();
			Console.WriteLine($"Target directory [{defaultDir}]:");
			Console.Write("  Press Enter to use default, or type a custom path (0 to cancel): ");
			string? input = Console.ReadLine()?.Trim();
			if (string.IsNullOrEmpty(input)) return defaultDir;
			if (input == "0") return null;
			return input;
		}

		private static string? PromptEnvironmentName()
		{
			Console.WriteLine("Select environment:");
			Console.WriteLine("1 : Development");
			Console.WriteLine("2 : Production");
			Console.WriteLine("0 : Back");
			ConsoleKeyInfo k = Console.ReadKey(true);
			Console.WriteLine();
			return k.Key switch
			{
				ConsoleKey.D1 => "Development",
				ConsoleKey.D2 => "Production",
				_ => null,
			};
		}

		/// <summary>
		/// Prompts for individual Npgsql connection string fields and returns a composed DSN.
		/// Used by web servers and other components that use ConnectionStrings format
		/// (e.g. <c>Host=...;Port=...;Database=...;Username=...;Password=...;Ssl Mode=Prefer;</c>).
		/// </summary>
		private static string PromptNpgsqlDsn(string? existingDsn)
		{
			Dictionary<string, string> parts = ParseNpgsqlDsn(existingDsn ?? string.Empty);
			Console.WriteLine();
			Console.WriteLine("--- PostgreSQL Connection String ---");
			string host = PromptString("  Host", parts.GetValueOrDefault("host", "127.0.0.1"));
			string port = PromptString("  Port", parts.GetValueOrDefault("port", "5432"));
			string db   = PromptString("  Database",
				parts.GetValueOrDefault("database",
				parts.GetValueOrDefault("initial catalog", "fishmmo")));
			string user = PromptString("  Username",
				parts.GetValueOrDefault("username",
				parts.GetValueOrDefault("user id", "user")));
			string pass = InstallerProcessHelper.PromptForPassword(
				$"  Password [{MaskSecret(parts.GetValueOrDefault("password", ""))}]: ");
			if (string.IsNullOrEmpty(pass))
				pass = parts.GetValueOrDefault("password", "change_me");
			string ssl = PromptString("  Ssl Mode (Disable / Prefer / Require / VerifyFull)",
				parts.GetValueOrDefault("ssl mode", "Prefer"));
			return $"Host={host};Port={port};Database={db};Username={user};Password={pass};Ssl Mode={ssl};";
		}

		private static Dictionary<string, string> ParseNpgsqlDsn(string dsn)
		{
			var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (string part in dsn.Split(';', StringSplitOptions.RemoveEmptyEntries))
			{
				int eq = part.IndexOf('=');
				if (eq > 0)
					result[part[..eq].Trim()] = part[(eq + 1)..].Trim();
			}
			return result;
		}

		private static ulong PromptUlong(string label, ulong defaultValue)
		{
			while (true)
			{
				Console.Write($"{label} [{defaultValue}]: ");
				string? input = Console.ReadLine();
				if (string.IsNullOrWhiteSpace(input)) return defaultValue;
				if (ulong.TryParse(input.Trim(), out ulong result)) return result;
				Console.WriteLine("  Enter a valid unsigned integer (e.g. Discord server/guild ID).");
			}
		}

		/// <summary>
		/// Loads an existing JSON file as a <see cref="JsonObject"/>, or returns an empty
		/// object if the file is absent or unparseable.
		/// </summary>
		private static async Task<JsonObject> LoadOrCreateJsonObject(string filePath)
		{
			if (!File.Exists(filePath)) return new JsonObject();
			try
			{
				string text = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
				return JsonNode.Parse(text)?.AsObject() ?? new JsonObject();
			}
			catch
			{
				return new JsonObject();
			}
		}

		/// <summary>
		/// Writes a <see cref="JsonObject"/> to <paramref name="filePath"/> with pretty-print
		/// formatting, creating parent directories as needed, and applies chmod 600 on Linux.
		/// </summary>
		private static async Task WriteJsonObjectSecure(string filePath, JsonObject root)
		{
			string? dir = Path.GetDirectoryName(filePath);
			if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
			await File.WriteAllTextAsync(filePath, root.ToJsonString(PrettyJson), Encoding.UTF8);
			await ApplySecurePermissions(filePath);
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