using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using FishMMO.Logging;
using UnityEngine;
using UnityEngine.Networking;

namespace FishMMO.Client
{
	/// <summary>
	/// Fetches launcher news HTML and converts selected content into TextMeshPro-compatible rich text.
	/// </summary>
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
		/// pane, which is purely cosmetic but sits on the critical path: the version check and
		/// launch only begin once this request settles. Retrying stacked the per-attempt
		/// timeout and the inter-attempt delay in front of every startup — with the previous
		/// value of 3 and an unreachable host that times out rather than refusing, that is
		/// 4 x 10s + 3 x 1s of the player watching "Loading News..." before the game even
		/// checks its version. A decorative panel is not worth retrying; the news pane simply
		/// shows an error and startup continues.
		/// </remarks>
		[Tooltip("Maximum number of retries for each web request. 0 = a single attempt (news is cosmetic and blocks startup).")]
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
				// with a disabled button and no message. FetchAndProcessHtml null-checks
				// and reports through onError instead.
				Log.Error("UnityHtmlContentFetcher", "WebRequestService dependency is not assigned! This script will not function.");
				this.enabled = false;
			}
		}

		/// <summary>
		/// Maximum recursion depth when converting HTML nodes to TMP text to prevent stack overflow from deeply nested input.
		/// </summary>
		private const int maxRecursionDepth = 100;

		/// <summary>
		/// Fetches HTML from a URL, extracts content from a div, and processes it into TMP rich text.
		/// </summary>
		/// <param name="url">The URL to fetch HTML from.</param>
		/// <param name="divClass">The class name of the div to extract.</param>
		/// <param name="onHtmlReady">Callback for successful extraction.</param>
		/// <param name="onError">Callback for error handling.</param>
		/// <returns>Coroutine enumerator.</returns>
		public IEnumerator FetchAndProcessHtml(string url, string divClass, Action<string> onHtmlReady, Action<string> onError)
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
						string htmlContent = request.downloadHandler.text;
						string extractedText = ExtractTextFromDiv(htmlContent, divClass);
						onHtmlReady?.Invoke(extractedText);
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
		/// Extracts and converts HTML content from a specific div element into TextMeshPro rich text.
		/// Handles various HTML tags, inline styles, and basic sanitization.
		/// </summary>
		/// <param name="htmlContent">The raw HTML content.</param>
		/// <param name="divClass">The class name of the div to extract.</param>
		/// <returns>Extracted and converted TMP rich text, or empty string if not found.</returns>
		private string ExtractTextFromDiv(string htmlContent, string divClass)
		{
			HtmlDocument htmlDoc = new HtmlDocument();
			htmlDoc.LoadHtml(htmlContent);

			foreach (var scriptOrStyle in htmlDoc.DocumentNode.SelectNodes("//script|//style") ?? Enumerable.Empty<HtmlNode>())
			{
				scriptOrStyle.Remove();
			}

			// NOTE: divClass comes from an Inspector field (not user input), so XPath injection is not a concern here.
			// If this method is ever called with attacker-controlled input, parameterize the XPath.
			HtmlNode divNode = htmlDoc.DocumentNode.SelectSingleNode($"//div[contains(@class, '{divClass}')]");
			if (divNode != null)
			{
				StringBuilder sb = new StringBuilder();
				foreach (HtmlNode childNode in divNode.ChildNodes)
				{
					if (childNode.NodeType == HtmlNodeType.Element || childNode.NodeType == HtmlNodeType.Text)
					{
						sb.Append(ConvertHtmlNodeToTmpText(childNode));
					}
				}
				return sb.ToString().Trim();
			}

			Log.Error("UnityHtmlContentFetcher", $"Div with class '{divClass}' not found in HTML content. Cannot extract news.");
			return string.Empty;
		}

		/// <summary>
		/// Recursively converts an HtmlAgilityPack HtmlNode into a TextMeshPro rich text string.
		/// Handles block elements, inline styles, and links.
		/// </summary>
		/// <param name="node">The HTML node to convert.</param>
		/// <returns>Converted TMP rich text string.</returns>
		private string ConvertHtmlNodeToTmpText(HtmlNode node, int depth = 0)
		{
			if (depth > maxRecursionDepth)
			{
				Log.Warning("UnityHtmlContentFetcher", $"Maximum recursion depth ({maxRecursionDepth}) exceeded. Truncating HTML conversion.");
				return string.Empty;
			}
			StringBuilder sb = new StringBuilder();

			if (node.NodeType == HtmlNodeType.Text)
			{
				return WebUtility.HtmlDecode(node.InnerText);
			}

			if (node.NodeType == HtmlNodeType.Comment)
			{
				return string.Empty;
			}

			// Collect all open tags in open order, then derive close tags by reversing.
			// This ensures proper LIFO nesting regardless of CSS style attribute order,
			// and guarantees each open tag gets exactly one close tag.
			List<string> openTags = new List<string>();

			string styleAttributes = node.GetAttributeValue("style", "");
			if (!string.IsNullOrEmpty(styleAttributes))
			{
				foreach (Match match in Regex.Matches(styleAttributes, @"\s*(?<prop>[\w-]+)\s*:\s*(?<value>[^;]+);?"))
				{
					string prop = match.Groups["prop"].Value.ToLower();
					string value = match.Groups["value"].Value.Trim();

					switch (prop)
					{
						case "color":
							openTags.Add($"<color={value}>");
							break;
						case "font-size":
							if (value.EndsWith("%") && float.TryParse(value.Replace("%", ""), out float percentage))
							{
								// KNOWN LIMITATION: The percentage value is used directly as an absolute TMP
								// size tag rather than being multiplied by a base font size. For example,
								// "150%" produces <size=150> instead of <size=1.5 * baseFontSize>. To support
								// percentage-based font sizes correctly, add a baseFontSize field and apply it:
								// <size={(int)(percentage / 100f * baseFontSize)}>.
								openTags.Add($"<size={(int)(percentage)}>");
							}
							else if (value.EndsWith("px") && float.TryParse(value.Replace("px", ""), out float pxValue))
							{
								openTags.Add($"<size={(int)(pxValue * HtmlPxToTmpSizeFactor)}>");
							}
							break;
						case "text-align":
							if (value == "center" || value == "left" || value == "right" || value == "justify")
							{
								openTags.Add($"<align=\"{value}\">");
							}
							break;
					}
				}
			}

			bool isBlockElement = false;
			switch (node.Name.ToLower())
			{
				case "h1": openTags.Add("<size=180%>"); openTags.Add("<B>"); isBlockElement = true; break;
				case "h2": openTags.Add("<size=150%>"); openTags.Add("<B>"); isBlockElement = true; break;
				case "h3": openTags.Add("<size=130%>"); openTags.Add("<B>"); isBlockElement = true; break;
				case "h4":
				case "h5":
				case "h6": openTags.Add("<B>"); isBlockElement = true; break;
				case "strong":
				case "b": openTags.Add("<B>"); break;
				case "em":
				case "i": openTags.Add("<I>"); break;
				case "u": openTags.Add("<U>"); break;
				case "li": sb.Append("• "); break;
				case "br": sb.AppendLine(); return sb.ToString();
				case "hr": sb.AppendLine("----------------------------------------"); sb.AppendLine(); return sb.ToString();
				case "a":
					string href = node.GetAttributeValue("href", "");
					if (!string.IsNullOrEmpty(href))
					{
						openTags.Add("<color=#00FF00>");
						openTags.Add($"<link=\"{href}\">");
					}
					break;
				case "ul":
				case "ol":
				case "div":
				case "p":
					isBlockElement = true;
					break;
			}

			if (isBlockElement && sb.Length > 0 && sb[sb.Length - 1] != '\n')
			{
				sb.AppendLine();
			}

			// Build the open tag string by concatenating tags in order
			foreach (string tag in openTags)
			{
				sb.Append(tag);
			}

			foreach (HtmlNode child in node.ChildNodes)
			{
				sb.Append(ConvertHtmlNodeToTmpText(child, depth + 1));
			}

			// Build and append close tags by reversing the open order.
			// Each open tag maps to its corresponding close tag.
			for (int i = openTags.Count - 1; i >= 0; i--)
			{
				string tag = openTags[i];
				if (tag.StartsWith("<color=", StringComparison.Ordinal))
					sb.Append("</color>");
				else if (tag.StartsWith("<size=", StringComparison.Ordinal))
					sb.Append("</size>");
				else if (tag.StartsWith("<align=", StringComparison.Ordinal))
					sb.Append("</align>");
				else if (tag.StartsWith("<link=", StringComparison.Ordinal))
					sb.Append("</link>");
				else if (tag == "<B>")
					sb.Append("</B>");
				else if (tag == "<I>")
					sb.Append("</I>");
				else if (tag == "<U>")
					sb.Append("</U>");
			}

			if (isBlockElement && sb.Length > 0 && sb[sb.Length - 1] != '\n')
			{
				sb.AppendLine();
			}

			return sb.ToString();
		}
	}
}