using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// No runtime panel may declare a keyword cursor.
	/// </summary>
	/// <remarks>
	/// <para>
	/// USS keywords such as <c>cursor: link</c> are editor-only. A runtime panel can only set the
	/// cursor from a texture, so the keyword resolves to nothing and Unity logs
	/// </para>
	/// <para>
	/// <c>"Runtime cursors other than the default cursor need to be defined using a texture."</c>
	/// </para>
	/// <para>
	/// from both UIElementsRuntimeUtility and the EventSystem on every frame the pointer rests on
	/// the element — a console full of warnings for a cursor that never actually changes.
	/// <c>UIGuild.uss</c> carried one on the rank header, which is a control the player clicks to
	/// fold a rank open, so the pointer sat there for long stretches.
	/// </para>
	/// <para>
	/// The sweep is the runtime tree only, and deliberately so. The editor tool windows — the
	/// FishMMO dashboard, the Addressables dashboard, the name generator — run on the editor panel,
	/// where keyword cursors <em>do</em> resolve, and they use <c>cursor: link</c> and
	/// <c>cursor: resize-horizontal</c> on purpose. Widening this to all of Assets would demand
	/// deleting working editor affordances. See FishMMO-Theme.uss, "NO CURSOR RULES", and
	/// <see cref="TheEditorToolWindowsKeepTheirKeywordCursors"/>.
	/// </para>
	/// <para>
	/// Comments are stripped before scanning, and not as a nicety: the theme's own documentation of
	/// this rule spells out both the illegal keyword form and the legal texture form, so a scan of
	/// the raw text flags the file that states the rule. <see cref="TheThemesOwnDocumentationIsNotADeclaration"/>
	/// pins that.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class StylesheetCursorTests
	{
		private const string ThemeSheet = "FishMMO-Theme.uss";
		private const string GuildSheet = "UIGuild.uss";
		private const string ThemePath = "Assets/Scripts/Client/GUI/FishMMO-Theme.uss";

		/// <summary>An editor window sheet, kept as the control for the scope boundary.</summary>
		private const string EditorSheetPath =
			"Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/FishMMO Dashboard/FishMMODashboard.uss";

		private static string GuiRoot =>
			Path.Combine(Directory.GetCurrentDirectory(), "Assets/Scripts/Client/GUI");

		/// <summary>One <c>cursor</c> declaration, named well enough to fix without opening the file.</summary>
		private sealed class Declaration
		{
			public string Sheet;
			public string Selector;
			public string Value;

			public override string ToString()
			{
				return $"{Sheet} '{Selector}' sets cursor: {Value}";
			}
		}

		private static string ReadSource(string path)
		{
			LogAssert.IsTrue(File.Exists(path), $"expected a file at {path}");
			return File.ReadAllText(path);
		}

		private static string StripCssComments(string source)
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

		private static string StripXmlComments(string source)
		{
			while (true)
			{
				int open = source.IndexOf("<!--", StringComparison.Ordinal);
				if (open < 0)
				{
					return source;
				}

				int close = source.IndexOf("-->", open + 4, StringComparison.Ordinal);
				if (close < 0)
				{
					return source.Substring(0, open);
				}

				source = source.Remove(open, close - open + 3);
			}
		}

		/// <summary>The value of <paramref name="property"/> in a rule body, or null if it is not set.</summary>
		private static string ValueOf(string ruleBody, string property)
		{
			foreach (string declaration in ruleBody.Split(';'))
			{
				int colon = declaration.IndexOf(':');
				if (colon > 0 && declaration.Substring(0, colon).Trim() == property)
				{
					return declaration.Substring(colon + 1).Trim();
				}
			}

			return null;
		}

		/// <summary>
		/// A cursor is legal only in the texture form. Anything else is a keyword, which a runtime
		/// panel cannot resolve.
		/// </summary>
		private static bool IsTextureForm(string value)
		{
			return value.IndexOf("url(", StringComparison.OrdinalIgnoreCase) >= 0;
		}

		/// <summary>Every runtime stylesheet, theme included.</summary>
		private static List<string> RuntimeSheets()
		{
			return new List<string>(Directory.GetFiles(GuiRoot, "*.uss", SearchOption.AllDirectories));
		}

		/// <summary>Every runtime layout.</summary>
		private static List<string> RuntimeLayouts()
		{
			return new List<string>(Directory.GetFiles(GuiRoot, "*.uxml", SearchOption.AllDirectories));
		}

		/// <summary>Walks a stylesheet's rule blocks and collects every <c>cursor</c> declaration.</summary>
		private static List<Declaration> CursorDeclarations(string sheetPath)
		{
			List<Declaration> found = new List<Declaration>();
			string source = StripCssComments(ReadSource(sheetPath));
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

				string value = ValueOf(body, "cursor");
				if (value != null)
				{
					found.Add(new Declaration { Sheet = Path.GetFileName(sheetPath), Selector = selector, Value = value });
				}
			}

			return found;
		}

		/// <summary>
		/// A real sweep, of real sheets, including the two that matter.
		/// </summary>
		/// <remarks>
		/// Without this the rule below passes for the wrong reason the moment the root is mistyped or
		/// the pattern is narrowed — an empty set of sheets has no violations in it.
		/// </remarks>
		[Test]
		public void TheSweepCoversTheRuntimePanels()
		{
			List<string> sheets = RuntimeSheets();
			List<string> names = sheets.ConvertAll(Path.GetFileName);

			LogAssert.IsTrue(names.Contains(ThemeSheet), $"the theme must be swept; looked in {GuiRoot}");
			LogAssert.IsTrue(names.Contains(GuildSheet), $"{GuildSheet} must be swept; looked in {GuiRoot}");
			LogAssert.IsTrue(names.Contains("UIFactions.uss"), "a panel sheet must be swept");

			/* A floor rather than an exact count: sheets are added, and this should never be the test
			 * that has to be edited when one is. It only has to be low enough to fail if the sweep
			 * silently stops finding files. */
			LogAssert.IsTrue(sheets.Count >= 40,
				$"the runtime tree should hold dozens of sheets, found {sheets.Count} in {GuiRoot}");
		}

		[Test]
		public void NoRuntimeStylesheetDeclaresAKeywordCursor()
		{
			List<Declaration> offenders = new List<Declaration>();

			foreach (string sheet in RuntimeSheets())
			{
				foreach (Declaration declaration in CursorDeclarations(sheet))
				{
					if (!IsTextureForm(declaration.Value))
					{
						offenders.Add(declaration);
					}
				}
			}

			LogAssert.IsTrue(offenders.Count == 0,
				"a runtime panel cannot resolve a keyword cursor, so it warns every frame for a cursor that " +
				"never changes. Use hover feedback instead, or the `cursor: url(...)` texture form. Offenders: " +
				string.Join("; ", offenders));
		}

		/// <summary>
		/// The same rule for inline styles, which are the way a cursor can reach a runtime panel
		/// without ever touching a stylesheet.
		/// </summary>
		[Test]
		public void NoRuntimeLayoutDeclaresAKeywordCursorInline()
		{
			List<string> offenders = new List<string>();

			foreach (string layout in RuntimeLayouts())
			{
				string source = StripXmlComments(ReadSource(layout));

				foreach (Match style in Regex.Matches(source, "style=\"([^\"]*)\""))
				{
					string value = ValueOf(style.Groups[1].Value, "cursor");
					if (value != null && !IsTextureForm(value))
					{
						offenders.Add($"{Path.GetFileName(layout)}: style=\"cursor: {value}\"");
					}
				}
			}

			LogAssert.IsTrue(offenders.Count == 0,
				"an inline keyword cursor warns exactly as a stylesheet one does: " + string.Join("; ", offenders));
		}

		/// <summary>
		/// The theme states this rule, so its own text mentions cursors — in a comment.
		/// </summary>
		/// <remarks>
		/// If comment stripping ever regresses, this fixture fails on the file that documents it while
		/// every other sheet stays silent, which is a confusing way to find out. This says why.
		/// </remarks>
		[Test]
		public void TheThemesOwnDocumentationIsNotADeclaration()
		{
			string raw = ReadSource(ThemePath);
			LogAssert.IsTrue(raw.Contains("path/to/hand.png"),
				"the theme is expected to document the legal texture form; if that moved, move this control with it");

			string stripped = StripCssComments(raw);
			LogAssert.IsFalse(stripped.IndexOf("cursor", StringComparison.OrdinalIgnoreCase) >= 0,
				"the theme mentions cursors only in prose, so stripping its comments must leave none behind");
		}

		/// <summary>
		/// The scope boundary, pinned, so nobody widens the sweep and then deletes working editor code.
		/// </summary>
		[Test]
		public void TheEditorToolWindowsKeepTheirKeywordCursors()
		{
			LogAssert.IsTrue(File.Exists(EditorSheetPath), $"expected the editor dashboard sheet at {EditorSheetPath}");

			List<Declaration> declarations = CursorDeclarations(EditorSheetPath);
			LogAssert.IsTrue(declarations.Count > 0,
				"the editor dashboard is expected to use keyword cursors on purpose; if it stopped, this " +
				"fixture no longer proves the runtime rule is scoped to runtime panels");

			foreach (Declaration declaration in declarations)
			{
				LogAssert.IsFalse(IsTextureForm(declaration.Value),
					$"the editor sheet should carry a keyword cursor, not a texture one: {declaration}");
			}

			string full = Path.GetFullPath(EditorSheetPath);
			LogAssert.IsFalse(RuntimeSheets().ConvertAll(Path.GetFullPath).Contains(full),
				$"editor sheets are out of scope for this rule, but {EditorSheetPath} is inside {GuiRoot}");
		}
	}
}
