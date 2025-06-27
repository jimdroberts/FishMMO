namespace AppHealthMonitor
{
	/// <summary>
	/// Defines the severity level of a log entry.
	/// </summary>
	public enum LogLevel
	{
		Debug,    // Detailed information useful for debugging.
		Info,     // General operational information.
		Warning,  // Potentially harmful situations or deviations from expected behavior.
		Error,    // Errors that prevent normal operation but might be recoverable.
		Critical  // Critical errors that cause application failure or unrecoverable state.
	}
}