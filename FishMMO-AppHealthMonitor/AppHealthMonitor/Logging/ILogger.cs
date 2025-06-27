namespace AppHealthMonitor
{
	/// <summary>
	/// Defines the interface for a logging service.
	/// </summary>
	public interface ILogger
	{
		/// <summary>
		/// Logs a structured log entry asynchronously.
		/// </summary>
		/// <param name="entry">The log entry to record.</param>
		Task Log(LogEntry entry);
	}
}