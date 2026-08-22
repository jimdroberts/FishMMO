using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Tasks;
using FishMMO.Logging;
using UnityEngine;
using UnityEngine.Networking;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// Unity-backed patch service implementation for endpoint discovery, version checks, and patch downloads.
	/// </summary>
	public class HttpPatchServerService : MonoBehaviour, IPatchServerService
	{
		[Header("Dependencies")]
		/// <summary>
		/// Service for handling Unity web requests.
		/// </summary>
		[SerializeField]
		private UnityWebRequestService webRequestService;
		/// <summary>
		/// Service for handling Unity web requests.
		/// </summary>
		public UnityWebRequestService WebRequestService => webRequestService;

		[Header("Configuration")]
		/// <summary>
		/// Maximum number of retries for each web request.
		/// </summary>
		[Tooltip("Maximum number of retries for each web request.")]
		[SerializeField]
		private int maxRetries = 3;
		/// <summary>
		/// Maximum number of retries for each web request.
		/// </summary>
		public int MaxRetries => maxRetries;
		/// <summary>
		/// Delay in seconds between retries for web requests.
		/// </summary>
		[Tooltip("Delay in seconds between retries for web requests.")]
		[SerializeField]
		private float retryDelay = 1.0f;
		/// <summary>
		/// Delay in seconds between retries for web requests.
		/// </summary>
		public float RetryDelay => retryDelay;
		/// <summary>
		/// Timeout in seconds for each individual web request.
		/// </summary>
		[Tooltip("Timeout in seconds for each individual web request.")]
		[SerializeField]
		private int webRequestTimeout = 10;
		/// <summary>
		/// Timeout in seconds for each individual web request.
		/// </summary>
		public int WebRequestTimeout => webRequestTimeout;

		/// <summary>
		/// Seconds a patch download may make no progress before it is abandoned.
		/// </summary>
		/// <remarks>
		/// <b>B1.</b> Downloads get their own budget, and it is an idle timeout rather than a
		/// total-duration one. See <c>UnityWebRequestService.WebRequestConfig.IdleTimeout</c>:
		/// sharing the 10s whole-request timeout with the version check meant any patch too big
		/// to arrive within ten seconds could never be downloaded at all, on any connection.
		/// </remarks>
		[Tooltip("Seconds a patch download may stall with no data before it is abandoned.")]
		[SerializeField]
		private int patchIdleTimeoutSeconds = 60;

		/// <summary>
		/// Hard ceiling on the size of a patch archive, in bytes.
		/// </summary>
		/// <remarks>
		/// <b>H6.</b> Without a cap the transfer runs until the disk is full: the response is
		/// written straight to the filesystem by <c>DownloadHandlerFile</c>, and nothing else in
		/// the path looks at how much has arrived. The manifest's declared size is not a bound —
		/// it comes from the same response as everything else — so this is a fixed local limit
		/// instead. 8 GiB is far larger than any real patch and far smaller than a modern disk.
		/// </remarks>
		[Tooltip("Maximum accepted patch archive size in bytes. The download is aborted past this.")]
		[SerializeField]
		private long maxPatchBytes = 8L * 1024L * 1024L * 1024L;

		/*
		 * Resolved per request rather than cached, so a settings change applies to the next
		 * attempt instead of requiring a restart. The serialized field is the fallback: an
		 * install where the player has never touched these behaves exactly as it always has.
		 *
		 * Only this service honours them. The news fetcher deliberately runs with no retries
		 * because it is cosmetic, and a player raising the download retry count has not asked
		 * to spend that budget on the news pane.
		 */

		/// <summary>Retry count for this request, from settings or the serialized default.</summary>
		private int EffectiveMaxRetries => LauncherSettings.GetMaxRetries(this.maxRetries);
		/// <summary>Retry delay for this request, from settings or the serialized default.</summary>
		private float EffectiveRetryDelay => LauncherSettings.GetRetryDelay(this.retryDelay);
		/// <summary>Timeout for this request, from settings or the serialized default.</summary>
		private int EffectiveTimeout => LauncherSettings.GetRequestTimeout(this.webRequestTimeout);

		/// <summary>
		/// Unity Awake method. Validates dependencies and disables script if missing.
		/// </summary>
		private void Awake()
		{
			if (this.webRequestService == null)
			{
				// Disable only this component. Deactivating the GameObject would take the
				// sibling ClientLauncher down with it (they share one GameObject), killing
				// its in-flight coroutines and freezing the UI with no explanation. Both
				// public entry points below null-check and report through onError instead.
				Log.Error("HttpPatchServerService", "WebRequestService dependency is not assigned! This script will not function.");
				this.enabled = false;
			}
		}

		/// <summary>
		/// Fetches the latest version from the API gateway and returns it via callback.
		/// Also returns optional per-version <see cref="PatchInfo"/> (SHA-256, size,
		/// availability) when <paramref name="clientVersion"/> is supplied.
		/// </summary>
		/// <remarks>
		/// <para><b>Manifest integrity (was the TODO above this method; S4).</b> The response
		/// is verified with Ed25519 against
		/// <c>GeneratedPinSet.VersionManifestPublicKeyBase64</c> before ANY field in it is
		/// read. That matters because the manifest is trusted absolutely downstream: its
		/// <c>sha256</c> is the only integrity check applied to the patch archive, so whoever
		/// writes the manifest chooses which bytes the updater installs, and its
		/// <c>latest_version</c> becomes part of a file path. TLS authenticates the transport
		/// and nothing else — it says nothing about a compromised gateway, a mis-issued
		/// certificate, or a CDN edge serving a substituted document.</para>
		///
		/// <para><b>Fail-closed, with one deliberate exception.</b> When a verification key is
		/// embedded, a manifest that does not verify is discarded and the version check fails —
		/// no partial trust, no "use it but warn". When NO key is embedded, signing is not
		/// configured for this deployment: the client falls back to the previous
		/// SHA-256-over-pinned-TLS posture and says so at Error level on every check in a
		/// release build. That exception exists so operators can adopt signing without every
		/// existing install breaking the moment this ships; it is loud precisely so it does not
		/// become the permanent state. The editor build validator reports the same thing at
		/// build time.</para>
		/// </remarks>
		/// <param name="apiHost">The unified API host URL.</param>
		/// <param name="clientVersion">Client's current version string, or null/empty.</param>
		/// <param name="onComplete">Callback for successful version fetch.</param>
		/// <param name="onError">Callback for error handling.</param>
		/// <returns>Coroutine enumerator.</returns>
		public IEnumerator GetLatestVersion(string apiHost, string clientVersion, Action<VersionConfig, PatchInfo> onComplete, Action<string> onError)
		{
			if (this.webRequestService == null)
			{
				onError?.Invoke("PatchServerService not initialized due to missing WebRequestService.");
				yield break;
			}

			string url = apiHost + "latest_version";
			if (!string.IsNullOrEmpty(clientVersion))
			{
				url += "?from=" + UnityWebRequest.EscapeURL(clientVersion);
			}

			Dictionary<string, string> headers = new Dictionary<string, string>();
			// Sign the request so the public web gateway accepts it. See ClientApiSigner.
			ClientApiSigner.SignAndAdd(headers, UnityWebRequest.kHttpVerbGET, url);

			UnityWebRequestService.WebRequestConfig config = new UnityWebRequestService.WebRequestConfig
			{
				URL = url,
				Method = UnityWebRequest.kHttpVerbGET,
				Headers = headers,
				CertificateHandlerFactory = () => new ClientSSLCertificateHandler(),
				MaxRetries = this.EffectiveMaxRetries,
				RetryDelay = this.EffectiveRetryDelay,
				Timeout = this.EffectiveTimeout,
				OnProgress = null,
				OnComplete = (request) =>
				{
					try
					{
						string responseJson = request.downloadHandler.text;

						VersionFetch versionFetch = JsonUtility.FromJson<VersionFetch>(responseJson);

						/* Verified BEFORE any field is trusted — including latest_version, which
						 * becomes half of a file name, and sha256, which is the only thing
						 * standing between the updater and an attacker-chosen archive. Parsing
						 * first is unavoidable (the signature is a field of the document), but
						 * nothing is acted on until this returns. */
						if (!VerifyVersionManifest(responseJson, versionFetch.signature, out string signatureError))
						{
							onError?.Invoke(signatureError);
							return;
						}

						// VersionConfig.Parse returns null on malformed input — it does not
						// throw. Without this guard a blank or malformed latest_version
						// propagates a null through onComplete and NREs in the caller's
						// coroutine, which Unity swallows, leaving the launcher wedged on
						// "Checking Version..." with no way forward.
						VersionConfig serverVersion = VersionConfig.Parse(versionFetch.latest_version);
						if (serverVersion == null)
						{
							onError?.Invoke($"Server returned an unusable version string: '{versionFetch.latest_version}'. Expected Major.Minor.Patch[.PreRelease].");
							return;
						}

						PatchInfo info = new PatchInfo
						{
							UpToDate = versionFetch.up_to_date,
							PatchAvailable = versionFetch.patch_available,
							Sha256 = versionFetch.sha256,
							Size = versionFetch.size,
						};
						onComplete?.Invoke(serverVersion, info);
					}
					catch (Exception ex)
					{
						onError?.Invoke($"Error parsing latest version JSON: {ex.Message}");
					}
				},
				// request is null when the service rejected the config outright (bad URL).
				OnFailure = (request) => onError?.Invoke($"Error fetching latest version: {(request != null ? request.error : "the request could not be sent")}")
			};

			yield return this.webRequestService.StartCoroutine(this.webRequestService.SendWebRequestWithRetries(config));
		}

		/// <summary>
		/// Downloads a patch file from the given URL to a temporary file path, reporting progress and completion via callbacks.
		/// When <paramref name="expectedSha256"/> is non-empty, the downloaded file is
		/// hashed after the transfer completes and the operation fails if the digest
		/// does not match (the partial file is deleted).
		/// </summary>
		/// <param name="patchUrl">The URL to download the patch from.</param>
		/// <param name="destinationFilePath">Full path (including file name) to save the patch archive to.</param>
		/// <param name="expectedSha256">Lowercase hex SHA-256 the downloaded file must match, or null/empty to skip verification.</param>
		/// <param name="expectedTotalBytes">Expected size from the version manifest, or 0 when unknown.</param>
		/// <param name="onComplete">Callback for successful download. The argument is false when the server reported the client is already up to date.</param>
		/// <param name="onError">Callback for error handling.</param>
		/// <param name="onProgress">Callback for progress updates.</param>
		/// <returns>Coroutine enumerator.</returns>
		public IEnumerator DownloadPatch(string patchUrl, string destinationFilePath, string expectedSha256, long expectedTotalBytes, Action<bool> onComplete, Action<string> onError, Action<DownloadStats> onProgress)
		{
			if (this.webRequestService == null)
			{
				onError?.Invoke("PatchServerService not initialized due to missing WebRequestService.");
				yield break;
			}

			if (string.IsNullOrWhiteSpace(destinationFilePath))
			{
				onError?.Invoke("No destination path was supplied for the patch download.");
				yield break;
			}

			// DownloadHandlerFile does not create missing parent directories; it fails the
			// request instead. The Patches directory does not exist in a fresh install.
			try
			{
				string destinationDirectory = Path.GetDirectoryName(destinationFilePath);
				if (!string.IsNullOrEmpty(destinationDirectory))
				{
					Directory.CreateDirectory(destinationDirectory);
				}
			}
			catch (Exception ex)
			{
				onError?.Invoke($"Could not create the patch directory for '{destinationFilePath}': {ex.Message}");
				yield break;
			}

#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
			// Release builds: refuse to download without a SHA-256 integrity check.
			// This prevents shipping a corrupted or tampered patch to end users.
			if (string.IsNullOrEmpty(expectedSha256))
			{
				onError?.Invoke("Release build requires a SHA-256 checksum for patch download integrity. The server did not provide one.");
				yield break;
			}
#else
			/* S7: the release-only guard above is compiled out here, so say so.
			 *
			 * A development build downloading a patch with no digest was previously silent, and
			 * silence is indistinguishable from "the check ran and passed" — which is how a
			 * server that stopped sending a hash goes unnoticed right up until a release build
			 * refuses to start. The behaviour is deliberate (a dev patch server has no reason to
			 * publish digests), so it is stated loudly rather than changed. */
			if (string.IsNullOrEmpty(expectedSha256))
			{
				Log.Warning("HttpPatchServerService",
					"INTEGRITY CHECK SKIPPED: no SHA-256 was supplied for this patch, and this is a development build. " +
					"A release build would refuse this download outright. The archive will be applied unverified.");
			}
#endif

			Dictionary<string, string> headers = new Dictionary<string, string>();
			// Sign patch download requests so the public web gateway accepts them.
			ClientApiSigner.SignAndAdd(headers, UnityWebRequest.kHttpVerbGET, patchUrl);

			// Per-download, so a retry after a failure does not inherit the previous attempt's
			// rate history and report a throughput that is no longer happening.
			DownloadRateTracker rateTracker = new DownloadRateTracker();

			/* H7: the progress total is a HINT, not a fact.
			 *
			 * It arrives in the same manifest as everything else, so a hostile or simply wrong
			 * server can report a size unrelated to what it is about to send — which shows the
			 * player a bar that fills to 3% and stops, or one that claims completion a tenth of
			 * the way through. Clamped to the same local ceiling that bounds the transfer, and
			 * a negative or absurd value is discarded entirely so the UI falls back to "bytes
			 * received, total unknown" instead of drawing a lie. */
			long displayTotalBytes = expectedTotalBytes;
			if (displayTotalBytes < 0 || displayTotalBytes > this.maxPatchBytes)
			{
				Log.Warning("HttpPatchServerService",
					$"Ignoring implausible patch size from the manifest ({expectedTotalBytes} bytes); progress will show received bytes only.");
				displayTotalBytes = 0;
			}

			// Emitted before the request is sent so the UI opens on "0 B of 240 MB" rather than
			// an empty bar. Without this the player sees nothing at all until the first
			// progress callback, which on a slow connect is several seconds of blank UI.
			onProgress?.Invoke(new DownloadStats(0UL, displayTotalBytes, 0, null, 0f));

			/* The outcome is recorded here and acted on AFTER the request coroutine returns,
			 * rather than inside OnComplete.
			 *
			 * H4: verifying the archive means streaming the whole file through SHA-256, which on
			 * a multi-gigabyte patch is seconds of solid work. Inside OnComplete that runs on
			 * the main thread with no opportunity to yield, so the launcher froze at a full
			 * progress bar for exactly as long as the hash took — the one moment the player is
			 * most likely to think it has crashed. Out here the coroutine can hand the hashing
			 * to a worker thread and poll it. */
			bool requestSucceeded = false;
			long responseCode = 0;
			ulong receivedBytes = 0;
			string failureMessage = null;
#if UNITY_WEBGL && !UNITY_EDITOR
			byte[] webglPayload = null;
#endif

			UnityWebRequestService.WebRequestConfig config = new UnityWebRequestService.WebRequestConfig
			{
				URL = patchUrl,
				Method = UnityWebRequest.kHttpVerbGET,
				Headers = headers,
				CertificateHandlerFactory = () => new ClientSSLCertificateHandler(),
#if !UNITY_WEBGL || UNITY_EDITOR
				// DownloadHandlerFile writes directly to disk via the OS filesystem.
				// WebGL runs under Emscripten's MEMFS (in-memory virtual FS); the
				// file is lost when the tab closes and may silently fail on large
				// writes. Fall back to DownloadHandlerBuffer + manual write below.
				// removeFileOnAbort: the handler leaves whatever it has already written on disk
				// when the request is aborted rather than completed. The launcher aborts any
				// in-flight request when it is torn down, which happens the moment the player
				// launches the game — so without this a quit mid-download strands a truncated
				// archive at the exact path the next run treats as its patch file.
				DownloadHandlerFactory = () => new DownloadHandlerFile(destinationFilePath) { removeFileOnAbort = true },
#else
				DownloadHandlerFactory = () => new DownloadHandlerBuffer(),
#endif
				MaxRetries = this.EffectiveMaxRetries,
				RetryDelay = this.EffectiveRetryDelay,
				/* B1: no whole-request deadline for a file transfer. The shared 10s timeout made
				 * any patch too large for the player's link permanently undownloadable — not
				 * slow, impossible, and identically so on every retry. Bounded by idle time
				 * instead, which is the question that distinguishes "slow" from "stopped". */
				Timeout = 0,
				IdleTimeout = Mathf.Max(5, this.patchIdleTimeoutSeconds),
				MaxResponseBytes = this.maxPatchBytes,
				OnProgress = (request, progress) =>
				{
					ulong downloaded = request.downloadedBytes;
					rateTracker.Sample(Time.realtimeSinceStartup, downloaded);

					onProgress?.Invoke(new DownloadStats(
						downloaded,
						displayTotalBytes,
						rateTracker.BytesPerSecond,
						rateTracker.EstimateSecondsRemaining(downloaded, displayTotalBytes),
						progress));
				},
				OnComplete = (request) =>
				{
					requestSucceeded = true;
					responseCode = request.responseCode;
					receivedBytes = request.downloadedBytes;
#if UNITY_WEBGL && !UNITY_EDITOR
					webglPayload = request.downloadHandler?.data;
#endif
				},
				// request is null when the service rejected the config outright (bad URL).
				OnFailure = (request) =>
				{
					failureMessage = $"Error downloading patch: {(request != null ? request.error : "the request could not be sent")}";
				}
			};

			yield return this.webRequestService.StartCoroutine(this.webRequestService.SendWebRequestWithRetries(config));

			if (!requestSucceeded)
			{
				TryDeleteTempFile(destinationFilePath);
				onError?.Invoke(failureMessage ?? "Error downloading patch: the request did not complete.");
				yield break;
			}

#if UNITY_WEBGL && !UNITY_EDITOR
			// DownloadHandlerBuffer was used; persist the downloaded bytes to the filesystem.
			if (responseCode == (long)HttpStatusCode.OK)
			{
				bool written = false;
				try
				{
					File.WriteAllBytes(destinationFilePath, webglPayload ?? System.Array.Empty<byte>());
					written = true;
				}
				catch (Exception ex)
				{
					onError?.Invoke($"Error writing downloaded patch to disk: {ex.Message}");
					TryDeleteTempFile(destinationFilePath);
				}
				if (!written)
				{
					yield break;
				}
			}
#endif

			if (responseCode == (long)HttpStatusCode.NoContent)
			{
				// Server indicates client is already up to date. Nothing was
				// written worth applying, so discard whatever landed on disk and
				// tell the caller there is no patch to hand to the Updater.
				TryDeleteTempFile(destinationFilePath);
				onProgress?.Invoke(new DownloadStats(0UL, 0L, 0, null, 1f));
				onComplete?.Invoke(false);
				yield break;
			}

			if (responseCode != (long)HttpStatusCode.OK)
			{
				onError?.Invoke($"Unexpected response code downloading patch: {responseCode}");
				TryDeleteTempFile(destinationFilePath);
				yield break;
			}

			// Marked complete rather than simply 100%: the shape check and SHA-256 pass
			// below stream the whole file, which on a large patch is seconds of work
			// after the transfer has visibly finished. Without saying so, the launcher
			// sits at a full bar looking hung.
			onProgress?.Invoke(new DownloadStats(
				receivedBytes,
				displayTotalBytes,
				0,
				null,
				1f,
				isComplete: true));

			// Shape check before anything downstream trusts this file. A 200 with a
			// JSON/HTML body (an error page, or a gateway that answers the patch
			// route with a status document) is written to disk verbatim by
			// DownloadHandlerFile and would otherwise be handed to the Updater as a
			// "patch". Cheap, and it fires before the hash comparison so the
			// resulting message names the real problem.
			if (!IsZipArchive(destinationFilePath))
			{
				Log.Error("HttpPatchServerService", $"Downloaded patch at '{destinationFilePath}' is not a ZIP archive.");
				onError?.Invoke("The server did not return a patch archive. The download has been discarded.");
				TryDeleteTempFile(destinationFilePath);
				yield break;
			}

			// SHA-256 verification (when an expected digest was supplied).
			if (!string.IsNullOrEmpty(expectedSha256))
			{
				/* H4: hashed on a worker thread, polled from here.
				 *
				 * File I/O and SHA-256 are plain .NET with no Unity API involved, so the work is
				 * safe off the main thread; only the result crosses back, and it crosses back
				 * into a coroutine, so everything after this is on the main thread again and
				 * free to touch UI. */
				Task<string> hashTask = Task.Run(() => ComputeFileSha256Hex(destinationFilePath));
				while (!hashTask.IsCompleted)
				{
					yield return null;
				}

				if (hashTask.IsFaulted)
				{
					string reason = hashTask.Exception?.GetBaseException().Message ?? "unknown error";
					onError?.Invoke($"Error hashing downloaded patch: {reason}");
					TryDeleteTempFile(destinationFilePath);
					yield break;
				}

				string actual = hashTask.Result;
				if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
				{
					Log.Error("HttpPatchServerService", $"Patch SHA-256 mismatch. expected={expectedSha256} actual={actual}");
					onError?.Invoke("Patch integrity check failed (SHA-256 mismatch). The downloaded file has been discarded.");
					TryDeleteTempFile(destinationFilePath);
					yield break;
				}
			}

			onComplete?.Invoke(true);
		}

		/// <summary>
		/// Verifies the Ed25519 signature on a version manifest, or reports why signing is not
		/// in force.
		/// </summary>
		/// <param name="responseJson">The raw response body, exactly as received.</param>
		/// <param name="signatureBase64">The <c>signature</c> field from that body.</param>
		/// <param name="error">Player-facing reason when this returns false.</param>
		/// <returns>True when the manifest may be trusted.</returns>
		/// <remarks>
		/// Split out so the fail-closed rule is one readable block rather than four branches
		/// woven through a JSON callback. The rule: a configured key makes a valid signature
		/// mandatory; no configured key means signing is not deployed yet, which is reported at
		/// Error level in a release build and at Debug in the editor, and is the ONLY path that
		/// returns true without a verified signature.
		/// </remarks>
		private static bool VerifyVersionManifest(string responseJson, string signatureBase64, out string error)
		{
			error = null;

			if (!FishMMO.Client.Security.Ed25519ManifestVerifier.TryDecodePublicKey(
					FishMMO.Client.Security.GeneratedPinSet.VersionManifestPublicKeyBase64,
					out byte[] publicKey))
			{
				/* No usable key. Note that this also covers a key that IS configured but is
				 * malformed — TryDecodePublicKey has already logged which. Treating a broken key
				 * as "signing disabled" is the wrong instinct in general, but the alternative
				 * here is a client that cannot update at all because of a typo in a generated
				 * file, and the fallback is the same posture the client shipped with. It is
				 * loud, and the build validator catches it before release. */
#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
				Log.Error("HttpPatchServerService",
					"VERSION MANIFEST IS UNSIGNED: no Ed25519 verification key is embedded in this build. " +
					"The patch SHA-256 is therefore only as trustworthy as the API gateway serving it. " +
					"Configure GeneratedPinSet.VersionManifestPublicKeyBase64 and sign /latest_version.");
#else
				Log.Debug("HttpPatchServerService",
					"Version manifest signature verification is disabled (no key configured). Release builds report this at Error level.");
#endif
				return true;
			}

			if (FishMMO.Client.Security.Ed25519ManifestVerifier.Verify(publicKey, responseJson, signatureBase64))
			{
				return true;
			}

			/* Fail closed. The message deliberately does not distinguish "unsigned" from
			 * "badly signed" to the player: both mean the same thing — this response cannot be
			 * shown to have come from the release key — and the log already carries the detail
			 * for whoever can act on it. */
			Log.Error("HttpPatchServerService",
				"Version manifest signature verification FAILED. Refusing to trust the version or the patch hash it carries.");
			error = "The update server's response could not be verified. Please try again later.";
			return false;
		}

		/// <summary>
		/// Returns true when the file at <paramref name="filePath"/> begins with the ZIP
		/// local-file-header magic (<c>PK\x03\x04</c>).
		/// </summary>
		/// <remarks>
		/// Only the first four bytes are read. This is a shape check, not an integrity
		/// check — SHA-256 verification remains the authority on content.
		/// </remarks>
		private static bool IsZipArchive(string filePath)
		{
			try
			{
				using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
				{
					byte[] header = new byte[4];
					int read = stream.Read(header, 0, header.Length);
					return read == 4 &&
						   header[0] == 0x50 && header[1] == 0x4B &&
						   header[2] == 0x03 && header[3] == 0x04;
				}
			}
			catch (Exception ex)
			{
				Log.Warning("HttpPatchServerService", $"Could not inspect downloaded patch '{filePath}': {ex.Message}");
				return false;
			}
		}

		/// <summary>
		/// Streams the file at <paramref name="filePath"/> through SHA-256 and returns
		/// the lowercase hexadecimal digest.
		/// </summary>
		private static string ComputeFileSha256Hex(string filePath)
		{
			using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
			using (SHA256 sha = SHA256.Create())
			{
				byte[] hash = sha.ComputeHash(stream);
				char[] chars = new char[hash.Length * 2];
				const string HexChars = "0123456789abcdef";
				for (int i = 0; i < hash.Length; i++)
				{
					byte b = hash[i];
					chars[i * 2] = HexChars[b >> 4];
					chars[i * 2 + 1] = HexChars[b & 0x0F];
				}
				return new string(chars);
			}
		}

		/// <summary>
		/// Best-effort deletion of a partial / rejected download. Errors are logged but
		/// not propagated.
		/// </summary>
		private static void TryDeleteTempFile(string filePath)
		{
			if (string.IsNullOrEmpty(filePath))
			{
				return;
			}
			try
			{
				if (File.Exists(filePath))
				{
					File.Delete(filePath);
				}
			}
			catch (Exception ex)
			{
				Log.Warning("HttpPatchServerService", $"Failed to delete temp patch file {filePath}: {ex.Message}");
			}
		}
	}
}
