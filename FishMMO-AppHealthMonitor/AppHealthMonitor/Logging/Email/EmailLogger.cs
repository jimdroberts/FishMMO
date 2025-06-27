using System.Net;
using System.Net.Mail;

namespace AppHealthMonitor
{
	/// <summary>
	/// Implements ILogger to send critical/error log entries as emails to specified recipients.
	/// Email sending is asynchronous and non-blocking.
	/// </summary>
	public class EmailLogger : ILogger
	{
		private readonly EmailLoggerConfig config;
		private readonly LogLevel minimumLevel;
		public bool IsEnabled { get; private set; }

		/// <summary>
		/// Initializes a new instance of the EmailLogger.
		/// </summary>
		/// <param name="config">Configuration for the email logger, including SMTP settings and recipient addresses.</param>
		public EmailLogger(EmailLoggerConfig config)
		{
			config = config ?? throw new ArgumentNullException(nameof(config), "EmailLogger configuration cannot be null.");
			IsEnabled = false; // Assume disabled until successfully validated

			if (!config.Enabled)
			{
				Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [EmailLogger] Email logging is disabled by configuration.");
			}
			else
			{
				if (string.IsNullOrWhiteSpace(config.SmtpHost) || string.IsNullOrWhiteSpace(config.FromAddress) || config.ToAddresses == null || !config.ToAddresses.Any())
				{
					Console.Error.WriteLine($"[{DateTime.Now:HH:mm:ss}] [EmailLogger] WARNING: Email logger is enabled but missing required configuration (SMTP Host, From Address, or To Addresses). Email sending will likely fail.");
					// IsEnabled remains false
				}
				else
				{
					minimumLevel = config.MinimumLevel;
					IsEnabled = true; // Mark as enabled only if configuration is valid
					Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [EmailLogger] Initialized. Sending emails for logs >= {minimumLevel}. From: {config.FromAddress}, To: {string.Join(", ", config.ToAddresses)}");
				}
			}
		}

		/// <summary>
		/// Sends a log entry as an email if email logging is enabled and the log level meets the minimum.
		/// The email sending is performed asynchronously without blocking the caller.
		/// </summary>
		/// <param name="entry">The log entry to record.</param>
		public async Task Log(LogEntry entry)
		{
			if (!IsEnabled || entry == null || entry.Level < minimumLevel) // Check IsEnabled property
			{
				return; // Skip if disabled, null, or below minimum level
			}

			try
			{
				using (var client = new SmtpClient(config.SmtpHost, config.SmtpPort))
				{
					client.EnableSsl = true; // Most modern SMTP servers require SSL/TLS
					client.DeliveryMethod = SmtpDeliveryMethod.Network;
					client.UseDefaultCredentials = false; // Always explicitly set credentials if provided

					if (!string.IsNullOrWhiteSpace(config.SmtpUsername))
					{
						client.Credentials = new NetworkCredential(config.SmtpUsername, config.SmtpPassword);
					}
					else
					{
						Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [EmailLogger] WARNING: No SMTP username provided. Attempting anonymous login. This may fail depending on server configuration.");
					}

					using (var mailMessage = new MailMessage())
					{
						mailMessage.From = new MailAddress(config.FromAddress);
						foreach (var toAddress in config.ToAddresses)
						{
							mailMessage.To.Add(toAddress);
						}
						mailMessage.Subject = $"{config.SubjectPrefix} {entry.Level.ToString().ToUpper()}: {entry.Source}";

						// Build email body from log entry
						var bodyBuilder = new System.Text.StringBuilder();
						bodyBuilder.AppendLine($"Timestamp (UTC): {entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}");
						bodyBuilder.AppendLine($"Log Level: {entry.Level.ToString().ToUpper()}");
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

						await client.SendMailAsync(mailMessage);
						Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [EmailLogger] Sent email for log level '{entry.Level}' from source '{entry.Source}'.");
					}
				}
			}
			catch (SmtpException smtpEx)
			{
				Console.Error.WriteLine($"[{DateTime.Now:HH:mm:ss}] [EmailLogger] ERROR: SMTP Error when sending email for log entry (Level: {entry.Level}, Source: {entry.Source}). SMTP Status: {smtpEx.StatusCode}, Message: {smtpEx.Message}");
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine($"[{DateTime.Now:HH:mm:ss}] [EmailLogger] ERROR: An unexpected error occurred while sending email for log entry (Level: {entry.Level}, Source: {entry.Source}). Exception: {ex.Message}");
			}
		}
	}
}