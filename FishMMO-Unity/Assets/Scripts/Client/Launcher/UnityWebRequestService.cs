using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using FishMMO.Logging;

namespace FishMMO.Client
{
	public class UnityWebRequestService : MonoBehaviour
	{
		/// <summary>
		/// Configuration object for web requests.
		/// </summary>
		public class WebRequestConfig
		{
			public string URL;
			public string Method;
			public Dictionary<string, string> Headers = new Dictionary<string, string>();
			public CertificateHandler CertificateHandler;
			public DownloadHandler DownloadHandler;
			public int MaxRetries = 3;
			public float RetryDelay = 2.0f;
			public int Timeout = 10;
			public System.Action<UnityWebRequest, float> OnProgress;
			public System.Action<UnityWebRequest> OnComplete;
			public System.Action<UnityWebRequest> OnFailure;
		}

		/// <summary>
		/// Sends a web request with configurable retries and timeout.
		/// </summary>
		/// <param name="config">The configuration for the web request.</param>
		/// <returns>The completed UnityWebRequest if successful, otherwise the last failed request.</returns>
		public IEnumerator SendWebRequestWithRetries(WebRequestConfig config)
		{
			for (int i = 0; i < config.MaxRetries + 1; i++)
			{
				using (UnityWebRequest request = new UnityWebRequest(config.URL, config.Method))
				{
					request.timeout = config.Timeout;

					// Add custom headers
					foreach (var header in config.Headers)
					{
						request.SetRequestHeader(header.Key, header.Value);
					}

					// Set custom handlers
					if (config.CertificateHandler != null)
					{
						request.certificateHandler = config.CertificateHandler;
					}
					if (config.DownloadHandler != null)
					{
						request.downloadHandler = config.DownloadHandler;
					}
					else
					{
						request.downloadHandler = new DownloadHandlerBuffer();
					}

					UnityWebRequestAsyncOperation operation = request.SendWebRequest();
					while (!operation.isDone)
					{
						config.OnProgress?.Invoke(request, operation.progress);
						yield return null;
					}
					config.OnProgress?.Invoke(request, operation.progress);

					if (request.result == UnityWebRequest.Result.Success)
					{
						config.OnComplete?.Invoke(request);
						yield break;
					}
					else
					{
						Log.Warning("UnityWebRequestService", $"Request failed ({config.URL}). Attempt {i + 1}/{config.MaxRetries + 1}. Error: {request.error}");
						if (i < config.MaxRetries)
						{
							yield return new WaitForSeconds(config.RetryDelay);
						}
						else
						{
							config.OnFailure?.Invoke(request);
							yield break;
						}
					}
				}
			}
		}

		/// <summary>
		/// Formats a byte count (ulong) into a human-readable string (e.g., "1.2 MB").
		/// This helper is placed here as it's often used in conjunction with network downloads.
		/// </summary>
		/// <param name="bytes">The number of bytes.</param>
		/// <returns>A formatted string representing the byte count.</returns>
		public string FormatBytes(ulong bytes)
		{
			if (bytes < 1024) return $"{bytes} B";
			if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
			if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0 * 1024.0):F1} MB";
			return $"{bytes / (1024.0 * 1024.0 * 1024.0):F1} GB";
		}
	}
}