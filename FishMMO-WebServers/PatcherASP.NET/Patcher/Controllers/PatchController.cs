using Microsoft.AspNetCore.Mvc;
using System;
using Microsoft.Extensions.Hosting;
using System.IO;
using FishMMO.Logging;

[ApiController]
[Route("/")]
public class PatchController : ControllerBase
{
	private readonly IHostEnvironment env;
	private readonly PatchVersionService versionService;

	public PatchController(IHostEnvironment env, PatchVersionService versionService)
	{
		this.env = env;
		this.versionService = versionService;
	}

	[HttpGet("latest_version")]
	public IActionResult GetLatestVersion()
	{
		return Ok(new { latest_version = versionService.LatestVersion });
	}

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