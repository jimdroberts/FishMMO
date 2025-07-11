using System.Collections.Generic;
using System.Text.Json.Serialization; // Added for JsonConverter attribute

namespace FishMMO.Logging
{
	/// <summary>
	/// Defines the common interface for all logger configurations.
	/// This allows for a flexible list of different logger types in the main LoggingConfig.
	/// </summary>
	[JsonConverter(typeof(ILoggerConfigConverter))] // Instructs System.Text.Json to use our custom converter
	public interface ILoggerConfig
	{
		/// <summary>
		/// A discriminator property used during JSON deserialization to identify the concrete Logger Config type.
		/// </summary>
		string Type { get; set; } // Added for JSON deserialization


		/// <summary>
		/// A discriminator property used during JSON deserialization to identify the concrete Logger type.
		/// </summary>
		string LoggerType { get; set; } // Added for JSON deserialization

		/// <summary>
		/// Gets or sets whether the logger associated with this configuration is enabled.
		/// </summary>
		bool Enabled { get; set; }

		/// <summary>
		/// Gets or sets the set of log levels that this logger is allowed to process.
		/// If a log entry's level is not in this set, it will be ignored by this logger.
		/// </summary>
		HashSet<LogLevel> AllowedLevels { get; set; }
	}
}