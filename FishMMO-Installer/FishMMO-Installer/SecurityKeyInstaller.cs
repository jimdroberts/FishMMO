using FishMMO.Database;
using FishMMO.Auth.Implementation;
using FishMMO.Logging;
using Npgsql;
using System.Security.Cryptography;
using System.Text;

namespace FishMMO.Installer
{
	/// <summary>
	/// Generates cryptographic keys for FishMMO deployments and stores them
	/// directly in the database using a superuser connection. No env files to
	/// copy between machines — every server loads its keys from the DB at startup.
	///
	/// Keys managed:
	///   - Gate secret → deployment_secrets table (key='client_gate_secret')
	///   - Connection token HMAC key → connection_token_keys table (key_id='shared')
	///   - Signing key KEK → deployment_secrets table (key='signing_key_kek')
	///   - TOTP master KEK → deployment_secrets table (key='totp_master_kek')
	///
	/// Client build files (ClientApiSecret.generated.cs, etc.) are generated
	/// separately from within the Unity Editor (FishMMO > Security > Fetch Client Secrets).
	///
	/// Uses a superuser NpgsqlConnection — matching the pattern established by
	/// InstallFishMMODatabase and GrantUserPermissions. The caller is responsible
	/// for obtaining the superuser credentials (via HandleWithSuperuser or the
	/// FISHMMO_PG_SUPERUSER_PASSWORD environment variable).
	/// </summary>
	public static class SecurityKeyInstaller
	{
		// ──────────────────────────────────────────────────────────────────
		//  Key generation primitives
		// ──────────────────────────────────────────────────────────────────

		/// <summary>
		/// Generates a cryptographically random key, base64-encoded.
		/// Uses RandomNumberGenerator.Fill (CSPRNG). Round-trip validates.
		/// Key material zeroed in finally block.
		/// </summary>
		public static string GenerateBase64Key(int byteLength = 32)
		{
			if (byteLength <= 0) throw new ArgumentOutOfRangeException(nameof(byteLength));
			byte[] keyBytes = new byte[byteLength];
			RandomNumberGenerator.Fill(keyBytes);
			try
			{
				string encoded = Convert.ToBase64String(keyBytes);
				byte[] decoded = Convert.FromBase64String(encoded);
				if (decoded.Length != byteLength)
					throw new InvalidOperationException(
						$"Key round-trip failed: expected {byteLength} bytes, got {decoded.Length}.");
				CryptographicOperations.ZeroMemory(decoded);
				return encoded;
			}
			finally { CryptographicOperations.ZeroMemory(keyBytes); }
		}

		// ──────────────────────────────────────────────────────────────────
		//  Database-backed key setup (called from Database menu / CLI)
		// ──────────────────────────────────────────────────────────────────

		/// <summary>
		/// Generates all deployment keys and stores them in the database using a
		/// superuser connection.
		///
		/// This is the primary entry point — called from the Database menu (option 9)
		/// and from the CLI --configure-server-secrets path.
		///
		/// Uses raw NpgsqlConnection + SQL upserts, matching the pattern used by
		/// <see cref="PostgreSQLInstaller.InstallFishMMODatabase"/>.
		/// </summary>
		/// <param name="superUsername">PostgreSQL superuser name.</param>
		/// <param name="superPassword">PostgreSQL superuser password.</param>
		/// <param name="appSettings">Application settings.</param>
		/// <param name="acceptDefaults">If true, generate all keys without prompting.</param>
		public static async Task ConfigureDatabaseKeysAsync(
			string superUsername, string superPassword, AppSettings appSettings,
			bool acceptDefaults = false)
		{
			await Log.Info("FishMMOInstaller", "=== Database Key Setup ===");
			Console.WriteLine();
			Console.WriteLine("=== Configure Server Keys (Database-Backed) ===");
			Console.WriteLine();
			Console.WriteLine("All keys are stored in the database. Every server loads them");
			Console.WriteLine("at startup. No env files to copy between machines.");
			Console.WriteLine();

			// ── Gate secret ──────────────────────────────────────────
			Console.WriteLine("── Gate Secret (X-FishMMO-Client HMAC signing) ──");
			string? gateSecret = PromptOrGenerate("FISHMMO_CLIENT_GATE_SECRET", "gate secret", acceptDefaults);

			// ── Shared connection token HMAC key ─────────────────────
			Console.WriteLine();
			Console.WriteLine("── Connection Token HMAC Key ──");
			Console.WriteLine("ONE key shared by ALL IpFetchServers. LoginServers load it from DB.");
			string? hmacKey = PromptOrGenerate(null, "connection token HMAC key", acceptDefaults);

			// ── Signing key KEK ──────────────────────────────────────
			Console.WriteLine();
			Console.WriteLine("── Signing Key KEK (AES-256) ──");
			Console.WriteLine("Wraps per-LoginServer auth token signing keys at rest in the DB.");
			string? kekKey = PromptOrGenerate("FISHMMO_SIGNING_KEY_KEK_BASE64", "KEK", acceptDefaults);

			Console.WriteLine();
			Console.WriteLine("── Writing to Database ──");

			// ── Connect as superuser and upsert keys ─────────────────
			string host = appSettings.Npgsql?.Host ?? "127.0.0.1";
			string port = appSettings.Npgsql?.Port ?? "5432";
			string dbName = appSettings.Npgsql?.Database ?? "fishmmo";
			string connStr = $"Host={host};Port={port};Database={dbName};Username={superUsername};Password={superPassword};Include Error Detail=true";

			bool dbOk = false;
			try
			{
				await using var conn = new NpgsqlConnection(connStr);
				await conn.OpenAsync();

				if (!string.IsNullOrWhiteSpace(gateSecret))
				{
					await UpsertSecretAsync(conn, "client_gate_secret", gateSecret);
					await Log.Info("FishMMOInstaller", "Gate secret → deployment_secrets table.");
				}

				if (!string.IsNullOrWhiteSpace(kekKey))
				{
					await UpsertSecretAsync(conn, "signing_key_kek", kekKey);
					await Log.Info("FishMMOInstaller", "KEK → deployment_secrets table.");
				}

				/* The TOTP master KEK wraps every account's authenticator secret at rest, so
				 * unlike the keys above it is INSERT-IF-ABSENT and never overwritten: replacing it
				 * would make every enrolled authenticator undecryptable in one step. Re-running
				 * this installer against a live deployment must be safe. */
				bool totpKekCreated = await InsertSecretIfAbsentAsync(conn, TotpMasterKek.DatabaseKey, TotpMasterKek.Generate());
				await Log.Info("FishMMOInstaller", totpKekCreated
					? "TOTP master KEK → deployment_secrets table (newly generated)."
					: "TOTP master KEK already present — left untouched (replacing it would lock out every authenticator).");

				if (!string.IsNullOrWhiteSpace(hmacKey))
				{
					await UpsertConnectionTokenKeyAsync(conn, "shared", hmacKey);
					await Log.Info("FishMMOInstaller", "Connection token key → connection_token_keys table.");
				}

				dbOk = true;
				Console.WriteLine("  All keys stored in database. ✓");
			}
			catch (Exception ex)
			{
				await Log.Warning("FishMMOInstaller", $"Database write failed: {ex.Message}");
				Console.WriteLine($"  Database error: {ex.Message}");
				Console.WriteLine("  Keys were generated but NOT stored in DB.");
			}

						// ── Client file generation moved to Unity Editor ────────────
			Console.WriteLine();
			Console.WriteLine("Client-side files (ClientApiSecret.generated.cs, etc.) are now");
			Console.WriteLine("generated from within the Unity Editor, not by the Installer.");
			Console.WriteLine("On a developer workstation with DB access, open Unity and run:");
			Console.WriteLine("  FishMMO > Security > Fetch Client Secrets");

			if (!dbOk)
			{
				Console.WriteLine();
				Console.WriteLine("ERROR: Could not write keys to database.");
				Console.WriteLine("Verify the superuser credentials and that the database is running.");
			}

			Console.WriteLine();
			Console.WriteLine("Done. Every server loads these keys from the database at startup.");
		}

		// ──────────────────────────────────────────────────────────────────
		//  Raw SQL upsert helpers (superuser connection)
		// ──────────────────────────────────────────────────────────────────

		private static async Task UpsertSecretAsync(NpgsqlConnection conn, string key, string value)
		{
			await using var cmd = new NpgsqlCommand(
				"INSERT INTO deployment_secrets (key, value, created_at, updated_at) " +
				"VALUES (@key, @value, NOW(), NOW()) " +
				"ON CONFLICT (key) DO UPDATE SET value = @value, updated_at = NOW()", conn);
			cmd.Parameters.AddWithValue("key", key);
			cmd.Parameters.AddWithValue("value", value);
			await cmd.ExecuteNonQueryAsync();
		}

		/// <summary>
		/// Inserts a deployment secret only when the row does not already exist.
		/// </summary>
		/// <remarks>
		/// For key material that other rows are encrypted under, an upsert is destructive: the old
		/// value is the only thing that can decrypt what is already stored. Rotation for those keys
		/// is a re-wrap operation, not an overwrite, so provisioning must never clobber.
		/// </remarks>
		/// <returns><c>true</c> when a new row was created; <c>false</c> when one already existed.</returns>
		private static async Task<bool> InsertSecretIfAbsentAsync(NpgsqlConnection conn, string key, string value)
		{
			await using var cmd = new NpgsqlCommand(
				"INSERT INTO deployment_secrets (key, value, created_at, updated_at) " +
				"VALUES (@key, @value, NOW(), NOW()) " +
				"ON CONFLICT (key) DO NOTHING", conn);
			cmd.Parameters.AddWithValue("key", key);
			cmd.Parameters.AddWithValue("value", value);
			return await cmd.ExecuteNonQueryAsync() > 0;
		}

		private static async Task UpsertConnectionTokenKeyAsync(NpgsqlConnection conn, string keyId, string hmacKeyBase64)
		{
			await using var cmd = new NpgsqlCommand(
				"INSERT INTO connection_token_keys (key_id, hmac_key_base64, is_active, time_created) " +
				"VALUES (@keyId, @hmacKeyBase64, true, NOW()) " +
				"ON CONFLICT (key_id) DO UPDATE SET hmac_key_base64 = @hmacKeyBase64", conn);
			cmd.Parameters.AddWithValue("keyId", keyId);
			cmd.Parameters.AddWithValue("hmacKeyBase64", hmacKeyBase64);
			await cmd.ExecuteNonQueryAsync();
		}

		// ──────────────────────────────────────────────────────────────────
		//  Helpers
		// ──────────────────────────────────────────────────────────────────

		private static string? PromptOrGenerate(string? envVarName, string description, bool acceptDefaults)
		{
			if (envVarName != null)
			{
				string? existing = Environment.GetEnvironmentVariable(envVarName);
				if (!string.IsNullOrWhiteSpace(existing))
				{
					Console.WriteLine($"Using existing {envVarName} ({existing.Length} chars).");
					return existing;
				}
			}
			if (acceptDefaults)
			{
				string key = GenerateBase64Key();
				Console.WriteLine($"Generated new {description} ({key.Length} chars).");
				return key;
			}
			Console.Write($"Generate new {description}? [Y/n]: ");
			string? resp = Console.ReadLine()?.Trim();
			if (!string.IsNullOrEmpty(resp) && !resp.Equals("y", StringComparison.OrdinalIgnoreCase))
			{
				Console.WriteLine($"Skipping {description}.");
				return null;
			}
			string key2 = GenerateBase64Key();
			Console.WriteLine($"Generated new {description} ({key2.Length} chars).");
			return key2;
		}
	}
}
