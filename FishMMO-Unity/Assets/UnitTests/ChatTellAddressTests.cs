using System;
using NUnit.Framework;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// <c>/tell</c> to a name with a space (optional change O9).
	/// </summary>
	/// <remarks>
	/// <para>
	/// Character names may hold single spaces, and a whisper took its target as the first word, so
	/// <c>/tell Aragorn of Arnor hi</c> went to "Aragorn". A quoted name is now the target whole, an
	/// unquoted one is still one word, and the persisted row carries the same address so the target's
	/// own scene server finds it: the SQL half (<c>ChatService.BuildPumpFilterSql</c>) was run against
	/// a throwaway PostgreSQL with quoted, unquoted and malformed rows; <see cref="ChatPumpTests"/>
	/// pins its text.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class ChatTellAddressTests
	{
		private const string ChatDirectory = "Assets/Scripts/Server/Implementation/World/SceneServer/Chat";
		private const string ChatPanelPath = "Assets/Scripts/Client/GUI/World/Chat/UITKChat.cs";

		private static void AssertParses(string text, string target, string body)
		{
			LogAssert.IsTrue(ChatTellAddress.TryParse(text, out string parsedTarget, out string parsedBody), $"'{text}' must parse");
			LogAssert.AreEqual(target, parsedTarget, $"target of '{text}'");
			LogAssert.AreEqual(body, parsedBody, $"body of '{text}'");
		}

		private static void AssertRefused(string text, string why)
		{
			LogAssert.IsFalse(ChatTellAddress.TryParse(text, out string target, out string body), why);
			LogAssert.AreEqual(string.Empty, target, $"a refused '{text}' leaves no target");
			LogAssert.AreEqual(string.Empty, body, $"a refused '{text}' leaves no body");
		}

		// ── The parse ──────────────────────────────────────────────────────────

		[Test]
		public void AnUnquotedTargetIsStillOneWord()
		{
			AssertParses("Bob hello there", "Bob", "hello there");
			AssertParses("Bob   hello", "Bob", "hello");
			AssertParses("Aragorn of Arnor hi", "Aragorn", "of Arnor hi");
		}

		[Test]
		public void AQuotedNameIsTheTargetWhole()
		{
			AssertParses("\"Aragorn of Arnor\" hello", "Aragorn of Arnor", "hello");
			AssertParses("\"Aragorn of Arnor\"hello", "Aragorn of Arnor", "hello");
			AssertParses("\"Bob\" hi", "Bob", "hi");
			AssertParses("  \"Aragorn of Arnor\"   a \"quoted\" body  ", "Aragorn of Arnor", "a \"quoted\" body");
		}

		[Test]
		public void AQuotedNameIsNormalisedToSingleSpaces()
		{
			AssertParses("\"  Aragorn   of\tArnor \" hello", "Aragorn of Arnor", "hello");
			LogAssert.AreEqual("Aragorn of Arnor", ChatTellAddress.NormalizeName(" Aragorn  of Arnor "));
			LogAssert.AreEqual("Bob", ChatTellAddress.NormalizeName("Bob"), "a clean name comes back as it was");
			LogAssert.AreEqual(string.Empty, ChatTellAddress.NormalizeName("   "));
			LogAssert.AreEqual(string.Empty, ChatTellAddress.NormalizeName(null));
		}

		[Test]
		public void NoTargetNoBodyOrAnUnclosedQuoteIsRefused()
		{
			AssertRefused(null, "nothing");
			AssertRefused("", "empty");
			AssertRefused("   ", "blank");
			AssertRefused("Bob", "a target with nothing to say, as a tell with no body always was");
			AssertRefused("\"Aragorn of Arnor\"", "a quoted target with nothing to say");
			AssertRefused("\"Aragorn of Arnor\"   ", "a quoted target with only spaces after it");
			AssertRefused("\"Aragorn of Arnor hello", "an unclosed quote is not a name");
			AssertRefused("\"\" hello", "an empty quoted name");
			AssertRefused("\"   \" hello", "a blank quoted name");
		}

		[Test]
		public void TheAddressReadsWithOrWithoutABody()
		{
			LogAssert.IsTrue(ChatTellAddress.TryParseAddress("\"Aragorn of Arnor\"", out string quoted) && quoted == "Aragorn of Arnor",
				"the pump's key needs the address only, as the SQL does");
			LogAssert.IsTrue(ChatTellAddress.TryParseAddress("bob", out string single) && single == "bob");
			LogAssert.IsFalse(ChatTellAddress.TryParseAddress("\"unclosed hello", out _), "an unclosed quote has no address");
		}

		// ── The format ─────────────────────────────────────────────────────────

		[Test]
		public void AOneWordNameIsWrittenExactlyAsBeforeAndANameWithASpaceIsQuoted()
		{
			LogAssert.AreEqual("Bob", ChatTellAddress.Format("Bob"), "a one-word row is byte-for-byte what it always was");
			LogAssert.AreEqual("Bob hello", ChatTellAddress.FormatLine("Bob", "hello"));
			LogAssert.AreEqual("\"Aragorn of Arnor\"", ChatTellAddress.Format("Aragorn of Arnor"));
			LogAssert.AreEqual("\"Aragorn of Arnor\" hello", ChatTellAddress.FormatLine("Aragorn  of Arnor", "hello"),
				"written normalised, so the SQL's exact match holds");
			LogAssert.AreEqual("/tell Bob ", ChatTellAddress.FormatCommand("Bob"));
			LogAssert.AreEqual("/tell \"Aragorn of Arnor\" ", ChatTellAddress.FormatCommand("Aragorn of Arnor"),
				"a panel's Message button pre-fills a name with a space quoted");
		}

		[Test]
		public void WhatIsWrittenParsesBackToTheSameTargetAndBody()
		{
			foreach (string name in new[] { "Bob", "Aragorn of Arnor", "A B C D" })
			{
				foreach (string body in new[] { "hello", "hello there", "\"quoted\" words", "Bob hello" })
				{
					AssertParses(ChatTellAddress.FormatLine(name, body), name, body);
				}
			}
		}

		// ── Source pins, each with its control run ─────────────────────────────

		[Test]
		public void TheTellHandlerParsesAndPersistsTheAddress()
		{
			string code = SourceScanPins.ReadCode(ChatDirectory + "/ChatSystem.TellChat.cs");
			Func<string, string> check = c =>
			{
				string handler = SourceScanPins.Body(c, "public bool OnTellChat(");
				if (handler == null)
				{
					return "OnTellChat is missing";
				}
				if (!handler.Contains("ChatTellAddress.TryParse(msg.Text"))
				{
					return "the handler no longer reads the target through ChatTellAddress";
				}
				if (c.Contains("GetWordAndTrimmed("))
				{
					return "a tell target is taken as the first word again";
				}
				if (c.Contains("targetName + \" \" + trimmed"))
				{
					return "a whisper is persisted with a bare name the pump cannot find";
				}
				return null;
			};
			SourceScanPins.HoldsAndFires("O9 tell parse", code, check,
				SourceScanPins.Replace("ChatTellAddress.FormatLine(targetName, trimmed)", "targetName + \" \" + trimmed"),
				"persisting the unquoted name");
		}

		[Test]
		public void ThePumpKeysATellRowByItsAddress()
		{
			string code = SourceScanPins.ReadCode(ChatDirectory + "/ChatSystem.cs");
			Func<string, string> check = c =>
			{
				string body = SourceScanPins.Body(c, "private static ChatPumpKey PumpKeyOf(");
				if (body == null)
				{
					return "PumpKeyOf is missing";
				}
				return body.Contains("ChatTellAddress.TryParseAddress(message.Message") ? null : "a tell row is keyed by something other than its address";
			};
			SourceScanPins.HoldsAndFires("O9 pump key", code, check,
				SourceScanPins.Replace("ChatTellAddress.TryParseAddress(message.Message, out string targetName)", "ChatPumpKey.FirstWord(message.Message) is string targetName"),
				"keying a tell by its first word");
		}

		[Test]
		public void TheOfflineReplyNamesTheWholeTarget()
		{
			string code = SourceScanPins.ReadCode(ChatPanelPath);
			Func<string, string> check = c =>
			{
				string body = SourceScanPins.Body(c, "public bool OnTellChat(");
				if (body == null)
				{
					return "the client's OnTellChat is missing";
				}
				int offline = body.IndexOf("ChatHelper.TARGET_OFFLINE", StringComparison.Ordinal);
				if (offline < 0)
				{
					return "the offline branch is missing";
				}
				string branch = body.Substring(offline);
				int end = branch.IndexOf("ChatHelper.TELL_ERROR_MESSAGE_SELF", StringComparison.Ordinal);
				if (end > 0)
				{
					branch = branch.Substring(0, end);
				}
				return branch.Contains("GetWordAndTrimmed(") ? "the offline reply drops the first word of a name with a space" : null;
			};
			SourceScanPins.HoldsAndFires("O9 offline reply", code, check,
				SourceScanPins.Replace("string targetName = trimmed?.Trim();", "ChatHelper.GetWordAndTrimmed(trimmed, out string targetName);"),
				"taking the words after the first as the name");
		}

		[Test]
		public void EveryMessageButtonQuotesTheName()
		{
			foreach (string path in new[]
			{
				"Assets/Scripts/Client/GUI/World/Party/UITKParty.cs",
				"Assets/Scripts/Client/GUI/World/Guild/UITKGuild.cs",
				"Assets/Scripts/Client/GUI/World/FriendList/UITKFriendList.cs",
			})
			{
				string code = SourceScanPins.ReadCode(path);
				Func<string, string> check = c =>
				{
					if (c.Contains("$\"/tell {"))
					{
						return "a Message button pre-fills an unquoted name";
					}
					return c.Contains("ChatTellAddress.FormatCommand(") ? null : "the Message button is gone or no longer uses ChatTellAddress";
				};
				SourceScanPins.HoldsAndFires("O9 " + path, code, check,
					SourceScanPins.Replace("ChatTellAddress.FormatCommand(displayName)", "$\"/tell {displayName} \""),
					"pre-filling the bare name");
			}
		}
	}
}
