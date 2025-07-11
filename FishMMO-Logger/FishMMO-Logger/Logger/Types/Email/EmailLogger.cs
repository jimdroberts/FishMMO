using System.Net;
using System.Net.Mail;
using System.Text;
using System.Linq;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace FishMMO.Logging
{
	/// <summary>
	/// Implements ILogger to send critical/error log entries as emails to specified recipients.
	/// Email sending is asynchronous and non-blocking.
	/// </summary>
	public class EmailLogger : ILogger
	{
		private readonly EmailLoggerConfig config;
		private HashSet<LogLevel> allowedLevels;
		public bool IsEnabled { get; private set; }
		public bool HandlesConsoleParts { get { return true; } }

		public IReadOnlyCollection<LogLevel> AllowedLevels => allowedLevels;

		private readonly Action<string> internalLogCallback;

		/// <summary>
		/// Initializes a new instance of the EmailLogger.
		/// </summary>
		/// <param name="config">Configuration for the email logger, including SMTP settings and recipient addresses.</param>
		/// <param name="internalLogCallback">Optional: A callback action for internal messages from the logger itself. If null, defaults to Console.WriteLine.</param>
		public EmailLogger(EmailLoggerConfig config, Action<string> internalLogCallback = null)
		{
			this.config = config ?? throw new ArgumentNullException(nameof(config), "EmailLogger configuration cannot be null.");
			// Set the internal log callback, defaulting to Console.WriteLine if none provided
			this.internalLogCallback = internalLogCallback ?? (msg => Console.WriteLine(msg));

			IsEnabled = false; // Assume disabled until successfully validated or configured

			if (!config.Enabled)
			{
				internalLogCallback?.Invoke($"[EmailLogger] Email logging is disabled by configuration.");
				SetAllowedLevels(config.AllowedLevels); // Still set allowed levels even if disabled
			}
			else
			{
				// Basic validation for essential configuration properties
				if (string.IsNullOrWhiteSpace(config.SmtpHost) || string.IsNullOrWhiteSpace(config.FromAddress) || config.ToAddresses == null || !config.ToAddresses.Any())
				{
					internalLogCallback?.Invoke($"[EmailLogger] ERROR: EmailLogger is enabled but missing required SMTP host, From address, or To addresses in configuration. Disabling.");
					IsEnabled = false;
				}
				else
				{
					SetAllowedLevels(config.AllowedLevels); // Set allowed levels from config
					IsEnabled = true; // Enable if configuration is valid
					internalLogCallback?.Invoke($"[EmailLogger] Email logger initialized. SMTP: {config.SmtpHost}:{config.SmtpPort}, From: {config.FromAddress}, To: {string.Join(", ", config.ToAddresses)}.");
				}
			}
		}

		/// <summary>
		/// Logs a structured entry by sending it as an email.
		/// </summary>
		/// <param name="entry">The log entry to send.</param>
		public async Task Log(LogEntry entry)
		{
			// Only log if enabled and the log level is allowed for this logger
			if (!IsEnabled || !allowedLevels.Contains(entry.Level))
			{
				return;
			}

			try
			{
				using (SmtpClient client = new SmtpClient(config.SmtpHost, config.SmtpPort))
				{
					client.EnableSsl = true;
					client.UseDefaultCredentials = false; // Explicitly set to false to use NetworkCredential
					if (!string.IsNullOrWhiteSpace(config.SmtpUsername))
					{
						client.Credentials = new NetworkCredential(config.SmtpUsername, config.SmtpPassword);
					}
					// Optional: Set timeout for sending email
					client.Timeout = 10000; // 10 seconds

					using (MailMessage mailMessage = new MailMessage())
					{
						mailMessage.From = new MailAddress(config.FromAddress);
						foreach (string toAddress in config.ToAddresses)
						{
							mailMessage.To.Add(toAddress);
						}
						// Construct the email subject
						mailMessage.Subject = $"{config.SubjectPrefix} {entry.Level.ToString().ToUpper()}: {entry.Source} - {entry.Message.Take(50)}..."; // Shorten message for subject

						// Construct the email body with detailed log information
						StringBuilder bodyBuilder = new StringBuilder();
						bodyBuilder.AppendLine($"Timestamp: {entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff UTC}");
						bodyBuilder.AppendLine($"Level: {entry.Level}");
						bodyBuilder.AppendLine($"Source: {entry.Source}");
						bodyBuilder.AppendLine($"Message: {entry.Message}");

						if (!string.IsNullOrWhiteSpace(entry.ExceptionDetails))
						{
							bodyBuilder.AppendLine("\nException Details:");
							bodyBuilder.AppendLine(entry.ExceptionDetails);
						}
						if (entry.Data != null && entry.Data.Any())
						{
							bodyBuilder.AppendLine("\nAdditional Data:");
							foreach (var kvp in entry.Data)
							{
								bodyBuilder.AppendLine($"  {kvp.Key}: {kvp.Value}");
							}
						}
						mailMessage.Body = bodyBuilder.ToString();

						// Send the email asynchronously
						await client.SendMailAsync(mailMessage);
						internalLogCallback?.Invoke($"[EmailLogger] Sent email for log level '{entry.Level}' from source '{entry.Source}'.");
					}
				}
			}
			catch (SmtpException smtpEx)
			{
				internalLogCallback?.Invoke($"[EmailLogger] ERROR: SMTP Error when sending email for log entry (Level: {entry.Level}, Source: {entry.Source}). SMTP Status: {smtpEx.StatusCode}, Message: {smtpEx.Message}");
				internalLogCallback?.Invoke(smtpEx.ToString()); // Log full exception details
			}
			catch (Exception ex)
			{
				internalLogCallback?.Invoke($"[EmailLogger] ERROR: An unexpected error occurred when sending email for log entry (Level: {entry.Level}, Source: {entry.Source}). Message: {ex.Message}");
				internalLogCallback?.Invoke(ex.ToString()); // Log full exception details
			}
		}

		/// <summary>
		/// Sets the enabled state of the logger.
		/// </summary>
		/// <param name="enabled">True to enable, false to disable.</param>
		public void SetEnabled(bool enabled)
		{
			if (IsEnabled == enabled) return; // No change needed
			IsEnabled = enabled;
			internalLogCallback?.Invoke($"[EmailLogger] Email logging enabled set to: {enabled}.");
		}

		/// <summary>
		/// Sets the specific log levels that this logger is allowed to process.
		/// Setting this will replace any previously configured allowed levels.
		/// </summary>
		/// <param name="levels">A HashSet containing the LogLevels to allow. If null or empty, no levels will be allowed.</param>
		public void SetAllowedLevels(HashSet<LogLevel> levels)
		{
			allowedLevels = levels ?? new HashSet<LogLevel>(); // Ensure it's never null
			internalLogCallback?.Invoke($"[EmailLogger] Allowed levels set to: {string.Join(", ", allowedLevels.Select(l => l.ToString()))}.");
		}

		/// <summary>
		/// Disposes the EmailLogger. For EmailLogger, this typically means no unmanaged resources need explicit disposal.
		/// </summary>
		public void Dispose()
		{
			// EmailLogger does not hold unmanaged resources like file handles or network connections that require explicit disposal
			// beyond what SmtpClient and MailMessage handle with their 'using' statements.
			// This method is here to satisfy the IDisposable interface.
		}
	}
}