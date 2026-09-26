using NUnit.Framework;
using FishMMO.Server.Implementation.World.SceneServer;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// <c>/report</c> names its target by the <c>/tell</c> rule: one word, or a name in double quotes.
	/// </summary>
	/// <remarks>
	/// It took the first space-delimited word (<c>ChatHelper.GetWordAndTrimmed</c>), so a character
	/// whose name holds a space could not be reported at all: <c>/report Aragorn of Arnor spamming</c>
	/// filed against "Aragorn" with "of Arnor spamming" as the description. The parse is now
	/// <see cref="FishMMO.Shared.ChatTellAddress"/>, pinned here through the pure
	/// <c>ChatSystem.ParseReportCommand</c>, and the handler is pinned to use it.
	/// </remarks>
	[TestFixture]
	public class ReportCommandParseTests
	{
		private const string SupportCommandsPath =
			"Assets/Scripts/Server/Implementation/World/SceneServer/Chat/ChatSystem.SupportCommands.cs";

		private static void AssertComplete(string text, string target, string details)
		{
			LogAssert.AreEqual(ChatSystem.ReportCommandParse.Complete, ChatSystem.ParseReportCommand(text, out string parsedTarget, out string parsedDetails), $"'{text}' names a player and says what happened");
			LogAssert.AreEqual(target, parsedTarget, $"target of '{text}'");
			LogAssert.AreEqual(details, parsedDetails, $"details of '{text}'");
		}

		[Test]
		public void AQuotedName_IsReportedWhole()
		{
			AssertComplete("\"Aragorn of Arnor\" spamming trade", "Aragorn of Arnor", "spamming trade");
			AssertComplete("  \"Aragorn  of Arnor\"   botting  ", "Aragorn of Arnor", "botting");
			AssertComplete("\"Bob\" griefing", "Bob", "griefing");
		}

		[Test]
		public void AnUnquotedName_IsStillOneWord_AsForATell()
		{
			AssertComplete("Bob he was botting", "Bob", "he was botting");
			AssertComplete("Bob   botting", "Bob", "botting");

			/* Never matched against longer online names: who a report is filed against must not
			 * depend on who happens to be logged in. The reply names "Aragorn", so the reporter
			 * sees the misread at once. */
			AssertComplete("Aragorn of Arnor spamming", "Aragorn", "of Arnor spamming");
		}

		[Test]
		public void ANameWithNothingSaid_AsksWhatHappened()
		{
			foreach (string text in new[] { "Bob", "  Bob  ", "\"Aragorn of Arnor\"", "\"Aragorn of Arnor\"   " })
			{
				LogAssert.AreEqual(ChatSystem.ReportCommandParse.NoDetails, ChatSystem.ParseReportCommand(text, out string target, out string details), $"'{text}' names a player and nothing else");
				LogAssert.IsTrue(target.Length > 0, $"'{text}' still reads a target");
				LogAssert.AreEqual(string.Empty, details, $"'{text}' has no details");
			}
		}

		[Test]
		public void NoReadablePlayer_AsksForAName()
		{
			foreach (string text in new[] { null, "", "   ", "\"\" spamming", "\"   \" spamming", "\"Aragorn of Arnor spamming" })
			{
				LogAssert.AreEqual(ChatSystem.ReportCommandParse.NoTarget, ChatSystem.ParseReportCommand(text, out string target, out string details), $"'{text}' names nobody");
				LogAssert.AreEqual(string.Empty, target, $"'{text}' leaves no target");
				LogAssert.AreEqual(string.Empty, details, $"'{text}' leaves no details");
			}
		}

		[Test]
		public void TheHandler_UsesTheParse_AndItsHelpSaysHowToQuote()
		{
			string code = SourceScanPins.ReadCode(SupportCommandsPath);

			SourceScanPins.HoldsAndFires("OnSupportReportCommand", code,
				source =>
				{
					string body = SourceScanPins.Body(source, "private bool OnSupportReportCommand(");
					if (body == null)
					{
						return "the handler was not found";
					}
					if (!body.Contains("ParseReportCommand(msg.Text, out string targetName, out string details)"))
					{
						return "the handler does not read the target through ParseReportCommand";
					}
					return body.Contains("GetWordAndTrimmed") ? "the handler still takes the first word" : null;
				},
				SourceScanPins.Replace("ParseReportCommand(msg.Text, out string targetName, out string details)",
					"ParseReportCommand(ChatHelper.GetWordAndTrimmed(msg.Text, out _), out string targetName, out string details)"),
				"the first-word parse put back in front of the address rule");

			string help = SourceScanPins.Body(code, "private void RegisterSupportCommands()");
			LogAssert.IsNotNull(help, "RegisterSupportCommands was not found");
			int report = help.IndexOf("SetCommandHelp(\"/report\"", System.StringComparison.Ordinal);
			int next = help.IndexOf("SetCommandHelp(\"/bug\"", System.StringComparison.Ordinal);
			LogAssert.IsTrue(report >= 0 && next > report, "the /report help registration was not found");
			LogAssert.IsTrue(help.Substring(report, next - report).Contains("double quotes"),
				"the /report help must say how to name a player whose name has a space");
		}
	}
}
