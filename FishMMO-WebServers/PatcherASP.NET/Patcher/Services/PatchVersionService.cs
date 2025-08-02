using Microsoft.Extensions.Hosting;
using System.IO;
using System;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using FishMMO.Logging;

public class PatchVersionService
{
	private readonly IHostEnvironment env;
	private readonly IConfiguration config;
	public string? LatestVersion { get; private set; }

	private static readonly Regex PatchFileNameRegex =
		new Regex(@"^(\d+\.\d+\.\d+(?:\.[a-zA-Z0-9]+)?)-(\d+\.\d+\.\d+(?:\.[a-zA-Z0-9]+)?)\.zip$", RegexOptions.Compiled);

	public PatchVersionService(IHostEnvironment env, IConfiguration config)
	{
		this.env = env;
		this.config = config;
		InitializeLatestVersion();
	}

	private void InitializeLatestVersion()
	{
		var patchesDirectoryConfig = config["Patches:DirectoryName"] ?? "Patches";
		var patchesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, patchesDirectoryConfig);

		if (!Directory.Exists(patchesPath))
		{
			Log.Warning("PatchVersionService", $"Patches directory not found: {patchesPath}. Setting latest version to 0.0.0.");
			LatestVersion = new VersionConfig() { Major = 0, Minor = 0, Patch = 0 }.FullVersion;
			return;
		}

		try
		{
			var patchFiles = Directory.EnumerateFiles(patchesPath, "*.zip", SearchOption.TopDirectoryOnly);

			VersionConfig? highestVersion = null;

			foreach (var filePath in patchFiles)
			{
				string fileName = Path.GetFileName(filePath);
				Match match = PatchFileNameRegex.Match(fileName);

				if (match.Success && match.Groups.Count >= 3)
				{
					string newVersionString = match.Groups[2].Value;
					// Pass the logger to VersionConfig.Parse
					VersionConfig? currentPatchTargetVersion = VersionConfig.Parse(newVersionString);

					if (currentPatchTargetVersion != null)
					{
						if (highestVersion == null || currentPatchTargetVersion > highestVersion)
						{
							highestVersion = currentPatchTargetVersion;
						}
					}
					else
					{
						Log.Warning("PatchVersionService", $"Could not parse version '{newVersionString}' from patch file name '{fileName}'. Ensure versions follow X.Y.Z or X.Y.Z.PreRelease format.");
					}
				}
				else
				{
					Log.Warning("PatchVersionService", $"File '{fileName}' does not match the expected patch file naming convention (e.g., '1.0.0-1.0.1.zip' or '1.0.0.alpha-1.0.0.beta.zip'). It will be ignored.");
				}
			}

			if (highestVersion != null)
			{
				LatestVersion = highestVersion.FullVersion;
				Log.Info("PatchVersionService", $"Determined latest client version from patches: {LatestVersion}");
			}
			else
			{
				LatestVersion = new VersionConfig() { Major = 0, Minor = 0, Patch = 0 }.FullVersion;
				Log.Info("PatchVersionService", $"No valid patch files found in '{patchesPath}' matching the expected pattern. Setting latest version to 0.0.0.");
			}
		}
		catch (Exception ex)
		{
			Log.Error("PatchVersionService", $"Error determining latest version from patch files in '{patchesPath}'. Setting latest version to 0.0.0.", ex);
			LatestVersion = new VersionConfig() { Major = 0, Minor = 0, Patch = 0 }.FullVersion;
		}
	}
}