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
		private const string ChatPath = "Assets/Scripts/Client/GUI/World/Chat/UITKChat.cs";
		private const string StylePath = "Assets/Scripts/Client/GUI/World/Chat/UIChat.uss";
		private const string InputPath = "Assets/Scripts/Client/Input/PlayerInputController.cs";

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
			int focus = body.IndexOf("inputField.Focus()", StringComparison.Ordinal);
			LogAssert.IsTrue(gate >= 0 && focus > gate, "Enter or '/' typed into another field must not pull focus into chat");
		}

		/// <summary>
		/// Enter focuses the input and leaves the cursor alone. Freeing it exited pointer lock in the browser, and taking it back
		/// after the line was sent is refused outside a click, so each chat line cost a click to get the mouse back.
		/// </summary>
		[Test]
		public void OpeningChatLeavesTheCursorAlone()
		{
			string source = ReadSource(ChatPath);
			string body = MethodBody(source, "public void EnableChatInput()", "private void UpdateCursorRelease()");
			LogAssert.IsTrue(body.Contains("inputField.Focus()"), "Enter still focuses the input");
			LogAssert.IsFalse(body.Contains("MouseMode = true"), "focusing the input must not free the cursor");
			string release = MethodBody(source, "private void UpdateCursorRelease()", "/// <summary>Releases keyboard focus from the input");
			LogAssert.IsFalse(release.Contains("IsInputFocused"), "typing must not claim the cursor either");
		}

		[Test]
		public void TabDoesNotToggleMouseModeWhileTyping()
		{
			string body = MethodBody(ReadSource(InputPath), "private void OnToggleMouseModePerformed", "private void OnCloseLastUIPerformed");
			int gate = body.IndexOf("if (TypingIntoField) return;", StringComparison.Ordinal);
			int toggle = body.IndexOf("ToggleMouseMode(", gate < 0 ? 0 : gate, StringComparison.Ordinal);
			LogAssert.IsTrue(gate >= 0 && toggle > gate, "the mouse-mode key must be ignored while a text field has focus");
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
