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
		/// Every request currently in flight through this service.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Tracked so <see cref="OnDestroy"/> can abort and dispose them. A request is created
		/// inside a <c>using</c> in <see cref="SendWebRequestWithRetries"/>, which disposes it
		/// on every path the coroutine actually runs to — but a coroutine killed by its
		/// GameObject being destroyed is abandoned rather than disposed, so <c>using</c> never
		/// executes and the native handle (and its socket) leaks until finalization.
		/// </para>
		/// <para>
		/// That is reachable in normal play: the launcher scene unloads the moment the player
		/// launches the game, which can happen while the news fetch or a patch download is
		/// still in flight.
		/// </para>
		/// <para>
		/// <b>A set, not a single slot.</b> This used to be one field, and this service is
		/// shared by three callers — the news fetcher, the version check and the patch download
		/// — at least two of which run concurrently by design (the launcher deliberately
		/// dispatches news and the version check together so neither waits on the other).
		/// Whichever started second overwrote the field, so the first request was no longer
		/// tracked and teardown leaked it; and when the second finished it wrote <c>null</c>,
		/// so a subsequent teardown missed a request that WAS still running. A set has neither
		/// failure and costs one hash lookup per attempt.
		/// </para>
		/// </remarks>
		private readonly HashSet<UnityWebRequest> inFlightRequests = new HashSet<UnityWebRequest>();

		/// <summary>
		/// Aborts and disposes any request still in flight when this service is destroyed.
		/// </summary>
		private void OnDestroy()
		{
			AbortAll();
		}

		/// <summary>
		/// Aborts and disposes every tracked request, then clears the set. Safe when idle.
		/// </summary>
		/// <remarks>
		/// Public because cancellation is not only a teardown concern: the launcher's Cancel
		/// button needs the socket and the file handle released promptly rather than waiting for
		/// a transfer nobody is listening to any more to run to completion.
		/// </remarks>
		public void AbortAll()
		{
			if (this.inFlightRequests.Count == 0)
			{
				return;
			}

			// Copied first: aborting can complete the request synchronously, and the coroutine
			// that owns it removes itself from this set.
			UnityWebRequest[] requests = new UnityWebRequest[this.inFlightRequests.Count];
			this.inFlightRequests.CopyTo(requests);
			this.inFlightRequests.Clear();

			foreach (UnityWebRequest request in requests)
			{
				DisposeRequest(request);
			}
		}

		/// <summary>
		/// Aborts and disposes one request, swallowing the failures that only matter during
		/// teardown.
		/// </summary>
		private static void DisposeRequest(UnityWebRequest request)
		{
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
			/// Per-request timeout in seconds. Zero disables the whole-request deadline.
			/// </summary>
			/// <remarks>
			/// <c>UnityWebRequest.timeout</c> bounds the <em>entire</em> request, not the gaps
			/// within it. That is right for a small JSON response and wrong for a file transfer:
			/// see <see cref="IdleTimeout"/>.
			/// </remarks>
			public int Timeout = 10;
			/// <summary>
			/// Seconds a transfer may make no progress before it is abandoned. Zero disables it.
			/// </summary>
			/// <remarks>
			/// <para>
			/// <b>B1.</b> The patch download shared the 10-second whole-request timeout with the
			/// version check, because both went through this method and there was only one knob.
			/// A whole-request deadline applied to a download does not mean "give up when the
			/// server stops answering" — it means "give up when the file takes longer than ten
			/// seconds", so any patch too large for the player's link was permanently
			/// undownloadable no matter how well the transfer was going. Raising the timeout is
			/// not the fix either: it is a guess at how long a legitimate download may take, and
			/// any guess is simultaneously too short for someone on a slow connection and too
			/// long as a hang detector.
			/// </para>
			/// <para>
			/// An idle timeout asks the question that actually matters. A transfer moving at
			/// 40 KiB/s is healthy however long it takes; a transfer that has moved nothing for
			/// thirty seconds is stuck at any size. So a download sets <see cref="Timeout"/> to
			/// zero and this instead, and it is bounded by progress rather than by duration.
			/// </para>
			/// </remarks>
			public int IdleTimeout = 0;
			/// <summary>
			/// Maximum bytes accepted for this response. Zero means unbounded.
			/// </summary>
			/// <remarks>
			/// <b>H1/H6.</b> Without a cap, the size of the response is whatever the server says
			/// it is — which for the news feed is an out-of-memory crash on the main thread, and
			/// for a patch download is the player's disk filled by a host that decided to keep
			/// sending. Enforced by polling <c>downloadedBytes</c> and aborting, so it applies
			/// to a chunked response with no <c>Content-Length</c> as well as an honest one.
			/// </remarks>
			public long MaxResponseBytes = 0;
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
					// Zero leaves UnityWebRequest with no whole-request deadline, which is what
					// a download wants; its bound is config.IdleTimeout below.
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

					// Publish for teardown. Removed on every path that leaves this using block,
					// so OnDestroy never sees a request the using has already disposed.
					this.inFlightRequests.Add(request);

					UnityWebRequestAsyncOperation operation = request.SendWebRequest();

					// Idle/size bookkeeping. Both are measured against downloadedBytes rather
					// than against operation.progress, which is a fraction of a total the server
					// supplied and is therefore neither trustworthy nor available for a chunked
					// response.
					ulong lastObservedBytes = 0;
					float lastAdvanceTime = Time.realtimeSinceStartup;
					string localAbortReason = null;

					while (!operation.isDone)
					{
						ulong downloaded = request.downloadedBytes;

						if (downloaded != lastObservedBytes)
						{
							lastObservedBytes = downloaded;
							lastAdvanceTime = Time.realtimeSinceStartup;
						}
						else if (config.IdleTimeout > 0 &&
								 Time.realtimeSinceStartup - lastAdvanceTime > config.IdleTimeout)
						{
							localAbortReason = $"no data received for {config.IdleTimeout}s";
							request.Abort();
							break;
						}

						if (config.MaxResponseBytes > 0 && downloaded > (ulong)config.MaxResponseBytes)
						{
							localAbortReason = $"response exceeded the {config.MaxResponseBytes} byte cap";
							request.Abort();
							break;
						}

						// NOTE: OnProgress may fire twice at 100% - once when the loop
						// polls progress on the final frame and once after the loop ends.
						// Callers should handle duplicate 100% notifications gracefully.
						config.OnProgress?.Invoke(request, operation.progress);
						yield return null;
					}

					/* Abort() does not complete the operation synchronously. Draining the
					 * remaining frames keeps the `using` from disposing a request whose native
					 * side is still unwinding, which is a crash rather than an error. */
					while (!operation.isDone)
					{
						yield return null;
					}

					// Progress may fire at 100% here again; see note above.
					config.OnProgress?.Invoke(request, operation.progress);

					// A response that arrived complete but over the cap is still refused — the
					// poll above can miss a small body that lands inside a single frame.
					if (localAbortReason == null &&
						config.MaxResponseBytes > 0 &&
						request.downloadedBytes > (ulong)config.MaxResponseBytes)
					{
						localAbortReason = $"response exceeded the {config.MaxResponseBytes} byte cap";
					}

					if (localAbortReason != null)
					{
						/* Not retried. Both reasons this fires are properties of the peer or the
						 * payload rather than of this attempt — a server that stopped sending or
						 * is sending more than we will accept does neither differently next
						 * time — so a retry spends the whole budget re-learning the same thing
						 * while the player waits. */
						Log.Error("UnityWebRequestService", $"Request aborted ({config.URL}): {localAbortReason}.");
						this.inFlightRequests.Remove(request);
						config.OnFailure?.Invoke(request);
						yield break;
					}

					if (request.result == UnityWebRequest.Result.Success)
					{
						this.inFlightRequests.Remove(request);
						config.OnComplete?.Invoke(request);
						yield break;
					}
					else
					{
						Log.Warning("UnityWebRequestService", $"Request failed ({config.URL}). Attempt {i + 1}/{config.MaxRetries + 1}. Error: {request.error}");
						if (i < config.MaxRetries)
						{
							// Removed before the retry delay: this request is disposed by the
							// using block as the loop iterates, so it must not remain tracked
							// across the wait where teardown could reach it.
							this.inFlightRequests.Remove(request);
							yield return new WaitForSeconds(config.RetryDelay);
						}
						else
						{
							this.inFlightRequests.Remove(request);
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