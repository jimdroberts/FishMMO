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
	/// Concrete implementation of DefaultPatchServerService using UnityWebRequestService.
	/// This class inherits from DefaultPatchServerService, making it assignable in the Inspector.
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
		/// Fetches the patch server address from the given host and returns it via callback.
		/// </summary>
		/// <param name="ipFetchHost">The host URL to fetch the patch server address from.</param>
		/// <param name="onComplete">Callback for successful address fetch.</param>
		/// <param name="onError">Callback for error handling.</param>
		/// <returns>Coroutine enumerator.</returns>
		public IEnumerator GetPatchServerAddress(string ipFetchHost, Action<ServerAddress> onComplete, Action<string> onError)
		{
			if (WebRequestService == null)
			{
				onError?.Invoke("PatchServerService not initialized due to missing WebRequestService.");
				yield break;
			}

			UnityWebRequestService.WebRequestConfig config = new UnityWebRequestService.WebRequestConfig
			{
				URL = ipFetchHost,
				Method = UnityWebRequest.kHttpVerbGET,
				Headers = new Dictionary<string, string>
				{
					{ "X-FishMMO", "Client" }
				},
				MaxRetries = MaxRetries,
				RetryDelay = RetryDelay,
				Timeout = WebRequestTimeout,
				OnProgress = null,
				OnComplete = (request) =>
				{
					try
					{
						ServerAddress server = JsonUtility.FromJson<ServerAddress>(request.downloadHandler.text);
						if (string.IsNullOrEmpty(server.Address) || server.Port == 0)
						{
							throw new Exception("Invalid or empty patch server address found in response.");
						}
						onComplete?.Invoke(server);
					}
					catch (Exception ex)
					{
						onError?.Invoke($"Error parsing patch server address JSON: {ex.Message}");
					}
				},
				OnFailure = (result) => onError?.Invoke($"Error fetching patch server list: {result}")
			};

			yield return WebRequestService.StartCoroutine(WebRequestService.SendWebRequestWithRetries(config));
		}

		/// <summary>
		/// Fetches the latest version from the patcher host and returns it via callback.
		/// </summary>
		/// <param name="patcherHost">The patcher host URL.</param>
		/// <param name="onComplete">Callback for successful version fetch.</param>
		/// <param name="onError">Callback for error handling.</param>
		/// <returns>Coroutine enumerator.</returns>
		public IEnumerator GetLatestVersion(string patcherHost, Action<VersionConfig> onComplete, Action<string> onError)
		{
			if (WebRequestService == null)
			{
				onError?.Invoke("PatchServerService not initialized due to missing WebRequestService.");
				yield break;
			}

			UnityWebRequestService.WebRequestConfig config = new UnityWebRequestService.WebRequestConfig
			{
				URL = patcherHost + "latest_version",
				Method = UnityWebRequest.kHttpVerbGET,
				Headers = new Dictionary<string, string>
				{
					{ "X-FishMMO", "Client" }
				},
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
				OnFailure = (result) => onError?.Invoke($"Error fetching latest version: {result}")
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
				DownloadHandler = new DownloadHandlerFile(tempFilePath),
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
					if (request.responseCode == (long)HttpStatusCode.OK || request.downloadHandler.text.Contains("AlreadyUpdated"))
					{
						onProgress?.Invoke(1f, "100% (Already Updated)");
					}
					onComplete?.Invoke();
				},
				OnFailure = (result) => onError?.Invoke($"Error downloading patch: {result}")
			};

			yield return WebRequestService.StartCoroutine(WebRequestService.SendWebRequestWithRetries(config));
		}
	}
}