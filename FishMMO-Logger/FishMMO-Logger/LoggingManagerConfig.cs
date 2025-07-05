using System.Collections.Generic;

namespace FishMMO.Logging
{
	/// <summary>
	/// Configuration for the LoggingManager itself.
	/// </summary>
	public class LoggingManagerConfig
	{
		/// <summary>
		/// The set of log levels that will be displayed on the console.
		/// If null or empty, no logs will be displayed on the console.
		/// Default: All levels from Critical to Verbose.
		/// </summary>
		public HashSet<LogLevel> ConsoleAllowedLevels { get; set; } = new HashSet<LogLevel>
		{
			LogLevel.Info,
			LogLevel.Debug,
			LogLevel.Verbose,
			LogLevel.Warning,
			LogLevel.Error,
			LogLevel.Critical
		};
	}
}