using System.Collections.Generic;

namespace FishMMO.Logging
{
	/// <summary>
	/// Configuration for the EmailLogger.
	/// </summary>
	public class EmailLoggerConfig : ILoggerConfig // Implements ILoggerConfig
	{
		public string Type { get; set; } // Implement Type property for JSON discriminator
		public string LoggerType { get; set; }
		public bool Enabled { get; set; } = false;
		public string SmtpHost { get; set; }
		public int SmtpPort { get; set; } = 587; // Default SMTP port for TLS/STARTTLS
		public string SmtpUsername { get; set; }
		public string SmtpPassword { get; set; } // Store securely in production!
		public string FromAddress { get; set; }
		public List<string> ToAddresses { get; set; }
		/// <summary>
		/// The set of log levels that this email logger is allowed to process.
		/// Default: Only Error and Critical levels.
		/// </summary>
		public HashSet<LogLevel> AllowedLevels { get; set; } = new HashSet<LogLevel>
		{
			LogLevel.Error,
			LogLevel.Critical
		};
		public string SubjectPrefix { get; set; } = "[HealthMonitor Alert]";

		public EmailLoggerConfig()
		{
			Type = nameof(EmailLoggerConfig); // Set the discriminator type
			LoggerType = nameof(EmailLogger);
		}
	}
}