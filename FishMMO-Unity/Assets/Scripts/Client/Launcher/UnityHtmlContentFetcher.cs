using System;
using System.Collections;
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
	public class UnityHtmlContentFetcher : MonoBehaviour, IHtmlContentFetcher
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
		/// Approximate conversion factor from HTML pixels to TextMeshPro font size.
		/// </summary>
		[Tooltip("Approximate conversion factor from HTML pixels to TextMeshPro font size.")]
		public float HtmlPxToTmpSizeFactor = 1.5f;

		/// <summary>
		/// Unity Awake method. Validates dependencies and disables script if missing.
		/// </summary>
		private void Awake()
		{
			if (WebRequestService == null)
			{
				Log.Error("UnityHtmlContentFetcher", "WebRequestService dependency is not assigned! This script will not function.");
				this.gameObject.SetActive(false);
			}
		}

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

			using (UnityWebRequest www = UnityWebRequest.Get(url))
			{
				www.SetRequestHeader("X-FishMMO", "Client");
				// Delegate web request execution to the service
				yield return WebRequestService.StartCoroutine(
					WebRequestService.SendWebRequestWithRetries(www, MaxRetries, RetryDelay, WebRequestTimeout));

				if (www.result != UnityWebRequest.Result.Success)
				{
					onError?.Invoke($"Error fetching HTML from {url}: {www.error}");
				}
				else
				{
					string htmlContent = www.downloadHandler.text;
					string extractedText = ExtractTextFromDiv(htmlContent, divClass);

					if (!string.IsNullOrEmpty(extractedText))
					{
						onHtmlReady?.Invoke(extractedText);
					}
					else
					{
						onError?.Invoke($"Failed to extract text from div '{divClass}' in HTML from {url}.");
					}
				}
			}
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
		private string ConvertHtmlNodeToTmpText(HtmlNode node)
		{
			StringBuilder sb = new StringBuilder();

			if (node.NodeType == HtmlNodeType.Text)
			{
				return WebUtility.HtmlDecode(node.InnerText);
			}

			if (node.NodeType == HtmlNodeType.Comment)
			{
				return string.Empty;
			}

			string colorTag = "";
			string sizeTag = "";
			string alignTag = "";
			string tmpTagOpen = "";
			string tmpTagClose = "";

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
							colorTag = $"<color={value}>";
							tmpTagClose = "</color>" + tmpTagClose;
							break;
						case "font-size":
							if (value.EndsWith("%") && float.TryParse(value.Replace("%", ""), out float percentage))
							{
								sizeTag = $"<size={(int)(percentage)}> ";
								tmpTagClose = "</size>" + tmpTagClose;
							}
							else if (value.EndsWith("px") && float.TryParse(value.Replace("px", ""), out float pxValue))
							{
								sizeTag = $"<size={(int)(pxValue * HtmlPxToTmpSizeFactor)}> ";
								tmpTagClose = "</size>" + tmpTagClose;
							}
							break;
						case "text-align":
							if (value == "center" || value == "left" || value == "right" || value == "justify")
							{
								alignTag = $"<align=\"{value}\">";
								tmpTagClose = "</align>" + tmpTagClose;
							}
							break;
					}
				}
			}

			bool isBlockElement = false;
			switch (node.Name.ToLower())
			{
				case "h1": tmpTagOpen += "<size=180%><B>"; tmpTagClose = "</B></size>" + tmpTagClose; isBlockElement = true; break;
				case "h2": tmpTagOpen += "<size=150%><B>"; tmpTagClose = "</B></size>" + tmpTagClose; isBlockElement = true; break;
				case "h3": tmpTagOpen += "<size=130%><B>"; tmpTagClose = "</B></size>" + tmpTagClose; isBlockElement = true; break;
				case "h4":
				case "h5":
				case "h6": tmpTagOpen += "<B>"; tmpTagClose = "</B>" + tmpTagClose; isBlockElement = true; break;
				case "strong":
				case "b": tmpTagOpen += "<B>"; tmpTagClose = "</B>" + tmpTagClose; break;
				case "em":
				case "i": tmpTagOpen += "<I>"; tmpTagClose = "</I>" + tmpTagClose; break;
				case "u": tmpTagOpen += "<U>"; tmpTagClose = "</U>" + tmpTagClose; break;
				case "li": sb.Append("• "); break;
				case "br": sb.AppendLine(); return "";
				case "hr": sb.AppendLine("----------------------------------------"); sb.AppendLine(); return "";
				case "a":
					string href = node.GetAttributeValue("href", "");
					if (!string.IsNullOrEmpty(href))
					{
						tmpTagOpen += $"<color=#00FF00><link=\"{href}\">";
						tmpTagClose = "</link></color>" + tmpTagClose;
					}
					break;
				case "ul":
				case "ol":
				case "div":
				case "p":
					isBlockElement = true;
					break;
			}

			if (isBlockElement && sb.Length > 0 && !(sb.ToString().EndsWith("\n") || sb.ToString().EndsWith("\r\n")))
			{
				sb.AppendLine();
			}

			sb.Append(alignTag);
			sb.Append(sizeTag);
			sb.Append(colorTag);
			sb.Append(tmpTagOpen);

			foreach (HtmlNode child in node.ChildNodes)
			{
				sb.Append(ConvertHtmlNodeToTmpText(child));
			}

			sb.Append(tmpTagClose);

			if (isBlockElement && sb.Length > 0 && !(sb.ToString().EndsWith("\n\n") || sb.ToString().EndsWith("\r\n\r\n")))
			{
				sb.AppendLine();
			}

			return sb.ToString();
		}
	}
}