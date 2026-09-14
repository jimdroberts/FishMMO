using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using FishMMO.Database.Data;
using FishMMO.Server.Implementation.World.SceneServer;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The text rules behind <c>/gm</c> and <c>/admin</c>, and the rule that combines a character's
	/// mute with its account's.
	/// </summary>
	/// <remarks>
	/// Every case here is a way an operator command could quietly do the wrong thing: a duration
	/// read in the wrong unit, a mistyped name falling back to the operator's own character on an
	/// economy command, a lapsed mute still silencing somebody, or a usage line describing a
	/// command differently from the form the staff console builds.
	/// </remarks>
	[TestFixture]
	public class OperatorCommandParsingTests
	{
		// ── Durations ─────────────────────────────────────────────────────────

		[Test]
		public void ABareNumberIsMinutes()
		{
			LogAssert.IsTrue(OperatorCommandParsing.TryParseDuration("90", out TimeSpan duration));
			LogAssert.AreEqual(TimeSpan.FromMinutes(90), duration);
		}

		[Test]
		public void EveryUnitParses()
		{
			var cases = new Dictionary<string, TimeSpan>()
			{
				{ "45s", TimeSpan.FromSeconds(45) },
				{ "30m", TimeSpan.FromMinutes(30) },
				{ "2H", TimeSpan.FromHours(2) },
				{ "7d", TimeSpan.FromDays(7) },
				{ "2w", TimeSpan.FromDays(14) },
			};
			foreach (var pair in cases)
			{
				LogAssert.IsTrue(OperatorCommandParsing.TryParseDuration(pair.Key, out TimeSpan duration), $"'{pair.Key}' must parse");
				LogAssert.AreEqual(pair.Value, duration, $"'{pair.Key}'");
			}
		}

		[Test]
		public void NothingZeroNegativeOrMalformedIsADuration()
		{
			/* Zero is refused rather than meaning "none" or "forever": either reading of a typo is
			 * worse than asking again. */
			foreach (string text in new[] { null, "", " ", "0", "0m", "-5m", "m", "5x", "1.5h", "5 m", "perm", "1234567" })
			{
				LogAssert.IsFalse(OperatorCommandParsing.TryParseDuration(text, out _), $"'{text}' must not parse");
			}
		}

		[Test]
		public void ADurationPastTheCeilingIsRefused()
		{
			LogAssert.IsFalse(OperatorCommandParsing.TryParseDuration("999999w", out _));
			LogAssert.IsTrue(OperatorCommandParsing.TryParseDuration("3650d", out _), "the ceiling itself is allowed");
			LogAssert.IsFalse(OperatorCommandParsing.TryParseDuration("3651d", out _));
		}

		[Test]
		public void PermanentIsOnlyTheTwoWords()
		{
			LogAssert.IsTrue(OperatorCommandParsing.IsPermanent("perm"));
			LogAssert.IsTrue(OperatorCommandParsing.IsPermanent("PERMANENT"));
			LogAssert.IsFalse(OperatorCommandParsing.IsPermanent("forever"));
			LogAssert.IsFalse(OperatorCommandParsing.IsPermanent("0"));
		}

		[Test]
		public void DurationsAreDescribedInTheUnitsTheyAreTyped()
		{
			LogAssert.AreEqual("30m", OperatorCommandParsing.DescribeDuration(TimeSpan.FromMinutes(30)));
			LogAssert.AreEqual("2h", OperatorCommandParsing.DescribeDuration(TimeSpan.FromHours(2)));
			LogAssert.AreEqual("2h 5m", OperatorCommandParsing.DescribeDuration(TimeSpan.FromMinutes(125)));
			LogAssert.AreEqual("7d", OperatorCommandParsing.DescribeDuration(TimeSpan.FromDays(7)));
			LogAssert.AreEqual("1d 3h", OperatorCommandParsing.DescribeDuration(TimeSpan.FromHours(27)));
		}

		[Test]
		public void TheGameMasterCeilingIsThirtyDays()
		{
			LogAssert.AreEqual(TimeSpan.FromDays(30), OperatorCommandParsing.GameMasterMaximumDuration);
		}

		// ── Optional leading character ────────────────────────────────────────

		[Test]
		public void NothingOrANumberFirstMeansTheCaller()
		{
			LogAssert.IsFalse(OperatorCommandParsing.TrySplitLeadingCharacter("", out string name, out string rest));
			LogAssert.IsNull(name);
			LogAssert.AreEqual("", rest);

			LogAssert.IsFalse(OperatorCommandParsing.TrySplitLeadingCharacter("500", out name, out rest));
			LogAssert.IsNull(name);
			LogAssert.AreEqual("500", rest);

			LogAssert.IsFalse(OperatorCommandParsing.TrySplitLeadingCharacter("5 Test Tunic", out name, out rest));
			LogAssert.AreEqual("5 Test Tunic", rest, "the item name must survive whole");
		}

		[Test]
		public void AWordFirstIsAlwaysACharacterName()
		{
			/* Never "a name if one is online, else the caller": a mistyped name on /admin setgold
			 * must be refused, not quietly paid to the administrator. Resolution against the online
			 * mapping happens after this split, and a miss there is reported. */
			LogAssert.IsTrue(OperatorCommandParsing.TrySplitLeadingCharacter("Bob 500", out string name, out string rest));
			LogAssert.AreEqual("Bob", name);
			LogAssert.AreEqual("500", rest);

			LogAssert.IsTrue(OperatorCommandParsing.TrySplitLeadingCharacter("Bobb", out name, out rest));
			LogAssert.AreEqual("Bobb", name);
			LogAssert.AreEqual("", rest);
		}

		[Test]
		public void TicketNumbersAcceptAHash()
		{
			LogAssert.IsTrue(OperatorCommandParsing.TryParseTicketID("#42", out long id));
			LogAssert.AreEqual(42L, id);
			LogAssert.IsTrue(OperatorCommandParsing.TryParseTicketID("42", out id));
			LogAssert.IsFalse(OperatorCommandParsing.TryParseTicketID("0", out _));
			LogAssert.IsFalse(OperatorCommandParsing.TryParseTicketID("-1", out _));
			LogAssert.IsFalse(OperatorCommandParsing.TryParseTicketID("#", out _));
		}

		// ── Specs and usage ───────────────────────────────────────────────────

		[Test]
		public void ASpecParsesKindsChoicesAndOptionality()
		{
			LogAssert.IsTrue(OperatorCommandParsing.TryParseSpec(
				"character:Character?;level:Choice=Banned,Player;reason:Text", out var arguments, out string error), error);

			LogAssert.AreEqual(3, arguments.Count);
			LogAssert.AreEqual(StaffArgumentKind.Character, arguments[0].Kind);
			LogAssert.IsTrue(arguments[0].Optional);
			LogAssert.AreEqual(StaffArgumentKind.Choice, arguments[1].Kind);
			LogAssert.AreEqual("Banned,Player", string.Join(",", arguments[1].Choices));
			LogAssert.IsFalse(arguments[1].Optional);
			LogAssert.AreEqual(StaffArgumentKind.Text, arguments[2].Kind);
		}

		[Test]
		public void AMalformedSpecIsRefused()
		{
			foreach (string spec in new[] { "character", "x:Nope", "reason:Text;amount:Integer", "pick:Choice", "n:Integer=1,2", ":Word" })
			{
				LogAssert.IsFalse(OperatorCommandParsing.TryParseSpec(spec, out _, out _), $"'{spec}' must be refused");
			}
		}

		[Test]
		public void UsageIsDerivedFromTheSpec()
		{
			LogAssert.AreEqual("/gm mute <character> <duration> [reason]",
				OperatorCommandParsing.FormatUsage("/gm", "mute", "character:Character;duration:Duration;reason:Text?"));
			LogAssert.AreEqual("/admin access <account> <Banned|Player>",
				OperatorCommandParsing.FormatUsage("/admin", "access", "account:Account;level:Choice=Banned,Player"));
			LogAssert.AreEqual("/gm who", OperatorCommandParsing.FormatUsage("/gm", "who", ""));
		}

		[Test]
		public void PackedLinesNeverExceedTheLimitAndLoseNothing()
		{
			var names = Enumerable.Range(0, 60).Select(i => "Character" + i).ToList();
			List<string> lines = OperatorCommandParsing.PackLines(names, "Players: ", ", ", 120).ToList();

			LogAssert.IsTrue(lines.Count > 1, "sixty names cannot fit one line");
			foreach (string line in lines)
			{
				LogAssert.IsTrue(line.Length <= 120, $"'{line}' is {line.Length} long");
				LogAssert.IsTrue(line.StartsWith("Players: ", StringComparison.Ordinal));
			}
			string joined = string.Join(", ", lines.Select(l => l.Substring("Players: ".Length)));
			LogAssert.AreEqual(string.Join(", ", names), joined);
		}

		// ── Mute combination ──────────────────────────────────────────────────

		private static readonly DateTime Now = new DateTime(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

		[Test]
		public void NoMuteIsZero()
		{
			long until = ChatMutePolicy.ResolveUntilTicks(default(CharacterChatMuteState), Now, out string reason);
			LogAssert.AreEqual(0L, until);
			LogAssert.IsNull(reason);
		}

		[Test]
		public void ALapsedMuteNoLongerApplies()
		{
			var state = new CharacterChatMuteState(
				new ChatMuteData(true, Now.AddMinutes(-1), "gm", "old"),
				new ChatMuteData(true, Now, "gm", "exactly now"));
			LogAssert.AreEqual(0L, ChatMutePolicy.ResolveUntilTicks(state, Now, out _), "a mute ending now has ended");
		}

		[Test]
		public void TheLaterMuteWinsWhicheverRowItIsOn()
		{
			var characterLater = new CharacterChatMuteState(
				new ChatMuteData(true, Now.AddHours(1), "gm", "account"),
				new ChatMuteData(true, Now.AddHours(5), "gm", "character"));
			LogAssert.AreEqual(Now.AddHours(5).Ticks, ChatMutePolicy.ResolveUntilTicks(characterLater, Now, out string reason));
			LogAssert.AreEqual("character", reason);

			var accountLater = new CharacterChatMuteState(
				new ChatMuteData(true, Now.AddDays(2), "gm", "account"),
				new ChatMuteData(true, Now.AddHours(5), "gm", "character"));
			LogAssert.AreEqual(Now.AddDays(2).Ticks, ChatMutePolicy.ResolveUntilTicks(accountLater, Now, out reason));
			LogAssert.AreEqual("account", reason);
		}

		[Test]
		public void AMuteWithNoEndBeatsAnyEnd()
		{
			var state = new CharacterChatMuteState(
				new ChatMuteData(true, null, "admin", "forever"),
				new ChatMuteData(true, Now.AddYears(5), "gm", "long"));
			LogAssert.AreEqual(ChatMutePolicy.NoEnd, ChatMutePolicy.ResolveUntilTicks(state, Now, out string reason));
			LogAssert.AreEqual("forever", reason);
		}

		[Test]
		public void AnUnsetFlagIgnoresAStaleEnd()
		{
			// Unmuting clears the columns, but the flag alone must decide even if they were left behind.
			var state = new CharacterChatMuteState(
				new ChatMuteData(false, Now.AddDays(1), "gm", "stale"),
				default);
			LogAssert.AreEqual(0L, ChatMutePolicy.ResolveUntilTicks(state, Now, out _));
		}

		[Test]
		public void TheMutedPlayerIsToldHowLongWhyAndWhereToGo()
		{
			string line = ChatMutePolicy.DescribeForPlayer(Now.AddMinutes(30).Ticks, "spam", Now.Ticks);
			StringAssert.Contains("30m", line);
			StringAssert.Contains("spam", line);
			StringAssert.Contains("/helpme", line);
			LogAssert.IsTrue(line.Length <= ChatBroadcast.MaxTextLength, "the client discards longer lines");

			string forever = ChatMutePolicy.DescribeForPlayer(ChatMutePolicy.NoEnd, new string('x', 400), Now.Ticks);
			LogAssert.IsTrue(forever.Length <= ChatBroadcast.MaxTextLength, "a long reason must not push the line past the limit");
		}
	}
}
