using System;
using System.Collections;
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
		public UnityWebRequestService WebRequestService;

		[Header("Configuration")]
		[Tooltip("Maximum number of retries for each web request.")]
		public int MaxRetries = 3;
		[Tooltip("Delay in seconds between retries for web requests.")]
		public float RetryDelay = 1.0f;
		[Tooltip("Timeout in seconds for each individual web request.")]
		public int WebRequestTimeout = 10;

		private void Awake()
		{
			if (WebRequestService == null)
			{
				Log.Error("HttpPatchServerService", "WebRequestService dependency is not assigned! This script will not function.");
				this.gameObject.SetActive(false);
			}
		}

		/// <inheritdoc/>
		public IEnumerator GetPatchServerAddress(string ipFetchHost, Action<ServerAddress> onComplete, Action<string> onError)
		{
			if (WebRequestService == null)
			{
				onError?.Invoke("PatchServerService not initialized due to missing WebRequestService.");
				yield break;
			}

			using (UnityWebRequest www = UnityWebRequest.Get(ipFetchHost))
			{
				www.SetRequestHeader("X-FishMMO", "Client");
				yield return WebRequestService.StartCoroutine(
					WebRequestService.SendWebRequestWithRetries(www, MaxRetries, RetryDelay, WebRequestTimeout));

				if (www.result != UnityWebRequest.Result.Success)
				{
					onError?.Invoke($"Error fetching patch server list: {www.error}");
					yield break;
				}

				try
				{
					ServerAddress server = JsonUtility.FromJson<ServerAddress>(www.downloadHandler.text);
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
			}
		}

		/// <inheritdoc/>
		public IEnumerator GetLatestVersion(string patcherHost, Action<VersionConfig> onComplete, Action<string> onError)
		{
			if (WebRequestService == null)
			{
				onError?.Invoke("PatchServerService not initialized due to missing WebRequestService.");
				yield break;
			}

			using (UnityWebRequest www = UnityWebRequest.Get(patcherHost + "latest_version"))
			{
				www.SetRequestHeader("X-FishMMO", "Client");
				yield return WebRequestService.StartCoroutine(
					WebRequestService.SendWebRequestWithRetries(www, MaxRetries, RetryDelay, WebRequestTimeout));

				if (www.result != UnityWebRequest.Result.Success)
				{
					onError?.Invoke($"Error fetching latest version: {www.error}");
					yield break;
				}

				try
				{
					VersionFetch versionFetch = JsonUtility.FromJson<VersionFetch>(www.downloadHandler.text);
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
			}
		}

		/// <inheritdoc/>
		public IEnumerator DownloadPatch(string patchUrl, string tempFilePath, Action onComplete, Action<string> onError, Action<float, string> onProgress)
		{
			if (WebRequestService == null)
			{
				onError?.Invoke("PatchServerService not initialized due to missing WebRequestService.");
				yield break;
			}

			using (UnityWebRequest www = UnityWebRequest.Get(patchUrl))
			{
				www.SetRequestHeader("X-FishMMO", "Client");
				www.downloadHandler = new DownloadHandlerFile(tempFilePath);

				yield return WebRequestService.StartCoroutine(
					WebRequestService.SendWebRequestWithRetries(www, MaxRetries, RetryDelay, WebRequestTimeout, (progress) =>
				{
					string progressText = $"{Mathf.RoundToInt(progress * 100f)}% ({WebRequestService.FormatBytes(www.downloadedBytes)})";
					onProgress?.Invoke(progress, progressText);
				}));

				if (www.result != UnityWebRequest.Result.Success)
				{
					onError?.Invoke($"Error downloading patch: {www.error}");
					yield break;
				}

				if (www.responseCode == (long)HttpStatusCode.OK && www.downloadHandler.text.Contains("AlreadyUpdated"))
				{
					onProgress?.Invoke(1f, "100% (Already Updated)");
					onComplete?.Invoke();
					yield break;
				}

				onComplete?.Invoke();
			}
		}
	}
}