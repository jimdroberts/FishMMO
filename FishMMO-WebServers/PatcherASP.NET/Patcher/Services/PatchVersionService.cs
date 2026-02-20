using System.Text.RegularExpressions;
using FishMMO.Logging;

/// <summary>
/// Service responsible for scanning the configured patches directory and
/// determining the latest client version available from patch filenames.
/// The service exposes the computed latest version via the <see cref="LatestVersion"/> property.
/// </summary>
public class PatchVersionService
{
	private readonly IHostEnvironment env;
	private readonly IConfiguration config;
	/// <summary>
	/// The latest client version discovered from patch files, or <c>null</c>
	/// if it has not yet been determined. The value is represented as the
	/// full version string from <see cref="VersionConfig.FullVersion"/>.
	/// </summary>
	public string? LatestVersion { get; private set; }

	private static readonly Regex PatchFileNameRegex =
		new Regex(@"^(\d+\.\d+\.\d+(?:\.[a-zA-Z0-9]+)?)-(\d+\.\d+\.\d+(?:\.[a-zA-Z0-9]+)?)\.zip$", RegexOptions.Compiled);

	/// <summary>
	/// Initializes a new instance of the <see cref="PatchVersionService"/> class.
	/// The constructor will immediately attempt to determine the latest version
	/// from patch files in the configured patches directory.
	/// </summary>
	/// <param name="env">The host environment (used for path resolution).</param>
	/// <param name="config">Application configuration used to locate the patches directory.</param>
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