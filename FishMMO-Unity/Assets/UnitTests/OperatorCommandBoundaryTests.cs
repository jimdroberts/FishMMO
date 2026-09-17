using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Auth.Core;
using FishMMO.Server.Implementation.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The lines the in-game operator commands must not cross, pinned.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>A game master must not be able to change the game.</b> Currency, items, attributes,
	/// health, life and death belong to <c>/admin</c>: a game master account that can mint value is
	/// an exploit waiting for one stolen password. Both sets are partials of one class, so nothing
	/// in the compiler stops a <c>/gm</c> handler calling an administrator helper. These scans do:
	/// no game master file may name a gameplay controller or call any method an administrator file
	/// declares.
	/// </para>
	/// <para>
	/// <b>One table, three readers.</b> The dispatcher, <c>help</c> and the staff console all read
	/// the same table. The reflection half walks it and holds every entry to what those readers
	/// assume: a spec that parses, a usage line that fits the chat limit, a roster action whose first
	/// argument really is a character.
	/// </para>
	/// <para>
	/// <b>Every privileged surface is audited and gated.</b> The staff console's reads are
	/// re-authorised on arrival; the chat mute is tested after commands and before chat; and the
	/// audit redactor narrows what is recorded without ever stopping the record.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class OperatorCommandBoundaryTests
	{
		private const string CommandDirectory = "Assets/Scripts/Server/Implementation/World/SceneServer/SceneServer";
		private const string ChatSystemPath = "Assets/Scripts/Server/Implementation/World/SceneServer/Chat/ChatSystem.cs";
		private const string PlayerCharacterPath = "Assets/Scripts/Shared/Implementation/Entity/PlayerCharacter.cs";
		private const string LoadingPath = "Assets/Scripts/Server/Implementation/World/SceneServer/Character/CharacterSystem.Loading.cs";
		private const string StaffConsolePath = CommandDirectory + "/SceneServerSystem.StaffConsole.cs";
		private const string OperatorCommandsPath = CommandDirectory + "/SceneServerSystem.OperatorCommands.cs";

		/// <summary>Types and members that change the game a player is playing.</summary>
		private static readonly string[] GameplayTokens =
		{
			"CharacterCurrency",
			"ICharacterAttributeController",
			"ICharacterAttributeService",
			"ICharacterInventorySystem",
			"ICharacterItemService",
			"TryGrantItem",
			"ICharacterDamageController",
			"IBuffController",
			"IAbilityController",
			"IAbilityKnowledgeController",
			"IFactionController",
			"IAchievementController",
			"IQuestController",
			"ICurrencyLedgerService",
			"IWeatherService",
			"weatherHost.",
			"PersistAccessLevelAsync",
			".Immortal",
			".SetValue(",
			".AddValue(",
		};

		// ── Helpers ───────────────────────────────────────────────────────────

		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path).Replace("\r\n", "\n");
		}

		/// <summary>The source with comment lines dropped, so a scan sees code and not prose.</summary>
		private static string CodeOnly(string source)
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

		/// <summary>The body of a method, brace-matched from its declaration.</summary>
		private static string MethodBody(string source, string signature)
		{
			int start = source.IndexOf(signature, StringComparison.Ordinal);
			LogAssert.IsTrue(start >= 0, $"the source must still declare {signature}");
			int open = source.IndexOf('{', start);
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
			LogAssert.Fail($"{signature}'s body must be balanced");
			return string.Empty;
		}

		private static string[] CommandFiles(string pattern)
		{
			string directory = Path.Combine(Directory.GetCurrentDirectory(), CommandDirectory);
			string[] files = Directory.GetFiles(directory, pattern)
				.Select(f => CommandDirectory + "/" + Path.GetFileName(f))
				.OrderBy(f => f, StringComparer.Ordinal)
				.ToArray();
			LogAssert.IsTrue(files.Length > 0, $"no files match {pattern}");
			return files;
		}

		/// <summary>Files every game master can reach: the /gm set, the console, and the shared helpers.</summary>
		private static string[] GameMasterReachableFiles()
		{
			return CommandFiles("SceneServerSystem.GameMasterCommands*.cs")
				.Concat(new[] { StaffConsolePath, OperatorCommandsPath })
				.ToArray();
		}

		// ── The game master boundary ──────────────────────────────────────────

		[Test]
		public void NoGameMasterFileNamesAGameplayController()
		{
			foreach (string file in GameMasterReachableFiles())
			{
				string code = CodeOnly(ReadSource(file));
				foreach (string token in GameplayTokens)
				{
					LogAssert.IsFalse(code.Contains(token),
						$"{file} names {token}. Anything that changes the game belongs to /admin, never to a game master.");
				}
			}
		}

		[Test]
		public void NoGameMasterFileCallsAnythingAnAdministratorFileDeclares()
		{
			/* The partial-class loophole: a /gm handler calling ChangeCurrency compiles and passes
			 * every other test. The declared names are read from the administrator files themselves,
			 * so a helper added there later is covered without editing this test. */
			var declared = new HashSet<string>(StringComparer.Ordinal);
			foreach (string file in CommandFiles("SceneServerSystem.AdminCommands*.cs"))
			{
				foreach (Match match in Regex.Matches(CodeOnly(ReadSource(file)),
					@"\b(?:private|protected|public|internal)\b[^\n;=(]*?\b([A-Za-z_][A-Za-z0-9_]*)\s*\("))
				{
					declared.Add(match.Groups[1].Value);
				}
			}
			LogAssert.IsTrue(declared.Contains("ChangeCurrency") && declared.Contains("KillCharacter"),
				"the declaration scan must find the administrator handlers, or it is checking nothing");

			foreach (string file in GameMasterReachableFiles())
			{
				string code = CodeOnly(ReadSource(file));
				foreach (string name in declared)
				{
					LogAssert.IsFalse(Regex.IsMatch(code, @"\b" + Regex.Escape(name) + @"\s*\("),
						$"{file} calls {name}, which an administrator file declares.");
				}
			}
		}

		[Test]
		public void AGameMasterBanAlwaysEnds()
		{
			foreach (string file in GameMasterReachableFiles())
			{
				string code = CodeOnly(ReadSource(file));
				LogAssert.IsFalse(Regex.IsMatch(code, @"BanAsync\(\s*[A-Za-z_][A-Za-z0-9_]*\s*\)"),
					$"{file} calls the plain ban, which is permanent.");
				LogAssert.IsFalse(Regex.IsMatch(code, @"BanAsync\([^;]*?,\s*null\s*,"),
					$"{file} passes a null end to a ban, which is permanent.");
			}
		}

		[Test]
		public void EachSetIsRegisteredOnceAtItsOwnLevel()
		{
			string gm = CodeOnly(ReadSource(CommandDirectory + "/SceneServerSystem.GameMasterCommands.cs"));
			string admin = CodeOnly(ReadSource(CommandDirectory + "/SceneServerSystem.AdminCommands.cs"));

			StringAssert.Contains("{ \"/gm\", OnGameMasterCommand },\n\t\t\t}, AccessLevel.GameMaster);", gm);
			StringAssert.Contains("{ \"/admin\", OnAdminCommand },\n\t\t\t}, AccessLevel.Admin);", admin);

			int registrations = CommandFiles("SceneServerSystem.*.cs")
				.Sum(f => Regex.Matches(CodeOnly(ReadSource(f)), @"ChatHelper\.AddCommands\(").Count);
			LogAssert.AreEqual(2, registrations,
				"one registration per set: a second one is a sub-command that could be registered below its level");
		}

		// ── The table ─────────────────────────────────────────────────────────

		private sealed class TableEntry
		{
			public string Set;
			public string Name;
			public string[] Aliases;
			public string Category;
			public string Summary;
			public string Arguments;
			public bool RosterAction;
			public bool TicketAction;
		}

		private static List<TableEntry> ReadTable(string buildMethod, string set, out object commands)
		{
			SceneServerSystem system = ScriptableObject.CreateInstance<SceneServerSystem>();
			try
			{
				MethodInfo build = typeof(SceneServerSystem).GetMethod(buildMethod, BindingFlags.Instance | BindingFlags.NonPublic);
				LogAssert.IsNotNull(build, $"SceneServerSystem.{buildMethod} must exist");
				commands = build.Invoke(system, null);

				var entries = new List<TableEntry>();
				foreach (object command in (IEnumerable)commands)
				{
					Type type = command.GetType();
					T Field<T>(string name) => (T)type.GetField(name, BindingFlags.Instance | BindingFlags.Public).GetValue(command);
					entries.Add(new TableEntry()
					{
						Set = set,
						Name = Field<string>("Name"),
						Aliases = Field<string[]>("Aliases") ?? Array.Empty<string>(),
						Category = Field<string>("Category"),
						Summary = Field<string>("Summary"),
						Arguments = Field<string>("Arguments"),
						RosterAction = Field<bool>("RosterAction"),
						TicketAction = Field<bool>("TicketAction"),
					});
				}
				return entries;
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(system);
			}
		}

		private static List<TableEntry> BothTables()
		{
			return ReadTable("BuildGameMasterCommands", "/gm", out _)
				.Concat(ReadTable("BuildAdminCommands", "/admin", out _))
				.ToList();
		}

		[Test]
		public void EverySetBuildsWithNoWordClaimedTwice()
		{
			Type setType = typeof(SceneServerSystem).GetNestedType("OperatorCommandSet", BindingFlags.NonPublic);
			LogAssert.IsNotNull(setType);

			foreach (var (method, command, level) in new[]
			{
				("BuildGameMasterCommands", "/gm", AccessLevel.GameMaster),
				("BuildAdminCommands", "/admin", AccessLevel.Admin),
			})
			{
				ReadTable(method, command, out object commands);
				try
				{
					Activator.CreateInstance(setType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
						null, new object[] { command, level, commands }, null);
				}
				catch (TargetInvocationException ex)
				{
					LogAssert.Fail($"{command} does not build: {ex.InnerException?.Message}");
				}
			}
		}

		[Test]
		public void EveryEntryIsDescribedAndItsSpecParses()
		{
			foreach (TableEntry entry in BothTables())
			{
				string label = $"{entry.Set} {entry.Name}";
				LogAssert.IsFalse(string.IsNullOrWhiteSpace(entry.Category), $"{label} has no category");
				LogAssert.IsFalse(string.IsNullOrWhiteSpace(entry.Summary), $"{label} has no summary");
				LogAssert.IsTrue(entry.Summary.Length <= ChatBroadcast.MaxTextLength,
					$"{label}'s summary is {entry.Summary.Length} long; help would be cut");

				LogAssert.IsTrue(OperatorCommandParsing.TryParseSpec(entry.Arguments, out var arguments, out string error),
					$"{label}'s spec does not parse: {error}");

				string usage = "Usage: " + OperatorCommandParsing.FormatUsage(entry.Set, entry.Name, entry.Arguments);
				LogAssert.IsTrue(usage.Length <= ChatBroadcast.MaxTextLength, $"{label}'s usage line is {usage.Length} long");

				if (entry.RosterAction)
				{
					LogAssert.IsTrue(arguments.Count > 0 && arguments[0].Kind == StaffArgumentKind.Character,
						$"{label} is offered on a roster row but its first argument is not a character");
				}
				if (entry.TicketAction)
				{
					LogAssert.IsTrue(arguments.Count > 0 && arguments[0].Kind == StaffArgumentKind.Ticket,
						$"{label} is offered on a ticket but its first argument is not a ticket number");
				}
			}
		}

		[Test]
		public void EconomyAndCharacterStateAreAdministratorOnly()
		{
			List<TableEntry> gm = ReadTable("BuildGameMasterCommands", "/gm", out _);
			List<TableEntry> admin = ReadTable("BuildAdminCommands", "/admin", out _);

			string[] adminOnly = { "setgold", "givegold", "takegold", "giveitem", "setattr", "heal", "revive", "kill", "god", "ban", "access" };
			foreach (string name in adminOnly)
			{
				LogAssert.IsTrue(admin.Any(e => e.Name == name), $"/admin {name} must exist");
				LogAssert.IsFalse(gm.Any(e => e.Name == name || e.Aliases.Contains(name)), $"/gm must not answer to {name}");
			}
			LogAssert.IsFalse(gm.Any(e => e.Category == "Economy" || e.Category == "Character"),
				"no game master command may be filed as economy or character state");
		}

		[Test]
		public void EveryUsageReplyNamesARealCommand()
		{
			/* ReplyUsage by name silently prints nothing for a name that is not in the table, which
			 * reads to the operator as the command ignoring them. */
			var names = new Dictionary<string, HashSet<string>>()
			{
				{ "gameMasterCommands", new HashSet<string>(ReadTable("BuildGameMasterCommands", "/gm", out _).Select(e => e.Name)) },
				{ "adminCommands", new HashSet<string>(ReadTable("BuildAdminCommands", "/admin", out _).Select(e => e.Name)) },
			};

			int seen = 0;
			foreach (string file in CommandFiles("SceneServerSystem.*.cs"))
			{
				foreach (Match match in Regex.Matches(CodeOnly(ReadSource(file)),
					@"ReplyUsage\(character, (gameMasterCommands|adminCommands), ""([a-z]+)""\)"))
				{
					++seen;
					LogAssert.IsTrue(names[match.Groups[1].Value].Contains(match.Groups[2].Value),
						$"{file} replies with the usage of '{match.Groups[2].Value}', which {match.Groups[1].Value} does not contain");
				}
			}
			LogAssert.IsTrue(seen > 10, "the usage scan must find the handlers' usage replies, or it is checking nothing");
		}

		// ── Mutes ─────────────────────────────────────────────────────────────

		[Test]
		public void AMuteIsTestedAfterCommandsAndBeforeChat()
		{
			string body = MethodBody(CodeOnly(ReadSource(ChatSystemPath)), "private void ProcessNewChatMessage(");
			int command = body.IndexOf("ChatHelper.TryParseCommand(", StringComparison.Ordinal);
			int mute = body.IndexOf("ChatMutedUntilTicks >", StringComparison.Ordinal);
			int chat = body.IndexOf("ChatHelper.TryParseChatCommand(", StringComparison.Ordinal);

			LogAssert.IsTrue(command >= 0 && mute >= 0 && chat >= 0, "all three must still be in ProcessNewChatMessage");
			LogAssert.IsTrue(command < mute, "the mute must come after commands, so a muted player can still /helpme");
			LogAssert.IsTrue(mute < chat, "the mute must come before every channel, /tell included");
		}

		[Test]
		public void APooledCharacterDropsItsMute()
		{
			string body = MethodBody(CodeOnly(ReadSource(PlayerCharacterPath)), "public override void ResetState(");
			StringAssert.Contains("ChatMutedUntilTicks = 0;", body);
			StringAssert.Contains("ChatMuteReason = null;", body);
		}

		[Test]
		public void TheLoadReadsTheMuteAndFailsOpen()
		{
			string code = CodeOnly(ReadSource(LoadingPath));
			StringAssert.Contains("FetchChatMuteAsync(characterID)", code);
			StringAssert.Contains("character.ChatMutedUntilTicks = ChatMutePolicy.ResolveUntilTicks(", code);
			StringAssert.Contains("character.ChatMutedUntilTicks = 0;", code);
		}

		// ── The staff console ─────────────────────────────────────────────────

		[Test]
		public void EveryConsoleReadIsAuthorisedBeforeItDoesAnything()
		{
			string code = CodeOnly(ReadSource(StaffConsolePath));
			foreach (string handler in new[] { "OnStaffRosterRequest(", "OnStaffTicketQueueRequest(", "OnStaffTicketDetailRequest(" })
			{
				string body = MethodBody(code, "private void " + handler);
				int authorise = body.IndexOf("TryAuthorizeStaffRequest(", StringComparison.Ordinal);
				LogAssert.IsTrue(authorise >= 0, $"{handler} must authorise the request");
				foreach (string work in new[] { "Broadcast(", "TryEnqueueAsyncWork(", "CharactersByID" })
				{
					int at = body.IndexOf(work, StringComparison.Ordinal);
					LogAssert.IsTrue(at < 0 || at > authorise, $"{handler} reaches {work} before authorising");
				}
			}

			string gate = MethodBody(code, "private bool TryAuthorizeStaffRequest(");
			StringAssert.Contains("AccessLevel.GameMaster", gate);
			StringAssert.Contains("ChatHelper.ReportRefused(", gate);
		}

		[Test]
		public void TheConsoleHelpNeverLeavesTheServer()
		{
			// The client ships an empty console: nothing in the Shared contract may name a command.
			string contract = CodeOnly(ReadSource("Assets/Scripts/Shared/Implementation/Network/Staff/StaffConsoleBroadcasts.cs"));
			foreach (TableEntry entry in BothTables())
			{
				LogAssert.IsFalse(Regex.IsMatch(contract, "\"" + Regex.Escape(entry.Name) + "\""),
					$"the shared contract names '{entry.Name}'");
			}
		}

		// ── Audit redaction at the gate ───────────────────────────────────────

		[Test]
		public void TicketTextIsWithheldFromTheAuditRow()
		{
			MethodInfo redact = typeof(SceneServerSystem).GetMethod("RedactGameMasterAudit", BindingFlags.Static | BindingFlags.NonPublic);
			LogAssert.IsNotNull(redact);
			string Redact(string text) => (string)redact.Invoke(null, new object[] { text });

			foreach (string word in new[] { "reply", "note", "resolve", "REPLY" })
			{
				string audited = Redact($"{word} 42 my address is 1 Example Road");
				StringAssert.DoesNotContain("Example", audited);
				StringAssert.Contains("42", audited, "the ticket number is what the auditor needs");
			}
			LogAssert.AreEqual("mute Bob 30m spamming", Redact("mute Bob 30m spamming"), "other commands are recorded as typed");
		}

		/// <summary>A character that is nothing but an access level, for driving the gate.</summary>
		public class GateCharacter : DispatchProxy
		{
			public AccessLevel Level;

			protected override object Invoke(MethodInfo targetMethod, object[] args)
			{
				switch (targetMethod.Name)
				{
					case "get_AccessLevel": return Level;
					case "get_CharacterName": return "GateTester";
					case "get_Account": return "gatetester";
				}
				return targetMethod.ReturnType.IsValueType && targetMethod.ReturnType != typeof(void)
					? Activator.CreateInstance(targetMethod.ReturnType)
					: null;
			}
		}

		private static IPlayerCharacter MakeCharacter(AccessLevel level)
		{
			IPlayerCharacter character = DispatchProxy.Create<IPlayerCharacter, GateCharacter>();
			((GateCharacter)(object)character).Level = level;
			return character;
		}

		[Test]
		public void TheGateRecordsTheRedactedTextAndStillRunsTheCommand()
		{
			const string command = "/zzoperatorgatetest";
			string recorded = null;
			int ran = 0;
			Action<IPlayerCharacter, string, string, AccessLevel> onElevated = (c, cmd, text, level) => recorded = text;

			ChatHelper.OnElevatedCommand += onElevated;
			try
			{
				ChatHelper.AddCommands(new Dictionary<string, ChatCommand>() { { command, (c, m) => { ++ran; return true; } } }, AccessLevel.GameMaster);
				ChatHelper.SetAuditRedactor(command, text => "redacted");

				LogAssert.IsTrue(ChatHelper.TryParseCommand(command, MakeCharacter(AccessLevel.GameMaster), new ChatBroadcast() { Text = "secret" }));
				LogAssert.AreEqual("redacted", recorded);
				LogAssert.AreEqual(1, ran);

				// Removing the command removes its redactor: a later registration records what is typed.
				ChatHelper.RemoveCommands(new[] { command });
				ChatHelper.AddCommands(new Dictionary<string, ChatCommand>() { { command, (c, m) => { ++ran; return true; } } }, AccessLevel.GameMaster);
				ChatHelper.TryParseCommand(command, MakeCharacter(AccessLevel.GameMaster), new ChatBroadcast() { Text = "plain" });
				LogAssert.AreEqual("plain", recorded);
			}
			finally
			{
				ChatHelper.OnElevatedCommand -= onElevated;
				ChatHelper.RemoveCommands(new[] { command });
			}
		}

		[Test]
		public void AThrowingRedactorWithholdsEverything()
		{
			const string command = "/zzoperatorgatethrows";
			string recorded = null;
			int ran = 0;
			Action<IPlayerCharacter, string, string, AccessLevel> onElevated = (c, cmd, text, level) => recorded = text;

			bool ignored = UnityEngine.TestTools.LogAssert.ignoreFailingMessages;
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
			ChatHelper.OnElevatedCommand += onElevated;
			try
			{
				ChatHelper.AddCommands(new Dictionary<string, ChatCommand>() { { command, (c, m) => { ++ran; return true; } } }, AccessLevel.Admin);
				ChatHelper.SetAuditRedactor(command, text => throw new InvalidOperationException("boom"));

				ChatHelper.TryParseCommand(command, MakeCharacter(AccessLevel.Admin), new ChatBroadcast() { Text = "the secret" });
				LogAssert.IsNotNull(recorded, "the row must still be written");
				StringAssert.DoesNotContain("secret", recorded);
				LogAssert.AreEqual(1, ran, "a broken redactor must not stop the command");
			}
			finally
			{
				ChatHelper.OnElevatedCommand -= onElevated;
				ChatHelper.RemoveCommands(new[] { command });
				UnityEngine.TestTools.LogAssert.ignoreFailingMessages = ignored;
			}
		}

		[Test]
		public void AConsoleRequestRefusalGoesThroughTheCommandRefusalEvent()
		{
			string refused = null;
			AccessLevel required = AccessLevel.Banned;
			Action<IPlayerCharacter, string, AccessLevel> onRefused = (c, request, level) => { refused = request; required = level; };

			ChatHelper.OnCommandRefused += onRefused;
			try
			{
				ChatHelper.ReportRefused(MakeCharacter(AccessLevel.Player), "staffconsole.roster", AccessLevel.GameMaster);
			}
			finally
			{
				ChatHelper.OnCommandRefused -= onRefused;
			}

			LogAssert.AreEqual("staffconsole.roster", refused);
			LogAssert.AreEqual(AccessLevel.GameMaster, required);
		}
	}
}
