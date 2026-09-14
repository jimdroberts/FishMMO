using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The client's staff console ships as an empty shell: nothing under its folder names a staff
	/// command.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The decision (made with the project owner) is that every command name, sub-command word,
	/// argument label and help line reaches the console at runtime in the catalogue the server sends
	/// to staff, so a player who unpacks the client finds a shell with nothing in it. This is not the
	/// security boundary — the server's access gate is — but it is a property that erodes silently: a
	/// convenience default, a placeholder, a "quick" hard-coded button, and the command surface is in
	/// every build.
	/// </para>
	/// <para>
	/// Source scans, because what is being prevented is text appearing in files. Comments are
	/// scanned too: a comment is still the command surface written down in the client's tree.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class StaffConsoleSecurityTests
	{
		private const string ConsoleFolder = "Assets/Scripts/Client/GUI/World/StaffConsole";

		/// <summary>Where the server's staff command words live, for the literal scan.</summary>
		private const string ServerFolder = "Assets/Scripts/Server";

		private static readonly string[] ScannedExtensions = { ".cs", ".uxml", ".uss" };

		private static List<string> ConsoleFiles()
		{
			string root = Path.Combine(Directory.GetCurrentDirectory(), ConsoleFolder);
			LogAssert.IsTrue(Directory.Exists(root), $"{ConsoleFolder} must exist");

			List<string> files = new List<string>();
			foreach (string path in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
			{
				if (Array.IndexOf(ScannedExtensions, Path.GetExtension(path).ToLowerInvariant()) >= 0)
				{
					files.Add(path);
				}
			}

			// Non-vacuous: the scan must actually be reading the console.
			LogAssert.IsTrue(files.Exists(f => f.EndsWith("UITKStaffConsole.cs", StringComparison.Ordinal)), "the panel class is scanned");
			LogAssert.IsTrue(files.Exists(f => f.EndsWith("UIStaffConsole.uxml", StringComparison.Ordinal)), "the markup is scanned");
			LogAssert.IsTrue(files.Exists(f => f.EndsWith("UIStaffConsole.uss", StringComparison.Ordinal)), "the stylesheet is scanned");
			return files;
		}

		private static string Read(string path) => File.ReadAllText(path).Replace("\r\n", "\n");

		private static string Relative(string path) => path.Substring(Directory.GetCurrentDirectory().Length + 1);

		[Test]
		public void NoConsoleFile_NamesTheStaffSlashCommands()
		{
			foreach (string path in ConsoleFiles())
			{
				string text = Read(path);
				foreach (string forbidden in new[] { "/gm", "/admin" })
				{
					int at = text.IndexOf(forbidden, StringComparison.OrdinalIgnoreCase);
					LogAssert.IsTrue(at < 0,
						$"{Relative(path)} contains '{forbidden}' at offset {at}. The console must learn every command from the catalogue.");
				}
			}
		}

		[Test]
		public void NoConsoleFile_HardCodesAnySlashCommand()
		{
			/* A string literal (or markup attribute) that begins with a slash and a letter is a chat
			 * command written into the client. Paths in markup begin with a dot, so they pass. */
			Regex slashLiteral = new Regex("\"\\s*/[A-Za-z]", RegexOptions.CultureInvariant);

			foreach (string path in ConsoleFiles())
			{
				Match match = slashLiteral.Match(Read(path));
				LogAssert.IsFalse(match.Success,
					$"{Relative(path)} contains a slash-command literal near offset {(match.Success ? match.Index : -1)}.");
			}
		}

		[Test]
		public void NoConsoleFile_QuotesAServerStaffSubCommandWord()
		{
			/* The words are read from the server's own command handling rather than listed here, so
			 * this file does not become a copy of the surface it guards. Sub-command words are
			 * lower-case tokens; a capitalised display word such as a column heading is not one. */
			HashSet<string> words = new HashSet<string>(StringComparer.Ordinal);

			/* Both shapes the server has used: a switch over the sub-command word, and the command
			 * table (Name = "word", Aliases = new[] { "word", ... }). */
			Regex caseWord = new Regex("case\\s+\"([a-z][a-z0-9_]*)\"\\s*:", RegexOptions.CultureInvariant);
			Regex tableName = new Regex("\\bName\\s*=\\s*\"([a-z][a-z0-9_]*)\"", RegexOptions.CultureInvariant);
			Regex tableAliases = new Regex("\\bAliases\\s*=\\s*new(?:\\s*string)?\\s*\\[\\s*\\]\\s*\\{([^}]*)\\}", RegexOptions.CultureInvariant);
			Regex quotedWord = new Regex("\"([a-z][a-z0-9_]*)\"", RegexOptions.CultureInvariant);

			string serverRoot = Path.Combine(Directory.GetCurrentDirectory(), ServerFolder);
			if (Directory.Exists(serverRoot))
			{
				foreach (string path in Directory.GetFiles(serverRoot, "*.cs", SearchOption.AllDirectories))
				{
					string name = Path.GetFileName(path);
					if (name.IndexOf("Command", StringComparison.OrdinalIgnoreCase) < 0 &&
						name.IndexOf("Staff", StringComparison.OrdinalIgnoreCase) < 0)
					{
						continue;
					}
					string source = File.ReadAllText(path);
					foreach (Match match in caseWord.Matches(source))
					{
						words.Add(match.Groups[1].Value);
					}
					foreach (Match match in tableName.Matches(source))
					{
						words.Add(match.Groups[1].Value);
					}
					foreach (Match aliases in tableAliases.Matches(source))
					{
						foreach (Match alias in quotedWord.Matches(aliases.Groups[1].Value))
						{
							words.Add(alias.Groups[1].Value);
						}
					}
				}
			}

			/* The view ids are part of the wire contract (StaffConsoleCatalogBroadcast.Views lists
			 * "players" and "tickets"), so the console has to know them; one of them happens to share
			 * its spelling with a sub-command. They name data views, not commands. */
			words.Remove("players");
			words.Remove("tickets");

			if (words.Count == 0)
			{
				Assert.Inconclusive("No staff sub-command words could be read from the server's command sources; " +
					"the literal scan has nothing to check. The slash-command scans above still apply.");
			}

			foreach (string path in ConsoleFiles())
			{
				string text = Read(path);
				foreach (string word in words)
				{
					int at = text.IndexOf("\"" + word + "\"", StringComparison.Ordinal);
					LogAssert.IsTrue(at < 0,
						$"{Relative(path)} quotes the staff sub-command word \"{word}\" at offset {at}.");
				}
			}
		}
	}
}
