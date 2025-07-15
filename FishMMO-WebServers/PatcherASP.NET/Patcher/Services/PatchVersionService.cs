using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;
using System.IO;
using System;
using System.Linq;
using System.Text.RegularExpressions;

public class PatchVersionService
{
	private readonly ILogger<PatchVersionService> logger;
	private readonly IWebHostEnvironment env;
	public string? LatestVersion { get; private set; }

	// Regex to match expected patch file names like "1.0.0-1.0.1.zip" or "1.0.0.alpha-1.0.0.beta.zip"
	// Group 1: Old version string (e.g., 1.0.0 or 1.0.0.alpha)
	// Group 2: New version string (e.g., 1.0.1 or 1.0.0.beta) - This is what we care about for the latest version
	// This regex is designed to capture full version strings compatible with VersionConfig.Parse
	private static readonly Regex PatchFileNameRegex =
		new Regex(@"^(\d+\.\d+\.\d+(?:\.[a-zA-Z0-9]+)?)-(\d+\.\d+\.\d+(?:\.[a-zA-Z0-9]+)?)\.zip$", RegexOptions.Compiled);

	public PatchVersionService(ILogger<PatchVersionService> logger, IWebHostEnvironment env)
	{
		this.logger = logger;
		this.env = env;
		InitializeLatestVersion(); // Determine the latest version when the service is created.
	}

	/// <summary>
	/// Scans the 'Patches' directory to determine the highest available client version.
	/// The latest version is determined by finding the highest 'new_version' (Y) from all 'X-Y.zip' patch files.
	/// </summary>
	private void InitializeLatestVersion()
	{
		string patchesPath = Path.Combine(env.ContentRootPath, "Patches");

		if (!Directory.Exists(patchesPath))
		{
			logger.LogWarning("Patches directory not found: {Path}. Setting latest version to 0.0.0.", patchesPath);
			// Default to a base version if the directory doesn't exist.
			// This needs to be a string that VersionConfig.Parse can handle.
			LatestVersion = new VersionConfig() { Major = 0, Minor = 0, Patch = 0 }.FullVersion;
			return;
		}

		try
		{
			// Get all .zip files in the Patches directory (only top level).
			var patchFiles = Directory.EnumerateFiles(patchesPath, "*.zip", SearchOption.TopDirectoryOnly);

			VersionConfig? highestVersion = null; // Use VersionConfig for proper semantic comparison.

			foreach (var filePath in patchFiles)
			{
				string fileName = Path.GetFileName(filePath);
				Match match = PatchFileNameRegex.Match(fileName);

				// Check if the filename matches our expected pattern and has enough groups.
				// Group 0: Full match, Group 1: Old Version, Group 2: New Version
				if (match.Success && match.Groups.Count >= 3)
				{
					// The 'new version' is captured in the second group (index 2) of the regex match.
					string newVersionString = match.Groups[2].Value;
					// Use VersionConfig.Parse to convert string to VersionConfig object
					VersionConfig? currentPatchTargetVersion = VersionConfig.Parse(newVersionString);

					if (currentPatchTargetVersion != null)
					{
						// If this is the first valid version found, or it's higher than the current highest.
						if (highestVersion == null || currentPatchTargetVersion > highestVersion)
						{
							highestVersion = currentPatchTargetVersion;
						}
					}
					else
					{
						logger.LogWarning("Could not parse version '{VersionString}' from patch file name '{FileName}'. Ensure versions follow X.Y.Z or X.Y.Z.PreRelease format.", newVersionString, fileName);
					}
				}
				else
				{
					logger.LogWarning("File '{FileName}' does not match the expected patch file naming convention (e.g., '1.0.0-1.0.1.zip' or '1.0.0.alpha-1.0.0.beta.zip'). It will be ignored.", fileName);
				}
			}

			if (highestVersion != null)
			{
				LatestVersion = highestVersion.FullVersion; // Use FullVersion string for storage
				logger.LogInformation("Determined latest client version from patches: {Version}", LatestVersion);
			}
			else
			{
				// If no valid patch files were found after scanning, default to "0.0.0".
				LatestVersion = new VersionConfig() { Major = 0, Minor = 0, Patch = 0 }.FullVersion;
				logger.LogInformation("No valid patch files found in '{Path}' matching the expected pattern. Setting latest version to 0.0.0.", patchesPath);
			}
		}
		catch (Exception ex)
		{
			// Log any errors during the scanning process and fall back to a default version.
			logger.LogError(ex, "Error determining latest version from patch files in '{Path}'. Setting latest version to 0.0.0.", patchesPath);
			LatestVersion = new VersionConfig() { Major = 0, Minor = 0, Patch = 0 }.FullVersion;
		}
	}
}