using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.Text;
using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Implements ILogger to output log entries to the Unity Editor console.
	/// Supports Unity rich text coloring.
	/// </summary>
	public class UnityConsoleLogger : FishMMO.Logging.ILogger
	{
		private readonly UnityConsoleLoggerConfig config;
		private HashSet<LogLevel> allowedLevels;
		private Dictionary<LogLevel, string> logLevelColors; // Store configured colors

		public bool IsEnabled { get; private set; }
		public IReadOnlyCollection<LogLevel> AllowedLevels => allowedLevels;
		public bool HandlesConsoleParts { get { return false; } } // This logger does not handle "parts" directly for console output

		// Internal logging callback for messages specific to the UnityConsoleLogger's operation
		private readonly Action<string> internalLogCallback;

		/// <summary>
		/// Initializes a new instance of the UnityConsoleLogger.
		/// </summary>
		/// <param name="config">Configuration for the Unity console logger.</param>
		/// <param name="internalLogCallback">Optional: A callback action for internal messages from the logger itself. If null, defaults to System.Console.WriteLine.</param>
		public UnityConsoleLogger(UnityConsoleLoggerConfig config, Action<string> internalLogCallback = null)
		{
			this.config = config ?? throw new ArgumentNullException(nameof(config), "UnityConsoleLogger configuration cannot be null.");
			// Set the internal log callback, defaulting to Console.WriteLine if none provided
			// This is for messages *about* the logger, not the actual game logs.
			this.internalLogCallback = internalLogCallback ?? (msg => Console.WriteLine(msg));

			SetAllowedLevels(config.AllowedLevels);
			SetEnabled(config.Enabled); // This will now use internalLogCallback
			this.logLevelColors = config.LogLevelColors ?? new Dictionary<LogLevel, string>(); // Use provided colors or empty

			Debug.Log(ToString());
		}

		/// <summary>
		/// Logs a structured entry to the Unity console, formatted similarly to ConsoleFormatter.
		/// </summary>
		/// <param name="entry">The log entry to send.</param>
		public async Task Log(LogEntry entry)
		{
			// Check if logger is enabled and if the log level is allowed
			if (!IsEnabled || !AllowedLevels.Contains(entry.Level))
			{
				await Task.CompletedTask; // Ensure async signature is respected
				return;
			}

			// Get the color for the log level, default to white if not found
			string entryColor = "white";
			if (logLevelColors != null && logLevelColors.TryGetValue(entry.Level, out string defaultColor))
			{
				entryColor = defaultColor;
			}

			// Set the flag to indicate that we are internally logging to Unity's console.
			// This prevents UnityLoggerBridge from re-processing this log.
			UnityLoggerBridge.IsLoggingInternally = true;
			try
			{
				StringBuilder sb = new StringBuilder();

				// Log Level with padding
				// Pad the raw level string (excluding brackets), then add brackets and apply color.
				string levelContent = entry.Level.ToString().ToUpper();
				string paddedLevel = ConsoleFormatterHelpers.PadRight($"[{levelContent}]", ConsoleFormatterHelpers.LogLevelColumnWidth - 2);
				sb.Append($"<color={entryColor}>{paddedLevel}</color>");

				// Source with padding
				// Pad the raw source string (excluding brackets), then add brackets and apply color.
				string sourceContent = ConsoleFormatterHelpers.EscapeUnityRichText(entry.Source);
				string paddedSource = ConsoleFormatterHelpers.PadRight($"[{sourceContent}]", ConsoleFormatterHelpers.SourceColumnWidth - 2);
				sb.Append($"<color={entryColor}>{paddedSource}</color>");

				// Message - add a space before the message to match ConsoleFormatter's output
				sb.Append($" <color={entryColor}>{ConsoleFormatterHelpers.EscapeUnityRichText(entry.Message)}</color>");

				Debug.Log(sb.ToString()); // Log the main line

				// Indent and print exception details if available
				if (!string.IsNullOrWhiteSpace(entry.ExceptionDetails))
				{
					// Calculate the total width of the preceding columns for indentation
					string exceptionIndentation = new string(' ', ConsoleFormatterHelpers.TimestampColumnWidth + ConsoleFormatterHelpers.LogLevelColumnWidth + ConsoleFormatterHelpers.SourceColumnWidth);
					sb = new StringBuilder(); // Reset StringBuilder for new lines
					sb.AppendLine($"{exceptionIndentation}<color=red>Exception Details:</color>");
					sb.AppendLine($"{exceptionIndentation}<color=red>{ConsoleFormatterHelpers.EscapeUnityRichText(entry.ExceptionDetails)}</color>");
					Debug.Log(sb.ToString());
				}

				// Additional Data (if any) - indented on new lines
				if (entry.Data != null && entry.Data.Count > 0)
				{
					// Calculate the total width of the preceding columns for indentation
					string dataIndentation = new string(' ', ConsoleFormatterHelpers.TimestampColumnWidth + ConsoleFormatterHelpers.LogLevelColumnWidth + ConsoleFormatterHelpers.SourceColumnWidth);
					sb = new StringBuilder(); // Reset StringBuilder for new lines
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

			await Task.CompletedTask;
		}

		/// <summary>
		/// Sets the enabled state of the logger.
		/// </summary>
		/// <param name="enabled">True to enable, false to disable.</param>
		public void SetEnabled(bool enabled)
		{
			if (IsEnabled == enabled) return; // No change needed
			IsEnabled = enabled;
			// Use the internal log callback for this message
			internalLogCallback?.Invoke($"[UnityConsoleLogger] Unity Console logging enabled set to: {enabled}.");
		}

		/// <summary>
		/// Sets the specific log levels that this logger is allowed to process.
		/// Setting this will replace any previously configured allowed levels.
		/// </summary>
		/// <param name="levels">A HashSet containing the LogLevels to allow. If null or empty, no levels will be allowed.</param>
		public void SetAllowedLevels(HashSet<LogLevel> levels)
		{
			allowedLevels = levels ?? new HashSet<LogLevel>(); // Ensure it's never null
															   // Use the internal log callback for this message
			internalLogCallback?.Invoke($"[UnityConsoleLogger] Allowed levels set to: {string.Join(", ", allowedLevels.Select(l => l.ToString()))}.");
		}

		/// <summary>
		/// Disposes the UnityConsoleLogger. No unmanaged resources to dispose for a simple console logger.
		/// </summary>
		public void Dispose()
		{
			// No unmanaged resources to dispose for a simple console logger.
			// Use the internal log callback for this message.
			internalLogCallback?.Invoke($"[UnityConsoleLogger] Disposed.");
		}
	}
}