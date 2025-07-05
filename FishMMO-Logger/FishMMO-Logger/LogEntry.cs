using System;
using System.Collections.Generic;
using System.Text;

namespace FishMMO.Logging
{
	/// <summary>
	/// Represents a single log entry with structured information.
	/// </summary>
	public class LogEntry
	{
		public DateTime Timestamp { get; set; }
		public LogLevel Level { get; set; }
		public string Source { get; set; } // e.g., "Daemon", "HealthMonitor", "FileLogger"
		public string Message { get; set; }
		public string ExceptionDetails { get; set; } // For storing exception stack traces or messages
		public Dictionary<string, object> Data { get; set; } // Additional structured data

		public LogEntry(LogLevel level, string source, string message, Exception exception = null, Dictionary<string, object> data = null)
		{
			Timestamp = DateTime.UtcNow; // Use UTC for consistency
			Level = level;
			Source = source;
			Message = message;
			ExceptionDetails = exception?.ToString(); // Captures full exception details
			Data = data;
		}

		public override string ToString()
		{
			// A simple default string representation for console/file logging
			var sb = new StringBuilder();
			sb.Append($"[{Timestamp:yyyy-MM-dd HH:mm:ss.fff UTC}] [{Level.ToString().ToUpper()}] [{Source}] {Message}");

			if (!string.IsNullOrWhiteSpace(ExceptionDetails))
			{
				sb.AppendLine("\nException Details:");
				sb.AppendLine(ExceptionDetails);
			}
			if (Data != null && Data.Count > 0)
			{
				sb.AppendLine("\nAdditional Data:");
				foreach (var kvp in Data)
				{
					sb.AppendLine($"  {kvp.Key}: {kvp.Value}");
				}
			}
			return sb.ToString();
		}
	}
}