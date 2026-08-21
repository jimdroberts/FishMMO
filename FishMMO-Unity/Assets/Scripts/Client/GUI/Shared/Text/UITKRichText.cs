using System.Text;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// Translates the TextMeshPro rich-text markup produced across FishMMO-Shared into markup a
	/// UI Toolkit <c>Label</c> renders correctly.
	/// </summary>
	/// <remarks>
	/// Tooltips, chat lines and world labels are all built by shared code — <c>TooltipBuilder</c>,
	/// <c>RichText</c>, <c>ChatHelper</c> — that predates UI Toolkit and emits TextMeshPro tags.
	/// Almost all of them are common to both parsers, but one is not: <c>&lt;size=120%&gt;</c>.
	/// TextMeshPro reads a percentage as a multiple of the current size; UI Toolkit's parser
	/// accepts absolute lengths and signed offsets only, and a percentage makes it drop the tag —
	/// so every item name, ability name and attribute header silently loses its emphasis.
	///
	/// Rewriting at display time rather than at the source keeps a single markup dialect in the
	/// shared assembly, which the server also compiles, and leaves the tooltip text usable by
	/// anything else that wants it.
	///
	/// Sizes are resolved against a caller-supplied base rather than tracked through nesting.
	/// The generators never nest <c>&lt;size&gt;</c> — each builder line opens and closes its own —
	/// so a single base is exact for the markup that actually exists, and predictable for the
	/// markup that does not.
	/// </remarks>
	public static class UITKRichText
	{
		/// <summary>
		/// Font size assumed when a caller does not supply one, in panel points.
		/// </summary>
		/// <remarks>Matches <c>.fish-tooltip__body</c> in FishMMO-Theme.uss.</remarks>
		public const float DefaultBaseFontSize = 12.0f;

		/// <summary>Smallest size a percentage tag is allowed to resolve to.</summary>
		private const float MinResolvedSize = 6.0f;

		/// <summary>Largest size a percentage tag is allowed to resolve to.</summary>
		private const float MaxResolvedSize = 96.0f;

		/// <summary>
		/// Converts TextMeshPro markup to UI Toolkit markup.
		/// </summary>
		/// <param name="text">Source markup, possibly null or empty.</param>
		/// <param name="baseFontSize">Font size percentages resolve against, in panel points.</param>
		/// <returns>Markup safe to assign to a UI Toolkit <c>Label.text</c>.</returns>
		public static string ToUITK(string text, float baseFontSize = DefaultBaseFontSize)
		{
			if (string.IsNullOrEmpty(text))
			{
				return text;
			}

			// Nothing to rewrite is the overwhelmingly common case; skip the builder entirely.
			int firstTag = text.IndexOf("<size=", System.StringComparison.OrdinalIgnoreCase);
			if (firstTag < 0)
			{
				return text;
			}

			StringBuilder sb = new StringBuilder(text.Length + 16);
			int cursor = 0;

			while (cursor < text.Length)
			{
				int open = text.IndexOf("<size=", cursor, System.StringComparison.OrdinalIgnoreCase);
				if (open < 0)
				{
					sb.Append(text, cursor, text.Length - cursor);
					break;
				}

				int close = text.IndexOf('>', open);
				if (close < 0)
				{
					// Unterminated tag — emit the remainder verbatim rather than guessing.
					sb.Append(text, cursor, text.Length - cursor);
					break;
				}

				sb.Append(text, cursor, open - cursor);

				int valueStart = open + "<size=".Length;
				string value = text.Substring(valueStart, close - valueStart).Trim().Trim('"');

				sb.Append(RewriteSizeValue(value, baseFontSize));

				cursor = close + 1;
			}

			return sb.ToString();
		}

		/// <summary>
		/// Produces the replacement for a single <c>&lt;size=…&gt;</c> tag body.
		/// </summary>
		/// <param name="value">The tag's value with the surrounding markup stripped.</param>
		/// <param name="baseFontSize">Font size percentages resolve against.</param>
		/// <returns>A complete tag, including angle brackets.</returns>
		private static string RewriteSizeValue(string value, float baseFontSize)
		{
			if (value.Length > 1 && value[value.Length - 1] == '%')
			{
				string number = value.Substring(0, value.Length - 1);
				if (float.TryParse(number, System.Globalization.NumberStyles.Float,
					System.Globalization.CultureInfo.InvariantCulture, out float percent))
				{
					float resolved = Mathf.Clamp(baseFontSize * percent * 0.01f, MinResolvedSize, MaxResolvedSize);
					return $"<size={resolved.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}px>";
				}
				// Unparseable percentage: dropping the tag beats emitting one the parser rejects.
				return string.Empty;
			}

			/* Signed offsets and bare numbers already mean the same thing to both parsers. A bare
			 * number is points in TextMeshPro and pixels in UI Toolkit, which coincide at the
			 * panel's reference scale, so it is passed through unchanged. */
			return $"<size={value}>";
		}

		/// <summary>
		/// Removes every rich-text tag, leaving the visible characters.
		/// </summary>
		/// <param name="text">Source markup, possibly null or empty.</param>
		/// <returns>The text with all markup removed.</returns>
		/// <remarks>
		/// For places that need the plain string — measuring, sorting, logging — where leaving
		/// markup in would compare or size against characters nobody sees.
		/// </remarks>
		public static string Strip(string text)
		{
			if (string.IsNullOrEmpty(text) || text.IndexOf('<') < 0)
			{
				return text;
			}

			StringBuilder sb = new StringBuilder(text.Length);
			bool inTag = false;
			for (int i = 0; i < text.Length; ++i)
			{
				char c = text[i];
				if (c == '<')
				{
					// A '<' with no closing '>' after it is literal text, not an unterminated tag.
					if (text.IndexOf('>', i) < 0)
					{
						sb.Append(c);
						continue;
					}
					inTag = true;
					continue;
				}
				if (c == '>' && inTag)
				{
					inTag = false;
					continue;
				}
				if (!inTag)
				{
					sb.Append(c);
				}
			}
			return sb.ToString();
		}
	}
}
