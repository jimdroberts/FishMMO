namespace AppHealthMonitor
{
	/// <summary>
	/// Configuration for the EmailLogger.
	/// </summary>
	public class EmailLoggerConfig
	{
		public bool Enabled { get; set; } = false;
		public string SmtpHost { get; set; }
		public int SmtpPort { get; set; } = 587; // Default SMTP port for TLS/STARTTLS
		public string SmtpUsername { get; set; }
		public string SmtpPassword { get; set; } // Store securely in production!
		public string FromAddress { get; set; }
		public List<string> ToAddresses { get; set; } // Renamed from RecipientAddresses for clarity
		public LogLevel MinimumLevel { get; set; } = LogLevel.Error; // Default minimum level for emails
		public string SubjectPrefix { get; set; } = "[HealthMonitor Alert]";
	}
}