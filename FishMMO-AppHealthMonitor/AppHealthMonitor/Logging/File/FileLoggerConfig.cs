namespace AppHealthMonitor
{
    /// <summary>
    /// Configuration for the FileLogger.
    /// </summary>
    public class FileLoggerConfig
    {
		public bool Enabled { get; set; } = true;
        public string LogFilePath { get; set; } = "app_health_monitor.log"; // Default log file path
        public LogLevel MinimumLevel { get; set; } = LogLevel.Info; // Default minimum log level
    }
}