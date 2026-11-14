using NUnit.Framework;
using FishMMO.Client;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs for printing the guild's message of the day into chat on login.
	/// </summary>
	/// <remarks>
	/// The server sends the text on every scene connect, so the rule under test is the one that
	/// keeps a zone change quiet: once per login, and again only when the text changes.
	/// </remarks>
	[TestFixture]
	public class GuildMessageOfTheDayChatTests
	{
		[Test]
		public void TheFirstCopyAfterLoginIsAnnounced()
		{
			GuildMessageOfTheDayGate gate = new GuildMessageOfTheDayGate();

			LogAssert.IsTrue(gate.ShouldAnnounce(7, 3, "  Raid at eight  ", out string text), "the login copy prints");
			LogAssert.AreEqual("Raid at eight", text, "printed trimmed");
		}

		[Test]
		public void AZoneChangeRepeatsNothing()
		{
			GuildMessageOfTheDayGate gate = new GuildMessageOfTheDayGate();
			gate.ShouldAnnounce(7, 3, "Raid at eight", out _);

			LogAssert.IsFalse(gate.ShouldAnnounce(7, 3, "Raid at eight", out _), "the same text on the next scene is not repeated");
		}

		[Test]
		public void AnEditIsAnnounced()
		{
			GuildMessageOfTheDayGate gate = new GuildMessageOfTheDayGate();
			gate.ShouldAnnounce(7, 3, "Raid at eight", out _);

			LogAssert.IsTrue(gate.ShouldAnnounce(7, 3, "Raid moved to nine", out string text), "a changed message prints");
			LogAssert.AreEqual("Raid moved to nine", text);
		}

		[Test]
		public void AnEmptyMessageIsNeverPrinted_ButOneSetLaterIs()
		{
			GuildMessageOfTheDayGate gate = new GuildMessageOfTheDayGate();

			LogAssert.IsFalse(gate.ShouldAnnounce(7, 3, "", out _), "no message, no line");
			LogAssert.IsFalse(gate.ShouldAnnounce(7, 3, null, out _), "null is empty");
			LogAssert.IsFalse(gate.ShouldAnnounce(7, 3, "   ", out _), "whitespace is empty");
			LogAssert.IsTrue(gate.ShouldAnnounce(7, 3, "Welcome", out _), "set while online, printed");
		}

		[Test]
		public void ANoGuildCopyIsNeverPrinted()
		{
			GuildMessageOfTheDayGate gate = new GuildMessageOfTheDayGate();

			LogAssert.IsFalse(gate.ShouldAnnounce(7, 0, "Welcome", out _), "guild 0 is no guild");
		}

		[Test]
		public void QuittingToLoginAnnouncesItAgain()
		{
			GuildMessageOfTheDayGate gate = new GuildMessageOfTheDayGate();
			gate.ShouldAnnounce(7, 3, "Raid at eight", out _);

			gate.Reset();

			LogAssert.IsTrue(gate.ShouldAnnounce(7, 3, "Raid at eight", out _), "a fresh login prints the same text again");
		}

		[Test]
		public void ADifferentCharacterOrGuildIsAnnounced()
		{
			GuildMessageOfTheDayGate gate = new GuildMessageOfTheDayGate();
			gate.ShouldAnnounce(7, 3, "Hello", out _);

			LogAssert.IsTrue(gate.ShouldAnnounce(8, 3, "Hello", out _), "another character");
			LogAssert.IsTrue(gate.ShouldAnnounce(8, 4, "Hello", out _), "another guild");
		}
	}
}
