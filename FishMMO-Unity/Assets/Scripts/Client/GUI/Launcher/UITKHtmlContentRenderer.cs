using System;
using System.Net;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UIElements;
using HtmlAgilityPack;
using FishMMO.Logging;

namespace FishMMO.Client
{
	/// <summary>
	/// Renders a parsed launcher news fragment into a tree of <see cref="VisualElement"/>s.
	/// </summary>
	/// <remarks>
	/// The UI Toolkit counterpart to <see cref="HtmlToTmpTextConverter"/>, which produces a
	/// TextMeshPro rich-text string instead. They cannot share an output format: UI Toolkit
	/// parses only lowercase rich-text tags, writes alignment differently, and — decisively —
	/// has no equivalent of TMP's <c>&lt;link&gt;</c> tag, so a news link cannot be a span of
	/// styled text here. It has to be an element that can receive a click.
	/// <para>
	/// Building elements rather than markup also removes an escaping problem. Composing a
	/// markup string means remote content is concatenated into a string that is then parsed,
	/// so a news page containing tag-like text can alter the formatting of everything after
	/// it. Text assigned to a <see cref="Label"/> is never parsed as markup.
	/// </para>
	/// </remarks>
	public static class UITKHtmlContentRenderer
	{
		/// <summary>
		/// Maximum traversal depth. The news document comes from an operator-configured URL,
		/// so its nesting depth is not something this client controls.
		/// </summary>
		private const int MaxRecursionDepth = 100;

		/// <summary>
		/// Maximum number of elements produced from one document.
		/// </summary>
		/// <remarks>
		/// A depth limit alone does not bound the output: a shallow document with a very large
		/// number of siblings produces a very large number of elements, and a UI Toolkit panel
		/// holding tens of thousands of them will stall the launcher on layout. Truncating is
		/// the right failure for a decorative pane.
		/// </remarks>
		private const int MaxElements = 2000;

		/// <summary>Default USS class applied to body text.</summary>
		private const string TextClass = "launcher-news__text";

		/// <summary>
		/// Carries formatting inherited from ancestor elements down the traversal.
		/// </summary>
		private struct InlineStyle
		{
			public bool Bold;
			public bool Italic;
			public bool Underline;
			public Color? TextColor;
			public int? FontSize;
			/// <summary>Href of the enclosing anchor, or null when not inside one.</summary>
			public string Href;
			/// <summary>USS class for text produced at this point in the tree.</summary>
			public string TextClass;
		}

		/// <summary>
		/// Mutable state shared across the traversal.
		/// </summary>
		private class RenderContext
		{
			public VisualElement Container;
			public Action<string> OnLinkActivated;
			public VisualElement CurrentRow;
			public int ElementCount;
			public bool Truncated;
		}

		/// <summary>
		/// Clears <paramref name="container"/> and rebuilds it from <paramref name="root"/>.
		/// </summary>
		/// <param name="root">The extracted news fragment. Null renders an empty pane.</param>
		/// <param name="container">The element to populate.</param>
		/// <param name="onLinkActivated">
		/// Invoked with the raw href when a link is clicked. Callers must route this through
		/// <see cref="LauncherLinkPolicy"/> — this renderer deliberately does not open URLs
		/// itself, so the allowlist stays in one place.
		/// </param>
		public static void Render(HtmlNode root, VisualElement container, Action<string> onLinkActivated)
		{
			if (container == null)
			{
				return;
			}

			container.Clear();

			if (root == null)
			{
				return;
			}

			RenderContext context = new RenderContext
			{
				Container = container,
				OnLinkActivated = onLinkActivated,
			};

			InlineStyle rootStyle = new InlineStyle { TextClass = TextClass };

			foreach (HtmlNode child in root.ChildNodes)
			{
				Walk(child, context, rootStyle, 0);
			}

			FlushRow(context);

			if (context.Truncated)
			{
				Log.Warning("UITKHtmlContentRenderer", $"News content exceeded {MaxElements} elements and was truncated.");
			}
		}

		/// <summary>
		/// Recursively converts <paramref name="node"/> into elements.
		/// </summary>
		private static void Walk(HtmlNode node, RenderContext context, InlineStyle style, int depth)
		{
			if (context.ElementCount >= MaxElements)
			{
				context.Truncated = true;
				return;
			}

			if (depth > MaxRecursionDepth)
			{
				Log.Warning("UITKHtmlContentRenderer", $"Maximum recursion depth ({MaxRecursionDepth}) exceeded. Truncating news conversion.");
				return;
			}

			if (node.NodeType == HtmlNodeType.Comment)
			{
				return;
			}

			if (node.NodeType == HtmlNodeType.Text)
			{
				AppendText(WebUtility.HtmlDecode(node.InnerText), context, style);
				return;
			}

			if (node.NodeType != HtmlNodeType.Element)
			{
				return;
			}

			InlineStyle childStyle = ApplyStyleAttribute(node, style);
			string tag = node.Name.ToLowerInvariant();

			switch (tag)
			{
				case "br":
					FlushRow(context);
					return;

				case "hr":
					FlushRow(context);
					VisualElement rule = new VisualElement();
					rule.AddToClassList("launcher-news__rule");
					context.Container.Add(rule);
					context.ElementCount++;
					return;

				case "h1":
				case "h2":
				case "h3":
					FlushRow(context);
					childStyle.TextClass = $"launcher-news__{tag}";
					WalkChildren(node, context, childStyle, depth);
					FlushRow(context);
					return;

				case "h4":
				case "h5":
				case "h6":
					FlushRow(context);
					childStyle.TextClass = "launcher-news__h3";
					WalkChildren(node, context, childStyle, depth);
					FlushRow(context);
					return;

				case "strong":
				case "b":
					childStyle.Bold = true;
					WalkChildren(node, context, childStyle, depth);
					return;

				case "em":
				case "i":
					childStyle.Italic = true;
					WalkChildren(node, context, childStyle, depth);
					return;

				case "u":
					childStyle.Underline = true;
					WalkChildren(node, context, childStyle, depth);
					return;

				case "a":
					string href = node.GetAttributeValue("href", "");
					if (!string.IsNullOrEmpty(href))
					{
						childStyle.Href = href;
					}
					WalkChildren(node, context, childStyle, depth);
					return;

				case "li":
					FlushRow(context);
					VisualElement row = EnsureRow(context);
					row.AddToClassList("launcher-news__list-item");
					Label bullet = new Label("•");
					bullet.AddToClassList("launcher-news__bullet");
					row.Add(bullet);
					context.ElementCount++;
					WalkChildren(node, context, childStyle, depth);
					FlushRow(context);
					return;

				case "p":
				case "div":
				case "ul":
				case "ol":
					FlushRow(context);
					WalkChildren(node, context, childStyle, depth);
					FlushRow(context);
					return;

				default:
					WalkChildren(node, context, childStyle, depth);
					return;
			}
		}

		/// <summary>
		/// Walks every child of <paramref name="node"/> at one greater depth.
		/// </summary>
		private static void WalkChildren(HtmlNode node, RenderContext context, InlineStyle style, int depth)
		{
			foreach (HtmlNode child in node.ChildNodes)
			{
				Walk(child, context, style, depth + 1);
			}
		}

		/// <summary>
		/// Reads the node's inline <c>style</c> attribute, returning a style updated with any
		/// properties it recognises.
		/// </summary>
		private static InlineStyle ApplyStyleAttribute(HtmlNode node, InlineStyle style)
		{
			string styleAttributes = node.GetAttributeValue("style", "");
			if (string.IsNullOrEmpty(styleAttributes))
			{
				return style;
			}

			foreach (Match match in Regex.Matches(styleAttributes, @"\s*(?<prop>[\w-]+)\s*:\s*(?<value>[^;]+);?"))
			{
				string prop = match.Groups["prop"].Value.ToLowerInvariant();
				string value = match.Groups["value"].Value.Trim();

				switch (prop)
				{
					case "color":
						if (ColorUtility.TryParseHtmlString(value, out Color parsed))
						{
							style.TextColor = parsed;
						}
						break;

					case "font-size":
						if (value.EndsWith("px") && float.TryParse(value.Replace("px", ""), out float px))
						{
							style.FontSize = Mathf.Clamp((int)px, 8, 48);
						}
						else if (value.EndsWith("%") && float.TryParse(value.Replace("%", ""), out float pct))
						{
							// Relative to the 12px body size the news classes use.
							style.FontSize = Mathf.Clamp((int)(12f * pct / 100f), 8, 48);
						}
						break;

					case "font-weight":
						if (value == "bold" || value == "700" || value == "800" || value == "900")
						{
							style.Bold = true;
						}
						break;
				}
			}

			return style;
		}

		/// <summary>
		/// Adds a text run to the current row, as a clickable link when inside an anchor.
		/// </summary>
		private static void AppendText(string text, RenderContext context, InlineStyle style)
		{
			if (string.IsNullOrWhiteSpace(text))
			{
				return;
			}

			// Collapse runs of whitespace the way HTML does. Without this, source indentation
			// and newlines survive into the pane as visible gaps.
			string collapsed = Regex.Replace(text, @"\s+", " ");
			if (collapsed.Length == 0)
			{
				return;
			}

			Label label = new Label(collapsed);
			bool isLink = !string.IsNullOrEmpty(style.Href);

			label.AddToClassList(isLink ? "launcher-news__link" : (style.TextClass ?? TextClass));

			if (style.Bold && style.Italic)
			{
				label.style.unityFontStyleAndWeight = FontStyle.BoldAndItalic;
			}
			else if (style.Bold)
			{
				label.style.unityFontStyleAndWeight = FontStyle.Bold;
			}
			else if (style.Italic)
			{
				label.style.unityFontStyleAndWeight = FontStyle.Italic;
			}

			if (style.Underline)
			{
				// UI Toolkit has no underline style property; a bottom border on the label is
				// the closest equivalent that does not involve re-introducing markup parsing.
				label.style.borderBottomWidth = 1;
				label.style.borderBottomColor = style.TextColor ?? new Color(0.78f, 0.80f, 0.84f);
			}

			// An explicit colour must not override link colouring, or links stop looking
			// clickable whenever the page styles its own text.
			if (style.TextColor.HasValue && !isLink)
			{
				label.style.color = style.TextColor.Value;
			}

			if (style.FontSize.HasValue)
			{
				label.style.fontSize = style.FontSize.Value;
			}

			if (isLink)
			{
				string href = style.Href;
				label.RegisterCallback<ClickEvent>(_ => context.OnLinkActivated?.Invoke(href));
			}

			EnsureRow(context).Add(label);
			context.ElementCount++;
		}

		/// <summary>
		/// Returns the row currently accumulating inline content, creating one if needed.
		/// </summary>
		private static VisualElement EnsureRow(RenderContext context)
		{
			if (context.CurrentRow == null)
			{
				context.CurrentRow = new VisualElement();
				context.CurrentRow.AddToClassList("launcher-news__block");
				context.CurrentRow.style.flexDirection = FlexDirection.Row;
				context.CurrentRow.style.flexWrap = Wrap.Wrap;
				context.Container.Add(context.CurrentRow);
				context.ElementCount++;
			}
			return context.CurrentRow;
		}

		/// <summary>
		/// Ends the current inline row so the next content starts on a new line. Discards the
		/// row when nothing was added to it, so consecutive block tags do not stack up empty
		/// containers and open a visible gap.
		/// </summary>
		private static void FlushRow(RenderContext context)
		{
			if (context.CurrentRow == null)
			{
				return;
			}

			if (context.CurrentRow.childCount == 0)
			{
				context.CurrentRow.RemoveFromHierarchy();
				context.ElementCount--;
			}

			context.CurrentRow = null;
		}
	}
}
