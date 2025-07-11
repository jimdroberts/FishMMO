using System.Collections.Generic;

namespace FishMMO.Logging
{
	/// <summary>
	/// Overall logging configuration, bundling individual logger configs dynamically.
	/// This is where you configure which loggers are active and their specific settings.
	/// </summary>
	public class LoggingConfig
	{
		/// <summary>
		/// Configuration specific to the LoggingManager itself (e.g., console output levels).
		/// </summary>
		public LoggingManagerConfig LoggingManager { get; set; } = new LoggingManagerConfig();

		/// <summary>
		/// A list of configurations for various ILogger implementations.
		/// This allows for adding new logger types without modifying LoggingConfig itself.
		/// </summary>
		public List<ILoggerConfig> Loggers { get; set; } = new List<ILoggerConfig>();
	}
}