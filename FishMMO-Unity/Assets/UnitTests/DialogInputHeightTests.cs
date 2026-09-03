using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs that a text input is never pinned to an exact height (issue #186).
	/// </summary>
	/// <remarks>
	/// <para>
	/// A UI Toolkit TextField draws its glyphs in an element nested two levels down. Give the
	/// TextField an exact height and that inner element has to fit inside whatever is left after the
	/// border and padding of the wrapper between them, which is less than the line box an 11px font
	/// asks for — so the text is drawn clipped across the top and bottom.
	/// </para>
	/// <para>
	/// This was reported on the guild create prompt, but the rule that caused it lives on the shared
	/// dialog, so every caller had it: the guild MOTD and notice editors, the guild and party invite
	/// prompts, rank renaming, member notes, and the friend-list add.
	/// </para>
	/// <para>
	/// The chat input row hit the same wall from the other side and is the precedent followed here.
	/// Pinned to 26px its inner text element resolved to a height of zero and nothing drew at all;
	/// the fix was to stop pinning it. A floor under the control is fine and is what keeps the field
	/// from collapsing when empty — it is the ceiling that does the damage.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class DialogInputHeightTests
	{
		private const string DialogInputStylePath =
			"Assets/Scripts/Client/GUI/Shared/DialogBox/UIDialogInputBox.uss";

		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path);
		}

		/// <summary>The declarations of the first rule whose selector contains <paramref name="selector"/>.</summary>
		/// <remarks>
		/// Comments are stripped first. Every rule in these sheets carries a block comment explaining
		/// itself, and those comments discuss the properties by name — reading a declaration out of
		/// the prose would report whatever the comment was warning against as though it were still
		/// set.
		/// </remarks>
		private static string RuleBody(string styleSheet, string selector)
		{
			string source = StripComments(styleSheet);

			int at = source.IndexOf(selector, StringComparison.Ordinal);
			LogAssert.IsTrue(at >= 0, $"the {selector} rule must still exist");

			int open = source.IndexOf('{', at);
			int close = source.IndexOf('}', open);
			LogAssert.IsTrue(open > at && close > open, $"the {selector} rule must have a body");

			return source.Substring(open + 1, close - open - 1);
		}

		private static string StripComments(string source)
		{
			while (true)
			{
				int open = source.IndexOf("/*", StringComparison.Ordinal);
				if (open < 0)
				{
					return source;
				}

				int close = source.IndexOf("*/", open + 2, StringComparison.Ordinal);
				if (close < 0)
				{
					return source.Substring(0, open);
				}

				source = source.Remove(open, close - open + 2);
			}
		}

		/// <summary>True when the block sets <paramref name="property"/> as its own declaration.</summary>
		/// <remarks>
		/// Matched on the property name alone rather than by searching for the text, so that
		/// "min-height" and "max-height" are not mistaken for "height".
		/// </remarks>
		private static bool Declares(string ruleBody, string property)
		{
			foreach (string declaration in ruleBody.Split(';'))
			{
				int colon = declaration.IndexOf(':');
				if (colon > 0 && declaration.Substring(0, colon).Trim() == property)
				{
					return true;
				}
			}

			return false;
		}

		[Test]
		public void TheSharedDialogInputIsNotPinnedToAnExactHeight()
		{
			string body = RuleBody(ReadSource(DialogInputStylePath), ".dialog-input-field");

			LogAssert.IsFalse(Declares(body, "height"),
				"an exact height clips the glyphs of the field's own text; use min-height");
		}

		[Test]
		public void TheSharedDialogInputKeepsAFloorUnderIt()
		{
			/* The half that is not just a deletion. Without a floor the field collapses toward
			 * nothing while it is empty, which is most of the time a prompt is on screen. */
			string body = RuleBody(ReadSource(DialogInputStylePath), ".dialog-input-field");

			LogAssert.IsTrue(Declares(body, "min-height"),
				"the field must keep a minimum height so an empty prompt still shows a box to type in");
		}

		[Test]
		public void NoStyleSheetPinsTheHeightOfATextInput()
		{
			/* The sweep. The reported symptom appeared on four separate prompts because one shared
			 * rule carried it, and the same mistake is available to every sheet that styles a field.
			 * Containers are exempt: pinning a ROW is how the chat input row is built, and it is the
			 * control inside that must be free to size itself. */
			string guiRoot = Path.Combine(Directory.GetCurrentDirectory(), "Assets/Scripts/Client/GUI");
			LogAssert.IsTrue(Directory.Exists(guiRoot), $"the GUI style sheets must live at {guiRoot}");

			string[] sheets = Directory.GetFiles(guiRoot, "*.uss", SearchOption.AllDirectories);
			LogAssert.IsTrue(sheets.Length > 0, "there must be style sheets to check");

			List<string> offenders = new List<string>();

			foreach (string sheet in sheets)
			{
				string source = StripComments(File.ReadAllText(sheet));
				int cursor = 0;

				while (true)
				{
					int open = source.IndexOf('{', cursor);
					if (open < 0)
					{
						break;
					}

					int close = source.IndexOf('}', open);
					if (close < 0)
					{
						break;
					}

					string selector = source.Substring(cursor, open - cursor).Trim();
					string body = source.Substring(open + 1, close - open - 1);
					cursor = close + 1;

					if (!NamesATextInput(selector) || !Declares(body, "height"))
					{
						continue;
					}

					offenders.Add($"{Path.GetFileName(sheet)}: {selector}");
				}
			}

			LogAssert.IsTrue(offenders.Count == 0,
				"a text input must size to its own text, or its glyphs clip; use min-height in: " +
				string.Join(", ", offenders));
		}

		/// <summary>True for a selector that styles a field itself rather than something around it.</summary>
		private static bool NamesATextInput(string selector)
		{
			string lowered = selector.ToLowerInvariant();

			if (!lowered.Contains("input") && !lowered.Contains("textfield"))
			{
				return false;
			}

			/* Wrappers, and controls that merely have an "input" part in their UITK internals --
			 * a toggle's checkmark and a dropdown's popup are sized boxes, not text, and pinning
			 * their height is correct. */
			foreach (string container in new[]
			{
				"row", "container", "panel", "body", "selector", "button", "label",
				"toggle", "dropdown", "slider", "checkmark", "popup",
			})
			{
				if (lowered.Contains(container))
				{
					return false;
				}
			}

			return true;
		}
	}
}
