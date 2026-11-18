using FishMMO.Shared;
using NUnit.Framework;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Regression tests for <see cref="ChatSanitizer"/>.
	/// <para>
	/// Background: the chat sanitiser was a single <c>Regex.Replace</c> pass with
	/// <c>RegexOptions.None</c>, a one-second timeout, and a catch block that returned the input
	/// truncated to 256 characters <em>with every tag still in it</em>. That is four separate
	/// bypasses, all of them reachable by typing into the chat box:
	/// </para>
	/// <list type="number">
	/// <item><description>
	/// Nested reassembly. One pass over <c>&lt;siz&lt;size=500&gt;e=500&gt;</c> removes the one
	/// real match and splices the remainder into <c>&lt;size=500&gt;</c> — the exact tag the
	/// filter exists to remove.
	/// </description></item>
	/// <item><description>Case. The patterns were case-sensitive; Unity's parser is not.</description></item>
	/// <item><description>
	/// <c>&lt;sprite=3&gt;</c> without the self-closing slash was not matched at all.
	/// </description></item>
	/// <item><description>
	/// Fail-open on timeout: make matching slow and the sanitiser stops sanitising.
	/// </description></item>
	/// </list>
	/// <para>
	/// These tests pin all four, plus the newer inbound rules: no line breaks, no bidirectional
	/// overrides, and no <c>FISHMMO_</c> control codes forged from player input.
	/// </para>
	/// </summary>
	[TestFixture]
	public class ChatSanitizerTests
	{
		/// <summary>The bypass that motivated the fixed-point loop. One pass is not enough.</summary>
		[Test]
		public void StripRichText_NestedReassembly_LeavesNoTag()
		{
			Assert.AreEqual("", ChatSanitizer.StripRichText("<siz<size=500>e=500>"));
		}

		/// <summary>Deeper nesting needs more passes; the loop must keep going until stable.</summary>
		[Test]
		public void StripRichText_DoublyNestedReassembly_LeavesNoTag()
		{
			Assert.AreEqual("", ChatSanitizer.StripRichText("<si<siz<size=1>e=500>ze=500>"));
		}

		/// <summary>The same trick against a simple tag, with surrounding text preserved.</summary>
		[Test]
		public void StripRichText_NestedReassembly_KeepsSurroundingText()
		{
			Assert.AreEqual("hello world", ChatSanitizer.StripRichText("hello <<b>b>world"));
		}

		/// <summary>Unity's rich-text parser is case-insensitive, so this filter must be too.</summary>
		[Test]
		public void StripRichText_IsCaseInsensitive()
		{
			Assert.AreEqual("hi", ChatSanitizer.StripRichText("<SIZE=500>hi</SIZE>"));
			Assert.AreEqual("hi", ChatSanitizer.StripRichText("<B>hi</B>"));
			Assert.AreEqual("hi", ChatSanitizer.StripRichText("<Color=red>hi</Color>"));
		}

		/// <summary>A sprite tag renders with or without the self-closing slash.</summary>
		[Test]
		public void StripRichText_SpriteWithoutSelfClose_IsRemoved()
		{
			Assert.AreEqual("", ChatSanitizer.StripRichText("<sprite=3>"));
			Assert.AreEqual("", ChatSanitizer.StripRichText("<sprite=3/>"));
			Assert.AreEqual("", ChatSanitizer.StripRichText("<sprite name=\"x\" index=3>"));
		}

		/// <summary>Ordinary text must survive untouched — the filter is not allowed to eat chat.</summary>
		[Test]
		public void StripRichText_PlainText_IsUnchanged()
		{
			Assert.AreEqual("meet me at 5<10 gold", ChatSanitizer.StripRichText("meet me at 5<10 gold"));
			Assert.AreEqual("hello there", ChatSanitizer.StripRichText("hello there"));
		}

		/// <summary>Null and empty are answered, not thrown on.</summary>
		[Test]
		public void StripRichText_NullOrEmpty_ReturnsEmpty()
		{
			Assert.AreEqual("", ChatSanitizer.StripRichText(null));
			Assert.AreEqual("", ChatSanitizer.StripRichText(""));
		}

		/// <summary>
		/// Input still growing new tags after the pass cap is rejected outright rather than
		/// returned half-cleaned. This is the fail-closed contract.
		/// </summary>
		[Test]
		public void StripRichText_ExceedingPassCap_FailsClosed()
		{
			/* Each wrap peels in exactly one pass: removing the inner <size=1> splices "<siz"
			 * onto "e=1>" and produces the next one down. Nested one layer deeper than the cap
			 * allows, so the loop runs out before the text is clean.
			 *
			 * The leading "hi" is the point of the test: failing OPEN here would return
			 * "hi<size=1>", which is the tag we were asked to remove. Failing closed returns
			 * nothing at all. */
			string nested = "<size=1>";
			for (int i = 0; i < ChatSanitizer.MaxPasses; ++i)
			{
				nested = "<siz" + nested + "e=1>";
			}

			Assert.AreEqual("", ChatSanitizer.StripRichText("hi" + nested));
		}

		/// <summary>Nesting just inside the cap still cleans successfully.</summary>
		[Test]
		public void StripRichText_WithinPassCap_CleansSuccessfully()
		{
			string nested = "<size=1>";
			for (int i = 0; i < ChatSanitizer.MaxPasses - 2; ++i)
			{
				nested = "<siz" + nested + "e=1>";
			}

			Assert.AreEqual("hi", ChatSanitizer.StripRichText("hi" + nested));
		}

		/// <summary>Newlines cannot be used to scroll everyone else's chat off the screen.</summary>
		[Test]
		public void StripControlCharacters_NewlinesBecomeSpaces()
		{
			Assert.AreEqual("a b c", ChatSanitizer.StripControlCharacters("a\nb\rc"));
			Assert.AreEqual("a b", ChatSanitizer.StripControlCharacters("a\tb"));
		}

		/// <summary>U+202E reverses the rendering of everything after it; it must never ship.</summary>
		[Test]
		public void StripControlCharacters_RemovesBidirectionalOverrides()
		{
			Assert.AreEqual("abc", ChatSanitizer.StripControlCharacters("a‮b‭c"));
			Assert.AreEqual("ab", ChatSanitizer.StripControlCharacters("a​b"));
		}

		/// <summary>Text with nothing to strip is returned by reference, not rebuilt.</summary>
		[Test]
		public void StripControlCharacters_CleanText_IsUnchanged()
		{
			Assert.AreEqual("hello there", ChatSanitizer.StripControlCharacters("hello there"));
		}

		/// <summary>
		/// The forged-whisper bug: <c>/tell Bob FISHMMO_TELL_RELAYED ...</c> put a message in
		/// Bob's log that looked like one Bob had sent to somebody else.
		/// </summary>
		[Test]
		public void StripChatCodes_RemovesControlCodePrefix()
		{
			Assert.AreEqual("TELL_RELAYED hi", ChatSanitizer.StripChatCodes("FISHMMO_TELL_RELAYED hi"));
			Assert.AreEqual("TARGET_OFFLINE", ChatSanitizer.StripChatCodes("fishmmo_TARGET_OFFLINE"));
		}

		/// <summary>Splitting the prefix must not let it reassemble, so this loops too.</summary>
		[Test]
		public void StripChatCodes_ReassembledPrefix_IsRemoved()
		{
			Assert.AreEqual("TELL_RELAYED", ChatSanitizer.StripChatCodes("FISHMMOFISHMMO__TELL_RELAYED"));
		}

		/// <summary>A word merely containing "fish" is not a control code.</summary>
		[Test]
		public void StripChatCodes_OrdinaryText_IsUnchanged()
		{
			Assert.AreEqual("i caught a fish", ChatSanitizer.StripChatCodes("i caught a fish"));
		}

		/// <summary>
		/// Order matters: a tag inside the prefix must not reassemble it once the tag is removed.
		/// </summary>
		[Test]
		public void SanitizeIncoming_TagSplittingAControlCode_DoesNotReassembleIt()
		{
			Assert.AreEqual("TELL_RELAYED hi", ChatSanitizer.SanitizeIncoming("FISH<b>MMO_TELL_RELAYED hi", 128));
		}

		/// <summary>The whole pipeline on a message using every trick at once.</summary>
		[Test]
		public void SanitizeIncoming_CombinedAttack_IsFullyCleaned()
		{
			string result = ChatSanitizer.SanitizeIncoming("<siz<SIZE=500>e=500>‮hi\nthere<sprite=3>", 128);
			Assert.AreEqual("hi there", result);
		}

		/// <summary>Length is capped hard, with no ellipsis appended past the limit.</summary>
		[Test]
		public void SanitizeIncoming_TruncatesToMaxLength()
		{
			string result = ChatSanitizer.SanitizeIncoming(new string('a', 300), 128);
			Assert.AreEqual(128, result.Length);
		}

		/// <summary>A message that is nothing but markup cleans to empty, and that is allowed.</summary>
		[Test]
		public void SanitizeIncoming_MarkupOnly_ReturnsEmpty()
		{
			Assert.AreEqual("", ChatSanitizer.SanitizeIncoming("<b>", 128));
		}

		/// <summary>Surrogate pairs are not control characters. Emoji survive.</summary>
		[Test]
		public void SanitizeIncoming_Emoji_Survives()
		{
			Assert.AreEqual("hi \U0001F600", ChatSanitizer.SanitizeIncoming("hi \U0001F600", 128));
		}
	}
}
