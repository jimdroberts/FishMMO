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
		/// The request currently in flight, or null when idle.
		/// </summary>
		/// <remarks>
		/// Tracked so <see cref="OnDestroy"/> can abort and dispose it. The request is created
		/// inside a <c>using</c> in <see cref="SendWebRequestWithRetries"/>, which disposes it
		/// on every path the coroutine actually runs to — but a coroutine killed by its
		/// GameObject being destroyed is abandoned rather than disposed, so <c>using</c> never
		/// executes and the native handle (and its socket) leaks until finalization.
		/// <para>
		/// That is reachable in normal play: the launcher scene unloads the moment the player
		/// launches the game, which can happen while the news fetch or a patch download is
		/// still in flight.
		/// </para>
		/// </remarks>
		private UnityWebRequest inFlightRequest;

		/// <summary>
		/// Aborts and disposes any request still in flight when this service is destroyed.
		/// </summary>
		private void OnDestroy()
		{
			DisposeInFlightRequest();
		}

		/// <summary>
		/// Aborts and disposes <see cref="inFlightRequest"/> if set, then clears it.
		/// Safe to call when idle.
		/// </summary>
		private void DisposeInFlightRequest()
		{
			UnityWebRequest request = this.inFlightRequest;
			this.inFlightRequest = null;
			if (request == null)
			{
				return;
			}

			try
			{
				// Abort first so an in-progress transfer stops rather than running to its
				// timeout after nothing is left to receive the result.
				request.Abort();
			}
			catch (System.Exception ex)
			{
				Log.Warning("UnityWebRequestService", $"Abort of in-flight request failed during teardown: {ex.Message}");
			}

			try
			{
				request.Dispose();
			}
			catch (System.ObjectDisposedException)
			{
				// Already disposed by the using block; nothing to do.
			}
			catch (System.Exception ex)
			{
				Log.Warning("UnityWebRequestService", $"Dispose of in-flight request failed during teardown: {ex.Message}");
			}
		}

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
			if (config == null || string.IsNullOrWhiteSpace(config.URL))
			{
				// Report through OnFailure rather than just bailing. Callers drive UI state
				// off these callbacks; a silent yield break left the launcher sitting on
				// "Loading News..." with a disabled button and no way forward.
				Log.Error("UnityWebRequestService", "Request URL is null or empty.");
				config?.OnFailure?.Invoke(null);
				yield break;
			}
			
			for (int i = 0; i < config.MaxRetries + 1; i++)
			{
				using (UnityWebRequest request = new UnityWebRequest(config.URL, config.Method))
				{
					request.timeout = config.Timeout;

					// Hardening: never follow HTTP redirects automatically. All launcher
					// endpoints are configured directly (loginserver, patches, news); a 3xx
					// response means either a misconfiguration or an active MITM trying to
					// pivot the request. Refusing redirects converts these into a hard fail
					// instead of silently following the attacker's Location header.
					request.redirectLimit = -1; // -1 disables redirect following (0 = default limit of 20 redirects in Unity 2021+)

					// Hardening: disable HTTP/1.1 100-Continue for upload bodies. Launcher
					// traffic is GET-only and has no request body; this is a defensive
					// no-op for non-upload paths but prevents downgrade surprises if a
					// future caller switches to POST.
					request.useHttpContinue = false;

					// Add custom headers
					if (config.Headers != null)
					{
						foreach (var header in config.Headers)
						{
							request.SetRequestHeader(header.Key, header.Value);
						}
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

					// Publish for teardown. Cleared on every path that leaves this using block,
					// so OnDestroy never sees a request the using has already disposed.
					this.inFlightRequest = request;

					UnityWebRequestAsyncOperation operation = request.SendWebRequest();
					while (!operation.isDone)
					{
						// NOTE: OnProgress may fire twice at 100% - once when the loop
						// polls progress on the final frame and once after the loop ends.
						// Callers should handle duplicate 100% notifications gracefully.
						config.OnProgress?.Invoke(request, operation.progress);
						yield return null;
					}
					// Progress may fire at 100% here again; see note above.
					config.OnProgress?.Invoke(request, operation.progress);

					if (request.result == UnityWebRequest.Result.Success)
					{
						this.inFlightRequest = null;
						config.OnComplete?.Invoke(request);
						yield break;
					}
					else
					{
						Log.Warning("UnityWebRequestService", $"Request failed ({config.URL}). Attempt {i + 1}/{config.MaxRetries + 1}. Error: {request.error}");
						if (i < config.MaxRetries)
						{
							// Cleared before the retry delay: this request is disposed by the
							// using block as the loop iterates, so it must not remain published
							// across the wait where teardown could reach it.
							this.inFlightRequest = null;
							yield return new WaitForSeconds(config.RetryDelay);
						}
						else
						{
							this.inFlightRequest = null;
							config.OnFailure?.Invoke(request);
							yield break;
						}
					}
				}
			}
		}

		// Byte formatting moved to DownloadStats.FormatBytes when progress reporting became
		// structured. It lives with the data it formats, and being static it is reachable
		// without a component reference. Two copies of the same formatter would eventually
		// disagree about what a megabyte looks like.
	}
}