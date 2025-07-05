using System.Collections.Generic;

namespace FishMMO.Logging
{
	/// <summary>
	/// Configuration for the FileLogger.
	/// </summary>
	public class FileLoggerConfig : ILoggerConfig // Implements ILoggerConfig
	{
		public string Type { get; set; } // Implement Type property for JSON discriminator
		public string LoggerType { get; set; }
		public bool Enabled { get; set; } = false;
		/// <summary>
		/// The set of log levels that this file logger is allowed to process.
		/// Default: All levels from Info to Critical.
		/// </summary>
		public HashSet<LogLevel> AllowedLevels { get; set; } = new HashSet<LogLevel>
		{
			LogLevel.Info,
			LogLevel.Debug,
			LogLevel.Verbose,
			LogLevel.Warning,
			LogLevel.Error,
			LogLevel.Critical
		};
		public string FileName { get; set; } = "app_log.txt"; // Default log file name
		public string LogDirectory { get; set; } = "Logs"; // Relative path to the log directory

		/// <summary>
		/// The maximum size of a single log file in kilobytes (KB).
		/// When this size is exceeded, the log file will be rolled over. Default is 10 MB (10 * 1024 KB).
		/// </summary>
		public long MaxFileSizeKB { get; set; } = 10 * 1024; // Default to 10 MB (in KB)

		/// <summary>
		/// The maximum number of rolled-over log files to keep.
		/// When a new file is created, the oldest files exceeding this limit will be deleted. Default is 5.
		/// </summary>
		public int MaxRolloverFiles { get; set; } = 5;

		public FileLoggerConfig()
		{
			Type = nameof(FileLoggerConfig); // Set the discriminator type
			LoggerType = nameof(FileLogger);
		}
	}
}