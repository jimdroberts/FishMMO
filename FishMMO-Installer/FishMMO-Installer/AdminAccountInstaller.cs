using FishMMO.Auth.Implementation;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Logging;
using FishMMO.Shared;
using Microsoft.Extensions.Configuration;
using SecureRemotePassword;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using DbAccessLevel = FishMMO.Database.Data.Enums.AccessLevel;
using DbChannels = FishMMO.Database.Data.Enums.AccountVerificationChannels;

namespace FishMMO.Installer
{
	/// <summary>
	/// Creates an Administrator account from the console: Database ▸ Create Admin Account.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A fresh deployment has nobody who can sign in to the Control Panel, and with the beta gate on
	/// nobody can register either, because registration needs a beta code that only an administrator
	/// can mint. This is the way in.
	/// </para>
	/// <para>
	/// The account must be indistinguishable from one the game or the Control Panel registered, so the
	/// steps are theirs (<c>AccountRegistrationService.RegisterAsync</c> and
	/// <c>SelfServiceService.EnrolAsync</c>), through the same database services: an SRP salt and
	/// verifier from the fixed identity and the game client's parameters, the profile with email as its
	/// channel, verified without a code (an operator at the server's console needs none), and two-factor
	/// enrolled: a TOTP secret encrypted under the deployment master key, enabled, and the hashed
	/// recovery codes. The Control Panel's Operator policies require Admin AND two-factor, so an
	/// administrator without it could sign in but not mint a beta code. Unlike a registration, the
	/// writes are one transaction, so a failure leaves no half-made administrator behind. The level
	/// change is recorded in the audit log, as every caller of PersistAccessLevelAsync must.
	/// </para>
	/// <para>
	/// The connection is the servers' own (the installer's appsettings.json Npgsql section with the
	/// credentials from FISHMMO_DB_USERNAME / FISHMMO_DB_PASSWORD or the secrets file), not a
	/// superuser: creating an account is an application write.
	/// </para>
	/// </remarks>
	public static class AdminAccountInstaller
	{
		/// <summary>Audit log <c>source</c> for rows this step writes (the column holds 16 characters).</summary>
		public const string AuditSource = "installer";

		/// <summary>Audit log action for an administrator created here.</summary>
		public const string AuditAction = "account.create-admin";

		/// <summary>Registration's age bounds (AccountRegistrationService).</summary>
		private const int MinAge = 13;
		private const int MaxAge = 120;

		/// <summary>Database ▸ Create Admin Account.</summary>
		public static async Task CreateAdminAccountAsync()
		{
			Console.WriteLine("=== Create Admin Account ===");
			Console.WriteLine();
			Console.WriteLine("Creates an Administrator account for the Control Panel and the game: verified, with");
			Console.WriteLine("two-factor authentication. Use it to sign in to the Control Panel and mint beta codes.");
			Console.WriteLine("The authenticator key and recovery codes are shown ONCE at the end; have an");
			Console.WriteLine("authenticator app ready (any TOTP app: Aegis, Google Authenticator, 1Password, ...).");
			Console.WriteLine();

			Database.Database database;
			try
			{
				IConfiguration configuration = DatabaseConfigurationHelper.BuildDesignTimeConfiguration(AppContext.BaseDirectory);
				database = new Database.Database(configuration);
			}
			catch (Exception ex)
			{
				await Log.Error("FishMMOInstaller", $"Could not set up the database connection: {ex.Message}");
				return;
			}

			await using (database)
			{
				var registry = database.ServiceRegistry;
				if (!registry.TryGet(out IAccountService accounts) ||
					!registry.TryGet(out ITwoFactorRecoveryCodeService recoveryCodes) ||
					!registry.TryGet(out IUnitOfWorkService unitOfWork) ||
					!registry.TryGet(out IDeploymentSecretService secrets) ||
					!registry.TryGet(out IAdminAuditService audit))
				{
					await Log.Error("FishMMOInstaller", "A database service this step needs is not registered. Nothing was changed.");
					return;
				}

				// ── The TOTP master key, before asking anything ─────────────────────
				byte[]? masterKek = await LoadMasterKekAsync(secrets);
				if (masterKek == null) return;

				try
				{
					await CreateAsync(accounts, recoveryCodes, unitOfWork, audit, masterKek);
				}
				finally
				{
					CryptographicOperations.ZeroMemory(masterKek);
				}
			}
		}

		private static async Task<byte[]?> LoadMasterKekAsync(IDeploymentSecretService secrets)
		{
			DatabaseResult<string> result = await secrets.FetchAsync(TotpMasterKek.DatabaseKey);
			if (!result.IsSuccess && result.ErrorCode != DatabaseErrorCodes.NotFound)
			{
				await Log.Error("FishMMOInstaller",
					$"Could not read the database: [{result.ErrorCode}] {result.ErrorMessage}. Check that PostgreSQL is running and that " +
					"the database credentials are set (Database ▸ 1 : Configure Secrets & Environment). Nothing was changed.");
				return null;
			}
			if (!TotpMasterKek.TryDecode(result.IsSuccess ? result.Data : null, out byte[]? key, out string? error) || key == null)
			{
				await Log.Error("FishMMOInstaller",
					$"Two-factor needs the TOTP master key, and it is not usable: {error} " +
					"Run Database ▸ 9 : Configure Server Keys first, then this step again. Nothing was changed.");
				return null;
			}
			return key;
		}

		private static async Task CreateAsync(
			IAccountService accounts,
			ITwoFactorRecoveryCodeService recoveryCodes,
			IUnitOfWorkService unitOfWork,
			IAdminAuditService audit,
			byte[] masterKek)
		{
			// ── Who already administers this deployment ─────────────────────────
			var existing = await accounts.SearchAdminAsync(new AccountAdminQuery { AccessLevel = (byte)DbAccessLevel.Admin, PageSize = 10 });
			if (!existing.IsSuccess)
			{
				await Log.Error("FishMMOInstaller", $"Could not read the accounts: [{existing.ErrorCode}] {existing.ErrorMessage}. Nothing was changed.");
				return;
			}
			if (existing.Data.TotalCount > 0)
			{
				Console.WriteLine($"This deployment already has {existing.Data.TotalCount} administrator(s): " +
					string.Join(", ", existing.Data.Items.Select(a => a.Name)) + (existing.Data.TotalCount > existing.Data.Items.Count ? ", ..." : "") + ".");
				Console.WriteLine("Another one is normally promoted from the Control Panel (Accounts ▸ access level).");
				if (!InstallerProcessHelper.PromptForYesNo("Create another administrator here anyway?")) return;
				Console.WriteLine();
			}

			// ── The account ─────────────────────────────────────────────────────
			string username = await PromptForNewUsernameAsync(accounts);
			string email = PromptForEmail();
			int age = PromptForAge();
			string password = PromptForNewPassword();

			Console.WriteLine();
			Console.WriteLine($"  Account name : {username}");
			Console.WriteLine($"  Email        : {email}");
			Console.WriteLine($"  Access level : {DbAccessLevel.Admin} (verified, two-factor enrolled)");
			if (!InstallerProcessHelper.PromptForYesNo("Create this administrator?"))
			{
				Console.WriteLine("Nothing was changed.");
				return;
			}

			// The game client's derivation: fixed SRP identity, 2048-bit group, SHA-512.
			var srp = new ClientSrpData(SrpParameters.Create2048<SHA512>());
			srp.GetSaltAndVerifier(password, out string salt, out string verifier);
			srp.Clear();
			password = string.Empty;

			byte[] secret = CryptoHelper.TwoFactor.GenerateTotpSecret();
			try
			{
				string[] codes = CryptoHelper.TwoFactor.GenerateRecoveryCodes();
				string? failure = await WriteAccountAsync(accounts, recoveryCodes, unitOfWork, username, salt, verifier, email, age,
					CryptoHelper.TwoFactor.EncryptTotpSecret(masterKek, username, secret),
					codes.Select(c => CryptoHelper.TwoFactor.HashRecoveryCode(username, c)).ToList());

				await AuditAsync(audit, username, failure);
				if (failure != null)
				{
					await Log.Error("FishMMOInstaller", $"The administrator was not created: {failure} Nothing was changed.");
					return;
				}

				await Log.Info("FishMMOInstaller", $"Administrator '{username}' created (verified, two-factor enrolled).");
				ShowTwoFactorHandover(CryptoHelper.TwoFactor.BuildOtpauthUri(secret, username), codes);
			}
			finally
			{
				CryptographicOperations.ZeroMemory(secret);
			}
		}

		/// <summary>
		/// Every write, in one transaction. Returns null on success, otherwise why it failed (and nothing was written).
		/// </summary>
		private static async Task<string?> WriteAccountAsync(
			IAccountService accounts,
			ITwoFactorRecoveryCodeService recoveryCodes,
			IUnitOfWorkService unitOfWork,
			string username, string salt, string verifier, string email, int age,
			string encryptedTotpSecret, IReadOnlyList<string> recoveryCodeHashes)
		{
			// Choosing nothing means email, the channel every account has always had (registration's default).
			if (!AccountProfileRules.TryValidate(new AccountProfileData { VerificationChannels = DbChannels.Email },
					out AccountProfileData profile, out string profileError))
			{
				return $"the default profile was refused: {profileError}";
			}

			var begun = await unitOfWork.BeginAsync();
			if (!begun.IsSuccess)
			{
				return $"the transaction could not be started: [{begun.ErrorCode}] {begun.ErrorMessage}";
			}

			// Leaving this block without a commit rolls everything back.
			await using (IUnitOfWork uow = begun.Data)
			{
				var steps = new (string What, Func<Task<DatabaseResult>> Write)[]
				{
					("the account", () => accounts.PersistAsync(username, salt, verifier, email, age)),
					("the profile", () => accounts.PersistProfileAsync(username, profile)),
					("verification", () => accounts.PersistAutoVerifiedAsync(username)),
					("the access level", () => accounts.PersistAccessLevelAsync(username, (byte)DbAccessLevel.Admin)),
					("the authenticator secret", () => accounts.PersistTotpSecretAsync(username, encryptedTotpSecret)),
					("two-factor", () => accounts.PersistTotpEnabledAsync(username, true)),
					("the recovery codes", () => recoveryCodes.PersistManyAsync(username, recoveryCodeHashes)),
				};
				foreach (var (what, write) in steps)
				{
					DatabaseResult result = await write();
					if (!result.IsSuccess)
					{
						return result.ErrorCode == DatabaseErrorCodes.UniqueViolation
							? $"the account name '{username}' was taken in the meantime."
							: $"writing {what} failed: [{result.ErrorCode}] {result.ErrorMessage}";
					}
				}

				DatabaseResult committed = await uow.CommitAsync();
				return committed.IsSuccess ? null : $"the transaction could not be committed: [{committed.ErrorCode}] {committed.ErrorMessage}";
			}
		}

		/// <summary>The level change is recorded, refused or not, like every other caller of PersistAccessLevelAsync.</summary>
		private static async Task AuditAsync(IAdminAuditService audit, string username, string? failure)
		{
			var entry = new AdminAuditData
			{
				OccurredUtc = DateTime.UtcNow,
				ActorName = $"installer:{Environment.UserName}",
				ActorAccessLevel = 0,
				Action = AuditAction,
				TargetType = "account",
				TargetID = username,
				TargetName = username,
				Reason = "Administrator account created from the installer console.",
				Succeeded = failure == null,
				Outcome = failure == null ? "Created: verified, two-factor enrolled." : $"Failed: {failure}",
				Details = JsonSerializer.Serialize(new { level = DbAccessLevel.Admin.ToString(), twoFactor = true }),
				Source = AuditSource,
			};
			var result = await audit.AppendAsync(entry);
			if (!result.IsSuccess)
			{
				await Log.Error("FishMMOInstaller", $"AUDIT WRITE FAILED for '{AuditAction}' on '{username}': [{result.ErrorCode}] {result.ErrorMessage}");
			}
		}

		// ──────────────────────────────────────────────────────────────────
		//  Prompts (the game's own rules, from FishMMO.Shared.Authentication)
		// ──────────────────────────────────────────────────────────────────

		private static async Task<string> PromptForNewUsernameAsync(IAccountService accounts)
		{
			while (true)
			{
				// Lowercased as the game and the panel do: every account row is keyed by the lowercase name.
				string username = SrpIdentity.NormalizeIdentifier(InstallerProcessHelper.PromptForRequiredInput("Account name: "));
				if (!Authentication.IsAllowedUsername(username))
				{
					Console.WriteLine($"  {Authentication.InvalidUsernameError}");
					continue;
				}
				var found = await accounts.FetchAdminAsync(username);
				if (found.IsSuccess)
				{
					Console.WriteLine($"  An account named '{username}' already exists. Promote it from the Control Panel, or choose another name.");
					continue;
				}
				if (found.ErrorCode != DatabaseErrorCodes.NotFound)
				{
					Console.WriteLine($"  That name could not be checked: [{found.ErrorCode}] {found.ErrorMessage}");
					continue;
				}
				return username;
			}
		}

		private static string PromptForEmail()
		{
			while (true)
			{
				string email = InstallerProcessHelper.PromptForRequiredInput("Email: ");
				if (Authentication.IsAllowedEmailUsername(email)) return email;
				Console.WriteLine("  That is not a valid email address.");
			}
		}

		private static int PromptForAge()
		{
			while (true)
			{
				string text = InstallerProcessHelper.PromptForRequiredInput($"Age ({MinAge}-{MaxAge}): ");
				if (int.TryParse(text, out int age) && age >= MinAge && age <= MaxAge) return age;
				Console.WriteLine($"  Enter a whole number from {MinAge} to {MaxAge}.");
			}
		}

		private static string PromptForNewPassword()
		{
			while (true)
			{
				string password = InstallerProcessHelper.PromptForRequiredPassword("Password: ");
				if (!Authentication.IsAllowedPassword(password))
				{
					Console.WriteLine($"  {Authentication.InvalidPasswordError}");
					continue;
				}
				if (InstallerProcessHelper.PromptForPassword("Confirm password: ") != password)
				{
					Console.WriteLine("  The passwords do not match.");
					continue;
				}
				return password;
			}
		}

		// ──────────────────────────────────────────────────────────────────
		//  Two-factor handover (the console's version of the panel's screen)
		// ──────────────────────────────────────────────────────────────────

		/// <summary>
		/// Shows the authenticator key and recovery codes once. Nothing is written to disk: the server stores
		/// only the encrypted secret and the code hashes, so this is the only time they exist in readable form.
		/// </summary>
		private static void ShowTwoFactorHandover(string otpauthUri, IReadOnlyList<string> codes)
		{
			Console.WriteLine();
			Console.WriteLine("── Two-factor authentication ── shown ONCE, never stored readable ──");
			Console.WriteLine();
			if (!TryPrintQrCode(otpauthUri))
			{
				Console.WriteLine("(Install 'qrencode' to get a scannable QR code here; until then enter the key by hand.)");
			}
			Console.WriteLine();
			Console.WriteLine($"Authenticator key (manual entry, time-based, 6 digits, 30 s): {ManualEntryKey(otpauthUri)}");
			Console.WriteLine($"otpauth URI: {otpauthUri}");
			Console.WriteLine();
			Console.WriteLine("Recovery codes (each signs in once without the authenticator; keep them offline):");
			foreach (string code in codes)
			{
				Console.WriteLine($"  {code}");
			}
			Console.WriteLine();

			while (!InstallerProcessHelper.PromptForYesNo("Have you added the key to your authenticator app and saved the recovery codes?"))
			{
				Console.WriteLine("  They cannot be shown again. Save them, then answer Y.");
			}
			// Take them off the screen; the terminal's scrollback is the operator's to clear.
			Console.Clear();
			Console.WriteLine("Two-factor handover complete. Sign in at the Control Panel with the account name and password,");
			Console.WriteLine("then the code from your authenticator app.");
		}

		/// <summary>The Base32 secret, as the Control Panel shows it for manual entry.</summary>
		private static string ManualEntryKey(string otpauthUri)
		{
			int start = otpauthUri.IndexOf("secret=", StringComparison.Ordinal);
			if (start < 0) return "(see the URI)";
			start += "secret=".Length;
			int end = otpauthUri.IndexOf('&', start);
			string key = end < 0 ? otpauthUri[start..] : otpauthUri[start..end];
			// Groups of four, the way authenticator apps accept and most people read it.
			return string.Join(' ', Enumerable.Range(0, (key.Length + 3) / 4).Select(i => key.Substring(i * 4, Math.Min(4, key.Length - i * 4))));
		}

		/// <summary>
		/// Renders the URI as a terminal QR code with qrencode when it is installed. The URI goes in on
		/// standard input, never on the command line, where any user could read it from the process list.
		/// </summary>
		private static bool TryPrintQrCode(string otpauthUri)
		{
			try
			{
				var start = new ProcessStartInfo("qrencode", "-t ANSIUTF8 -m 2")
				{
					RedirectStandardInput = true,
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					UseShellExecute = false,
				};
				using Process? process = Process.Start(start);
				if (process == null) return false;
				process.StandardInput.Write(otpauthUri);
				process.StandardInput.Close();
				string output = process.StandardOutput.ReadToEnd();
				process.WaitForExit(5000);
				if (process.ExitCode != 0 || output.Length == 0) return false;
				Console.WriteLine("Scan with your authenticator app:");
				Console.Write(output);
				return true;
			}
			catch (Exception)
			{
				// Not installed (or not on PATH): the manual key below does the same job.
				return false;
			}
		}
	}
}
