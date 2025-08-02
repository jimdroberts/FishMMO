using System;
using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Bridges Unity's logging system with the FishMMO.Logging system by implementing ILogHandler.
	/// Intercepts all Unity Debug.Log messages and forwards them to the central Log manager,
	/// while also preventing Unity's default console output. Designed to be separate from core ILogger implementations.
	/// </summary>
	public class UnityLoggerBridge : ILogHandler
	{
		/// <summary>
		/// Internal flag to prevent re-capturing logs that our own system just wrote to Debug.Log.
		/// Set by loggers like UnityConsoleFormatter before they call Debug.Log.
		/// </summary>
		internal static bool IsLoggingInternally = false;

		/// <summary>
		/// Singleton instance of the bridge.
		/// </summary>
		private static UnityLoggerBridge instance;

		/// <summary>
		/// Stores Unity's original log handler for restoration.
		/// </summary>
		private ILogHandler defaultUnityLogHandler;

		/// <summary>
		/// Callback for internal log messages.
		/// </summary>
		private static Action<string> _internalLogMessageCallback;

		/// <summary>
		/// Initializes the Unity logger bridge and sets it as Unity's primary log handler.
		/// Should be called once, typically at application start.
		/// </summary>
		/// <param name="internalLogMessageCallback">Callback for internal log messages.</param>
		public static void Initialize(Action<string> internalLogMessageCallback)
		{
			_internalLogMessageCallback = internalLogMessageCallback;
			if (instance != null)
			{
				internalLogMessageCallback?.Invoke($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss 'UTC'}] [UnityLoggerBridge] Bridge already initialized. Skipping re-initialization.");
				return;
			}

			instance = new UnityLoggerBridge();

			// Store Unity's current log handler before replacing it.
			// This is crucial if you ever want to re-route certain logs back to Unity's default.
			instance.defaultUnityLogHandler = Debug.unityLogger.logHandler;

			// Set this instance as Unity's new log handler.
			// This effectively disables Unity's default console output for Debug.Log
			// that doesn't pass through our system.
			Debug.unityLogger.logHandler = instance;

			_internalLogMessageCallback?.Invoke($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss 'UTC'}] [UnityLoggerBridge] Bridge initialized and set as ILogHandler.");
		}

		/// <summary>
		/// Shuts down the Unity logger bridge and restores Unity's default log handler.
		/// Should be called during application shutdown for cleanup.
		/// </summary>
		public static void Shutdown()
		{
			if (instance == null)
			{
				return;
			}

			// Restore Unity's default log handler
			if (Debug.unityLogger.logHandler == instance)
			{
				Debug.unityLogger.logHandler = instance.defaultUnityLogHandler;
			}

			_internalLogMessageCallback?.Invoke($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss 'UTC'}] [UnityLoggerBridge] Bridge shut down and default ILogHandler restored.");
			instance = null;
		}

		/// <summary>
		/// Implementation of ILogHandler.LogFormat. Intercepts all Debug.Log, Debug.LogWarning, Debug.LogError messages.
		/// </summary>
		/// <param name="logType">The type of log message.</param>
		/// <param name="context">The Unity object context.</param>
		/// <param name="format">The log message format string.</param>
		/// <param name="args">Arguments for the format string.</param>
		public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args)
		{
			// If IsLoggingInternally is true, this log originated from our FishMMO.Logging system
			// (e.g., from UnityConsoleFormatter calling Debug.Log). Pass directly to Unity's original log handler.
			if (IsLoggingInternally)
			{
				defaultUnityLogHandler.LogFormat(logType, context, format, args);
				return;
			}

			// Log originated from a direct Unity Debug.Log call outside our system.
			LogLevel logLevel = ConvertLogTypeToLogLevel(logType);
			string message = string.Format(format, args);
			string source = "UNITY";

			// Pass the log to the FishMMO.Logging.Log static class (fire-and-forget async).
			_ = Log.Write(logLevel, source, message);
		}

		/// <summary>
		/// Implementation of ILogHandler.LogException. Intercepts all Debug.LogException messages.
		/// </summary>
		/// <param name="exception">The exception to log.</param>
		/// <param name="context">The Unity object context.</param>
		public void LogException(Exception exception, UnityEngine.Object context)
		{
			// If IsLoggingInternally is true, pass directly to Unity's original log handler.
			if (IsLoggingInternally)
			{
				defaultUnityLogHandler.LogException(exception, context);
				return;
			}

			// Exception originated from a direct Unity Debug.LogException call outside our system.
			LogLevel logLevel = LogLevel.Critical;
			string source = "UNITY";
			string message = "Unhandled Unity Exception";

			// Pass the exception log to the FishMMO.Logging.Log static class.
			_ = Log.Write(logLevel, source, message, exception);
		}

		/// <summary>
		/// Converts Unity's LogType to FishMMO.Logging.LogLevel.
		/// </summary>
		/// <param name="type">The Unity LogType.</param>
		/// <returns>The corresponding LogLevel.</returns>
		private static LogLevel ConvertLogTypeToLogLevel(LogType type)
		{
			switch (type)
			{
				case LogType.Error:
					return LogLevel.Error;
				case LogType.Assert:
					return LogLevel.Critical;
				case LogType.Warning:
					return LogLevel.Warning;
				case LogType.Log:
					return LogLevel.Info;
				case LogType.Exception:
					return LogLevel.Critical;
				default:
					return LogLevel.Info;
			}
		}
	}
}