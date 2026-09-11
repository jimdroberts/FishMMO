using FishMMO.Database;
using FishMMO.Logging;
using System.Diagnostics;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FishMMO.Installer
{
	/// <summary>
	/// The one place FishMMO's secrets and environment variables are configured.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>One screen, one writer, one copy.</b> Every <c>FISHMMO_*</c> variable the servers actually
	/// read lands in a single file — <c>/etc/fishmmo/db-secrets.env</c> on Linux,
	/// <c>%ProgramData%\FishMMO\db-secrets.env</c> on Windows — which every systemd unit loads with
	/// <c>EnvironmentFile=-/etc/fishmmo/db-secrets.env</c>. Splitting that file between several
	/// wizards is how it used to lose data: each one rewrote the whole file and emitted only the
	/// keys it knew about, so configuring the database silently deleted the mail credentials.
	/// There is now exactly one editor, it loads every key before it writes any, and a full rewrite
	/// is therefore safe.
	/// </para>
	/// <para>
	/// <b>The file is the only thing this screen reads.</b> It never seeds itself from the process
	/// environment. A shell that still exports an old <c>FISHMMO_DB_PASSWORD</c> from some earlier
	/// session would otherwise show that stale value as "current", and saving would copy it back
	/// over the real one — which is how a secrets file and an exported snippet ended up holding two
	/// different passwords written half a minute apart. What the environment does get is a
	/// <i>comparison</i>: <see cref="DescribeEnvironmentMismatches"/> names every variable whose
	/// value in this shell differs from the file's, without printing either one.
	/// </para>
	/// <para>
	/// <b>That warning matters because the runtime precedence is the opposite.</b>
	/// <see cref="DatabaseSecrets"/> resolves the environment first and the file second, so a
	/// process started by hand from a polluted shell uses the shell's value no matter what this
	/// screen writes. That order is deliberate — an operator must be able to override a deployment
	/// for one run — so the fix is to say so plainly, not to reorder the runtime.
	/// </para>
	/// <para>
	/// <b>Keys it does not know are still preserved.</b> An operator will have added something —
	/// a proxy variable, a feature flag, a note to themselves. Unrecognised assignments, comments,
	/// blank lines and anything this parser cannot make sense of are carried through verbatim.
	/// </para>
	/// <para>
	/// <b>There is no export.</b> This screen used to be able to write the same values out again as
	/// a fish/PowerShell/CMD snippet or a stray <c>.env</c>. Every one of those was a second copy of
	/// a live credential, and the fish one landed in <c>conf.d</c>, where every new shell sourced it
	/// and re-exported the database password into itself and into every process it launched. The
	/// services never needed it: systemd hands them the secrets file. Only the <i>creation</i> is
	/// gone — <see cref="ExportedSnippetPaths"/> still lists those paths so the uninstall can offer
	/// to delete the copies an earlier version already made.
	/// </para>
	/// <para>
	/// <b>What is deliberately not here:</b>
	/// <list type="bullet">
	///   <item><c>FISHMMO_PG_SUPERUSER_PASSWORD</c> — prompted per run by design. Persisting the
	///   PostgreSQL superuser password to disk is a worse trade than persisting the application
	///   user's, because it is the credential that can drop the cluster.</item>
	///   <item><c>FISHMMO_SIGNING_KEY_KEK_BASE64</c> and <c>FISHMMO_CLIENT_GATE_SECRET</c> — owned
	///   by <see cref="SecurityKeyInstaller"/>, which provisions them into the
	///   <c>deployment_secrets</c> table. Writing them here as well would give the deployment two
	///   sources of truth for the same key, and the wrong one would win silently.</item>
	///   <item><c>FISHMMO_CONNECTION_STRING</c> / <c>ConnectionStrings__NpgsqlConnection</c> — an
	///   alternative spelling of the <c>FISHMMO_DB_*</c> set below. Offering both invites a
	///   deployment where the two disagree.</item>
	///   <item>Tooling-only variables (<c>FISHMMO_UNITY_EXE</c>, <c>FISHMMO_INSTALL_ROOT</c>,
	///   <c>FISHMMO_TRADE_RENDER_DIR</c>, <c>FISHMMO_ALLOW_COREDUMPS</c>,
	///   <c>FISHMMO_PG_LISTEN_ADDRESSES</c>) — they steer a developer's machine, not a server.</item>
	/// </list>
	/// </para>
	/// <para>
	/// <b>Mail is the one setting with a second destination.</b> The Control Panel is the process
	/// that drains the outbound email queue and sends (the LoginServer still creates accounts and
	/// enqueues, but its own <c>Smtp:DrainQueue</c> is off), and it resolves each SMTP setting from
	/// the environment first and its configuration second. The non-secret values are therefore
	/// mirrored into <c>FishMMO-Setup/&lt;environment&gt;/appsettings.ControlPanel.json</c> so a
	/// deployment that does not use the secrets file still works — but the password is written to
	/// the secrets file only. That JSON ships in the repository, and a live sending credential must
	/// never be committed.
	/// </para>
	/// </remarks>
	public static class SecretsEnvironmentInstaller
	{
		// ──────────────────────────────────────────────────────────────────────
		//  Inventory
		// ──────────────────────────────────────────────────────────────────────

		private const string GroupDatabase = "Database";
		private const string GroupMail = "Mail  (Control Panel sends; the LoginServer only enqueues)";
		private const string GroupService = "Service";
		private const string GroupSecrets = "Other secrets";

		private const string DbHostKey = "FISHMMO_DB_HOST";
		private const string DbPortKey = "FISHMMO_DB_PORT";
		private const string DbNameKey = "FISHMMO_DB_NAME";
		private const string DbUsernameKey = "FISHMMO_DB_USERNAME";
		private const string DbPasswordKey = "FISHMMO_DB_PASSWORD";
		private const string DbSchemaKey = "FISHMMO_DB_SCHEMA";

		private const string SmtpHostKey = "FISHMMO_SMTP_HOST";
		private const string SmtpPortKey = "FISHMMO_SMTP_PORT";
		private const string SmtpUsernameKey = "FISHMMO_SMTP_USERNAME";
		private const string SmtpPasswordKey = "FISHMMO_SMTP_PASSWORD";
		private const string SmtpFromAddressKey = "FISHMMO_SMTP_FROM_ADDRESS";
		private const string SmtpFromNameKey = "FISHMMO_SMTP_FROM_NAME";
		private const string SmtpUseSslKey = "FISHMMO_SMTP_USE_SSL";

		private const string EnvironmentKey = "FISHMMO_ENVIRONMENT";
		private const string LogLevelKey = "FISHMMO_LOG_LEVEL";

		private const string DiscordTokenKey = "FISHMMO_DISCORD_TOKEN";
		private const string ManifestSigningKey = "FISHMMO_VERSION_MANIFEST_SIGNING_KEY";

		/// <summary>One environment variable this screen owns.</summary>
		/// <param name="Key">The variable name, exactly as the servers read it.</param>
		/// <param name="Group">Which guided step edits it, and which block it is written under.</param>
		private sealed record Setting(string Key, string Group);

		/// <summary>
		/// Every variable this screen writes, in the order it is written to the file.
		/// </summary>
		/// <remarks>
		/// This list is the file layout, the set of keys each guided step covers, and the set the
		/// environment is compared against. Adding a variable here adds it to all three; there is no
		/// second list to keep in step.
		/// </remarks>
		private static readonly Setting[] Settings =
		{
			new(DbHostKey, GroupDatabase),
			new(DbPortKey, GroupDatabase),
			new(DbNameKey, GroupDatabase),
			new(DbUsernameKey, GroupDatabase),
			new(DbPasswordKey, GroupDatabase),
			new(DbSchemaKey, GroupDatabase),

			new(SmtpHostKey, GroupMail),
			new(SmtpPortKey, GroupMail),
			new(SmtpUsernameKey, GroupMail),
			new(SmtpPasswordKey, GroupMail),
			new(SmtpFromAddressKey, GroupMail),
			new(SmtpFromNameKey, GroupMail),
			new(SmtpUseSslKey, GroupMail),

			new(EnvironmentKey, GroupService),
			new(LogLevelKey, GroupService),

			new(DiscordTokenKey, GroupSecrets),
			new(ManifestSigningKey, GroupSecrets),
		};

		/// <summary>Header written when the secrets file is created from nothing.</summary>
		private static readonly string[] NewFileHeader =
		{
			"# FishMMO secrets and environment — chmod 600, owned by the service user.",
			"# Written by the FishMMO Installer: Configuration ▸ Configure Secrets & Environment.",
			"# Loaded by every FishMMO systemd unit (EnvironmentFile=-/etc/fishmmo/db-secrets.env).",
			"#",
			"# Keys the installer does not know about are preserved when it rewrites this file,",
			"# so anything you add by hand survives. Never copy these values into appsettings.json:",
			"# that file ships in the repository.",
		};

		/// <summary>How long a test send may take before it is treated as failed.</summary>
		private const int TestSendTimeoutMilliseconds = 30_000;

		// ──────────────────────────────────────────────────────────────────────
		//  Screen
		// ──────────────────────────────────────────────────────────────────────

		/// <summary>
		/// Runs the interactive editor until the operator goes back.
		/// </summary>
		/// <remarks>
		/// Four guided steps rather than seventeen numbered settings and five letter actions. The
		/// flat list showed every variable at once and made the operator assemble a database
		/// connection out of six separate menu entries; each step now prompts its fields in the
		/// order they belong together, with the current value as the default so Enter keeps it.
		/// <para>
		/// <c>B</c> goes back, not <c>0</c>: a lone zero in a column of letters reads as the letter
		/// O, and was asked about as one. <c>0</c> and Escape are still accepted, silently.
		/// </para>
		/// </remarks>
		public static async Task Configure()
		{
			string secretsPath = DatabaseSecrets.DefaultSecretsFilePath;

			(bool readable, string? existingContent) = await TryReadExistingAsync(secretsPath);
			if (!readable)
			{
				/* The file is there and we cannot see inside it. Editing from here would mean
				 * writing back a file whose other keys we never read — the exact data loss this
				 * screen exists to end. */
				Console.Clear();
				Console.WriteLine("=== Configure Secrets & Environment ===");
				Console.WriteLine();
				Console.WriteLine($"{secretsPath} exists but could not be read.");
				Console.WriteLine("Editing it from here would destroy the keys that cannot be read.");
				Console.WriteLine("Fix its permissions (it should be chmod 600 owned by you) and try again.");
				await Log.Error("FishMMOInstaller", $"Refused to edit unreadable secrets file {secretsPath}.");
				Console.WriteLine();
				Console.WriteLine("Press any key to continue...");
				Console.ReadKey(true);
				return;
			}

			/* The file, and nothing else. Never Environment.GetEnvironmentVariable here: a stale
			 * export in this shell would be shown as the current value and then saved back over the
			 * real one. The environment is compared against this, never merged into it. */
			Dictionary<string, string> values = ParseKeyValues(existingContent);
			var saved = new Dictionary<string, string>(values, StringComparer.Ordinal);

			/* Program takes this snapshot at startup, before it normalises FISHMMO_ENVIRONMENT.
			 * Repeating it here costs nothing (the first capture is the only one) and keeps the
			 * screen correct if it is ever reached by an entry point that did not. */
			CaptureInheritedEnvironment();

			while (true)
			{
				Console.Clear();
				RenderScreen(secretsPath, values, HasUnsavedChanges(saved, values));

				ConsoleKeyInfo pressed = Console.ReadKey(true);
				Console.WriteLine();

				char choice = char.ToUpperInvariant(pressed.KeyChar);
				if (pressed.Key == ConsoleKey.Escape || choice is 'B' or '0')
				{
					if (!HasUnsavedChanges(saved, values))
					{
						return;
					}

					Console.WriteLine("There are unsaved changes. Nothing has been written to the secrets file.");
					if (InstallerProcessHelper.PromptForYesNo("Leave without saving?"))
					{
						return;
					}
					continue;
				}

				switch (choice)
				{
					case '1':
						ConfigureDatabase(values);
						break;
					case '2':
						await ConfigureMail(values);
						break;
					case '3':
						ConfigureService(values);
						break;
					case '4':
						ConfigureOtherSecrets(values);
						break;
					case 'S':
					// W is what this action used to be, and somebody has it in their fingers.
					case 'W':
						if (await WriteConfiguration(secretsPath, values))
						{
							saved = new Dictionary<string, string>(values, StringComparer.Ordinal);
						}
						break;
					case '?':
						ShowWhatIsNotSetHere();
						break;
					default:
						Console.WriteLine("Invalid option.");
						break;
				}

				Console.WriteLine();
				Console.WriteLine("Press any key to continue...");
				Console.ReadKey(true);
			}
		}

		/// <summary>Draws the top-level screen: four groups, three actions, and any warnings.</summary>
		private static void RenderScreen(
			string secretsPath,
			IReadOnlyDictionary<string, string> values,
			bool unsaved)
		{
			Console.WriteLine("=== Configure Secrets & Environment ===");
			Console.WriteLine($"Secrets file: {secretsPath}");
			Console.WriteLine();

			foreach (string line in GroupMenuLines(values))
			{
				Console.WriteLine(line);
			}

			Console.WriteLine();
			Console.WriteLine("  S : Save        ? : What is not set here        B : Back");

			if (unsaved)
			{
				Console.WriteLine();
				Console.WriteLine("* Unsaved changes — nothing reaches the secrets file until you choose S.");
			}

			IReadOnlyList<string> mismatches = DescribeEnvironmentMismatches(values, ReadInherited);
			if (mismatches.Count > 0)
			{
				Console.WriteLine();
				Console.WriteLine("! This shell's environment disagrees with the file. Neither value is shown:");
				foreach (string line in mismatches)
				{
					Console.WriteLine($"    {line}");
				}
				Console.WriteLine("  A server you start BY HAND from this shell reads environment variables before the");
				Console.WriteLine("  file, so it would use the shell's value, not the file's. Services started by");
				Console.WriteLine("  systemd read the file and are unaffected. Open a clean shell to be rid of it.");
			}

			Console.WriteLine();
			Console.Write("Select: ");
		}

		/// <summary>
		/// The four menu rows, built from the file's values.
		/// </summary>
		/// <remarks>Public so the rows can be asserted without a terminal.</remarks>
		public static IReadOnlyList<string> GroupMenuLines(IReadOnlyDictionary<string, string> values)
		{
			return new[]
			{
				$"  1 : {"Database connection",-20} [{DescribeDatabase(values)}]",
				$"  2 : {"Mail (SMTP)",-20} [{DescribeMailSummary(values)}]",
				$"  3 : {"Service",-20} [{DescribeService(values)}]",
				$"  4 : {"Other secrets",-20} [{DescribeOtherSecrets(values)}]",
			};
		}

		/// <summary>The database connection in one line. Never includes the password.</summary>
		public static string DescribeDatabase(IReadOnlyDictionary<string, string> values)
		{
			if (!DatabaseKeys.Any(key => Value(values, key).Length > 0))
			{
				return "not configured";
			}

			string username = Value(values, DbUsernameKey);
			string summary =
				$"{(username.Length == 0 ? "(no username)" : username)}@" +
				$"{Value(values, DbHostKey, "127.0.0.1")}:{Value(values, DbPortKey, "5432")}/" +
				$"{Value(values, DbNameKey, "fishmmo")}";

			return Value(values, DbPasswordKey).Length == 0 ? $"{summary} — no password" : summary;
		}

		/// <summary>Mail in one line for the menu; the mail step shows the full sentence.</summary>
		public static string DescribeMailSummary(IReadOnlyDictionary<string, string> values)
		{
			string? problem = DescribeMailProblem(values, out bool idle);
			if (idle)
			{
				return "not configured — no mail will be sent";
			}
			if (problem != null)
			{
				return Value(values, SmtpFromAddressKey).Length == 0
					? "will not send — no From address"
					: "will not send — the From address is not valid";
			}

			string username = Value(values, SmtpUsernameKey);
			return $"{Value(values, SmtpHostKey, "localhost")}:{Value(values, SmtpPortKey, "587")} as " +
				   $"{(username.Length == 0 ? "an unauthenticated sender" : username)}";
		}

		/// <summary>Environment, and the log level when one is pinned.</summary>
		public static string DescribeService(IReadOnlyDictionary<string, string> values)
		{
			string environmentName = Value(values, EnvironmentKey);
			string logLevel = Value(values, LogLevelKey);

			if (environmentName.Length == 0 && logLevel.Length == 0)
			{
				return "not set";
			}

			string shown = environmentName.Length == 0 ? "environment not set" : environmentName;
			return logLevel.Length == 0 ? shown : $"{shown}, log {logLevel}";
		}

		/// <summary>Whether each standalone secret exists — never anything about its value.</summary>
		public static string DescribeOtherSecrets(IReadOnlyDictionary<string, string> values)
		{
			string discord = Value(values, DiscordTokenKey).Length == 0 ? "not set" : "set";
			string patcher = Value(values, ManifestSigningKey).Length == 0 ? "not set" : "set";
			return $"Discord: {discord}   Patcher: {patcher}";
		}

		/// <summary>
		/// The launching shell's values, captured once before anything can overwrite them.
		/// </summary>
		/// <remarks>
		/// Read through <see cref="ReadInherited"/> rather than directly, so that every comparison
		/// asks the same question: "what did the SHELL give us?", not "what does this process hold
		/// right now?" — which <see cref="ApplyToThisProcess"/> deliberately changes.
		/// </remarks>
		private static Dictionary<string, string?> inheritedEnvironment = new(StringComparer.Ordinal);

		/// <summary>Whether the snapshot has been taken, so the first capture is the only one.</summary>
		private static bool inheritedEnvironmentCaptured;

		/// <summary>
		/// Records what the launching shell exported, before this process changes any of it.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Must run before the process edits its own environment, and only the first call counts.
		/// Two separate things would otherwise poison the snapshot and turn the mismatch warning
		/// into a liar:
		/// </para>
		/// <list type="bullet">
		///   <item><c>Program</c> normalises <c>FISHMMO_ENVIRONMENT</c> at startup, setting it from
		///   <see cref="DatabaseConfigurationHelper.ResolveEnvironmentName"/> — which falls back to
		///   "Production" in a release build when the shell set nothing at all. Captured after that,
		///   a secrets file saying <c>Development</c> would be reported as a shell disagreement that
		///   no shell ever caused.</item>
		///   <item><see cref="ApplyToThisProcess"/> copies saved values into this process. Captured
		///   after a save, the snapshot would equal the file by construction and the warning would
		///   go quiet while the operator's shell was still stale — silence meaning the opposite of
		///   what it means everywhere else on this screen.</item>
		/// </list>
		/// </remarks>
		internal static void CaptureInheritedEnvironment()
		{
			if (inheritedEnvironmentCaptured)
			{
				return;
			}

			inheritedEnvironment = Settings.ToDictionary(
				setting => setting.Key,
				setting => Environment.GetEnvironmentVariable(setting.Key),
				StringComparer.Ordinal);
			inheritedEnvironmentCaptured = true;
		}

		/// <summary>The launching shell's value for one key, or null if it set none.</summary>
		private static string? ReadInherited(string key) => inheritedEnvironment.GetValueOrDefault(key);

		/// <summary>
		/// Copies the saved values into THIS process's environment, returning how many were set.
		/// </summary>
		/// <remarks>
		/// Writing the file is not enough to continue a setup. <see cref="DatabaseSecrets"/> reads
		/// the environment before the file, so the installer — and every tool it launches, such as
		/// <c>dotnet ef</c> and <c>psql</c>, which inherit this environment — would otherwise keep
		/// using whatever the launching shell held: nothing at all, or a stale password from a
		/// previous run. Saving and then immediately being unable to install the database is how
		/// that presents, and the cause is invisible from the outside.
		/// <para>
		/// Children inherit this. The PARENT shell cannot be reached — no process can change its
		/// parent's environment — which is why the screen states that limit instead of working
		/// around it. The usual workaround is to write a snippet for the shell to source, and that
		/// second copy of the secret is exactly what was removed from this installer.
		/// </para>
		/// <para>
		/// A key with no value is actively UNSET rather than skipped, so clearing a field takes
		/// effect here too instead of leaving the previous value live in the process.
		/// </para>
		/// </remarks>
		private static int ApplyToThisProcess(IReadOnlyDictionary<string, string> values)
		{
			int applied = 0;
			foreach (Setting setting in Settings)
			{
				string value = Value(values, setting.Key);
				Environment.SetEnvironmentVariable(setting.Key, value.Length == 0 ? null : value);
				if (value.Length > 0)
				{
					applied++;
				}
			}
			return applied;
		}

		/// <summary>
		/// Names every variable whose value in the launching shell differs from the file's.
		/// </summary>
		/// <param name="fileValues">What the secrets file holds — the values this screen edits.</param>
		/// <param name="readEnvironment">
		/// How to read a variable; defaults to this process's environment. Injectable so the
		/// comparison can be tested without touching the real environment, and so the screen can
		/// ask <see cref="ReadInherited"/> instead of the live process.
		/// </param>
		/// <remarks>
		/// <para>
		/// This is the diagnostic the screen could not previously give. An operator whose shell still
		/// exported an old password had no way to see that the shell and the file disagreed, and the
		/// disagreement is what decides which password a hand-started server actually uses:
		/// <see cref="DatabaseSecrets"/> takes the environment first and the file second.
		/// </para>
		/// <para>
		/// Neither value is returned, formatted or logged — only the key name and the fact that the
		/// two differ. An empty variable is not a disagreement, because the runtime treats an empty
		/// variable as unset and falls through to the file; the shell's value is compared trimmed,
		/// because trimming is what the runtime does to it before use, so trailing whitespace is not
		/// reported as a difference that would not exist in practice.
		/// </para>
		/// </remarks>
		public static IReadOnlyList<string> DescribeEnvironmentMismatches(
			IReadOnlyDictionary<string, string> fileValues,
			Func<string, string?>? readEnvironment = null)
		{
			Func<string, string?> read = readEnvironment ?? Environment.GetEnvironmentVariable;
			var lines = new List<string>();

			foreach (Setting setting in Settings)
			{
				string? raw = read(setting.Key);
				if (string.IsNullOrEmpty(raw))
				{
					continue;
				}

				string inShell = raw.Trim();
				string inFile = Value(fileValues, setting.Key);

				if (inFile.Length == 0)
				{
					lines.Add($"{setting.Key}: this shell sets a value the file does not have " +
							  "(the file is what services use)");
				}
				else if (!string.Equals(inShell, inFile, StringComparison.Ordinal))
				{
					lines.Add($"{setting.Key}: this shell has a different value than the file " +
							  "(the file is what services use)");
				}
			}

			return lines;
		}

		/// <summary>
		/// Whether the screen holds edits that are not on disk.
		/// </summary>
		/// <remarks>
		/// Compared on non-empty values only, because that is what a save round-trips: an empty value
		/// means "unset", <see cref="ComposeSecretsFile"/> emits no line for it, and reloading the
		/// file would not produce the key at all. Without that, a file containing a bare <c>KEY=</c>
		/// would report unsaved changes for ever.
		/// </remarks>
		public static bool HasUnsavedChanges(
			IReadOnlyDictionary<string, string> saved,
			IReadOnlyDictionary<string, string> current)
		{
			static Dictionary<string, string> Set(IReadOnlyDictionary<string, string> source)
				=> source.Where(pair => pair.Value.Length > 0)
						 .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

			Dictionary<string, string> a = Set(saved);
			Dictionary<string, string> b = Set(current);

			if (a.Count != b.Count)
			{
				return true;
			}

			foreach (KeyValuePair<string, string> pair in a)
			{
				if (!b.TryGetValue(pair.Key, out string? other) ||
					!string.Equals(other, pair.Value, StringComparison.Ordinal))
				{
					return true;
				}
			}

			return false;
		}

		/// <summary>The keys of one group, in file order.</summary>
		private static string[] KeysIn(string group)
			=> Settings.Where(setting => setting.Group == group).Select(setting => setting.Key).ToArray();

		private static readonly string[] DatabaseKeys = KeysIn(GroupDatabase);

		/// <summary>A value from the file, or the fallback when it is absent or empty.</summary>
		private static string Value(IReadOnlyDictionary<string, string> values, string key, string fallback = "")
		{
			string value = values.GetValueOrDefault(key, "");
			return value.Length == 0 ? fallback : value;
		}

		/// <summary>The things an operator will look for here and not find, and where they are.</summary>
		/// <remarks>
		/// Behind a key rather than printed under the menu. It is four paragraphs of "not this
		/// screen", and it used to take more vertical space than the settings did.
		/// </remarks>
		private static void ShowWhatIsNotSetHere()
		{
			Console.WriteLine("=== What is not set here ===");
			Console.WriteLine();
			Console.WriteLine("  PostgreSQL superuser password");
			Console.WriteLine("    Prompted per run, never stored. It is the credential that can drop the cluster.");
			Console.WriteLine();
			Console.WriteLine("  Signing-key KEK and client gate secret");
			Console.WriteLine("    Database ▸ Configure Server Keys. They live in the deployment_secrets table, and");
			Console.WriteLine("    a second copy here would give the deployment two sources of truth for one key.");
			Console.WriteLine();
			Console.WriteLine("  FISHMMO_CONNECTION_STRING");
			Console.WriteLine("    Another spelling of the FISHMMO_DB_* values above. Offering both invites a");
			Console.WriteLine("    deployment where the two disagree.");
			Console.WriteLine();
			Console.WriteLine("  Tooling variables (FISHMMO_UNITY_EXE, FISHMMO_INSTALL_ROOT and friends)");
			Console.WriteLine("    They steer a developer's machine, not a server.");
		}

		// ──────────────────────────────────────────────────────────────────────
		//  The four guided steps
		// ──────────────────────────────────────────────────────────────────────

		private static void ConfigureDatabase(Dictionary<string, string> values)
		{
			Console.WriteLine("=== Database connection ===");
			Console.WriteLine();
			Console.WriteLine("Every FishMMO process resolves its connection from these. Enter keeps the current");
			Console.WriteLine("value, '-' clears it.");
			Console.WriteLine();

			PromptText(values, DbHostKey, "Host", "127.0.0.1");
			PromptNumber(values, DbPortKey, "Port", "5432");
			PromptText(values, DbNameKey, "Database name", "fishmmo");
			PromptText(values, DbUsernameKey, "Username");
			PromptSecret(values, DbPasswordKey, "Password");
			PromptText(values, DbSchemaKey, "Schema", "must match Npgsql:Schema in appsettings");

			Console.WriteLine();
			Console.WriteLine($"Now: {DescribeDatabase(values)}");
		}

		/// <summary>
		/// Mail, preset first and test send last.
		/// </summary>
		/// <remarks>
		/// The Resend preset and the test send used to be top-level letter actions sitting beside
		/// seventeen numbers, which is the wrong shape for both: the preset is how you begin
		/// configuring mail and the test is how you find out whether you succeeded. They are the
		/// first and last steps of this flow now.
		/// </remarks>
		private static async Task ConfigureMail(Dictionary<string, string> values)
		{
			Console.WriteLine("=== Mail (SMTP) ===");
			Console.WriteLine();
			Console.WriteLine("The Control Panel drains the queue and sends; the LoginServer only enqueues.");
			Console.WriteLine($"Currently: {DescribeMail(values)}");
			Console.WriteLine();

			if (InstallerProcessHelper.PromptForYesNo(
					"Use the Resend preset (smtp.resend.com:587, username 'resend', TLS on)?"))
			{
				ApplyResendPreset(values);
			}
			else
			{
				Console.WriteLine();
				Console.WriteLine("Enter keeps the current value, '-' clears it.");
				Console.WriteLine();

				PromptText(values, SmtpHostKey, "SMTP host", "localhost = no relay");
				PromptNumber(values, SmtpPortKey, "SMTP port", "587");
				PromptText(values, SmtpUsernameKey, "Username", "blank = unauthenticated");
				PromptSecret(values, SmtpPasswordKey, "Password / API key");
				PromptText(values, SmtpFromAddressKey, "From address", "required to send");
				PromptText(values, SmtpFromNameKey, "From name", "FishMMO");
				PromptBool(values, SmtpUseSslKey, "TLS (STARTTLS)", "true");
			}

			Console.WriteLine();
			Console.WriteLine($"Mail status: {DescribeMail(values)}");

			if (DescribeMailProblem(values, out _) != null)
			{
				return;
			}

			Console.WriteLine();
			Console.WriteLine("A test send uses the values on this screen, saved or not, and writes nothing.");
			if (InstallerProcessHelper.PromptForYesNo("Send a test message now?"))
			{
				Console.WriteLine();
				await SendTestMessage(values);
			}
		}

		private static void ConfigureService(Dictionary<string, string> values)
		{
			Console.WriteLine("=== Service ===");
			Console.WriteLine();
			Console.WriteLine("Enter keeps the current value, '-' clears it.");
			Console.WriteLine();

			PromptText(values, EnvironmentKey, "Environment", "Development or Production");
			PromptText(values, LogLevelKey, "Log level", "Verbose / Debug / Info / Warning / Error");

			Console.WriteLine();
			Console.WriteLine($"Now: {DescribeService(values)}");
			Console.WriteLine("Mail settings are mirrored into: " +
							  $"{ControlPanelSettingsPath(values) ?? "(FishMMO-Setup not found — the secrets file only)"}");
		}

		private static void ConfigureOtherSecrets(Dictionary<string, string> values)
		{
			Console.WriteLine("=== Other secrets ===");
			Console.WriteLine();
			Console.WriteLine("Both are hidden. Enter keeps the current value, '-' clears it.");
			Console.WriteLine();

			PromptSecret(values, DiscordTokenKey, "Discord bot token  (read by FishMMO-DiscordBot)");
			PromptSecret(values, ManifestSigningKey, "Manifest signing key  (read by the Patcher)");

			Console.WriteLine();
			Console.WriteLine($"Now: {DescribeOtherSecrets(values)}");
		}

		// ──────────────────────────────────────────────────────────────────────
		//  Prompts
		// ──────────────────────────────────────────────────────────────────────

		private static void PromptText(
			Dictionary<string, string> values,
			string key,
			string label,
			string? placeholder = null)
		{
			string current = Value(values, key);
			Console.Write($"  {label,-16} [{(current.Length > 0 ? current : placeholder ?? "not set")}]: ");

			string input = (Console.ReadLine() ?? string.Empty).Trim();
			if (input.Length == 0)
			{
				return;
			}
			if (input == "-")
			{
				values.Remove(key);
				return;
			}
			values[key] = input;
		}

		private static void PromptNumber(
			Dictionary<string, string> values,
			string key,
			string label,
			string? placeholder = null)
		{
			while (true)
			{
				string current = Value(values, key);
				Console.Write($"  {label,-16} [{(current.Length > 0 ? current : placeholder ?? "not set")}]: ");

				string input = (Console.ReadLine() ?? string.Empty).Trim();
				if (input.Length == 0)
				{
					return;
				}
				if (input == "-")
				{
					values.Remove(key);
					return;
				}
				if (!int.TryParse(input, out int number) || number <= 0)
				{
					Console.WriteLine("    Enter a positive integer.");
					continue;
				}

				values[key] = number.ToString();
				if (key == SmtpPortKey && number == 465)
				{
					WarnAboutImplicitTls();
				}
				return;
			}
		}

		private static void PromptBool(
			Dictionary<string, string> values,
			string key,
			string label,
			string? placeholder = null)
		{
			while (true)
			{
				string current = Value(values, key);
				Console.Write($"  {label,-16} (true/false) [{(current.Length > 0 ? current : placeholder ?? "not set")}]: ");

				string input = (Console.ReadLine() ?? string.Empty).Trim();
				if (input.Length == 0)
				{
					return;
				}
				if (input == "-")
				{
					values.Remove(key);
					return;
				}
				if (!bool.TryParse(input, out bool parsed))
				{
					Console.WriteLine("    Enter true or false.");
					continue;
				}

				values[key] = parsed ? "true" : "false";
				return;
			}
		}

		/// <summary>
		/// Prompts for a value that is never echoed.
		/// </summary>
		/// <remarks>
		/// Enter keeps what is already there — which is why this uses
		/// <see cref="InstallerProcessHelper.PromptForPassword"/> and not the "required" variant,
		/// which loops until something is typed and would turn every guided step into a forced
		/// retype of every secret in it.
		/// </remarks>
		private static void PromptSecret(Dictionary<string, string> values, string key, string label)
		{
			string current = Value(values, key);
			Console.WriteLine($"  {label} [{MaskSecret(current)}]  (hidden; Enter keeps, '-' clears)");

			string entered = InstallerProcessHelper.PromptForPassword("    Value: ");
			if (entered.Length == 0)
			{
				return;
			}
			if (entered == "-")
			{
				values.Remove(key);
				Console.WriteLine("    Cleared.");
				return;
			}

			/* An environment file is one line per value, and a control character in one is either a
			 * stray key that the masked prompt turned into a '\0' or a paste that brought a newline
			 * with it. Either way the server would read back a credential that is not the one
			 * typed, and it would be invisible on screen. */
			if (entered.Any(char.IsControl))
			{
				Console.WriteLine("    Refused: the value contains a line break or control character.");
				return;
			}

			values[key] = entered;
			Console.WriteLine("    Set.");
		}

		/// <summary>Shows that a secret exists without showing any part of it.</summary>
		/// <remarks>Fixed width on purpose: the length of a credential is information too.</remarks>
		private static string MaskSecret(string? value)
			=> string.IsNullOrEmpty(value) ? "not set" : "********";

		/// <summary>
		/// Says plainly that this sender cannot do implicit TLS, rather than letting a send sit on
		/// a socket until the timeout.
		/// </summary>
		private static void WarnAboutImplicitTls()
		{
			Console.WriteLine();
			Console.WriteLine("    Port 465 is implicit TLS, which System.Net.Mail does not support.");
			Console.WriteLine("    A send on 465 will hang until it times out and then fail with nothing useful.");
			Console.WriteLine("    Use 587 (STARTTLS, TLS on) — that is what the relays this project targets expect.");
			Console.WriteLine();
		}

		// ──────────────────────────────────────────────────────────────────────
		//  Mail
		// ──────────────────────────────────────────────────────────────────────

		/// <summary>
		/// Fills in the Resend fields and asks only for what is specific to this deployment.
		/// </summary>
		/// <remarks>
		/// The host is set from the value Resend documents, but the operator is told to confirm it
		/// against their own dashboard: a hostname written down from memory in an installer is
		/// exactly the sort of thing that is quietly wrong for a year.
		/// </remarks>
		private static void ApplyResendPreset(Dictionary<string, string> values)
		{
			Console.WriteLine();
			Console.WriteLine("Setting host smtp.resend.com, port 587, username 'resend' and TLS on.");
			Console.WriteLine("Confirm the host and port against your Resend dashboard before relying on them.");
			Console.WriteLine();

			values[SmtpHostKey] = "smtp.resend.com";
			values[SmtpPortKey] = "587";
			values[SmtpUsernameKey] = "resend";
			values[SmtpUseSslKey] = "true";

			PromptSecret(values, SmtpPasswordKey, "API key  (this is the password; secrets file only)");
			PromptText(values, SmtpFromAddressKey, "From address", "required to send");
			PromptText(values, SmtpFromNameKey, "From name", "FishMMO");

			Console.WriteLine();
			Console.WriteLine("The From address must be on a domain you have verified with Resend, or it will");
			Console.WriteLine("reject the message.");
		}

		/// <summary>
		/// Says whether this configuration would send, in the Control Panel's own terms.
		/// </summary>
		/// <remarks>
		/// Deliberately the same three rules as <c>SmtpSender.DescribeConfiguration</c>: no host, a
		/// loopback host with no credentials, or a missing/malformed From address. A step that
		/// reported success for a combination the panel refuses would be worse than no step at all.
		/// Not sending is a supported state — a shard whose mail goes out elsewhere has no relay —
		/// so this reports it as a fact rather than an error.
		/// </remarks>
		private static string DescribeMail(IReadOnlyDictionary<string, string> values)
		{
			string? reason = DescribeMailProblem(values, out _);
			if (reason != null)
			{
				return reason;
			}

			string host = values.GetValueOrDefault(SmtpHostKey, "localhost");
			string port = values.GetValueOrDefault(SmtpPortKey, "587");
			bool tls = UseSsl(values);
			string user = values.GetValueOrDefault(SmtpUsernameKey, "");
			return $"ready — relay {host}:{port}, {(tls ? "STARTTLS" : "no TLS")}, " +
				   $"from {values.GetValueOrDefault(SmtpFromAddressKey, "")}, " +
				   $"{(user.Length == 0 ? "no credentials" : "authenticated")}";
		}

		/// <summary>
		/// The reason the Control Panel would not send with these values, or null when it would.
		/// </summary>
		/// <param name="idle">
		/// True when the reason is the unconfigured default rather than a mistake — the drain logs
		/// once and idles, which is a supported deployment and not a failure to report.
		/// </param>
		private static string? DescribeMailProblem(IReadOnlyDictionary<string, string> values, out bool idle)
		{
			idle = false;

			string host = values.GetValueOrDefault(SmtpHostKey, "localhost");
			string username = values.GetValueOrDefault(SmtpUsernameKey, "");
			string fromAddress = values.GetValueOrDefault(SmtpFromAddressKey, "");
			string fromName = values.GetValueOrDefault(SmtpFromNameKey, "FishMMO");

			if (string.IsNullOrWhiteSpace(host))
			{
				idle = true;
				return "no relay — no SMTP host is set, so the Control Panel's drain will idle and no mail is sent.";
			}

			bool loopback = host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
							host.Equals("127.0.0.1", StringComparison.Ordinal) ||
							host.Equals("::1", StringComparison.Ordinal);
			if (loopback && username.Length == 0)
			{
				idle = true;
				return $"no relay — host '{host}' with no username is the unconfigured default, so the " +
					   "Control Panel's drain will idle and no mail is sent.";
			}

			if (string.IsNullOrWhiteSpace(fromAddress))
			{
				return "will not send — no From address is set.";
			}

			try
			{
				_ = new MailAddress(fromAddress, fromName);
			}
			catch (Exception ex) when (ex is FormatException or ArgumentException)
			{
				return $"will not send — '{fromAddress}' is not a valid mail address.";
			}

			return null;
		}

		private static bool UseSsl(IReadOnlyDictionary<string, string> values)
			=> values.GetValueOrDefault(SmtpUseSslKey, "true").Equals("true", StringComparison.OrdinalIgnoreCase);

		/// <summary>
		/// Sends one real message through the configured relay.
		/// </summary>
		/// <remarks>
		/// The only way to learn whether the credentials, the relay and the sending domain's
		/// DKIM/SPF records actually work is to send something and read the answer. The alternative
		/// is finding out when a player's password reset does not arrive. Nothing here is written
		/// to disk, and the failure text is the relay's own response, which is what makes it worth
		/// reading.
		/// </remarks>
		private static async Task SendTestMessage(Dictionary<string, string> values)
		{
			Console.WriteLine("=== Send a test message ===");
			Console.WriteLine();

			string? problem = DescribeMailProblem(values, out bool idle);
			if (problem != null)
			{
				Console.WriteLine(problem);
				if (idle)
				{
					Console.WriteLine("Set a real SMTP host (and username, if the relay authenticates) to send.");
				}
				return;
			}

			string host = values.GetValueOrDefault(SmtpHostKey, "localhost");
			int port = int.TryParse(values.GetValueOrDefault(SmtpPortKey, "587"), out int parsedPort) ? parsedPort : 587;
			if (port == 465)
			{
				WarnAboutImplicitTls();
				Console.WriteLine("Not sending: this client cannot talk to 465 at all.");
				return;
			}

			Console.Write("Send a test message to: ");
			string recipient = (Console.ReadLine() ?? string.Empty).Trim();
			if (recipient.Length == 0)
			{
				Console.WriteLine("Cancelled.");
				return;
			}

			try
			{
				_ = new MailAddress(recipient);
			}
			catch (Exception ex) when (ex is FormatException or ArgumentException)
			{
				Console.WriteLine($"'{recipient}' is not a valid mail address.");
				return;
			}

			string fromAddress = values.GetValueOrDefault(SmtpFromAddressKey, "");
			string fromName = values.GetValueOrDefault(SmtpFromNameKey, "FishMMO");
			string username = values.GetValueOrDefault(SmtpUsernameKey, "");
			string password = values.GetValueOrDefault(SmtpPasswordKey, "");
			bool tls = UseSsl(values);

			Console.WriteLine();
			Console.WriteLine($"Sending via {host}:{port} ({(tls ? "STARTTLS" : "no TLS")}) as " +
							  $"{(username.Length == 0 ? "an unauthenticated sender" : $"'{username}'")}...");

			try
			{
				using var message = new MailMessage
				{
					From = new MailAddress(fromAddress, fromName),
					Subject = "FishMMO installer test message",
					SubjectEncoding = Encoding.UTF8,
					BodyEncoding = Encoding.UTF8,
					Body =
						"This is a test message from the FishMMO Installer.\n\n" +
						"If you are reading it, the relay accepted a message from this host and delivered it, " +
						"which is what a player's verification mail and password reset code depend on.\n\n" +
						$"Relay: {host}:{port}\n" +
						$"From: {fromAddress}\n",
				};
				message.To.Add(recipient);

				using var client = new SmtpClient(host, port)
				{
					// EnableSsl is STARTTLS in System.Net.Mail, which is what 587 expects.
					EnableSsl = tls,
					DeliveryMethod = SmtpDeliveryMethod.Network,
					Timeout = TestSendTimeoutMilliseconds,
				};

				if (username.Length > 0)
				{
					client.Credentials = new NetworkCredential(username, password);
				}

				await client.SendMailAsync(message);

				Console.WriteLine();
				Console.WriteLine($"Accepted by the relay and addressed to {recipient}.");
				Console.WriteLine("Check the inbox — and the spam folder. A message that arrives in spam means the");
				Console.WriteLine("relay worked and the sending domain's SPF/DKIM records still need attention.");
				await Log.Info("FishMMOInstaller", $"SMTP test message accepted by {host}:{port}.");
			}
			catch (SmtpFailedRecipientException ex)
			{
				Console.WriteLine();
				Console.WriteLine($"The relay rejected the recipient: {ex.StatusCode} {ex.Message}");
				await Log.Warning("FishMMOInstaller", $"SMTP test rejected by {host}:{port}: {ex.Message}");
			}
			catch (SmtpException ex)
			{
				Console.WriteLine();
				Console.WriteLine($"The relay refused the message: {ex.StatusCode} {ex.Message}");
				if (ex.InnerException != null)
				{
					Console.WriteLine($"  {ex.InnerException.Message}");
				}
				if (!tls)
				{
					Console.WriteLine("  TLS is off. Most relays refuse authentication on a plaintext connection.");
				}
				await Log.Warning("FishMMOInstaller", $"SMTP test failed against {host}:{port}: {ex.Message}");
			}
			catch (Exception ex)
			{
				Console.WriteLine();
				Console.WriteLine($"Send failed: {ex.Message}");
				await Log.Error("FishMMOInstaller", $"SMTP test failed against {host}:{port}: {ex.Message}");
			}
		}

		// ──────────────────────────────────────────────────────────────────────
		//  Writing
		// ──────────────────────────────────────────────────────────────────────

		/// <summary>
		/// Writes the secrets file, and mirrors the non-secret mail settings into the Control
		/// Panel's configuration.
		/// </summary>
		/// <returns>
		/// True when the secrets file was rewritten, so the caller can treat the screen as saved.
		/// False when the write was refused, which must leave the unsaved-changes marker up.
		/// </returns>
		private static async Task<bool> WriteConfiguration(string secretsPath, Dictionary<string, string> values)
		{
			Console.WriteLine("=== Save ===");
			Console.WriteLine();

			(bool readable, string? existingContent) = await TryReadExistingAsync(secretsPath);
			if (!readable)
			{
				Console.WriteLine($"Refusing to write: {secretsPath} exists but could not be read, and writing");
				Console.WriteLine("it now would destroy the keys that could not be read.");
				await Log.Error("FishMMOInstaller", $"Refused to write unreadable secrets file {secretsPath}.");
				return false;
			}

			string content = ComposeSecretsFile(existingContent, values);
			await DatabaseSecretsInstaller.WriteWithSudoIfNeeded(secretsPath, content);

			Console.WriteLine();
			Console.WriteLine("Secrets and environment written. Every FISHMMO_* value, including the passwords, is");
			Console.WriteLine("in that file and nowhere else. Restart the services to pick it up:");
			Console.WriteLine("  sudo systemctl restart 'fishmmo-*'");

			int applied = ApplyToThisProcess(values);
			Console.WriteLine();
			Console.WriteLine($"The installer is now using these values itself ({applied} variable(s)), so you can");
			Console.WriteLine("carry straight on to installing the database without restarting it.");

			await WriteControlPanelMailSettings(values);

			/* Saying it again here, because this is the moment an operator believes they are done.
			 * The file is now right and this shell is still wrong, and the shell wins for anything
			 * launched from it. */
			IReadOnlyList<string> mismatches = DescribeEnvironmentMismatches(values, ReadInherited);
			if (mismatches.Count > 0)
			{
				Console.WriteLine();
				Console.WriteLine("Note: this shell still holds a different value for:");
				foreach (string line in mismatches)
				{
					Console.WriteLine($"  {line}");
				}
				Console.WriteLine("systemd services read the file, and so does anything this installer launches from");
				Console.WriteLine("here on. A command you type in this terminal yourself still gets the shell's old");
				Console.WriteLine("value — open a new shell before starting a server by hand.");
			}

			return true;
		}

		/// <summary>
		/// Updates the <c>Smtp</c> block in the Control Panel's settings file — without the password.
		/// </summary>
		/// <remarks>
		/// The panel resolves each setting from the environment first and configuration second, so
		/// the secrets file already covers a systemd deployment. This exists for the deployments
		/// that do not load it, and for anyone reading the repository to see what the relay is. The
		/// password is not only withheld here, it is actively cleared: this file is committed, and
		/// a credential pasted into it by hand needs to stop being there.
		/// </remarks>
		private static async Task WriteControlPanelMailSettings(IReadOnlyDictionary<string, string> values)
		{
			string? path = ControlPanelSettingsPath(values);
			if (path == null || !File.Exists(path))
			{
				Console.WriteLine();
				Console.WriteLine("Control Panel settings file not found, so mail settings were written to the");
				Console.WriteLine("secrets file only. That is enough for a systemd deployment: the environment");
				Console.WriteLine("variables win over the JSON either way.");
				return;
			}

			try
			{
				JsonObject root = JsonNode.Parse(await File.ReadAllTextAsync(path, Encoding.UTF8))?.AsObject()
								  ?? new JsonObject();

				if (root["Smtp"] is not JsonObject smtp)
				{
					smtp = new JsonObject();
					root["Smtp"] = smtp;
				}

				smtp["Host"] = values.GetValueOrDefault(SmtpHostKey, "localhost");
				smtp["Port"] = int.TryParse(values.GetValueOrDefault(SmtpPortKey, "587"), out int port) ? port : 587;
				smtp["Username"] = values.GetValueOrDefault(SmtpUsernameKey, "");
				// Never the password. Cleared rather than left, in case one was pasted in by hand.
				smtp["Password"] = "";
				smtp["FromAddress"] = values.GetValueOrDefault(SmtpFromAddressKey, "");
				smtp["FromName"] = values.GetValueOrDefault(SmtpFromNameKey, "FishMMO");
				smtp["UseSsl"] = UseSsl(values);

				await File.WriteAllTextAsync(
					path,
					root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
					Utf8NoBom);

				Console.WriteLine();
				Console.WriteLine($"Mail settings (no password) written to: {path}");
				Console.WriteLine("The password stays in the secrets file: that JSON ships in the repository.");
				Console.WriteLine("The Control Panel copies this file into its output at build time, so rebuild or");
				Console.WriteLine("redeploy the panel for the JSON change to reach it. The secrets file needs only a restart.");
				await Log.Info("FishMMOInstaller", $"Control Panel SMTP settings written to {path}.");
			}
			catch (Exception ex)
			{
				Console.WriteLine();
				Console.WriteLine($"Could not update {path}: {ex.Message}");
				Console.WriteLine("The secrets file was still written, and its values win over this JSON.");
				await Log.Error("FishMMOInstaller", $"Failed to update Control Panel settings: {ex.Message}");
			}
		}

		/// <summary>
		/// The Control Panel settings file for the environment being configured, or null when the
		/// monorepo is not next to this installer.
		/// </summary>
		private static string? ControlPanelSettingsPath(IReadOnlyDictionary<string, string> values)
		{
			string environmentName = values.GetValueOrDefault(EnvironmentKey, "");
			if (environmentName.Length == 0)
			{
				environmentName = DatabaseConfigurationHelper.ResolveEnvironmentName();
			}
			if (environmentName.Length == 0)
			{
				environmentName = "Development";
			}

			string directory = Path.Combine(InstallationConstants.FishMMOSetupPath, environmentName);
			return Directory.Exists(directory)
				? Path.Combine(directory, "appsettings.ControlPanel.json")
				: null;
		}

		// ──────────────────────────────────────────────────────────────────────
		//  File composition — the part that must never lose data
		// ──────────────────────────────────────────────────────────────────────

		/// <summary>
		/// Produces the full text of the secrets file: every known key set to the value given, and
		/// everything else in the existing file left exactly as it was.
		/// </summary>
		/// <param name="existingContent">Current file content, or null when there is no file yet.</param>
		/// <param name="values">
		/// The known keys and their values. A key absent from this map (or present with an empty
		/// value) is removed from the file; the servers treat an empty variable as unset anyway,
		/// and an empty assignment is noise.
		/// </param>
		/// <remarks>
		/// <para>
		/// Known keys are rewritten where they already stand, so the comment above a key stays with
		/// it. Keys that are not in the file yet are appended. Duplicate assignments to a key being
		/// written are collapsed to one: both systemd and <see cref="DatabaseSecrets"/> take the
		/// last assignment, so a leftover duplicate further down would quietly beat the new value.
		/// </para>
		/// <para>
		/// Values are written raw, never quoted. <see cref="DatabaseSecrets"/> takes everything
		/// after the first <c>=</c> literally — it does not strip quotes — so quoting here would
		/// deliver the quote characters to the server as part of the password. Quotes an operator
		/// wrote by hand around a key we are not writing survive untouched, because those lines are
		/// preserved rather than re-emitted.
		/// </para>
		/// <para>
		/// Public only so it can be tested directly; it is the one function here whose failure
		/// would destroy an operator's credentials.
		/// </para>
		/// </remarks>
		public static string ComposeSecretsFile(string? existingContent, IReadOnlyDictionary<string, string> values)
		{
			foreach (KeyValuePair<string, string> pair in values)
			{
				if (pair.Value.Contains('\n') || pair.Value.Contains('\r'))
				{
					throw new ArgumentException(
						$"The value for '{pair.Key}' contains a line break, which an environment file cannot represent.",
						nameof(values));
				}
			}

			if (string.IsNullOrWhiteSpace(existingContent))
			{
				return ComposeNewFile(values);
			}

			string newline = existingContent.Contains("\r\n") ? "\r\n" : "\n";
			bool endedWithNewline = existingContent.EndsWith('\n');
			List<string> lines = SplitLines(existingContent, endedWithNewline);

			var known = new HashSet<string>(Settings.Select(s => s.Key), StringComparer.Ordinal);
			var handled = new HashSet<string>(StringComparer.Ordinal);
			var output = new List<string>(lines.Count + Settings.Length);

			foreach (string line in lines)
			{
				// Comments, blank lines and anything that is not a plain assignment are content we
				// did not write and do not understand. They are kept exactly as they are.
				if (IsComment(line) || !TryParseAssignment(line, out string key, out string linePrefix))
				{
					output.Add(line);
					continue;
				}

				if (!known.Contains(key))
				{
					// Someone added this by hand. Not ours to touch.
					output.Add(line);
					continue;
				}

				if (handled.Contains(key))
				{
					// A duplicate of a key already rewritten above — dropped, or it would win.
					continue;
				}

				handled.Add(key);
				string value = values.GetValueOrDefault(key, "");
				if (value.Length > 0)
				{
					output.Add($"{linePrefix}{key}={value}");
				}
				// An empty value means the key is unset, so no line is emitted for it.
			}

			var appended = new List<string>();
			foreach (Setting setting in Settings)
			{
				if (handled.Contains(setting.Key))
				{
					continue;
				}
				string value = values.GetValueOrDefault(setting.Key, "");
				if (value.Length > 0)
				{
					appended.Add($"{setting.Key}={value}");
				}
			}

			if (appended.Count > 0)
			{
				if (output.Count > 0 && output[^1].Trim().Length != 0)
				{
					output.Add(string.Empty);
				}
				output.AddRange(appended);
			}

			var sb = new StringBuilder();
			foreach (string line in output)
			{
				sb.Append(line).Append(newline);
			}

			string result = sb.ToString();
			if (!endedWithNewline && appended.Count == 0 && result.EndsWith(newline, StringComparison.Ordinal))
			{
				// The file had no trailing newline; do not silently add one.
				result = result[..^newline.Length];
			}
			return result;
		}

		/// <summary>Builds the file from nothing: header, then the groups in menu order.</summary>
		private static string ComposeNewFile(IReadOnlyDictionary<string, string> values)
		{
			var sb = new StringBuilder();
			foreach (string line in NewFileHeader)
			{
				sb.Append(line).Append('\n');
			}

			string? group = null;
			foreach (Setting setting in Settings)
			{
				string value = values.GetValueOrDefault(setting.Key, "");
				if (value.Length == 0)
				{
					continue;
				}

				if (setting.Group != group)
				{
					group = setting.Group;
					sb.Append('\n').Append("# --- ").Append(group).Append(" ---").Append('\n');
				}
				sb.Append(setting.Key).Append('=').Append(value).Append('\n');
			}
			return sb.ToString();
		}

		/// <summary>
		/// Reads the secrets file into key/value pairs the way the servers read it, minus any
		/// quotes a human wrapped a value in.
		/// </summary>
		/// <remarks>
		/// Used to load the screen, never to decide what to write — what is written comes from
		/// <see cref="ComposeSecretsFile"/>, which works from the original lines. Public for the
		/// same reason as that one.
		/// </remarks>
		public static Dictionary<string, string> ParseKeyValues(string? content)
		{
			var result = new Dictionary<string, string>(StringComparer.Ordinal);
			if (string.IsNullOrEmpty(content))
			{
				return result;
			}

			foreach (string line in SplitLines(content, content.EndsWith('\n')))
			{
				if (IsComment(line) || !TryParseAssignment(line, out string key, out _))
				{
					continue;
				}

				string value = line[(line.IndexOf('=') + 1)..].Trim();
				if (value.Length >= 2 &&
					((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
				{
					value = value[1..^1];
				}
				result[key] = value;
			}

			return result;
		}

		// ──────────────────────────────────────────────────────────────────────
		//  Support for the uninstall path
		// ──────────────────────────────────────────────────────────────────────

		/// <summary>
		/// Every file an earlier installer could have written that DEFINES these environment
		/// variables, besides the secrets file itself.
		/// </summary>
		/// <remarks>
		/// Nothing creates these any more — the export that did is gone, because a second copy of a
		/// live credential in a shell profile is worse than the inconvenience it saved. They are
		/// still listed because they exist on machines configured before that: deleting the secrets
		/// file unsets nothing, and the fish one is sourced by every new shell, so an operator who
		/// has just "removed the secrets" can still have a live database password in the environment
		/// of every terminal they open, in a file that now outlives the database it belonged to.
		/// <para>
		/// The current directory is deliberately not searched: the old <c>.env</c> export landed
		/// wherever the installer happened to be run, which is not knowable from here, so that one
		/// is reported as a caveat rather than guessed at.
		/// </para>
		/// </remarks>
		internal static IReadOnlyList<string> ExportedSnippetPaths()
		{
			string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			if (string.IsNullOrWhiteSpace(home))
			{
				return Array.Empty<string>();
			}

			return new[]
			{
				Path.Combine(home, ".config", "fish", "conf.d", "fishmmo-secrets.fish"),
				Path.Combine(home, "Documents", "WindowsPowerShell", "fishmmo-secrets.ps1"),
				Path.Combine(home, "fishmmo-secrets.cmd"),
			};
		}

		/// <summary>
		/// The same read for callers outside this screen — the uninstall path, which must list the
		/// keys it is about to destroy and must distinguish "no file" from "cannot read it".
		/// </summary>
		/// <remarks>
		/// Exposed rather than duplicated: a second reader would be a second place that has to know
		/// about the sudo fallback and about the parent directory this user cannot traverse, and the
		/// one that forgot would report a file full of credentials as absent.
		/// </remarks>
		internal static Task<(bool Readable, string? Content)> TryReadSecretsFileAsync(string path)
		{
			return TryReadExistingAsync(path);
		}

		/// <summary>
		/// Blanks the SMTP credential in every configuration file that has a field for one,
		/// returning the files actually changed.
		/// </summary>
		/// <remarks>
		/// The write path already refuses to put the credential in JSON, so on a machine configured
		/// through this installer there is nothing here to clear. It exists for the machine that was
		/// configured by hand: a relay API key pasted into <c>appsettings.ControlPanel.json</c> or
		/// into <c>LoginServer.cfg</c> survives deleting the secrets file, survives uninstalling the
		/// database, and is committed the moment somebody stages the tree.
		/// <para>
		/// These files are NOT deleted — they hold host, port, From address and much else that is
		/// not secret. Only the credential field is emptied, and only when it actually holds
		/// something, so a clean tree reports nothing rather than rewriting files for no reason.
		/// </para>
		/// </remarks>
		internal static async Task<IReadOnlyList<string>> ClearSmtpCredentialsInConfigsAsync()
		{
			var changed = new List<string>();
			string setup = InstallationConstants.FishMMOSetupPath;
			if (string.IsNullOrWhiteSpace(setup) || !Directory.Exists(setup))
			{
				return changed;
			}

			foreach (string directory in Directory.GetDirectories(setup))
			{
				string json = Path.Combine(directory, "appsettings.ControlPanel.json");
				if (File.Exists(json) && await ClearJsonSmtpPasswordAsync(json))
				{
					changed.Add(json);
				}

				string cfg = Path.Combine(directory, "LoginServer.cfg");
				if (File.Exists(cfg) && await ClearCfgSmtpPasswordAsync(cfg))
				{
					changed.Add(cfg);
				}
			}

			return changed;
		}

		/// <summary>Empties Smtp:Password in a JSON settings file. Returns true if it had a value.</summary>
		private static async Task<bool> ClearJsonSmtpPasswordAsync(string path)
		{
			try
			{
				JsonObject? root = JsonNode.Parse(await File.ReadAllTextAsync(path, Encoding.UTF8))?.AsObject();
				if (root?["Smtp"] is not JsonObject smtp)
				{
					return false;
				}

				string current = smtp["Password"]?.GetValue<string>() ?? "";
				if (current.Length == 0)
				{
					return false;
				}

				smtp["Password"] = "";
				await File.WriteAllTextAsync(path,
					root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine,
					Utf8NoBom);
				return true;
			}
			catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
			{
				return false;
			}
		}

		/// <summary>Empties Smtp:Password in a key=value cfg file, leaving every other line alone.</summary>
		private static async Task<bool> ClearCfgSmtpPasswordAsync(string path)
		{
			try
			{
				string[] lines = await File.ReadAllLinesAsync(path, Encoding.UTF8);
				bool changed = false;
				for (int i = 0; i < lines.Length; i++)
				{
					// The key at the start of a line only — a comment mentioning it is not a setting.
					if (!lines[i].StartsWith("Smtp:Password=", StringComparison.Ordinal))
					{
						continue;
					}
					if (lines[i].Length > "Smtp:Password=".Length)
					{
						lines[i] = "Smtp:Password=";
						changed = true;
					}
				}

				if (changed)
				{
					await File.WriteAllLinesAsync(path, lines, Utf8NoBom);
				}
				return changed;
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				return false;
			}
		}

		/// <summary>UTF-8 without a byte order mark, for every file this class rewrites.</summary>
		/// <remarks>
		/// <c>Encoding.UTF8</c> EMITS a BOM when writing. None of these files has one, so using it
		/// would prepend three bytes to an operator's configuration for no reason — enough to make
		/// a strict JSON parser reject the file, which is how this was noticed. Reading with
		/// <c>Encoding.UTF8</c> is still correct: it strips a BOM if some other tool left one.
		/// </remarks>
		private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

		// ──────────────────────────────────────────────────────────────────────
		//  Reading and parsing
		// ──────────────────────────────────────────────────────────────────────

		/// <summary>
		/// Reads the secrets file, asking sudo only if the direct read is refused.
		/// </summary>
		/// <returns>
		/// (true, null) when there is no file, (true, content) when it was read, and (false, null)
		/// when it exists but cannot be read — in which case the caller must not write it.
		/// </returns>
		private static async Task<(bool Readable, string? Content)> TryReadExistingAsync(string path)
		{
			if (File.Exists(path))
			{
				try
				{
					return (true, await File.ReadAllTextAsync(path));
				}
				catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
				{
					string? elevated = await TryReadWithSudoAsync(path);
					return elevated != null ? (true, elevated) : (false, null);
				}
			}

			/* File.Exists is also false when a parent directory is not traversable by this user, so
			 * ask before concluding the file is not there — concluding wrongly means writing a new
			 * file over an existing one. */
			string? viaSudo = await TryReadWithSudoAsync(path);
			return viaSudo != null ? (true, viaSudo) : (true, null);
		}

		/// <summary>Reads a root-owned file with a non-interactive sudo, or gives up.</summary>
		/// <remarks>
		/// <c>-n</c> so sudo never prompts: a password prompt appearing mid-menu with no
		/// explanation is worse than the caller reporting that it could not read the file. The
		/// write path prompts properly, on its own screen.
		/// </remarks>
		private static async Task<string?> TryReadWithSudoAsync(string path)
		{
			if (OperatingSystem.IsWindows())
			{
				return null;
			}

			try
			{
				var psi = new ProcessStartInfo
				{
					FileName = "sudo",
					Arguments = $"-n cat '{path}'",
					UseShellExecute = false,
					CreateNoWindow = true,
					RedirectStandardOutput = true,
					RedirectStandardError = true,
				};
				using var proc = Process.Start(psi)!;
				string output = await proc.StandardOutput.ReadToEndAsync();
				await proc.WaitForExitAsync();
				return proc.ExitCode == 0 ? output : null;
			}
			catch
			{
				return null;
			}
		}

		private static List<string> SplitLines(string content, bool endedWithNewline)
		{
			var lines = new List<string>(content.Replace("\r\n", "\n").Split('\n'));
			if (endedWithNewline && lines.Count > 0)
			{
				// "A\n" splits to ["A", ""]; that last entry is the newline, not a blank line.
				lines.RemoveAt(lines.Count - 1);
			}
			return lines;
		}

		private static bool IsComment(string line)
		{
			string trimmed = line.TrimStart();
			return trimmed.Length > 0 && trimmed[0] == '#';
		}

		/// <summary>
		/// Decides whether a line is a <c>KEY=VALUE</c> assignment, and what its key is.
		/// </summary>
		/// <param name="linePrefix">
		/// Everything before the key — indentation, and an <c>export</c> keyword if there is one —
		/// so a rewritten line keeps the shape the operator gave it.
		/// </param>
		/// <returns>
		/// False for anything that is not an assignment to a legal environment variable name. The
		/// caller preserves those lines verbatim: a line this parser does not understand is far
		/// more likely to be something an operator needs than something to delete.
		/// </returns>
		private static bool TryParseAssignment(string line, out string key, out string linePrefix)
		{
			key = string.Empty;
			linePrefix = string.Empty;

			int equals = line.IndexOf('=');
			if (equals <= 0)
			{
				return false;
			}

			string left = line[..equals];
			int nameStart = 0;
			while (nameStart < left.Length && char.IsWhiteSpace(left[nameStart]))
			{
				nameStart++;
			}

			// "export KEY=VALUE" is valid in a shell-sourced file (systemd ignores the line, but
			// that is the operator's business). Either way the key is the same, and keeping the
			// prefix means the line goes back the way it came.
			const string ExportKeyword = "export ";
			if (left.Length - nameStart > ExportKeyword.Length &&
				left.AsSpan(nameStart, ExportKeyword.Length).SequenceEqual(ExportKeyword))
			{
				nameStart += ExportKeyword.Length;
				while (nameStart < left.Length && char.IsWhiteSpace(left[nameStart]))
				{
					nameStart++;
				}
			}

			string name = left[nameStart..].TrimEnd();
			if (!IsValidEnvironmentName(name))
			{
				return false;
			}

			key = name;
			linePrefix = left[..nameStart];
			return true;
		}

		private static bool IsValidEnvironmentName(string name)
		{
			if (name.Length == 0 || (!char.IsLetter(name[0]) && name[0] != '_'))
			{
				return false;
			}
			foreach (char c in name)
			{
				if (!char.IsLetterOrDigit(c) && c != '_')
				{
					return false;
				}
			}
			return true;
		}
	}
}
