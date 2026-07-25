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
	/// Database credentials (Username/Password) are NOT stored in appsettings.json.
	/// They are resolved at runtime via the <c>DatabaseSecrets</c> class from
	/// environment variables (<c>FISHMMO_DB_USERNAME</c>, <c>FISHMMO_DB_PASSWORD</c>)
	/// or the platform secrets file (<c>/etc/fishmmo/db-secrets.env</c> on Linux).
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

			Console.WriteLine("Select web server to configure:");
			Console.WriteLine("1 : IPFetch Web Server         (Login gate + IP fetch)");
			Console.WriteLine("2 : Patcher Web Server         (Patch distribution)");
			Console.WriteLine("3 : WebGL Web Server           (WebGL client host)");
			Console.WriteLine("0 : Back");

			ConsoleKeyInfo ckey = Console.ReadKey(true);
			Console.WriteLine();

			switch (ckey.Key)
			{
				case ConsoleKey.D1:
					await ConfigureWebServerComponent("IPFetch", "IPFetchASP.NET", "IpFetchServer",
						defaultPort: 8080, hasNpgsqlDsn: true, hasPatches: false);
					break;
				case ConsoleKey.D2:
					await ConfigureWebServerComponent("Patcher", "PatcherASP.NET", "Patcher",
						defaultPort: 8090, hasNpgsqlDsn: false, hasPatches: true);
					break;
				case ConsoleKey.D3:
					await ConfigureWebServerComponent("WebGL", "WebGLServerASP.NET", "WebGLServer",
						defaultPort: 8000, hasNpgsqlDsn: false, hasPatches: false);
					break;
				case ConsoleKey.D0:
				case ConsoleKey.NumPad0:
					return;
				default:
					if (ckey.KeyChar == '0') return;
					break;
			}
		}

		//──────────────────────────────────────────────────────────────────────────
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
				[ClientGateSecretEnvVar] = GenerateBase64Key(),
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

		/// <summary>
		/// Generates a cryptographically random key, base64-encoded.
		/// Uses <see cref="RandomNumberGenerator.Fill"/> (CSPRNG), the same primitive
		/// as <c>CryptoHelper.GenerateKey</c>. The returned string round-trips through
		/// <c>Convert.FromBase64String</c> to the exact byte length specified.
		/// Key material is zeroed in a finally block.
		/// </summary>
		/// <param name="byteLength">Number of random bytes to generate (default 32 for AES-256 / HMAC-SHA256).</param>
		/// <returns>Base64-encoded key string.</returns>
		/// <exception cref="ArgumentOutOfRangeException">If <paramref name="byteLength"/> is not positive.</exception>
		private static string GenerateBase64Key(int byteLength = 32)
		{
			if (byteLength <= 0) throw new ArgumentOutOfRangeException(nameof(byteLength));
			byte[] keyBytes = new byte[byteLength];
			RandomNumberGenerator.Fill(keyBytes);
			try
			{
				string encoded = Convert.ToBase64String(keyBytes);
				// Round-trip validation: the generated key must decode correctly.
				byte[] decoded = Convert.FromBase64String(encoded);
				if (decoded.Length != byteLength)
					throw new InvalidOperationException(
						$"Generated key round-trip failed: expected {byteLength} bytes, got {decoded.Length}.");
				CryptographicOperations.ZeroMemory(decoded);
				return encoded;
			}
			finally
			{
				CryptographicOperations.ZeroMemory(keyBytes);
			}
		}

		// ──────────────────────────────────────────────────────────────────────────
		//  Component: Discord Bot
		// ──────────────────────────────────────────────────────────────────────────

		public static async Task ConfigureDiscordBotComponent()
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

			string token = InstallerProcessHelper.PromptForRequiredPassword("  Discord Bot Token: ");
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

		public static async Task ConfigureCmsComponent()
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
		//  Component: Client Security Files (generated .cs files for Unity)
		// ──────────────────────────────────────────────────────────────────────────

		/// <summary>
		/// Generates the IL-embedded client security and host configuration files
		/// in the Unity project. Covers:
		///   - Gate secret (X-FishMMO-Client HMAC signing)
		///   - Connection token HMAC key (shared across IpFetch + all game servers)
		///   - Signing key KEK (wraps auth token signing keys at rest)
		///   - Deployment domains (API, Game, Play hosts, root domain)
		///   - Certificate pins (sentinel placeholders or env-var-provided)
		/// </summary>
		public static async Task ConfigureClientSecurityFiles()
		{
			string unityProjectPath = InstallationConstants.FishMMOUnityProjectPath;
			string securityDir = Path.Combine(unityProjectPath, "Assets", "Scripts", "Client", "Security");

			if (!Directory.Exists(securityDir))
			{
				await Log.Error("FishMMOInstaller",
					$"Unity project security directory not found: {securityDir}\n" +
					"Ensure the FishMMO-Unity project exists and the path is correct.");
				return;
			}

			await Log.Info("FishMMOInstaller", "=== Client Security & Host Configuration ===");
			Console.WriteLine();
			Console.WriteLine($"Unity project: {unityProjectPath}");
			Console.WriteLine();

			// ── Gate secret ──────────────────────────────────────────────
			Console.WriteLine("── Gate Secret (X-FishMMO-Client HMAC signing) ──");
			Console.WriteLine("The gate secret is shared between the client binary and the");
			Console.WriteLine("public web endpoints (IpFetch, Patcher, WebGL).");
			Console.WriteLine();

			string? gateSecret = null;
			string? envSecret = Environment.GetEnvironmentVariable("FISHMMO_CLIENT_GATE_SECRET");
			if (!string.IsNullOrWhiteSpace(envSecret))
			{
				Console.WriteLine($"Using existing FISHMMO_CLIENT_GATE_SECRET env var ({envSecret.Length} chars).");
				gateSecret = envSecret;
			}
			else
			{
				Console.Write("Generate a new gate secret? [Y/n]: ");
				string? response = Console.ReadLine()?.Trim();
				if (!string.IsNullOrEmpty(response) &&
					!response.Equals("y", StringComparison.OrdinalIgnoreCase) &&
					!response.Equals("yes", StringComparison.OrdinalIgnoreCase))
				{
					Console.WriteLine("Skipping gate secret generation.");
				}
				else
				{
					gateSecret = GenerateBase64Key();
					Console.WriteLine($"Generated new gate secret ({gateSecret.Length} chars).");
				}
			}

			// Write ClientApiSecret.generated.cs
			if (!string.IsNullOrWhiteSpace(gateSecret))
			{
				string secretFilePath = Path.Combine(securityDir, "ClientApiSecret.generated.cs");
				await WriteClientApiSecretGeneratedFile(secretFilePath, gateSecret);
				await Log.Info("FishMMOInstaller", $"Client API secret written to: {secretFilePath}");
			}

			// ── Connection Token HMAC Key ────────────────────────────────
			Console.WriteLine();
			Console.WriteLine("── Connection Token HMAC Key ──");
			Console.WriteLine("This key signs connection tokens shared between IpFetchServer");
			Console.WriteLine("and all game servers. It MUST be identical across all servers.");
			Console.WriteLine();

			string? hmacKey = null;
			string? envHmac = Environment.GetEnvironmentVariable("FISHMMO_CONNECTION_TOKEN_HMAC_KEY_BASE64");
			if (!string.IsNullOrWhiteSpace(envHmac))
			{
				Console.WriteLine($"Using existing FISHMMO_CONNECTION_TOKEN_HMAC_KEY_BASE64 env var ({envHmac.Length} chars).");
				hmacKey = envHmac;
			}
			else
			{
				Console.Write("Generate a new HMAC key? [Y/n]: ");
				string? resp = Console.ReadLine()?.Trim();
				if (string.IsNullOrEmpty(resp) || resp.Equals("y", StringComparison.OrdinalIgnoreCase))
				{
					hmacKey = GenerateBase64Key();
					Console.WriteLine($"Generated new HMAC key ({hmacKey.Length} chars).");
				}
			}

			// ── Signing Key KEK ──────────────────────────────────────────
			Console.WriteLine();
			Console.WriteLine("── Signing Key KEK (AES-256 Key Encryption Key) ──");
			Console.WriteLine("This key wraps auth token signing keys at rest in the database.");
			Console.WriteLine();

			string? kekKey = null;
			string? envKek = Environment.GetEnvironmentVariable("FISHMMO_SIGNING_KEY_KEK_BASE64");
			if (!string.IsNullOrWhiteSpace(envKek))
			{
				Console.WriteLine($"Using existing FISHMMO_SIGNING_KEY_KEK_BASE64 env var ({envKek.Length} chars).");
				kekKey = envKek;
			}
			else
			{
				Console.Write("Generate a new KEK? [Y/n]: ");
				string? resp2 = Console.ReadLine()?.Trim();
				if (string.IsNullOrEmpty(resp2) || resp2.Equals("y", StringComparison.OrdinalIgnoreCase))
				{
					kekKey = GenerateBase64Key();
					Console.WriteLine($"Generated new KEK ({kekKey.Length} chars).");
				}
			}

			// Collect all secrets for unified export.
			var allSecrets = new Dictionary<string, string>();
			if (!string.IsNullOrWhiteSpace(gateSecret))
				allSecrets["FISHMMO_CLIENT_GATE_SECRET"] = gateSecret;
			if (!string.IsNullOrWhiteSpace(hmacKey))
				allSecrets["FISHMMO_CONNECTION_TOKEN_HMAC_KEY_BASE64"] = hmacKey;
			if (!string.IsNullOrWhiteSpace(kekKey))
				allSecrets["FISHMMO_SIGNING_KEY_KEK_BASE64"] = kekKey;

			// Offer unified secrets export (only if we have more than just the gate secret).
			if (allSecrets.Count > 1)
			{
				Console.WriteLine();
				Console.WriteLine("Export secrets as environment variables?");
				Console.WriteLine("1 : fish shell snippet");
				Console.WriteLine("2 : systemd / .env file");
				Console.WriteLine("3 : Skip export");
				ConsoleKeyInfo exportKey = Console.ReadKey(true);
				Console.WriteLine();
				switch (exportKey.Key)
				{
					case ConsoleKey.D1: await WriteFishSecretsSnippet(allSecrets); break;
					case ConsoleKey.D2: await WriteSystemWideEnvFile(allSecrets); break;
				}
			}

			// ── Domain configuration ────────────────────────────────────
			Console.WriteLine();
			Console.WriteLine("── Deployment Domains ──");
			Console.WriteLine("Configure the hostnames for this deployment.");
			Console.WriteLine("Press Enter to keep the default shown in brackets.");
			Console.WriteLine();

			Console.Write("  Root domain    [fishmmo.com]: ");
			string rootDomain = Console.ReadLine()?.Trim() ?? "";
			if (string.IsNullOrWhiteSpace(rootDomain)) rootDomain = "fishmmo.com";

			string defaultApi = "https://api." + rootDomain + "/";
			Console.Write($"  API host       [{defaultApi}]: ");
			string apiHost = Console.ReadLine()?.Trim() ?? "";
			if (string.IsNullOrWhiteSpace(apiHost)) apiHost = defaultApi;
			if (!apiHost.EndsWith("/")) apiHost += "/";

			string defaultGame = "game." + rootDomain;
			Console.Write($"  Game host      [{defaultGame}]: ");
			string gameHost = Console.ReadLine()?.Trim() ?? "";
			if (string.IsNullOrWhiteSpace(gameHost)) gameHost = defaultGame;

			string defaultPlay = "play." + rootDomain;
			Console.Write($"  Play host      [{defaultPlay}]: ");
			string playHost = Console.ReadLine()?.Trim() ?? "";
			if (string.IsNullOrWhiteSpace(playHost)) playHost = defaultPlay;

			// Write HostConfig.generated.cs
			string sharedImplDir = Path.Combine(unityProjectPath, "Assets", "Scripts", "Shared", "Implementation");
			string hostConfigPath = Path.Combine(sharedImplDir, "HostConfig.generated.cs");
			await WriteHostConfigGeneratedFile(hostConfigPath, apiHost, gameHost, playHost, rootDomain);
			await Log.Info("FishMMOInstaller", $"Host config written to: {hostConfigPath}");

			// ── Certificate pins ────────────────────────────────────────
			Console.WriteLine();
			Console.WriteLine("── TLS Certificate Pins ──");
			Console.WriteLine();
			Console.WriteLine("Certificate pin generation requires live TLS connections to the");
			Console.WriteLine("server hosts. The Installer cannot fetch pins automatically.");
			Console.WriteLine();
			Console.WriteLine("To generate real pins:");
			Console.WriteLine("  1. Open the Unity project");
			Console.WriteLine("  2. FishMMO > Security > Fetch Certificate Pins");
			Console.WriteLine("  3. Click 'Fetch Pins' then 'Write to CertificatePins.generated.cs'");
			Console.WriteLine();
			Console.WriteLine("For CI: set FISHMMO_PIN_ACTIVE and FISHMMO_PIN_BACKUP env vars");
			Console.WriteLine("before building; the CI substitution script will replace sentinels.");
			Console.WriteLine();

			string pinsFilePath = Path.Combine(securityDir, "CertificatePins.generated.cs");
			string? pinActive = Environment.GetEnvironmentVariable("FISHMMO_PIN_ACTIVE");
			string? pinBackup = Environment.GetEnvironmentVariable("FISHMMO_PIN_BACKUP");

			if (!string.IsNullOrWhiteSpace(pinActive) && !string.IsNullOrWhiteSpace(pinBackup))
			{
				Console.WriteLine("FISHMMO_PIN_ACTIVE and FISHMMO_PIN_BACKUP env vars found.");
				await WriteCertificatePinsGeneratedFile(pinsFilePath, pinActive, pinBackup);
				await Log.Info("FishMMOInstaller", $"Certificate pins written to: {pinsFilePath}");
			}
			else
			{
				Console.WriteLine("Writing sentinel placeholder pin file (replace before release build).");
				await WriteCertificatePinsGeneratedFile(pinsFilePath,
					"FISHMMO_SENTINEL_PLACEHOLDER_ACTIVE_PIN",
					"FISHMMO_SENTINEL_PLACEHOLDER_BACKUP_PIN");
				await Log.Info("FishMMOInstaller", $"Sentinel pin file written to: {pinsFilePath}");
			}

			Console.WriteLine();
			Console.WriteLine("Done. Generated files:");
			Console.WriteLine($"  {Path.Combine(securityDir, "ClientApiSecret.generated.cs")}");
			Console.WriteLine($"  {pinsFilePath}");
			Console.WriteLine($"  {hostConfigPath}");
			Console.WriteLine();
			Console.WriteLine("Next steps:");
			Console.WriteLine("  1. Open the Unity project to confirm it compiles");
			Console.WriteLine("  2. Generate real certificate pins (see instructions above)");
			Console.WriteLine("  3. The build validator will block release builds until pins are real");
		}

		/// <summary>
		/// <summary>
		/// Writes <c>ClientApiSecret.generated.cs</c> with the given secret.
		/// </summary>
		private static async Task WriteClientApiSecretGeneratedFile(string filePath, string secret)
		{
			var sb = new StringBuilder();
			sb.AppendLine("// ═══════════════════════════════════════════════════════════════════════════════");
			sb.AppendLine("// AUTO-GENERATED by FishMMO-Installer — CI may override from FISHMMO_CLIENT_GATE_SECRET.");
			sb.AppendLine("// Generated at: " + DateTime.UtcNow.ToString("O"));
			sb.AppendLine("// ═══════════════════════════════════════════════════════════════════════════════");
			sb.AppendLine("//");
			sb.AppendLine("// Sentinel check: the build validator (ClientSecurityBuildValidator) blocks");
			sb.AppendLine("// non-development builds that contain the FISHMMO_SENTINEL_PLACEHOLDER marker.");
			sb.AppendLine("// CI must replace every occurrence before invoking Unity.");
			sb.AppendLine();
			sb.AppendLine("namespace FishMMO.Client.Security");
			sb.AppendLine("{");
			sb.AppendLine("\t/// <summary>");
			sb.AppendLine("\t/// IL-embedded client gate secret. The real value is substituted at");
			sb.AppendLine("\t/// build time by CI from the FISHMMO_CLIENT_GATE_SECRET env var.");
			sb.AppendLine("\t/// </summary>");
			sb.AppendLine("\tinternal static class GeneratedClientSecret");
			sb.AppendLine("\t{");
			sb.AppendLine("\t\t/// <summary>");
			sb.AppendLine("\t\t/// Shared secret for X-FishMMO-Client HMAC header signing.");
			sb.AppendLine("\t\t/// The committed value is a sentinel — CI must replace it.");
			sb.AppendLine("\t\t/// </summary>");
			sb.AppendLine("\t\tinternal const string Secret = \"" + secret + "\";");
			sb.AppendLine("\t}");
			sb.AppendLine("}");

			string? dir = Path.GetDirectoryName(filePath);
			if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
			await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8);
			await ApplySecurePermissions(filePath);
		}

		/// <summary>
		/// Writes <c>CertificatePins.generated.cs</c> with the given pin values.
		/// </summary>
		private static async Task WriteCertificatePinsGeneratedFile(string filePath, string pinActive, string pinBackup)
		{
			var sb = new StringBuilder();
			sb.AppendLine("// ═══════════════════════════════════════════════════════════════════════════════");
			sb.AppendLine("// AUTO-GENERATED by FishMMO-Installer — CI may override from FISHMMO_PIN_* env vars.");
			sb.AppendLine("// Generated at: " + DateTime.UtcNow.ToString("O"));
			sb.AppendLine("// ═══════════════════════════════════════════════════════════════════════════════");
			sb.AppendLine("//");
			sb.AppendLine("// Sentinel check: the build validator (ClientSecurityBuildValidator) blocks");
			sb.AppendLine("// non-development builds that contain the FISHMMO_SENTINEL_PLACEHOLDER marker.");
			sb.AppendLine("// CI must replace every occurrence before invoking Unity.");
			sb.AppendLine();
			sb.AppendLine("namespace FishMMO.Client.Security");
			sb.AppendLine("{");
			sb.AppendLine("\t/// <summary>");
			sb.AppendLine("\t/// IL-embedded certificate pin set. The real values are substituted at");
			sb.AppendLine("\t/// build time by CI. The committed sentinel values are intentionally");
			sb.AppendLine("\t/// invalid so pinning cannot accidentally ship with empty values.");
			sb.AppendLine("\t/// </summary>");
			sb.AppendLine("\tinternal static class GeneratedPinSet");
			sb.AppendLine("\t{");
			sb.AppendLine("\t\tinternal const string SentinelMarker = \"FISHMMO_SENTINEL_PLACEHOLDER\";");
			sb.AppendLine();
			sb.AppendLine("\t\t/// <summary>");
			sb.AppendLine("\t\t/// SHA-256 SPKI pins (base64). Minimum 2 entries required for");
			sb.AppendLine("\t\t/// release builds — one active key + one backup for rotation.");
			sb.AppendLine("\t\t/// </summary>");
			sb.AppendLine("\t\tinternal static readonly string[] Pins =");
			sb.AppendLine("\t\t{");
			sb.AppendLine("\t\t\t\"" + pinActive + "\",");
			sb.AppendLine("\t\t\t\"" + pinBackup + "\",");
			sb.AppendLine("\t\t};");
			sb.AppendLine();
			sb.AppendLine("\t\t/// <summary>");
			sb.AppendLine("\t\t/// Ed25519 public key (base64) for verifying signed pin update");
			sb.AppendLine("\t\t/// manifests from the API. Empty string disables runtime updates.");
			sb.AppendLine("\t\t/// </summary>");
			sb.AppendLine("\t\tinternal const string ManifestPublicKeyBase64 = \"\";");
			sb.AppendLine("\t}");
			sb.AppendLine("}");

			string? dir = Path.GetDirectoryName(filePath);
			if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
			await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8);
		}

		/// <summary>
		/// Writes <c>HostConfig.generated.cs</c> with the configured domain values.
		/// </summary>
		private static async Task WriteHostConfigGeneratedFile(
			string filePath, string apiHost, string gameHost, string playHost, string rootDomain)
		{
			string smtpFromAddress = "noreply@" + rootDomain;
			string launcherUrl = "https://www." + rootDomain + "/docs/introduction.html";

			var sb = new StringBuilder();
			sb.AppendLine("// ═══════════════════════════════════════════════════════════════════════════════");
			sb.AppendLine("// AUTO-GENERATED by FishMMO-Installer — CI may override from FISHMMO_* env vars.");
			sb.AppendLine("// Generated at: " + DateTime.UtcNow.ToString("O"));
			sb.AppendLine("// ═══════════════════════════════════════════════════════════════════════════════");
			sb.AppendLine();
			sb.AppendLine("namespace FishMMO.Shared");
			sb.AppendLine("{");
			sb.AppendLine("\tpublic static class GeneratedHostConfig");
			sb.AppendLine("\t{");
			sb.AppendLine("\t\tinternal const string SentinelMarker = \"FISHMMO_SENTINEL_PLACEHOLDER\";");
			sb.AppendLine();
			sb.AppendLine("\t\tinternal const string ApiHost = \"" + apiHost + "\";");
			sb.AppendLine("\t\tinternal const string GameHost = \"" + gameHost + "\";");
			sb.AppendLine("\t\tinternal const string PlayHost = \"" + playHost + "\";");
			sb.AppendLine("\t\tinternal const string RootDomain = \"" + rootDomain + "\";");
			sb.AppendLine("\t\tinternal const string SmtpFromAddress = \"" + smtpFromAddress + "\";");
			sb.AppendLine("\t\tinternal const string SmtpFromName = \"FishMMO\";");
			sb.AppendLine("\t\tinternal const string LauncherHtmlUrl = \"" + launcherUrl + "\";");
			sb.AppendLine("\t}");
			sb.AppendLine("}");

			string? dir = Path.GetDirectoryName(filePath);
			if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
			await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8);
			await ApplySecurePermissions(filePath);
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
			sb.AppendLine("# Database credentials resolved at runtime via DatabaseSecrets (env vars or /etc/fishmmo/db-secrets.env)");
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
			ps.AppendLine("# Database credentials resolved at runtime via DatabaseSecrets (env vars or /etc/fishmmo/db-secrets.env)");
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
			sb.AppendLine("# Database credentials resolved at runtime via DatabaseSecrets (env vars or /etc/fishmmo/db-secrets.env)");
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
		/// Sets file permissions to 600 (owner read/write only) on Linux.
		/// No-ops silently on non-Linux platforms.
		/// </summary>

	/// <summary>
	/// Resolves the system-wide secrets file path shared by all FishMMO services.
	/// Linux: /etc/fishmmo/secrets.env. Windows: %ProgramData%\FishMMO\secrets.env.
	/// All systemd units reference this single file so the HMAC key, KEK, and
	/// gate secret are guaranteed identical across every service.
	/// </summary>
	private static string GetSystemWideSecretsPath()
	{
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			string programData = Environment.GetEnvironmentVariable("ProgramData") ?? @"C:\ProgramData";
			return Path.Combine(programData, "FishMMO", "secrets.env");
		}
		return "/etc/fishmmo/secrets.env";
	}

	/// <summary>
	/// Writes secrets to the system-wide path shared by all FishMMO services.
	/// On Linux this is /etc/fishmmo/secrets.env (chmod 600).
	/// All systemd units reference this single file, guaranteeing the HMAC key,
	/// KEK, and gate secret are identical across IpFetchServer, Patcher,
	/// WebGLServer, LoginServer, WorldServer, and SceneServer.
	/// </summary>
	private static async Task WriteSystemWideEnvFile(Dictionary<string, string> secrets)
	{
		string filePath = GetSystemWideSecretsPath();
		string? dir = Path.GetDirectoryName(filePath);

		if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
		{
			try { Directory.CreateDirectory(dir); }
			catch (UnauthorizedAccessException)
			{
				await Log.Warning("FishMMOInstaller",
					$"Cannot create {dir}. Writing to current directory instead.");
				filePath = Path.Combine(Directory.GetCurrentDirectory(), "fishmmo-secrets.env");
			}
		}

		var sb = new StringBuilder();
		sb.AppendLine("# FishMMO system-wide secrets — shared by all services.");
		sb.AppendLine("# Written by FishMMO-Installer at " + DateTime.UtcNow.ToString("O"));
		sb.AppendLine("#");
		sb.AppendLine("# ALL FishMMO systemd units reference this file:");
		sb.AppendLine("#   EnvironmentFile=-" + filePath);
		sb.AppendLine("#");
		sb.AppendLine("# WARNING: This file contains secrets. Keep permissions at 600.");
		sb.AppendLine("# Deploy this EXACT same file to every server in the cluster.");
		sb.AppendLine();
		foreach (KeyValuePair<string, string> kvp in secrets)
		{
			if (!string.IsNullOrEmpty(kvp.Value))
				sb.AppendLine($"{kvp.Key}={EscapeEnvFileValue(kvp.Value)}");
		}

		await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8);
		await ApplySecurePermissions(filePath);

		await Log.Info("FishMMOInstaller", $"System-wide secrets written to: {filePath}");
		Console.WriteLine();
		Console.WriteLine($"System-wide secrets written to: {filePath}");
		Console.WriteLine("All FishMMO systemd services reference this single file.");
		Console.WriteLine("Copy this file to every server in the deployment cluster.");
	}

		internal static async Task ApplySecurePermissions(string filePath)
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


		// ---------------------------------------------------------------------------
		//  Prompt helpers
		// ---------------------------------------------------------------------------


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
				parts.GetValueOrDefault("user id", "fishmmo")));
			string pass = InstallerProcessHelper.PromptForRequiredPassword(
				$"  Password [{MaskSecret(parts.GetValueOrDefault("password", ""))}]: ");
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