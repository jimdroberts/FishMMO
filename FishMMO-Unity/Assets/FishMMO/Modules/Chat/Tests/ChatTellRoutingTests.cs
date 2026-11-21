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
	public class ChatTellRoutingTests
	{
		private const string ChatDirectory = "Assets/FishMMO/Modules/Chat/Server";
		private const string ChatPanelPath = "Assets/FishMMO/Modules/Chat/Client/UITKChat.cs";

		// ── Source pins, each with its control run ─────────────────────────────

		[Test]
		public void TheTellHandlerParsesAndPersistsTheAddress()
		{
			string code = SourceScanPins.ReadCode(ChatDirectory + "/ChatChannelSystem.TellChat.cs");
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
			string code = SourceScanPins.ReadCode(ChatDirectory + "/ChatChannelSystem.cs");
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
	}
}
