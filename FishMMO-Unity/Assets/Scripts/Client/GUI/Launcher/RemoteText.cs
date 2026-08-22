using System.Text;
using UnityEngine.UIElements;

namespace FishMMO.Client
{
	/// <summary>
	/// Builds <see cref="Label"/>s for text that came from somewhere this client does not
	/// control, and sanitises such text before it reaches a log line.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The thing this exists to prevent.</b> A UI Toolkit <see cref="TextElement"/> — and
	/// therefore <see cref="Label"/> and <see cref="Button"/>, both of which derive from it —
	/// has <see cref="TextElement.enableRichText"/> defaulting to <c>true</c>. Assigning
	/// <c>label.text</c> does not "just show the string": the text is parsed for Unity rich-text
	/// markup, so a news feed or an update server that returns
	/// <c>&lt;color=#00FF00&gt;Verified by FishMMO&lt;/color&gt;</c> gets exactly that, rendered
	/// as trusted-looking launcher chrome. <c>&lt;size=2000&gt;</c> is the same primitive
	/// pointed at the layout instead: one tag makes the pane unusable.
	/// </para>
	/// <para>
	/// The launcher's news renderer additionally runs every text node through
	/// <c>WebUtility.HtmlDecode</c>, which turns an *escaped* <c>&amp;lt;color=...&amp;gt;</c>
	/// back into a live tag — so escaping upstream is not a defence, and a feed that looks inert
	/// as HTML is not inert as Unity markup.
	/// </para>
	/// <para>
	/// <b>Belt and braces, deliberately.</b> <see cref="CreateLabel"/> turns rich text off, which
	/// is the actual fix; <see cref="Sanitize"/> strips the control characters and caps the
	/// length, which is what protects the log file and anything that later renders the same
	/// string somewhere this helper did not build. Either alone would be enough today. Both,
	/// because "today" is the part that changes: a future panel that builds its own Label from
	/// the same string is a one-line regression, and one that logs it is not covered by
	/// <c>enableRichText</c> at all.
	/// </para>
	/// <para>
	/// Nothing here parses or rewrites markup. There is no tag allowlist and no attempt to
	/// "clean" tags, because a sanitiser that rewrites markup is a parser, and a parser is
	/// something to be bypassed. Remote text is rendered as literal characters or not at all.
	/// </para>
	/// </remarks>
	public static class RemoteText
	{
		/// <summary>
		/// Longest remote string rendered or logged verbatim.
		/// </summary>
		/// <remarks>
		/// Generous for a status line or an error detail; nowhere near enough to be a layout or
		/// a log-file problem. The news <em>body</em> is bounded separately and much higher —
		/// this cap is for the short server-controlled strings (versions, error details) that
		/// reach the status label.
		/// </remarks>
		public const int MaxLength = 2048;

		/// <summary>
		/// Creates a <see cref="Label"/> that renders <paramref name="text"/> literally.
		/// </summary>
		/// <param name="text">Untrusted text. Null is treated as empty.</param>
		/// <param name="ussClass">Optional USS class to add.</param>
		/// <param name="allowNewlines">
		/// True for body copy that is meant to wrap across paragraphs; false (the default) for
		/// single-line chrome such as a status line, where an embedded newline is only ever a
		/// way to push real text out of view.
		/// </param>
		/// <param name="maxLength">Length cap; defaults to <see cref="MaxLength"/>.</param>
		/// <returns>A label with rich text disabled and the text sanitised.</returns>
		public static Label CreateLabel(string text, string ussClass = null, bool allowNewlines = false, int maxLength = MaxLength)
		{
			Label label = new Label();
			if (!string.IsNullOrEmpty(ussClass))
			{
				label.AddToClassList(ussClass);
			}
			SetText(label, text, allowNewlines, maxLength);
			return label;
		}

		/// <summary>
		/// Assigns <paramref name="text"/> to <paramref name="label"/> as literal text.
		/// </summary>
		/// <remarks>
		/// Use this for every write of remote content into an existing label, not just the
		/// first: <c>enableRichText</c> is a property of the element, and a label resolved from
		/// a freshly cloned UXML tree is a different element with the default back in place.
		/// (See the project's "mutate then Show" contract — <c>UIDocument</c> re-clones its tree
		/// on every enable, so a flag set once on a cached element does not survive.)
		/// </remarks>
		/// <param name="label">The label to write into. Null is ignored.</param>
		/// <param name="text">Untrusted text. Null is treated as empty.</param>
		/// <param name="allowNewlines">True to keep line breaks (body copy).</param>
		/// <param name="maxLength">Length cap; defaults to <see cref="MaxLength"/>.</param>
		public static void SetText(Label label, string text, bool allowNewlines = false, int maxLength = MaxLength)
		{
			if (label == null)
			{
				return;
			}

			// The line that matters. Set on every write, for the reason in the remarks above.
			label.enableRichText = false;
			label.text = Sanitize(text, maxLength, allowNewlines);
		}

		/// <summary>
		/// Returns <paramref name="text"/> with control characters removed and its length
		/// capped, safe to put in a log line or a single-line UI element.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Removes CR and LF (CWE-117: a server-controlled error string containing a newline
		/// forges whole log lines, which is how a real failure gets buried under fabricated
		/// "success" entries), the remaining C0/C1 control characters, and the bidirectional
		/// override codepoints — U+202A-U+202E and U+2066-U+2069 — which reorder rendered text
		/// and can make a hostile version string display as a benign one.
		/// </para>
		/// <para>
		/// Tabs are kept: they are ordinary layout in a status message and carry no ambiguity.
		/// </para>
		/// </remarks>
		/// <param name="text">Untrusted text. Null returns an empty string.</param>
		/// <param name="maxLength">Length cap; defaults to <see cref="MaxLength"/>.</param>
		/// <param name="allowNewlines">
		/// True to preserve line breaks. Only for text rendered as a wrapping block; never for
		/// a log line, where a newline is the log-forging primitive, and never for single-line
		/// chrome. CR and CRLF are normalised to a single LF either way so one line break is
		/// one line break regardless of who authored the string.
		/// </param>
		public static string Sanitize(string text, int maxLength = MaxLength, bool allowNewlines = false)
		{
			if (string.IsNullOrEmpty(text))
			{
				return string.Empty;
			}

			if (maxLength < 1)
			{
				maxLength = 1;
			}

			StringBuilder builder = new StringBuilder(text.Length < maxLength ? text.Length : maxLength);

			for (int i = 0; i < text.Length; ++i)
			{
				if (builder.Length >= maxLength)
				{
					builder.Append('\u2026'); // horizontal ellipsis, to show it was cut
					break;
				}

				char c = text[i];

				if (c == '\t')
				{
					builder.Append(c);
					continue;
				}
				if (allowNewlines && (c == '\n' || c == '\r'))
				{
					// Normalise CR, LF and CRLF to a single LF, so a feed cannot triple the
					// apparent spacing simply by choosing a line ending.
					if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
					{
						continue;
					}
					builder.Append('\n');
					continue;
				}
				// C0 (includes CR/LF) and DEL.
				if (c < ' ' || c == '\u007F')
				{
					continue;
				}
				// C1.
				if (c >= '\u0080' && c <= '\u009F')
				{
					continue;
				}
				// Bidi embedding / override / isolate controls.
				if ((c >= '\u202A' && c <= '\u202E') || (c >= '\u2066' && c <= '\u2069'))
				{
					continue;
				}

				builder.Append(c);
			}

			return builder.ToString();
		}
	}
}
