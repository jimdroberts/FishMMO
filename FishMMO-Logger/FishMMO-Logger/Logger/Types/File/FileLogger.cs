using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading; // Add this namespace for SemaphoreSlim

namespace FishMMO.Logging
{
	public class FileLogger : ILogger
	{
		private readonly FileLoggerConfig config;
		private StreamWriter writer;
		private string currentLogFilePath;
		private string logDirectoryPath;

		public bool IsEnabled { get; private set; }
		private HashSet<LogLevel> allowedLevels;
		public bool HandlesConsoleParts { get { return true; } }

		public IReadOnlyCollection<LogLevel> AllowedLevels => allowedLevels;

		private readonly long maxFileSizeBytes;
		private readonly int maxRolloverFiles;
		private readonly string baseFileName;
		private readonly string fileExtension;

		private readonly Action<string> internalLogCallback;

		// Add a SemaphoreSlim for asynchronous locking
		private readonly SemaphoreSlim writeLock = new SemaphoreSlim(1, 1);

		public FileLogger(FileLoggerConfig config, Action<string> internalLogCallback = null)
		{
			this.config = config ?? throw new ArgumentNullException(nameof(config), "FileLogger configuration cannot be null.");
			this.internalLogCallback = internalLogCallback;

			// Ensure configuration values are sensible
			this.maxFileSizeBytes = config.MaxFileSizeKB * 1024L; // Convert KB to bytes
			this.maxRolloverFiles = Math.Max(0, config.MaxRolloverFiles); // Ensure it's not negative

			this.allowedLevels = config.AllowedLevels ?? new HashSet<LogLevel>();

			// Extract base file name and extension
			this.baseFileName = Path.GetFileNameWithoutExtension(config.FileName);
			this.fileExtension = Path.GetExtension(config.FileName);

			// Construct and ensure the log directory exists
			this.logDirectoryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, config.LogDirectory);
			Directory.CreateDirectory(logDirectoryPath); // Ensure directory exists

			internalLogCallback?.Invoke($"[FileLogger] Initialized with directory: {logDirectoryPath}, max size: {config.MaxFileSizeKB}KB, max rollovers: {maxRolloverFiles}.");
		}

		public async Task Log(LogEntry entry)
		{
			if (!IsEnabled || !allowedLevels.Contains(entry.Level)) return;

			// Acquire the semaphore before accessing the writer
			await writeLock.WaitAsync();
			try
			{
				// Ensure the writer is ready and handle rollovers if necessary.
				// CheckAndRollover will also execute while the lock is held.
				await CheckAndRollover();

				string logLine = entry.ToString();

				// Asynchronously write the log line to the file
				await writer.WriteLineAsync(logLine);
				await writer.FlushAsync(); // Ensure data is written to disk

				//internalLogCallback?.Invoke($"[FileLogger] Logged entry to {currentLogFilePath}: {entry.Message}");
			}
			catch (Exception ex)
			{
				internalLogCallback?.Invoke($"[FileLogger] ERROR: Failed to write log entry to file '{currentLogFilePath}': {ex.Message}");
			}
			finally
			{
				// Release the semaphore in a finally block to ensure it's always released
				writeLock.Release();
			}
		}

		/// <summary>
		/// Sets the enabled state of the logger.
		/// </summary>
		/// <param name="enabled">True to enable, false to disable.</param>
		public void SetEnabled(bool enabled)
		{
			if (IsEnabled == enabled) return; // No change needed
			IsEnabled = enabled;

			if (enabled)
			{
				// Initialize writer if enabling. Using .Wait() for simplicity,
				// but in a production async context, you might handle this differently.
				// NOTE: This call to InitializeWriter must also be safe for potential concurrent access
				// or ensure it's only called once during setup. If CheckAndRollover calls it,
				// CheckAndRollover must ensure synchronization.
				// For direct writes to file, the `writeLock` in Log method is the primary fix.
				InitializeWriter().Wait();
			}
			else
			{
				Dispose(); // Clean up writer when disabled
			}
			internalLogCallback?.Invoke($"[FileLogger] File logging enabled set to: {enabled}.");
		}

		/// <summary>
		/// Sets the specific log levels that this logger is allowed to process.
		/// Setting this will replace any previously configured allowed levels.
		/// </summary>
		/// <param name="levels">A HashSet containing the LogLevels to allow. If null or empty, no levels will be allowed.</param>
		public void SetAllowedLevels(HashSet<LogLevel> levels)
		{
			allowedLevels = levels ?? new HashSet<LogLevel>(); // Ensure it's never null
			internalLogCallback?.Invoke($"[FileLogger] Allowed levels set to: {string.Join(", ", allowedLevels.Select(l => l.ToString()))}.");
		}

		/// <summary>
		/// Disposes the underlying StreamWriter, releasing file resources.
		/// </summary>
		public void Dispose()
		{
			if (writer != null)
			{
				try
				{
					writer.Close(); // Close the underlying stream
					writer.Dispose(); // Release resources
					writer = null;
				}
				catch (Exception ex)
				{
					internalLogCallback?.Invoke($"[FileLogger] ERROR: Error disposing file logger writer: {ex.Message}");
				}
			}
		}

		/// <summary>
		/// Ensures the log file is open and handles rollover logic if the current file exceeds its maximum size.
		/// </summary>
		private async Task CheckAndRollover()
		{
			// If no writer is open, or it's disposed, open a new one.
			if (writer == null || (writer.BaseStream is FileStream fs && !fs.CanWrite))
			{
				await InitializeWriter();
				return;
			}

			// Check if the current file size exceeds the max allowed size
			if (writer.BaseStream.Length >= maxFileSizeBytes)
			{
				internalLogCallback?.Invoke($"[FileLogger] Log file '{currentLogFilePath}' exceeded max size ({maxFileSizeBytes / 1024}KB). Initiating rollover.");
				Dispose(); // Close current file

				// Roll over existing files
				RollFiles();

				// Open a new file
				await InitializeWriter();
			}
		}

		/// <summary>
		/// Initializes or re-initializes the StreamWriter for the current log file.
		/// </summary>
		private async Task InitializeWriter()
		{
			// Generate a new file name for the current session (or rollover)
			currentLogFilePath = GetNewLogFilePath();
			writer = new StreamWriter(currentLogFilePath, append: true, encoding: Encoding.UTF8)
			{
				AutoFlush = false // Manual flushing for performance and control
			};
			internalLogCallback?.Invoke($"[FileLogger] Opened new log file: {currentLogFilePath}");
		}

		/// <summary>
		/// Generates a new unique log file path based on current timestamp.
		/// </summary>
		private string GetNewLogFilePath()
		{
			string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
			return Path.Combine(logDirectoryPath, $"{baseFileName}_{timestamp}{fileExtension}");
		}

		/// <summary>
		/// Manages the rollover of log files, ensuring only `maxRolloverFiles` are kept.
		/// </summary>
		private void RollFiles()
		{
			// Get all log files for this logger, ordered by creation time descending
			var existingLogFiles = Directory.GetFiles(logDirectoryPath, $"{baseFileName}_*{fileExtension}")
											.Select(f => new FileInfo(f))
											.OrderByDescending(f => f.CreationTimeUtc)
											.ToList();

			// Delete older files beyond the max rollover limit
			if (maxRolloverFiles > 0) // Only roll if maxRolloverFiles is positive
			{
				for (int i = maxRolloverFiles - 1; i < existingLogFiles.Count; i++) // Keep maxRolloverFiles files
				{
					try
					{
						existingLogFiles[i].Delete();
						internalLogCallback?.Invoke($"[FileLogger] Deleted old log file: {existingLogFiles[i].FullName}");
					}
					catch (Exception ex)
					{
						internalLogCallback?.Invoke($"[FileLogger] ERROR: Failed to delete old log file '{existingLogFiles[i].FullName}': {ex.Message}");
					}
				}
			}
			else if (maxRolloverFiles == 0) // If maxRolloverFiles is 0, delete all existing files before writing new one
			{
				foreach (var file in existingLogFiles)
				{
					try
					{
						file.Delete();
						internalLogCallback?.Invoke($"[FileLogger] Deleted all old log file (maxRolloverFiles is 0): {file.FullName}");
					}
					catch (Exception ex)
					{
						internalLogCallback?.Invoke($"[FileLogger] ERROR: Failed to delete old log file '{file.FullName}': {ex.Message}");
					}
				}
			}
		}
	}
}