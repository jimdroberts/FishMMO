using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using FishMMO.Logging;

namespace FishMMO.Client
{
	/// <summary>
	/// Converts a parsed HTML fragment into TextMeshPro rich text.
	/// </summary>
	/// <remarks>
	/// Previously lived inside <see cref="UnityHtmlContentFetcher"/>. It moved here when the
	/// fetcher stopped producing a formatted string and started handing the parsed node to the
	/// active view: this output format belongs to the TextMeshPro view specifically, and the
	/// UI Toolkit view builds elements instead. Fetching and formatting are now separate
	/// concerns because there is more than one target format.
	/// </remarks>
	public static class HtmlToTmpTextConverter
	{
		/// <summary>
		/// Maximum recursion depth when converting HTML nodes, to prevent a stack overflow from
		/// deeply nested input. The news document comes from an operator-configured URL, so its
		/// nesting depth is not something this client controls.
		/// </summary>
		private const int MaxRecursionDepth = 100;

		/// <summary>
		/// Converts every child of <paramref name="root"/> into a single TMP rich text string.
		/// </summary>
		/// <param name="root">The extracted news fragment.</param>
		/// <param name="pxToTmpSizeFactor">Approximate conversion factor from HTML pixels to TMP font size.</param>
		/// <returns>TMP rich text, or an empty string when <paramref name="root"/> is null.</returns>
		public static string Convert(HtmlNode root, float pxToTmpSizeFactor)
		{
			if (root == null)
			{
				return string.Empty;
			}

			StringBuilder sb = new StringBuilder();
			foreach (HtmlNode childNode in root.ChildNodes)
			{
				if (childNode.NodeType == HtmlNodeType.Element || childNode.NodeType == HtmlNodeType.Text)
				{
					sb.Append(ConvertNode(childNode, pxToTmpSizeFactor));
				}
			}
			return sb.ToString().Trim();
		}

		/// <summary>
		/// Recursively converts an <see cref="HtmlNode"/> into a TextMeshPro rich text string.
		/// Handles block elements, inline styles, and links.
		/// </summary>
		private static string ConvertNode(HtmlNode node, float pxToTmpSizeFactor, int depth = 0)
		{
			if (depth > MaxRecursionDepth)
			{
				Log.Warning("HtmlToTmpTextConverter", $"Maximum recursion depth ({MaxRecursionDepth}) exceeded. Truncating HTML conversion.");
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
								openTags.Add($"<size={(int)(pxValue * pxToTmpSizeFactor)}>");
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
					/* The href is remote content being pasted into a rich-text tag, so it is
					 * escaped rather than trusted: a quote or an angle bracket in it would
					 * otherwise close the link tag early and let the news document inject
					 * arbitrary TMP markup — enough to restyle the pane or make one link's
					 * visible text sit inside a different link's clickable span. The scheme
					 * is still checked at click time by LauncherLinkPolicy; this only keeps
					 * the markup well-formed. */
					string href = EscapeRichTextAttribute(node.GetAttributeValue("href", ""));
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
				sb.Append(ConvertNode(child, pxToTmpSizeFactor, depth + 1));
			}

			// Build and append close tags by reversing the open order.
			// Each open tag maps to its corresponding close tag.
			for (int i = openTags.Count - 1; i >= 0; i--)
			{
				string tag = openTags[i];
				if (tag.StartsWith("<color=", System.StringComparison.Ordinal))
					sb.Append("</color>");
				else if (tag.StartsWith("<size=", System.StringComparison.Ordinal))
					sb.Append("</size>");
				else if (tag.StartsWith("<align=", System.StringComparison.Ordinal))
					sb.Append("</align>");
				else if (tag.StartsWith("<link=", System.StringComparison.Ordinal))
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

		/// <summary>
		/// Strips the characters that would let an attribute value escape the rich-text tag
		/// it is being written into.
		/// </summary>
		/// <remarks>
		/// TextMeshPro has no escaping syntax inside a tag, so there is nothing to encode to —
		/// the only safe transformation is removal. A URL that legitimately contains one of
		/// these carries it percent-encoded, which survives untouched.
		/// </remarks>
		/// <param name="value">The raw attribute value from the news document.</param>
		/// <returns>The value with tag-breaking characters removed.</returns>
		private static string EscapeRichTextAttribute(string value)
		{
			if (string.IsNullOrEmpty(value))
			{
				return value;
			}
			return value.Replace("\"", string.Empty).Replace("<", string.Empty).Replace(">", string.Empty);
		}

	}
}
