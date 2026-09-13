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

		/// <summary>The shared stylesheet the input rules and the theme tokens live in.</summary>
		public const string ThemePath = "Assets/Scripts/Client/GUI/FishMMO-Theme.uss";

		/// <summary>The theme's class for a field that has given up Unity's inner padding.</summary>
		public const string CompactClass = "fish-input--compact";

		/// <summary>
		/// Every field type that draws glyphs, in the one place the sweep reads them from.
		/// </summary>
		/// <remarks>
		/// <para>
		/// THIS ARRAY IS THE CONTRACT, and it is deliberately the only copy of it. There used to be
		/// three — this one's predecessor inside the UXML pattern below, a second literal of that
		/// same pattern inside <c>FieldsWearingCompact</c>, and the type-qualified selector list in
		/// <c>FishMMO-Theme.uss</c> — kept in agreement by hand, with nothing checking that they
		/// agreed. The stylesheet is the copy that decides whether the field renders at all: Unity's
		/// default theme sets the inner surface's padding through a TYPE-qualified rule, so a field
		/// type missing from that selector keeps the padding however it is styled and draws an
		/// empty box, which is how issues #263 and #277 came back after the TextField fix appeared
		/// to settle them — the merchant's quantity box is an <c>IntegerField</c>.
		/// </para>
		/// <para>
		/// So the pattern below is built from this array rather than written out beside it, and
		/// <see cref="TheThemeNamesEveryFieldTypeThisSweepKnowsAbout"/> holds the one edge left: the
		/// array against the stylesheet.
		/// </para>
		/// </remarks>
		public static readonly string[] GlyphFieldTypes =
		{
			"TextField", "IntegerField", "FloatField", "LongField", "DoubleField",
		};

		/// <summary>A UXML opening tag for one of <see cref="GlyphFieldTypes"/>.</summary>
		/// <remarks>
		/// Public, and used by the merchant fixture as well, so a field type added to the sweep and
		/// a field type checked by name somewhere else cannot disagree about which types exist.
		/// </remarks>
		public static readonly string FieldTagPattern =
			"<ui:(" + string.Join("|", GlyphFieldTypes) + ")\\b[^>]*>";

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

		/// <summary>The selector list of the first rule whose text contains <paramref name="marker"/>.</summary>
		/// <remarks>
		/// The selector and not the body, because a test about which field TYPES a rule names has to
		/// read the part that names them — <see cref="RuleBody"/> deliberately throws that away, and
		/// the whole point of <see cref="ThemeCompactFieldTypes"/> is that the type-qualified
		/// alternatives are load bearing.
		/// </remarks>
		private static string RuleSelector(string styleSheet, string marker)
		{
			string source = StripComments(styleSheet);

			int at = source.IndexOf(marker, StringComparison.Ordinal);
			LogAssert.IsTrue(at >= 0, $"the {marker} rule must still exist");

			int open = source.IndexOf('{', at);
			LogAssert.IsTrue(open > at, $"the {marker} rule must have a body");

			int start = source.LastIndexOf('}', at) + 1;
			return source.Substring(start, open - start);
		}

		/// <summary>
		/// The field types named on the theme's <c>.fish-input--compact</c> selectors.
		/// </summary>
		/// <remarks>
		/// Only the TYPE-QUALIFIED alternatives count. The bare <c>.fish-input--compact</c> that
		/// heads that list loses the specificity fight against Unity's own type-qualified padding
		/// rule, which is why the stylesheet spells the types out — so a field type covered by the
		/// bare class alone is a field type the remedy does not reach.
		/// </remarks>
		public static HashSet<string> ThemeCompactFieldTypes()
		{
			string selector = RuleSelector(
				ReadSource(ThemePath), "." + CompactClass);

			HashSet<string> types = new HashSet<string>(StringComparer.Ordinal);

			foreach (Match alternative in Regex.Matches(selector,
				@"(?<![\w-])([A-Za-z][A-Za-z0-9_]*)\." + Regex.Escape(CompactClass) + @"(?![\w-])"))
			{
				types.Add(alternative.Groups[1].Value);
			}

			return types;
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

			List<GlyphField> fields = SingleLineGlyphFields();
			Dictionary<string, HashSet<string>> wornBy = ClassesWorn(fields);
			HashSet<string> compactFields = FieldsWearingCompact(fields);
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

					foreach (KeyValuePair<string, HashSet<string>> worn in wornBy)
					{
						if (!Targets(selector, worn.Key))
						{
							continue;
						}

						/* A pinned height is legal WITH fish-input--compact, which is the theme's own
						 * remedy: it zeroes the padding Unity puts inside the editable surface, which is
						 * what starves the glyph box at small heights. A field that pins a height and does
						 * not wear it renders an empty box — the rule this test exists to hold.
						 *
						 * The exemption is asked of EVERY field wearing the targeted class, not of one
						 * of them. `fish-input` is worn by compact and plain fields alike, so a rule
						 * pinned to it would clip whichever field does not carry the remedy — exempting
						 * it on the strength of one compact wearer would be the wrong answer.
						 *
						 * Which is also what makes this order-independent. The scan used to keep ONE
						 * location per class, last writer winning over Directory.GetFiles enumeration
						 * order, so which field answered for `fish-input` was a fact about the
						 * filesystem rather than about the rule. */
						if (worn.Value.IsSubsetOf(compactFields))
						{
							continue;
						}

						offenders.Add(
							$"{Path.GetFileName(sheet)} '{selector}' pins the height of the " +
							$"{worn.Value.Count} field(s) wearing .{worn.Key} without .{CompactClass}");
					}
				}
			}

			LogAssert.IsTrue(offenders.Count == 0,
				"a single-line text field must size to its own text or its glyphs clip — use min-height. " +
				string.Join("; ", offenders));
		}

		[Test]
		public void TheThemeNamesEveryFieldTypeThisSweepKnowsAbout()
		{
			/* The lists, cross-checked — or rather the one list and the stylesheet, which is all
			 * that is left of them once the UXML pattern is built from the array rather than
			 * written out a second time beside it.
			 *
			 * This is the invariant that actually protects the fix. A pinned height is only
			 * survivable because .fish-input--compact zeroes the inner padding, and the class
			 * alone does not do it: Unity's default theme sets that padding through a TYPE-qualified
			 * rule, so the stylesheet has to name each field type for the override to win. A type
			 * this sweep checks but the sheet does not name renders an empty box — issues #263 and
			 * #277, which came back exactly once, for IntegerField, after the TextField fix looked
			 * complete. A type the sheet names but the sweep does not read is the same hole from the
			 * other side: the sheet would be claiming to fix a field nothing ever checks. */
			HashSet<string> theme = ThemeCompactFieldTypes();

			List<string> missing = new List<string>();
			for (int i = 0; i < GlyphFieldTypes.Length; ++i)
			{
				if (!theme.Contains(GlyphFieldTypes[i]))
				{
					missing.Add(GlyphFieldTypes[i]);
				}
			}

			LogAssert.IsTrue(missing.Count == 0,
				$"the theme's .{CompactClass} selector does not name every field type this sweep " +
				"treats as glyph-drawing, so Unity's own padding survives for: " +
				string.Join(", ", missing.ToArray()) +
				" — name them there too, or the field draws an empty box however it is styled");

			List<string> unswept = new List<string>();
			foreach (string type in theme)
			{
				if (Array.IndexOf(GlyphFieldTypes, type) < 0)
				{
					unswept.Add(type);
				}
			}

			LogAssert.IsTrue(unswept.Count == 0,
				$"the theme's .{CompactClass} selector names a field type the sweep does not read, " +
				"so nothing here checks the fields of that type: " + string.Join(", ", unswept.ToArray()));
		}

		/// <summary>One single-line field that draws glyphs, as found in a UXML layout.</summary>
		private struct GlyphField
		{
			/// <summary>Where the field is, as <c>file:name</c> — one entry per field.</summary>
			public string Where;
			/// <summary>The USS classes the field wears.</summary>
			public string[] Classes;
		}

		/// <summary>
		/// Every single-line field that draws glyphs, in one pass over the UXML.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Every field type that draws glyphs, not only TextField. Scanning for TextField alone is
		/// how issues #263 and #277 returned after this test was written: the merchant quantity box
		/// is an IntegerField, so it was never checked, and it pinned an exact height exactly as
		/// the TextFields once did. The numeric fields derive from the same TextInputBaseField and
		/// their glyph box collapses the same way.
		/// </para>
		/// <para>
		/// ONE pass, because the two questions asked of it — which classes are worn, and which
		/// fields wear the compact class — must be answered about the same set of elements. They
		/// were two scans with two copies of the same pattern, and the exemption in
		/// <c>NoSingleLineTextFieldWearsAPinnedHeight</c> then trusted an answer derived from the
		/// other one.
		/// </para>
		/// </remarks>
		private static List<GlyphField> SingleLineGlyphFields()
		{
			List<GlyphField> fields = new List<GlyphField>();

			foreach (string layout in Directory.GetFiles(GuiRoot, "*.uxml", SearchOption.AllDirectories))
			{
				string source = File.ReadAllText(layout);
				string file = Path.GetFileName(layout);
				int ordinal = 0;

				foreach (Match field in Regex.Matches(source, FieldTagPattern))
				{
					++ordinal;
					string tag = field.Value;

					// A text area is a sized, scrolling box: pinning its height is the correct thing.
					if (tag.Contains("multiline=\"true\""))
					{
						continue;
					}

					Match named = Regex.Match(tag, "name=\"([^\"]*)\"");
					Match classes = Regex.Match(tag, "class=\"([^\"]*)\"");

					fields.Add(new GlyphField
					{
						/* An unnamed field is identified by where it sits in its file. Falling back
						 * to one shared "?" key would let two unnamed fields collapse into a single
						 * entry, and a class worn by both would then be judged on whichever of them
						 * happened to be seen — the same order-dependence this pass exists to
						 * remove. */
						Where = $"{file}:{(named.Success ? named.Groups[1].Value : "#" + ordinal)}",
						Classes = classes.Success
							? classes.Groups[1].Value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
							: new string[0],
					});
				}
			}

			return fields;
		}

		/// <summary>Every USS class worn by a glyph-drawing field, and every field that wears it.</summary>
		/// <remarks>
		/// A SET of fields per class, not one. A class shared between a compact field and a plain
		/// one has to be able to say so — see the exemption in the sweep.
		/// </remarks>
		private static Dictionary<string, HashSet<string>> ClassesWorn(List<GlyphField> fields)
		{
			Dictionary<string, HashSet<string>> worn =
				new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

			for (int f = 0; f < fields.Count; ++f)
			{
				for (int c = 0; c < fields[f].Classes.Length; ++c)
				{
					if (!worn.TryGetValue(fields[f].Classes[c], out HashSet<string> where))
					{
						where = new HashSet<string>(StringComparer.Ordinal);
						worn[fields[f].Classes[c]] = where;
					}

					where.Add(fields[f].Where);
				}
			}

			return worn;
		}

		/// <summary>The fields that carry fish-input--compact, by the same where-key.</summary>
		private static HashSet<string> FieldsWearingCompact(List<GlyphField> fields)
		{
			HashSet<string> compact = new HashSet<string>(StringComparer.Ordinal);

			for (int i = 0; i < fields.Count; ++i)
			{
				if (Array.IndexOf(fields[i].Classes, CompactClass) >= 0)
				{
					compact.Add(fields[i].Where);
				}
			}

			return compact;
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
