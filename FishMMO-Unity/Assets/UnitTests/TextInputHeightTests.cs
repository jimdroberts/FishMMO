using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs that a single-line text input is never pinned to an exact height (issue #186).
	/// </summary>
	/// <remarks>
	/// <para>
	/// A UI Toolkit TextField draws its glyphs in an element nested two levels down. Give the
	/// TextField an exact height and that inner element has to fit inside whatever is left after the
	/// border and padding of the wrapper between them, which is less than the line box an 11px font
	/// asks for — so the text draws clipped across the top and bottom.
	/// </para>
	/// <para>
	/// Reported first on the guild create prompt, whose rule is on the shared input dialog and so
	/// carried the fault to every caller: the guild MOTD and notice editors, the guild and party
	/// invite prompts, rank renaming, member notes, the friend-list add, and the login and register
	/// prompts. Then reported again on the guild roster search box, which is a separate rule.
	/// </para>
	/// <para>
	/// The first version of this fixture missed that second one, and the miss is the reason the sweep
	/// is written the way it is. It looked for style rules whose SELECTOR mentioned an input, which
	/// cannot see a field named for its job — <c>.guild-search</c>, <c>.mail-compose__field</c>. Two
	/// more pinned mail fields were hiding behind the same blind spot. So the sweep starts from the
	/// UXML instead and asks what a TextField is actually wearing: a class is checked because a text
	/// field is using it, not because of what it is called.
	/// </para>
	/// <para>
	/// Multiline fields are exempt. A text area is a deliberately sized box that scrolls its content,
	/// which is a different control with a different rule — the map note body and the mail message
	/// body are both legitimately fixed.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class TextInputHeightTests
	{
		private const string DialogInputStylePath =
			"Assets/Scripts/Client/GUI/Shared/DialogBox/UIDialogInputBox.uss";

		private static string GuiRoot =>
			Path.Combine(Directory.GetCurrentDirectory(), "Assets/Scripts/Client/GUI");

		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path);
		}

		/// <summary>Removes block comments, which discuss these properties by name.</summary>
		/// <remarks>
		/// Every rule in these sheets carries a comment explaining itself, and those comments name the
		/// properties they are warning against. Reading a declaration out of the prose would report
		/// the warning as though it were the rule.
		/// </remarks>
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
		/// <remarks>Matched on the property name, so "min-height" is not mistaken for "height".</remarks>
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

		/// <summary>The declarations of the first rule whose selector contains <paramref name="selector"/>.</summary>
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
		public void TheRosterFilterBarSizesItsControlsTogether()
		{
			/* The follow-on defect from the fix above, reported after it: freeing the search field to
			 * size to its own text left it visibly taller than the two buttons beside it, which were
			 * still pinned to 20px.
			 *
			 * There is no pixel value that fixes this. A text field must size to its text or it
			 * clips, and what that comes to is UITK's business — so a sheet that names a height for
			 * the buttons is naming a number it cannot keep in agreement with the field. Stretch
			 * makes the row the one source of the height and hands the same one to every child, which
			 * holds whatever the field resolves to.
			 *
			 * The general rule this stands for: a control sharing a row with a text field must not
			 * pin its own height. */
			string sheet = ReadSource("Assets/Scripts/Client/GUI/World/Guild/UIGuild.uss");

			LogAssert.IsTrue(Declares(RuleBody(sheet, ".guild-filter-bar"), "align-items"),
				"the filter bar must give its children a height, so they cannot disagree");

			LogAssert.IsTrue(RuleBody(sheet, ".guild-filter-bar").Contains("stretch"),
				"centring lets each child keep its own height; stretch is what makes them equal");

			LogAssert.IsFalse(Declares(RuleBody(sheet, ".guild-filter-button"), "height"),
				"a button beside a text field must take the row's height, not name its own");
		}

		[Test]
		public void NoSingleLineTextFieldWearsAPinnedHeight()
		{
			LogAssert.IsTrue(Directory.Exists(GuiRoot), $"the GUI must live at {GuiRoot}");

			Dictionary<string, string> wornBy = ClassesWornBySingleLineTextFields();
			LogAssert.IsTrue(wornBy.Count > 0, "there must be text fields to check");

			List<string> offenders = new List<string>();

			foreach (string sheet in Directory.GetFiles(GuiRoot, "*.uss", SearchOption.AllDirectories))
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

					if (!Declares(body, "height"))
					{
						continue;
					}

					foreach (KeyValuePair<string, string> worn in wornBy)
					{
						if (Targets(selector, worn.Key))
						{
							offenders.Add(
								$"{Path.GetFileName(sheet)} '{selector}' pins the height of {worn.Value}");
						}
					}
				}
			}

			LogAssert.IsTrue(offenders.Count == 0,
				"a single-line text field must size to its own text or its glyphs clip — use min-height. " +
				string.Join("; ", offenders));
		}

		/// <summary>Every USS class worn by a single-line TextField, mapped to where it is worn.</summary>
		private static Dictionary<string, string> ClassesWornBySingleLineTextFields()
		{
			Dictionary<string, string> worn = new Dictionary<string, string>();

			foreach (string layout in Directory.GetFiles(GuiRoot, "*.uxml", SearchOption.AllDirectories))
			{
				string source = File.ReadAllText(layout);

				foreach (Match field in Regex.Matches(source, "<ui:TextField\\b[^>]*>"))
				{
					string tag = field.Value;

					// A text area is a sized, scrolling box: pinning its height is the correct thing.
					if (tag.Contains("multiline=\"true\""))
					{
						continue;
					}

					Match classes = Regex.Match(tag, "class=\"([^\"]*)\"");
					if (!classes.Success)
					{
						continue;
					}

					Match named = Regex.Match(tag, "name=\"([^\"]*)\"");
					string where = $"{Path.GetFileName(layout)}:{(named.Success ? named.Groups[1].Value : "?")}";

					foreach (string cssClass in classes.Groups[1].Value.Split(
						new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
					{
						worn[cssClass] = where;
					}
				}
			}

			return worn;
		}

		/// <summary>True when the rule applies to the element wearing <paramref name="cssClass"/> itself.</summary>
		/// <remarks>
		/// The last simple selector is the one that decides what a rule lands on. A rule reaching a
		/// DESCENDANT of the field — <c>.fish-input &gt; #unity-text-input</c> — has a different
		/// subject, and that is where the theme legitimately does its work.
		/// </remarks>
		private static bool Targets(string selector, string cssClass)
		{
			foreach (string alternative in selector.Split(','))
			{
				string trimmed = alternative.Trim();
				if (trimmed.Length == 0)
				{
					continue;
				}

				string[] parts = trimmed.Split(new[] { ' ', '\t', '\n', '\r', '>' },
					StringSplitOptions.RemoveEmptyEntries);

				string subject = parts[parts.Length - 1].Split(':')[0];

				if (Regex.IsMatch(subject, "\\." + Regex.Escape(cssClass) + "(?![\\w-])"))
				{
					return true;
				}
			}

			return false;
		}
	}
}
