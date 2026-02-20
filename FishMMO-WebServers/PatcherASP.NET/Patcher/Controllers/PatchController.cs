using Microsoft.AspNetCore.Mvc;
using FishMMO.Logging;

/// <summary>
/// API controller that exposes endpoints for clients to query the latest
/// available version and to download patch files from a client version
/// to the latest server version.
/// </summary>
[ApiController]
[Route("/")]
public class PatchController : ControllerBase
{
	private readonly IHostEnvironment env;
	private readonly PatchVersionService versionService;

	/// <summary>
	/// Initializes a new instance of the <see cref="PatchController"/> class.
	/// </summary>
	/// <param name="env">The host environment (used for locating application base paths).</param>
	/// <param name="versionService">Service that provides the latest version derived from patch files.</param>
	public PatchController(IHostEnvironment env, PatchVersionService versionService)
	{
		this.env = env;
		this.versionService = versionService;
	}

	/// <summary>
	/// GET /latest_version
	/// Returns the latest client version available on the server.
	/// </summary>
	/// <returns>An <see cref="IActionResult"/> containing a JSON object with the <c>latest_version</c> property.</returns>
	[HttpGet("latest_version")]
	public IActionResult GetLatestVersion()
	{
		return Ok(new { latest_version = versionService.LatestVersion });
	}

	/// <summary>
	/// GET /{version}
	/// Streams the patch file that updates a client from the specified <paramref name="version"/>
	/// to the server's latest version, if available.
	/// </summary>
	/// <param name="version">The client's current version string (e.g., "1.0.0").</param>
	/// <returns>
	/// - <see cref="IActionResult"/> returning the patch file as a stream when found,
	/// - <see cref="BadRequestResult"/> for invalid version formats,
	/// - <see cref="NotFoundResult"/> when the patch file does not exist,
	/// - or a 500 status code for server-side errors.
	/// </returns>
	[HttpGet("{version}")]
	public IActionResult GetPatch(string version)
	{
		var latest = versionService.LatestVersion;
		if (latest == null)
		{
			Log.Warning("PatchController", $"Latest version is null, cannot process patch request for version {version}.");
			return StatusCode(500, "Latest version information not available on server."); // More specific error
		}

		// Use VersionConfig.Parse for client version
		VersionConfig? clientVersionConfig = VersionConfig.Parse(version);
		if (clientVersionConfig == null)
		{
			Log.Warning("PatchController", $"Invalid client version format received: {version}");
			return BadRequest("Invalid client version format. Expected X.Y.Z or X.Y.Z.PreRelease.");
		}

		// Use VersionConfig.Parse for latest version
		VersionConfig? latestVersionConfig = VersionConfig.Parse(latest);
		if (latestVersionConfig == null)
		{
			Log.Error("PatchController", $"Failed to parse latest version '{latest}' from PatchVersionService. Server version malformed.");
			return StatusCode(500, "Internal server error: Latest server version malformed.");
		}

		// Use VersionConfig's comparison
		if (clientVersionConfig >= latestVersionConfig)
		{
			Log.Info("PatchController", $"Client version {clientVersionConfig.FullVersion} is already up to date with latest version {latestVersionConfig.FullVersion}.");
			return Ok(new { status = "AlreadyUpdated" });
		}

		var patchDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Patches");
		var filePath = Path.Combine(patchDirectory, $"{clientVersionConfig.FullVersion}-{latestVersionConfig.FullVersion}.zip");

		if (!System.IO.File.Exists(filePath))
		{
			Log.Warning("PatchController", $"Patch file not found for request {clientVersionConfig.FullVersion}-{latestVersionConfig.FullVersion}: {filePath}");
			return NotFound($"Patch file not found from version {clientVersionConfig.FullVersion} to {latestVersionConfig.FullVersion}.");
		}

		try
		{
			var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
			return new FileStreamResult(fileStream, "application/octet-stream")
			{
				FileDownloadName = $"{clientVersionConfig.FullVersion}-{latestVersionConfig.FullVersion}.zip" // Suggest download name
			};
		}
		catch (Exception ex)
		{
			Log.Error("PatchController", $"Error opening patch file for streaming: {filePath}", ex);
			return StatusCode(500, "Internal server error: Could not access patch file.");
		}
	}
}