#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace FishMMO.GeneratedFiles
{
	/// <summary>
	/// Compares the values a *.generated.cs file assigns against the values its
	/// tracked template assigns, and reports anything the template has that the
	/// generated file does not.
	///
	/// Why this exists: <see cref="GeneratedFileRestorer"/> never overwrites an
	/// existing generated file — it would clobber real hosts, pins, and the gate
	/// secret — so a checkout that already has one keeps whatever shape it had when
	/// it was written. A generated file only overrides the sentinel defaults its
	/// tracked class declares (HostConfig.cs, CertificatePinSet.cs, ClientGateSecret.cs),
	/// so an old file no longer breaks the compile — but a value it does not assign
	/// silently stays at its sentinel, and a file from before the partial-class split
	/// declares a class nothing reads, so every value in it is ignored. Both are
	/// reported here, by name, instead of surfacing as a release build the validator
	/// refuses or a client that cannot reach its hosts. The compile-error form of the
	/// same drift is how https://github.com/jimdroberts/FishMMO/issues/122 reached dev.
	///
	/// Deliberately text-based, so it reports on the file as written rather than on
	/// whatever the last successful compile happened to contain.
	/// </summary>
	internal static class GeneratedFileDriftCheck
	{
		/// <summary>
		/// A value assigned inside the generated Configure method, e.g.
		/// <c>values.ApiHost =</c>. The <c>(?!=)</c> keeps a comparison from
		/// counting as an assignment.
		/// </summary>
		private static readonly Regex MemberPattern = new Regex(
			@"^\s*values\.(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=(?!=)",
			RegexOptions.Multiline);

		/// <summary>A public type declaration, e.g. <c>public static partial class CertificatePinSet</c>.</summary>
		private static readonly Regex TypePattern = new Regex(
			@"^\s*public\s+(?:(?:static|sealed|abstract|partial)\s+)*(?:class|struct|enum|interface)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)",
			RegexOptions.Multiline);

		/// <summary>
		/// Comment lines directly above an assignment are part of it, so the
		/// message can quote something that is ready to paste.
		/// </summary>
		private const string DocCommentPrefix = "//";

		/// <summary>
		/// Upper bound on how many lines one quoted declaration may span. A template
		/// field is a handful of lines; anything longer means the scan lost its place,
		/// and quoting half the file would bury the actual message.
		/// </summary>
		private const int MaxDeclarationLines = 64;

		/// <summary>
		/// Checks one generated file against its template.
		/// </summary>
		/// <returns>
		/// A ready-to-log description of the drift, or null when the generated file
		/// declares the template's type and assigns every value the template does.
		/// </returns>
		public static string Describe(
			string relativePath,
			string templateRelativePath,
			string templateSource,
			string generatedSource)
		{
			List<string> missingTypes = MissingNames(TypePattern, templateSource, generatedSource);
			List<string> missingMembers = MissingNames(MemberPattern, templateSource, generatedSource);

			if (missingTypes.Count == 0 && missingMembers.Count == 0)
				return null;

			// Only the template-has-it/file-does-not direction is reported. The reverse
			// (a generated file assigning a value the template dropped) is a compile
			// error the compiler already names, so repeating it here would be noise.
			var message = new StringBuilder();
			message.Append("[FishMMO] '").Append(relativePath).Append("' is out of date. ");

			if (missingTypes.Count > 0)
			{
				// No partial class means the file predates the split, so it declares a
				// class of its own that nothing reads: every value in it is ignored and
				// the per-member list would just be all of them.
				message.Append("It does not declare ").Append(Join(missingTypes)).AppendLine(
					", so it predates the tracked defaults and nothing reads the values it holds — " +
					"the sentinel defaults are in effect, and release builds will be refused.");
				message.AppendLine();
				message.AppendLine(
					"Open FishMMO Dashboard > Game Settings: it still reads the old file, so its fields " +
					"show your current values. Click that section's Write button to rewrite the file in " +
					"the current shape. Or delete the file and reopen the project to restore it from '" +
					templateRelativePath + "', which discards the values it holds.");
				return message.ToString();
			}

			bool single = missingMembers.Count == 1;
			message.Append("It does not assign ").Append(missingMembers.Count).Append(single ? " value" : " values")
				.AppendLine(" that its template assigns:");
			foreach (string name in missingMembers)
				message.Append("  ").AppendLine(name);

			message.AppendLine();
			message.AppendLine(
				(single ? "That value stays" : "Those values stay") + " at the tracked default — a sentinel " +
				"for hosts, pins, and the secret, which release builds refuse.");
			message.AppendLine(
				"The file is not overwritten automatically, because it holds your real hosts, pins, " +
				"and gate secret. Either paste the line" + (single ? "" : "s") + " below into its Configure " +
				"method from '" + templateRelativePath + "' and set the real value, rewrite it from FishMMO " +
				"Dashboard > Game Settings, or delete the file and reopen the project to restore it from " +
				"the template — which discards the real values it currently holds.");

			foreach (string name in missingMembers)
			{
				string declaration = ExtractDeclaration(templateSource, name);
				if (declaration == null)
					continue;

				message.AppendLine();
				message.Append(declaration);
			}

			return message.ToString();
		}

		/// <summary>
		/// Names matched by <paramref name="pattern"/> in the template that the
		/// generated file does not declare, in the order the template declares them.
		/// </summary>
		private static List<string> MissingNames(Regex pattern, string templateSource, string generatedSource)
		{
			var declared = new HashSet<string>();
			foreach (Match match in pattern.Matches(generatedSource))
				declared.Add(match.Groups["name"].Value);

			var missing = new List<string>();
			foreach (Match match in pattern.Matches(templateSource))
			{
				string name = match.Groups["name"].Value;
				if (!declared.Contains(name) && !missing.Contains(name))
					missing.Add(name);
			}

			return missing;
		}

		/// <summary>
		/// The template's own text for a member — its doc comment, its declaration, and
		/// any continuation lines up to the closing semicolon — so the log quotes
		/// something that can be pasted as-is.
		/// </summary>
		/// <returns>The declaration text, or null if it could not be delimited.</returns>
		private static string ExtractDeclaration(string templateSource, string name)
		{
			string[] lines = templateSource.Replace("\r\n", "\n").Split('\n');

			int declaration = -1;
			for (int i = 0; i < lines.Length; i++)
			{
				Match match = MemberPattern.Match(lines[i]);
				if (match.Success && match.Groups["name"].Value == name)
				{
					declaration = i;
					break;
				}
			}

			if (declaration < 0)
				return null;

			int first = declaration;
			while (first > 0 && lines[first - 1].TrimStart().StartsWith(DocCommentPrefix))
				first--;

			// A field may carry its value on later lines ("public const string ApiHost ="
			// then the string) or span a collection initialiser, so run to the semicolon
			// that ends the statement rather than assuming one line.
			int last = -1;
			for (int i = declaration; i < lines.Length && i - declaration < MaxDeclarationLines; i++)
			{
				if (lines[i].TrimEnd().EndsWith(";"))
				{
					last = i;
					break;
				}
			}

			if (last < 0)
				return null;

			var declarationText = new StringBuilder();
			for (int i = first; i <= last; i++)
				declarationText.AppendLine(lines[i]);

			return declarationText.ToString();
		}

		/// <summary>"HostConfig" / "A and B".</summary>
		private static string Join(List<string> names)
		{
			if (names.Count == 1)
				return names[0];

			return string.Join(", ", names.GetRange(0, names.Count - 1).ToArray()) + " and " + names[names.Count - 1];
		}
	}
}
#endif
