using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using FishMMO.Logging;

namespace FishMMO.Client
{
	public class UnityWebRequestService : MonoBehaviour
	{
		/// <summary>
		/// Sends a UnityWebRequest with configurable retries and timeout.
		/// Includes an optional progress callback for downloads.
		/// </summary>
		/// <param name="request">The UnityWebRequest to send.</param>
		/// <param name="maxRetries">Maximum number of retries for the request.</param>
		/// <param name="retryDelay">Delay in seconds between retries.</param>
		/// <param name="timeout">Timeout in seconds for each individual request attempt.</param>
		/// <param name="onProgress">Optional callback to report download progress (0.0 to 1.0).</param>
		/// <returns>The completed UnityWebRequest if successful, otherwise the last failed request.</returns>
		public IEnumerator SendWebRequestWithRetries(UnityWebRequest request, int maxRetries, float retryDelay, int timeout, Action<float> onProgress = null)
		{
			request.timeout = timeout;
			for (int i = 0; i < maxRetries; i++)
			{
				UnityWebRequestAsyncOperation operation = request.SendWebRequest();
				while (!operation.isDone)
				{
					onProgress?.Invoke(operation.progress);
					yield return null;
				}
				onProgress?.Invoke(operation.progress); // Ensure final progress (100%) is reported.

				if (request.result == UnityWebRequest.Result.Success)
				{
					yield break; // Request succeeded, exit the coroutine.
				}
				else
				{
					Log.Warning("UnityWebRequestService", $"Request failed ({request.url}). Attempt {i + 1}/{maxRetries}. Error: {request.error}");
					if (i < maxRetries - 1)
					{
						yield return new WaitForSeconds(retryDelay);
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