using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using FishMMO.Logging;
using FishMMO.WebServers.Signing;

/// <summary>
/// API controller exposing patch metadata and downloads. Only patch files
/// pre-indexed at startup by <see cref="PatchVersionService"/> are reachable;
/// arbitrary file paths cannot be constructed from request input.
/// </summary>
[ApiController]
[Route("/")]
public class PatchController : ControllerBase
{
	private readonly PatchVersionService versionService;
	private readonly VersionManifestSigner signer;

	public PatchController(PatchVersionService versionService, VersionManifestSigner signer)
	{
		this.versionService = versionService;
		this.signer = signer;
	}

	/// <summary>
	/// GET /latest_version[?from={clientVersion}]
	/// Returns the latest available version. If <paramref name="from"/> is supplied,
	/// the response additionally includes the SHA-256 and size of the patch that
	/// upgrades that client version to the latest, so the client can verify the
	/// downloaded file's integrity.
	///
	/// HEAD is supported with identical headers but no body. Both methods are
	/// cacheable for a short window so launchers polling on a tight loop do not
	/// hammer the origin; the ETag is derived from the indexed patch's hash so
	/// new patches invalidate the cache immediately.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Every shape this endpoint returns is Ed25519-signed</b> (see
	/// <see cref="VersionManifestSigner"/>). The <c>sha256</c> field here is the only integrity
	/// check the launcher applies to a patch archive, so whoever writes this document chooses the
	/// bytes that get installed; TLS proves who is answering on the socket, not who produced the
	/// document. The <c>signature</c> field is always present — filled in when a key is
	/// configured, empty otherwise — because the client's verifier locates the field textually and
	/// refuses to guess a canonical form when it is missing.
	/// </para>
	/// <para>
	/// The older <c>X-FishMMO-Version-Signature</c> HMAC header is left in place and is a
	/// different thing solving a different problem: it is keyed on the shared client-gate secret,
	/// which every client already holds, so it authenticates "a party that knows the gate secret"
	/// rather than "the release key holder" and cannot survive that secret being extracted from a
	/// shipped binary. It is retained for compatibility with anything already reading it; the
	/// body signature is the one that carries the security property.
	/// </para>
	/// </remarks>
	[HttpGet("latest_version")]
	[HttpHead("latest_version")]
	public IActionResult GetLatestVersion([FromQuery] string? from)
	{
		var latest = versionService.LatestVersion;
		if (string.IsNullOrEmpty(latest))
		{
			Log.Warning("PatchController", "Latest version is unavailable.");
			return StatusCode(500, "Latest version information not available on server.");
		}

		/* Build the response payload and a weak ETag derived from its contents.
		 *
		 * The payload is built as EXPLICIT JSON TEXT rather than an anonymous object handed to
		 * MVC's serialiser, because the Ed25519 signature below covers the literal bytes the
		 * client receives. The client verifies by locating its own "signature" field in the
		 * received string and blanking it — it never re-serialises — so if the framework changed
		 * the spacing, the property naming policy or the escaping table (an AddJsonOptions call
		 * anywhere in Program.cs would), every manifest in the field would stop verifying and
		 * nothing would connect that to the change. ManifestJsonWriter owns the format instead.
		 *
		 * All four response shapes go through the same path, so all four are signed. The
		 * short "latest_version only" shape is signed for the same reason as the rest: it is what
		 * decides whether the launcher believes an update exists at all, and an unsigned shape
		 * would be a downgrade oracle (strip the patch fields, keep the version). */
		ManifestJsonWriter writer = new ManifestJsonWriter();
		string etagSource;

		if (string.IsNullOrEmpty(from))
		{
			writer.AddString("latest_version", latest);
			etagSource = "v:" + latest;
		}
		else
		{
			VersionConfig? clientVersion = VersionConfig.Parse(from);
			if (clientVersion == null)
			{
				Log.Warning("PatchController", $"Invalid 'from' version: {from}");
				return BadRequest("Invalid 'from' version format.");
			}

			var latestVersion = VersionConfig.Parse(latest);
			if (latestVersion == null)
			{
				return StatusCode(500, "Internal server error: server latest version malformed.");
			}

			if (clientVersion >= latestVersion)
			{
				writer.AddString("latest_version", latest)
					  .AddBool("up_to_date", true);
				etagSource = "u:" + latest;
			}
			else
			{
				var entry = versionService.TryGetPatch(clientVersion.FullVersion, latestVersion.FullVersion);
				if (entry == null)
				{
					writer.AddString("latest_version", latest)
						  .AddBool("patch_available", false);
					etagSource = "n:" + clientVersion.FullVersion + "->" + latestVersion.FullVersion;
				}
				else
				{
					writer.AddString("latest_version", latest)
						  .AddBool("patch_available", true)
						  .AddString("sha256", entry.Sha256Hex)
						  .AddNumber("size", entry.Size);
					etagSource = entry.Sha256Hex;
				}
			}
		}

		// Weak ETag (W/...) — the body's exact byte representation can vary across
		// serializer versions; semantic equivalence is what we mean here.
		string etag = "W/\"" + etagSource + "\"";

		// Conditional GET: short-circuit with 304 when the client already has the
		// current answer cached.  Per RFC 7232, If-None-Match can contain a
		// comma-separated list of ETags; parse all values and match any one.
		var ifNoneMatch = Request.Headers["If-None-Match"].ToString();
		if (!string.IsNullOrEmpty(ifNoneMatch))
		{
			var etags = ifNoneMatch.Split(',').Select(e => e.Trim().Trim('"')).ToArray();
			if (etags.Contains(etag.Trim('"')))
			{
				Response.Headers["ETag"] = etag;
				Response.Headers["Cache-Control"] = "public, max-age=30";
				return StatusCode(StatusCodes.Status304NotModified);
			}
		}

		Response.Headers["ETag"] = etag;
		Response.Headers["Cache-Control"] = "public, max-age=30";

		// HMAC-sign the version manifest using the shared gate secret.
		// The canonical content is derived from the response payload fields.
		// The client can verify the signature using the same shared secret to
		// confirm the manifest was produced by an authentic patcher server
		// (defense against DNS spoofing or MITM proxy serving a tampered
		// version manifest).
		string canonicalContent = "latest_version=" + (latest ?? "") +
			"&etag_source=" + etagSource;
		string? signature = versionService.SignContent(canonicalContent);
		if (signature != null)
		{
			Response.Headers["X-FishMMO-Version-Signature"] = signature;
		}

		/* Sign before the HEAD short-circuit so a HEAD and a GET cannot disagree about whether
		 * this server is capable of producing a manifest at all. Ed25519 is deterministic
		 * (RFC 8032), so the same payload always yields the same signature and the ETag above
		 * stays a correct cache key. */
		string document;
		try
		{
			document = signer.BuildDocument(writer);
		}
		catch (Exception ex)
		{
			/* SignDocument throws only when its canonicalisation self-check fails, i.e. the
			 * document it is about to emit would NOT reproduce the bytes it signed. Serving it
			 * anyway would hand every client a manifest it must refuse; serving it unsigned would
			 * be worse. 500 is the honest answer. */
			Log.Error("PatchController", $"Refusing to serve an unverifiable version manifest: {ex.Message}");
			return StatusCode(500, "Internal server error: version manifest could not be signed.");
		}

		if (HttpMethods.IsHead(Request.Method))
		{
			// HEAD: emit headers only, no body.
			return new EmptyResult();
		}

		return Content(document, "application/json");
	}

	/// <summary>
	/// GET /{version}
	/// Streams the patch file that upgrades from <paramref name="version"/> to the
	/// server's latest version. Only patches indexed at startup are served; no
	/// caller-controlled string is ever concatenated into a filesystem path.
	/// The dedicated PatchDownload rate-limiting policy is applied here so
	/// download requests are throttled independently of metadata endpoints.
	/// </summary>
	[HttpGet("{version}")]
	[EnableRateLimiting("PatchDownload")]
	public IActionResult GetPatch(string version)
	{
		var latest = versionService.LatestVersion;
		if (string.IsNullOrEmpty(latest))
		{
			return StatusCode(500, "Latest version information not available on server.");
		}

		VersionConfig? clientVersion = VersionConfig.Parse(version);
		if (clientVersion == null)
		{
			Log.Warning("PatchController", $"Invalid client version format received: {version}");
			return BadRequest("Invalid client version format. Expected Major.Minor.Patch[.PreRelease].");
		}

		VersionConfig? latestVersion = VersionConfig.Parse(latest);
		if (latestVersion == null)
		{
			Log.Error("PatchController", $"Failed to parse server latest version '{latest}'.");
			return StatusCode(500, "Internal server error: latest server version malformed.");
		}

		if (clientVersion >= latestVersion)
		{
			// 204, not 200-with-a-body. This route streams a binary archive, and the
			// launcher writes the response straight to disk without inspecting it. A JSON
			// status document returned here would be saved as if it were the patch and
			// handed to the Updater. 204 is also what the launcher already tests for to
			// detect the up-to-date case.
			Log.Info("PatchController", $"Client {clientVersion.FullVersion} already up to date.");
			return NoContent();
		}

		var entry = versionService.TryGetPatch(clientVersion.FullVersion, latestVersion.FullVersion);
		if (entry == null)
		{
			Log.Warning("PatchController", $"No indexed patch from {clientVersion.FullVersion} to {latestVersion.FullVersion}.");
			return NotFound($"Patch file not found from version {clientVersion.FullVersion} to {latestVersion.FullVersion}.");
		}

		// Defense in depth: re-verify the indexed path still lives inside PatchesRoot
		// before opening it (guards against post-startup symlink swaps).
		if (!entry.FullPath.StartsWith(versionService.PatchesRoot, StringComparison.Ordinal))
		{
			Log.Error("PatchController", $"Refusing to serve patch with unsafe path: {entry.FullPath}");
			return StatusCode(500, "Internal server error.");
		}

		try
		{
			// Cheap defence against on-disk substitution after indexing: re-stat
			// the file and compare size against the indexed value. A full re-hash
			// is intentionally avoided on the hot path (the file may be many MB);
			// the launcher hashes the downloaded copy and rejects on mismatch.
			var info = new FileInfo(entry.FullPath);
			if (!info.Exists || info.Length != entry.Size)
			{
				Log.Error("PatchController",
					$"Refusing to serve '{entry.FullPath}': file vanished or size changed (indexed={entry.Size}, now={(info.Exists ? info.Length : -1)}). Trigger reindex.");
				return StatusCode(500, "Patch artifact is no longer available.");
			}

			// Reject symlinks/junctions. An attacker who can drop a symlink into the
			// patches directory but cannot replace the underlying file could otherwise
			// pivot the open() onto an arbitrary path (e.g. /etc/passwd). The patch
			// indexer only registers real regular files; a symlink at serve-time means
			// the tree has been tampered with since the last index.
			if ((info.Attributes & System.IO.FileAttributes.ReparsePoint) != 0)
			{
				Log.Error("PatchController", $"Refusing to serve '{entry.FullPath}': symlink/junction not allowed.");
				return StatusCode(500, "Patch artifact path failed sanity checks.");
			}

			// SequentialScan: this is a single linear download; hint to the OS to drop
			// pages aggressively rather than pollute the page cache.
			var fileStream = new FileStream(
				entry.FullPath,
				FileMode.Open,
				FileAccess.Read,
				FileShare.Read,
				4096,
				FileOptions.Asynchronous | FileOptions.SequentialScan);
			Response.Headers["X-Patch-Sha256"] = entry.Sha256Hex;
			Response.Headers["X-Patch-Size"] = entry.Size.ToString();
			Response.Headers["ETag"] = "\"" + entry.Sha256Hex + "\"";
			Response.Headers["Cache-Control"] = "public, max-age=3600, immutable";
			return new FileStreamResult(fileStream, "application/octet-stream")
			{
				FileDownloadName = $"{clientVersion.FullVersion}-{latestVersion.FullVersion}.zip",
				EnableRangeProcessing = true,
			};
		}
		catch (Exception ex)
		{
			Log.Error("PatchController", $"Error streaming patch file '{entry.FullPath}': {ex.Message}");
			return StatusCode(500, "Internal server error: could not access patch file.");
		}
	}
}
