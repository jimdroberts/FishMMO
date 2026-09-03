using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs that every panel's close button is the same control.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The size used to live on each panel's own rule. Fourteen panels agreed on 26x26 by copying
	/// each other, and four did not: inspect at 22, ability-craft at 24, and the map and dungeon
	/// finder naming no size at all, so their buttons sized to the glyph. Ability-craft had also
	/// drifted onto <c>fish-button</c> rather than <c>fish-close-btn</c>, so it was not even the
	/// same control underneath, and two panels drew a capital X instead of the multiplication sign.
	/// </para>
	/// <para>
	/// None of that is panel-specific. A close button is the same affordance wherever it appears, so
	/// the shared class owns it and a panel adds a rule only for what genuinely differs — the margin
	/// separating the button from the title beside it.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class CloseButtonConsistencyTests
	{
		private const string ThemePath = "Assets/Scripts/Client/GUI/FishMMO-Theme.uss";

		private static string GuiRoot =>
			Path.Combine(Directory.GetCurrentDirectory(), "Assets/Scripts/Client/GUI");

		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path);
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

		/// <summary>Every close-button element declared in the project's UXML.</summary>
		private static List<string> CloseButtonTags()
		{
			List<string> tags = new List<string>();

			foreach (string layout in Directory.GetFiles(GuiRoot, "*.uxml", SearchOption.AllDirectories))
			{
				foreach (Match button in Regex.Matches(File.ReadAllText(layout), "<ui:Button\\b[^>]*>"))
				{
					string tag = button.Value;

					// A close button is one that closes a panel, named or classed as such. Buttons
					// whose LABEL is the word Close (the options and colour-picker footers) are
					// ordinary footer buttons and are deliberately not in scope.
					bool named = Regex.IsMatch(tag, "name=\"[^\"]*close[^\"]*\"", RegexOptions.IgnoreCase);
					bool classed = tag.Contains("fish-close-btn");

					if ((named || classed) && !Regex.IsMatch(tag, "text=\"Close\"|text=\"Resume\""))
					{
						tags.Add($"{Path.GetFileName(layout)}: {tag}");
					}
				}
			}

			return tags;
		}

		[Test]
		public void TheSharedCloseButtonOwnsItsSize()
		{
			string theme = StripComments(ReadSource(ThemePath));

			int at = theme.IndexOf(".fish-close-btn {", StringComparison.Ordinal);
			LogAssert.IsTrue(at >= 0, "the shared close-button rule must exist");

			int open = theme.IndexOf('{', at);
			int close = theme.IndexOf('}', open);
			string body = theme.Substring(open + 1, close - open - 1);

			LogAssert.IsTrue(Declares(body, "width") && Declares(body, "height"),
				"the shared rule must name the size, or panels will each invent their own");
		}

		[Test]
		public void EveryCloseButtonUsesTheSharedClass()
		{
			List<string> offenders = new List<string>();

			foreach (string tag in CloseButtonTags())
			{
				if (!tag.Contains("fish-close-btn"))
				{
					offenders.Add(tag);
				}
			}

			LogAssert.IsTrue(offenders.Count == 0,
				"a close button must use fish-close-btn, not fish-button: " + string.Join("; ", offenders));
		}

		[Test]
		public void EveryCloseButtonDrawsTheSameGlyph()
		{
			/* A capital X is a letter and renders as one — narrower, and in the panel's body font
			 * rather than as a symbol. Two panels had drifted onto it. */
			List<string> offenders = new List<string>();

			foreach (string tag in CloseButtonTags())
			{
				if (!tag.Contains("text=\"✕\""))
				{
					offenders.Add(tag);
				}
			}

			LogAssert.IsTrue(offenders.Count == 0,
				"every close button must draw the same glyph: " + string.Join("; ", offenders));
		}

		[Test]
		public void NoPanelRedeclaresTheCloseButtonSize()
		{
			/* The rule that keeps them from drifting apart again. A panel may still position its
			 * button — margin is genuinely per-panel — but the moment one names a width or a height
			 * the buttons stop being the same control. */
			List<string> offenders = new List<string>();

			foreach (string sheet in Directory.GetFiles(GuiRoot, "*.uss", SearchOption.AllDirectories))
			{
				if (Path.GetFileName(sheet) == "FishMMO-Theme.uss")
				{
					continue;
				}

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

					if (selector.IndexOf("close", StringComparison.OrdinalIgnoreCase) < 0)
					{
						continue;
					}

					foreach (string sized in new[] { "width", "height", "min-width", "min-height" })
					{
						if (Declares(body, sized))
						{
							offenders.Add($"{Path.GetFileName(sheet)} '{selector}' sets {sized}");
						}
					}
				}
			}

			LogAssert.IsTrue(offenders.Count == 0,
				"close-button size belongs to the shared class only: " + string.Join("; ", offenders));
		}
	}
}
