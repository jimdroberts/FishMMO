namespace FishMMO.Logging
{
	/// <summary>
	/// Defines the severity level of a log entry.
	/// </summary>
	public enum LogLevel
	{
		None = 0,    // No logs at all
		Critical = 1,  // Critical errors that cause application failure or unrecoverable state.
		Error = 2,   // Errors that prevent normal operation but might be recoverable.
		Warning = 3, // Potentially harmful situations or deviations from expected behavior.
		Info = 4,    // General operational information.
		Debug = 5,   // Detailed information useful for debugging.
		Verbose = 6  // All possible logs, including very fine-grained details
	}
}