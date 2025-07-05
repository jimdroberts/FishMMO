using System;
using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Bridges Unity's logging system with the FishMMO.Logging system by implementing ILogHandler.
	/// This allows intercepting all Unity Debug.Log messages and forwarding them to the central Log manager,
	/// while also preventing Unity's default console output.
	/// This class is designed to be separate from the core ILogger implementations.
	/// </summary>
	public class UnityLoggerBridge : ILogHandler
	{
		// Internal flag to prevent re-capturing logs that our own system just wrote to Debug.Log
		// This flag is set by loggers like UnityConsoleFormatter before they call Debug.Log
		internal static bool IsLoggingInternally = false;

		private static UnityLoggerBridge instance; // Singleton instance
		private ILogHandler defaultUnityLogHandler; // To store Unity's original log handler

		private static Action<string> _internalLogMessageCallback;

		/// <summary>
		/// Initializes the Unity logger bridge.
		/// This should be called once, typically at the start of your Unity application,
		/// for example, from a MonoBehaviour's Awake method.
		/// It sets this bridge as Unity's primary log handler.
		/// </summary>
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
		/// This should be called when your application is shutting down to clean up.
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
		/// Implementation of ILogHandler.LogFormat.
		/// This method intercepts all Debug.Log, Debug.LogWarning, Debug.LogError messages.
		/// </summary>
		public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args)
		{
			// If IsLoggingInternally is true, it means this log originated from our FishMMO.Logging system
			// (e.g., from UnityConsoleFormatter calling Debug.Log).
			// In this case, pass it directly to Unity's original log handler for display.
			if (IsLoggingInternally)
			{
				defaultUnityLogHandler.LogFormat(logType, context, format, args);
				return;
			}

			// If we reach here, this log originated from a direct Unity Debug.Log call
			// that was NOT initiated by our internal FishMMO.Logging system.
			// Convert Unity's log data to FishMMO.Logging's LogLevel
			LogLevel logLevel = ConvertLogTypeToLogLevel(logType);
			string message = string.Format(format, args);
			string source = "UNITY";

			// Pass the log to the FishMMO.Logging.Log static class.
			// We use _ = Log.Write(...) to fire-and-forget the async operation
			// and avoid blocking the Unity main thread.
			// UnityConsoleFormatter (if used) will then internally set IsLoggingInternally to true
			// before making its Debug.Log call, which UnityLoggerBridge will then route to defaultUnityLogHandler.
			_ = Log.Write(logLevel, source, message);
		}

		/// <summary>
		/// Implementation of ILogHandler.LogException.
		/// This method intercepts all Debug.LogException messages.
		/// </summary>
		public void LogException(Exception exception, UnityEngine.Object context)
		{
			// If IsLoggingInternally is true, pass it directly to Unity's original log handler.
			if (IsLoggingInternally)
			{
				defaultUnityLogHandler.LogException(exception, context);
				return;
			}

			// This exception originated from a direct Unity Debug.LogException call outside our system.
			LogLevel logLevel = LogLevel.Critical;
			string source = "UNITY";
			string message = "Unhandled Unity Exception";

			// Pass the exception log to the FishMMO.Logging.Log static class.
			_ = Log.Write(logLevel, source, message, exception);
		}

		// Helper method to convert Unity's LogType to your LogLevel
		private static LogLevel ConvertLogTypeToLogLevel(LogType type)
		{
			switch (type)
			{
				case LogType.Error:
					return LogLevel.Error;
				case LogType.Assert: // Assertions usually indicate critical issues
					return LogLevel.Critical;
				case LogType.Warning:
					return LogLevel.Warning;
				case LogType.Log: // Default for Debug.Log
					return LogLevel.Info;
				case LogType.Exception:
					return LogLevel.Critical; // Critical for unhandled exceptions
				default:
					return LogLevel.Info; // Fallback
			}
		}
	}
}