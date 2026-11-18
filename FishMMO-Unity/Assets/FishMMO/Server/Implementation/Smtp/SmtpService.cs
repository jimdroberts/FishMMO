using System;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using FishMMO.Server.Core;
using FishMMO.Server.Core.Smtp;
using FishMMO.Shared;
using FishMMO.Logging;

namespace FishMMO.Server.Implementation.Smtp
{
	/// <summary>
	/// SMTP email sender using System.Net.Mail. Reads SMTP settings from
	/// server configuration, with environment variable overrides for container/
	/// orchestration deployments (FISHMMO_SMTP_HOST, FISHMMO_SMTP_PORT, etc.).
	/// </summary>
	public class SmtpService : ISmtpService
	{
		/// <summary>SMTP server hostname.</summary>
		private readonly string host;
		/// <summary>SMTP server port.</summary>
		private readonly int port;
		/// <summary>SMTP authentication username.</summary>
		private readonly string username;
		/// <summary>SMTP authentication password.</summary>
		private readonly string password;
		/// <summary>Email From address.</summary>
		private readonly string fromAddress;
		/// <summary>Display name for the From address.</summary>
		private readonly string fromName;
		/// <summary>Whether to use SSL/TLS for the SMTP connection.</summary>
		private readonly bool useSsl;

		/// <summary>
		/// Initializes a new instance of the <see cref="SmtpService"/> class.
		/// Reads SMTP settings from server configuration with environment variable overrides.
		/// </summary>
		/// <param name="configuration">The server configuration instance.</param>
		public SmtpService(IServerConfiguration configuration)
		{
			// Environment variables take precedence over config files so operators
			// can inject SMTP credentials at runtime (Docker, k8s, systemd).
			host = EnvOrConfig("FISHMMO_SMTP_HOST", "Smtp:Host", "localhost", configuration);
			port = int.TryParse(Environment.GetEnvironmentVariable("FISHMMO_SMTP_PORT"), out var p) ? p
				: configuration.GetInt("Smtp:Port", 587);
			username = EnvOrConfig("FISHMMO_SMTP_USERNAME", "Smtp:Username", "", configuration);
			password = EnvOrConfig("FISHMMO_SMTP_PASSWORD", "Smtp:Password", "", configuration);
			fromAddress = EnvOrConfig("FISHMMO_SMTP_FROM_ADDRESS", "Smtp:FromAddress", Constants.Configuration.SmtpFromAddress, configuration);
			fromName = EnvOrConfig("FISHMMO_SMTP_FROM_NAME", "Smtp:FromName", Constants.Configuration.SmtpFromName, configuration);
			useSsl = EnvOrConfig("FISHMMO_SMTP_USE_SSL", "Smtp:UseSsl", "true", configuration)
				.Equals("true", StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>
		/// Resolves a configuration value from an environment variable (first priority) or config file.
		/// </summary>
		/// <param name="envKey">The environment variable name.</param>
		/// <param name="configKey">The config file key.</param>
		/// <param name="defaultValue">The default value if neither source provides a value.</param>
		/// <param name="config">The server configuration instance.</param>
		/// <returns>The resolved value.</returns>
		private static string EnvOrConfig(string envKey, string configKey, string defaultValue, IServerConfiguration config)
		{
			var env = Environment.GetEnvironmentVariable(envKey);
			return !string.IsNullOrEmpty(env) ? env : config.GetString(configKey, defaultValue);
		}

		/// <inheritdoc/>
		public async Task<bool> SendEmailAsync(string to, string subject, string body)
		{
			if (string.IsNullOrWhiteSpace(to))
			{
				await Log.Warning("SmtpService", "Cannot send email: recipient address is empty.");
				return false;
			}

			try
			{
				using var mailMessage = new MailMessage
				{
					From = new MailAddress(fromAddress, fromName),
					Subject = subject,
					Body = body,
					IsBodyHtml = true,
				};
				mailMessage.To.Add(to);

				using var smtpClient = new SmtpClient(host, port)
				{
					EnableSsl = useSsl,
					DeliveryMethod = SmtpDeliveryMethod.Network,
					Timeout = 30_000, // 30 seconds
				};

				if (!string.IsNullOrEmpty(username))
				{
					smtpClient.Credentials = new NetworkCredential(username, password);
				}

				await smtpClient.SendMailAsync(mailMessage);
				await Log.Debug("SmtpService", $"Email sent to {to}: {subject}");
				return true;
			}
			catch (SmtpFailedRecipientException ex)
			{
				await Log.Warning("SmtpService", $"SMTP recipient rejected for {to}: {ex.Message}");
				return false;
			}
			catch (SmtpException ex)
			{
				await Log.Error("SmtpService", $"SMTP error sending to {to}: {ex.Message}");
				return false;
			}
			catch (Exception ex)
			{
				await Log.Error("SmtpService", $"Unexpected error sending email to {to}: {ex.Message}");
				return false;
			}
		}
	}
}