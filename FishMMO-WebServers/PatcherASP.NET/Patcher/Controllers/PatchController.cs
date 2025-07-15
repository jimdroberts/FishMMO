using Microsoft.AspNetCore.Mvc;
using System;

[ApiController]
[Route("/")]
public class PatchController : ControllerBase
{
	private readonly IWebHostEnvironment env;
	private readonly PatchVersionService versionService;
	private readonly ILogger<PatchController> logger; // Inject logger for better error messages

	public PatchController(IWebHostEnvironment env, PatchVersionService versionService, ILogger<PatchController> logger)
	{
		this.env = env;
		this.versionService = versionService;
		this.logger = logger;
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
			logger.LogWarning("Latest version is null, cannot process patch request for version {RequestedVersion}.", version);
			return NotFound("Latest version information not available.");
		}

		// Parse client's requested version and the latest version using System.Version
		if (!System.Version.TryParse(version, out System.Version? clientVersion))
		{
			logger.LogWarning("Invalid client version format received: {RequestedVersion}", version);
			return BadRequest("Invalid client version format.");
		}

		if (!System.Version.TryParse(latest, out System.Version? latestVersion))
		{
			// This case should ideally not happen if PatchVersionService initializes correctly.
			logger.LogError("Failed to parse latest version '{LatestVersion}' from PatchVersionService.", latest);
			return StatusCode(500, "Internal server error: Latest version malformed.");
		}

		// Use System.Version's comparison to check if already updated
		if (clientVersion >= latestVersion)
		{
			logger.LogInformation("Client version {ClientVersion} is already up to date with latest version {LatestVersion}.", clientVersion, latestVersion);
			return Ok(new { status = "AlreadyUpdated" });
		}

		// Construct the file path using the string representations of the parsed versions
		var filePath = Path.Combine(env.ContentRootPath, "Patches", $"{clientVersion}-{latestVersion}.zip");

		if (!System.IO.File.Exists(filePath))
		{
			logger.LogWarning("Patch file not found for request {ClientVersion}-{LatestVersion}: {FilePath}", clientVersion, latestVersion, filePath);
			return NotFound($"Patch file not found for version {version}.");
		}

		try
		{
			// Open a FileStream to the patch file
			// FileShare.Read allows other processes to read the file while it's open
			var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
			// Return a FileStreamResult to stream the file
			return new FileStreamResult(fileStream, "application/octet-stream");
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error opening patch file for streaming: {FilePath}", filePath);
			return StatusCode(500, "Internal server error: Could not access patch file.");
		}
	}
}