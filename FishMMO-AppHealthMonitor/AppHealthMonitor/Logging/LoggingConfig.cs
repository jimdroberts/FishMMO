namespace AppHealthMonitor
{
	/// <summary>
	/// Overall logging configuration, bundling individual logger configs.
	/// This is where you configure which loggers are active and their specific settings.
	/// </summary>
	public class LoggingConfig
	{
		public LoggingManagerConfig LoggingManager { get; set; } // Configuration specific to the LoggingManager (e.g., console output level)
		public FileLoggerConfig FileLogger { get; set; }
		public EmailLoggerConfig EmailLogger { get; set; }
	}
}