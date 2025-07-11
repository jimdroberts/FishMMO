using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Security.Cryptography;
using FishMMO.Shared;
using FishMMO.Patcher;

class Program
{
	private static readonly string WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory;
	private static readonly string PatchesDirectory = Path.Combine(WorkingDirectory, "patches");

	private static string LatestVersion;
	private static int PID;
	private static string Executable;
	private static string Version;
	private static Configuration Configuration;

	static void Main(string[] args)
	{
		// Parse command line arguments for version, launcher pid, and exe
		foreach (var arg in args)
		{
			if (arg.StartsWith("-version"))
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

		Console.WriteLine($"Client Patcher started. LatestVersion: {LatestVersion}, Launcher PID: {PID}, Executable: {Executable}");

		// Load current client version
		Configuration = new Configuration(WorkingDirectory);
		if (!Configuration.Load())
		{
			Console.WriteLine("Unable to load client configuration. Assuming version 0.0.0.");
			Version = "0.0.0"; // Default to a very old version if config not found
		}
		else
		{
			if (!Configuration.TryGetString("Version", out Version))
			{
				Console.WriteLine("Version not found in client configuration. Assuming version 0.0.0.");
				Version = "0.0.0";
			}
		}

		Console.WriteLine($"Current Client Version: {Version}");

		if (Version == LatestVersion)
		{
			Console.WriteLine("Client is already up-to-date. Exiting patcher.");
			TryStartExecutableAndExit();
			return;
		}

		// Find relevant patch files
		// Sorting by name should ensure correct application order (e.g., 1.0.0-1.0.1.zip before 1.0.1-1.0.2.zip)
		string[] patchFiles = Directory.GetFiles(PatchesDirectory, "*.zip")
									  .OrderBy(f => f)
									  .ToArray();

		List<string> relevantPatches = new List<string>();
		string currentPatchVersion = Version;

		foreach (string patchFile in patchFiles)
		{
			string fileName = Path.GetFileNameWithoutExtension(patchFile);
			string[] versions = fileName.Split('-'); // Expected format: oldVersion-newVersion

			if (versions.Length == 2)
			{
				string oldVer = versions[0];
				string newVer = versions[1];

				// If this patch starts from our current version, add it to the list
				if (oldVer == currentPatchVersion)
				{
					relevantPatches.Add(patchFile);
					currentPatchVersion = newVer; // Update current version to the new version of this patch
				}

				// If we've reached the target LatestVersion, stop looking for more patches
				if (currentPatchVersion == LatestVersion)
				{
					break;
				}
			}
		}

		if (relevantPatches.Count == 0 && Version != LatestVersion)
		{
			Console.WriteLine("No relevant patch files found to update the client. Exiting.");
			TryStartExecutableAndExit();
			return;
		}

		Console.WriteLine($"Found {relevantPatches.Count} relevant patches to apply:");
		foreach (var patch in relevantPatches)
		{
			Console.WriteLine($"- {Path.GetFileName(patch)}");
		}

		// Apply patches sequentially
		foreach (string patchFilePath in relevantPatches)
		{
			Console.WriteLine($"\nApplying patch: {Path.GetFileName(patchFilePath)}");
			ApplyPatchFile(patchFilePath);

			// After applying, update the current version in the configuration
			// This assumes the patch successfully updated the client to the 'newVersion' of this patch file
			string[] versions = Path.GetFileNameWithoutExtension(patchFilePath).Split('-');
			if (versions.Length == 2)
			{
				Version = versions[1];
				Configuration.Set("Version", Version);
				Configuration.Save();
				Console.WriteLine($"Client version updated to {Version}.");
			}
		}

		Console.WriteLine("\nAll patches applied. Client is up-to-date. Exiting patcher.");
		TryStartExecutableAndExit();
	}

	/// <summary>
	/// Applies a single patch file (ZIP archive) to the client.
	/// </summary>
	/// <param name="patchFilePath">The full path to the patch ZIP file.</param>
	static void ApplyPatchFile(string patchFilePath)
	{
		Patcher patcher = new Patcher();
		try
		{
			using (ZipArchive archive = ZipFile.OpenRead(patchFilePath))
			{
				// Read the manifest from the ZIP archive
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

				// 1. Process Deleted Files
				if (manifest.DeletedFiles != null)
				{
					Console.WriteLine($"Processing {manifest.DeletedFiles.Count} files for deletion...");
					foreach (var deletedFile in manifest.DeletedFiles)
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

				// 2. Process New Files
				if (manifest.NewFiles != null)
				{
					Console.WriteLine($"Processing {manifest.NewFiles.Count} new files...");
					foreach (var newFile in manifest.NewFiles)
					{
						string fullPath = Path.Combine(WorkingDirectory, newFile.RelativePath);
						ZipArchiveEntry newFileZipEntry = archive.GetEntry(newFile.FileDataEntryName);

						if (newFileZipEntry != null)
						{
							try
							{
								// Ensure directory exists for the new file
								string directoryPath = Path.GetDirectoryName(fullPath);
								if (!string.IsNullOrEmpty(directoryPath))
								{
									Directory.CreateDirectory(directoryPath);
								}

								// Stream the new file directly from the ZIP to its destination
								using (Stream sourceStream = newFileZipEntry.Open())
								using (FileStream destinationStream = File.Create(fullPath))
								{
									sourceStream.CopyTo(destinationStream);
								}
								Console.WriteLine($"\tAdded new file: {newFile.RelativePath}");

								string actualHash = ComputeFileHash(fullPath);
								if (actualHash != newFile.NewHash)
								{
									Console.WriteLine($"\tERROR: Hash mismatch for new file {newFile.RelativePath}. Expected {newFile.NewHash}, Got {actualHash}. File might be corrupted or patch is bad. (Deleting file)");
									File.Delete(fullPath); // Optionally delete corrupted file
								}
								else
								{
									Console.WriteLine($"\tHash verified for new file: {newFile.RelativePath}");
								}

							}
							catch (Exception ex)
							{
								Console.WriteLine($"\tError adding new file {newFile.RelativePath}: {ex.Message}");
							}
						}
						else
						{
							Console.WriteLine($"\tWarning: New file entry not found in ZIP: {newFile.FileDataEntryName}");
						}
					}
				}

				// 3. Process Modified Files
				if (manifest.ModifiedFiles != null)
				{
					Console.WriteLine($"Processing {manifest.ModifiedFiles.Count} modified files...");
					foreach (var modifiedFile in manifest.ModifiedFiles)
					{
						string oldFilePath = Path.Combine(WorkingDirectory, modifiedFile.RelativePath);
						ZipArchiveEntry patchDataEntry = archive.GetEntry(modifiedFile.PatchDataEntryName);

						if (patchDataEntry != null)
						{
							Console.WriteLine($"\tApplying patch for modified file: {modifiedFile.RelativePath}");
							// Open the patch data stream from the ZIP and pass it to Patcher.Apply
							using (Stream patchDataStream = patchDataEntry.Open())
							{
								using (BinaryReader reader = new BinaryReader(patchDataStream))
								{
									patcher.Apply(reader, oldFilePath, (success) =>
									{
										if (success)
										{
											Console.WriteLine($"\tSuccessfully patched: {modifiedFile.RelativePath}");

											string actualHash = ComputeFileHash(oldFilePath);
											if (actualHash != modifiedFile.NewHash)
											{
												Console.WriteLine($"\tERROR: Hash mismatch for patched file {modifiedFile.RelativePath}. Expected {modifiedFile.NewHash}, Got {actualHash}. File might be corrupted or patch is bad. (Restore from backup if possible)");
											}
											else
											{
												Console.WriteLine($"\tHash verified for patched file: {modifiedFile.RelativePath}");
											}
										}
										else
										{
											Console.WriteLine($"\tFailed to patch: {modifiedFile.RelativePath}");
										}
									});
								}
							}
						}
						else
						{
							Console.WriteLine($"\tWarning: Patch data entry not found in ZIP for {modifiedFile.RelativePath}: {modifiedFile.PatchDataEntryName}");
						}
					}
				}
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Critical Error during patch application of '{Path.GetFileName(patchFilePath)}': {ex.Message}");
			Console.WriteLine(ex.StackTrace);
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

	static void TryStartExecutableAndExit()
	{
		if (!string.IsNullOrEmpty(Executable))
		{
			try
			{
				ProcessStartInfo startInfo = new ProcessStartInfo(Executable)
				{
					WorkingDirectory = Path.GetDirectoryName(Executable),
					UseShellExecute = true // Use shell execute to allow starting without full path or in case of associated file types
				};
				Process.Start(startInfo);
				Console.WriteLine($"Started executable: {Executable}");
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error starting executable '{Executable}': {ex.Message}");
			}
		}

		if (PID > 0)
		{
			try
			{
				Process launcherProcess = Process.GetProcessById(PID);
				launcherProcess.Kill();
				Console.WriteLine($"Killed launcher process with PID: {PID}");
			}
			catch (ArgumentException)
			{
				Console.WriteLine($"Launcher process with PID {PID} not found or already exited.");
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error killing launcher process {PID}: {ex.Message}");
			}
		}
		Environment.Exit(0);
	}
}