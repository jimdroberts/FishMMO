namespace AppHealthMonitor
{
	/// <summary>
	/// Configuration for the LoggingManager itself.
	/// </summary>
	public class LoggingManagerConfig
	{
		public LogLevel ConsoleMinimumLevel { get; set; } = LogLevel.Info; // Minimum level to display logs on the console
	}
}