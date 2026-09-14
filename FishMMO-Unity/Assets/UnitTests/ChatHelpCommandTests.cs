using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Auth.Core;
using FishMMO.Server.Implementation.World.SceneServer;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// <c>/help</c> lists only what the caller may run.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Access-level guarded.</b> The owner's rule: players do not see game master or
	/// administrator commands, and game masters do not see administrator commands. The listing is
	/// built from the caller's server-loaded level with the gate's own test, so these fixtures
	/// hold the output to it: a level's lines are exactly the lines of a registry with everything
	/// above that level removed, name no command the level cannot run, and name no sub-command of
	/// a set it cannot open.
	/// </para>
	/// <para>
	/// <b>Against the real registrations.</b> The registry is read from the source — every
	/// <c>ChatHelper.AddCommands</c> table with its level, every <c>ChatHelper.SetCommandHelp</c>
	/// with its text — because the systems that register them cannot be stood up in EditMode. The
	/// same parse pins that every registered word is described, so a command added later cannot be
	/// listed as a bare word by accident.
	/// </para>
	/// <para>
	/// <b>No oracle.</b> <c>/help gm</c> as a player answers exactly as <c>/help doesnotexist</c>.
	/// </para>
	/// <para>
	/// Each check is a function returning a failure reason, and each is run once against output it
	/// must reject, so a check that has stopped checking fails.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class ChatHelpCommandTests
	{
		private const string ScriptsDirectory = "Assets/Scripts";
		private const string ChatDirectory = "Assets/Scripts/Server/Implementation/World/SceneServer/Chat";
		private const string ChatSystemPath = ChatDirectory + "/ChatSystem.cs";
		private const string HelpPath = ChatDirectory + "/ChatSystem.Help.cs";

		private static readonly AccessLevel[] Levels = { AccessLevel.Player, AccessLevel.GameMaster, AccessLevel.Admin };

		// ── The registry, read from the source ────────────────────────────────

		private sealed class Registry
		{
			public readonly Dictionary<string, ChatCommandRegistration> Commands =
				new Dictionary<string, ChatCommandRegistration>(StringComparer.OrdinalIgnoreCase);
			public readonly Dictionary<string, ChatCommandHelp> Help =
				new Dictionary<string, ChatCommandHelp>(StringComparer.OrdinalIgnoreCase);

			/// <summary>A copy holding only the commands a caller at <paramref name="level"/> may run.</summary>
			public Registry UpTo(AccessLevel level)
			{
				var copy = new Registry();
				foreach (var pair in Commands.Where(p => p.Value.MinimumAccessLevel <= level))
				{
					copy.Commands[pair.Key] = pair.Value;
				}
				foreach (var pair in Help)
				{
					copy.Help[pair.Key] = pair.Value;
				}
				return copy;
			}

			public IReadOnlyList<string> Build(AccessLevel caller, string topic)
			{
				return ChatCommandHelpListing.Build(caller, topic, Commands, Help, ChatHelper.ChannelCommandMap, ChatHelper.ChannelCommandHelp);
			}
		}

		/// <summary>Every script that registers a slash command, code only, concatenated.</summary>
		private static string RegistrationCode()
		{
			string root = Path.Combine(Directory.GetCurrentDirectory(), ScriptsDirectory);
			var files = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
				.OrderBy(f => f, StringComparer.Ordinal)
				.Select(f => SourceScanPins.CodeOnly(File.ReadAllText(f).Replace("\r\n", "\n")))
				.Where(code => code.Contains("ChatHelper.AddCommands(") || code.Contains("ChatHelper.SetCommandHelp("))
				.ToList();
			LogAssert.IsTrue(files.Count >= 8, $"expected the registering systems under {ScriptsDirectory}, found {files.Count}");
			return string.Join("\n", files);
		}

		/// <summary>Registered words and their levels, from every <c>ChatHelper.AddCommands</c> table.</summary>
		private static Dictionary<string, AccessLevel> RegisteredWords(string code, out string failure)
		{
			failure = null;
			var words = new Dictionary<string, AccessLevel>(StringComparer.OrdinalIgnoreCase);
			foreach (Match add in Regex.Matches(code, @"ChatHelper\.AddCommands\("))
			{
				int open = code.IndexOf('{', add.Index);
				string table = SourceScanPins.Braced(code, open);
				int close = table == null ? -1 : code.IndexOf(");", open + table.Length, StringComparison.Ordinal);
				if (close < 0)
				{
					failure = "an AddCommands table does not parse";
					return words;
				}

				string after = code.Substring(open + table.Length, close - open - table.Length).Trim();
				AccessLevel level = AccessLevel.Player;
				if (after.Length > 0)
				{
					Match named = Regex.Match(after, @"^,\s*(?:[\w\.]+\.)?AccessLevel\.(\w+)$");
					if (!named.Success || !Enum.TryParse(named.Groups[1].Value, out level))
					{
						failure = $"an AddCommands level does not parse: '{after}'";
						return words;
					}
				}

				foreach (Match entry in Regex.Matches(table, @"\{\s*""(/[A-Za-z]+)""\s*,"))
				{
					words[entry.Groups[1].Value] = level;
				}
			}
			return words;
		}

		/// <summary>Help entries from every <c>ChatHelper.SetCommandHelp</c> call.</summary>
		private static Dictionary<string, ChatCommandHelp> DescribedCommands(string code, out string failure)
		{
			failure = null;
			var help = new Dictionary<string, ChatCommandHelp>(StringComparer.OrdinalIgnoreCase);
			foreach (Match call in Regex.Matches(code, @"ChatHelper\.SetCommandHelp\(""(/[A-Za-z]+)"",\s*new ChatCommandHelp\(\)"))
			{
				string body = SourceScanPins.Braced(code, code.IndexOf('{', call.Index));
				if (body == null)
				{
					failure = $"the help for {call.Groups[1].Value} does not parse";
					return help;
				}
				string Field(string name)
				{
					Match m = Regex.Match(body, name + @"\s*=\s*""([^""]*)""");
					return m.Success ? m.Groups[1].Value : null;
				}
				Match aliases = Regex.Match(body, @"Aliases\s*=\s*new\[\]\s*\{([^}]*)\}");
				help[call.Groups[1].Value] = new ChatCommandHelp()
				{
					Category = Field("Category"),
					Arguments = Field("Arguments") ?? string.Empty,
					Summary = Field("Summary"),
					Aliases = aliases.Success
						? Regex.Matches(aliases.Groups[1].Value, @"""(/[A-Za-z]+)""").Cast<Match>().Select(m => m.Groups[1].Value).ToArray()
						: Array.Empty<string>(),
				};
			}
			return help;
		}

		/// <summary>Null when every registered word is described, and every description names registered words at one level.</summary>
		private static string UndescribedFailure(string code)
		{
			Dictionary<string, AccessLevel> registered = RegisteredWords(code, out string failure);
			if (failure != null)
			{
				return failure;
			}
			Dictionary<string, ChatCommandHelp> help = DescribedCommands(code, out failure);
			if (failure != null)
			{
				return failure;
			}

			var described = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var pair in help)
			{
				if (string.IsNullOrWhiteSpace(pair.Value.Summary) || string.IsNullOrWhiteSpace(pair.Value.Category))
				{
					return $"{pair.Key} is described without a category or a summary";
				}
				if (!registered.TryGetValue(pair.Key, out AccessLevel level))
				{
					return $"{pair.Key} is described but never registered";
				}
				described.Add(pair.Key);
				foreach (string alias in pair.Value.Aliases)
				{
					if (!registered.TryGetValue(alias, out AccessLevel aliasLevel) || aliasLevel != level)
					{
						return $"{alias}, an alias of {pair.Key}, is not registered at {pair.Key}'s level";
					}
					described.Add(alias);
				}
			}

			string[] missing = registered.Keys.Where(w => !described.Contains(w)).OrderBy(w => w, StringComparer.Ordinal).ToArray();
			return missing.Length == 0 ? null : $"registered but not described: {string.Join(", ", missing)}";
		}

		private static Registry ReadRegistry()
		{
			string code = RegistrationCode();
			Dictionary<string, AccessLevel> registered = RegisteredWords(code, out string failure);
			LogAssert.IsNull(failure, failure);
			Dictionary<string, ChatCommandHelp> help = DescribedCommands(code, out failure);
			LogAssert.IsNull(failure, failure);

			// The parse is what everything below stands on; hold it to the registrations that must be there.
			LogAssert.IsTrue(registered.TryGetValue("/help", out AccessLevel helpLevel) && helpLevel == AccessLevel.Player,
				"/help must be registered at Player: it is not an elevated command and is not audited");
			LogAssert.IsTrue(registered.TryGetValue("/gm", out AccessLevel gmLevel) && gmLevel == AccessLevel.GameMaster, "/gm must parse at GameMaster");
			LogAssert.IsTrue(registered.TryGetValue("/admin", out AccessLevel adminLevel) && adminLevel == AccessLevel.Admin, "/admin must parse at Admin");
			LogAssert.IsTrue(registered.Keys.Contains("/unstuck") && registered.Keys.Contains("/report") && registered.Keys.Contains("/spectatearena"),
				"the parse must find the character, support and arena registrations");

			var registry = new Registry();
			foreach (var pair in registered)
			{
				registry.Commands[pair.Key] = new ChatCommandRegistration() { MinimumAccessLevel = pair.Value };
			}
			foreach (var pair in help)
			{
				registry.Help[pair.Key] = pair.Value;
			}
			return registry;
		}

		// ── Operator sub-command names, from their tables ─────────────────────

		private static HashSet<string> SubCommandWords(string buildMethod)
		{
			SceneServerSystem system = ScriptableObject.CreateInstance<SceneServerSystem>();
			try
			{
				MethodInfo build = typeof(SceneServerSystem).GetMethod(buildMethod, BindingFlags.Instance | BindingFlags.NonPublic);
				LogAssert.IsNotNull(build, $"SceneServerSystem.{buildMethod} must exist");
				var words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				foreach (object command in (IEnumerable)build.Invoke(system, null))
				{
					Type type = command.GetType();
					words.Add((string)type.GetField("Name").GetValue(command));
					foreach (string alias in (string[])type.GetField("Aliases").GetValue(command) ?? Array.Empty<string>())
					{
						words.Add(alias);
					}
				}
				LogAssert.IsTrue(words.Count > 5, $"{buildMethod} must return its table");
				return words;
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(system);
			}
		}

		// ── The checks ────────────────────────────────────────────────────────

		/// <summary>Null when <paramref name="lines"/> show a caller at <paramref name="level"/> nothing above it; otherwise why not.</summary>
		private static string LeakFailure(Registry registry, IReadOnlyList<string> lines, AccessLevel level)
		{
			IReadOnlyList<string> expected = registry.UpTo(level).Build(level, string.Empty);
			if (!lines.SequenceEqual(expected))
			{
				return $"the lines differ from a registry holding only what {level} may run";
			}

			string text = string.Join("\n", lines);
			var usable = new HashSet<string>(
				registry.Commands.Where(p => ChatCommandHelpListing.CanUse(level, p.Value.MinimumAccessLevel)).Select(p => p.Key)
					.Concat(ChatHelper.ChannelCommandMap.Values.SelectMany(v => v)),
				StringComparer.OrdinalIgnoreCase);
			foreach (Match token in Regex.Matches(text, @"(?<![\w<])/[A-Za-z]+"))
			{
				if (!usable.Contains(token.Value))
				{
					return $"{level} is shown {token.Value}, which it cannot run";
				}
			}

			if (level < AccessLevel.GameMaster && Regex.IsMatch(text, @"\bgm\b|game master", RegexOptions.IgnoreCase))
			{
				return $"{level} is shown a game master command";
			}
			if (level < AccessLevel.Admin && Regex.IsMatch(text, @"\badmin\b|administrator", RegexOptions.IgnoreCase))
			{
				return $"{level} is shown an administrator command";
			}

			var hidden = new List<(string Set, HashSet<string> Words)>();
			if (level < AccessLevel.GameMaster)
			{
				hidden.Add(("gm", SubCommandWords("BuildGameMasterCommands")));
			}
			if (level < AccessLevel.Admin)
			{
				hidden.Add(("admin", SubCommandWords("BuildAdminCommands")));
			}
			foreach (var (set, words) in hidden)
			{
				foreach (string word in words)
				{
					if (Regex.IsMatch(text, $@"\b{set}\s+{Regex.Escape(word)}\b", RegexOptions.IgnoreCase))
					{
						return $"{level} is shown /{set} {word}";
					}
				}
			}
			return null;
		}

		// ── Tests ─────────────────────────────────────────────────────────────

		[Test]
		public void EveryRegisteredCommandIsDescribed()
		{
			string code = RegistrationCode();
			SourceScanPins.HoldsAndFires("chat command registrations", code, UndescribedFailure,
				SourceScanPins.Replace("Aliases = new[] { \"/stuck\" },", string.Empty),
				"/stuck is registered with no description");
			SourceScanPins.HoldsAndFires("chat command registrations", code, UndescribedFailure,
				SourceScanPins.Replace("Summary = \"Game master commands. /gm help lists them.\",", string.Empty),
				"/gm is described without a summary");
		}

		[Test]
		public void APlayerSeesNoGameMasterOrAdministratorCommand()
		{
			Registry registry = ReadRegistry();
			IReadOnlyList<string> lines = registry.Build(AccessLevel.Player, string.Empty);
			LogAssert.IsTrue(lines.Count > 1, "a player must be shown commands");
			string failure = LeakFailure(registry, lines, AccessLevel.Player);
			LogAssert.IsNull(failure, failure);

			// Control: a game master's listing, claimed as a player's, must be rejected.
			LogAssert.IsNotNull(LeakFailure(registry, registry.Build(AccessLevel.GameMaster, string.Empty), AccessLevel.Player),
				"the leak check must reject a game master's lines shown to a player");
		}

		[Test]
		public void AGameMasterSeesGmButNoAdministratorCommand()
		{
			Registry registry = ReadRegistry();
			IReadOnlyList<string> lines = registry.Build(AccessLevel.GameMaster, string.Empty);
			string text = string.Join("\n", lines);
			StringAssert.Contains("/gm <command>", text);
			StringAssert.Contains("/spectatearena", text);
			string failure = LeakFailure(registry, lines, AccessLevel.GameMaster);
			LogAssert.IsNull(failure, failure);

			// Control: an administrator's listing, claimed as a game master's, must be rejected.
			LogAssert.IsNotNull(LeakFailure(registry, registry.Build(AccessLevel.Admin, string.Empty), AccessLevel.GameMaster),
				"the leak check must reject an administrator's lines shown to a game master");
		}

		[Test]
		public void AnAdministratorSeesEverything()
		{
			Registry registry = ReadRegistry();
			string text = string.Join("\n", registry.Build(AccessLevel.Admin, string.Empty));
			foreach (string word in registry.Commands.Keys)
			{
				LogAssert.IsTrue(Regex.IsMatch(text, $@"(?<![\w<]){Regex.Escape(word)}(?![A-Za-z])"), $"an administrator must be shown {word}");
			}
			foreach (string word in ChatHelper.ChannelCommandMap.Values.SelectMany(v => v))
			{
				LogAssert.IsTrue(Regex.IsMatch(text, $@"(?<![\w<]){Regex.Escape(word)}(?![A-Za-z])"), $"an administrator must be shown {word}");
			}
			LogAssert.IsNull(LeakFailure(registry, registry.Build(AccessLevel.Admin, string.Empty), AccessLevel.Admin));
		}

		[Test]
		public void EveryLineFitsTheChatLimit()
		{
			Registry registry = ReadRegistry();
			foreach (AccessLevel level in Levels)
			{
				var topics = new List<string>() { string.Empty, "doesnotexist" };
				topics.AddRange(registry.Commands.Keys);
				topics.AddRange(ChatHelper.ChannelCommandMap.Values.SelectMany(v => v));
				foreach (string topic in topics)
				{
					foreach (string line in registry.Build(level, topic))
					{
						LogAssert.IsTrue(line.Length <= ChatCommandHelpListing.LineLength && line.Length <= ChatBroadcast.MaxTextLength,
							$"/help {topic} as {level}: {line.Length} characters, over the limit: {line}");
					}
				}

				// The sample, for whoever reads the results.
				TestContext.Out.WriteLine($"--- /help as {level} ---");
				foreach (string line in registry.Build(level, string.Empty))
				{
					TestContext.Out.WriteLine(line);
				}
			}

			// A description far over the limit is cut, not sent whole for the client to discard.
			var oversized = new Registry();
			oversized.Commands["/long"] = new ChatCommandRegistration() { MinimumAccessLevel = AccessLevel.Player };
			oversized.Help["/long"] = new ChatCommandHelp() { Category = new string('c', 200), Arguments = new string('a', 200), Summary = new string('s', 300) };
			foreach (string topic in new[] { string.Empty, "long" })
			{
				foreach (string line in oversized.Build(AccessLevel.Player, topic))
				{
					LogAssert.IsTrue(line.Length <= ChatCommandHelpListing.LineLength, $"an oversized description produced {line.Length} characters");
				}
			}
		}

		[Test]
		public void AskingAboutACommandAboveYouAnswersAsIfItDidNotExist()
		{
			Registry registry = ReadRegistry();
			IReadOnlyList<string> unknownAsPlayer = registry.Build(AccessLevel.Player, "doesnotexist");
			LogAssert.IsTrue(unknownAsPlayer.SequenceEqual(new[] { ChatCommandHelpListing.UnknownCommand }), "an unknown topic must answer UnknownCommand");
			LogAssert.IsTrue(registry.Build(AccessLevel.Player, "gm").SequenceEqual(unknownAsPlayer), "/help gm as a player must answer as /help doesnotexist");

			foreach (AccessLevel level in Levels)
			{
				IReadOnlyList<string> unknown = registry.Build(level, "doesnotexist");
				var probes = registry.Commands.Where(p => !ChatCommandHelpListing.CanUse(level, p.Value.MinimumAccessLevel))
					.SelectMany(p => new[] { p.Key, p.Key.TrimStart('/') });
				if (level < AccessLevel.Admin)
				{
					probes = probes.Concat(SubCommandWords("BuildAdminCommands"));
				}
				if (level < AccessLevel.GameMaster)
				{
					probes = probes.Concat(SubCommandWords("BuildGameMasterCommands"));
				}
				foreach (string probe in probes)
				{
					// A sub-command word that is also a command the caller has (unstuck, tickets) is answered as that command.
					if (registry.Build(level, string.Empty).Any(l => Regex.IsMatch(l, $@"(?<![\w<])/{Regex.Escape(probe.TrimStart('/'))}(?![A-Za-z])", RegexOptions.IgnoreCase)))
					{
						continue;
					}
					LogAssert.IsTrue(registry.Build(level, probe).SequenceEqual(unknown), $"/help {probe} as {level} must answer as /help doesnotexist");
				}
			}

			// Control: the same question from somebody who may run it is answered.
			LogAssert.IsFalse(registry.Build(AccessLevel.Admin, "gm").SequenceEqual(registry.Build(AccessLevel.Admin, "doesnotexist")),
				"an administrator's /help gm must describe /gm");
			LogAssert.IsFalse(registry.Build(AccessLevel.GameMaster, "gm").SequenceEqual(registry.Build(AccessLevel.GameMaster, "doesnotexist")),
				"a game master's /help gm must describe /gm");
		}

		[Test]
		public void AliasesAreShownTogether()
		{
			Registry registry = ReadRegistry();
			string text = string.Join("\n", registry.Build(AccessLevel.Player, string.Empty));
			StringAssert.Contains("/unstuck|/stuck", text);
			StringAssert.Contains("/leaveinstance|/exitinstance", text);
			LogAssert.AreEqual(1, Regex.Matches(text, @"(?<![\w<])/stuck(?![A-Za-z])").Count, "/stuck must appear once, beside /unstuck");

			// Asked by any of its words, with or without the slash, a command gets one answer.
			IReadOnlyList<string> byMain = registry.Build(AccessLevel.Player, "/unstuck");
			LogAssert.IsTrue(byMain.Count == 2 && byMain[0].StartsWith("Usage: /unstuck|/stuck", StringComparison.Ordinal), string.Join(" / ", byMain));
			LogAssert.IsTrue(registry.Build(AccessLevel.Player, "stuck").SequenceEqual(byMain), "/help stuck must describe /unstuck");
			LogAssert.IsTrue(registry.Build(AccessLevel.Player, "W").SequenceEqual(registry.Build(AccessLevel.Player, "/world")), "/help W must describe /world");
		}

		[Test]
		public void NothingIsListedForABannedCharacterAndUndescribedCommandsStillShow()
		{
			Registry registry = ReadRegistry();
			LogAssert.AreEqual(0, registry.Build(AccessLevel.Banned, string.Empty).Count, "nothing is usable from Banned");

			var bare = new Registry();
			bare.Commands["/bare"] = new ChatCommandRegistration() { MinimumAccessLevel = AccessLevel.Player };
			bare.Commands["/bareop"] = new ChatCommandRegistration() { MinimumAccessLevel = AccessLevel.GameMaster };
			string player = string.Join("\n", bare.Build(AccessLevel.Player, string.Empty));
			StringAssert.Contains(ChatCommandHelpListing.UndescribedCategory + ": /bare", player);
			StringAssert.DoesNotContain("/bareop", player);
		}

		[Test]
		public void RemovingACommandRemovesItsHelp()
		{
			const string word = "/chathelpcommandtestsprobe";
			try
			{
				ChatHelper.AddCommands(new Dictionary<string, ChatCommand>() { { word, (c, m) => true } });
				ChatHelper.SetCommandHelp(word, new ChatCommandHelp() { Category = "Test", Summary = "probe" });
				LogAssert.IsTrue(ChatHelper.CommandHelp.ContainsKey(word), "SetCommandHelp must record the help");
				ChatHelper.RemoveCommands(new[] { word });
				LogAssert.IsFalse(ChatHelper.CommandHelp.ContainsKey(word), "RemoveCommands must remove the help with the command");
			}
			finally
			{
				ChatHelper.RemoveCommands(new[] { word });
			}
		}

		[Test]
		public void HelpIsRegisteredAndRemovedWithTheChatSystem()
		{
			SourceScanPins.HoldsAndFires("ChatSystem", SourceScanPins.ReadCode(ChatSystemPath),
				c =>
				{
					string init = SourceScanPins.Body(c, "public override ServerComponentInitializationStatus InitializeOnce(");
					string deinit = SourceScanPins.Body(c, "public override void OnDeinitialize(");
					if (init == null || !init.Contains("RegisterHelpCommand();"))
					{
						return "InitializeOnce must register /help";
					}
					return deinit != null && deinit.Contains("UnregisterHelpCommand();") ? null : "OnDeinitialize must remove /help";
				},
				SourceScanPins.Replace("UnregisterHelpCommand();", string.Empty),
				"/help is never removed");

			FieldInfo field = typeof(ChatSystem).GetField("HelpCommandWords", BindingFlags.Static | BindingFlags.NonPublic);
			LogAssert.IsNotNull(field, "ChatSystem.HelpCommandWords must exist");
			string[] removed = (string[])field.GetValue(null);
			string help = SourceScanPins.ReadCode(HelpPath);
			LogAssert.IsTrue(RegisteredWords(help, out _).Keys.OrderBy(w => w).SequenceEqual(removed.OrderBy(w => w)),
				"every word /help registers must be in HelpCommandWords");

			SourceScanPins.HoldsAndFires("ChatSystem.Help", help,
				c =>
				{
					string unregister = SourceScanPins.Body(c, "private void UnregisterHelpCommand(");
					if (unregister == null || !unregister.Contains("ChatHelper.RemoveCommands(HelpCommandWords);"))
					{
						return "UnregisterHelpCommand must remove HelpCommandWords";
					}
					string handler = SourceScanPins.Body(c, "private bool OnHelpCommand(");
					return handler != null && handler.Contains("ChatCommandHelpListing.Build(character.AccessLevel, msg.Text)")
						? null
						: "the listing must be built from the character's own server-loaded access level";
				},
				SourceScanPins.Replace("character.AccessLevel, msg.Text", "AccessLevel.Admin, msg.Text"),
				"the listing is built for a fixed level instead of the caller's");
		}
	}
}
