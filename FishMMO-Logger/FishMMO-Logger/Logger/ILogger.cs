using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FishMMO.Logging
{
	/// <summary>
	/// Defines the interface for a logging service.
	/// </summary>
	public interface ILogger : IDisposable
	{
		/// <summary>
		/// Gets whether this logger is currently enabled.
		/// </summary>
		bool IsEnabled { get; }

		/// <summary>
		/// Gets the set of log levels that this logger is allowed to process.
		/// If a log entry's level is not in this set, it will be ignored by this logger.
		/// </summary>
		IReadOnlyCollection<LogLevel> AllowedLevels { get; }

		/// <summary>
		/// Gets a value indicating whether this logger is specifically designed to handle
		/// console output from the Log.WritePartsToConsole method.
		/// If true, this logger will be excluded from general dispatch when WritePartsToConsole
		/// has already sent output to the primary console formatter.
		/// </summary>
		bool HandlesConsoleParts { get; }

		/// <summary>
		/// Logs a structured log entry asynchronously.
		/// </summary>
		/// <param name="entry">The log entry to record.</param>
		Task Log(LogEntry entry);

		/// <summary>
		/// Sets the enabled state of the logger.
		/// </summary>
		/// <param name="enabled">True to enable, false to disable.</param>
		void SetEnabled(bool enabled);

		/// <summary>
		/// Sets the specific log levels that this logger is allowed to process.
		/// Setting this will replace any previously configured allowed levels.
		/// </summary>
		/// <param name="levels">A HashSet containing the LogLevels to allow. If null or empty, no levels will be allowed.</param>
		void SetAllowedLevels(HashSet<LogLevel> levels);
	}
}