using System.Collections.Generic;
using System.Text.Json.Serialization;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Configuration for the Unity Console Logger.
	/// Messages routed through FishMMO.Logging.Log will also appear in the Unity console
	/// if this logger is enabled and the log level is allowed.
	/// </summary>
	public class UnityConsoleLoggerConfig : ILoggerConfig
	{
		public string Type { get; set; }
		public string LoggerType { get; set; }

		/// <summary>
		/// Gets or sets a value indicating whether this logger is enabled.
		/// If false, no messages will be sent to the Unity console via this logger.
		/// </summary>
		public bool Enabled { get; set; } = true;

		/// <summary>
		/// Gets or sets the collection of LogLevels that this logger is allowed to process.
		/// Only messages with levels present in this collection will be forwarded to the Unity console.
		/// </summary>
		public HashSet<LogLevel> AllowedLevels { get; set; } = new HashSet<LogLevel>
		{
			LogLevel.Critical,
			LogLevel.Error,
			LogLevel.Warning,
			LogLevel.Info,
			LogLevel.Debug,
			LogLevel.Verbose
		}; // All levels allowed by default

		/// <summary>
		/// Defines Unity Rich Text color strings for each LogLevel.
		/// Keys are LogLevel enum values, values are Unity color names (e.g., "red", "green")
		/// or hex codes (e.g., "#FF0000").
		/// </summary>
		public Dictionary<LogLevel, string> LogLevelColors { get; set; } = new Dictionary<LogLevel, string>
		{
			{ LogLevel.Critical, "red" },      // Or "#FF0000"
            { LogLevel.Error, "red" },         // Or "#FF0000"
            { LogLevel.Warning, "yellow" },    // Or "#FFFF00"
            { LogLevel.Info, "white" },        // Or "#FFFFFF"
            { LogLevel.Debug, "lime" },        // Or "#00FF00"
            { LogLevel.Verbose, "grey" }       // Or "#808080"
        };

		public UnityConsoleLoggerConfig()
		{
			Type = nameof(UnityConsoleLoggerConfig);
			LoggerType = nameof(UnityConsoleLogger);
		}
	}
}