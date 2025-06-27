namespace AppHealthMonitor
{
	/// <summary>
	/// Manages multiple ILogger implementations, dispatching log entries to all configured loggers.
	/// This acts as the central logging hub for the application.
	/// </summary>
	public class LoggingManager : IDisposable
	{
		private readonly List<ILogger> loggers = new List<ILogger>();
		private readonly LogLevel consoleMinimumLevel;
		private readonly object consoleLock = new object(); // To prevent garbled console output from concurrent writes

		// Define fixed widths for console columns
		private const int TimestampColumnWidth = 12; // [HH:mm:ss]
		private const int LogLevelColumnWidth = 12;  // [LEVEL] - e.g., "CRITICAL "W
		private const int SourceColumnWidth = 20;    // [Source] - e.g., "HealthMonitor-LoginServer  "

		/// <summary>
		/// Initializes a new instance of the LoggingManager.
		/// </summary>
		/// <param name="fileLogger">The FileLogger instance (can be null if not enabled).</param>
		/// <param name="emailLogger">The EmailLogger instance (can be null if not enabled).</param>
		/// <param name="consoleMinimumLevel">The minimum log level for console output. Messages below this level will not be printed to console.</param>
		public LoggingManager(FileLogger fileLogger, EmailLogger emailLogger, LogLevel consoleMinimumLevel = LogLevel.Info)
		{
			if (fileLogger != null)
			{
				loggers.Add(fileLogger);
			}
			if (emailLogger != null && emailLogger.IsEnabled)
			{
				loggers.Add(emailLogger);
			}

			consoleMinimumLevel = consoleMinimumLevel;
			LogInternal(LogLevel.Info, "LoggingManager", $"Initialized with {loggers.Count} active loggers. Console output from level {consoleMinimumLevel} and above.", null, null, true);
		}

		/// <summary>
		/// Logs a structured entry across all managed loggers and to the console.
		/// This is the primary method for logging in the application.
		/// </summary>
		/// <param name="level">The severity level of the log entry.</param>
		/// <param name="source">The source of the log entry (e.g., "Daemon", "HealthMonitor-App1").</param>
		/// <param name="message">The main log message.</param>
		/// <param name="exception">Optional: An exception associated with the log entry.</param>
		/// <param name="data">Optional: Additional structured data for the log entry. This data will be colorized per key-value pair.</param>
		public async Task Log(LogLevel level, string source, string message, Exception exception = null, Dictionary<string, object> data = null)
		{
			var entry = new LogEntry(level, source, message, exception, data);

			// Always log to Console based on its minimum level setting, using the dedicated internal method
			LogInternal(level, source, message, exception, data, false);

			// Dispatch to all other loggers asynchronously.
			// Each logger is responsible for checking its own internal 'Enabled' status and 'MinimumLevel'.
			var loggingTasks = loggers.Select(logger => logger.Log(entry)).ToList();
			await Task.WhenAll(loggingTasks);
		}

		/// <summary>
		/// Internal method to handle console logging directly with enhanced colorization and column alignment.
		/// </summary>
		/// <param name="level">The severity level.</param>
		/// <param name="source">The log source.</param>
		/// <param name="message">The log message.</param>
		/// <param name="exception">Optional exception.</param>
		/// <param name="data">Optional data dictionary for key-value pair colorization.</param>
		/// <param name="forceConsole">If true, logs to console regardless of consoleMinimumLevel (used for LoggingManager's own init messages).</param>
		private void LogInternal(LogLevel level, string source, string message, Exception exception, Dictionary<string, object> data, bool forceConsole = false)
		{
			if (forceConsole || level >= consoleMinimumLevel)
			{
				lock (consoleLock)
				{
					// Get base color for the log message body
					ConsoleColor baseColor = GetConsoleColorForLevel(level);

					// --- Line 1: Timestamp, Log Level, Source, and Main Message ---
					// Timestamp
					Console.ForegroundColor = ConsoleColor.DarkGray;
					Console.Write($"[{DateTime.Now:HH:mm:ss}]".PadRight(TimestampColumnWidth));

					// LogLevel
					Console.ForegroundColor = GetLogLevelColor(level);
					Console.Write($"[{level.ToString().ToUpper()}]".PadRight(LogLevelColumnWidth));

					// Source
					Console.ForegroundColor = GetSourceColor(source, level);
					// Ensure source doesn't exceed its column width, truncate if necessary
					string paddedSource = $"[{source}]";
					if (paddedSource.Length > SourceColumnWidth)
					{
						paddedSource = paddedSource.Substring(0, SourceColumnWidth - 3) + "..."; // Truncate and add ellipsis
					}
					Console.Write(paddedSource.PadRight(SourceColumnWidth));

					// Main Message
					Console.ForegroundColor = baseColor;
					Console.WriteLine(message);

					// --- Subsequent Lines: Exception Details and Additional Data ---
					// Calculate indentation for subsequent lines to align with the main message
					int indentation = TimestampColumnWidth + LogLevelColumnWidth + SourceColumnWidth;
					string indentString = new string(' ', indentation);

					// Handle Exception details with consistent coloring
					if (exception != null)
					{
						Console.ForegroundColor = baseColor; // Use base color for general exception text
						Console.WriteLine($"{indentString}  Exception:");
						Console.ForegroundColor = ConsoleColor.DarkYellow; // Color for exception details

						// For Error and Critical, provide full exception details including stack trace
						if (level == LogLevel.Error || level == LogLevel.Critical)
						{
							// Output full exception string, indenting each line of the stack trace
							string[] exceptionLines = exception.ToString().Split(new[] { Environment.NewLine }, StringSplitOptions.None);
							foreach (var line in exceptionLines)
							{
								Console.WriteLine($"{indentString}    {line}");
							}
						}
						else
						{
							Console.WriteLine($"{indentString}    {exception.Message}"); // Only message for other levels
						}
					}

					// Handle Additional Data (per-property colorization)
					if (data != null && data.Any())
					{
						foreach (var kvp in data)
						{
							Console.Write($"{indentString}    "); // Indentation for key-value pair
							Console.ForegroundColor = ConsoleColor.DarkCyan; // Color for the key
							Console.Write($"{kvp.Key}: ");
							Console.ForegroundColor = ConsoleColor.White; // Color for the value
							Console.WriteLine($"{kvp.Value}");
						}
					}

					Console.ResetColor(); // Always reset color at the end of the full log entry
				}
			}
		}

		/// <summary>
		/// Gets the appropriate ConsoleColor for the main message body based on LogLevel.
		/// </summary>
		private ConsoleColor GetConsoleColorForLevel(LogLevel level)
		{
			return level switch
			{
				LogLevel.Debug => ConsoleColor.Gray,
				LogLevel.Info => ConsoleColor.White,
				LogLevel.Warning => ConsoleColor.Yellow,
				LogLevel.Error => ConsoleColor.Red,
				LogLevel.Critical => ConsoleColor.Magenta,
				_ => ConsoleColor.White,
			};
		}

		/// <summary>
		/// Gets a unique color for the LogLevel part itself.
		/// </summary>
		private ConsoleColor GetLogLevelColor(LogLevel level)
		{
			return level switch
			{
				LogLevel.Debug => ConsoleColor.DarkGray,
				LogLevel.Info => ConsoleColor.Green,
				LogLevel.Warning => ConsoleColor.DarkYellow,
				LogLevel.Error => ConsoleColor.DarkRed,
				LogLevel.Critical => ConsoleColor.Red,
				_ => ConsoleColor.White,
			};
		}

		/// <summary>
		/// Gets a specific color for the log source (e.g., Daemon, Orchestration, App Name).
		/// </summary>
		private ConsoleColor GetSourceColor(string source, LogLevel level)
		{
			if (source.StartsWith("Daemon"))
			{
				return ConsoleColor.DarkGreen;
			}
			else if (source.StartsWith("Orchestration"))
			{
				return ConsoleColor.Cyan;
			}
			else if (source.StartsWith("HealthMonitor-"))
			{
				return ConsoleColor.Blue;
			}
			if (level == LogLevel.Warning)
			{
				return ConsoleColor.Yellow;
			}
			if (level >= LogLevel.Error)
			{
				return ConsoleColor.Red;
			}
			return ConsoleColor.DarkCyan; // Default for other sources
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		protected virtual void Dispose(bool disposing)
		{
			if (disposing)
			{
				foreach (var logger in loggers)
				{
					if (logger is IDisposable disposableLogger)
					{
						disposableLogger.Dispose();
					}
				}
				loggers.Clear();
				Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [LoggingManager] Disposed all managed loggers. (Console output directly)");
			}
		}
	}
}