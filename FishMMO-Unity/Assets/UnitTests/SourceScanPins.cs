using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Source-scan helpers for pins that carry their own control run.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A source scan fails in a way nobody notices: the anchor is reworded, the regex stops matching,
	/// and the test is green for ever while checking nothing. So each pin here is a check function
	/// (source in, failure reason or null out), and <see cref="HoldsAndFires"/> runs it twice — once
	/// on the real source, where it must pass, and once on a copy with the defect it guards against
	/// put back, where it must fail. A mutation that no longer changes the text fails too, which is
	/// the signal to re-anchor.
	/// </para>
	/// <para>
	/// Check functions must not assert: a missing anchor is a returned reason, so the control run can
	/// see it.
	/// </para>
	/// </remarks>
	internal static class SourceScanPins
	{
		/// <summary>A file's text with line endings normalised, relative to the Unity project.</summary>
		public static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path).Replace("\r\n", "\n");
		}

		/// <summary>A file's code with comment lines dropped.</summary>
		public static string ReadCode(string relativePath) => CodeOnly(ReadSource(relativePath));

		/// <summary>The source with comment lines dropped, so a scan sees code and not prose.</summary>
		public static string CodeOnly(string source)
		{
			var code = new StringBuilder(source.Length);
			foreach (string line in source.Split('\n'))
			{
				string trimmed = line.TrimStart();
				if (trimmed.StartsWith("//", StringComparison.Ordinal) ||
					trimmed.StartsWith("/*", StringComparison.Ordinal) ||
					trimmed.StartsWith("*", StringComparison.Ordinal))
				{
					continue;
				}
				code.Append(line).Append('\n');
			}
			return code.ToString();
		}

		/// <summary>
		/// The brace-matched body after the first occurrence of <paramref name="signature"/>, or null
		/// when the signature is gone or the braces do not balance.
		/// </summary>
		public static string Body(string source, string signature)
		{
			if (source == null)
			{
				return null;
			}
			int start = source.IndexOf(signature, StringComparison.Ordinal);
			return start < 0 ? null : Braced(source, source.IndexOf('{', start));
		}

		/// <summary>The brace-matched block opening at <paramref name="open"/>, or null.</summary>
		public static string Braced(string source, int open)
		{
			if (open < 0)
			{
				return null;
			}
			int depth = 0;
			for (int i = open; i < source.Length; ++i)
			{
				if (source[i] == '{')
				{
					++depth;
				}
				else if (source[i] == '}' && --depth == 0)
				{
					return source.Substring(open, i - open + 1);
				}
			}
			return null;
		}

		/// <summary>
		/// Null when the first occurrence of every anchor exists and they appear in the order given;
		/// otherwise why not.
		/// </summary>
		public static string InOrder(string text, params string[] anchors)
		{
			if (text == null)
			{
				return "the enclosing body was not found";
			}
			int previous = -1;
			string previousAnchor = null;
			foreach (string anchor in anchors)
			{
				int at = text.IndexOf(anchor, StringComparison.Ordinal);
				if (at < 0)
				{
					return $"'{anchor}' is missing";
				}
				if (at <= previous)
				{
					return $"'{anchor}' comes before '{previousAnchor}'";
				}
				previous = at;
				previousAnchor = anchor;
			}
			return null;
		}

		/// <summary>
		/// Asserts the check passes on <paramref name="code"/>, and fails on a copy mutated by
		/// <paramref name="mutate"/> — so the pin is shown to fire on the defect it exists for.
		/// </summary>
		public static void HoldsAndFires(string label, string code, Func<string, string> check, Func<string, string> mutate, string defect)
		{
			string failure = check(code);
			LogAssert.IsNull(failure, $"{label}: {failure}");

			string broken = mutate(code);
			LogAssert.IsFalse(broken == null || broken == code,
				$"{label}: the control ({defect}) no longer changes the source. Its anchor has moved; re-anchor the pin.");
			LogAssert.IsNotNull(check(broken),
				$"{label}: the pin stayed green on a copy of the source with the defect put back ({defect}), so it checks nothing.");
		}

		/// <summary>Replaces every occurrence.</summary>
		public static Func<string, string> Replace(string find, string replacement) =>
			s => s.Replace(find, replacement);

		/// <summary>Replaces the first match of a pattern.</summary>
		public static Func<string, string> RegexReplaceFirst(string pattern, string replacement) =>
			s => new Regex(pattern).Replace(s, replacement, 1);

		/// <summary>Inserts text before the first occurrence of an anchor; unchanged when the anchor is gone.</summary>
		public static Func<string, string> InsertBefore(string anchor, string text) =>
			s =>
			{
				int at = s.IndexOf(anchor, StringComparison.Ordinal);
				return at < 0 ? s : s.Insert(at, text);
			};
	}
}
