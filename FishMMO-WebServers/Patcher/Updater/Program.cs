using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Security.Cryptography;
using System.Threading.Tasks;
using FishMMO.Patcher;

class Program
{
	private static readonly string WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory;
	private static readonly string PatchesDirectory = Path.Combine(WorkingDirectory, "patches");

	private static string Version;
	private static string LatestVersion;
	private static int PID;
	private static string Executable;

	static void Main(string[] args)
	{
		// Parse command line arguments for Version, Latest Version, Client Launcher PID, and Client Launcher Executable
		foreach (var arg in args)
		{
			if (arg.StartsWith("-version"))
			{
				var splitArg = arg.Split('=');
				if (splitArg.Length == 2)
				{
					Version = splitArg[1];
				}
			}
			if (arg.StartsWith("-latestversion"))
			{
				var splitArg = arg.Split('=');
				if (splitArg.Length == 2)
				{
					LatestVersion = splitArg[1];
				}
			}
			else if (arg.StartsWith("-pid"))
			{
				var splitArg = arg.Split('=');
				if (splitArg.Length == 2 &&
					int.TryParse(splitArg[1], out int pid))
				{
					PID = pid;
				}
			}
			else if (arg.StartsWith("-exe"))
			{
				var splitArg = arg.Split('=');
				if (splitArg.Length == 2)
				{
					Executable = splitArg[1];
				}
			}
		}

		Console.WriteLine($"Client Patcher started. Current Client Version: {Version}, LatestVersion: {LatestVersion}, Launcher PID: {PID}, Executable: {Executable}");

		// Attempt to kill the launcher process immediately after parsing arguments
		// This ensures the old launcher is terminated before any file operations for patching begin.
		KillLauncherProcess(PID);

		if (Version == LatestVersion)
		{
			Console.WriteLine("Client is already up-to-date. Exiting patcher.");
			TryStartExecutableAndExit(Executable, PID);
			return;
		}

		string expectedPatchFileName = $"{Version}-{LatestVersion}.zip";
		string patchFilePath = Path.Combine(PatchesDirectory, expectedPatchFileName);

		if (!File.Exists(patchFilePath))
		{
			Console.WriteLine($"Error: Expected patch file '{expectedPatchFileName}' not found in '{PatchesDirectory}'. Cannot update client.");
			TryStartExecutableAndExit(Executable, PID);
			return;
		}

		Console.WriteLine($"\nApplying patch: {Path.GetFileName(patchFilePath)}");
		ApplyPatchFile(patchFilePath);

		Console.WriteLine("\nPatch applied. Client is up-to-date. Exiting patcher.");
		TryStartExecutableAndExit(Executable, PID);
	}

	/// <summary>
	/// Applies a single patch file (ZIP archive) to the client.
	/// </summary>
	/// <param name="patchFilePath">The full path to the patch ZIP file.</param>
	static void ApplyPatchFile(string patchFilePath)
	{
		try
		{
			using (ZipArchive archive = ZipFile.OpenRead(patchFilePath))
			{
				ZipArchiveEntry manifestEntry = archive.GetEntry("manifest.json");
				if (manifestEntry == null)
				{
					Console.WriteLine($"Error: manifest.json not found in patch file '{Path.GetFileName(patchFilePath)}'. Skipping.");
					return;
				}

				PatchManifest manifest;
				using (Stream manifestStream = manifestEntry.Open())
				{
					manifest = JsonSerializer.Deserialize<PatchManifest>(manifestStream);
				}
				Console.WriteLine($"Loaded manifest from {Path.GetFileName(patchFilePath)}. Old Version: {manifest.OldVersion}, New Version: {manifest.NewVersion}");

				// Pre-create all necessary directories before parallel file processing
				PreCreateDirectories(manifest);

				// Process files
				ProcessDeletedFiles(manifest.DeletedFiles);
				ProcessNewFiles(archive, manifest.NewFiles);
				ProcessModifiedFiles(archive, manifest.ModifiedFiles);
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Critical Error during patch application of '{Path.GetFileName(patchFilePath)}': {ex.Message}");
			Console.WriteLine(ex.StackTrace);
		}
	}

	/// <summary>
	/// Gathers and creates all unique directories required for new and modified files.
	/// </summary>
	/// <param name="manifest">The patch manifest containing file entries.</param>
	private static void PreCreateDirectories(PatchManifest manifest)
	{
		HashSet<string> directoriesToCreate = new HashSet<string>();

		if (manifest.NewFiles != null)
		{
			foreach (var newFile in manifest.NewFiles)
			{
				string fullPath = Path.Combine(WorkingDirectory, newFile.RelativePath);
				string directoryPath = Path.GetDirectoryName(fullPath);
				if (!string.IsNullOrEmpty(directoryPath))
				{
					directoriesToCreate.Add(directoryPath);
				}
			}
		}

		if (manifest.ModifiedFiles != null)
		{
			foreach (var modifiedFile in manifest.ModifiedFiles)
			{
				string fullPath = Path.Combine(WorkingDirectory, modifiedFile.RelativePath);
				string directoryPath = Path.GetDirectoryName(fullPath);
				if (!string.IsNullOrEmpty(directoryPath))
				{
					directoriesToCreate.Add(directoryPath);
				}
			}
		}

		if (directoriesToCreate.Count > 0)
		{
			Console.WriteLine($"Pre-creating {directoriesToCreate.Count} directories...");
			Parallel.ForEach(directoriesToCreate, dirPath =>
			{
				try
				{
					Directory.CreateDirectory(dirPath);
				}
				catch (Exception ex)
				{
					lock (Console.Out)
					{
						Console.WriteLine($"\tError creating directory {dirPath}: {ex.Message}");
					}
				}
			});
		}
	}

	/// <summary>
	/// Processes files marked for deletion.
	/// </summary>
	/// <param name="deletedFiles">List of files to delete.</param>
	private static void ProcessDeletedFiles(List<DeletedFileEntry> deletedFiles)
	{
		if (deletedFiles != null)
		{
			Console.WriteLine($"Processing {deletedFiles.Count} files for deletion...");
			foreach (var deletedFile in deletedFiles) // Keeping sequential for simplicity as often very fast
			{
				string fullPath = Path.Combine(WorkingDirectory, deletedFile.RelativePath);
				if (File.Exists(fullPath))
				{
					try
					{
						File.Delete(fullPath);
						Console.WriteLine($"\tDeleted: {deletedFile.RelativePath}");
					}
					catch (Exception ex)
					{
						Console.WriteLine($"\tError deleting {deletedFile.RelativePath}: {ex.Message}");
					}
				}
				else
				{
					Console.WriteLine($"\tWarning: File to delete not found: {deletedFile.RelativePath}");
				}
			}
		}
	}

	/// <summary>
	/// Processes new files, adding them to the working directory.
	/// </summary>
	/// <param name="archive">The ZIP archive containing the patch data.</param>
	/// <param name="newFiles">List of new files to add.</param>
	private static void ProcessNewFiles(ZipArchive archive, List<NewFileEntry> newFiles)
	{
		if (newFiles != null)
		{
			Console.WriteLine($"Processing {newFiles.Count} new files...");
			Parallel.ForEach(newFiles, newFile =>
			{
				string fullPath = Path.Combine(WorkingDirectory, newFile.RelativePath);
				ZipArchiveEntry newFileZipEntry = archive.GetEntry(newFile.FileDataEntryName);

				if (newFileZipEntry != null)
				{
					try
					{
						// Directory.CreateDirectory is removed here as directories are pre-created
						using (Stream sourceStream = newFileZipEntry.Open())
						using (FileStream destinationStream = File.Create(fullPath))
						{
							sourceStream.CopyTo(destinationStream);
						}
						lock (Console.Out) // Lock for safe console output
						{
							Console.WriteLine($"\tAdded new file: {newFile.RelativePath}");
						}

						string actualHash = ComputeFileHash(fullPath);
						if (actualHash != newFile.NewHash)
						{
							lock (Console.Out) // Lock for safe console output
							{
								Console.WriteLine($"\tERROR: Hash mismatch for new file {newFile.RelativePath}. Expected {newFile.NewHash}, Got {actualHash}. File might be corrupted or patch is bad. (Deleting file)");
							}
							File.Delete(fullPath);
						}
						else
						{
							lock (Console.Out) // Lock for safe console output
							{
								Console.WriteLine($"\tHash verified for new file: {newFile.RelativePath}");
							}
						}
					}
					catch (Exception ex)
					{
						lock (Console.Out) // Lock for safe console output
						{
							Console.WriteLine($"\tError adding new file {newFile.RelativePath}: {ex.Message}");
						}
					}
				}
				else
				{
					lock (Console.Out) // Lock for safe console output
					{
						Console.WriteLine($"\tWarning: New file entry not found in ZIP: {newFile.FileDataEntryName}");
					}
				}
			});
		}
	}

	/// <summary>
	/// Processes modified files, applying binary patches to them.
	/// </summary>
	/// <param name="archive">The ZIP archive containing the patch data.</param>
	/// <param name="modifiedFiles">List of modified files to patch.</param>
	private static void ProcessModifiedFiles(ZipArchive archive, List<ModifiedFileEntry> modifiedFiles)
	{
		if (modifiedFiles != null)
		{
			Console.WriteLine($"Processing {modifiedFiles.Count} modified files...");
			Parallel.ForEach(modifiedFiles, modifiedFile =>
			{
				// Each parallel task gets its own Patcher instance
				Patcher patcher = new Patcher();
				string oldFilePath = Path.Combine(WorkingDirectory, modifiedFile.RelativePath);
				ZipArchiveEntry patchDataEntry = archive.GetEntry(modifiedFile.PatchDataEntryName);

				if (patchDataEntry != null)
				{
					lock (Console.Out) // Lock for safe console output
					{
						Console.WriteLine($"\tApplying patch for modified file: {modifiedFile.RelativePath}");
					}
					using (Stream patchDataStream = patchDataEntry.Open())
					{
						using (BinaryReader reader = new BinaryReader(patchDataStream))
						{
							patcher.Apply(reader, oldFilePath, (success) =>
							{
								if (success)
								{
									lock (Console.Out) // Lock for safe console output
									{
										Console.WriteLine($"\tSuccessfully patched: {modifiedFile.RelativePath}");
									}

									string actualHash = ComputeFileHash(oldFilePath);
									if (actualHash != modifiedFile.NewHash)
									{
										lock (Console.Out) // Lock for safe console output
										{
											Console.WriteLine($"\tERROR: Hash mismatch for patched file {modifiedFile.RelativePath}. Expected {modifiedFile.NewHash}, Got {actualHash}. File might be corrupted or patch is bad. (Restore from backup if possible)");
										}
									}
									else
									{
										lock (Console.Out) // Lock for safe console output
										{
											Console.WriteLine($"\tHash verified for patched file: {modifiedFile.RelativePath}");
										}
									}
								}
								else
								{
									lock (Console.Out) // Lock for safe console output
									{
										Console.WriteLine($"\tFailed to patch: {modifiedFile.RelativePath}");
									}
								}
							});
						}
					}
				}
				else
				{
					lock (Console.Out) // Lock for safe console output
					{
						Console.WriteLine($"\tWarning: Patch data entry not found in ZIP for {modifiedFile.RelativePath}: {modifiedFile.PatchDataEntryName}");
					}
				}
			});
		}
	}

	/// <summary>
	/// Computes the SHA256 hash of a file.
	/// </summary>
	private static string ComputeFileHash(string filePath)
	{
		if (!File.Exists(filePath))
		{
			// For a missing file, return null or an empty string, or throw a specific exception.
			// Depending on desired error handling, null is usually fine for "not found".
			return null;
		}
		using (var sha256 = SHA256.Create())
		{
			using (var stream = File.OpenRead(filePath))
			{
				byte[] hashBytes = sha256.ComputeHash(stream);
				return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
			}
		}
	}

	/// <summary>
	/// Attempts to gracefully close and then forcefully kill a process by its PID.
	/// </summary>
	/// <param name="pidToKill">The process ID of the launcher process to manage.</param>
	private static void KillLauncherProcess(int pidToKill)
	{
		if (pidToKill > 0)
		{
			try
			{
				Process launcherProcess = Process.GetProcessById(pidToKill);
				if (!launcherProcess.HasExited)
				{
					Console.WriteLine($"Attempting to close launcher process with PID {pidToKill} gracefully...");
					// Attempt graceful close first
					if (launcherProcess.CloseMainWindow())
					{
						Console.WriteLine($"Launcher process with PID {pidToKill} main window closed. Waiting for exit...");
						if (!launcherProcess.WaitForExit(5000)) // Wait up to 5 seconds for it to exit after close request
						{
							Console.WriteLine($"Launcher process with PID {pidToKill} did not exit after main window close, forcing kill...");
							launcherProcess.Kill();
							launcherProcess.WaitForExit(); // Wait for the process to actually terminate after killing
							Console.WriteLine($"Killed launcher process with PID: {pidToKill}");
						}
						else
						{
							Console.WriteLine($"Launcher process with PID {pidToKill} exited gracefully after main window closed.");
						}
					}
					else if (!launcherProcess.WaitForExit(5000)) // If no main window or graceful close failed, try waiting for a short period
					{
						Console.WriteLine($"Launcher process with PID {pidToKill} did not exit gracefully, forcing kill...");
						launcherProcess.Kill(); // Force kill if it didn't exit
						launcherProcess.WaitForExit(); // Wait for the process to actually terminate after killing
						Console.WriteLine($"Killed launcher process with PID: {pidToKill}");
					}
					else
					{
						Console.WriteLine($"Launcher process with PID {pidToKill} exited gracefully.");
					}
				}
				else
				{
					Console.WriteLine($"Launcher process with PID {pidToKill} was already exited.");
				}
			}
			catch (ArgumentException)
			{
				// Process not found, likely already exited or never existed.
				Console.WriteLine($"Launcher process with PID {pidToKill} not found (already exited or never started).");
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error managing launcher process {pidToKill}: {ex.Message}");
			}
		}
	}

	/// <summary>
	/// Attempts to start the client executable and then exits the patcher.
	/// It also ensures the old launcher process is terminated.
	/// </summary>
	/// <param name="executable">The name or relative path of the executable to launch.</param>
	/// <param name="pidToKill">The process ID of the launcher process to kill before starting the executable.</param>
	static void TryStartExecutableAndExit(string executable, int pidToKill)
	{
		// First, kill the old process if it's still running (as a final failsafe)
		KillLauncherProcess(pidToKill);

		if (!string.IsNullOrEmpty(executable))
		{
			try
			{
				// Ensure the executable path is combined with the working directory
				// to correctly resolve its location relative to the patcher.
				string fullExecutablePath = Path.Combine(WorkingDirectory, executable);

				ProcessStartInfo startInfo = new ProcessStartInfo(fullExecutablePath)
				{
					WorkingDirectory = Path.GetDirectoryName(fullExecutablePath), // Set working directory for the launched process
					UseShellExecute = false, // Use false for direct execution, better control
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