using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using System.Text.RegularExpressions;

namespace FishMMO.ControlPanel.Services
{
	/// <summary>
	/// Delivers one message to the configured relay.
	/// </summary>
	/// <remarks>
	/// Separate from the queue on purpose: <see cref="EmailQueueDrainService"/> decides what to
	/// send and what to record afterwards, and this decides only how a message reaches a relay.
	/// </remarks>
	public interface ISmtpSender
	{
		/// <summary>
		/// Whether this deployment has a relay worth talking to.
		/// </summary>
		/// <remarks>
		/// False is a supported state, not a fault. A panel brought up for development, or one
		/// standing in front of a shard whose mail goes out elsewhere, has no relay and should
		/// not be spending a database round trip every couple of seconds claiming messages it
		/// cannot deliver.
		/// </remarks>
		bool IsConfigured { get; }

		/// <summary>
		/// One line naming where mail goes, or why it goes nowhere. Safe to log: it never
		/// contains the username or the password.
		/// </summary>
		string ConfigurationSummary { get; }

		/// <summary>
		/// Sends one message, returning whether the relay accepted it.
		/// </summary>
		/// <param name="to">Recipient address.</param>
		/// <param name="subject">Subject line.</param>
		/// <param name="body">
		/// The stored body, either plain text or HTML. Both parts are derived from it; see
		/// <see cref="SmtpSender"/>.
		/// </param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>True when the relay accepted the message; false for every failure, which is
		/// what the queue records as an attempt.</returns>
		Task<bool> SendAsync(
			string to,
			string subject,
			string body,
			CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// The panel's SMTP sender, configured exactly the way the LoginServer's is.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Configuration is deliberately identical to <c>FishMMO.Server.Implementation.Smtp.SmtpService</c>.</b>
	/// The same environment variables (<c>FISHMMO_SMTP_HOST</c> and friends) take precedence over
	/// the same configuration keys (<c>Smtp:Host</c> and friends), so one set of credentials
	/// configures whichever process happens to be draining the queue. Two processes that had to
	/// be configured differently to send the same mail would be a trap waiting for whoever
	/// migrates the drain again.
	/// </para>
	/// <para>
	/// <b>System.Net.Mail, and no new package.</b> <see cref="SmtpClient"/> is formally obsolete
	/// for new work and MailKit is the better library — it speaks modern authentication, reuses
	/// connections, and is what Microsoft's own obsoletion notice points at. Taking a dependency
	/// is not a decision this change is allowed to make, so this uses the framework client, the
	/// same one the LoginServer already used against the same relay. If the relay ever needs
	/// OAuth or connection reuse, that is the moment to revisit it.
	/// </para>
	/// <para>
	/// <b><see cref="SmtpClient.EnableSsl"/> means STARTTLS, not implicit TLS.</b> The client
	/// connects in the clear and upgrades, which is what port 587 expects and what the intended
	/// relay wants. Implicit TLS on port 465 is <i>not</i> supported by this client at all —
	/// pointing it at 465 hangs until the timeout rather than failing usefully — so a deployment
	/// that only offers 465 needs MailKit, not a configuration change.
	/// </para>
	/// <para>
	/// <b>Every message goes out as multipart/alternative: plain text and HTML.</b> The previous
	/// sender set <c>IsBodyHtml = true</c> for everything, and three of the four bodies this
	/// system sends are plain text written with <c>\n</c> line breaks. Delivered as HTML those
	/// newlines collapse, and the result is one run-on paragraph with the code buried in the
	/// middle of a sentence — including a 43-character password reset token, which is exactly
	/// the kind of thing a reader has to be able to pick out and copy. Sending both parts also
	/// scores better with spam filters than either part alone.
	/// </para>
	/// <para>
	/// <b>The missing part is derived here rather than stored.</b> The queue carries one body
	/// column and gains no second one: the LoginServer's own verification mail is already HTML
	/// and cannot be changed from this side, so the sender has to handle an HTML body correctly
	/// regardless — and once it does, deriving in the other direction costs nothing and fixes
	/// every message already sitting in the queue. See <see cref="EmailBodyParts"/> for how each
	/// direction is decided, and for why an already-HTML body is never escaped or wrapped twice.
	/// </para>
	/// <para>
	/// <b>Nothing here logs a body.</b> A verification body carries the six-digit code and a
	/// reset body carries the token; both are credentials, and a log file is not a mailbox. The
	/// recipient, the subject and the outcome are logged, and that is all.
	/// </para>
	/// </remarks>
	public sealed class SmtpSender : ISmtpSender
	{
		/// <summary>How long a single delivery may take before it is treated as failed.</summary>
		private const int TimeoutMilliseconds = 30_000;

		private readonly string host;
		private readonly int port;
		private readonly string username;
		private readonly string password;
		private readonly string fromAddress;
		private readonly string fromName;
		private readonly bool useSsl;

		private readonly ILogger<SmtpSender> log;

		/// <summary>Why this configuration cannot send, or null when it can.</summary>
		private readonly string? configurationError;

		/// <summary>Reads the SMTP settings once, at construction.</summary>
		/// <remarks>
		/// Once, because a relay that changes underneath a running process is not a thing
		/// operators do and a half-applied credential change is worse than a restart.
		/// </remarks>
		public SmtpSender(IConfiguration configuration, ILogger<SmtpSender> log)
		{
			this.log = log;

			host = EnvOrConfig(configuration, "FISHMMO_SMTP_HOST", "Smtp:Host", "localhost");
			port = int.TryParse(Environment.GetEnvironmentVariable("FISHMMO_SMTP_PORT"), out int envPort)
				? envPort
				: configuration.GetValue("Smtp:Port", 587);
			username = EnvOrConfig(configuration, "FISHMMO_SMTP_USERNAME", "Smtp:Username", "");
			password = EnvOrConfig(configuration, "FISHMMO_SMTP_PASSWORD", "Smtp:Password", "");
			fromAddress = EnvOrConfig(configuration, "FISHMMO_SMTP_FROM_ADDRESS", "Smtp:FromAddress", "");
			fromName = EnvOrConfig(configuration, "FISHMMO_SMTP_FROM_NAME", "Smtp:FromName", "FishMMO");
			useSsl = EnvOrConfig(configuration, "FISHMMO_SMTP_USE_SSL", "Smtp:UseSsl", "true")
				.Equals("true", StringComparison.OrdinalIgnoreCase);

			configurationError = DescribeConfiguration();
			ConfigurationSummary = configurationError ??
				$"relay {host}:{port}, {(useSsl ? "STARTTLS" : "no TLS")}, " +
				$"from {fromAddress}, {(string.IsNullOrEmpty(username) ? "no credentials" : "authenticated")}";
		}

		/// <inheritdoc />
		/// <remarks>
		/// Three ways to be unconfigured, and they are all ordinary. No host at all is the
		/// obvious one. A <c>localhost</c> host with no credentials is the default this class
		/// falls back to when nothing is set, which means "nobody configured mail" far more often
		/// than it means "there is a relay on this box" — a deployment that really does run a
		/// local relay names it explicitly or supplies credentials, and either satisfies this.
		/// A missing or malformed From address is the third: there is nothing to put in the
		/// envelope, and <see cref="MailAddress"/> would throw on the first send rather than
		/// letting the queue idle quietly.
		/// </remarks>
		public bool IsConfigured => configurationError == null;

		/// <inheritdoc />
		/// <remarks>
		/// Built once, at construction, and never contains the credentials — so the drain can log
		/// it verbatim on the way up, whether it says where mail goes or why it does not go.
		/// </remarks>
		public string ConfigurationSummary { get; }

		/// <inheritdoc />
		public async Task<bool> SendAsync(
			string to,
			string subject,
			string body,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(to))
			{
				log.LogWarning("Cannot send mail: the recipient address is empty.");
				return false;
			}

			if (!IsConfigured)
			{
				log.LogWarning("Cannot send mail to {Recipient}: {Reason}", to, configurationError);
				return false;
			}

			try
			{
				using var message = new MailMessage
				{
					From = new MailAddress(fromAddress, fromName),
					Subject = subject ?? string.Empty,
					SubjectEncoding = Encoding.UTF8,
				};
				message.To.Add(to);

				/* Text first, HTML second. A multipart/alternative body is ordered worst part to
				 * best, and a client shows the last one it can render — so the order here is what
				 * decides that a graphical client shows the HTML and a text-only one still gets a
				 * message it can read. */
				var (text, html) = EmailBodyParts.Derive(body);
				message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
					text, Encoding.UTF8, MediaTypeNames.Text.Plain));
				message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
					html, Encoding.UTF8, MediaTypeNames.Text.Html));

				using var client = new SmtpClient(host, port)
				{
					// STARTTLS on the submission port; see the class remarks.
					EnableSsl = useSsl,
					DeliveryMethod = SmtpDeliveryMethod.Network,
					Timeout = TimeoutMilliseconds,
				};

				if (!string.IsNullOrEmpty(username))
				{
					client.Credentials = new NetworkCredential(username, password);
				}

				/* SmtpClient's task API does not take a cancellation token. Registering
				 * SendAsyncCancel is what makes host shutdown actually stop a send in flight
				 * rather than waiting out the 30-second timeout. */
				using (cancellationToken.Register(() =>
				{
					try
					{
						client.SendAsyncCancel();
					}
					catch (InvalidOperationException)
					{
						// Nothing in flight to cancel. Nothing to do about it either.
					}
				}))
				{
					await client.SendMailAsync(message).ConfigureAwait(false);
				}

				log.LogInformation("Mail sent to {Recipient}: {Subject}", to, subject);
				return true;
			}
			catch (SmtpFailedRecipientException ex)
			{
				log.LogWarning("Relay rejected {Recipient} for '{Subject}': {Message}", to, subject, ex.Message);
				return false;
			}
			catch (SmtpException ex)
			{
				log.LogError("SMTP error sending to {Recipient} ('{Subject}'): {Message}", to, subject, ex.Message);
				return false;
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				log.LogError(ex, "Unexpected error sending mail to {Recipient} ('{Subject}').", to, subject);
				return false;
			}
		}

		/// <summary>
		/// Decides whether this configuration can send anything, and returns the reason it
		/// cannot, or null when it can.
		/// </summary>
		private string? DescribeConfiguration()
		{
			if (string.IsNullOrWhiteSpace(host))
			{
				return "no SMTP host is configured (FISHMMO_SMTP_HOST or Smtp:Host)";
			}

			bool loopback = host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
							host.Equals("127.0.0.1", StringComparison.Ordinal) ||
							host.Equals("::1", StringComparison.Ordinal);
			if (loopback && string.IsNullOrEmpty(username))
			{
				return $"SMTP host is '{host}' with no credentials, which is the unconfigured default " +
					   "(set FISHMMO_SMTP_HOST or Smtp:Host)";
			}

			if (string.IsNullOrWhiteSpace(fromAddress))
			{
				return "no SMTP From address is configured (FISHMMO_SMTP_FROM_ADDRESS or Smtp:FromAddress)";
			}

			try
			{
				_ = new MailAddress(fromAddress, fromName);
			}
			catch (Exception ex) when (ex is FormatException or ArgumentException)
			{
				return $"the SMTP From address '{fromAddress}' is not a valid mail address";
			}

			return null;
		}

		/// <summary>
		/// Resolves one setting: environment variable first, then configuration, then a default.
		/// </summary>
		/// <remarks>
		/// The order matters and matches the LoginServer's. Credentials are injected as
		/// environment variables by every deployment this project supports (systemd unit files,
		/// containers, the setup scripts), and a stale value in a configuration file that beat
		/// them would be very hard to see.
		/// </remarks>
		private static string EnvOrConfig(IConfiguration configuration, string envKey, string configKey, string fallback)
		{
			string? env = Environment.GetEnvironmentVariable(envKey);
			return !string.IsNullOrEmpty(env) ? env : configuration[configKey] ?? fallback;
		}
	}

	/// <summary>
	/// Turns one stored body into the two parts a multipart/alternative message needs.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Four bodies reach this system and they are not all the same shape. The LoginServer's
	/// verification mail is a small HTML document. The panel's registration, email-change and
	/// password-reset bodies are plain text with <c>\n</c> breaks. Rather than require every
	/// author to produce both — which would leave every row already in the queue wrong, and
	/// cannot be done at all to the LoginServer's body from this side — the sender derives the
	/// part it was not given.
	/// </para>
	/// <para>
	/// <b>An HTML body is never escaped or wrapped a second time.</b> <see cref="LooksLikeHtml"/>
	/// decides which direction to derive in, and the original text is always carried through as
	/// the part it already was; only the part that did not exist is generated.
	/// </para>
	/// <para>
	/// Bodies are generated from account names, which are <c>[a-zA-Z0-9_]+</c>, and from codes
	/// that are digits or base64url — so no markup can arrive inside one. Interpolations are
	/// HTML-encoded on the way into the HTML part anyway: that is what keeps this safe if a body
	/// ever starts carrying a display name, a support message, or anything else a person typed.
	/// </para>
	/// </remarks>
	internal static class EmailBodyParts
	{
		/// <summary>Blocks whose content must not survive into the text part at all.</summary>
		private static readonly Regex ScriptOrStyle = new(
			@"<(script|style)\b[^>]*>.*?</\1\s*>",
			RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

		/// <summary>Tags that end a line when flattening HTML to text.</summary>
		private static readonly Regex LineBreakingTags = new(
			@"<\s*(br|/p|/div|/h[1-6]|hr|/tr|/li)\s*/?\s*>",
			RegexOptions.IgnoreCase | RegexOptions.Compiled);

		/// <summary>Tags that end a paragraph when flattening HTML to text.</summary>
		private static readonly Regex ParagraphBreakingTags = new(
			@"<\s*(/p|/div|/h[1-6]|hr|/table|/ul|/ol)\s*/?\s*>",
			RegexOptions.IgnoreCase | RegexOptions.Compiled);

		/// <summary>Any remaining markup.</summary>
		private static readonly Regex AnyTag = new(@"<[^>]*>", RegexOptions.Compiled);

		/// <summary>Three or more consecutive newlines.</summary>
		private static readonly Regex ExcessBlankLines = new(@"(\r?\n){3,}", RegexOptions.Compiled);

		/// <summary>Trailing spaces on a line.</summary>
		private static readonly Regex TrailingSpaces = new(@"[ \t]+(\r?\n)", RegexOptions.Compiled);

		/// <summary>
		/// Produces the plain-text and HTML parts for a stored body.
		/// </summary>
		public static (string Text, string Html) Derive(string body)
		{
			string source = body ?? string.Empty;

			return LooksLikeHtml(source)
				? (ToPlainText(source), source)
				: (source, ToHtml(source));
		}

		/// <summary>
		/// Whether a body is already an HTML document or fragment.
		/// </summary>
		/// <remarks>
		/// A body is HTML when it opens with a tag. That is a crude test and a deliberate one:
		/// every body this system sends is generated, every plain one opens with "Hello", and
		/// every HTML one opens with <c>&lt;html&gt;</c>. A subtler sniffer — looking for tags
		/// anywhere in the text — would misread a plain-text body that merely mentioned one, and
		/// there is no body here whose correct classification this test gets wrong.
		/// </remarks>
		private static bool LooksLikeHtml(string body)
		{
			string trimmed = body.TrimStart();
			return trimmed.StartsWith("<", StringComparison.Ordinal);
		}

		/// <summary>
		/// Flattens an HTML body into readable text.
		/// </summary>
		/// <remarks>
		/// Not a general-purpose HTML renderer and does not need to be: the only HTML that
		/// reaches it is the LoginServer's verification mail, which is headings, paragraphs and a
		/// rule. What matters is that the code survives on a line of its own, and that no markup
		/// or script text leaks into what a text-only client shows.
		/// </remarks>
		private static string ToPlainText(string html)
		{
			string working = ScriptOrStyle.Replace(html, string.Empty);

			// Paragraph breaks first: both patterns match the same closing tags, and doing the
			// coarser one first means a </p> becomes a blank line rather than a single break.
			working = ParagraphBreakingTags.Replace(working, "\n\n");
			working = LineBreakingTags.Replace(working, "\n");
			working = AnyTag.Replace(working, string.Empty);

			working = WebUtility.HtmlDecode(working);

			// Source indentation inside the markup is layout, not content.
			var lines = working.Replace("\r\n", "\n").Split('\n');
			for (int i = 0; i < lines.Length; i++)
			{
				lines[i] = lines[i].Trim();
			}

			working = string.Join("\n", lines);
			working = ExcessBlankLines.Replace(working, "\n\n");

			return working.Trim() + "\n";
		}

		/// <summary>
		/// Builds an HTML part from a plain-text body, preserving its paragraphs.
		/// </summary>
		/// <remarks>
		/// The line breaks are the entire point. A blank line starts a new paragraph and a single
		/// newline is a line break inside one, which is how these bodies are written — so the
		/// code or token that sits alone on its own line goes on being alone on its own line
		/// instead of being swallowed into a sentence.
		/// </remarks>
		private static string ToHtml(string text)
		{
			string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();

			var paragraphs = new StringBuilder();
			foreach (string block in normalized.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
			{
				string trimmedBlock = block.Trim();
				if (trimmedBlock.Length == 0)
				{
					continue;
				}

				// Encoded before any markup is added, so nothing in the body can become markup.
				string encoded = WebUtility.HtmlEncode(trimmedBlock).Replace("\n", "<br />\n");
				paragraphs.Append("<p>").Append(encoded).Append("</p>\n");
			}

			if (paragraphs.Length == 0)
			{
				paragraphs.Append("<p></p>\n");
			}

			/* Styled to match the LoginServer's verification mail, so a player who gets one of
			 * each does not get two different-looking messages from the same shard. Inline
			 * styles because mail clients discard a <style> block. */
			return "<html><body style=\"font-family: Arial, sans-serif; color: #333; font-size: 15px; line-height: 1.5;\">\n" +
				   paragraphs +
				   "</body></html>";
		}
	}
}
