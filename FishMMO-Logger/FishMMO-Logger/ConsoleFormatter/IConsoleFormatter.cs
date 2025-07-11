namespace FishMMO.Logging
{
	/// <summary>
	/// Defines the interface for formatting and writing log entries to the console.
	/// </summary>
	public interface IConsoleFormatter
	{
		/// <summary>
		/// Writes a structured log entry to the console.
		/// </summary>
		/// <param name="entry">The log entry to format and write.</param>
		void WriteStructuredLog(LogEntry entry);

		/// <summary>
		/// Writes a message to the console composed of multiple colored parts.
		/// </summary>
		/// <param name="level">The log level for this message (used for indentation/context).</param>
		/// <param name="columnWidth">Optional. The minimum width for each text segment. Text will be padded if shorter.
		/// Use 0 or negative for no padding.</param>
		/// <param name="parts">An array of tuples, where each tuple contains a color (hex or named) and the text for that part.</param>
		void WriteColoredParts(LogLevel level, string source, int columnWidth, params (string color, string text)[] parts);
	}
}