using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Implements ILogger to output log entries to the Unity Editor console.
	/// Also implements IConsoleFormatter to support direct colored output via Log.WritePartsToConsole.
	/// Supports Unity rich text coloring.
	/// </summary>
	public class UnityConsoleFormatter : IConsoleFormatter
	{
		/// <summary>
		/// Configured colors for each log level.
		/// </summary>
		private Dictionary<LogLevel, string> logLevelColors;
		/// <summary>
		/// Controls whether timestamps are included in formatted output.
		/// </summary>
		private bool _includeTimestamps;

		// Column widths and indentation are defined in ConsoleFormatterHelpers

		/// <summary>
		/// Initializes a new instance of the UnityConsoleFormatter with specified log level colors.
		/// </summary>
		/// <param name="logLevelColors">A dictionary mapping LogLevels to Unity rich text color strings.</param>
		/// <param name="includeTimestamps">If true, timestamps will be included in the formatted output.</param>
		public UnityConsoleFormatter(Dictionary<LogLevel, string> logLevelColors, bool includeTimestamps)
		{
			// Ensure the dictionary is always instantiated, even if null is passed.
			this.logLevelColors = logLevelColors ?? new Dictionary<LogLevel, string>();
			this._includeTimestamps = includeTimestamps;
		}

		/// <summary>
		/// Formats and writes a structured log entry to the Unity console, mimicking the columnar layout of ConsoleFormatter.
		/// </summary>
		/// <param name="entry">The log entry to format and write.</param>
		public void WriteStructuredLog(LogEntry entry)
		{
			try
			{
				UnityLoggerBridge.IsLoggingInternally = true;

				StringBuilder sb = new StringBuilder();
				string entryColor = "white";
				if (logLevelColors != null && logLevelColors.TryGetValue(entry.Level, out string defaultColor))
				{
					entryColor = defaultColor;
				}

				// Conditionally add timestamp with padding
				if (_includeTimestamps)
				{
					string timestampContent = entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
					string paddedTimestamp = ConsoleFormatterHelpers.PadRight($"[{timestampContent}]", ConsoleFormatterHelpers.TimestampColumnWidth - 2);
					sb.Append($"<color=grey>{paddedTimestamp}</color>");
				}
				else
				{
					sb.Append(new string(' ', ConsoleFormatterHelpers.TimestampColumnWidth));
				}

				// Log Level with padding
				string levelContent = entry.Level.ToString().ToUpper();
				string paddedLevel = ConsoleFormatterHelpers.PadRight($"[{levelContent}]", ConsoleFormatterHelpers.LogLevelColumnWidth - 2);
				sb.Append($"<color={entryColor}>{paddedLevel}</color>");

				// Source with padding
				string sourceContent = ConsoleFormatterHelpers.EscapeUnityRichText(entry.Source);
				string paddedSource = ConsoleFormatterHelpers.PadRight($"[{sourceContent}]", ConsoleFormatterHelpers.SourceColumnWidth - 2);
				sb.Append($"<color={entryColor}>{paddedSource}</color>");

				// Message
				sb.Append($" <color={entryColor}>{ConsoleFormatterHelpers.EscapeUnityRichText(entry.Message)}</color>");

				Debug.Log(sb.ToString());

				// Indent and print exception details if available
				if (!string.IsNullOrWhiteSpace(entry.ExceptionDetails))
				{
					string exceptionIndentation = new string(' ', ConsoleFormatterHelpers.TimestampColumnWidth + ConsoleFormatterHelpers.LogLevelColumnWidth + ConsoleFormatterHelpers.SourceColumnWidth);
					sb = new StringBuilder();
					sb.AppendLine($"{exceptionIndentation}<color=red>Exception Details:</color>");
					sb.AppendLine($"{exceptionIndentation}<color=red>{ConsoleFormatterHelpers.EscapeUnityRichText(entry.ExceptionDetails)}</color>");
					Debug.Log(sb.ToString());
				}

				// Additional Data (if any) - indented on new lines
				if (entry.Data != null && entry.Data.Count > 0)
				{
					string dataIndentation = new string(' ', ConsoleFormatterHelpers.TimestampColumnWidth + ConsoleFormatterHelpers.LogLevelColumnWidth + ConsoleFormatterHelpers.SourceColumnWidth);
					sb = new StringBuilder();
					sb.AppendLine($"{dataIndentation}<color=cyan>--- Additional Data ---</color>");
					foreach (var kvp in entry.Data)
					{
						sb.AppendLine($"{dataIndentation}<color=cyan>  {ConsoleFormatterHelpers.EscapeUnityRichText(kvp.Key)}: {ConsoleFormatterHelpers.EscapeUnityRichText(kvp.Value?.ToString())}</color>");
					}
					sb.AppendLine($"{dataIndentation}<color=cyan>-----------------------</color>");
					Debug.Log(sb.ToString());
				}
			}
			finally
			{
				UnityLoggerBridge.IsLoggingInternally = false;
			}
		}

		/// <summary>
		/// Writes a message to the Unity console composed of multiple colored parts.
		/// Implements IConsoleFormatter.WriteColoredParts.
		/// </summary>
		/// <param name="level">The log level for this message (used for a prefix, not for direct coloring of parts).</param>
		/// <param name="source">The source of the log message.</param>
		/// <param name="columnWidth">Optional. The minimum width for each text segment. Text will be padded if shorter. Use 0 or negative for no padding.</param>
		/// <param name="parts">An array of tuples, where each tuple contains a color (hex or named) and the text for that part.</param>
		public void WriteColoredParts(LogLevel level, string source, int columnWidth = 0, params (string color, string text)[] parts)
		{
			try
			{
				UnityLoggerBridge.IsLoggingInternally = true;

				StringBuilder sb = new StringBuilder();

				// Conditionally add timestamp with padding and color.
				if (_includeTimestamps)
				{
					string timestampContent = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
					string paddedTimestamp = ConsoleFormatterHelpers.PadRight($"[{timestampContent}]", ConsoleFormatterHelpers.TimestampColumnWidth - 2);
					sb.Append($"<color=grey>{paddedTimestamp}</color>");
				}
				else
				{
					sb.Append(new string(' ', ConsoleFormatterHelpers.TimestampColumnWidth));
				}

				// Add level prefix with padding and color.
				string levelPrefixColor = "white";
				if (logLevelColors != null && logLevelColors.TryGetValue(level, out string defaultColor))
				{
					levelPrefixColor = defaultColor;
				}
				string levelContent = level.ToString().ToUpper();
				string paddedLevel = ConsoleFormatterHelpers.PadRight($"[{levelContent}]", ConsoleFormatterHelpers.LogLevelColumnWidth - 2);
				sb.Append($"<color={levelPrefixColor}>{paddedLevel}</color>");

				// Add source prefix with padding and color.
				string sourceColor = "white";
				if (logLevelColors != null && logLevelColors.TryGetValue(level, out string defaultSourceColor))
				{
					sourceColor = defaultSourceColor;
				}
				string sourceContent = ConsoleFormatterHelpers.EscapeUnityRichText(source);
				string paddedSource = ConsoleFormatterHelpers.PadRight($"[{sourceContent}]", ConsoleFormatterHelpers.SourceColumnWidth - 2);
				sb.Append($"<color={sourceColor}>{paddedSource}</color>");

				// Process each part: pad the raw text, then apply color tags.
				foreach (var part in parts)
				{
					string colorTag = string.IsNullOrWhiteSpace(part.color) ? "" : $"<color={part.color}>";
					string endColorTag = string.IsNullOrWhiteSpace(part.color) ? "" : "</color>";

					string escapedText = ConsoleFormatterHelpers.EscapeUnityRichText(part.text);
					string textToWrite = columnWidth > 0 ? ConsoleFormatterHelpers.PadRight(escapedText, columnWidth) : escapedText;
					sb.Append($"{colorTag}{textToWrite}{endColorTag}");
				}

				// Use Debug.Log for the combined message, routing through appropriate Unity log type
				switch (level)
				{
					case LogLevel.Critical:
					case LogLevel.Error:
						Debug.LogError(sb.ToString());
						break;
					case LogLevel.Warning:
						Debug.LogWarning(sb.ToString());
						break;
					case LogLevel.Info:
					case LogLevel.Debug:
					case LogLevel.Verbose:
					default:
						Debug.Log(sb.ToString());
						break;
				}
			}
			finally
			{
				UnityLoggerBridge.IsLoggingInternally = false;
			}
		}
	}
}