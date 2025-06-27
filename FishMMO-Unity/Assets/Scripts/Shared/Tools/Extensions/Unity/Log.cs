using UnityEngine;
using System;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Text;

namespace FishMMO.Shared
{
	/// <summary>
	/// Defines the verbosity level for logging.
	/// Higher values mean more detailed logs.
	/// </summary>
	public enum LogLevel
	{
		None = 0,    // No logs at all
		Error = 1,   // Only errors and exceptions
		Warning = 2, // Warnings, errors, exceptions
		Info = 3,    // General information, warnings, errors, exceptions
		Debug = 4,   // Debugging details, info, warnings, errors, exceptions
		Verbose = 5  // All possible logs, including very fine-grained details
	}

	/// <summary>
	/// A static logger for Unity applications, providing runtime log level control,
	/// environment-specific output (Editor Console, System Console), and custom file logging.
	/// It intercepts all Debug.Log calls and offers wrapper methods with optional color.
	/// Supports multi-colored output to System Console.
	/// </summary>
	public static class Log
	{
		// --- Internal State ---
		private static StreamWriter logFileWriter;
		private static List<string> inGameLogMessages = new List<string>();
		private const int MaxInGameLogMessages = 50; // Limit for in-game console messages

		// The current log level, accessible statically for runtime changes
		private static LogLevel currentLogLevel = LogLevel.Info; // Default initial log level

		// Configuration for disabling Unity's default logger in builds
		private static bool disableDefaultUnityLoggerInBuilds = true;

		// Configuration for enabling/disabling writing logs to a file
		private static bool enableFileLogging = false; // Default: File logging is OFF until explicitly enabled

		// Regex pattern for stripping rich text tags from strings for plain output.
		private static readonly Regex RichTextTagStrippingRegex = new Regex("<.*?>", RegexOptions.Compiled);

		// Regex pattern to detect single-color log messages with standard prefixes
		private static readonly Regex SingleColorLogPrefixRegex = new Regex(@"^<color=(#?[0-9a-fA-F]{6,8}|[a-zA-Z]+?)>\[(INFO|DEBUG|VERBOSE|WARN|ERROR|Log)\]\s(.*)</color>$", RegexOptions.Compiled);

		/// <summary>
		/// Gets or sets the current global log level.
		/// Messages with a higher verbosity than this level will be filtered out.
		/// </summary>
		public static LogLevel CurrentLogLevel
		{
			get => currentLogLevel;
			set
			{
				currentLogLevel = value;
				// This specific log bypasses filtering to always indicate a level change.
				UnityEngine.Debug.Log($"<color=#ADD8E6>[Log]</color> Log Level changed to: {currentLogLevel}"); // Light blue for log level changes
			}
		}

		/// <summary>
		/// Static constructor: Called once when the class is first accessed.
		/// This sets up log interception. File writer initialization is now explicit via SetFileLoggingEnabled.
		/// </summary>
		static Log()
		{
			// Subscribe to Unity's log message event for interception
			Application.logMessageReceived += HandleLog;
			// Ensure cleanup when the application quits (important for file writer if it's active)
			Application.quitting += CleanupLogger;

			// Apply default Unity logger disabling based on build type early, if configured.
			if (!Application.isEditor && disableDefaultUnityLoggerInBuilds)
			{
				UnityEngine.Debug.unityLogger.logEnabled = false;
			}
		}

		// --- Initialization & Cleanup ---
		/// <summary>
		/// Initializes the log file writer. This is called internally when file logging is enabled.
		/// </summary>
		private static void InitializeLogFileWriter()
		{
			if (logFileWriter != null) return; // Already initialized

			string logFilePath = "";

#if UNITY_EDITOR
			logFilePath = Path.Combine(Application.dataPath, "..", "Logs", "editor_custom_log.txt");
#elif UNITY_SERVER
            // For dedicated server builds, assume path relative to executable
            logFilePath = Path.Combine(Application.dataPath, "..", "Logs", "server_custom_log.txt");
#else
            // For standalone players, use persistentDataPath which is user-specific and writable
            logFilePath = Path.Combine(Application.persistentDataPath, "Logs", "player_custom_log.txt");
#endif

			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(logFilePath));
				logFileWriter = new StreamWriter(logFilePath, true) { AutoFlush = true }; // AutoFlush for immediate writes
																						  // Log that the log file is active. This message will go through HandleLog.
				Info($"Log file initialized: {logFilePath}", "#ADD8E6"); // Light blue for logger messages
			}
			catch (Exception e)
			{
				// If file logging fails, fall back to Unity's default (which might be disabled later for builds)
				Error($"Failed to initialize log file: {e.Message}", "red");
				logFileWriter = null; // Ensure null so we don't try to write to it later
			}
		}

		private static void CleanupLogger()
		{
			if (logFileWriter != null)
			{
				logFileWriter.Close();
				logFileWriter.Dispose();
				logFileWriter = null;
			}
		}

		// --- Log Indentation Helper ---
		/// <summary>
		/// Returns an indentation string based on the LogLevel.
		/// Adjust these values to change the visual indentation.
		/// </summary>
		private static string GetIndentation(LogLevel level)
		{
			switch (level)
			{
				case LogLevel.Error:
				case LogLevel.Warning:
					return ""; // Errors and Warnings might not need extra indentation
				case LogLevel.Info:
					return "  "; // Two spaces for Info
				case LogLevel.Debug:
					return "    "; // Four spaces for Debug
				case LogLevel.Verbose:
					return "      "; // Six spaces for Verbose
				default:
					return "";
			}
		}

		// --- Log Interception Handler ---
		private static void HandleLog(string logString, string stackTrace, LogType type)
		{
			LogLevel messageLevel = LogLevel.Info; // Default to Info
			string cleanedLogString = StripRichTextTags(logString); // Always strip for file/in-game display

			// Infer LogLevel from Unity's LogType
			switch (type)
			{
				case LogType.Error: case LogType.Exception: case LogType.Assert: messageLevel = LogLevel.Error; break;
				case LogType.Warning: messageLevel = LogLevel.Warning; break;
				case LogType.Log: default: messageLevel = LogLevel.Info; break;
			}

			// --- Step 1: Runtime Log Level Filtering ---
			if (messageLevel > currentLogLevel)
			{
				return; // This log message is too verbose for the current setting, so skip it.
			}

			// --- Step 2: Prepare Final Formatted Message with Indentation ---
			string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
			string indentation = GetIndentation(messageLevel); // Get indentation based on determined level
			string finalOutput = $"[{timestamp}] [{messageLevel.ToString().ToUpperInvariant()}] {indentation}{cleanedLogString}";

			// --- Step 3: Output to In-Game UI Console & Custom Log File ---
#if !UNITY_SERVER && !UNITY_EDITOR // Only add to in-game console for non-server, non-editor builds
            if (!Application.isBatchMode) // Also exclude explicit batchmode runs
            {
                inGameLogMessages.Add(finalOutput);
                if (inGameLogMessages.Count > MaxInGameLogMessages)
                {
                    inGameLogMessages.RemoveAt(0);
                }
            }
#endif

			if (enableFileLogging && logFileWriter != null)
			{
				try
				{
					logFileWriter.WriteLine(finalOutput);
					if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
					{
						logFileWriter.WriteLine(stackTrace);
					}
				}
				catch (Exception e)
				{
					// If file logging itself fails, use UnityEngine.Debug.LogError
					// as we can't rely on our own system here.
					UnityEngine.Debug.LogError($"[Log] Failed to write to log file: {e.Message}");
				}
			}
		}

		// --- Helper for Console Colors (Hex to ConsoleColor mapping) ---
		private static readonly Dictionary<ConsoleColor, (byte r, byte g, byte b)> ConsoleColorRgbMap = new Dictionary<ConsoleColor, (byte r, byte g, byte b)>
		{
			{ ConsoleColor.Black, (0, 0, 0) },
			{ ConsoleColor.DarkBlue, (0, 0, 128) },
			{ ConsoleColor.DarkGreen, (0, 128, 0) },
			{ ConsoleColor.DarkCyan, (0, 128, 128) },
			{ ConsoleColor.DarkRed, (128, 0, 0) },
			{ ConsoleColor.DarkMagenta, (128, 0, 128) },
			{ ConsoleColor.DarkYellow, (128, 128, 0) }, // Often brown/orange
            { ConsoleColor.Gray, (192, 192, 192) },
			{ ConsoleColor.DarkGray, (128, 128, 128) },
			{ ConsoleColor.Blue, (0, 0, 255) },
			{ ConsoleColor.Green, (0, 255, 0) },
			{ ConsoleColor.Cyan, (0, 255, 255) },
			{ ConsoleColor.Red, (255, 0, 0) },
			{ ConsoleColor.Magenta, (255, 0, 255) },
			{ ConsoleColor.Yellow, (255, 255, 0) },
			{ ConsoleColor.White, (255, 255, 255) }
		};

		/// <summary>
		/// Converts a hex color string or named color to the closest System.ConsoleColor.
		/// </summary>
		private static System.ConsoleColor GetClosestConsoleColor(string color)
		{
			// Try to parse hex color
			if (color.StartsWith("#") && color.Length >= 7)
			{
				if (byte.TryParse(color.Substring(1, 2), System.Globalization.NumberStyles.HexNumber, null, out byte r) &&
					byte.TryParse(color.Substring(3, 2), System.Globalization.NumberStyles.HexNumber, null, out byte g) &&
					byte.TryParse(color.Substring(5, 2), System.Globalization.NumberStyles.HexNumber, null, out byte b))
				{
					return FindClosestConsoleColor((r, g, b));
				}
			}

			// Fallback to named colors if hex parsing fails or if it's a named color
			switch (color.ToLower())
			{
				case "red": return System.ConsoleColor.Red;
				case "green": return System.ConsoleColor.Green;
				case "blue": return System.ConsoleColor.Blue;
				case "yellow": return System.ConsoleColor.Yellow;
				case "cyan": return System.ConsoleColor.Cyan;
				case "magenta": return System.ConsoleColor.Magenta;
				case "white": return System.ConsoleColor.White;
				case "black": return System.ConsoleColor.Black;
				case "gray": return System.ConsoleColor.Gray;
				case "grey": return System.ConsoleColor.Gray;
				case "darkred": return System.ConsoleColor.DarkRed;
				case "darkgreen": return System.ConsoleColor.DarkGreen;
				case "darkblue": return System.ConsoleColor.DarkBlue;
				case "darkyellow": return System.ConsoleColor.DarkYellow; // DarkYellow is a good approximation for brown/orange
				case "darkcyan": return System.ConsoleColor.DarkCyan;
				case "darkmagenta": return System.ConsoleColor.DarkMagenta;
				case "darkgray": return System.ConsoleColor.DarkGray;
				case "orange": return System.ConsoleColor.DarkYellow; // Map common web colors
				case "purple": return System.ConsoleColor.DarkMagenta;
				case "lightblue": return System.ConsoleColor.Cyan;
				case "#add8e6": return System.ConsoleColor.Cyan; // Specific light blue for logger
				case "#8b4513": return System.ConsoleColor.DarkYellow; // Specific brown
				default: return System.ConsoleColor.White; // Default for unknown colors
			}
		}

		/// <summary>
		/// Finds the closest System.ConsoleColor to a given RGB tuple using Euclidean distance.
		/// </summary>
		private static System.ConsoleColor FindClosestConsoleColor((byte r, byte g, byte b) targetRgb)
		{
			double minDistance = double.MaxValue;
			System.ConsoleColor closestColor = System.ConsoleColor.White;

			foreach (var entry in ConsoleColorRgbMap)
			{
				double dr = entry.Value.r - targetRgb.r;
				double dg = entry.Value.g - targetRgb.g;
				double db = entry.Value.b - targetRgb.b;
				double distance = (dr * dr) + (dg * dg) + (db * db); // Squared Euclidean distance

				if (distance < minDistance)
				{
					minDistance = distance;
					closestColor = entry.Key;
				}
			}
			return closestColor;
		}

		// --- Public Static Wrapper Methods for Logging ---

		/// <summary>
		/// Sets whether Unity's default logger should be disabled in non-editor builds.
		/// Call this early in your application's lifecycle if you want to override the default.
		/// </summary>
		/// <param name="disable">True to disable, false to keep enabled.</param>
		public static void SetDisableDefaultUnityLoggerInBuilds(bool disable)
		{
			disableDefaultUnityLoggerInBuilds = disable;
			if (!Application.isEditor)
			{
				UnityEngine.Debug.unityLogger.logEnabled = !disable;
			}
			UnityEngine.Debug.Log($"<color=#ADD8E6>[Log]</color> Disabling default Unity logger in builds set to: {disable}");
		}

		/// <summary>
		/// Sets whether log messages should be written to a file.
		/// Call this early in your application's lifecycle if you want to override the default.
		/// If enabling, the log file writer will be initialized. If disabling, it will be cleaned up.
		/// </summary>
		/// <param name="enable">True to enable file logging, false to disable.</param>
		public static void SetFileLoggingEnabled(bool enable)
		{
			if (enableFileLogging == enable) return;

			enableFileLogging = enable;
			UnityEngine.Debug.Log($"<color=#ADD8E6>[Log]</color> File logging enabled set to: {enable}");

			if (enable)
			{
				InitializeLogFileWriter();
			}
			else
			{
				CleanupLogger();
			}
		}

		/// <summary>
		/// Logs an informational message.
		/// </summary>
		/// <param name="message">The message to log.</param>
		/// <param name="color">Optional color for the Editor Console (e.g., "green", "#00FF00").</param>
		public static void Info(string message, string color = "green")
		{
			string indentation = GetIndentation(LogLevel.Info);
#if UNITY_SERVER || (!UNITY_EDITOR && (UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX))
            System.ConsoleColor consoleSystemColor = GetClosestConsoleColor(color);
            System.Console.ForegroundColor = consoleSystemColor;
            System.Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [INFO] {indentation}{StripRichTextTags(message)}");
            System.Console.ResetColor();
#endif
			UnityEngine.Debug.Log($"<color={color}>[INFO]</color> {message}");
		}

		/// <summary>
		/// Logs a debugging message.
		/// </summary>
		/// <param name="message">The message to log.</param>
		/// <param name="color">Optional color for the Editor Console (e.g., "cyan", "#00FFFF").</param>
		public static void Debug(string message, string color = "cyan")
		{
			string indentation = GetIndentation(LogLevel.Debug);
#if UNITY_SERVER || (!UNITY_EDITOR && (UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX))
            System.ConsoleColor consoleSystemColor = GetClosestConsoleColor(color);
            System.Console.ForegroundColor = consoleSystemColor;
            System.Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [DEBUG] {indentation}{StripRichTextTags(message)}");
            System.Console.ResetColor();
#endif
			UnityEngine.Debug.Log($"<color={color}>[DEBUG]</color> {message}");
		}

		/// <summary>
		/// Logs a verbose debugging message.
		/// </summary>
		/// <param name="message">The message to log.</param>
		/// <param name="color">Optional color for the Editor Console (e.g., "grey", "#808080").</param>
		public static void Verbose(string message, string color = "grey")
		{
			string indentation = GetIndentation(LogLevel.Verbose);
#if UNITY_SERVER || (!UNITY_EDITOR && (UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX))
            System.ConsoleColor consoleSystemColor = GetClosestConsoleColor(color);
            System.Console.ForegroundColor = consoleSystemColor;
            System.Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [VERBOSE] {indentation}{StripRichTextTags(message)}");
            System.Console.ResetColor();
#endif
			UnityEngine.Debug.Log($"<color={color}>[VERBOSE]</color> {message}");
		}

		/// <summary>
		/// Logs a warning message.
		/// </summary>
		/// <param name="message">The message to log.</param>
		/// <param name="color">Optional color for the Editor Console (e.g., "yellow", "#FFFF00").</param>
		public static void Warning(string message, string color = "yellow")
		{
			string indentation = GetIndentation(LogLevel.Warning);
#if UNITY_SERVER || (!UNITY_EDITOR && (UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX))
            System.ConsoleColor consoleSystemColor = GetClosestConsoleColor(color);
            System.Console.ForegroundColor = consoleSystemColor;
            System.Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [WARN] {indentation}{StripRichTextTags(message)}");
            System.Console.ResetColor();
#endif
			UnityEngine.Debug.LogWarning($"<color={color}>[WARN]</color> {message}");
		}

		/// <summary>
		/// Logs an error message.
		/// </summary>
		/// <param name="message">The message to log.</param>
		/// <param name="color">Optional color for the Editor Console (e.g., "red", "#FF0000").</param>
		public static void Error(string message, string color = "red")
		{
			string indentation = GetIndentation(LogLevel.Error);
#if UNITY_SERVER || (!UNITY_EDITOR && (UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX))
            System.ConsoleColor consoleSystemColor = GetClosestConsoleColor(color);
            System.Console.ForegroundColor = consoleSystemColor;
            System.Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [ERROR] {indentation}{StripRichTextTags(message)}");
            System.Console.ResetColor();
#endif
			UnityEngine.Debug.LogError($"<color={color}>[ERROR]</color> {message}");
		}

		/// <summary>
		/// Logs an exception.
		/// </summary>
		/// <param name="exception">The exception to log.</param>
		public static void Exception(Exception exception)
		{
			// Indentation for exceptions is handled implicitly by the log level.
			string indentation = GetIndentation(LogLevel.Error); // Treat exceptions as errors for indentation

#if UNITY_SERVER || (!UNITY_EDITOR && (UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX))
            System.Console.ForegroundColor = System.ConsoleColor.Red;
            System.Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [EXCEPTION] {indentation}{exception.Message}");
            System.Console.WriteLine($"{indentation}{exception.StackTrace}"); // Indent stack trace as well
            System.Console.ResetColor();
#endif
			UnityEngine.Debug.LogException(exception);
		}

		/// <summary>
		/// Logs a message composed of multiple colored parts.
		/// This provides multi-colored output to the System Console (CMD/Bash/Terminal) and rich-text in Editor Console.
		/// </summary>
		/// <param name="level">The log level for this message (used for filtering). The message will be
		/// sent to Unity's Debug.Log/Warning/Error based on this level.</param>
		/// <param name="columnWidth">Optional. The minimum width for each text segment. Text will be padded with spaces if shorter.
		/// Use 0 or negative for no padding (default behavior for previous versions).</param>
		/// <param name="parts">An array of tuples, where each tuple contains a color (hex or named) and the text for that part.</param>
		public static void WriteColored(LogLevel level, int columnWidth = 0, params (string color, string text)[] parts)
		{
			StringBuilder richTextBuilder = new StringBuilder();
			StringBuilder plainTextBuilder = new StringBuilder();
			string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
			string levelPrefix = $"[{level.ToString().ToUpperInvariant()}]";
			string baseIndentation = GetIndentation(level); // Base indentation for the whole line

			// Conditional System.Console output for dedicated servers/standalones
#if UNITY_SERVER || (!UNITY_EDITOR && (UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX))
            System.Console.Write($"[{timestamp}] {levelPrefix} {baseIndentation}"); // Add base indentation here
            foreach (var part in parts)
            {
                string textToPrint = part.text;
                // Apply padding if columnWidth is specified and text is shorter
                if (columnWidth > 0 && textToPrint.Length < columnWidth)
                {
                    textToPrint = textToPrint.PadRight(columnWidth);
                }

                System.ConsoleColor consoleSystemColor = GetClosestConsoleColor(part.color);
                System.Console.ForegroundColor = consoleSystemColor;
                System.Console.Write(textToPrint); // Print padded text to console
                
                // Build rich text string (no padding here, Unity handles rich text spacing differently)
                richTextBuilder.Append($"<color={part.color}>{part.text}</color>"); 
                plainTextBuilder.Append(textToPrint); // Use padded text for plain text builder
            }
            System.Console.ResetColor();
            System.Console.WriteLine(); // New line after all parts
#else
			// If not a console-enabled build, just build rich text for Debug.Log and plain text for HandleLog
			foreach (var part in parts)
			{
				string textToPrint = part.text;
				// Apply padding if columnWidth is specified and text is shorter
				if (columnWidth > 0 && textToPrint.Length < columnWidth)
				{
					textToPrint = textToPrint.PadRight(columnWidth);
				}
				richTextBuilder.Append($"<color={part.color}>{textToPrint}</color>"); // Use padded text for rich text
				plainTextBuilder.Append(textToPrint); // Use padded text for plain text builder
			}
#endif

			// Now, send a single Debug.Log call. This will be intercepted by HandleLog.
			// HandleLog will use the stripped version (`plainTextBuilder.ToString()`) for file/in-game.
			// In the Editor, `Debug.Log` will display the rich text.
			// Note: Unity's rich text doesn't always render exact spacing/padding consistently,
			// but the System.Console output will be precisely aligned.
			string debugLogMessage = richTextBuilder.ToString();
			switch (level)
			{
				case LogLevel.Error:
					UnityEngine.Debug.LogError(debugLogMessage);
					break;
				case LogLevel.Warning:
					UnityEngine.Debug.LogWarning(debugLogMessage);
					break;
				case LogLevel.Debug:
				case LogLevel.Verbose:
				case LogLevel.Info:
				default:
					UnityEngine.Debug.Log(debugLogMessage);
					break;
			}
		}


		// --- Methods for In-Game UI Console (if implemented) ---
		/// <summary>
		/// Returns a copy of the list of log messages for potential in-game UI display.
		/// </summary>
		public static List<string> GetInGameLogs()
		{
			return new List<string>(inGameLogMessages); // Return a copy
		}

		// --- Utility for Rich Text Stripping ---
		/// <summary>
		/// Strips Unity's rich text tags from a string.
		/// </summary>
		/// <param name="source">The string containing rich text tags.</param>
		/// <returns>The string with rich text tags removed.</returns>
		public static string StripRichTextTags(string source)
		{
			return RichTextTagStrippingRegex.Replace(source, String.Empty);
		}
	}
}