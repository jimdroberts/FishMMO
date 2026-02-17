using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using FishMMO.Logging;

namespace FishMMO.Client
{
	/// <summary>
	/// Provides shared UnityWebRequest execution with retry, timeout, and progress callbacks.
	/// </summary>
	public class UnityWebRequestService : MonoBehaviour
	{
		/// <summary>
		/// Configuration object for web requests.
		/// </summary>
		public class WebRequestConfig
		{
			/// <summary>
			/// Request URL.
			/// </summary>
			public string URL;
			/// <summary>
			/// HTTP method (for example GET, POST).
			/// </summary>
			public string Method;
			/// <summary>
			/// Optional request headers.
			/// </summary>
			public Dictionary<string, string> Headers = new Dictionary<string, string>();
			/// <summary>
			/// Optional factory for creating a certificate handler per attempt.
			/// A new instance is needed for each retry because the handler is
			/// disposed together with the UnityWebRequest.
			/// </summary>
			public System.Func<CertificateHandler> CertificateHandlerFactory;
			/// <summary>
			/// Optional factory for creating a download handler per attempt.
			/// A new instance is needed for each retry because DownloadHandlerFile
			/// (and other handlers) are disposed together with the UnityWebRequest.
			/// </summary>
			public System.Func<DownloadHandler> DownloadHandlerFactory;
			/// <summary>
			/// Maximum retry attempts after the initial request fails.
			/// </summary>
			public int MaxRetries = 3;
			/// <summary>
			/// Delay in seconds between retries.
			/// </summary>
			public float RetryDelay = 2.0f;
			/// <summary>
			/// Per-request timeout in seconds.
			/// </summary>
			public int Timeout = 10;
			/// <summary>
			/// Optional progress callback.
			/// </summary>
			public System.Action<UnityWebRequest, float> OnProgress;
			/// <summary>
			/// Completion callback when request succeeds.
			/// </summary>
			public System.Action<UnityWebRequest> OnComplete;
			/// <summary>
			/// Failure callback when all retries are exhausted.
			/// </summary>
			public System.Action<UnityWebRequest> OnFailure;
		}

		/// <summary>
		/// Sends a web request with configurable retries and timeout.
		/// </summary>
		/// <param name="config">The configuration for the web request.</param>
		/// <returns>Coroutine enumerator.</returns>
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

					// Create fresh handlers per attempt to avoid reusing disposed instances.
					if (config.CertificateHandlerFactory != null)
					{
						request.certificateHandler = config.CertificateHandlerFactory();
					}
					if (config.DownloadHandlerFactory != null)
					{
						request.downloadHandler = config.DownloadHandlerFactory();
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
			if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
			return $"{bytes / (1024.0 * 1024.0 * 1024.0):F1} GB";
		}
	}
}