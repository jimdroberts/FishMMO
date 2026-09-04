using System;
using System.IO;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs for where the chat window scrolls to, and when it refuses to.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Two rules that pull in opposite directions, which is why they are pinned. Sending a line
	/// returns the reader to the bottom, because the line they just sent is the one thing they are
	/// certain to want to see. A line ARRIVING must not, because moving the list under someone
	/// reading history is what makes a busy channel unreadable.
	/// </para>
	/// <para>
	/// The pill is what keeps the second rule from being a silent drop: it is how a reader who has
	/// scrolled back finds out something landed below them, and it is raised only in that case.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class ChatScrollBehaviourTests
	{
		private const string ChatPath = "Assets/Scripts/Client/GUI/World/Chat/UITKChat.cs";
		private const string LayoutPath = "Assets/Scripts/Client/GUI/World/Chat/UIChat.uxml";
		private const string StylePath = "Assets/Scripts/Client/GUI/World/Chat/UIChat.uss";

		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path);
		}

		/// <summary>The body of a named method, bounded by the next member's signature.</summary>
		/// <remarks>
		/// Bounded by a signature alone. A pattern spanning a line break depends on whether the file
		/// is stored with CRLF or LF, and git rewrites that on checkout — a test bounded that way
		/// passes on its own branch and fails once merged, for no reason connected to the code.
		/// </remarks>
		private static string MethodBody(string source, string signature, string nextSymbol)
		{
			int start = source.IndexOf(signature, StringComparison.Ordinal);
			LogAssert.IsTrue(start >= 0, $"the source must still declare {signature}");

			int end = source.IndexOf(nextSymbol, start, StringComparison.Ordinal);
			LogAssert.IsTrue(end > start, $"the end of {signature} must be locatable");

			return source.Substring(start, end - start);
		}

		[Test]
		public void SendingReturnsTheReaderToTheNewestMessage()
		{
			/* The reported defect: send a line while scrolled back and the window stayed where it
			 * was, so the message the player had just typed was not on screen. */
			string body = MethodBody(ReadSource(ChatPath), "public void OnSubmit", "private void CycleSendChannel");

			LogAssert.IsTrue(body.Contains("ForceScrollToBottom"),
				"sending must return to the bottom even when the reader had scrolled back");
		}

		[Test]
		public void AnArrivingMessageDoesNotMoveTheReader()
		{
			/* The other half, and the reason the first is not simply "always scroll". A message
			 * from someone else must leave the scroll position alone. */
			string body = MethodBody(ReadSource(ChatPath), "private void RenderRecord", "private static void DisableRichText");

			LogAssert.IsTrue(body.Contains("if (follow)"),
				"an arriving message must only scroll when the reader was already following");
			LogAssert.IsFalse(body.Contains("ForceScrollToBottom"),
				"an arriving message must never override a scrolled-back position");
		}

		[Test]
		public void AMessageThatLandsOutOfViewRaisesThePill()
		{
			string body = MethodBody(ReadSource(ChatPath), "private void RenderRecord", "private static void DisableRichText");

			LogAssert.IsTrue(body.Contains("SetNewMessagesPillVisible(true)"),
				"a message arriving below a scrolled-back reader must announce itself");
		}

		[Test]
		public void ReturningToTheBottomClearsThePill()
		{
			/* By any route — the pill, the scrollbar, or sending. A pill still showing once the
			 * reader is back at the bottom is announcing something they are already looking at. */
			string source = ReadSource(ChatPath);

			string scroll = MethodBody(source, "private void OnVerticalScroll", "private void SetNewMessagesPillVisible");
			LogAssert.IsTrue(scroll.Contains("SetNewMessagesPillVisible(false)"),
				"scrolling back to the bottom must clear the pill");

			string force = MethodBody(source, "private void ForceScrollToBottom", "#endregion");
			LogAssert.IsTrue(force.Contains("SetNewMessagesPillVisible(false)"),
				"jumping to the bottom must clear the pill");
		}

		[Test]
		public void ThePillExistsAndStartsHidden()
		{
			string layout = ReadSource(LayoutPath);
			LogAssert.IsTrue(layout.Contains("chat-new-messages"),
				"the layout must carry the pill");
			LogAssert.IsTrue(layout.Contains("chat-new-messages--hidden"),
				"the pill must start hidden, or it shows on a window nobody has scrolled");

			string style = ReadSource(StylePath);
			LogAssert.IsTrue(style.Contains("position: absolute"),
				"the pill must sit over the list rather than in it, so showing it does not reflow the messages");
		}

		[Test]
		public void ReleasingTheInputBlursWhatActuallyHasFocus()
		{
			/* A TextField delegates focus to an inner text element, so the focused element is a
			 * CHILD of the field and Blur() on the field itself can be a no-op. Focus then survives
			 * the release: EnableChatInput returns early on every later press so chat cannot be
			 * reopened, and the panel keeps asking for the cursor so mouse mode never dismisses.
			 * Reported as sending one line and then being unable to send another. */
			string source = ReadSource(ChatPath);

			string release = MethodBody(source, "private void ReleaseInputFocus", "private bool IsInputFocused");
			LogAssert.IsTrue(release.Contains("focusedElement"),
				"the release must blur the element the focus controller actually names");

			string keys = MethodBody(source, "private void OnInputKeyDown", "private void OnInputFocusOut");
			LogAssert.IsFalse(keys.Contains("inputField.Blur()"),
				"the key handler must release through ReleaseInputFocus, not the field alone");
		}

		[Test]
		public void SenderlessRowsDoNotCarryAnEmptyLabel()
		{
			/* The row wraps, and a hidden child still counts as a flex item — it opened an empty
			 * flex line above the text, so every line without a visible sender drew about twice as
			 * tall. Left out of the row entirely instead. */
			string body = MethodBody(ReadSource(ChatPath), "private void RenderRecord", "private static void DisableRichText");

			LogAssert.IsTrue(body.Contains("if (ShouldShowSender(record, index))"),
				"the sender label must be added only when it is shown");
			LogAssert.IsFalse(body.Contains("nameLabel.style.display = DisplayStyle.None"),
				"hiding the label instead of omitting it leaves an empty flex line in the row");
		}
	}
}
