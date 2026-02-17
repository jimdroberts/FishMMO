using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
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
		/// </summary>
		/// <param name="apiHost">The unified API host URL.</param>
		/// <param name="onComplete">Callback for successful version fetch.</param>
		/// <param name="onError">Callback for error handling.</param>
		/// <returns>Coroutine enumerator.</returns>
		public IEnumerator GetLatestVersion(string apiHost, Action<VersionConfig> onComplete, Action<string> onError)
		{
			if (WebRequestService == null)
			{
				onError?.Invoke("PatchServerService not initialized due to missing WebRequestService.");
				yield break;
			}

			UnityWebRequestService.WebRequestConfig config = new UnityWebRequestService.WebRequestConfig
			{
				URL = apiHost + "latest_version",
				Method = UnityWebRequest.kHttpVerbGET,
				Headers = new Dictionary<string, string>
				{
					{ "X-FishMMO", "Client" }
				},
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
						onComplete?.Invoke(serverVersion);
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
		/// </summary>
		/// <param name="patchUrl">The URL to download the patch from.</param>
		/// <param name="tempFilePath">The temporary file path to save the patch.</param>
		/// <param name="onComplete">Callback for successful download.</param>
		/// <param name="onError">Callback for error handling.</param>
		/// <param name="onProgress">Callback for progress updates.</param>
		/// <returns>Coroutine enumerator.</returns>
		public IEnumerator DownloadPatch(string patchUrl, string tempFilePath, Action onComplete, Action<string> onError, Action<float, string> onProgress)
		{
			if (WebRequestService == null)
			{
				onError?.Invoke("PatchServerService not initialized due to missing WebRequestService.");
				yield break;
			}

			UnityWebRequestService.WebRequestConfig config = new UnityWebRequestService.WebRequestConfig
			{
				URL = patchUrl,
				Method = UnityWebRequest.kHttpVerbGET,
				Headers = new Dictionary<string, string>
				{
					{ "X-FishMMO", "Client" }
				},
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
					if (request.responseCode == (long)HttpStatusCode.OK)
					{
						onProgress?.Invoke(1f, "100%");
					}
					else if (request.responseCode == (long)HttpStatusCode.NoContent)
					{
						// Server indicates client is already up to date.
						onProgress?.Invoke(1f, "100% (Already Updated)");
					}
					onComplete?.Invoke();
				},
				OnFailure = (request) => onError?.Invoke($"Error downloading patch: {request.error}")
			};

			yield return WebRequestService.StartCoroutine(WebRequestService.SendWebRequestWithRetries(config));
		}
	}
}