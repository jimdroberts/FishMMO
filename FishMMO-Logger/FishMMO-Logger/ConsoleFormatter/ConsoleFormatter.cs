using System;
using System.Linq;

namespace FishMMO.Logging
{
	/// <summary>
	/// Implements IConsoleFormatter to handle the specific formatting and coloring
	/// of log entries for System.Console output.
	/// </summary>
	public class ConsoleFormatter : IConsoleFormatter
	{
		private readonly object consoleLock = new object(); // To prevent garbled console output from concurrent writes

		/// <summary>
		/// Writes a structured log entry to the console.
		/// This method performs synchronous console output and handles basic colorization.
		/// </summary>
		/// <param name="entry">The log entry to format and write.</param>
		public void WriteStructuredLog(LogEntry entry)
		{
			// Format timestamp, level, and source content, padding them internally
			var timestampContent = entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
			var formattedTimestamp = ConsoleFormatterHelpers.PadRight($"[{timestampContent}]", ConsoleFormatterHelpers.TimestampColumnWidth);

			var levelString = entry.Level.ToString().ToUpper();
			var formattedLevel = ConsoleFormatterHelpers.PadRight($"[{levelString}]", ConsoleFormatterHelpers.LogLevelColumnWidth);

			var formattedSource = ConsoleFormatterHelpers.PadRight($"[{entry.Source}]", ConsoleFormatterHelpers.SourceColumnWidth);

			lock (consoleLock)
			{
				// Write Timestamp part
				Console.ForegroundColor = ConsoleColor.DarkGray;
				Console.Write(formattedTimestamp);

				// Write Log Level part
				Console.ForegroundColor = ConsoleFormatterHelpers.GetSourceColor(entry.Source, entry.Level);
				Console.Write(formattedLevel);

				// Write Source part
				Console.ForegroundColor = ConsoleFormatterHelpers.GetSourceColor(entry.Source, entry.Level);
				Console.Write(formattedSource);

				// Reset color and write message (with a leading space to separate from indentation)
				Console.ResetColor();
				Console.WriteLine($" {entry.Message}");

				// Indent and print exception details if available
				if (!string.IsNullOrWhiteSpace(entry.ExceptionDetails))
				{
					// Calculate the total width of the preceding columns for indentation
					var totalColumnWidth = ConsoleFormatterHelpers.TimestampColumnWidth + ConsoleFormatterHelpers.LogLevelColumnWidth + ConsoleFormatterHelpers.SourceColumnWidth;
					var exceptionIndent = new string(' ', totalColumnWidth); // Indent by columns
					Console.ForegroundColor = ConsoleFormatterHelpers.GetSourceColor(entry.Source, LogLevel.Error); ; // Exception details usually red
					Console.WriteLine($"{exceptionIndent}Exception: {entry.ExceptionDetails}"); // ExceptionDetails now contains full ToString()
					Console.ResetColor();
				}

				// Indent and print additional data if available
				if (entry.Data != null && entry.Data.Any())
				{
					var totalColumnWidth = ConsoleFormatterHelpers.TimestampColumnWidth + ConsoleFormatterHelpers.LogLevelColumnWidth + ConsoleFormatterHelpers.SourceColumnWidth;
					var dataIndent = new string(' ', totalColumnWidth); // Indent by columns
					Console.ForegroundColor = ConsoleColor.Cyan; // A different color for data
					Console.WriteLine($"{dataIndent}--- Additional Data ---");
					foreach (var kvp in entry.Data)
					{
						Console.WriteLine($"{dataIndent}{kvp.Key}: {kvp.Value}");
					}
					Console.WriteLine($"{dataIndent}-----------------------");
					Console.ResetColor();
				}
			}
		}

		/// <summary>
		/// Writes a message to the console composed of multiple colored parts.
		/// </summary>
		/// <param name="level">The log level for this message (used for indentation/context).</param>
		/// <param name="source">The source of the log message.</param>
		/// <param name="columnWidth">Optional. The minimum width for each text segment. Text will be padded if shorter.
		/// Use 0 or negative for no padding.</param>
		/// <param name="parts">An array of tuples, where each tuple contains a color (hex or named) and the text for that part.</param>
		public void WriteColoredParts(LogLevel level, string source, int columnWidth = 0, params (string color, string text)[] parts)
		{
			// The ConsoleFormatter.WriteColoredParts explicitly adds timestamp, level, and source.
			lock (consoleLock) // Ensure thread-safe console output
			{
				// Format timestamp, level, and source content, padding them internally
				var timestampContent = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
				var formattedTimestamp = ConsoleFormatterHelpers.PadRight($"[{timestampContent}]", ConsoleFormatterHelpers.TimestampColumnWidth);

				var levelString = level.ToString().ToUpperInvariant();
				var formattedLevel = ConsoleFormatterHelpers.PadRight($"[{levelString}]", ConsoleFormatterHelpers.LogLevelColumnWidth);

				var formattedSource = ConsoleFormatterHelpers.PadRight($"[{source}]", ConsoleFormatterHelpers.SourceColumnWidth);

				// Write Timestamp part
				Console.ForegroundColor = ConsoleColor.DarkGray;
				Console.Write(formattedTimestamp);

				// Write Log Level part
				Console.ForegroundColor = ConsoleFormatterHelpers.GetSourceColor(source, level);
				Console.Write(formattedLevel);

				// Write Source part
				Console.ForegroundColor = ConsoleFormatterHelpers.GetSourceColor(source, level);
				Console.Write(formattedSource);

				// Reset color after writing the initial parts
				Console.ResetColor();

				foreach (var part in parts)
				{
					string textToPrint = part.text;
					// Apply padding if columnWidth is specified and text is shorter
					if (columnWidth > 0 && textToPrint.Length < columnWidth)
					{
						textToPrint = ConsoleFormatterHelpers.PadRight(textToPrint, columnWidth);
					}

					ConsoleColor consoleSystemColor = ConsoleFormatterHelpers.GetClosestConsoleColor(part.color);
					Console.ForegroundColor = consoleSystemColor;
					Console.Write(textToPrint);
				}
				Console.ResetColor();
				Console.WriteLine(); // New line after all parts
			}
		}
	}
}