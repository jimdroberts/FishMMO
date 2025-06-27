using System.Collections.Concurrent;

namespace AppHealthMonitor
{
	/// <summary>
	/// Implements ILogger to write log entries to a local text file.
	/// Uses a concurrent queue and a background task to ensure non-blocking file writes.
	/// </summary>
	public class FileLogger : ILogger, IDisposable
	{
		private readonly string logFilePath;
		private readonly LogLevel minimumLevel;
		private readonly ConcurrentQueue<LogEntry> logQueue = new ConcurrentQueue<LogEntry>();
		private readonly CancellationTokenSource cts = new CancellationTokenSource();
		private readonly Task loggingTask;
		private readonly SemaphoreSlim writeLock = new SemaphoreSlim(1, 1); // To ensure single writer to file

		/// <summary>
		/// Initializes a new instance of the FileLogger.
		/// </summary>
		/// <param name="config">Configuration for the file logger, including path and minimum log level.</param>
		public FileLogger(FileLoggerConfig config)
		{
			if (config == null)
			{
				throw new ArgumentNullException(nameof(config), "FileLogger configuration cannot be null.");
			}
			if (string.IsNullOrWhiteSpace(config.LogFilePath))
			{
				throw new ArgumentException("Log file path cannot be null or empty.", nameof(config.LogFilePath));
			}

			logFilePath = Path.GetFullPath(config.LogFilePath); // Ensure full path resolution
			minimumLevel = config.MinimumLevel;

			// Ensure the directory exists
			string? logDirectory = Path.GetDirectoryName(logFilePath);
			if (!string.IsNullOrEmpty(logDirectory) && !Directory.Exists(logDirectory))
			{
				try
				{
					Directory.CreateDirectory(logDirectory);
				}
				catch (Exception ex)
				{
					Console.Error.WriteLine($"[{DateTime.Now:HH:mm:ss}] [FileLogger] CRITICAL ERROR: Could not create log directory '{logDirectory}'. Logging to file will fail. Exception: {ex.Message}");
				}
			}

			// Start a background task to process the log queue
			loggingTask = Task.Run(ProcessLogQueueAsync, cts.Token);
			Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [FileLogger] Initialized. Logging to: '{logFilePath}' (Min Level: {minimumLevel})");
		}

		/// <summary>
		/// Logs a structured entry to the file if its level meets the minimum configured level.
		/// Adds the log entry to an internal queue for asynchronous processing.
		/// </summary>
		/// <param name="entry">The log entry to record.</param>
		public Task Log(LogEntry entry)
		{
			if (entry == null)
			{
				// Can't log a null entry, just return
				return Task.CompletedTask;
			}

			if (entry.Level >= minimumLevel)
			{
				logQueue.Enqueue(entry);
			}
			return Task.CompletedTask; // Return immediately, actual logging happens in background
		}

		/// <summary>
		/// Background task that processes log entries from the queue and writes them to the file.
		/// </summary>
		private async Task ProcessLogQueueAsync()
		{
			try
			{
				while (!cts.Token.IsCancellationRequested)
				{
					if (logQueue.TryDequeue(out LogEntry entry))
					{
						// Acquire lock before writing to ensure only one writer at a time
						await writeLock.WaitAsync(cts.Token);
						try
						{
							// Append text asynchronously to the file
							await File.AppendAllTextAsync(logFilePath, entry.ToString() + Environment.NewLine, cts.Token);
						}
						catch (OperationCanceledException)
						{
							throw; // Propagate cancellation
						}
						catch (Exception ex)
						{
							// Log internal error to console, as file logging failed
							Console.Error.WriteLine($"[{DateTime.Now:HH:mm:ss}] [FileLogger] ERROR: Failed to write log entry to file '{logFilePath}'. Exception: {ex.Message}");
							Console.Error.WriteLine($"[{DateTime.Now:HH:mm:ss}] [FileLogger] Failed Entry: {entry.ToString()}");
						}
						finally
						{
							writeLock.Release();
						}
					}
					else
					{
						// No items in queue, wait a bit before checking again
						await Task.Delay(100, cts.Token);
					}
				}
			}
			catch (OperationCanceledException)
			{
				// Task was cancelled, exit gracefully
				Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [FileLogger] Background logging task cancelled.");
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine($"[{DateTime.Now:HH:mm:ss}] [FileLogger] CRITICAL ERROR: Unhandled exception in background logging task. Exception: {ex.Message}");
			}
		}

		/// <summary>
		/// Disposes the logger, stopping the background task and releasing resources.
		/// </summary>
		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		protected virtual void Dispose(bool disposing)
		{
			if (disposing)
			{
				cts.Cancel(); // Signal cancellation to the logging task
				loggingTask.Wait(TimeSpan.FromSeconds(5)); // Wait for task to finish gracefully
				cts.Dispose();
				writeLock.Dispose();
			}
		}
	}
}