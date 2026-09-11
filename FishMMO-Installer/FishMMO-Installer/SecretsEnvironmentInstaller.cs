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
	/// <b>One screen, one writer.</b> Every <c>FISHMMO_*</c> variable the servers actually read
	/// lands in a single file — <c>/etc/fishmmo/db-secrets.env</c> on Linux,
	/// <c>%ProgramData%\FishMMO\db-secrets.env</c> on Windows — which every systemd unit loads with
	/// <c>EnvironmentFile=-/etc/fishmmo/db-secrets.env</c>. Splitting that file between several
	/// wizards is how it used to lose data: each one rewrote the whole file and emitted only the
	/// keys it knew about, so configuring the database silently deleted the mail credentials.
	/// There is now exactly one editor, it loads every key before it writes any, and a full rewrite
	/// is therefore safe.
	/// </para>
	/// <para>
	/// <b>Keys it does not know are still preserved.</b> An operator will have added something —
	/// a proxy variable, a feature flag, a note to themselves. Unrecognised assignments, comments,
	/// blank lines and anything this parser cannot make sense of are carried through verbatim.
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

		private const string SmtpHostKey = "FISHMMO_SMTP_HOST";
		private const string SmtpPortKey = "FISHMMO_SMTP_PORT";
		private const string SmtpUsernameKey = "FISHMMO_SMTP_USERNAME";
		private const string SmtpPasswordKey = "FISHMMO_SMTP_PASSWORD";
		private const string SmtpFromAddressKey = "FISHMMO_SMTP_FROM_ADDRESS";
		private const string SmtpFromNameKey = "FISHMMO_SMTP_FROM_NAME";
		private const string SmtpUseSslKey = "FISHMMO_SMTP_USE_SSL";
		private const string EnvironmentKey = "FISHMMO_ENVIRONMENT";

		/// <summary>How a value is prompted for and displayed.</summary>
		private enum SettingKind
		{
			/// <summary>Free text, echoed back on screen.</summary>
			Text,

			/// <summary>A positive integer.</summary>
			Number,

			/// <summary>true/false.</summary>
			Bool,

			/// <summary>Never echoed, never logged, never written to JSON.</summary>
			Secret,
		}

		/// <summary>One environment variable this screen owns.</summary>
		/// <param name="Key">The variable name, exactly as the servers read it.</param>
		/// <param name="Group">Heading it appears under.</param>
		/// <param name="Label">Short name shown in the menu.</param>
		/// <param name="Kind">How it is prompted for.</param>
		/// <param name="Hint">What the server does when it is unset, or what it must agree with.</param>
		private sealed record Setting(string Key, string Group, string Label, SettingKind Kind, string Hint);

		/// <summary>
		/// Every variable this screen writes, in the order it is written to the file.
		/// </summary>
		/// <remarks>
		/// This list is the screen, the file layout and the export snippets. Adding a variable here
		/// adds it to all three; there is no second list to keep in step.
		/// </remarks>
		private static readonly Setting[] Settings =
		{
			new("FISHMMO_DB_HOST", GroupDatabase, "Host", SettingKind.Text, "default 127.0.0.1"),
			new("FISHMMO_DB_PORT", GroupDatabase, "Port", SettingKind.Number, "default 5432"),
			new("FISHMMO_DB_NAME", GroupDatabase, "Database name", SettingKind.Text, "default fishmmo"),
			new("FISHMMO_DB_USERNAME", GroupDatabase, "Username", SettingKind.Text, ""),
			new("FISHMMO_DB_PASSWORD", GroupDatabase, "Password", SettingKind.Secret, "secret"),
			new("FISHMMO_DB_SCHEMA", GroupDatabase, "Schema", SettingKind.Text, "must match Npgsql:Schema in appsettings"),

			new(SmtpHostKey, GroupMail, "SMTP host", SettingKind.Text, "default localhost = no relay"),
			new(SmtpPortKey, GroupMail, "SMTP port", SettingKind.Number, "587; 465 is not supported"),
			new(SmtpUsernameKey, GroupMail, "SMTP username", SettingKind.Text, "'resend' for Resend"),
			new(SmtpPasswordKey, GroupMail, "SMTP password / API key", SettingKind.Secret, "secret; never written to JSON"),
			new(SmtpFromAddressKey, GroupMail, "From address", SettingKind.Text, "required to send"),
			new(SmtpFromNameKey, GroupMail, "From name", SettingKind.Text, "default FishMMO"),
			new(SmtpUseSslKey, GroupMail, "TLS (STARTTLS)", SettingKind.Bool, "default true"),

			new(EnvironmentKey, GroupService, "Environment", SettingKind.Text, "Development or Production"),
			new("FISHMMO_LOG_LEVEL", GroupService, "Log level", SettingKind.Text, "Verbose / Debug / Info / Warning / Error"),

			new("FISHMMO_DISCORD_TOKEN", GroupSecrets, "Discord bot token", SettingKind.Secret, "secret; read by FishMMO-DiscordBot"),
			new("FISHMMO_VERSION_MANIFEST_SIGNING_KEY", GroupSecrets, "Manifest signing key", SettingKind.Secret, "secret; read by the Patcher"),
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

			Dictionary<string, string> values = ParseKeyValues(existingContent);

			while (true)
			{
				Console.Clear();
				Console.WriteLine("=== Configure Secrets & Environment ===");
				Console.WriteLine($"Secrets file: {secretsPath}");
				Console.WriteLine($"Control Panel settings: {ControlPanelSettingsPath(values) ?? "(FishMMO-Setup not found — mail settings go to the secrets file only)"}");
				Console.WriteLine();

				string? group = null;
				for (int i = 0; i < Settings.Length; i++)
				{
					Setting setting = Settings[i];
					if (setting.Group != group)
					{
						group = setting.Group;
						Console.WriteLine($"-- {group} --");
					}

					string shown = Display(setting, values);
					string hint = string.IsNullOrEmpty(setting.Hint) ? "" : $"   {setting.Hint}";
					Console.WriteLine($"{i + 1,3} : {setting.Label,-24} [{shown}]{hint}");
				}

				Console.WriteLine();
				Console.WriteLine($"Mail status: {DescribeMail(values)}");
				Console.WriteLine();
				Console.WriteLine("  R : Resend preset (mail)      T : Send a test message");
				Console.WriteLine("  W : Write configuration       X : Export as environment variables");
				Console.WriteLine("  0 : Back");
				Console.WriteLine();
				Console.WriteLine("Not written here: the PostgreSQL superuser password (prompted per run), the signing-key");
				Console.WriteLine("KEK and client gate secret (Database ▸ Configure Server Keys — they live in the");
				Console.WriteLine("deployment_secrets table), and FISHMMO_CONNECTION_STRING (superseded by FISHMMO_DB_*).");
				Console.WriteLine();
				Console.Write("Select: ");

				string choice = (Console.ReadLine() ?? string.Empty).Trim();
				Console.WriteLine();

				if (choice.Length == 0)
				{
					continue;
				}

				if (choice == "0")
				{
					return;
				}

				if (int.TryParse(choice, out int index) && index >= 1 && index <= Settings.Length)
				{
					EditSetting(Settings[index - 1], values);
					continue;
				}

				switch (choice.ToUpperInvariant())
				{
					case "R":
						ApplyResendPreset(values);
						break;
					case "T":
						await SendTestMessage(values);
						break;
					case "W":
						await WriteConfiguration(secretsPath, values);
						break;
					case "X":
						await ExportEnvironmentVariables(values);
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

		/// <summary>What a setting's current value looks like on screen — secrets are masked.</summary>
		private static string Display(Setting setting, IReadOnlyDictionary<string, string> values)
		{
			if (!values.TryGetValue(setting.Key, out string? value) || value.Length == 0)
			{
				return "not set";
			}
			return setting.Kind == SettingKind.Secret ? MaskSecret(value) : value;
		}

		/// <summary>Shows that a secret exists without showing any part of it.</summary>
		/// <remarks>Fixed width on purpose: the length of a credential is information too.</remarks>
		private static string MaskSecret(string? value)
			=> string.IsNullOrEmpty(value) ? "not set" : "********";

		private static void EditSetting(Setting setting, Dictionary<string, string> values)
		{
			values.TryGetValue(setting.Key, out string? current);

			switch (setting.Kind)
			{
				case SettingKind.Secret:
				{
					Console.WriteLine($"{setting.Label} ({setting.Key})");
					Console.WriteLine("  Input is hidden. Type '-' to clear it.");
					string entered = InstallerProcessHelper.PromptForRequiredPassword("  Value: ");
					if (entered == "-")
					{
						values.Remove(setting.Key);
						Console.WriteLine("  Cleared.");
					}
					else if (entered.Contains('\n') || entered.Contains('\r'))
					{
						// An environment file cannot represent a line break, so refuse rather than
						// write something the server would read back as a truncated credential.
						Console.WriteLine("  Refused: the value contains a line break.");
					}
					else
					{
						values[setting.Key] = entered;
						Console.WriteLine("  Set.");
					}
					break;
				}

				case SettingKind.Bool:
				{
					Console.Write($"{setting.Label} (true/false) [{current ?? "not set"}]: ");
					string input = (Console.ReadLine() ?? string.Empty).Trim();
					if (input.Length == 0)
					{
						break;
					}
					if (input == "-")
					{
						values.Remove(setting.Key);
						break;
					}
					if (bool.TryParse(input, out bool parsed))
					{
						values[setting.Key] = parsed ? "true" : "false";
					}
					else
					{
						Console.WriteLine("  Enter true or false.");
					}
					break;
				}

				case SettingKind.Number:
				{
					Console.Write($"{setting.Label} [{current ?? "not set"}]: ");
					string input = (Console.ReadLine() ?? string.Empty).Trim();
					if (input.Length == 0)
					{
						break;
					}
					if (input == "-")
					{
						values.Remove(setting.Key);
						break;
					}
					if (int.TryParse(input, out int number) && number > 0)
					{
						values[setting.Key] = number.ToString();
						if (setting.Key == SmtpPortKey && number == 465)
						{
							WarnAboutImplicitTls();
						}
					}
					else
					{
						Console.WriteLine("  Enter a positive integer.");
					}
					break;
				}

				default:
				{
					Console.Write($"{setting.Label} [{current ?? "not set"}]  (Enter keeps, '-' clears): ");
					string input = (Console.ReadLine() ?? string.Empty).Trim();
					if (input.Length == 0)
					{
						break;
					}
					if (input == "-")
					{
						values.Remove(setting.Key);
						break;
					}
					values[setting.Key] = input;
					break;
				}
			}
		}

		/// <summary>
		/// Says plainly that this sender cannot do implicit TLS, rather than letting a send sit on
		/// a socket until the timeout.
		/// </summary>
		private static void WarnAboutImplicitTls()
		{
			Console.WriteLine();
			Console.WriteLine("  Port 465 is implicit TLS, which System.Net.Mail does not support.");
			Console.WriteLine("  A send on 465 will hang until it times out and then fail with nothing useful.");
			Console.WriteLine("  Use 587 (STARTTLS, TLS on) — that is what the relays this project targets expect.");
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
			Console.WriteLine("=== Resend preset ===");
			Console.WriteLine();
			Console.WriteLine("Sets host smtp.resend.com, port 587, username 'resend' and TLS on.");
			Console.WriteLine("Confirm the host and port against your Resend dashboard before relying on them.");
			Console.WriteLine();

			values[SmtpHostKey] = "smtp.resend.com";
			values[SmtpPortKey] = "587";
			values[SmtpUsernameKey] = "resend";
			values[SmtpUseSslKey] = "true";

			Console.WriteLine("API key (hidden; this is the password — it is written only to the secrets file).");
			string apiKey = InstallerProcessHelper.PromptForRequiredPassword("  API key: ");
			if (apiKey != "-")
			{
				values[SmtpPasswordKey] = apiKey;
			}

			Console.Write($"From address [{values.GetValueOrDefault(SmtpFromAddressKey, "not set")}]: ");
			string from = (Console.ReadLine() ?? string.Empty).Trim();
			if (from.Length > 0)
			{
				values[SmtpFromAddressKey] = from;
			}

			Console.WriteLine();
			Console.WriteLine("The From address must be on a domain you have verified with Resend, or it will");
			Console.WriteLine("reject the message. Nothing has been written yet — choose W to write.");
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
				Console.WriteLine();
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
		private static async Task WriteConfiguration(string secretsPath, Dictionary<string, string> values)
		{
			Console.WriteLine("=== Write configuration ===");
			Console.WriteLine();

			(bool readable, string? existingContent) = await TryReadExistingAsync(secretsPath);
			if (!readable)
			{
				Console.WriteLine($"Refusing to write: {secretsPath} exists but could not be read, and writing");
				Console.WriteLine("it now would destroy the keys that could not be read.");
				await Log.Error("FishMMOInstaller", $"Refused to write unreadable secrets file {secretsPath}.");
				return;
			}

			string content = ComposeSecretsFile(existingContent, values);
			await DatabaseSecretsInstaller.WriteWithSudoIfNeeded(secretsPath, content);

			Console.WriteLine();
			Console.WriteLine("Secrets and environment written. Every FISHMMO_* value above, including the");
			Console.WriteLine("passwords, is in that file and nowhere else. Restart the services to pick it up:");
			Console.WriteLine("  sudo systemctl restart 'fishmmo-*'");

			await WriteControlPanelMailSettings(values);
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

		/// <summary>
		/// The same read for callers outside this screen — the uninstall path, which must list the
		/// keys it is about to destroy and must distinguish "no file" from "cannot read it".
		/// </summary>
		/// <remarks>
		/// Exposed rather than duplicated: a second reader would be a second place that has to know
		/// about the sudo fallback and about the parent directory this user cannot traverse, and the
		/// one that forgot would report a file full of credentials as absent.
		/// </remarks>
		/// <summary>
		/// Every file this installer can write that DEFINES the environment variables, besides the
		/// secrets file itself.
		/// </summary>
		/// <remarks>
		/// Deleting the secrets file does not unset anything. These exported copies keep setting the
		/// variables — the fish one is sourced by every new shell — so an operator who "removed the
		/// secrets" can still have a live database password in their environment, and in a file that
		/// outlives the database it belonged to. The current directory is deliberately not searched:
		/// the .env export lands wherever the installer happened to be run, which is not knowable
		/// from here, so that one is reported as a caveat rather than guessed at.
		/// </remarks>
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

		internal static Task<(bool Readable, string? Content)> TryReadSecretsFileAsync(string path)
		{
			return TryReadExistingAsync(path);
		}

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

		// ──────────────────────────────────────────────────────────────────────
		//  Export
		// ──────────────────────────────────────────────────────────────────────

		/// <summary>
		/// Writes the same values as a shell snippet, for a machine that does not use the secrets
		/// file (a developer workstation, or a Windows host).
		/// </summary>
		private static async Task ExportEnvironmentVariables(IReadOnlyDictionary<string, string> values)
		{
			Console.WriteLine("=== Export as environment variables ===");
			Console.WriteLine();
			Console.WriteLine("These files contain the secrets in plain text. They are written chmod 600 where");
			Console.WriteLine("the platform supports it.");
			Console.WriteLine();
			Console.WriteLine("1 : fish shell  (~/.config/fish/conf.d/fishmmo-secrets.fish)");
			Console.WriteLine("2 : systemd / .env (fishmmo-secrets.env in the current directory)");
			Console.WriteLine("3 : Windows PowerShell / CMD");
			Console.WriteLine("0 : Back");

			ConsoleKeyInfo key = Console.ReadKey(true);
			Console.WriteLine();
			if (key.Key == ConsoleKey.D0 || key.KeyChar == '0')
			{
				return;
			}

			var exported = new Dictionary<string, string>(StringComparer.Ordinal);
			foreach (Setting setting in Settings)
			{
				string value = values.GetValueOrDefault(setting.Key, "");
				if (value.Length > 0)
				{
					exported[setting.Key] = value;
				}
			}

			switch (key.Key)
			{
				case ConsoleKey.D1:
					await DatabaseSecretsInstaller.WriteFishSnippet(exported);
					break;
				case ConsoleKey.D2:
					await DatabaseSecretsInstaller.WriteEnvFile(exported);
					break;
				case ConsoleKey.D3:
					await DatabaseSecretsInstaller.WriteWindowsSnippets(exported);
					break;
				default:
					Console.WriteLine("Invalid option.");
					break;
			}
		}
	}
}
