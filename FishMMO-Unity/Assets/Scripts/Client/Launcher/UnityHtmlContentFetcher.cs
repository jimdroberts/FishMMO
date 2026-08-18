using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
		/// Consumed by <see cref="HtmlToTmpTextConverter"/> via the TextMeshPro view. It stays
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
				MaxRetries = MaxRetries,
				RetryDelay = RetryDelay,
				OnProgress = null,
				OnComplete = (request) =>
				{
					try
					{
						HtmlNode extracted = ExtractDivNode(request.downloadHandler.text, divClass);
						if (extracted == null)
						{
							onError?.Invoke($"No element with class '{divClass}' was found in the news page.");
							return;
						}
						onContentReady?.Invoke(extracted);
					}
					catch (Exception ex)
					{
						Log.Error("UnityHtmlContentFetcher", $"Error processing HTML content: {ex.Message}");
						onError?.Invoke($"Error processing HTML content: {ex.Message}");
					}
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
