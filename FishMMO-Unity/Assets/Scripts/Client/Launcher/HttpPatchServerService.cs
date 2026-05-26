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
		public UnityWebRequestService WebRequestService;

		[Header("Configuration")]
		/// <summary>
		/// Maximum number of retries for each web request.
		/// </summary>
		[Tooltip("Maximum number of retries for each web request.")]
		public int MaxRetries = 3;
		/// <summary>
		/// Delay in seconds between retries for web requests.
		/// </summary>
		[Tooltip("Delay in seconds between retries for web requests.")]
		public float RetryDelay = 1.0f;
		/// <summary>
		/// Timeout in seconds for each individual web request.
		/// </summary>
		[Tooltip("Timeout in seconds for each individual web request.")]
		public int WebRequestTimeout = 10;

		/// <summary>
		/// Unity Awake method. Validates dependencies and disables script if missing.
		/// </summary>
		private void Awake()
		{
			if (WebRequestService == null)
			{
				Log.Error("HttpPatchServerService", "WebRequestService dependency is not assigned! This script will not function.");
				this.gameObject.SetActive(false);
			}
		}

		/// <summary>
		/// Fetches the latest version from the API gateway and returns it via callback.
		/// Also returns optional per-version <see cref="PatchInfo"/> (SHA-256, size,
		/// availability) when <paramref name="clientVersion"/> is supplied.
		/// </summary>
		/// <param name="apiHost">The unified API host URL.</param>
		/// <param name="clientVersion">Client's current version string, or null/empty.</param>
		/// <param name="onComplete">Callback for successful version fetch.</param>
		/// <param name="onError">Callback for error handling.</param>
		/// <returns>Coroutine enumerator.</returns>
		public IEnumerator GetLatestVersion(string apiHost, string clientVersion, Action<VersionConfig, PatchInfo> onComplete, Action<string> onError)
		{
			if (WebRequestService == null)
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
				MaxRetries = MaxRetries,
				RetryDelay = RetryDelay,
				Timeout = WebRequestTimeout,
				OnProgress = null,
				OnComplete = (request) =>
				{
					try
					{
						VersionFetch versionFetch = JsonUtility.FromJson<VersionFetch>(request.downloadHandler.text);
						VersionConfig serverVersion = VersionConfig.Parse(versionFetch.latest_version);
						PatchInfo info = new PatchInfo
						{
							UpToDate = versionFetch.up_to_date,
							PatchAvailable = versionFetch.patch_available,
							Sha256 = versionFetch.sha256,
							Size = versionFetch.size,
						};
						onComplete?.Invoke(serverVersion, info);
					}
					catch (ArgumentException ex)
					{
						onError?.Invoke($"Invalid server version format: {ex.Message}");
					}
					catch (Exception ex)
					{
						onError?.Invoke($"Error parsing latest version JSON: {ex.Message}");
					}
				},
				OnFailure = (request) => onError?.Invoke($"Error fetching latest version: {request.error}")
			};

			yield return WebRequestService.StartCoroutine(WebRequestService.SendWebRequestWithRetries(config));
		}

		/// <summary>
		/// Downloads a patch file from the given URL to a temporary file path, reporting progress and completion via callbacks.
		/// When <paramref name="expectedSha256"/> is non-empty, the downloaded file is
		/// hashed after the transfer completes and the operation fails if the digest
		/// does not match (the partial file is deleted).
		/// </summary>
		/// <param name="patchUrl">The URL to download the patch from.</param>
		/// <param name="tempFilePath">The temporary file path to save the patch.</param>
		/// <param name="expectedSha256">Lowercase hex SHA-256 the downloaded file must match, or null/empty to skip verification.</param>
		/// <param name="onComplete">Callback for successful download.</param>
		/// <param name="onError">Callback for error handling.</param>
		/// <param name="onProgress">Callback for progress updates.</param>
		/// <returns>Coroutine enumerator.</returns>
		public IEnumerator DownloadPatch(string patchUrl, string tempFilePath, string expectedSha256, Action onComplete, Action<string> onError, Action<float, string> onProgress)
		{
			if (WebRequestService == null)
			{
				onError?.Invoke("PatchServerService not initialized due to missing WebRequestService.");
				yield break;
			}

			Dictionary<string, string> headers = new Dictionary<string, string>();
			// Sign patch download requests so the public web gateway accepts them.
			ClientApiSigner.SignAndAdd(headers, UnityWebRequest.kHttpVerbGET, patchUrl);

			UnityWebRequestService.WebRequestConfig config = new UnityWebRequestService.WebRequestConfig
			{
				URL = patchUrl,
				Method = UnityWebRequest.kHttpVerbGET,
				Headers = headers,
				CertificateHandlerFactory = () => new ClientSSLCertificateHandler(),
				DownloadHandlerFactory = () => new DownloadHandlerFile(tempFilePath),
				MaxRetries = MaxRetries,
				RetryDelay = RetryDelay,
				Timeout = WebRequestTimeout,
				OnProgress = (request, progress) =>
				{
					string progressText = $"{Mathf.RoundToInt(progress * 100f)}% ({WebRequestService.FormatBytes(request.downloadedBytes)})";
					onProgress?.Invoke(progress, progressText);
				},
				OnComplete = (request) =>
				{
					// DownloadHandlerFile does not support .text; check response code only.
					if (request.responseCode == (long)HttpStatusCode.NoContent)
					{
						// Server indicates client is already up to date.
						onProgress?.Invoke(1f, "100% (Already Updated)");
						onComplete?.Invoke();
						return;
					}

					if (request.responseCode != (long)HttpStatusCode.OK)
					{
						onError?.Invoke($"Unexpected response code downloading patch: {request.responseCode}");
						TryDeleteTempFile(tempFilePath);
						return;
					}

					onProgress?.Invoke(1f, "100%");

					// SHA-256 verification (when an expected digest was supplied).
					if (!string.IsNullOrEmpty(expectedSha256))
					{
						string actual;
						try
						{
							actual = ComputeFileSha256Hex(tempFilePath);
						}
						catch (Exception ex)
						{
							onError?.Invoke($"Error hashing downloaded patch: {ex.Message}");
							TryDeleteTempFile(tempFilePath);
							return;
						}

						if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
						{
							Log.Error("HttpPatchServerService", $"Patch SHA-256 mismatch. expected={expectedSha256} actual={actual}");
							onError?.Invoke("Patch integrity check failed (SHA-256 mismatch). The downloaded file has been discarded.");
							TryDeleteTempFile(tempFilePath);
							return;
						}
					}

					onComplete?.Invoke();
				},
				OnFailure = (request) =>
				{
					TryDeleteTempFile(tempFilePath);
					onError?.Invoke($"Error downloading patch: {request.error}");
				}
			};

			yield return WebRequestService.StartCoroutine(WebRequestService.SendWebRequestWithRetries(config));
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