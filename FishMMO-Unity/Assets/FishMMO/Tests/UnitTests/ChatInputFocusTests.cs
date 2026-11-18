using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs for the chat input keeping and refusing keyboard focus, and for its width.
	/// </summary>
	/// <remarks>
	/// Reported as "chat input loses or gains focus randomly on certain hotkeys" and "the input
	/// does not scroll right". The causes, each pinned below: Tab moved focus out of the field,
	/// because the text field's own handler focuses the next element on Tab; Tab also toggled
	/// mouse mode mid-sentence; Tab with nothing focused landed focus in the chat input through
	/// the focus ring; Enter or '/' typed into another text field pulled focus into chat; and the
	/// field widened with its text, so the caret was scrolled into a part of the field the panel
	/// clips.
	/// </remarks>
	[TestFixture]
	public class ChatInputFocusTests
	{
		private const string ChatPath = "Assets/FishMMO/Client/GUI/World/Chat/UITKChat.cs";
		private const string StylePath = "Assets/FishMMO/Client/GUI/World/Chat/UIChat.uss";
		private const string InputPath = "Assets/FishMMO/Client/Input/PlayerInputController.cs";

		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(ProjectFiles.Exists(path), $"{relativePath} not found at {path}.");
			return ProjectFiles.ReadAllText(path);
		}

		private static string MethodBody(string source, string signature, string nextSymbol)
		{
			int start = source.IndexOf(signature, StringComparison.Ordinal);
			LogAssert.IsTrue(start >= 0, $"the source must still declare {signature}");
			int end = source.IndexOf(nextSymbol, start + signature.Length, StringComparison.Ordinal);
			LogAssert.IsTrue(end > start, $"{nextSymbol} must follow {signature}");
			return source.Substring(start, end - start);
		}

		/// <summary>The declarations of the first USS rule whose selector is exactly <paramref name="selector"/>.</summary>
		private static string Rule(string uss, string selector)
		{
			Match m = Regex.Match(uss, "(^|\\n)" + Regex.Escape(selector) + "\\s*\\{([^}]*)\\}");
			LogAssert.IsTrue(m.Success, $"UIChat.uss must still have a {selector} rule");
			return m.Groups[2].Value;
		}

		[Test]
		public void TabInsideTheFieldIsSwallowed()
		{
			string body = MethodBody(ReadSource(ChatPath), "private void OnInputKeyDown", "private void OnInputFocusOut");
			int tab = body.IndexOf("KeyCode.Tab", StringComparison.Ordinal);
			LogAssert.IsTrue(tab >= 0, "the input must handle Tab itself, or the text field moves focus to the next element");
			string after = body.Substring(tab);
			LogAssert.IsTrue(after.Contains("StopPropagation()"), "the Tab press must not reach the inner text element");
		}

		[Test]
		public void ChatIsOutsideTheTabFocusRing()
		{
			string source = ReadSource(ChatPath);
			string helper = MethodBody(source, "private static void ExcludeFromTabNavigation", "private void BuildTabButton");
			LogAssert.IsTrue(helper.Contains("tabIndex = -1"), "a negative tab index is what keeps an element out of the ring");
			string starting = MethodBody(source, "public override void OnStarting()", "protected override void OnAfterStarting()");
			LogAssert.IsTrue(starting.Contains("ExcludeFromTabNavigation(Root)"), "the whole panel must be excluded when the tree is wired");
			string tabs = MethodBody(source, "private void BuildTabButton", "private void RebuildTabViews");
			LogAssert.IsTrue(tabs.Contains("ExcludeFromTabNavigation("), "tab buttons are built later and must be excluded as they are made");
		}

		[Test]
		public void ChatDoesNotTakeFocusFromAnotherField()
		{
			string body = MethodBody(ReadSource(ChatPath), "public void EnableChatInput()", "private void UpdateCursorRelease()");
			int gate = body.IndexOf("UIManager.InputControlHasFocus(this)", StringComparison.Ordinal);
			int focus = body.IndexOf("FocusInputLikeAClick()", StringComparison.Ordinal);
			LogAssert.IsTrue(gate >= 0 && focus > gate, "Enter or '/' typed into another field must not pull focus into chat");
		}

		/// <summary>
		/// Enter focuses the input and frees the cursor for typing, claiming it so the auto-dismiss does not take it back
		/// mid-sentence; sending or Escape returns to aim mode when that is where the player came from.
		/// </summary>
		[Test]
		public void OpeningChatFreesTheCursorUntilTheLineIsDone()
		{
			string source = ReadSource(ChatPath);
			string body = MethodBody(source, "public void EnableChatInput()", "private void UpdateCursorRelease()");
			int record = body.IndexOf("typingStartedInMouseMode = PlayerInputController.MouseMode", StringComparison.Ordinal);
			int focus = body.IndexOf("FocusInputLikeAClick()", StringComparison.Ordinal);
			int claim = body.IndexOf("ReleasesCursor = true", StringComparison.Ordinal);
			int free = body.IndexOf("PlayerInputController.MouseMode = true", StringComparison.Ordinal);
			LogAssert.IsTrue(focus >= 0, "Enter still focuses the input");
			LogAssert.IsTrue(record >= 0 && record < free, "the mode to return to must be read before the cursor is freed");
			LogAssert.IsTrue(claim >= 0 && claim < free, "the cursor must be claimed before it is freed, or the auto-dismiss can take it straight back");
			string release = MethodBody(source, "private void UpdateCursorRelease()", "public void OnChatKey(bool enter)");
			LogAssert.IsTrue(release.Contains("IsInputFocused"), "typing must hold the cursor free");
			string send = MethodBody(source, "private void SubmitAndRelease()", "private void RestoreModeAfterTyping()");
			LogAssert.IsTrue(send.Contains("RestoreModeAfterTyping()"), "sending a line must restore the mode the player came from");
			string keys = MethodBody(source, "private void OnInputKeyDown", "private void OnInputFocusIn");
			int escape = keys.IndexOf("case KeyCode.Escape:", StringComparison.Ordinal);
			LogAssert.IsTrue(escape >= 0 && keys.IndexOf("RestoreModeAfterTyping()", escape, StringComparison.Ordinal) > escape,
				"abandoning the line with Escape must restore it too");
		}

		/// <summary>
		/// Enter's character twin (keyCode None, '\n' or '\r') must never reach the inner text element: a single-line field
		/// takes it to mean "done" and moves focus off the text, so the field stays focused but nothing typed goes in. In the
		/// browser the twin of the Enter that opened chat arrives after the field took focus.
		/// </summary>
		[Test]
		public void EntersCharacterTwinIsSwallowed()
		{
			string body = MethodBody(ReadSource(ChatPath), "private void OnInputKeyDown", "private void OnInputFocusOut");
			int twin = body.IndexOf("evt.keyCode == KeyCode.None && (evt.character == '\\n' || evt.character == '\\r')", StringComparison.Ordinal);
			int sw = body.IndexOf("switch (evt.keyCode)", StringComparison.Ordinal);
			LogAssert.IsTrue(twin >= 0 && twin < sw, "the newline twin must be caught before the key switch");
			LogAssert.IsTrue(body.Substring(twin, sw - twin).Contains("StopPropagation()"), "the twin must not reach the text element");
		}

		/// <summary>
		/// Enter is one event: the Chat action opens chat or sends the line, and the text field only swallows its copy. When
		/// the field sent on its own KeyDownEvent, the browser delivered UI Toolkit's copy of the opening Enter frames
		/// after the game's, and it sent an empty line: Enter freed the cursor and took it straight back.
		/// </summary>
		[Test]
		public void EnterIsHandledByTheChatActionAlone()
		{
			string source = ReadSource(ChatPath);
			string keys = MethodBody(source, "private void OnInputKeyDown", "private void OnInputFocusIn");
			int ret = keys.IndexOf("case KeyCode.Return:", StringComparison.Ordinal);
			int next = keys.IndexOf("case KeyCode.KeypadEnter:", ret, StringComparison.Ordinal);
			LogAssert.IsTrue(ret >= 0 && next > ret, "the key handler must still catch Return");
			string returnCase = keys.Substring(ret, next - ret);
			LogAssert.IsTrue(returnCase.Contains("StopPropagation()"), "the field must not act on Enter itself");
			LogAssert.IsFalse(returnCase.Contains("OnSubmit(") || returnCase.Contains("SubmitAndRelease("),
				"Return must not send from the key handler: the Chat action does");

			string chatKey = MethodBody(source, "public void OnChatKey(bool enter)", "private void SubmitAndRelease()");
			int focused = chatKey.IndexOf("if (IsInputFocused)", StringComparison.Ordinal);
			int submit = chatKey.IndexOf("SubmitAndRelease()", StringComparison.Ordinal);
			int open = chatKey.IndexOf("EnableChatInput()", StringComparison.Ordinal);
			LogAssert.IsTrue(focused >= 0 && submit > focused && open > submit, "typing: Enter sends; otherwise the key opens chat");
			LogAssert.IsTrue(chatKey.Contains("inputFocusedOnFrame != Time.frameCount"), "the press that opened chat must not also send");

			string input = MethodBody(ReadSource(InputPath), "private void OnChatPerformed", "/// <summary>");
			LogAssert.IsTrue(input.Contains("OnChatKey(") && input.Contains("Key.Enter"), "the Chat action must tell chat whether it was Enter");
		}

		/// <summary>
		/// Typing that began in mouse mode (Tab, then a click on the field) stays in the field after Enter sends; only typing
		/// that began in aim mode leaves it and goes back to the game.
		/// </summary>
		[Test]
		public void SendingKeepsTheFieldWhenTypingBeganInMouseMode()
		{
			string send = MethodBody(ReadSource(ChatPath), "private void SubmitAndRelease()", "private void RestoreModeAfterTyping()");
			int keep = send.IndexOf("if (typingStartedInMouseMode)", StringComparison.Ordinal);
			int release = send.IndexOf("ReleaseInputFocus()", StringComparison.Ordinal);
			LogAssert.IsTrue(keep >= 0 && release > keep, "the mouse-mode case must return before the field is released");
			LogAssert.IsTrue(send.Substring(keep, release - keep).Contains("return;"), "and keep the field focused");
		}

		/// <summary>The chat key's opening holds focus for a moment and puts it back if something takes it.</summary>
		[Test]
		public void OpeningHoldsFocusAgainstWhateverTakesIt()
		{
			string source = ReadSource(ChatPath);
			string open = MethodBody(source, "public void EnableChatInput()", "private void UpdateCursorRelease()");
			LogAssert.IsTrue(open.Contains("holdFocusUntilFrame = Time.frameCount + HoldFocusFrames"), "opening starts the hold");
			string claim = MethodBody(source, "private void UpdateCursorRelease()", "/// <summary>Frames after the chat key opens");
			LogAssert.IsTrue(claim.Contains("HoldFocusAfterOpening()") && claim.Contains("HoldingFocus"),
				"the panel keeps the cursor and restores focus during the hold");
		}

		[Test]
		public void TabWhileTypingLeavesChatInsteadOfToggling()
		{
			string body = MethodBody(ReadSource(InputPath), "private void OnToggleMouseModePerformed", "private void OnCloseLastUIPerformed");
			int gate = body.IndexOf("if (TypingIntoField)", StringComparison.Ordinal);
			int leave = body.IndexOf("LeaveInputToAimMode()", StringComparison.Ordinal);
			int toggle = body.IndexOf("ToggleMouseMode(", gate < 0 ? 0 : gate, StringComparison.Ordinal);
			LogAssert.IsTrue(gate >= 0 && leave > gate && toggle > leave, "while typing, the mouse-mode key must leave chat and return before toggling");
			LogAssert.IsTrue(body.Substring(leave, toggle - leave).Contains("return;"), "the toggle must not also run while typing");
		}

		[Test]
		public void TheFieldKeepsTheRowsWidthAndClipsItsText()
		{
			string uss = ReadSource(StylePath);
			string field = Rule(uss, ".chat-input");
			LogAssert.IsTrue(field.Contains("flex-shrink: 1"), "the field must be allowed to shrink, against the theme's flex-shrink: 0");
			LogAssert.IsTrue(field.Contains("flex-basis: 0"), "the field's size must not start from its text's width");
			LogAssert.IsTrue(field.Contains("min-width: 0"), "the field must have no content-derived minimum width");
			string input = Rule(uss, ".chat-input > #unity-text-input");
			LogAssert.IsTrue(input.Contains("overflow: hidden"), "the scrolled glyphs must be clipped to the field");
		}
	}
}
