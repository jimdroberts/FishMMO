using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Text;
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
	/// <para>
	/// Builds elements rather than a rich-text string. UI Toolkit's rich text has no equivalent
	/// of a link tag, so a news link cannot be a span of styled text — it has to be an element
	/// that can receive a click, which means the tree has to be walked rather than flattened.
	/// </para>
	/// <para>
	/// <b>Correction to a previous remark.</b> This class used to claim that "text assigned to a
	/// <see cref="Label"/> is never parsed as markup", and used that as the reason building
	/// elements removed the escaping problem. That is false, and it was load-bearing.
	/// <see cref="TextElement.enableRichText"/> defaults to <c>true</c> on every
	/// <see cref="Label"/>, so assigning remote text to one hands it straight to Unity's
	/// rich-text parser. Worse, <see cref="WebUtility.HtmlDecode"/> below turns an escaped
	/// <c>&amp;lt;color=#0f0&amp;gt;</c> — which a feed author reasonably believes is inert —
	/// back into a live tag. A compromised or merely sloppy news feed could therefore forge
	/// authoritative-looking launcher chrome ("Verified by FishMMO — enter your key at…") or
	/// <c>&lt;size=2000&gt;</c> the pane into uselessness.
	/// </para>
	/// <para>
	/// Every label produced here now goes through <see cref="RemoteText"/>, which turns rich
	/// text off and strips control characters. Building elements is still the right shape — it
	/// is what makes links possible — but it is not what makes the content safe.
	/// </para>
	/// <para>
	/// <b>Bounded and incremental.</b> The document is fetched from a URL this client does not
	/// control, so its size, breadth and depth are all attacker-chosen. Rendering used to be one
	/// synchronous recursive pass over the whole thing on the main thread: a large feed froze
	/// the launcher, and a hostile one could keep it frozen. Traversal is now an explicit work
	/// stack driven by <see cref="RenderIncremental"/>, which yields every
	/// <see cref="NodesPerFrame"/> nodes, and it stops dead on every budget rather than merely
	/// declining to add more elements.
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

		/// <summary>
		/// Maximum number of HTML nodes visited, whether or not they produce elements.
		/// </summary>
		/// <remarks>
		/// <see cref="MaxElements"/> does not bound the work, only the output. A document of a
		/// million empty <c>&lt;span&gt;</c>s produces no elements at all and still costs a
		/// million visits — which is the cheap way to hang the launcher from the news feed.
		/// </remarks>
		private const int MaxNodesVisited = 100000;

		/// <summary>
		/// Nodes processed per frame by <see cref="RenderIncremental"/>.
		/// </summary>
		/// <remarks>
		/// Large enough that a normal news page finishes within a frame or two, small enough
		/// that a pathological one never costs more than a hitch. The pane is decorative; it
		/// arriving one frame later than it could have is not a cost anybody can perceive.
		/// </remarks>
		private const int NodesPerFrame = 250;

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
		/// What a queued unit of work does when it is popped.
		/// </summary>
		private enum WorkKind
		{
			/// <summary>Process <see cref="WorkItem.Node"/>.</summary>
			Visit,
			/// <summary>End the current inline row. Queued after a block element's children.</summary>
			FlushRow,
		}

		/// <summary>
		/// One unit of traversal work.
		/// </summary>
		/// <remarks>
		/// The traversal is an explicit stack rather than recursion because it has to be able to
		/// stop between any two nodes and resume on the next frame — which a recursive walk
		/// cannot do — and because the recursive form could not be made to honour the element
		/// cap: <c>Walk</c> returned early once the cap was hit, but the <c>foreach</c> in
		/// <c>WalkChildren</c> that called it kept iterating, so a document with a hundred
		/// thousand siblings paid for all of them after the two-thousandth element. Clearing one
		/// stack ends the whole traversal at once.
		/// </remarks>
		private struct WorkItem
		{
			public WorkKind Kind;
			public HtmlNode Node;
			public InlineStyle Style;
			public int Depth;
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
			public int NodesVisited;
			public bool Truncated;
			/// <summary>
			/// True when whitespace was seen between two text runs and has not yet been emitted.
			/// </summary>
			/// <remarks>
			/// Whitespace-only text nodes used to be dropped outright, which silently deleted
			/// the space in markup like <c>&lt;b&gt;Patch&lt;/b&gt; notes&lt;/b&gt;</c> —
			/// HtmlAgilityPack hands the separating space over as its own text node, so the two
			/// runs became "Patchnotes". Recorded rather than emitted immediately so that
			/// trailing whitespace before a block boundary still disappears, which is what HTML
			/// does.
			/// </remarks>
			public bool PendingSpace;
			/// <summary>Reason the traversal stopped early, for the log line.</summary>
			public string StopReason;
		}

		/// <summary>
		/// Clears <paramref name="container"/> and rebuilds it from <paramref name="root"/>,
		/// synchronously.
		/// </summary>
		/// <remarks>
		/// Retained for callers with no coroutine host and for small, locally-authored trees.
		/// Anything rendering a fetched document should prefer
		/// <see cref="RenderIncremental"/> — the budgets are identical, but this form spends the
		/// whole of them inside one frame.
		/// </remarks>
		/// <param name="root">The extracted news fragment. Null renders an empty pane.</param>
		/// <param name="container">The element to populate.</param>
		/// <param name="onLinkActivated">
		/// Invoked with the raw href when a link is clicked. Callers must route this through
		/// <see cref="LauncherLinkPolicy"/> — this renderer deliberately does not open URLs
		/// itself, so the allowlist stays in one place.
		/// </param>
		public static void Render(HtmlNode root, VisualElement container, Action<string> onLinkActivated)
		{
			IEnumerator enumerator = RenderIncremental(root, container, onLinkActivated);
			while (enumerator.MoveNext())
			{
				// Drain. The yields are frame boundaries for the coroutine form; here they are
				// simply resumption points.
			}
		}

		/// <summary>
		/// Clears <paramref name="container"/> and rebuilds it from <paramref name="root"/>,
		/// spreading the work across frames.
		/// </summary>
		/// <remarks>
		/// Drive this with <c>StartCoroutine</c>. It yields <c>null</c> (one frame) every
		/// <see cref="NodesPerFrame"/> nodes, so a document large enough to be a problem
		/// becomes a pane that fills in progressively instead of a launcher that stops
		/// responding.
		/// </remarks>
		/// <param name="root">The extracted news fragment. Null renders an empty pane.</param>
		/// <param name="container">The element to populate.</param>
		/// <param name="onLinkActivated">Invoked with the raw href when a link is clicked.</param>
		public static IEnumerator RenderIncremental(HtmlNode root, VisualElement container, Action<string> onLinkActivated)
		{
			if (container == null)
			{
				yield break;
			}

			container.Clear();

			if (root == null)
			{
				yield break;
			}

			RenderContext context = new RenderContext
			{
				Container = container,
				OnLinkActivated = onLinkActivated,
			};

			InlineStyle rootStyle = new InlineStyle { TextClass = TextClass };

			Stack<WorkItem> work = new Stack<WorkItem>();
			PushChildren(work, root, rootStyle, 0);

			/* Whether the container started out attached to a panel.
			 *
			 * The mid-render bail below has to distinguish "the tree was torn out from under us"
			 * from "this container was never in a panel to begin with" — the latter is the
			 * ordinary case for the synchronous Render() path and for anything building a
			 * subtree before inserting it, and bailing on it would render nothing at all. */
			bool startedAttached = container.panel != null;

			int sinceYield = 0;
			while (work.Count > 0)
			{
				WorkItem item = work.Pop();

				if (item.Kind == WorkKind.FlushRow)
				{
					FlushRow(context);
				}
				else
				{
					Visit(item, work, context);
				}

				if (++sinceYield >= NodesPerFrame)
				{
					sinceYield = 0;
					yield return null;

					/* The container can go away underneath a multi-frame render — the launcher
					 * scene is unloaded the moment the player presses Play, and the news fetch
					 * may still be settling. Writing into a detached tree is wasted work at
					 * best; bailing keeps it from being anything worse. */
					if (startedAttached && context.Container.panel == null)
					{
						yield break;
					}
				}
			}

			FlushRow(context);

			if (context.Truncated)
			{
				Log.Warning("UITKHtmlContentRenderer",
					$"News content was truncated: {context.StopReason} " +
					$"(elements={context.ElementCount}/{MaxElements}, nodes={context.NodesVisited}/{MaxNodesVisited}).");
			}
		}

		/// <summary>
		/// Processes one node, queueing its children as further work.
		/// </summary>
		private static void Visit(WorkItem item, Stack<WorkItem> work, RenderContext context)
		{
			HtmlNode node = item.Node;
			if (node == null)
			{
				return;
			}

			/* Both budgets clear the stack rather than returning.
			 *
			 * This is the H2 fix. The previous recursive form returned from Walk once the
			 * element cap was reached, but its caller went right on iterating the remaining
			 * siblings and calling it again for each — so the cap limited what was *built* and
			 * not what was *walked*, and a wide document paid full price for a truncated
			 * result. Emptying the stack ends the traversal outright. */
			if (context.ElementCount >= MaxElements)
			{
				context.Truncated = true;
				context.StopReason = $"element cap of {MaxElements} reached";
				work.Clear();
				return;
			}
			if (++context.NodesVisited > MaxNodesVisited)
			{
				context.Truncated = true;
				context.StopReason = $"node-visit cap of {MaxNodesVisited} reached";
				work.Clear();
				return;
			}

			if (node.NodeType == HtmlNodeType.Comment)
			{
				return;
			}

			if (node.NodeType == HtmlNodeType.Text)
			{
				AppendText(WebUtility.HtmlDecode(node.InnerText), context, item.Style);
				return;
			}

			if (node.NodeType != HtmlNodeType.Element)
			{
				return;
			}

			// Depth is bounded here rather than by the stack, because the stack no longer grows
			// with depth in a way that would fault. A document nested past this is malformed
			// or hostile either way.
			if (item.Depth > MaxRecursionDepth)
			{
				context.Truncated = true;
				context.StopReason = $"nesting deeper than {MaxRecursionDepth}";
				return;
			}

			InlineStyle childStyle = ApplyStyleAttribute(node, item.Style);
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
					PushBlock(work, node, childStyle, item.Depth);
					return;

				case "h4":
				case "h5":
				case "h6":
					FlushRow(context);
					childStyle.TextClass = "launcher-news__h3";
					PushBlock(work, node, childStyle, item.Depth);
					return;

				case "strong":
				case "b":
					childStyle.Bold = true;
					PushChildren(work, node, childStyle, item.Depth + 1);
					return;

				case "em":
				case "i":
					childStyle.Italic = true;
					PushChildren(work, node, childStyle, item.Depth + 1);
					return;

				case "u":
					childStyle.Underline = true;
					PushChildren(work, node, childStyle, item.Depth + 1);
					return;

				case "a":
					string href = node.GetAttributeValue("href", "");
					if (!string.IsNullOrEmpty(href))
					{
						childStyle.Href = href;
					}
					PushChildren(work, node, childStyle, item.Depth + 1);
					return;

				case "li":
					FlushRow(context);
					VisualElement row = EnsureRow(context);
					row.AddToClassList("launcher-news__list-item");
					Label bullet = RemoteText.CreateLabel("•", "launcher-news__bullet");
					row.Add(bullet);
					context.ElementCount++;
					PushBlock(work, node, childStyle, item.Depth);
					return;

				case "p":
				case "div":
				case "ul":
				case "ol":
					FlushRow(context);
					PushBlock(work, node, childStyle, item.Depth);
					return;

				default:
					PushChildren(work, node, childStyle, item.Depth + 1);
					return;
			}
		}

		/// <summary>
		/// Queues a block element's children followed by a row flush.
		/// </summary>
		/// <remarks>
		/// The flush is pushed <em>first</em> because the stack is LIFO, so it pops last —
		/// after every child. This is the explicit-stack equivalent of the statement that used
		/// to follow the recursive <c>WalkChildren</c> call.
		/// </remarks>
		private static void PushBlock(Stack<WorkItem> work, HtmlNode node, InlineStyle style, int depth)
		{
			work.Push(new WorkItem { Kind = WorkKind.FlushRow });
			PushChildren(work, node, style, depth + 1);
		}

		/// <summary>
		/// Queues every child of <paramref name="node"/> in document order.
		/// </summary>
		/// <remarks>
		/// Pushed in reverse so they pop in the order they appear in the document.
		/// </remarks>
		private static void PushChildren(Stack<WorkItem> work, HtmlNode node, InlineStyle style, int depth)
		{
			HtmlNodeCollection children = node.ChildNodes;
			if (children == null)
			{
				return;
			}

			for (int i = children.Count - 1; i >= 0; --i)
			{
				work.Push(new WorkItem
				{
					Kind = WorkKind.Visit,
					Node = children[i],
					Style = style,
					Depth = depth,
				});
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

			/* Bounded before the regex sees it, and with a match timeout.
			 *
			 * The attribute value is remote input and its length is unbounded; the pattern is
			 * simple, but "simple pattern, hostile input, main thread" is a combination this
			 * codebase has already been bitten by (see the chat sanitiser's 1s budget burning
			 * on the game loop). A style attribute worth honouring is tens of characters. */
			const int MaxStyleAttributeLength = 512;
			if (styleAttributes.Length > MaxStyleAttributeLength)
			{
				return style;
			}

			MatchCollection matches;
			try
			{
				matches = Regex.Matches(
					styleAttributes,
					@"\s*(?<prop>[\w-]+)\s*:\s*(?<value>[^;]+);?",
					RegexOptions.None,
					TimeSpan.FromMilliseconds(50));
			}
			catch (RegexMatchTimeoutException)
			{
				// Unstyled is a perfectly good outcome for a decorative pane.
				return style;
			}

			foreach (Match match in matches)
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
			if (string.IsNullOrEmpty(text))
			{
				return;
			}

			string collapsed = CollapseWhitespace(text, out bool leadingSpace, out bool trailingSpace);

			if (collapsed.Length == 0)
			{
				/* A whitespace-only node is a word separator, not nothing.
				 *
				 * Dropping it — which is what happened before — ran the surrounding runs
				 * together, so "<b>Patch</b> notes" rendered as "Patchnotes": HtmlAgilityPack
				 * emits that space as its own text node, and each styled run is a separate
				 * Label, so nothing else reintroduces it. It is recorded rather than emitted so
				 * that whitespace against a block boundary still vanishes, as it does in HTML —
				 * FlushRow clears the flag. */
				if (context.CurrentRow != null && context.CurrentRow.childCount > 0)
				{
					context.PendingSpace = true;
				}
				return;
			}

			// A separator is owed if the previous node was whitespace, or this run began with
			// whitespace that the collapse removed.
			bool needsSeparator = (context.PendingSpace || leadingSpace) &&
								  context.CurrentRow != null &&
								  context.CurrentRow.childCount > 0;
			context.PendingSpace = trailingSpace;

			string display = needsSeparator ? " " + collapsed : collapsed;

			bool isLink = !string.IsNullOrEmpty(style.Href);

			/* Rich text OFF. See RemoteText: Label.enableRichText defaults to true, and the
			 * HtmlDecode above has just turned any escaped markup in the feed back into live
			 * tags — so this is the line standing between a compromised news feed and forged
			 * launcher chrome. */
			Label label = RemoteText.CreateLabel(
				display,
				isLink ? "launcher-news__link" : (style.TextClass ?? TextClass),
				allowNewlines: false);

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
		/// Collapses runs of whitespace to single spaces the way HTML layout does, and reports
		/// whether the original had leading or trailing whitespace.
		/// </summary>
		/// <remarks>
		/// Hand-written rather than <c>Regex.Replace(text, @"\s+", " ")</c>. The input is remote
		/// and unbounded, this runs once per text node, and the caller needs the leading and
		/// trailing flags anyway — which a replace would have thrown away. One pass, no
		/// allocation beyond the result.
		/// </remarks>
		private static string CollapseWhitespace(string text, out bool leadingSpace, out bool trailingSpace)
		{
			leadingSpace = false;
			trailingSpace = false;

			StringBuilder builder = new StringBuilder(text.Length);
			bool pendingSpace = false;

			for (int i = 0; i < text.Length; ++i)
			{
				char c = text[i];
				if (char.IsWhiteSpace(c))
				{
					if (builder.Length == 0)
					{
						leadingSpace = true;
					}
					else
					{
						pendingSpace = true;
					}
					continue;
				}

				if (pendingSpace)
				{
					builder.Append(' ');
					pendingSpace = false;
				}
				builder.Append(c);
			}

			trailingSpace = pendingSpace;
			return builder.ToString();
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
			// Whitespace against a block boundary is not a word separator; HTML drops it and so
			// does this. Cleared unconditionally, including when there is no row to flush.
			context.PendingSpace = false;

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
