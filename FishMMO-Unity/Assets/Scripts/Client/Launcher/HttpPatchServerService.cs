using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
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
		///
		/// <para><b>TODO (future version):</b> The version manifest response from
		/// <c>/latest_version</c> should include an HMAC signature computed over the
		/// manifest fields (server version, SHA-256, patch URL) using the
		/// <see cref="ClientApiSecret"/> shared key. The client would verify the
		/// HMAC before trusting the patch hash, preventing a MITM from substituting
		/// a malicious patch. Until this is implemented, the SHA-256 hash in the
		/// manifest is carried over a TLS-protected transport — the risk is that
		/// TLS alone does not protect against a compromised API gateway or
		/// mis-issued certificate.</para>
		///
		/// <para><b>Integrity note:</b> The downloaded patch file is verified against
		/// its SHA-256 hash, but the version manifest response (which contains the hash)
		/// is NOT cryptographically signed. An attacker who can MITM the
		/// <c>/latest_version</c> endpoint could substitute a malicious patch hash.
		/// Future hardening: sign the manifest with HMAC or Ed25519.</para>
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
						VersionFetch versionFetch = JsonUtility.FromJson<VersionFetch>(request.downloadHandler.text);

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
#endif

			Dictionary<string, string> headers = new Dictionary<string, string>();
			// Sign patch download requests so the public web gateway accepts them.
			ClientApiSigner.SignAndAdd(headers, UnityWebRequest.kHttpVerbGET, patchUrl);

			// Per-download, so a retry after a failure does not inherit the previous attempt's
			// rate history and report a throughput that is no longer happening.
			DownloadRateTracker rateTracker = new DownloadRateTracker();

			// Emitted before the request is sent so the UI opens on "0 B of 240 MB" rather than
			// an empty bar. Without this the player sees nothing at all until the first
			// progress callback, which on a slow connect is several seconds of blank UI.
			onProgress?.Invoke(new DownloadStats(0UL, expectedTotalBytes, 0, null, 0f));

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
				Timeout = this.EffectiveTimeout,
				OnProgress = (request, progress) =>
				{
					ulong downloaded = request.downloadedBytes;
					rateTracker.Sample(Time.realtimeSinceStartup, downloaded);

					onProgress?.Invoke(new DownloadStats(
						downloaded,
						expectedTotalBytes,
						rateTracker.BytesPerSecond,
						rateTracker.EstimateSecondsRemaining(downloaded, expectedTotalBytes),
						progress));
				},
				OnComplete = (request) =>
				{
#if !UNITY_WEBGL || UNITY_EDITOR
					// DownloadHandlerFile does not support .text; check response code only.
#else
					// DownloadHandlerBuffer was used; persist the downloaded bytes
					// to the filesystem at destinationFilePath.
					try
					{
						File.WriteAllBytes(destinationFilePath, request.downloadHandler.data);
					}
					catch (Exception ex)
					{
						onError?.Invoke($"Error writing downloaded patch to disk: {ex.Message}");
						TryDeleteTempFile(destinationFilePath);
						return;
					}
#endif
					if (request.responseCode == (long)HttpStatusCode.NoContent)
					{
						// Server indicates client is already up to date. Nothing was
						// written worth applying, so discard whatever landed on disk and
						// tell the caller there is no patch to hand to the Updater.
						TryDeleteTempFile(destinationFilePath);
						onProgress?.Invoke(new DownloadStats(0UL, 0L, 0, null, 1f));
						onComplete?.Invoke(false);
						return;
					}

					if (request.responseCode != (long)HttpStatusCode.OK)
					{
						onError?.Invoke($"Unexpected response code downloading patch: {request.responseCode}");
						TryDeleteTempFile(destinationFilePath);
						return;
					}

					// Marked complete rather than simply 100%: the shape check and SHA-256 pass
					// below stream the whole file, which on a large patch is seconds of work
					// after the transfer has visibly finished. Without saying so, the launcher
					// sits at a full bar looking hung.
					onProgress?.Invoke(new DownloadStats(
						request.downloadedBytes,
						expectedTotalBytes,
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
						return;
					}

					// SHA-256 verification (when an expected digest was supplied).
					if (!string.IsNullOrEmpty(expectedSha256))
					{
						string actual;
						try
						{
							actual = ComputeFileSha256Hex(destinationFilePath);
						}
						catch (Exception ex)
						{
							onError?.Invoke($"Error hashing downloaded patch: {ex.Message}");
							TryDeleteTempFile(destinationFilePath);
							return;
						}

						if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
						{
							Log.Error("HttpPatchServerService", $"Patch SHA-256 mismatch. expected={expectedSha256} actual={actual}");
							onError?.Invoke("Patch integrity check failed (SHA-256 mismatch). The downloaded file has been discarded.");
							TryDeleteTempFile(destinationFilePath);
							return;
						}
					}

					onComplete?.Invoke(true);
				},
				// request is null when the service rejected the config outright (bad URL).
				OnFailure = (request) =>
				{
					TryDeleteTempFile(destinationFilePath);
					onError?.Invoke($"Error downloading patch: {(request != null ? request.error : "the request could not be sent")}");
				}
			};

			yield return this.webRequestService.StartCoroutine(this.webRequestService.SendWebRequestWithRetries(config));
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
