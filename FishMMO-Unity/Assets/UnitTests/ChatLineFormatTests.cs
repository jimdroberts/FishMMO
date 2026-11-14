using System;
using System.IO;
using NUnit.Framework;
using FishMMO.Client;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs for how a chat line is written: the timestamp formats, the colon after a sender's
	/// name, and the lines that must not take one.
	/// </summary>
	[TestFixture]
	public class ChatLineFormatTests
	{
		private const string ChatPath = "Assets/Scripts/Client/GUI/World/Chat/UITKChat.cs";
		private const string OptionsLayoutPath = "Assets/Scripts/Client/GUI/World/Options/UIOptions.uxml";

		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(ProjectFiles.Exists(path), $"{relativePath} not found at {path}.");
			return ProjectFiles.ReadAllText(path);
		}

		private static string MethodBody(string source, string signature, string nextSymbol)
		{
			int start = source.IndexOf(signature, StringComparison.Ordinal);
			LogAssert.IsTrue(start >= 0, $"the source must still declare {signature}");
			int end = source.IndexOf(nextSymbol, start + signature.Length, StringComparison.Ordinal);
			LogAssert.IsTrue(end > start, $"{nextSymbol} must follow {signature}");
			return source.Substring(start, end - start);
		}

		[TestCase(ClientChatSettings.TimestampFormat.Hours24, "[14:05]")]
		[TestCase(ClientChatSettings.TimestampFormat.Hours24Seconds, "[14:05:09]")]
		[TestCase(ClientChatSettings.TimestampFormat.Hours12, "[2:05 PM]")]
		[TestCase(ClientChatSettings.TimestampFormat.Hours12Seconds, "[2:05:09 PM]")]
		public void EachTimestampFormatWritesWhatItsLabelPromises(ClientChatSettings.TimestampFormat format, string expected)
		{
			DateTime time = new DateTime(2026, 11, 13, 14, 5, 9);
			Assert.AreEqual(expected, ClientChatSettings.FormatTimestamp(time, format));
		}

		[Test]
		public void AnOutOfRangeFormatFallsBackRatherThanThrowing()
		{
			DateTime time = new DateTime(2026, 11, 13, 9, 7, 0);
			Assert.AreEqual("[09:07]", ClientChatSettings.FormatTimestamp(time, (ClientChatSettings.TimestampFormat)99));
		}

		[Test]
		public void EveryFormatHasALabel()
		{
			Assert.AreEqual(Enum.GetValues(typeof(ClientChatSettings.TimestampFormat)).Length,
				ClientChatSettings.TimestampFormatLabels.Length);
		}

		[Test]
		public void ASenderNameEndsWithAColonUnlessItIsTheSubject()
		{
			/* "[Tell] Anewbie: hello" -- but the party/guild errors carry the TARGET as sender
			 * and complete a sentence about them ("Bob" + " is already in a party."), where a
			 * colon would read "Bob: is already in a party." */
			string body = MethodBody(ReadSource(ChatPath), "private static string BuildNameText", "private bool ShouldShowSender");
			LogAssert.IsTrue(body.Contains("NAME_SEPARATOR"), "a sender name must be followed by the separator");
			LogAssert.IsTrue(body.Contains("record.NameIsSubject"), "a subject-style line must not take the separator");
		}

		[Test]
		public void TellErrorsAreNoticesNotLinesFromThePlayer()
		{
			/* The offline reply carried the local player as its sender; with the colon it would
			 * read "Anewbie: Sonez is not online." as if the player had typed it. */
			string body = MethodBody(ReadSource(ChatPath), "public bool OnTellChat(", "public bool OnTradeChat(");
			int offline = body.IndexOf("ChatHelper.TARGET_OFFLINE", StringComparison.Ordinal);
			LogAssert.IsTrue(offline >= 0, "the offline branch must still exist");
			int fromOther = body.IndexOf("We received a tell from someone else", StringComparison.Ordinal);
			LogAssert.IsTrue(fromOther > offline, "the received-tell branch must follow the error branches");
			string errors = body.Substring(offline, fromOther - offline);
			LogAssert.IsFalse(errors.Contains("AddSenderMessage("), "a tell error must not be written as a line from a sender");
			LogAssert.IsTrue(errors.Contains("AddChannelNotice("), "a tell error must be written as a channel notice");
		}

		[Test]
		public void TheOptionsUiTabCarriesTheChatFormatRows()
		{
			string layout = ReadSource(OptionsLayoutPath);
			foreach (string name in new[]
			{
				"chat-timestamps-toggle",
				"chat-timestamp-format-dropdown",
				"chat-channel-tags-toggle",
				"chat-group-toggle",
			})
			{
				LogAssert.IsTrue(layout.Contains("name=\"" + name + "\""), $"UIOptions.uxml must declare {name}");
			}
		}
	}
}
