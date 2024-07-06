using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FishMMO.Shared;
using FishMMO.Patcher;

class Program
{
	static void Main()
	{
		Console.Write("Enter the path to the latest directory: ");
		string latestDirectory = Console.ReadLine();
		if (!Directory.Exists(latestDirectory))
		{
			Console.WriteLine($"Directory '{latestDirectory}' does not exist.");
			return;
		}

		Console.Write("Enter the path to the old directories: ");
		string oldDirectory = Console.ReadLine();
		if (!Directory.Exists(oldDirectory))
		{
			Console.WriteLine($"Directory '{oldDirectory}' does not exist.");
			return;
		}

		Console.Write("Enter the path to the patch output directory: ");
		string patchesDirectory = Console.ReadLine();
		// Delete the old patches as they should no longer be relevant. We want the shortest path to the latest version only.
		if (Directory.Exists(patchesDirectory))
		{
			Directory.Delete(patchesDirectory, true);
		}
		// Ensure patch directory exists
		Directory.CreateDirectory(patchesDirectory);

		PatchGenerator patchGenerator = new PatchGenerator();

		try
		{
			Console.WriteLine("Starting patch generation.");

			// Get all directories under the specified directory
			string[] oldDirectories = Directory.GetDirectories(oldDirectory);

			// Print all directory names
			foreach (string old in oldDirectories)
			{
				CreatePatch(patchGenerator, latestDirectory, old, patchesDirectory);
				Console.WriteLine();
			}

			Console.WriteLine("All operations complete.");
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Error: {ex.Message}");
		}
	}

	public static void CreatePatch(PatchGenerator patchGenerator, string latestDirectory, string oldDirectory, string patchDirectory)
	{
		// Load latest configuration
		Configuration latestConfiguration = new Configuration(latestDirectory);
		if (!latestConfiguration.Load())
		{
			// If we failed to load the file..
			Console.WriteLine("Unable to load latest configuration file.");
			return;
		}
		if (!latestConfiguration.TryGetString("Version", out string latestVersion) ||
			string.IsNullOrWhiteSpace(latestVersion))
		{
			Console.WriteLine("Unable to load latest version data.");
			return;
		}

		// Load old configuration
		Configuration oldConfiguration = new Configuration(oldDirectory);
		if (!oldConfiguration.Load())
		{
			// If we failed to load the file..
			Console.WriteLine("Unable to load old configuration file.");
			return;
		}
		if (!oldConfiguration.TryGetString("Version", out string oldVersion) ||
			string.IsNullOrWhiteSpace(oldVersion))
		{
			Console.WriteLine("Unable to load old version data.");
			return;
		}

		string patchFileName = $"{oldVersion}-{latestVersion}.patch";

		// Generate delta
		string patchFilePath = Path.Combine(patchDirectory, patchFileName);

		Console.WriteLine($"Generating {patchFileName}");

		// Get all files in old and latest directories
		Console.WriteLine("\tLatest Files:");
		HashSet<string> latestFiles = GetAllFiles(latestDirectory);
		Console.WriteLine("\tOld Files:");
		HashSet<string> oldFiles = GetAllFiles(oldDirectory);

		// Match files based on relative paths
		var matchedFiles = MatchFiles(oldFiles, latestFiles);

		Console.WriteLine("Building patch...");

		using (FileStream patchFile = File.Create(patchFilePath))
		using (BinaryWriter writer = new BinaryWriter(patchFile, System.Text.Encoding.UTF8, true))
		{
			// Process files only in old directory (delete metadata)
			writer.Write(oldFiles.Count);
			foreach (var oldFilePath in oldFiles)
			{
				writer.Write(oldFilePath);
				Console.WriteLine($"\tAdded delete metadata for {oldFilePath}");
			}

			// Process files in both directories (generate delta)
			writer.Write(matchedFiles.Count);
			foreach (var matched in matchedFiles)
			{
				writer.Write(matched);
				string oldFilePath = Path.Combine(oldDirectory, matched);
				string newFilePath = Path.Combine(latestDirectory, matched);

				Console.WriteLine($"\tGenerating comparison metadata for {matched}");

				patchGenerator.Generate(writer, oldFilePath, newFilePath, OnMetaDataGenerationComplete);
			}

			// Process files only in latest directory (add file data)
			writer.Write(latestFiles.Count);
			foreach (var newFilePath in latestFiles)
			{
				writer.Write(newFilePath);
				Console.WriteLine($"\tAdded new file {newFilePath}");
				StreamFileChunks(Path.Combine(latestDirectory, newFilePath), patchFile);
			}
		}

		Console.WriteLine("Patch build completed.");
	}

	// Method to stream file chunks and write them to another file using BinaryWriter
	public static void StreamFileChunks(string newFilePath, FileStream patchFile)
	{
		try
		{
			const int bufferSize = 4096; // 4KB buffer size, can be adjusted as needed
			byte[] buffer = new byte[bufferSize];
			int bytesRead;

			// Open FileStream for reading the source file
			using (FileStream newFile = File.OpenRead(newFilePath))
			{
				// Use BinaryWriter to write to file
				using (BinaryWriter writer = new BinaryWriter(patchFile, System.Text.Encoding.UTF8, true))
				{
					// Write the length of the file
					writer.Write(newFile.Length);

					// Read and write in chunks
					while ((bytesRead = newFile.Read(buffer, 0, bufferSize)) > 0)
					{
						writer.Write(buffer, 0, bytesRead);
					}
				}
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Error streaming file chunks to delta: {ex.Message}");
			throw; // Optional: Handle or propagate the exception as needed
		}
	}

	/// <summary>
	/// Gets all files recursively inside a root directory and trims the root directory path.
	/// </summary>
	public static HashSet<string> GetAllFiles(string rootDirectory)
	{
		HashSet<string> files = new HashSet<string>();
		Stack<string> directories = new Stack<string>();

		// Start with the root directory
		directories.Push(rootDirectory);

		while (directories.Count > 0)
		{
			string currentDir = directories.Pop();

			// Get files in the current directory
			try
			{
				string[] currentFiles = Directory.GetFiles(currentDir);
				foreach (string file in currentFiles)
				{
					// Skip configuration files
					if (file.EndsWith(".cfg"))
					{
						continue;
					}
					string path = file.Substring(rootDirectory.Length, file.Length - rootDirectory.Length);
					if (Path.IsPathRooted(path))
					{
						path = path.TrimStart(Path.DirectorySeparatorChar);
						path = path.TrimStart(Path.AltDirectorySeparatorChar);
					}
					files.Add(path);
					Console.WriteLine($"\t\t{path}"); // Optional: Print each file path
				}
			}
			catch (UnauthorizedAccessException)
			{
				Console.WriteLine($"Access to directory '{currentDir}' is denied.");
				continue; // Skip this directory if access is denied
			}
			catch (DirectoryNotFoundException)
			{
				Console.WriteLine($"Directory '{currentDir}' not found.");
				continue; // Skip this directory if it's not found
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
				Console.WriteLine($"Access to directory '{currentDir}' is denied.");
				continue; // Skip subdirectories if access is denied
			}
			catch (DirectoryNotFoundException)
			{
				Console.WriteLine($"Directory '{currentDir}' not found.");
				continue; // Skip subdirectories if the current directory is not found
			}
		}

		return files;
	}

	/// <summary>
	/// Returns a list of matching relative file paths. OldFiles and LatestFiles are updated so that matching files are no longer contained.
	/// </summary>
	static List<string> MatchFiles(HashSet<string> oldFiles, HashSet<string> latestFiles)
	{
		List<string> matchingFiles = new List<string>();
		foreach (string oldFile in new HashSet<string>(oldFiles))
		{
			if (latestFiles.Contains(oldFile))
			{
				matchingFiles.Add(oldFile);
				latestFiles.Remove(oldFile);
				oldFiles.Remove(oldFile);
			}
		}
		return matchingFiles;
	}

	private static void OnMetaDataGenerationComplete(bool success)
	{
		if (success)
			Console.WriteLine("\tMetadata generation successful.");
		else
			Console.WriteLine("Failed to generate metadata.");
	}
}