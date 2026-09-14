using System;
using System.Collections.Generic;
using NUnit.Framework;
using FishMMO.Auth.Core;
using FishMMO.Client;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The staff console's argument spec grammar and the chat line it composes.
	/// </summary>
	/// <remarks>
	/// The command names used here are neutral placeholders: the console knows the grammar, never
	/// a real command, and these tests hold it to that.
	/// </remarks>
	[TestFixture]
	public class StaffCommandLineTests
	{
		private static StaffCommandEntry Entry(string arguments, string command = "/cmd", string name = "sub")
		{
			return new StaffCommandEntry
			{
				Command = command,
				Name = name,
				Category = "Test",
				Summary = "A test command.",
				Arguments = arguments,
			};
		}

		private static List<StaffArgument> Parse(string spec)
		{
			bool ok = StaffCommandLine.TryParseSpec(spec, out List<StaffArgument> arguments, out string error);
			LogAssert.IsTrue(ok, $"'{spec}' must parse; it failed with: {error}");
			return arguments;
		}

		private static string Compose(string spec, params string[] values)
		{
			StaffCommandEntry entry = Entry(spec);
			bool ok = StaffCommandLine.TryCompose(entry, Parse(spec), values, out string line, out string error);
			LogAssert.IsTrue(ok, $"'{spec}' with [{string.Join("|", values)}] must compose; it failed with: {error}");
			return line;
		}

		private static string Refusal(string spec, params string[] values)
		{
			bool ok = StaffCommandLine.TryCompose(Entry(spec), Parse(spec), values, out _, out string error);
			LogAssert.IsFalse(ok, $"'{spec}' with [{string.Join("|", values)}] must be refused");
			LogAssert.IsFalse(string.IsNullOrEmpty(error), "a refusal must say why");
			return error;
		}

		private static void AssertSpecRefused(string spec)
		{
			bool ok = StaffCommandLine.TryParseSpec(spec, out _, out string error);
			LogAssert.IsFalse(ok, $"'{spec}' must not parse");
			LogAssert.IsFalse(string.IsNullOrEmpty(error), $"'{spec}' must say why it did not parse");
		}

		// ── Parsing ─────────────────────────────────────────────────────────────────────────

		[Test]
		public void ASpec_ReadsLabelsKindsChoicesAndOptionality()
		{
			List<StaffArgument> arguments = Parse("character:Character?;amount:Integer;mode:Choice=on,off?;reason:Text");

			LogAssert.AreEqual(4, arguments.Count, "four entries");

			LogAssert.AreEqual("character", arguments[0].Label, "first label");
			LogAssert.AreEqual(StaffArgumentKind.Character, arguments[0].Kind, "first kind");
			LogAssert.IsTrue(arguments[0].Optional, "a trailing ? makes it optional");

			LogAssert.AreEqual(StaffArgumentKind.Integer, arguments[1].Kind, "second kind");
			LogAssert.IsFalse(arguments[1].Optional, "no ? means required");

			LogAssert.AreEqual(StaffArgumentKind.Choice, arguments[2].Kind, "third kind");
			LogAssert.AreEqual(2, arguments[2].Choices.Count, "the choice words");
			LogAssert.AreEqual("on", arguments[2].Choices[0], "first choice word");
			LogAssert.AreEqual("off", arguments[2].Choices[1], "the ? after the word list is optionality, not part of the last word");
			LogAssert.IsTrue(arguments[2].Optional, "optional choice");

			LogAssert.AreEqual(StaffArgumentKind.Text, arguments[3].Kind, "fourth kind");
		}

		[Test]
		public void ABlankSpec_IsACommandWithNoArguments()
		{
			LogAssert.AreEqual(0, Parse(null).Count, "null");
			LogAssert.AreEqual(0, Parse("   ").Count, "whitespace");
			LogAssert.AreEqual("/cmd sub", Compose(""), "the line is the command alone");
		}

		[Test]
		public void KindNames_AreCaseInsensitive_AndATrailingSeparatorIsTolerated()
		{
			List<StaffArgument> arguments = Parse("a:word; b:DURATION ;");
			LogAssert.AreEqual(2, arguments.Count, "two entries, the empty tail skipped");
			LogAssert.AreEqual(StaffArgumentKind.Word, arguments[0].Kind, "lower-case kind");
			LogAssert.AreEqual(StaffArgumentKind.Duration, arguments[1].Kind, "upper-case kind, trimmed");
		}

		[Test]
		public void MalformedSpecs_AreRefused()
		{
			AssertSpecRefused("nokind");
			AssertSpecRefused(":Word");
			AssertSpecRefused("a:Banana");
			AssertSpecRefused("a:4");
			AssertSpecRefused("a:Choice");
			AssertSpecRefused("a:Choice=");
			AssertSpecRefused("a:Word=x,y");
			AssertSpecRefused("a:Choice=two words,x");
		}

		[Test]
		public void Text_MustBeTheLastArgument()
		{
			AssertSpecRefused("reason:Text;target:Character");
		}

		// ── Composing ───────────────────────────────────────────────────────────────────────

		[Test]
		public void TheLine_IsCommandNameThenValuesInOrder()
		{
			LogAssert.AreEqual("/cmd sub Bob 12", Compose("character:Character;amount:Integer", "Bob", "12"), "composed line");
			LogAssert.AreEqual("/cmd sub Bob 12", Compose("character:Character;amount:Integer", "  Bob ", " 12"), "values are trimmed");
		}

		[Test]
		public void AnEntryWithNoName_SendsTheCommandAlone()
		{
			StaffCommandEntry entry = Entry("a:Word", "/cmd", "");
			LogAssert.IsTrue(StaffCommandLine.TryCompose(entry, Parse("a:Word"), new[] { "x" }, out string line, out string error), error);
			LogAssert.AreEqual("/cmd x", line, "no doubled space where the name would be");
		}

		[Test]
		public void ABlankOptional_IsOmitted_AndAFilledArgumentAfterItIsStillSent()
		{
			LogAssert.AreEqual("/cmd sub 500", Compose("character:Character?;amount:Integer", "", "500"),
				"the server tells a leading number from a name by shape, so the blank one is simply left out");
			LogAssert.AreEqual("/cmd sub Bob 500", Compose("character:Character?;amount:Integer", "Bob", "500"), "filled optional kept");
			LogAssert.AreEqual("/cmd sub", Compose("a:Word?;b:Word?"), "all blank optionals leave the bare command");
			LogAssert.AreEqual("/cmd sub y", Compose("a:Word?;b:Word?", null, "y"), "a missing value is blank");
		}

		[Test]
		public void ABlankRequiredArgument_IsRefused_AndNamed()
		{
			string error = Refusal("character:Character?;amount:Integer", "Bob", "");
			LogAssert.IsTrue(error.Contains("amount"), $"the refusal must name the argument; it said: {error}");
		}

		[Test]
		public void Text_ConsumesTheRestOfTheLine()
		{
			LogAssert.AreEqual("/cmd sub Bob being rude in chat",
				Compose("character:Character;reason:Text", "Bob", "  being rude in chat  "), "spaces inside text are kept");
			Refusal("reason:Text", "two\nlines");
		}

		[Test]
		public void SingleWordKinds_RefuseSpaces()
		{
			Refusal("character:Character", "Bo b");
			Refusal("word:Word", "a b");
			Refusal("account:Account", "some account");
		}

		[Test]
		public void AChoice_TakesOnlyItsWords_InTheirListedSpelling()
		{
			LogAssert.AreEqual("/cmd sub on", Compose("mode:Choice=on,off", "ON"), "matched case-insensitively, sent as listed");
			string error = Refusal("mode:Choice=on,off", "maybe");
			LogAssert.IsTrue(error.Contains("on") && error.Contains("off"), $"the refusal lists the words; it said: {error}");
		}

		[Test]
		public void Numbers_TicketsAndDurations_AreValidated()
		{
			LogAssert.AreEqual("/cmd sub -5", Compose("n:Integer", "-5"), "negative whole number");
			Refusal("n:Integer", "1.5");
			Refusal("n:Integer", "ten");

			LogAssert.AreEqual("/cmd sub 2.5", Compose("n:Number", "2.5"), "decimal");
			Refusal("n:Number", "2,5");
			Refusal("n:Number", "NaN");

			LogAssert.AreEqual("/cmd sub 42", Compose("t:Ticket", "42"), "ticket number");
			Refusal("t:Ticket", "0");
			Refusal("t:Ticket", "-3");
			Refusal("t:Ticket", "#42");

			LogAssert.AreEqual("/cmd sub 30m", Compose("d:Duration", "30m"), "minutes");
			LogAssert.AreEqual("/cmd sub 7d", Compose("d:Duration", "7d"), "days");
			LogAssert.AreEqual("/cmd sub 90", Compose("d:Duration", "90"), "a bare number is a duration");
			Refusal("d:Duration", "30x");
			Refusal("d:Duration", "m30");
			Refusal("d:Duration", "1.5h");
		}

		[Test]
		public void ALineLongerThanAChatMessage_IsRefused_ButStillShown()
		{
			const string spec = "reason:Text";
			int room = ChatBroadcast.MaxTextLength - "/cmd sub ".Length;

			string exactly = new string('a', room);
			LogAssert.AreEqual(ChatBroadcast.MaxTextLength, Compose(spec, exactly).Length, "a line of exactly the limit is sent");

			string over = new string('a', room + 1);
			bool ok = StaffCommandLine.TryCompose(Entry(spec), Parse(spec), new[] { over }, out string line, out string error);
			LogAssert.IsFalse(ok, "one character over is refused");
			LogAssert.IsNotNull(line, "the too-long line is still returned so the console can show it");
			LogAssert.AreEqual(ChatBroadcast.MaxTextLength + 1, line.Length, "and it is the whole line");
			LogAssert.IsTrue(error.Contains(ChatBroadcast.MaxTextLength.ToString()), $"the refusal states the limit; it said: {error}");
		}

		[Test]
		public void AnEntryWithNoCommand_IsRefused()
		{
			bool ok = StaffCommandLine.TryCompose(Entry("", "", "sub"), new List<StaffArgument>(), null, out _, out string error);
			LogAssert.IsFalse(ok, "nothing to send");
			LogAssert.IsFalse(string.IsNullOrEmpty(error), "and it says so");
		}

		// ── Formatting ──────────────────────────────────────────────────────────────────────

		[Test]
		public void AccessLevels_ReadAsTheirEnumNames()
		{
			LogAssert.AreEqual("Banned", StaffConsoleFormat.AccessLevelName((byte)AccessLevel.Banned), "0");
			LogAssert.AreEqual("Player", StaffConsoleFormat.AccessLevelName((byte)AccessLevel.Player), "1");
			LogAssert.AreEqual("GameMaster", StaffConsoleFormat.AccessLevelName((byte)AccessLevel.GameMaster), "2");
			LogAssert.AreEqual("Admin", StaffConsoleFormat.AccessLevelName((byte)AccessLevel.Admin), "3");
			LogAssert.AreEqual("Level 9", StaffConsoleFormat.AccessLevelName(9), "an unknown value is not silently mislabelled");
		}

		[Test]
		public void Ages_ReadInTheShortestUsefulUnit()
		{
			long now = new DateTime(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc).Ticks;
			LogAssert.AreEqual("just now", StaffConsoleFormat.FormatAge(now - TimeSpan.TicksPerSecond * 20, now), "seconds");
			LogAssert.AreEqual("just now", StaffConsoleFormat.FormatAge(now + TimeSpan.TicksPerMinute, now), "clock skew");
			LogAssert.AreEqual("5m ago", StaffConsoleFormat.FormatAge(now - TimeSpan.TicksPerMinute * 5, now), "minutes");
			LogAssert.AreEqual("3h ago", StaffConsoleFormat.FormatAge(now - TimeSpan.TicksPerHour * 3, now), "hours");
			LogAssert.AreEqual("2d ago", StaffConsoleFormat.FormatAge(now - TimeSpan.TicksPerDay * 2, now), "days");
			LogAssert.AreEqual("unknown", StaffConsoleFormat.FormatAge(0, now), "no timestamp");
		}
	}
}
