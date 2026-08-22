using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HtmlAgilityPack;
using FishMMO.Logging;
using UnityEngine;
using UnityEngine.Networking;

namespace FishMMO.Client
{
	/// <summary>
	/// Fetches launcher news HTML and extracts the configured content fragment from it.
	/// </summary>
	/// <remarks>
	/// Fetching and formatting are separate concerns: this component produces a parsed node and
	/// the active <see cref="ILauncherView"/> renders it. Formatting used to live here, back
	/// when TextMeshPro rich text was the only output format.
	/// </remarks>
	public class UnityHtmlContentFetcher : MonoBehaviour, IHtmlContentFetcher
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
		/// <remarks>
		/// Defaults to 0 (a single attempt) because this fetcher serves the launcher's news
		/// pane, which is purely cosmetic. Retrying stacked the per-attempt timeout and the
		/// inter-attempt delay for content nobody is waiting on — with the previous value of 3
		/// and an unreachable host that times out rather than refusing, that is 4 x 10s + 3 x 1s
		/// of requests for a decorative panel. The news pane simply shows an error instead.
		/// <para>
		/// This used to also sit on the critical path, with the version check waiting on it.
		/// It no longer does — startup and the news fetch are dispatched independently — so the
		/// retry count now costs only the news pane, not the launch.
		/// </para>
		/// </remarks>
		[Tooltip("Maximum number of retries for each web request. 0 = a single attempt (news is cosmetic).")]
		[SerializeField]
		private int maxRetries = 0;
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
		/// <summary>
		/// Approximate conversion factor from HTML pixels to TextMeshPro font size.
		/// </summary>
		/// <remarks>
		/// Was consumed by the TextMeshPro launcher view, which the UI Toolkit conversion removed. It stays
		/// serialized on this component so the existing scene keeps its configured value; the
		/// launcher passes it to the view rather than the view reaching for it.
		/// </remarks>
		[Tooltip("Approximate conversion factor from HTML pixels to TextMeshPro font size.")]
		[SerializeField]
		private float htmlPxToTmpSizeFactor = 1.5f;
		/// <summary>
		/// Approximate conversion factor from HTML pixels to TextMeshPro font size.
		/// </summary>
		public float HtmlPxToTmpSizeFactor => htmlPxToTmpSizeFactor;

		/// <summary>
		/// Maximum accepted size of the news document, in bytes.
		/// </summary>
		/// <remarks>
		/// <b>H1.</b> The response had no size limit whatsoever. It is buffered whole in memory
		/// and then handed to an HTML parser that builds a node per tag, so the peak cost is
		/// several times the wire size — which for an unbounded response is an out-of-memory
		/// kill of the launcher, from a decorative pane, triggered by a host the player has no
		/// relationship with. 2 MiB is a very large news page and a very small allocation.
		/// </remarks>
		[Tooltip("Maximum accepted size of the news document in bytes. Larger responses are refused.")]
		[SerializeField]
		private long maxNewsBytes = 2L * 1024L * 1024L;

		/// <summary>
		/// Seconds the news fetch may stall with no data before it is abandoned.
		/// </summary>
		/// <remarks>
		/// The news fetch keeps a whole-request timeout as well — it is a small document, so
		/// "took too long overall" really is a fault there, unlike a patch download. This is the
		/// second bound, for a host that dribbles bytes slowly enough to stay under the total.
		/// </remarks>
		[Tooltip("Seconds the news fetch may stall with no data before it is abandoned.")]
		[SerializeField]
		private int newsIdleTimeoutSeconds = 10;

		/// <summary>
		/// Unity Awake method. Validates dependencies and disables script if missing.
		/// </summary>
		private void Awake()
		{
			if (this.webRequestService == null)
			{
				// Disable only this component. Deactivating the GameObject would take the
				// sibling ClientLauncher down with it (they share one GameObject) and abort
				// its in-flight news coroutine, leaving the UI stuck on "Loading News..."
				// with a disabled button and no message. FetchAndExtract null-checks
				// and reports through onError instead.
				Log.Error("UnityHtmlContentFetcher", "WebRequestService dependency is not assigned! This script will not function.");
				this.enabled = false;
			}
		}

		/// <summary>
		/// Fetches HTML from a URL and extracts the element identified by <paramref name="divClass"/>.
		/// </summary>
		/// <param name="url">The URL to fetch HTML from.</param>
		/// <param name="divClass">The class name of the div to extract.</param>
		/// <param name="onContentReady">Callback for successful extraction.</param>
		/// <param name="onError">Callback for error handling.</param>
		/// <returns>Coroutine enumerator.</returns>
		public IEnumerator FetchAndExtract(string url, string divClass, Action<HtmlNode> onContentReady, Action<string> onError)
		{
			if (WebRequestService == null)
			{
				onError?.Invoke("HtmlContentFetcher not initialized due to missing WebRequestService.");
				yield break;
			}

			// Set by OnComplete, read after the request coroutine returns. Null means the
			// request failed and OnFailure has already reported it.
			string responseHtml = null;

			UnityWebRequestService.WebRequestConfig config = new UnityWebRequestService.WebRequestConfig
			{
				URL = url,
				Method = UnityWebRequest.kHttpVerbGET,
				// No custom headers — the cosmetic "X-FishMMO: Client" header was not a
				// security boundary (trivially spoofable) and only made it easier for
				// an attacker to identify our launcher traffic by header signature.
				Headers = new Dictionary<string, string>(),
				CertificateHandlerFactory = () => new ClientSSLCertificateHandler(),
				Timeout = WebRequestTimeout,
				IdleTimeout = Mathf.Max(1, this.newsIdleTimeoutSeconds),
				// H1: the transfer is aborted the moment it goes past the cap, rather than
				// buffered to completion and only then measured.
				MaxResponseBytes = Mathf.Max(1024, (int)Mathf.Min(this.maxNewsBytes, int.MaxValue)),
				MaxRetries = MaxRetries,
				RetryDelay = RetryDelay,
				OnProgress = null,
				OnComplete = (request) =>
				{
					// Captured here; parsed after the request coroutine returns, where this can
					// hand the work to a thread. See below.
					responseHtml = request.downloadHandler.text;
				},
				// request is null when the service rejected the config outright (bad URL),
				// so it must not be dereferenced unguarded.
				OnFailure = (request) =>
				{
					string reason = request != null ? request.error : "the request could not be sent";
					string errorMsg = $"Failed to fetch HTML content from {url}. Error: {reason}";
					Log.Error("UnityHtmlContentFetcher", errorMsg);
					onError?.Invoke(errorMsg);
				}
			};

			// Delegate web request execution to the service
			yield return WebRequestService.StartCoroutine(WebRequestService.SendWebRequestWithRetries(config));

			if (responseHtml == null)
			{
				// OnFailure has already reported. Nothing further to do.
				yield break;
			}

			/* Second size check, on the decoded string.
			 *
			 * MaxResponseBytes bounds the wire bytes; this bounds the characters the parser will
			 * see, which is what the allocation actually scales with. They differ by the
			 * encoding, and a multi-byte-hostile response can put more characters through than
			 * the byte count suggests it should.
			 */
			if (responseHtml.Length > this.maxNewsBytes)
			{
				Log.Warning("UnityHtmlContentFetcher", $"News document is {responseHtml.Length} characters, over the {this.maxNewsBytes} limit; refusing to parse it.");
				onError?.Invoke("The news page was too large to display.");
				yield break;
			}

			/* H1: parsed OFF the main thread.
			 *
			 * HtmlDocument.LoadHtml is the expensive half of this — it allocates a node per tag
			 * over a document whose shape the operator's CMS decides — and it ran inside the
			 * completion callback, i.e. synchronously on the main thread, i.e. as a frame-time
			 * spike proportional to a stranger's HTML. HtmlAgilityPack is plain .NET with no
			 * Unity API in it, so the parse is safe on a worker; only the resulting node crosses
			 * back, and it crosses back into a coroutine, so the callback below still runs on the
			 * main thread and may touch UI freely.
			 */
			string html = responseHtml;
			string divClassCapture = divClass;
			Task<HtmlNode> parseTask = Task.Run(() => ExtractDivNode(html, divClassCapture));

			while (!parseTask.IsCompleted)
			{
				yield return null;
			}

			if (parseTask.IsFaulted)
			{
				string reason = parseTask.Exception?.GetBaseException().Message ?? "unknown error";
				Log.Error("UnityHtmlContentFetcher", $"Error processing HTML content: {reason}");
				onError?.Invoke($"Error processing HTML content: {reason}");
				yield break;
			}

			HtmlNode extracted = parseTask.Result;
			if (extracted == null)
			{
				onError?.Invoke($"No element with class '{divClass}' was found in the news page.");
				yield break;
			}

			onContentReady?.Invoke(extracted);
		}

		/// <summary>
		/// Parses <paramref name="htmlContent"/> and returns the first element whose class
		/// contains <paramref name="divClass"/>, with script and style nodes stripped.
		/// </summary>
		/// <param name="htmlContent">The raw HTML content.</param>
		/// <param name="divClass">The class name of the div to extract.</param>
		/// <returns>The matching node, or null when it is not present.</returns>
		private static HtmlNode ExtractDivNode(string htmlContent, string divClass)
		{
			HtmlDocument htmlDoc = new HtmlDocument();
			htmlDoc.LoadHtml(htmlContent);

			// Strip executable and styling content before anything walks the tree. Neither is
			// rendered by either view, and leaving script bodies in place would let them
			// surface as visible text.
			foreach (var scriptOrStyle in htmlDoc.DocumentNode.SelectNodes("//script|//style") ?? Enumerable.Empty<HtmlNode>())
			{
				scriptOrStyle.Remove();
			}

			// NOTE: divClass comes from an Inspector field (not user input), so XPath injection is not a concern here.
			// If this method is ever called with attacker-controlled input, parameterize the XPath.
			HtmlNode divNode = htmlDoc.DocumentNode.SelectSingleNode($"//div[contains(@class, '{divClass}')]");
			if (divNode == null)
			{
				Log.Error("UnityHtmlContentFetcher", $"Div with class '{divClass}' not found in HTML content. Cannot extract news.");
			}
			return divNode;
		}
	}
}
