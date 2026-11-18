using System.Text;

namespace FishMMO.Shared.NameGeneration
{
	/// <summary>
	/// Shared string-sanitization helpers used by the name generator and
	/// its editor tooling.
	/// </summary>
	public static class StringUtility
	{
		/// <summary>
		/// URL / id slug: lowercase letters and digits, non-alphanumerics
		/// collapsed to a single <c>-</c>. Empty input returns <c>fallback</c>.
		/// Also strips template metacharacters (<c>{</c>, <c>}</c>) so a
		/// malicious or mis-named source file cannot inject a lore slot.
		/// </summary>
		public static string Slug(string s, string fallback = "unknown")
		{
			if (string.IsNullOrEmpty(s)) return fallback;
			var sb = new StringBuilder(s.Length);
			foreach (var c in s)
			{
				if (c == '{' || c == '}') continue;
				if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
				else if (sb.Length > 0 && sb[sb.Length - 1] != '-') sb.Append('-');
			}
			string result = sb.ToString().Trim('-');
			return string.IsNullOrEmpty(result) ? fallback : result;
		}

		/// <summary>
		/// Asset-filename-safe sanitizer: keeps letters, digits, underscore,
		/// and hyphen; lowercases the rest. Non-matching characters become
		/// underscore. Empty input returns <c>fallback</c>.
		/// </summary>
		public static string SanitizeFileName(string s, string fallback = "unnamed")
		{
			if (string.IsNullOrEmpty(s)) return fallback;
			var sb = new StringBuilder(s.Length);
			foreach (var c in s)
			{
				if (c == '{' || c == '}') continue;
				if (char.IsLetterOrDigit(c) || c == '_' || c == '-') sb.Append(c);
				else sb.Append('_');
			}
			string result = sb.ToString().ToLowerInvariant();
			return string.IsNullOrEmpty(result) ? fallback : result;
		}

		/// <summary>
		/// Strips template metacharacters <c>{</c> and <c>}</c> from a string.
		/// Use when a value flows into a template expansion path to prevent
		/// injection of unresolved slot tokens.
		/// </summary>
		public static string StripTemplateChars(string s)
		{
			if (string.IsNullOrEmpty(s)) return s;
			if (s.IndexOf('{') < 0 && s.IndexOf('}') < 0) return s;
			var sb = new StringBuilder(s.Length);
			foreach (var c in s)
				if (c != '{' && c != '}') sb.Append(c);
			return sb.ToString();
		}
	}
}
