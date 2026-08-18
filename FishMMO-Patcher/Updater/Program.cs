using System.Diagnostics;
using System.IO.Compression;
using System.IO.Hashing;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using FishMMO.Patcher;
using System.Collections.Concurrent;

class Program
{
	private static readonly string WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory;

	/// <summary>
	/// Where the install itself lives. Always the updater's own directory — the updater ships
	/// beside the client binaries it patches, so this is the install root by construction and
	/// is not configurable. Relocating an install means moving the updater with it.
	/// </summary>
	private static string InstallDirectory => WorkingDirectory;

	/// <summary>
	/// Directory patch archives are read from. Defaults to "Patches" under the install root
	/// and is overridden by <c>-patches=</c>.
	/// </summary>
	/// <remarks>
	/// This must resolve to the same folder the launcher downloaded into
	/// (<c>FishMMO.Shared.Constants.GetPatchesDirectory()</c>, which honours the same setting).
	/// If the two ever disagree the archive is not found, the update silently no-ops, and the
	/// client relaunches at the same version forever — so the launcher passes its resolved path
	/// explicitly rather than both sides deriving it and hoping they agree.
	/// <para>
	/// Overriding this does not weaken the integrity guarantee: the launcher hashes the archive
	/// against the server-supplied SHA-256 before invoking the updater at all, and anyone able
	/// to write the launcher's configuration file could equally write a file into the default
	/// location. The override changes where a verified archive is read from, not whether it is
	/// verified.
	/// </para>
	/// </remarks>
	private static string PatchesDirectory = Path.Combine(WorkingDirectory, "Patches");

	private static string Version;
	private static string LatestVersion;
	private static int PID;
	private static string Executable;

	// Dedicated lock object for thread-safe console output.
	private static readonly object consoleLock = new object();

	// Configuration for robust file operations.
	private const int MaxFileOperationRetries = 5;
	private const int FileOperationRetryDelayMs = 200;

	// Process shutdown tuning.
	private const int GracefulExitTimeoutMs = 10000;
	private const int ForceKillTimeoutMs = 5000;
	private const int PostKillSettleMs = 500;

	/// <summary>POSIX SIGTERM. Requests a graceful shutdown.</summary>
	private const int SIGTERM = 15;

	/// <summary>
	/// POSIX <c>kill(2)</c>. Used to request a graceful shutdown on Linux/macOS, where
	/// .NET exposes no managed equivalent — <see cref="Process.Kill()"/> sends SIGKILL,
	/// which denies the client any chance to run its shutdown handlers, and
	/// <see cref="Process.CloseMainWindow"/> is Windows-only.
	/// </summary>
	[DllImport("libc", SetLastError = true, EntryPoint = "kill")]
	private static extern int SysKill(int pid, int sig);

	/// <summary>
	/// Points <see cref="PatchesDirectory"/> at <paramref name="path"/> when it is usable,
	/// otherwise leaves the default in place.
	/// </summary>
	/// <remarks>
	/// Falling back rather than failing is deliberate. The override is a convenience — it lets
	/// a player keep patch archives off a small system drive — and an unusable one should cost
	/// them the convenience, not the update. The default location is where the launcher writes
	/// when its own setting is unusable, so the two still agree after the fallback.
	/// </remarks>
	private static void ApplyPatchesDirectoryOverride(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return;
		}

		try
		{
			// Rooted only. A relative path resolves against the updater's current directory,
			// which is not guaranteed to be the install root when it is started by the OS
			// rather than by the launcher — so the same string could mean two places.
			if (!Path.IsPathRooted(path))
			{
				Console.WriteLine($"WARNING: Ignoring -patches='{path}' because it is not an absolute path. Using '{PatchesDirectory}'.");
				return;
			}

			string full = Path.GetFullPath(path);
			if (!Directory.Exists(full))
			{
				Console.WriteLine($"WARNING: Ignoring -patches='{full}' because the directory does not exist. Using '{PatchesDirectory}'.");
				return;
			}

			PatchesDirectory = full;
			Console.WriteLine($"Patch archives will be read from '{PatchesDirectory}'.");
		}
		catch (Exception ex)
		{
			Console.WriteLine($"WARNING: Ignoring -patches='{path}' ({ex.Message}). Using '{PatchesDirectory}'.");
		}
	}

	/// <summary>
	/// Helper method for robust file deletion with retries.
	/// </summary>
	/// <param name="path">The path of the file to delete.</param>
	/// <param name="retries">The number of retry attempts.</param>
	/// <returns>True if the file was successfully deleted or didn't exist, false otherwise.</returns>
	private static bool TryDeleteFile(string path, int retries)
	{
		for (int i = 0; i < retries; i++)
		{
			try
			{
				if (File.Exists(path))
				{
					File.Delete(path);
					lock (consoleLock)
					{
						Console.WriteLine($"DEBUG: Successfully deleted: '{path}' (Attempt {i + 1})");
					}
				}
				return true;
			}
			catch (IOException ex)
			{
				lock (consoleLock)
				{
					Console.WriteLine($"WARNING: Failed to delete '{path}' (Attempt {i + 1}/{retries}). Reason: {ex.Message}");
				}
				Thread.Sleep(FileOperationRetryDelayMs);
			}
			catch (UnauthorizedAccessException ex)
			{
				lock (consoleLock)
				{
					Console.WriteLine($"ERROR: Unauthorized access when deleting '{path}'. This usually indicates permission issues. Reason: {ex.Message}");
				}
				return false;
			}
			catch (Exception ex)
			{
				lock (consoleLock)
				{
					Console.WriteLine($"ERROR: An unexpected error occurred while deleting '{path}'. Reason: {ex.Message}");
				}
				return false;
			}
		}
		lock (consoleLock)
		{
			Console.WriteLine($"ERROR: Failed to delete '{path}' after {retries} attempts. Giving up.");
		}
		return false;
	}

	/// <summary>
	/// Helper method for robust file move with retries.
	/// </summary>
	/// <param name="sourcePath">The source file path.</param>
	/// <param name="destinationPath">The destination file path.</param>
	/// <param name="retries">The number of retry attempts.</param>
	/// <returns>True if the file was successfully moved, false otherwise.</returns>
	private static bool TryMoveFile(string sourcePath, string destinationPath, int retries)
	{
		for (int i = 0; i < retries; i++)
		{
			try
			{
				File.Move(sourcePath, destinationPath);
				lock (consoleLock)
				{
					Console.WriteLine($"DEBUG: Successfully moved '{sourcePath}' to '{destinationPath}' (Attempt {i + 1})");
				}
				return true;
			}
			catch (IOException ex)
			{
				lock (consoleLock)
				{
					Console.WriteLine($"WARNING: Failed to move '{sourcePath}' to '{destinationPath}' (Attempt {i + 1}/{retries}). Reason: {ex.Message}");
				}
				Thread.Sleep(FileOperationRetryDelayMs);
			}
			catch (UnauthorizedAccessException ex)
			{
				lock (consoleLock)
				{
					Console.WriteLine($"ERROR: Unauthorized access when moving '{sourcePath}' to '{destinationPath}'. Reason: {ex.Message}");
				}
				return false;
			}
			catch (Exception ex)
			{
				lock (consoleLock)
				{
					Console.WriteLine($"ERROR: An unexpected error occurred while moving '{sourcePath}' to '{destinationPath}'. Reason: {ex.Message}");
				}
				return false;
			}
		}
		lock (consoleLock)
		{
			Console.WriteLine($"ERROR: Failed to move '{sourcePath}' to '{destinationPath}' after {retries} attempts. Giving up.");
		}
		return false;
	}

	static void Main(string[] args)
	{
		// Parse command line arguments.
		foreach (var arg in args)
		{
			if (arg.StartsWith("-version"))
			{
				var splitArg = arg.Split('=');
				if (splitArg.Length == 2) Version = splitArg[1];
			}
			if (arg.StartsWith("-latestversion"))
			{
				var splitArg = arg.Split('=');
				if (splitArg.Length == 2) LatestVersion = splitArg[1];
			}
			if (arg.StartsWith("-pid"))
			{
				var splitArg = arg.Split('=');
				if (splitArg.Length == 2 && int.TryParse(splitArg[1], out int pid)) PID = pid;
			}
			if (arg.StartsWith("-exe"))
			{
				var splitArg = arg.Split('=');
				if (splitArg.Length == 2) Executable = splitArg[1];
			}
			if (arg.StartsWith("-patches"))
			{
				// Split on the first '=' only. A Windows path can contain '=', and
				// Split('=') would truncate it at the first one.
				int separator = arg.IndexOf('=');
				if (separator > 0 && separator < arg.Length - 1)
				{
					ApplyPatchesDirectoryOverride(arg.Substring(separator + 1));
				}
			}
		}

		Console.WriteLine($"Client Patcher started. Current Client Version: {Version}, LatestVersion: {LatestVersion}, Launcher PID: {PID}, Executable: {Executable}");

		// Terminate the launcher process before patching begins.
		KillLauncherProcess(PID);

		if (Version == LatestVersion)
		{
			Console.WriteLine("Client is already up-to-date. Exiting patcher.");
			TryStartExecutableAndExit(Executable, PID);
			return;
		}

		// Naming scheme shared with the launcher's download path and the patcher server's
		// index. See PatchesDirectory.
		string expectedPatchFileName = $"{Version}-{LatestVersion}.zip";
		string patchFilePath = Path.Combine(PatchesDirectory, expectedPatchFileName);

		if (!File.Exists(patchFilePath))
		{
			Console.WriteLine($"Error: Expected patch file '{expectedPatchFileName}' not found in '{PatchesDirectory}'. Cannot update client.");
			TryStartExecutableAndExit(Executable, PID);
			return;
		}

		Console.WriteLine($"\nApplying patch: {Path.GetFileName(patchFilePath)}");
		bool applied = ApplyPatchFile(patchFilePath);

		if (applied)
		{
			Console.WriteLine("\nPatch applied. Client is up-to-date.");
			// Remove the consumed archive so Patches/ does not accumulate every update the
			// player has ever installed. Only on success — a failed apply rolls back, and
			// keeping the archive allows a retry without re-downloading.
			if (!TryDeleteFile(patchFilePath, MaxFileOperationRetries))
			{
				Console.WriteLine($"WARNING: Could not remove the applied patch archive '{patchFilePath}'. It can be deleted manually.");
			}
		}
		else
		{
			Console.WriteLine("\nPatch application FAILED. The client has been rolled back to its previous state and remains on the old version.");
			Console.WriteLine($"The patch archive has been left at '{patchFilePath}' for a retry.");
		}

		Console.WriteLine("Exiting patcher.");
		TryStartExecutableAndExit(Executable, PID);
	}

	/// <summary>
	/// Applies a single patch file (ZIP archive) to the client.
	/// This method orchestrates parallel patching of new/modified files,
	/// followed by sequential deletions and file moves for finalization.
	/// If any critical step fails, a full rollback is attempted.
	/// </summary>
	/// <param name="patchFilePath">The full path to the patch ZIP file.</param>
	/// <returns>
	/// True when every operation committed successfully; false when the patch was rolled
	/// back or otherwise did not complete. Callers must not treat a failed apply as an
	/// upgrade — the install is still on the old version.
	/// </returns>
	static bool ApplyPatchFile(string patchFilePath)
	{
		// Lists to store paths for deferred sequential operations
		List<string> filesToDelete = new List<string>();
		// Tuple: Item1 = tempPatchedFilePath, Item2 = normalizedTargetFilePath
		List<Tuple<string, string>> filesToMove = new List<Tuple<string, string>>();
		List<string> tempFilesCreated = new List<string>(); // To track temp files for cleanup on error

		// Store original file paths for potential rollback
		// This will store backups created *before* any modifications are committed.
		Dictionary<string, string> originalFileBackups = new Dictionary<string, string>(); // TargetPath -> BackupPath

		// Flag to track overall success of the patching process.
		bool overallSuccess = true;
		// ConcurrentBag to collect exceptions from parallel operations.
		ConcurrentBag<Exception> parallelExceptions = new ConcurrentBag<Exception>();

		try
		{
			using (ZipArchive archive = ZipFile.OpenRead(patchFilePath))
			{
				ZipArchiveEntry manifestEntry = archive.GetEntry("manifest.json");
				if (manifestEntry == null)
				{
					Console.WriteLine($"Error: manifest.json not found in patch file '{Path.GetFileName(patchFilePath)}'. Skipping.");
					overallSuccess = false; // Mark as failed
					return false; // Exit early (the finally block still runs)
				}

				PatchManifest manifest;
				using (Stream manifestStream = manifestEntry.Open())
				{
					manifest = JsonSerializer.Deserialize<PatchManifest>(manifestStream);
				}
				Console.WriteLine($"Loaded manifest from {Path.GetFileName(patchFilePath)}. Old Version: {manifest.OldVersion}, New Version: {manifest.NewVersion}");

				// Phase 1: Pre-create all necessary directories.
				Console.WriteLine($"Pre-creating {manifest.NewFiles?.Count + manifest.ModifiedFiles?.Count ?? 0} directories...");
				PreCreateDirectories(manifest, parallelExceptions);
				if (!parallelExceptions.IsEmpty)
				{
					overallSuccess = false;
					throw new AggregateException("Directory pre-creation failed.", parallelExceptions);
				}


				// Phase 2: Process new files in parallel.
				Console.WriteLine($"Processing {manifest.NewFiles?.Count ?? 0} new files in parallel...");
				Parallel.ForEach(manifest.NewFiles ?? new List<NewFileEntry>(), (newFile, loopState) =>
				{
					string fullPath = Path.Combine(WorkingDirectory, newFile.RelativePath);
					ZipArchiveEntry newFileZipEntry = archive.GetEntry(newFile.FileDataEntryName);

					if (newFileZipEntry != null)
					{
						try
						{
							using (Stream sourceStream = newFileZipEntry.Open())
							using (FileStream destinationStream = File.Create(fullPath)) // File.Create ensures exclusive access
							{
								sourceStream.CopyTo(destinationStream);
							}
							lock (consoleLock)
							{
								Console.WriteLine($"\tAdded new file: {newFile.RelativePath}");
							}

							string actualHash = ComputeFileHash(fullPath);
							if (actualHash != newFile.NewHash)
							{
								lock (consoleLock)
								{
									Console.WriteLine($"\tERROR: Hash mismatch for new file {newFile.RelativePath}. Expected {newFile.NewHash}, Got {actualHash}. File might be corrupted or patch is bad. (Deleting file)");
								}
								// Attempt to delete the bad file, but this is a critical failure.
								if (!TryDeleteFile(fullPath, MaxFileOperationRetries))
								{
									throw new IOException($"Failed to delete corrupted new file: {fullPath}");
								}
								throw new InvalidOperationException($"Hash mismatch for new file: {newFile.RelativePath}"); // Propagate failure
							}
							else
							{
								lock (consoleLock)
								{
									Console.WriteLine($"\tHash verified for new file: {newFile.RelativePath}");
								}
							}
						}
						catch (Exception ex)
						{
							lock (consoleLock)
							{
								Console.WriteLine($"\tError adding new file {newFile.RelativePath}: {ex.Message}");
							}
							parallelExceptions.Add(ex); // Collect exception
							loopState.Stop(); // Signal other tasks to stop if possible
						}
					}
					else
					{
						lock (consoleLock)
						{
							Console.WriteLine($"\tWarning: New file entry not found in ZIP: {newFile.FileDataEntryName}");
						}
						parallelExceptions.Add(new FileNotFoundException($"New file entry not found in ZIP: {newFile.FileDataEntryName}"));
						loopState.Stop();
					}
				});
				if (!parallelExceptions.IsEmpty)
				{
					overallSuccess = false;
					throw new AggregateException("Processing new files failed.", parallelExceptions);
				}


				// Phase 3: Process modified files in parallel (create temp files).
				Console.WriteLine($"Processing {manifest.ModifiedFiles?.Count ?? 0} modified files in parallel (creating temp files)...");
				Parallel.ForEach(manifest.ModifiedFiles ?? new List<ModifiedFileEntry>(), (modifiedFile, loopState) =>
				{
					Patcher patcher = new Patcher(); // Each task gets its own Patcher instance
					string oldFilePath = Path.Combine(WorkingDirectory, modifiedFile.RelativePath);
					ZipArchiveEntry patchDataEntry = archive.GetEntry(modifiedFile.PatchDataEntryName);

					if (patchDataEntry != null)
					{
						lock (consoleLock)
						{
							Console.WriteLine($"\tPatching {modifiedFile.RelativePath} to temp file...");
						}
						using (Stream patchDataStream = patchDataEntry.Open())
						{
							using (BinaryReader reader = new BinaryReader(patchDataStream))
							{
								string tempPatchedFilePath = null;
								try
								{
									// Generate a unique temp file path for each parallel task
									string uniqueTempFilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".tmp");
									// Ensure the temp directory exists
									string tempDir = Path.GetDirectoryName(uniqueTempFilePath);
									if (!string.IsNullOrEmpty(tempDir) && !Directory.Exists(tempDir))
									{
										Directory.CreateDirectory(tempDir);
									}

									tempPatchedFilePath = patcher.Apply(reader, oldFilePath, modifiedFile.FinalFileSize, uniqueTempFilePath);
								}
								catch (Exception ex) // Catch exceptions from Patcher.Apply
								{
									parallelExceptions.Add(ex);
									loopState.Stop();
									return; // Exit this parallel task
								}

								if (tempPatchedFilePath != null)
								{
									lock (filesToMove) // Protect shared list
									{
										filesToMove.Add(Tuple.Create(tempPatchedFilePath, oldFilePath));
									}
									lock (tempFilesCreated) // Track for cleanup
									{
										tempFilesCreated.Add(tempPatchedFilePath);
									}
									lock (consoleLock)
									{
										Console.WriteLine($"\tSuccessfully created temp file for {modifiedFile.RelativePath}: {tempPatchedFilePath}");
									}
								}
								else
								{
									lock (consoleLock)
									{
										Console.WriteLine($"\tFailed to create temp file for: {modifiedFile.RelativePath}");
									}
									parallelExceptions.Add(new InvalidOperationException($"Patcher.Apply returned null for {modifiedFile.RelativePath}"));
									loopState.Stop();
								}
							}
						}
					}
					else
					{
						lock (consoleLock)
						{
							Console.WriteLine($"\tWarning: Patch data entry not found in ZIP for {modifiedFile.RelativePath}: {modifiedFile.PatchDataEntryName}");
						}
						parallelExceptions.Add(new FileNotFoundException($"Patch data entry not found in ZIP: {modifiedFile.PatchDataEntryName}"));
						loopState.Stop();
					}
				});
				if (!parallelExceptions.IsEmpty)
				{
					overallSuccess = false;
					throw new AggregateException("Processing modified files failed.", parallelExceptions);
				}


				// Phase 4: Identify files to delete and add to list.
				// This phase is inherently sequential and identifies files that *will* be deleted.
				Console.WriteLine($"Identifying {manifest.DeletedFiles?.Count ?? 0} files for deletion...");
				if (manifest.DeletedFiles != null)
				{
					foreach (var deletedFile in manifest.DeletedFiles)
					{
						string fullPath = Path.Combine(WorkingDirectory, deletedFile.RelativePath);
						if (File.Exists(fullPath))
						{
							filesToDelete.Add(fullPath);
							Console.WriteLine($"\tQueued for deletion: {deletedFile.RelativePath}");
						}
						else
						{
							Console.WriteLine($"\tWarning: File to delete not found: {deletedFile.RelativePath}");
						}
					}
				}

				// --- Critical Sequential Finalization Phase ---
				// If we reach here, all temp files are created and new files are written.
				// Now we commit the changes. This phase must be atomic.

				// Phase 5: Create backups for modified files that will be replaced.
				// This must happen *before* any original files are deleted or moved.
				Console.WriteLine($"Creating backups for {filesToMove.Count} modified files...");
				foreach (var fileEntry in filesToMove.ToList()) // Operate on a copy if modifying list during iteration
				{
					string targetPath = fileEntry.Item2;
					string backupPath = targetPath + ".bak";
					if (File.Exists(targetPath))
					{
						try
						{
							// Ensure old backup is deleted first
							if (!TryDeleteFile(backupPath, MaxFileOperationRetries))
							{
								throw new IOException($"Failed to delete old backup file '{backupPath}'.");
							}
							File.Copy(targetPath, backupPath, true); // Overwrite if backup exists.
							originalFileBackups[targetPath] = backupPath; // Store for potential rollback.
							Console.WriteLine($"\tBacked up '{targetPath}' to '{backupPath}'");
						}
						catch (Exception ex)
						{
							Console.WriteLine($"ERROR: Failed to backup '{targetPath}': {ex.Message}. Aborting patch for this file.");
							overallSuccess = false;
							// If backup fails, we cannot safely proceed with this file.
							// Remove from filesToMove so it's not processed in later phases.
							filesToMove.RemoveAll(t => t.Item2 == targetPath);
							// Also delete the temp file associated with this target, if it was created
							TryDeleteFile(fileEntry.Item1, MaxFileOperationRetries);
							// Throw to trigger overall rollback
							throw new InvalidOperationException($"Critical failure during backup for {targetPath}.", ex);
						}
					}
					else
					{
						Console.WriteLine($"WARNING: Original file '{targetPath}' not found for backup. Assuming new file or already deleted.");
					}
				}


				// Phase 6: Perform all deletions sequentially.
				Console.WriteLine($"Performing {filesToDelete.Count} sequential deletions...");
				foreach (string pathToDelete in filesToDelete.ToList()) // Operate on a copy
				{
					if (!TryDeleteFile(pathToDelete, MaxFileOperationRetries))
					{
						Console.WriteLine($"ERROR: Failed to delete '{pathToDelete}'. This is a critical failure for transaction integrity.");
						overallSuccess = false;
						throw new IOException($"Failed to delete critical file: {pathToDelete}"); // Trigger overall rollback
					}
				}

				// Phase 7: Perform all file moves (replacements) sequentially.
				Console.WriteLine($"Performing {filesToMove.Count} sequential file moves...");
				foreach (var fileEntry in filesToMove.ToList()) // Operate on a copy
				{
					string tempPath = fileEntry.Item1;
					string targetPath = fileEntry.Item2;

					if (File.Exists(tempPath))
					{
						// Ensure target file is deleted before moving, in case it wasn't deleted by Phase 6
						// or if it's a modification to a file that wasn't explicitly in 'deleted_files'
						if (File.Exists(targetPath))
						{
							if (!TryDeleteFile(targetPath, MaxFileOperationRetries))
							{
								Console.WriteLine($"ERROR: Failed to delete old target '{targetPath}' before move. This is a critical failure for transaction integrity.");
								overallSuccess = false;
								throw new IOException($"Failed to delete target file before move: {targetPath}"); // Trigger overall rollback
							}
						}

						if (!TryMoveFile(tempPath, targetPath, MaxFileOperationRetries))
						{
							Console.WriteLine($"ERROR: Failed to move '{tempPath}' to '{targetPath}'. This is a critical failure for transaction integrity. Attempting rollback for this file.");
							overallSuccess = false;
							// Attempt to rollback this specific file if move failed
							if (originalFileBackups.TryGetValue(targetPath, out string backupPath) && File.Exists(backupPath))
							{
								if (TryMoveFile(backupPath, targetPath, MaxFileOperationRetries))
								{
									Console.WriteLine($"\tRolled back '{targetPath}' from backup.");
								}
								else
								{
									Console.WriteLine($"\tCRITICAL: Failed to restore '{targetPath}' from backup. Manual intervention may be required.");
								}
							}
							else
							{
								Console.WriteLine($"\tNo backup available or failed to restore for '{targetPath}'. File may be corrupted.");
							}
							throw new IOException($"Failed to move patched file to target: {targetPath}"); // Trigger overall rollback
						}
						else
						{
							Console.WriteLine($"\tFinalized: {targetPath}");
							// Delete backup file if move was successful
							if (originalFileBackups.TryGetValue(targetPath, out string backupPathToDelete))
							{
								TryDeleteFile(backupPathToDelete, MaxFileOperationRetries);
							}
						}
					}
					else
					{
						Console.WriteLine($"WARNING: Temporary patched file '{tempPath}' not found for move to '{targetPath}'. Skipping.");
						// This indicates a previous error, but we log and continue to allow overall rollback to handle.
						overallSuccess = false;
						throw new FileNotFoundException($"Temporary patched file not found: {tempPath}");
					}
				}
			}
			// If we reached here, all operations were attempted. Check overallSuccess.
			if (overallSuccess)
			{
				Console.WriteLine("All patch operations completed successfully.");
			}
			else
			{
				// This path should ideally not be hit if exceptions are thrown as intended above.
				// It's a safeguard.
				throw new InvalidOperationException("Patching process completed with unhandled failures.");
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine($"CRITICAL ERROR during patch application of '{Path.GetFileName(patchFilePath)}': {ex.Message}");
			Console.WriteLine(ex.StackTrace);

			// Overall Rollback: Attempt to restore any files that were backed up.
			Console.WriteLine("Attempting overall rollback due to critical error...");
			foreach (var entry in originalFileBackups)
			{
				string targetPath = entry.Key;
				string backupPath = entry.Value;

				if (File.Exists(backupPath))
				{
					Console.WriteLine($"\tRestoring '{targetPath}' from backup '{backupPath}'...");
					if (File.Exists(targetPath))
					{
						TryDeleteFile(targetPath, MaxFileOperationRetries); // Best effort delete of potentially corrupted file
					}
					if (TryMoveFile(backupPath, targetPath, MaxFileOperationRetries))
					{
						Console.WriteLine($"\tSuccessfully restored '{targetPath}'.");
					}
					else
					{
						Console.WriteLine($"\tFailed to restore '{targetPath}'. Manual intervention may be required.");
					}
				}
			}
			overallSuccess = false; // Ensure final status is failure.
		}
		finally
		{
			// Final cleanup of any remaining temporary files.
			Console.WriteLine("Performing final cleanup of temporary files...");
			foreach (string tempPath in tempFilesCreated)
			{
				if (File.Exists(tempPath))
				{
					TryDeleteFile(tempPath, MaxFileOperationRetries);
				}
			}
		}

		return overallSuccess;
	}

	/// <summary>
	/// Gathers and creates all unique directories required for new and modified files.
	/// </summary>
	/// <param name="manifest">The patch manifest containing file entries.</param>
	/// <param name="parallelExceptions">A ConcurrentBag to collect exceptions from parallel operations.</param>
	private static void PreCreateDirectories(PatchManifest manifest, ConcurrentBag<Exception> parallelExceptions)
	{
		HashSet<string> directoriesToCreate = new HashSet<string>();

		if (manifest.NewFiles != null)
		{
			foreach (var newFile in manifest.NewFiles)
			{
				string fullPath = Path.Combine(WorkingDirectory, newFile.RelativePath);
				string directoryPath = Path.GetDirectoryName(fullPath);
				if (!string.IsNullOrEmpty(directoryPath)) directoriesToCreate.Add(directoryPath);
			}
		}

		if (manifest.ModifiedFiles != null)
		{
			foreach (var modifiedFile in manifest.ModifiedFiles)
			{
				string fullPath = Path.Combine(WorkingDirectory, modifiedFile.RelativePath);
				string directoryPath = Path.GetDirectoryName(fullPath);
				if (!string.IsNullOrEmpty(directoryPath)) directoriesToCreate.Add(directoryPath);
			}
		}

		if (directoriesToCreate.Any())
		{
			Console.WriteLine($"Pre-creating {directoriesToCreate.Count} directories...");
			Parallel.ForEach(directoriesToCreate, (dirPath, loopState) =>
			{
				try
				{
					Directory.CreateDirectory(dirPath);
				}
				catch (Exception ex)
				{
					lock (consoleLock)
					{
						Console.WriteLine($"\tError creating directory {dirPath}: {ex.Message}");
					}
					parallelExceptions.Add(ex);
					loopState.Stop(); // Signal other tasks to stop if possible
				}
			});
		}
	}

	/// <summary>
	/// Computes the XxHash128 hash of a file.
	/// </summary>
	/// <param name="filePath">The path to the file.</param>
	/// <returns>The XxHash128 hash as a lowercase hexadecimal string (32 characters for 128-bit hash).</returns>
	private static string ComputeFileHash(string filePath)
	{
		using (var stream = File.OpenRead(filePath))
		{
			XxHash128 xxHash128 = new XxHash128();

			byte[] buffer = new byte[65536]; // 64KB buffer
			int bytesRead;
			while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
			{
				ReadOnlySpan<byte> dataSpan = new ReadOnlySpan<byte>(buffer, 0, bytesRead);
				xxHash128.Append(dataSpan);
			}

			byte[] hashBytes = xxHash128.GetCurrentHash();
			return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
		}
	}

	/// <summary>
	/// Requests a graceful shutdown of <paramref name="process"/> in a platform-appropriate
	/// way. Returns true if the request was delivered; false if it could not be (in which
	/// case the caller should fall back to a forced kill).
	/// </summary>
	/// <remarks>
	/// <see cref="Process.CloseMainWindow"/> is Windows-only — on Linux and macOS .NET
	/// throws <see cref="PlatformNotSupportedException"/>. The previous implementation
	/// called it unconditionally and swallowed that exception in a broad catch, so on those
	/// platforms the client was never asked to exit and never killed: the updater went on
	/// to patch files underneath a live process and then started a second client instance
	/// alongside the first.
	/// </remarks>
	private static bool TryRequestGracefulExit(Process process)
	{
		try
		{
			if (OperatingSystem.IsWindows())
			{
				return process.CloseMainWindow();
			}

			int result = SysKill(process.Id, SIGTERM);
			if (result != 0)
			{
				Console.WriteLine($"WARNING: kill(SIGTERM) on PID {process.Id} failed with errno {Marshal.GetLastWin32Error()}.");
				return false;
			}
			return true;
		}
		catch (Exception ex)
		{
			Console.WriteLine($"WARNING: Could not request graceful exit for PID {process.Id}: {ex.Message}");
			return false;
		}
	}

	/// <summary>
	/// Attempts to gracefully close and then forcefully kill a process by its PID.
	/// Includes robust waiting for process exit.
	/// </summary>
	/// <param name="pidToKill">The process ID of the launcher process to manage.</param>
	private static void KillLauncherProcess(int pidToKill)
	{
		if (pidToKill <= 0)
		{
			return;
		}

		try
		{
			Process launcherProcess = Process.GetProcessById(pidToKill);
			if (launcherProcess.HasExited)
			{
				Console.WriteLine($"Launcher process with PID {pidToKill} was already exited.");
				return;
			}

			Console.WriteLine($"Attempting to close launcher process with PID {pidToKill} gracefully...");

			if (TryRequestGracefulExit(launcherProcess))
			{
				if (launcherProcess.WaitForExit(GracefulExitTimeoutMs))
				{
					Console.WriteLine($"Launcher process with PID {pidToKill} exited gracefully.");
					Thread.Sleep(PostKillSettleMs);
					return;
				}
				Console.WriteLine($"Launcher process with PID {pidToKill} did not exit within {GracefulExitTimeoutMs}ms, forcing kill...");
			}
			else
			{
				Console.WriteLine($"Graceful exit request could not be delivered to PID {pidToKill}, forcing kill...");
			}

			// Fall through to a forced kill on every path where the graceful request
			// either failed to deliver or was ignored. Patching a live install is far
			// worse than an ungraceful client shutdown.
			launcherProcess.Kill();
			if (launcherProcess.WaitForExit(ForceKillTimeoutMs))
			{
				Console.WriteLine($"Killed launcher process with PID: {pidToKill}");
			}
			else
			{
				Console.WriteLine($"ERROR: Launcher process with PID {pidToKill} is still running after a forced kill. Patching may fail on locked files.");
			}
		}
		catch (ArgumentException)
		{
			Console.WriteLine($"Launcher process with PID {pidToKill} not found (already exited or never started).");
		}
		catch (InvalidOperationException)
		{
			Console.WriteLine($"Launcher process with PID {pidToKill} exited while it was being shut down.");
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Error managing launcher process {pidToKill}: {ex.Message}");
		}

		Thread.Sleep(PostKillSettleMs); // Small delay after attempting to kill the process.
	}

	/// <summary>
	/// Attempts to start the client executable and then exits the patcher.
	/// </summary>
	/// <param name="executable">The name or relative path of the executable to launch.</param>
	/// <param name="pidToKill">The process ID of the launcher process to kill before starting the executable.</param>
	static void TryStartExecutableAndExit(string executable, int pidToKill)
	{
		KillLauncherProcess(pidToKill);

		if (!string.IsNullOrEmpty(executable))
		{
			try
			{
				string fullExecutablePath = Path.Combine(WorkingDirectory, executable);
				ProcessStartInfo startInfo = new ProcessStartInfo(fullExecutablePath)
				{
					WorkingDirectory = Path.GetDirectoryName(fullExecutablePath),
					UseShellExecute = false,
				};
				Process.Start(startInfo);
				Console.WriteLine($"Started executable: {fullExecutablePath}");
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error starting executable '{executable}': {ex.Message}");
			}
		}
		Environment.Exit(0);
	}
}