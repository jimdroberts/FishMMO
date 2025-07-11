using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FishMMO.Patcher;
using FishMMO.Logging;

class Program
{
	private static string latestVersion = "";
	private static readonly string loggingConfigName = "logging.json";

	static void Main()
	{
		string workingDirectory = Directory.GetCurrentDirectory();

		// Load Logging Configuration from logging.json
		string configFilePath = Path.Combine(workingDirectory, loggingConfigName);

		// Initialize the Log manager.
		Log.Initialize(configFilePath, new ConsoleFormatter(), null, Log.OnInternalLogMessage);

		Log.Info("Patcher", "Welcome to the Game Client Patch Generator!");
		Log.Info("Patcher", "This tool will create patch files to update older game client versions to the latest version.");
		Log.Info("Patcher", "-------------------------------------------------------------------------------------");

		Console.Write("Enter the FULL path to the directory containing the LATEST game client files (e.g., C:\\GameClient\\LatestVersion): ");
		string latestDirectory = ReadAndTrimQuotes(Console.ReadLine());
		if (!Directory.Exists(latestDirectory))
		{
			Log.Error("Patcher", $"The specified directory '{latestDirectory}' does not exist. Please ensure the path is correct.");
			return;
		}

		// Load latest version using UnityAssetReader
		Log.Info("Patcher", $"Attempting to load latest version from '{latestDirectory}'...");
		VersionConfig latestVersionConfig = UnityAssetReader.GetVersionFromClientDirectory(latestDirectory);
		if (latestVersionConfig == null)
		{
			Log.Error("Patcher", $"Unable to determine latest version from '{latestDirectory}'. Closing...");
			return;
		}
		latestVersion = latestVersionConfig.FullVersion;
		Log.Info("Patcher", $"Latest client version detected: {latestVersion}");

		Console.Write("Enter the FULL path to the directory containing the OLD game client versions. This directory should contain subdirectories, each representing a distinct old version (e.g., C:\\OldGameClients): ");
		string oldRootDirectory = ReadAndTrimQuotes(Console.ReadLine());
		if (!Directory.Exists(oldRootDirectory))
		{
			Log.Error("Patcher", $"The specified directory '{oldRootDirectory}' does not exist. Please ensure the path is correct.");
			return;
		}

		Console.Write("Enter the FULL path to the directory where the generated patch files will be saved (e.g., C:\\GamePatches\\Output): ");
		string patchesDirectory = ReadAndTrimQuotes(Console.ReadLine());
		// Delete the old patches as they should no longer be relevant. We want the shortest path to the latest version only.
		if (Directory.Exists(patchesDirectory))
		{
			Log.Warning("Patcher", $"Deleting existing content in the patch output directory '{patchesDirectory}'.");
			Directory.Delete(patchesDirectory, true);
		}
		// Ensure patch directory exists
		Directory.CreateDirectory(patchesDirectory);

		// Define extensions to ignore (e.g., .cfg, .log, .bak)
		HashSet<string> ignoredExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			".cfg", // Ignore configuration files (less relevant now, but good to keep)
            ".log", // Ignore log files
            ".bak", // Ignore backup files
            // Add any other extensions you want to ignore here
        };
		// Ensure VersionConfig.asset is NOT ignored if it's the only .asset file you care about
		// This is handled implicitly by the VersionConfigFileName constant in UnityAssetReader.
		// If there were other .asset files you wanted to ignore generally, you'd add them here,
		// but for now, it's safer to keep the .asset extension in mind for explicit handling.

		Log.Info("Patcher", "The following file extensions will be ignored during patch generation: " + string.Join(", ", ignoredExtensions));

		PatchGenerator patchGenerator = new PatchGenerator();

		try
		{
			Log.Info("Patcher", "\nStarting patch generation process.");

			// Get all directories under the specified oldRootDirectory
			string[] oldDirectories = Directory.GetDirectories(oldRootDirectory);

			if (oldDirectories.Length == 0)
			{
				Log.Info("Patcher", $"No old client version subdirectories found in '{oldRootDirectory}'. No patches will be generated.");
				return;
			}

			Log.Info("Patcher", $"Found {oldDirectories.Length} old client versions. Generating patches for each:");

			// Use Parallel.ForEach to process old directories concurrently
			// Each CreatePatch call will generate a single compressed ZIP file
			Parallel.ForEach(oldDirectories, (oldDirectory) =>
			{
				CreatePatch(patchGenerator, latestDirectory, oldDirectory, patchesDirectory, ignoredExtensions);
			});

			Log.Info("Patcher", "All patch generation operations complete.");
		}
		catch (Exception ex)
		{
			Log.Error("Patcher", $"An unexpected error occurred during patch generation.", ex);
		}

		Log.Shutdown();
	}

	/// <summary>
	/// Helper method to trim only leading and trailing double quotes from a string.
	/// </summary>
	private static string ReadAndTrimQuotes(string input)
	{
		if (input.StartsWith("\"") && input.EndsWith("\"") && input.Length >= 2)
		{
			return input.Substring(1, input.Length - 2);
		}
		return input;
	}

	public static void CreatePatch(PatchGenerator patchGenerator, string latestDirectory, string oldDirectory, string patchOutputDirectory, HashSet<string> ignoredExtensions)
	{
		// Load old version using UnityAssetReader
		Log.Info("Patcher", $"[{Path.GetFileName(oldDirectory)}] Attempting to load old version from '{oldDirectory}'...");
		VersionConfig oldVersionConfig = UnityAssetReader.GetVersionFromClientDirectory(oldDirectory);
		if (oldVersionConfig == null)
		{
			Log.Info("Patcher", $"[{Path.GetFileName(oldDirectory)}] Unable to determine old version from '{oldDirectory}'. Skipping patch generation for this version.");
			return;
		}
		string oldVersion = oldVersionConfig.FullVersion;
		Log.Info("Patcher", $"[{Path.GetFileName(oldDirectory)}] Old client version detected: {oldVersion}");

		string patchFileName = $"{oldVersion}-{latestVersion}.zip";
		string patchFilePath = Path.Combine(patchOutputDirectory, patchFileName);
		string tempZipFilePath = Path.Combine(patchOutputDirectory, $"temp_{Guid.NewGuid()}.zip"); // Unique temporary zip file

		// List to keep track of temporary patch files that need to be cleaned up
		ConcurrentBag<string> tempPatchFilesToCleanUp = new ConcurrentBag<string>();

		Log.Info("Patcher", $"[{Path.GetFileName(oldDirectory)}] Generating patch: {patchFileName}");

		try
		{
			// Get all files in old and latest directories with their hashes, applying ignoredExtensions
			Log.Info("Patcher", $"[{Path.GetFileName(oldDirectory)}] Scanning Latest Files for hashes (ignoring {ignoredExtensions.Count} extensions)...");
			Dictionary<string, (string relativePath, string hash)> latestFilesWithHashes = GetAllFilesWithHashes(latestDirectory, ignoredExtensions);
			HashSet<string> latestFileRelativePaths = latestFilesWithHashes.Keys.ToHashSet();

			Log.Info("Patcher", $"[{Path.GetFileName(oldDirectory)}] Scanning Old Files for hashes (ignoring {ignoredExtensions.Count} extensions)...");
			Dictionary<string, (string relativePath, string hash)> oldFilesWithHashes = GetAllFilesWithHashes(oldDirectory, ignoredExtensions);
			HashSet<string> oldFileRelativePaths = oldFilesWithHashes.Keys.ToHashSet();

			// Determine deleted, new, and modified files based on hashes
			List<string> filesToDelete = oldFileRelativePaths.Except(latestFileRelativePaths).ToList();
			List<string> filesToAdd = latestFileRelativePaths.Except(oldFileRelativePaths).ToList();
			List<string> filesToCompare = oldFileRelativePaths.Intersect(latestFileRelativePaths).ToList();

			ConcurrentBag<ModifiedFileEntry> modifiedFilesData = new ConcurrentBag<ModifiedFileEntry>();
			ConcurrentBag<NewFileEntry> newFilesData = new ConcurrentBag<NewFileEntry>();
			ConcurrentBag<DeletedFileEntry> deletedFilesData = new ConcurrentBag<DeletedFileEntry>();

			// 1. Process Deleted Files
			foreach (var deletedFilePath in filesToDelete)
			{
				deletedFilesData.Add(new DeletedFileEntry { RelativePath = deletedFilePath });
				Log.Info("Patcher", $"[{Path.GetFileName(oldDirectory)}] \tIdentified for deletion: {deletedFilePath}");
			}

			// 2. Process Modified Files in parallel
			Parallel.ForEach(filesToCompare, (comparedRelativePath) =>
			{
				// Only generate patch if hash differs
				if (oldFilesWithHashes[comparedRelativePath].hash != latestFilesWithHashes[comparedRelativePath].hash)
				{
					string oldFullFilePath = Path.Combine(oldDirectory, comparedRelativePath);
					string newFullFilePath = Path.Combine(latestDirectory, comparedRelativePath);

					byte[] patchDataBytes = patchGenerator.Generate(oldFullFilePath, newFullFilePath);

					if (patchDataBytes.Length > 0)
					{
						string tempPatchFile = Path.Combine(Path.GetTempPath(), $"patch_{Guid.NewGuid()}.bin");
						try
						{
							File.WriteAllBytes(tempPatchFile, patchDataBytes);
							tempPatchFilesToCleanUp.Add(tempPatchFile);

							modifiedFilesData.Add(new ModifiedFileEntry
							{
								RelativePath = comparedRelativePath,
								OldHash = oldFilesWithHashes[comparedRelativePath].hash,
								NewHash = latestFilesWithHashes[comparedRelativePath].hash,
								PatchDataEntryName = $"patches/{comparedRelativePath.Replace('\\', '/')}.bin",
								TempPatchFilePath = tempPatchFile
							});
							Log.Info("Patcher", $"[{Path.GetFileName(oldDirectory)}] \tGenerated patch for modified: {comparedRelativePath} (written to temp file)");
						}
						catch (Exception ex)
						{
							Log.Info("Patcher", $"[{Path.GetFileName(oldDirectory)}] \tError writing patch for {comparedRelativePath} to temp file: {ex.Message}");
						}
					}
					else
					{
						Log.Info("Patcher", $"[{Path.GetFileName(oldDirectory)}] \tFiles are identical, no patch generated for: {comparedRelativePath}");
					}
				}
			});

			// 3. Process New Files
			Parallel.ForEach(filesToAdd, (newRelativePath) =>
			{
				newFilesData.Add(new NewFileEntry
				{
					RelativePath = newRelativePath,
					NewHash = latestFilesWithHashes[newRelativePath].hash,
					FileDataEntryName = $"new_files/{newRelativePath.Replace('\\', '/')}"
				});
				Log.Info("Patcher", $"[{Path.GetFileName(oldDirectory)}] \tPrepared metadata for new file: {newRelativePath}");
			});

			// 4. Assemble the PatchManifest
			PatchManifest manifest = new PatchManifest
			{
				OldVersion = oldVersion,
				NewVersion = latestVersion,
				DeletedFiles = deletedFilesData.ToList(),
				ModifiedFiles = modifiedFilesData.ToList(),
				NewFiles = newFilesData.ToList()
			};

			// Serialize Manifest to JSON
			var options = new JsonSerializerOptions { WriteIndented = true };
			string manifestJson = JsonSerializer.Serialize(manifest, options);
			byte[] manifestBytes = Encoding.UTF8.GetBytes(manifestJson);

			// 5. Create the ZIP Archive
			using (FileStream zipStream = File.Create(tempZipFilePath))
			{
				using (ZipArchive archive = new ZipArchive(zipStream, ZipArchiveMode.Create, true))
				{
					// Add Manifest to ZIP
					ZipArchiveEntry manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
					using (Stream entryStream = manifestEntry.Open())
					{
						entryStream.Write(manifestBytes, 0, manifestBytes.Length);
					}
					Log.Info("Patcher", $"[{Path.GetFileName(oldDirectory)}] \tAdded manifest.json to ZIP.");

					// Add Modified File Patches to ZIP by streaming from temporary files
					foreach (var entry in modifiedFilesData)
					{
						if (!string.IsNullOrEmpty(entry.TempPatchFilePath) && File.Exists(entry.TempPatchFilePath))
						{
							ZipArchiveEntry patchEntry = archive.CreateEntry(entry.PatchDataEntryName, CompressionLevel.Optimal);
							using (Stream entryStream = patchEntry.Open())
							using (FileStream sourcePatchStream = File.OpenRead(entry.TempPatchFilePath))
							{
								sourcePatchStream.CopyTo(entryStream);
							}
							Log.Info("Patcher", $"[{Path.GetFileName(oldDirectory)}] \tAdded patch data for {entry.RelativePath} to ZIP by streaming from temp file.");
						}
						else
						{
							Log.Warning("Patcher", $"[{Path.GetFileName(oldDirectory)}] \tTemporary patch file not found for {entry.RelativePath}, skipping ZIP addition.");
						}
					}

					// Add New Files to ZIP by streaming directly from disk
					foreach (var entry in newFilesData)
					{
						string fullFilePath = Path.Combine(latestDirectory, entry.RelativePath);
						if (File.Exists(fullFilePath))
						{
							ZipArchiveEntry newFileEntry = archive.CreateEntry(entry.FileDataEntryName, CompressionLevel.Optimal);
							using (Stream entryStream = newFileEntry.Open())
							using (FileStream sourceFileStream = File.OpenRead(fullFilePath))
							{
								sourceFileStream.CopyTo(entryStream);
							}
							Log.Info("Patcher", $"[{Path.GetFileName(oldDirectory)}] \tAdded new file data for {entry.RelativePath} to ZIP by streaming.");
						}
						else
						{
							Log.Warning("Patcher", $"[{Path.GetFileName(oldDirectory)}] \tNew file not found at {fullFilePath}, skipping ZIP addition.");
						}
					}
				}
			}

			// Move the temporary file to its final destination
			File.Move(tempZipFilePath, patchFilePath);
			Log.Info("Patcher", $"[{Path.GetFileName(oldDirectory)}] Patch build completed: {patchFileName}");
		}
		catch (Exception ex)
		{
			Log.Info("Patcher", $"[{Path.GetFileName(oldDirectory)}] Error generating patch for '{oldDirectory}': {ex.Message}");
			Log.Info("Patcher", ex.StackTrace);
			// Clean up temporary patch file in case of error
			if (File.Exists(tempZipFilePath))
			{
				File.Delete(tempZipFilePath);
			}
		}
		finally
		{
			// Clean up all temporary patch files generated for modified files
			foreach (string tempFile in tempPatchFilesToCleanUp)
			{
				try
				{
					if (File.Exists(tempFile))
					{
						File.Delete(tempFile);
						Log.Info("Patcher", $"[{Path.GetFileName(oldDirectory)}] \tCleaned up temporary patch file: {tempFile}");
					}
				}
				catch (Exception ex)
				{
					Log.Info("Patcher", $"[{Path.GetFileName(oldDirectory)}] \tError cleaning up temporary file {tempFile}: {ex.Message}");
				}
			}
		}
	}

	/// <summary>
	/// This method is no longer used for adding 'new files' to the ZIP, as they are now streamed directly.
	/// It remains in the code to satisfy existing references if any, or for other potential uses (e.g., if PatchGenerator.Generate
	/// were to produce full file content instead of diffs and those needed temporary memory storage).
	/// </summary>
	public static byte[] StreamFileChunksIntoMemory(string sourceFilePath)
	{
		try
		{
			using (FileStream sourceFileStream = File.OpenRead(sourceFilePath))
			{
				using (MemoryStream memoryStream = new MemoryStream())
				{
					sourceFileStream.CopyTo(memoryStream);
					return memoryStream.ToArray();
				}
			}
		}
		catch (Exception ex)
		{
			Log.Info("Patcher", $"Error streaming file chunks from '{sourceFilePath}' to memory: {ex.Message}");
			throw; // Propagate the exception
		}
	}

	/// <summary>
	/// Gets all files recursively inside a root directory and trims the root directory path, also computes their SHA256 hashes.
	/// Files with extensions present in the ignoredExtensions set will be skipped.
	/// </summary>
	public static Dictionary<string, (string relativePath, string hash)> GetAllFilesWithHashes(string rootDirectory, HashSet<string> ignoredExtensions)
	{
		Dictionary<string, (string relativePath, string hash)> filesWithHashes = new Dictionary<string, (string, string)>();
		Stack<string> directories = new Stack<string>();

		// Start with the root directory
		directories.Push(rootDirectory);

		using (var sha256 = SHA256.Create())
		{
			while (directories.Count > 0)
			{
				string currentDir = directories.Pop();

				// Get files in the current directory
				try
				{
					string[] currentFiles = Directory.GetFiles(currentDir);
					foreach (string file in currentFiles)
					{
						string extension = Path.GetExtension(file);
						// Skip files if their extension is in the ignoredExtensions set
						if (ignoredExtensions.Contains(extension))
						{
							Log.Info("Patcher", $"\tSkipping ignored file: {Path.GetFileName(file)} (.{extension})");
							continue;
						}

						// IMPORTANT: Do NOT ignore VersionConfig.asset itself during hash calculation,
						// as it's the source of truth for versioning. The UnityAssetReader will find it.
						// However, if it's explicitly added to ignoredExtensions, the file will be skipped
						// for hashing, which might be an issue if it changes beyond just version number.
						// For this specific case, it's fine if the VersionConfig.asset itself
						// isn't included in the patch content unless its hash changes (e.g., if you added a new field).
						// It's mainly used for *determining* the versions.

						string relativePath = file.Substring(rootDirectory.Length);
						if (Path.IsPathRooted(relativePath))
						{
							relativePath = relativePath.TrimStart(Path.DirectorySeparatorChar);
							relativePath = relativePath.TrimStart(Path.AltDirectorySeparatorChar);
						}
						// Normalize path separators for consistency, especially for manifest
						relativePath = relativePath.Replace('\\', '/');

						string fileHash = ComputeFileHash(file, sha256);
						filesWithHashes.Add(relativePath, (relativePath, fileHash));
					}
				}
				catch (UnauthorizedAccessException)
				{
					Log.Warning("Patcher", $"Access to directory '{currentDir}' is denied. Skipping.");
					continue;
				}
				catch (DirectoryNotFoundException)
				{
					Log.Warning("Patcher", $"Directory '{currentDir}' not found. Skipping.");
					continue;
				}
				catch (IOException ex)
				{
					Log.Warning("Patcher", $"IO error accessing files in '{currentDir}': {ex.Message}. Skipping.");
					continue;
				}

				// Get subdirectories and push them to the stack
				try
				{
					string[] subdirectories = Directory.GetDirectories(currentDir);
					foreach (string subdir in subdirectories)
					{
						directories.Push(subdir);
					}
				}
				catch (UnauthorizedAccessException)
				{
					Log.Warning("Patcher", $"Access to subdirectory '{currentDir}' is denied. Skipping.");
					continue;
				}
				catch (DirectoryNotFoundException)
				{
					Log.Warning("Patcher", $"Subdirectory '{currentDir}' not found. Skipping.");
					continue;
				}
			}
		}
		return filesWithHashes;
	}

	/// <summary>
	/// Computes the SHA256 hash of a file.
	/// </summary>
	private static string ComputeFileHash(string filePath, SHA256 sha256)
	{
		using (var stream = File.OpenRead(filePath))
		{
			byte[] hashBytes = sha256.ComputeHash(stream);
			return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
		}
	}

	/// <summary>
	/// Returns a list of matching relative file paths.
	/// This method is modified to only find the intersection, as `GetAllFilesWithHashes` now returns all files,
	/// and the subsequent logic uses `Except` and `Intersect` on the file paths to determine differences.
	/// </summary>
	static List<string> MatchFiles(HashSet<string> oldFiles, HashSet<string> latestFiles)
	{
		// This method is now effectively redundant in the new flow,
		// as the Intersection logic is handled directly in CreatePatch.
		// Keeping it for now, but it's not strictly needed for the new approach.
		return oldFiles.Intersect(latestFiles).ToList();
	}
}