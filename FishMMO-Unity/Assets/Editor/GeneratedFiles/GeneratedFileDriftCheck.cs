#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace FishMMO.GeneratedFiles
{
	/// <summary>
	/// Compares the public API a *.generated.cs file declares against the API its
	/// tracked template declares, and reports anything the template has that the
	/// generated file does not.
	///
	/// Why this exists: <see cref="GeneratedFileRestorer"/> never overwrites an
	/// existing generated file — it would clobber real hosts, pins, and the gate
	/// secret — so a checkout that already has one keeps whatever shape it had when
	/// it was written. Adding a field to the template and to the dashboard writer
	/// therefore only fixes fresh clones: every existing checkout still holds the
	/// old file, and the first sign of trouble is an unrelated-looking CS0117 in a
	/// consuming assembly. That is exactly how
	/// https://github.com/jimdroberts/FishMMO/issues/122 reached dev — the field was
	/// added to the API and missed in both writers, and the build kept working for
	/// whoever added it.
	///
	/// Deliberately text-based: the generated files compile into FishMMO.Shared and
	/// FishMMO.Client, and this check has to work precisely when those assemblies
	/// are the ones failing, so it cannot reflect over the types it is checking.
	/// </summary>
	internal static class GeneratedFileDriftCheck
	{
		/// <summary>
		/// A public constant or static readonly field, e.g.
		/// <c>public const string ApiHost =</c> or
		/// <c>public static readonly string[] Pins =</c>. The trailing <c>=</c> is
		/// required so method and property declarations are not mistaken for fields.
		/// </summary>
		private static readonly Regex MemberPattern = new Regex(
			@"^\s*public\s+(?:const|static\s+readonly|readonly\s+static)\s+[^\s=;]+\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=",
			RegexOptions.Multiline);

		/// <summary>A public type declaration, e.g. <c>public static class GeneratedPinSet</c>.</summary>
		private static readonly Regex TypePattern = new Regex(
			@"^\s*public\s+(?:(?:static|sealed|abstract|partial)\s+)*(?:class|struct|enum|interface)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)",
			RegexOptions.Multiline);

		/// <summary>
		/// Doc comment lines directly above a declaration are part of it, so the
		/// message can quote something that is ready to paste.
		/// </summary>
		private const string DocCommentPrefix = "///";

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
		/// declares everything the template does.
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
			// (a generated file with extra members) compiles fine and is what a machine
			// mid-rollback looks like, so flagging it would be noise.
			var message = new StringBuilder();
			message.Append("[FishMMO] '").Append(relativePath).Append("' is out of date. It does not declare ");
			message.Append(Describe(missingTypes, "type")).Append(Join(missingTypes, missingMembers));
			message.Append(Describe(missingMembers, "member")).AppendLine(" that its template declares:");

			foreach (string name in missingTypes)
				message.Append("  ").AppendLine(name);
			foreach (string name in missingMembers)
				message.Append("  ").AppendLine(name);

			bool single = missingTypes.Count + missingMembers.Count == 1;
			message.AppendLine();
			message.AppendLine(
				"Anything that uses " + (single ? "it" : "them") + " will not compile — a missing member is " +
				"the CS0117 you would otherwise meet in an assembly that looks unrelated.");
			message.AppendLine(
				"The file is not overwritten automatically, because it holds your real hosts, pins, " +
				"and gate secret. Either paste the declaration" + (single ? "" : "s") + " below into it from '" +
				templateRelativePath + "', or delete the file and reopen the project to restore it from " +
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

		/// <summary>"1 type" / "2 members", or empty when nothing of that kind is missing.</summary>
		private static string Describe(List<string> names, string noun)
		{
			if (names.Count == 0)
				return "";

			return names.Count + " " + noun + (names.Count == 1 ? "" : "s");
		}

		private static string Join(List<string> first, List<string> second)
		{
			return first.Count > 0 && second.Count > 0 ? " and " : "";
		}
	}
}
#endif
