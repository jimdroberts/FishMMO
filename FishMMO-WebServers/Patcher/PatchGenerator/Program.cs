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
using FishMMO.Shared; // Assuming this namespace exists for Configuration
using FishMMO.Patcher; // Using the namespace for the new PatchMetadata classes

class Program
{
	static void Main()
	{
		Console.WriteLine("Welcome to the Game Client Patch Generator!");
		Console.WriteLine("This tool will create patch files to update older game client versions to the latest version.");
		Console.WriteLine("-------------------------------------------------------------------------------------");

		Console.Write("Enter the FULL path to the directory containing the LATEST game client files (e.g., C:\\GameClient\\LatestVersion): ");
		string latestDirectory = ReadAndTrimQuotes(Console.ReadLine());
		if (!Directory.Exists(latestDirectory))
		{
			Console.WriteLine($"Error: The specified directory '{latestDirectory}' does not exist. Please ensure the path is correct.");
			return;
		}

		Console.Write("Enter the FULL path to the directory containing the OLD game client versions. This directory should contain subdirectories, each representing a distinct old version (e.g., C:\\OldGameClients): ");
		string oldRootDirectory = ReadAndTrimQuotes(Console.ReadLine());
		if (!Directory.Exists(oldRootDirectory))
		{
			Console.WriteLine($"Error: The specified directory '{oldRootDirectory}' does not exist. Please ensure the path is correct.");
			return;
		}

		Console.Write("Enter the FULL path to the directory where the generated patch files will be saved (e.g., C:\\GamePatches\\Output): ");
		string patchesDirectory = ReadAndTrimQuotes(Console.ReadLine());
		// Delete the old patches as they should no longer be relevant. We want the shortest path to the latest version only.
		if (Directory.Exists(patchesDirectory))
		{
			Console.WriteLine($"Warning: Deleting existing content in the patch output directory '{patchesDirectory}'.");
			Directory.Delete(patchesDirectory, true);
		}
		// Ensure patch directory exists
		Directory.CreateDirectory(patchesDirectory);

		// Define extensions to ignore (e.g., .cfg, .log, .bak)
		HashSet<string> ignoredExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			".cfg", // Ignore configuration files
            ".log", // Ignore log files
            ".bak", // Ignore backup files
            // Add any other extensions you want to ignore here
        };
		Console.WriteLine("The following file extensions will be ignored during patch generation: " + string.Join(", ", ignoredExtensions));

		PatchGenerator patchGenerator = new PatchGenerator();

		try
		{
			Console.WriteLine("\nStarting patch generation process.");

			// Get all directories under the specified oldRootDirectory
			string[] oldDirectories = Directory.GetDirectories(oldRootDirectory);

			if (oldDirectories.Length == 0)
			{
				Console.WriteLine($"No old client version subdirectories found in '{oldRootDirectory}'. No patches will be generated.");
				return;
			}

			Console.WriteLine($"Found {oldDirectories.Length} old client versions. Generating patches for each:");

			// Use Parallel.ForEach to process old directories concurrently
			// Each CreatePatch call will generate a single compressed ZIP file
			Parallel.ForEach(oldDirectories, (oldDirectory) =>
			{
				CreatePatch(patchGenerator, latestDirectory, oldDirectory, patchesDirectory, ignoredExtensions);
				Console.WriteLine(); // Add a newline for better readability between patch generations
			});

			Console.WriteLine("All patch generation operations complete.");
		}
		catch (Exception ex)
		{
			Console.WriteLine($"An unexpected error occurred during patch generation: {ex.Message}");
			Console.WriteLine(ex.StackTrace); // Provide stack trace for debugging
		}
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
		// Load latest configuration
		Configuration latestConfiguration = new Configuration(latestDirectory);
		if (!latestConfiguration.Load())
		{
			Console.WriteLine($"[{Path.GetFileName(oldDirectory)}] Unable to load latest configuration file from '{latestDirectory}'. Skipping patch generation for this version.");
			return;
		}
		if (!latestConfiguration.TryGetString("Version", out string latestVersion) ||
			string.IsNullOrWhiteSpace(latestVersion))
		{
			Console.WriteLine($"[{Path.GetFileName(oldDirectory)}] Unable to load 'Version' data from latest configuration at '{latestDirectory}'. Skipping patch generation for this version.");
			return;
		}

		// Load old configuration
		Configuration oldConfiguration = new Configuration(oldDirectory);
		if (!oldConfiguration.Load())
		{
			Console.WriteLine($"[{Path.GetFileName(oldDirectory)}] Unable to load old configuration file from '{oldDirectory}'. Skipping patch generation for this version.");
			return;
		}
		if (!oldConfiguration.TryGetString("Version", out string oldVersion) ||
			string.IsNullOrWhiteSpace(oldVersion))
		{
			Console.WriteLine($"[{Path.GetFileName(oldDirectory)}] Unable to load 'Version' data from old configuration at '{oldDirectory}'. Skipping patch generation for this version.");
			return;
		}

		string patchFileName = $"{oldVersion}-{latestVersion}.zip";
		string patchFilePath = Path.Combine(patchOutputDirectory, patchFileName);
		string tempZipFilePath = Path.Combine(patchOutputDirectory, $"temp_{Guid.NewGuid()}.zip"); // Unique temporary zip file

		// List to keep track of temporary patch files that need to be cleaned up
		ConcurrentBag<string> tempPatchFilesToCleanUp = new ConcurrentBag<string>();

		Console.WriteLine($"[{Path.GetFileName(oldDirectory)}] Generating patch: {patchFileName}");

		try
		{
			// Get all files in old and latest directories with their hashes, applying ignoredExtensions
			Console.WriteLine($"[{Path.GetFileName(oldDirectory)}] Scanning Latest Files for hashes (ignoring {ignoredExtensions.Count} extensions)...");
			Dictionary<string, (string relativePath, string hash)> latestFilesWithHashes = GetAllFilesWithHashes(latestDirectory, ignoredExtensions);
			HashSet<string> latestFileRelativePaths = latestFilesWithHashes.Keys.ToHashSet();

			Console.WriteLine($"[{Path.GetFileName(oldDirectory)}] Scanning Old Files for hashes (ignoring {ignoredExtensions.Count} extensions)...");
			Dictionary<string, (string relativePath, string hash)> oldFilesWithHashes = GetAllFilesWithHashes(oldDirectory, ignoredExtensions);
			HashSet<string> oldFileRelativePaths = oldFilesWithHashes.Keys.ToHashSet();

			// Match files based on relative paths
			List<string> matchedRelativePaths = MatchFiles(oldFileRelativePaths, latestFileRelativePaths);

			Console.WriteLine($"[{Path.GetFileName(oldDirectory)}] Building patch data (writing to temporary files)...");

			ConcurrentBag<ModifiedFileEntry> modifiedFilesData = new ConcurrentBag<ModifiedFileEntry>();
			ConcurrentBag<NewFileEntry> newFilesData = new ConcurrentBag<NewFileEntry>();
			ConcurrentBag<DeletedFileEntry> deletedFilesData = new ConcurrentBag<DeletedFileEntry>();

			// 1. Process Deleted Files (sequential, as they are just paths)
			foreach (var deletedFilePath in oldFileRelativePaths)
			{
				deletedFilesData.Add(new DeletedFileEntry { RelativePath = deletedFilePath });
				Console.WriteLine($"[{Path.GetFileName(oldDirectory)}] \tIdentified for deletion: {deletedFilePath}");
			}

			// 2. Process Modified Files in parallel
			Parallel.ForEach(matchedRelativePaths, (matchedRelativePath) =>
			{
				string oldFullFilePath = Path.Combine(oldDirectory, matchedRelativePath);
				string newFullFilePath = Path.Combine(latestDirectory, matchedRelativePath);

				// Generate patch data (byte array) for this specific file
				byte[] patchDataBytes = patchGenerator.Generate(oldFullFilePath, newFullFilePath);

				if (patchDataBytes.Length > 0) // Only add if there's actual patch data
				{
					// Create a unique temporary file path for this patch data
					string tempPatchFile = Path.Combine(Path.GetTempPath(), $"patch_{Guid.NewGuid()}.bin");

					try
					{
						// Write patch data directly to the temporary file
						File.WriteAllBytes(tempPatchFile, patchDataBytes);
						tempPatchFilesToCleanUp.Add(tempPatchFile); // Add to cleanup list

						modifiedFilesData.Add(new ModifiedFileEntry
						{
							RelativePath = matchedRelativePath,
							OldHash = oldFilesWithHashes[matchedRelativePath].hash,
							NewHash = latestFilesWithHashes[matchedRelativePath].hash,
							PatchDataEntryName = $"patches/{matchedRelativePath.Replace('\\', '/')}.bin", // Consistent ZIP path
							TempPatchFilePath = tempPatchFile // Store path to temp file
						});
						Console.WriteLine($"[{Path.GetFileName(oldDirectory)}] \tGenerated patch for modified: {matchedRelativePath} (written to temp file)");
					}
					catch (Exception ex)
					{
						Console.WriteLine($"[{Path.GetFileName(oldDirectory)}] \tError writing patch for {matchedRelativePath} to temp file: {ex.Message}");
						// Continue processing other files, but this one will be skipped
					}
				}
				else // If patchDataBytes is empty, it means files are identical, no patch needed
				{
					Console.WriteLine($"[{Path.GetFileName(oldDirectory)}] \tFiles are identical, no patch generated for: {matchedRelativePath}");
				}
			});

			// 3. Process New Files (metadata only here, actual file streaming happens during ZIP creation)
			Parallel.ForEach(latestFileRelativePaths, (newRelativePath) =>
			{
				newFilesData.Add(new NewFileEntry
				{
					RelativePath = newRelativePath,
					NewHash = latestFilesWithHashes[newRelativePath].hash,
					FileDataEntryName = $"new_files/{newRelativePath.Replace('\\', '/')}" // Consistent ZIP path
				});
				Console.WriteLine($"[{Path.GetFileName(oldDirectory)}] \tPrepared metadata for new file: {newRelativePath}");
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
					Console.WriteLine($"[{Path.GetFileName(oldDirectory)}] \tAdded manifest.json to ZIP.");

					// Add Modified File Patches to ZIP by streaming from temporary files
					foreach (var entry in modifiedFilesData)
					{
						if (!string.IsNullOrEmpty(entry.TempPatchFilePath) && File.Exists(entry.TempPatchFilePath))
						{
							ZipArchiveEntry patchEntry = archive.CreateEntry(entry.PatchDataEntryName, CompressionLevel.Optimal);
							using (Stream entryStream = patchEntry.Open())
							using (FileStream sourcePatchStream = File.OpenRead(entry.TempPatchFilePath))
							{
								sourcePatchStream.CopyTo(entryStream); // Stream directly
							}
							Console.WriteLine($"[{Path.GetFileName(oldDirectory)}] \tAdded patch data for {entry.RelativePath} to ZIP by streaming from temp file.");
						}
						else
						{
							Console.WriteLine($"[{Path.GetFileName(oldDirectory)}] \tWarning: Temporary patch file not found for {entry.RelativePath}, skipping ZIP addition.");
						}
					}

					// Add New Files to ZIP by streaming directly from disk (already optimized in previous step)
					foreach (var entry in newFilesData)
					{
						string fullFilePath = Path.Combine(latestDirectory, entry.RelativePath);
						if (File.Exists(fullFilePath))
						{
							ZipArchiveEntry newFileEntry = archive.CreateEntry(entry.FileDataEntryName, CompressionLevel.Optimal);
							using (Stream entryStream = newFileEntry.Open())
							using (FileStream sourceFileStream = File.OpenRead(fullFilePath))
							{
								sourceFileStream.CopyTo(entryStream); // Stream directly from source file to ZIP entry
							}
							Console.WriteLine($"[{Path.GetFileName(oldDirectory)}] \tAdded new file data for {entry.RelativePath} to ZIP by streaming.");
						}
						else
						{
							Console.WriteLine($"[{Path.GetFileName(oldDirectory)}] \tWarning: New file not found at {fullFilePath}, skipping ZIP addition.");
						}
					}
				} // ZipArchive is disposed and written to stream
			} // FileStream is disposed, completing the file write

			// Move the temporary file to its final destination
			File.Move(tempZipFilePath, patchFilePath);
			Console.WriteLine($"[{Path.GetFileName(oldDirectory)}] Patch build completed: {patchFileName}");
		}
		catch (Exception ex)
		{
			Console.WriteLine($"[{Path.GetFileName(oldDirectory)}] Error generating patch for '{oldDirectory}': {ex.Message}");
			Console.WriteLine(ex.StackTrace);
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
						Console.WriteLine($"[{Path.GetFileName(oldDirectory)}] \tCleaned up temporary patch file: {tempFile}");
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine($"[{Path.GetFileName(oldDirectory)}] \tError cleaning up temporary file {tempFile}: {ex.Message}");
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
			Console.WriteLine($"Error streaming file chunks from '{sourceFilePath}' to memory: {ex.Message}");
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
							Console.WriteLine($"\tSkipping ignored file: {Path.GetFileName(file)} (.{extension})");
							continue;
						}

						string relativePath = file.Substring(rootDirectory.Length);
						if (Path.IsPathRooted(relativePath))
						{
							relativePath = relativePath.TrimStart(Path.DirectorySeparatorChar);
							relativePath = relativePath.TrimStart(Path.AltDirectorySeparatorChar);
						}
						// Normalize path separators for consistency, especially for manifest
						relativePath = relativePath.Replace('\\', '/');

						string fileHash = ComputeFileHash(file, sha256);
						// Using Add instead of []= to ensure unique keys in case of unexpected duplicates (though unlikely for paths)
						filesWithHashes.Add(relativePath, (relativePath, fileHash));
					}
				}
				catch (UnauthorizedAccessException)
				{
					Console.WriteLine($"Warning: Access to directory '{currentDir}' is denied. Skipping.");
					continue;
				}
				catch (DirectoryNotFoundException)
				{
					Console.WriteLine($"Warning: Directory '{currentDir}' not found. Skipping.");
					continue;
				}
				catch (IOException ex)
				{
					Console.WriteLine($"Warning: IO error accessing files in '{currentDir}': {ex.Message}. Skipping.");
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
					Console.WriteLine($"Warning: Access to subdirectory '{currentDir}' is denied. Skipping.");
					continue;
				}
				catch (DirectoryNotFoundException)
				{
					Console.WriteLine($"Warning: Subdirectory '{currentDir}' not found. Skipping.");
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
	/// Returns a list of matching relative file paths. OldFiles and LatestFiles are updated so that matching files are no longer contained.
	/// </summary>
	static List<string> MatchFiles(HashSet<string> oldFiles, HashSet<string> latestFiles)
	{
		List<string> matchingFiles = new List<string>();
		// Create a copy to iterate, as we will modify the original oldFiles HashSet
		foreach (string oldFile in new HashSet<string>(oldFiles))
		{
			if (latestFiles.Contains(oldFile))
			{
				matchingFiles.Add(oldFile);
				latestFiles.Remove(oldFile); // Remove from latest as it's a matched file
				oldFiles.Remove(oldFile);    // Remove from old as it's a matched file
			}
		}
		return matchingFiles;
	}
}