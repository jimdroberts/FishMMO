using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("/")]
public class PatchController : ControllerBase
{
	private readonly IWebHostEnvironment env;
	private readonly PatchVersionService versionService;

	public PatchController(IWebHostEnvironment env, PatchVersionService versionService)
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
		if (latest == null) return NotFound();

		if (version == latest)
			return Ok(new { status = "AlreadyUpdated" });

		var filePath = Path.Combine(env.ContentRootPath, "patches", $"{version}-{latest}.patch");

		if (!System.IO.File.Exists(filePath))
			return NotFound();

		var fileBytes = System.IO.File.ReadAllBytes(filePath);
		return File(fileBytes, "application/octet-stream");
	}
}