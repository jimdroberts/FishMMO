using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs that inviting somebody by name is ONE flow, and that consolidating it did not flatten
	/// what the panels legitimately say differently.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Three panels had an independent copy of the same five steps: find the shared input dialog,
	/// validate the typed name, resolve it to a character ID through the naming system, refuse the
	/// player's own ID, broadcast. Guild and party additionally shared the hover-then-pinned target
	/// fallback ahead of it, comment and all. They differed only in the broadcast sent and two
	/// sentences.
	/// </para>
	/// <para>
	/// Every step of that flow has a way of being wrong that produces NO error where it is written.
	/// Omit the name validation and an unvalidatable string travels to the server to be rejected;
	/// omit the <c>id == 0</c> test and a failed lookup invites character zero; omit the self test
	/// and the player invites themselves and waits for a dialog that never comes. A fourth panel
	/// growing an invite button would be written by copying whichever of the three its author found
	/// first, and a step dropped in the copying looks exactly like working code. That is what this
	/// fixture exists to catch — not the duplication as an aesthetic matter, but the silent omission
	/// duplication invites.
	/// </para>
	/// <para>
	/// The other half of the fixture is the opposite worry. Consolidation is only correct while each
	/// panel still sends ITS OWN broadcast and says its own words — "you can't invite yourself to
	/// the guild" is not "you can't add yourself as a friend", and a helper that unified those would
	/// be a regression dressed as a cleanup. Those are pinned per panel below.
	/// </para>
	/// <para>
	/// Source assertions, deliberately: the flow is a sequence of calls into a shared panel
	/// registry, an async naming cache and the network, so exercising it needs a running client and
	/// a registered dialog. What can be proven cheaply is the CONTRACT — that the shared helper is
	/// what the panels call — which is exactly the thing that regresses.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class InviteByNameHelperTests
	{
		/// <summary>The base class that owns the shared prompt and target helpers.</summary>
		private const string ControlPath = "Assets/Scripts/Client/GUI/UITKControl.cs";

		private const string GuildPath = "Assets/Scripts/Client/GUI/World/Guild/UITKGuild.cs";
		private const string PartyPath = "Assets/Scripts/Client/GUI/World/Party/UITKParty.cs";
		private const string FriendListPath = "Assets/Scripts/Client/GUI/World/FriendList/UITKFriendList.cs";

		/// <summary>Every GUI source file, which is the population the sweeps below run over.</summary>
		private static string GuiRoot =>
			Path.Combine(Directory.GetCurrentDirectory(), "Assets/Scripts/Client/GUI");

		/// <summary>The text of a source file, with its line endings normalised to \n.</summary>
		/// <remarks>
		/// The patterns below span line breaks — a call written across four lines is the normal shape
		/// in this tree. Whether a working tree stores a file LF or CRLF is decided by git on
		/// checkout and by each developer's core.autocrlf, so a bound written with \n silently stops
		/// matching on a Windows checkout and the assertion then reports the code as missing while it
		/// sits there unchanged.
		/// </remarks>
		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path).Replace("\r\n", "\n");
		}

		/// <summary>
		/// A source file with its comments removed.
		/// </summary>
		/// <remarks>
		/// Required rather than tidy. The consolidated call sites EXPLAIN what they no longer do —
		/// they name the naming system and the validation they used to call inline — so a sweep for
		/// those names over raw text would report the explanation as the offence it warns about.
		/// Comments are where this project keeps its reasoning, so a rule that cannot tell code from
		/// prose would push that reasoning out of the files.
		/// <para>
		/// Only WHOLE-LINE line comments are removed, not trailing ones. A trailing <c>//</c> cannot
		/// be told from a <c>//</c> inside a string literal without parsing the file properly, and
		/// cutting at the wrong one takes the rest of a real line of code with it — which would
		/// unbalance the braces <see cref="MethodBody"/> counts and fail on a file that is perfectly
		/// fine. Every comment this needs to see past is a <c>///</c> block on its own lines.
		/// </para>
		/// </remarks>
		private static string StripComments(string source)
		{
			source = Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
			return Regex.Replace(source, @"(?m)^[ \t]*//[^\n]*", string.Empty);
		}

		/// <summary>Every GUI source file except the base class that owns the shared helpers.</summary>
		private static IEnumerable<string> PanelSources()
		{
			foreach (string file in Directory.GetFiles(GuiRoot, "*.cs", SearchOption.AllDirectories))
			{
				if (Path.GetFileName(file) == "UITKControl.cs")
				{
					continue;
				}

				yield return file;
			}
		}

		/// <summary>
		/// The body of a method, from its signature to its matching closing brace.
		/// </summary>
		/// <param name="source">The file's source text.</param>
		/// <param name="signature">A signature fragment that occurs exactly once in the file.</param>
		/// <remarks>
		/// Brace counting rather than a regex: these methods contain string literals, lambdas and
		/// nested initialisers, and a pattern loose enough to match them all is loose enough to run
		/// on into the next method — which for a test that asserts "this method sends THAT
		/// broadcast" would pass by reading a neighbour.
		/// </remarks>
		private static string MethodBody(string source, string signature)
		{
			int start = source.IndexOf(signature, StringComparison.Ordinal);
			LogAssert.IsTrue(start >= 0, $"'{signature}' must exist.");

			int open = source.IndexOf('{', start);
			LogAssert.IsTrue(open >= 0, $"'{signature}' must open a body.");

			int depth = 0;
			for (int i = open; i < source.Length; ++i)
			{
				if (source[i] == '{')
				{
					++depth;
				}
				else if (source[i] == '}')
				{
					if (--depth == 0)
					{
						return source.Substring(start, i - start + 1);
					}
				}
			}

			Assert.Fail($"'{signature}' has no matching closing brace.");
			return null;
		}

		/// <summary>
		/// The shared helpers exist, and the failure wording nobody owns is a constant.
		/// </summary>
		/// <remarks>
		/// The sweeps below are all of the form "no panel may do this itself". Each of them passes
		/// trivially if the shared helper is deleted and nothing replaces it, so the helper's
		/// existence is asserted first: without this test the fixture could go green on a codebase
		/// where the feature had simply been removed.
		/// </remarks>
		[Test]
		public void TheSharedHelpersExist()
		{
			string control = ReadSource(ControlPath);

			LogAssert.IsTrue(control.Contains("protected static bool PromptForCharacterID("),
				"UITKControl must own the prompt-then-resolve-a-name flow.");
			LogAssert.IsTrue(control.Contains("protected static bool TryResolveTargetCharacter("),
				"UITKControl must own the hover-then-pinned target fallback.");
			LogAssert.IsTrue(control.Contains("UnknownCharacterMessage = \"A person with that name could not be found.\""),
				"The not-found wording belongs to one constant — it is not panel-specific.");
		}

		/// <summary>
		/// No panel resolves a typed name into a character ID by itself.
		/// </summary>
		/// <remarks>
		/// <see cref="FishMMO.Client.ClientNamingSystem"/>'s reverse lookup is the giveaway step: a
		/// panel only ever needs a name turned into an ID because the player typed the name, and
		/// that is the flow the shared helper performs end to end. Calling it from a panel means the
		/// four checks around it have been re-decided locally, which is where the silent omissions
		/// described in the fixture remarks come from.
		/// </remarks>
		[Test]
		public void NoPanelResolvesANameItself()
		{
			List<string> offenders = new List<string>();

			foreach (string file in PanelSources())
			{
				string source = StripComments(File.ReadAllText(file).Replace("\r\n", "\n"));

				if (source.Contains("ClientNamingSystem.GetCharacterID("))
				{
					offenders.Add(Path.GetFileName(file));
				}
			}

			LogAssert.IsTrue(offenders.Count == 0,
				"a panel must reach a character ID through UITKControl.PromptForCharacterID, not resolve the name itself: "
				+ string.Join("; ", offenders));
		}

		/// <summary>
		/// No panel writes the not-found sentence itself.
		/// </summary>
		/// <remarks>
		/// Three panels spelled it identically, which is the state a shared string is in immediately
		/// before somebody rewords one of them. The message reports that a name did not resolve — it
		/// has nothing to do with guilds, parties or friends — so a panel that has its own copy of it
		/// is a panel that has its own copy of the lookup.
		/// </remarks>
		[Test]
		public void NoPanelSpellsTheNotFoundMessageItself()
		{
			List<string> offenders = new List<string>();

			foreach (string file in PanelSources())
			{
				string source = StripComments(File.ReadAllText(file).Replace("\r\n", "\n"));

				if (source.Contains("A person with that name could not be found."))
				{
					offenders.Add(Path.GetFileName(file));
				}
			}

			LogAssert.IsTrue(offenders.Count == 0,
				"the not-found wording lives on UITKControl.UnknownCharacterMessage: " + string.Join("; ", offenders));
		}

		/// <summary>
		/// No panel hand-rolls the hover-then-pinned target fallback.
		/// </summary>
		/// <remarks>
		/// The ORDER is the rule: the hovered target is read first and the pin is the fallback,
		/// because the pointer is on the button when it fires and so the hover is usually empty. A
		/// second copy of that ternary is a second place for the order to be swapped, and a swapped
		/// order is not a crash — it is a button that quietly invites the wrong person whenever a pin
		/// is held. Reading <c>PinnedTarget</c> at all is deliberately NOT the offence: the pet
		/// panel sends both the pinned and the hovered target to the server and lets it choose,
		/// which is a different contract.
		/// </remarks>
		[Test]
		public void NoPanelHandRollsTheTargetFallback()
		{
			List<string> offenders = new List<string>();

			foreach (string file in PanelSources())
			{
				string source = StripComments(File.ReadAllText(file).Replace("\r\n", "\n"));

				if (Regex.IsMatch(source, @"Current\.Target\s*!=\s*null[^;]{0,200}PinnedTarget", RegexOptions.Singleline))
				{
					offenders.Add(Path.GetFileName(file));
				}
			}

			LogAssert.IsTrue(offenders.Count == 0,
				"the hovered-then-pinned fallback belongs to UITKControl.TryResolveTargetCharacter: "
				+ string.Join("; ", offenders));
		}

		/// <summary>
		/// Each panel calls the shared helper, and still sends its own broadcast.
		/// </summary>
		/// <remarks>
		/// The behaviour-preserving half. A helper that took over the broadcast as well would have
		/// had to be told which one to send, and the obvious way to tell it — a flag, or a channel
		/// enum — reintroduces the duplication inside the helper while making every panel's wire
		/// message harder to find. The broadcast is the one part of this flow that is genuinely the
		/// panel's, so the test insists it stays there.
		/// </remarks>
		[Test]
		public void EachPanelCallsTheHelperAndSendsItsOwnBroadcast()
		{
			(string path, string signature, string broadcast)[] panels = new[]
			{
				(GuildPath, "public void OnButtonInviteToGuild()", "GuildInviteBroadcast"),
				(PartyPath, "public void OnButtonInviteToParty()", "PartyInviteBroadcast"),
				(FriendListPath, "public void OnButtonAddFriend()", "FriendAddNewBroadcast"),
			};

			foreach ((string path, string signature, string broadcast) panel in panels)
			{
				string body = MethodBody(StripComments(ReadSource(panel.path)), panel.signature);

				LogAssert.IsTrue(body.Contains("PromptForCharacterID("),
					$"{panel.signature} must ask for a name through the shared helper.");
				LogAssert.IsTrue(body.Contains(panel.broadcast),
					$"{panel.signature} must still send {panel.broadcast} itself.");
			}
		}

		/// <summary>
		/// Each panel still refuses the player in its own words.
		/// </summary>
		/// <remarks>
		/// Three distinct sentences, checked as three, because "you can't invite yourself to the
		/// guild" tells the player which action was refused and a single shared "you can't do that to
		/// yourself" would not. Sharing the mechanism is the point of the refactor; sharing the
		/// wording would have been a loss, and this is the test that would have caught it.
		/// </remarks>
		[Test]
		public void EachPanelKeepsItsOwnRefusalWording()
		{
			(string path, string signature, string message)[] panels = new[]
			{
				(GuildPath, "public void OnButtonInviteToGuild()", "You can't invite yourself to the guild."),
				(PartyPath, "public void OnButtonInviteToParty()", "You can't invite yourself to the party."),
				(FriendListPath, "public void OnButtonAddFriend()", "You can't add yourself as a friend."),
			};

			foreach ((string path, string signature, string message) panel in panels)
			{
				string body = MethodBody(StripComments(ReadSource(panel.path)), panel.signature);

				LogAssert.IsTrue(body.Contains(panel.message),
					$"{panel.signature} must pass its own refusal wording: \"{panel.message}\".");
			}
		}

		/// <summary>
		/// The self test survives, wherever it now lives.
		/// </summary>
		/// <remarks>
		/// The one check with no visible consequence when it is missing: inviting yourself sends a
		/// broadcast the server drops, so the button appears to work and no dialog ever arrives. It
		/// was written out three times and is now written once, which is a saving only while the once
		/// is still there.
		/// </remarks>
		[Test]
		public void TheSharedHelperStillRefusesThePlayerThemselves()
		{
			string body = MethodBody(StripComments(ReadSource(ControlPath)),
				"protected static bool PromptForCharacterID(");

			LogAssert.IsTrue(body.Contains("Authentication.IsAllowedCharacterName("),
				"An unvalidatable name must not reach the naming system.");
			LogAssert.IsTrue(body.Contains("id == 0"),
				"An unresolved name must not be broadcast as character zero.");
			LogAssert.IsTrue(body.Contains(".ID == id"),
				"The player's own ID must still be refused.");

			int selfTest = body.IndexOf(".ID == id", StringComparison.Ordinal);
			int invoke = body.IndexOf("onResolved?.Invoke(id)", StringComparison.Ordinal);

			LogAssert.IsTrue(invoke >= 0, "A resolved ID must still reach the caller.");
			LogAssert.IsTrue(selfTest < invoke,
				"The self test decides BEFORE the caller is handed the ID, or the refusal is decoration.");
		}
	}
}
