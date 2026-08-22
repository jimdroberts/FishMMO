// This file is compiled into the Discord bot as well, which builds with nullable reference types
// enabled. Nothing here is null-annotated, so opt the file out rather than carry annotations that
// the Unity assemblies (which do not enable the feature) would then warn about.
#nullable disable

using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FishMMO.Shared
{
	/// <summary>
	/// Text-hygiene routines applied to every piece of untrusted text that enters the chat
	/// pipeline — player input, and anything bridged in from Discord.
	/// </summary>
	/// <remarks>
	/// Deliberately free of every project dependency (no logging, no Unity, no character types)
	/// so the whole file can be compiled and exercised on its own. This is security code that is
	/// cheap to test and expensive to get wrong, and the old implementation lived inside
	/// <see cref="ChatHelper"/> where nothing could reach it without dragging the server's whole
	/// object graph along.
	/// <para>
	/// Everything here is <em>fail closed</em>: a call that cannot complete its work returns the
	/// empty string rather than a partially-cleaned one. The previous behaviour on a regex
	/// timeout was the opposite — it returned the input truncated to 256 characters with every
	/// rich-text tag still in it, which handed an attacker a guaranteed bypass: make the pattern
	/// slow, and the sanitiser stops sanitising.
	/// </para>
	/// </remarks>
	public static class ChatSanitizer
	{
		#region Rich text patterns
		// Regex patterns for Unity Rich Text tags. Used to sanitize chat messages by removing
		// formatting. Matching is case-insensitive (see RichTextRegex): Unity's own parser is,
		// so a case-sensitive filter simply told the player to type "<SIZE=500>" instead.
		private const string AlignPattern = @"<align=[^>]*?>|<\/align>";
		private const string AllCapsPattern = @"<allcaps>|<\/allcaps>";
		private const string AlphaPattern = @"<alpha=[^>]*?>|<\/alpha>";
		private const string BoldPattern = @"<b>|<\/b>";
		private const string BrPattern = @"<br\s*\/?>|<\/br>";
		private const string ColorPattern = @"<color=[^>]*?>|<\/color>";
		private const string CspacePattern = @"<cspace=[^>]*?>|<\/cspace>";
		private const string FontPattern = @"<font=[^>]*?>|<\/font>";
		private const string FontWeightPattern = @"<font-weight=[^>]*?>|<\/font-weight>";
		private const string GradientPattern = @"<gradient=[^>]*?>|<\/gradient>";
		private const string ItalicPattern = @"<i>|<\/i>";
		private const string IndentPattern = @"<indent=[^>]*?>|<\/indent>";
		private const string LineHeightPattern = @"<line-height=[^>]*?>|<\/line-height>";
		private const string LineIndentPattern = @"<line-indent=[^>]*?>|<\/line-indent>";
		private const string LinkPattern = @"<link=[^>]*?>|<\/link>";
		private const string LowercasePattern = @"<lowercase>|<\/lowercase>";
		private const string MarginPattern = @"<margin=[^>]*?>|<\/margin>";
		private const string MarkPattern = @"<mark=[^>]*?>|<\/mark>";
		private const string MspacePattern = @"<mspace=[^>]*?>|<\/mspace>";
		private const string NobrPattern = @"<nobr>|<\/nobr>";
		private const string NoparsePattern = @"<noparse>|<\/noparse>";
		private const string PagePattern = @"<page=[^>]*?>|<\/page>";
		private const string PosPattern = @"<pos=[^>]*?>|<\/pos>";
		private const string RotatePattern = @"<rotate=[^>]*?>|<\/rotate>";
		private const string SPattern = @"<s>|<\/s>";
		private const string SizePattern = @"<size=[^>]*?>|<\/size>";
		private const string SmallcapsPattern = @"<smallcaps>|<\/smallcaps>";
		private const string SpacePattern = @"<space=[^>]*?>|<\/space>";
		/// <summary>
		/// Sprite tags. The self-closing slash is optional.
		/// </summary>
		/// <remarks>
		/// This used to require <c>/&gt;</c>, so the perfectly valid <c>&lt;sprite=3&gt;</c> —
		/// which TextMeshPro renders exactly like the self-closing spelling — passed straight
		/// through the filter and into the message.
		/// </remarks>
		private const string SpritePattern = @"<sprite[^>]*?\/?>|<\/sprite>";
		private const string StrikethroughPattern = @"<strikethrough>|<\/strikethrough>";
		private const string StylePattern = @"<style=[^>]*?>|<\/style>";
		private const string SubPattern = @"<sub>|<\/sub>";
		private const string SupPattern = @"<sup>|<\/sup>";
		private const string UPattern = @"<u>|<\/u>";
		private const string UppercasePattern = @"<uppercase>|<\/uppercase>";
		private const string VoffsetPattern = @"<voffset=[^>]*?>|<\/voffset>";
		private const string WidthPattern = @"<width=[^>]*?>|<\/width>";

		/// <summary>
		/// Combined regex pattern for all supported Unity Rich Text tags.
		/// </summary>
		public static readonly string CombinedRichTextPattern =
			$"{AlignPattern}|{AllCapsPattern}|{AlphaPattern}|{BoldPattern}|{BrPattern}|{ColorPattern}|" +
			$"{CspacePattern}|{FontPattern}|{FontWeightPattern}|{GradientPattern}|{ItalicPattern}|" +
			$"{IndentPattern}|{LineHeightPattern}|{LineIndentPattern}|{LinkPattern}|{LowercasePattern}|" +
			$"{MarginPattern}|{MarkPattern}|{MspacePattern}|{NobrPattern}|{NoparsePattern}|{PagePattern}|" +
			$"{PosPattern}|{RotatePattern}|{SPattern}|{SizePattern}|{SmallcapsPattern}|{SpacePattern}|" +
			$"{SpritePattern}|{StrikethroughPattern}|{StylePattern}|{SubPattern}|{SupPattern}|{UPattern}|" +
			$"{UppercasePattern}|{VoffsetPattern}|{WidthPattern}";
		#endregion

		/// <summary>
		/// Prefix shared by every server-authored chat control code (see <see cref="ChatHelper"/>).
		/// </summary>
		private const string ChatCodePrefix = "FISHMMO_";

		/// <summary>
		/// Maximum number of removal passes made before the input is rejected outright.
		/// </summary>
		/// <remarks>
		/// A single pass is not enough. Removing a match can splice its neighbours together into
		/// a brand new tag: <c>&lt;siz&lt;size=500&gt;e=500&gt;</c> contains exactly one match
		/// (<c>&lt;size=500&gt;</c>), and deleting it leaves <c>&lt;size=500&gt;</c> behind — the
		/// tag the filter was written to remove. Nesting that trick <em>n</em> deep needs
		/// <em>n</em> passes, so the loop runs to a fixed point.
		/// <para>
		/// The cap exists because "run until nothing changes" on attacker-chosen input is an
		/// unbounded loop on the game loop. Reaching it means the input is still growing new
		/// tags after this many rounds, which no honest message does, so it is rejected.
		/// </para>
		/// </remarks>
		public const int MaxPasses = 8;

		/// <summary>
		/// Per-pass regex match budget. Previously one second, on the main thread.
		/// </summary>
		/// <remarks>
		/// A second of matching per message is an availability bug in its own right: chat is
		/// drained on the Unity main thread (<c>ChatSystem.DrainIncomingChatQueue</c>) at up to
		/// 500 messages a frame, so a handful of pathological messages could stall the server for
		/// minutes. None of the patterns above nest a quantifier inside a quantifier, so
		/// catastrophic backtracking should not be reachable at all; this is the backstop, sized
		/// so that tripping it costs a frame rather than a session.
		/// </remarks>
		private const int PassTimeoutMilliseconds = 20;

		/// <summary>
		/// Total wall-clock budget for one <see cref="StripRichText"/> call across all passes.
		/// </summary>
		private const int TotalBudgetMilliseconds = 50;

		/// <summary>
		/// Compiled rich-text matcher. Case-insensitive and culture-invariant.
		/// </summary>
		/// <remarks>
		/// <see cref="RegexOptions.IgnoreCase"/> without <see cref="RegexOptions.CultureInvariant"/>
		/// is a trap on a Turkish locale, where "I" does not lower-case to "i" and
		/// <c>&lt;I&gt;</c> would stop matching the italic tag.
		/// </remarks>
		private static readonly Regex RichTextRegex = new Regex(
			CombinedRichTextPattern,
			RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
			TimeSpan.FromMilliseconds(PassTimeoutMilliseconds));

		/// <summary>
		/// Matches the chat control-code prefix in any casing.
		/// </summary>
		private static readonly Regex ChatCodeRegex = new Regex(
			Regex.Escape(ChatCodePrefix),
			RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
			TimeSpan.FromMilliseconds(PassTimeoutMilliseconds));

		/// <summary>
		/// Removes every Unity Rich Text tag from <paramref name="message"/>, repeating until the
		/// text stops changing.
		/// </summary>
		/// <param name="message">Untrusted text.</param>
		/// <returns>
		/// The text with all recognised tags removed, or <see cref="string.Empty"/> when the
		/// input could not be cleaned inside <see cref="MaxPasses"/> passes or
		/// <see cref="TotalBudgetMilliseconds"/>.
		/// </returns>
		public static string StripRichText(string message)
		{
			if (string.IsNullOrEmpty(message))
			{
				return string.Empty;
			}

			// Nothing to do, and by far the common case — skip the regex machinery entirely.
			if (message.IndexOf('<') < 0)
			{
				return message;
			}

			Stopwatch budget = Stopwatch.StartNew();
			string current = message;

			for (int pass = 0; pass < MaxPasses; ++pass)
			{
				string next;
				try
				{
					next = RichTextRegex.Replace(current, string.Empty);
				}
				catch (RegexMatchTimeoutException)
				{
					// Fail CLOSED. Returning what we have would return tags we know we did not
					// finish removing.
					return string.Empty;
				}

				if (string.Equals(next, current, StringComparison.Ordinal))
				{
					// Fixed point: a pass changed nothing, so no tag remains and no removal can
					// splice a new one into existence.
					return next;
				}

				current = next;

				if (budget.ElapsedMilliseconds > TotalBudgetMilliseconds)
				{
					return string.Empty;
				}
			}

			// Still producing new tags after MaxPasses. Nothing a player types does this.
			return string.Empty;
		}

		/// <summary>
		/// Removes the <c>FISHMMO_</c> control-code prefix wherever it appears, repeating until
		/// the text stops changing.
		/// </summary>
		/// <remarks>
		/// The chat protocol carries a handful of in-band control codes — <c>FISHMMO_TELL_RELAYED</c>,
		/// <c>FISHMMO_TARGET_OFFLINE</c> and friends — which the client matches on the first word
		/// of a message and renders specially. They were never stripped from player input, so
		/// <c>/tell Bob FISHMMO_TELL_RELAYED you owe me gold</c> arrived at Bob's client looking
		/// exactly like a whisper Bob had sent to <em>someone else</em>. Removing the prefix is
		/// enough to defuse every code at once, and the codes the server itself emits are
		/// prepended after this runs.
		/// <para>
		/// Looped for the same reassembly reason as <see cref="StripRichText"/>:
		/// <c>FISHMMOFISHMMO__</c> collapses to <c>FISHMMO_</c> in one pass.
		/// </para>
		/// </remarks>
		/// <param name="message">Untrusted text.</param>
		/// <returns>The text with no control-code prefix left in it, or empty on failure.</returns>
		public static string StripChatCodes(string message)
		{
			if (string.IsNullOrEmpty(message))
			{
				return string.Empty;
			}

			// Cheap reject: the prefix always begins with this character in either casing.
			if (message.IndexOf('F') < 0 && message.IndexOf('f') < 0)
			{
				return message;
			}

			string current = message;
			for (int pass = 0; pass < MaxPasses; ++pass)
			{
				string next;
				try
				{
					next = ChatCodeRegex.Replace(current, string.Empty);
				}
				catch (RegexMatchTimeoutException)
				{
					return string.Empty;
				}

				if (string.Equals(next, current, StringComparison.Ordinal))
				{
					return next;
				}
				current = next;
			}
			return string.Empty;
		}

		/// <summary>
		/// Folds control characters and invisible formatting characters out of the text.
		/// </summary>
		/// <remarks>
		/// Two separate problems, both of which reached the chat log unfiltered:
		/// <list type="bullet">
		/// <item><description>
		/// Newlines. Every chat row is a single-line record; a message carrying <c>\n</c> renders
		/// as several lines in a wrapping <c>Label</c>, which lets one player push everyone
		/// else's chat off the top of the window from inside a 128-character budget. Line breaks
		/// and tabs become a space so words either side of them stay separated.
		/// </description></item>
		/// <item><description>
		/// Unicode <c>Format</c> characters — U+202E RIGHT-TO-LEFT OVERRIDE above all. It reverses
		/// the display order of everything after it, so a message can be made to render as a
		/// different message entirely (including impersonating another player's name), and it
		/// leaks out of the message into the rest of the line. Zero-width spaces and joiners are
		/// in the same category and are what defeats a "no duplicate messages" filter.
		/// </description></item>
		/// </list>
		/// Surrogate pairs are left alone — emoji are fine.
		/// </remarks>
		/// <param name="message">Untrusted text.</param>
		/// <returns>Single-line text containing no control or format characters.</returns>
		public static string StripControlCharacters(string message)
		{
			if (string.IsNullOrEmpty(message))
			{
				return string.Empty;
			}

			StringBuilder builder = null;
			for (int i = 0; i < message.Length; ++i)
			{
				char c = message[i];

				bool replaceWithSpace = c == '\n' || c == '\r' || c == '\t' || c == '\f' || c == '\v';
				bool drop = false;

				if (!replaceWithSpace)
				{
					if (char.IsControl(c))
					{
						drop = true;
					}
					else
					{
						UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(c);
						drop = category == UnicodeCategory.Format ||
							   category == UnicodeCategory.LineSeparator ||
							   category == UnicodeCategory.ParagraphSeparator;
					}
				}

				if (!replaceWithSpace && !drop)
				{
					builder?.Append(c);
					continue;
				}

				if (builder == null)
				{
					// First offending character: copy everything before it and switch to building.
					builder = new StringBuilder(message.Length);
					builder.Append(message, 0, i);
				}
				if (replaceWithSpace)
				{
					builder.Append(' ');
				}
			}

			return builder == null ? message : builder.ToString();
		}

		/// <summary>
		/// The complete inbound pipeline for untrusted chat text.
		/// </summary>
		/// <remarks>
		/// Order matters. Control characters go first, because they can be used to break a tag or
		/// a control code apart so the later passes do not recognise it. Rich text goes next, and
		/// the control-code prefix last, because removing a tag from the middle of
		/// <c>FISH&lt;b&gt;MMO_</c> is what would otherwise reassemble the prefix.
		/// <para>
		/// Truncation happens at the very end and is a hard cut, not a "…" — the ellipsis the old
		/// timeout path appended was three extra characters appearing on a length-limited field.
		/// </para>
		/// </remarks>
		/// <param name="message">Untrusted text, from a player or from the Discord bridge.</param>
		/// <param name="maxLength">
		/// Hard character cap applied after cleaning. Values below one disable truncation.
		/// </param>
		/// <returns>
		/// Clean, single-line, tag-free, code-free text within <paramref name="maxLength"/>, or
		/// <see cref="string.Empty"/> if nothing survived.
		/// </returns>
		public static string SanitizeIncoming(string message, int maxLength)
		{
			if (string.IsNullOrEmpty(message))
			{
				return string.Empty;
			}

			string result = StripControlCharacters(message);
			result = StripRichText(result);
			result = StripChatCodes(result);

			if (string.IsNullOrEmpty(result))
			{
				return string.Empty;
			}

			result = result.Trim();

			if (maxLength > 0 && result.Length > maxLength)
			{
				result = result.Substring(0, maxLength).TrimEnd();
			}

			return result;
		}
	}
}
