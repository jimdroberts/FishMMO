using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
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

		try
		{
			Process launcherProcess = Process.GetProcessById(PID);
			launcherProcess.WaitForExit(); // Wait for the parent process to exit
		}
		catch (ArgumentException)
		{
			// Parent process already exited
		}

		if (string.IsNullOrWhiteSpace(LatestVersion))
		{
			Console.WriteLine("Latest version was not specified!");
			return;
		}

		if (!Directory.Exists(PatchesDirectory))
		{
			throw new Exception($"{PatchesDirectory} does not exist.");
		}

		// load configuration
		Configuration = new Configuration(WorkingDirectory);
		if (!Configuration.Load())
		{
			// if we failed to load the file..
			Console.WriteLine("Unable to load current configuration file.");
			return;
		}
		if (!Configuration.TryGetString("Version", out Version))
		{
			Console.WriteLine("Unable to load current version data.");
			return;
		}

		if (LatestVersion.Equals(Version))
		{
			Console.WriteLine("Patch has already been applied.");
			return;
		}

		Patcher patcher = new Patcher();

		try
		{
			ApplyPatch(patcher);

			// Update the current .cfg to the latest version
			Configuration.Set("Version", LatestVersion);
			Configuration.Save();

			Console.WriteLine("All operations complete.");
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Error: {ex.Message}");
		}

		// Restart the launcher
		ProcessStartInfo startInfo = new ProcessStartInfo(Path.Combine(WorkingDirectory, Executable));
		startInfo.UseShellExecute = false;
		Process.Start(startInfo);

		Environment.Exit(0);
	}

	public static void ApplyPatch(Patcher patcher)
	{
		string patchFilePath = Path.Combine(PatchesDirectory, $"{Version}-{LatestVersion}.patch");

		using (FileStream patchFile = File.OpenRead(patchFilePath))
		using (BinaryReader reader = new BinaryReader(patchFile, System.Text.Encoding.UTF8, true))
		{
			// Process files only in old directory (delete metadata)
			int deleteFileCount = reader.ReadInt32();
			for (int i = 0; i < deleteFileCount; ++i)
			{
				string fileToDelete = reader.ReadString();
				Console.WriteLine($"Deleting file {fileToDelete}");
				string pathToFile = Path.Combine(WorkingDirectory, fileToDelete);
				if (File.Exists(pathToFile))
				{
					File.Delete(pathToFile);
				}
			}

			// Process files in both directories (generate delta)
			int diffCount = reader.ReadInt32();
			for (int i = 0; i < diffCount; i++)
			{
				string matched = reader.ReadString();
				string oldFilePath = Path.Combine(WorkingDirectory, matched);

				// Apply delta to reconstruct new file
				Console.WriteLine($"Applying patch to reconstruct {matched}...");
				patcher.Apply(reader, diffCount, oldFilePath, OnDeltaApplicationComplete);
			}

			int newFilesCount = reader.ReadInt32();
			// Optionally, process files only in latest directory (copy or handle as needed)
			for (int i = 0; i < newFilesCount; i++)
			{
				string newFilePath = reader.ReadString();
				if (!string.IsNullOrWhiteSpace(newFilePath))
				{
					StreamToFile(Path.Combine(WorkingDirectory, newFilePath), reader);
				}
			}
		}
	}

	// Method to stream file chunks and write them to another file using BinaryWriter
	public static void StreamToFile(string newFilePath, BinaryReader reader)
	{
		try
		{
			const int bufferSize = 4096; // 4KB buffer size, can be adjusted as needed
			byte[] buffer = new byte[bufferSize];
			int bytesRead;

			// Ensure the directory exists
			string directoryPath = Path.GetDirectoryName(newFilePath);
			if (!string.IsNullOrEmpty(directoryPath))
			{
				Directory.CreateDirectory(directoryPath);
			}

			// Open FileStream for writing the new file
			using (FileStream newFile = File.Create(newFilePath))
			{
				// Read and write in chunks up to newFileLength
				long bytesRemaining = reader.ReadInt64();
				while (bytesRemaining > 0 && (bytesRead = reader.Read(buffer, 0, (int)Math.Min(bufferSize, bytesRemaining))) > 0)
				{
					newFile.Write(buffer, 0, bytesRead);
					bytesRemaining -= bytesRead;
				}
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Error streaming file chunks to delta: {ex.Message}");
			throw; // Optional: Handle or propagate the exception as needed
		}
	}

	private static void OnDeltaApplicationComplete(bool success)
	{
		if (success)
			Console.WriteLine("Patch application successful.");
		else
			Console.WriteLine("Failed to apply patch.");
	}
}