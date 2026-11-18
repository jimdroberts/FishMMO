using System;
using System.Reflection;
using NUnit.Framework;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// <see cref="ChatReadWindow"/>, the chat-table read window the Discord relay keeps (hot-path
	/// audit H5, the relay's half).
	/// </summary>
	/// <remarks>
	/// The relay paged the chat table by <c>id &gt; last id seen</c>. IDs are taken at the INSERT
	/// and rows become visible at the commit, so a row that committed after a higher ID had been
	/// read was skipped for good; and the read had no bound. The window lives in the database
	/// layer because the bot cannot reference the game's code. The SQL half — a first read that
	/// replays nothing, a row stamped first and committed last read once it commits, Discord rows
	/// never returned, a bounded read that catches up over polls with nothing read twice — was run
	/// against a throwaway PostgreSQL; these pin the rule.
	/// </remarks>
	[TestFixture]
	public class ChatReadWindowTests
	{
		private static readonly DateTime T0 = new DateTime(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);
		private static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

		[Test]
		public void TheFirstReadStartsAtNowAndSetsAFloor()
		{
			var window = new ChatReadWindow(Window);
			LogAssert.IsFalse(window.Watermark.HasValue, "before the first read there is no start: the database's own now is used");

			window.CompleteRead(T0, true, null);
			LogAssert.AreEqual<DateTime?>(T0, window.Watermark, "the first read settles at its own start, not a window earlier");

			window.CompleteRead(T0.AddSeconds(5), true, null);
			LogAssert.AreEqual<DateTime?>(T0, window.Watermark, "inside the first window the floor holds: nothing before the start is ever read");

			window.CompleteRead(T0.AddSeconds(25), true, null);
			LogAssert.AreEqual<DateTime?>(T0.AddSeconds(15), window.Watermark, "then the start trails one commit window behind the last read");
		}

		[Test]
		public void ARowCommittedLateIsStillInsideTheNextRead()
		{
			var window = new ChatReadWindow(Window);
			window.CompleteRead(T0, true, null);

			// B (stamped 100.1) is read at 100.2; A (stamped 100.0) commits at 100.3.
			LogAssert.IsTrue(window.Admit(2, T0.AddSeconds(100.1)), "B");
			window.CompleteRead(T0.AddSeconds(100.2), true, T0.AddSeconds(100.1));
			LogAssert.IsTrue(window.Watermark.Value <= T0.AddSeconds(100.0), "the next read still starts before A's stamp");
			CollectionAssert.AreEqual(new long[] { 2 }, window.SnapshotSeenIds(), "and skips B, already handled");

			LogAssert.IsTrue(window.Admit(1, T0.AddSeconds(100.0)), "A is relayed when it shows up");
			LogAssert.IsFalse(window.Admit(2, T0.AddSeconds(100.1)), "B is not relayed twice");
		}

		[Test]
		public void ARowIsForgottenOnlyOnceNoReadCanReturnIt()
		{
			var window = new ChatReadWindow(Window);
			window.CompleteRead(T0, true, null);
			window.Admit(1, T0.AddSeconds(1));
			window.Admit(2, T0.AddSeconds(20));
			window.CompleteRead(T0.AddSeconds(21), true, T0.AddSeconds(20));
			LogAssert.AreEqual(1, window.SeenCount, "row 1 is behind the watermark (11 s) and can never come back; row 2 can");
			CollectionAssert.AreEqual(new long[] { 2 }, window.SnapshotSeenIds(), "only row 2 is sent to be skipped");
		}

		[Test]
		public void AReadStoppedAtItsPageLimitSettlesOnlyAsFarAsItGot()
		{
			var window = new ChatReadWindow(Window);
			window.CompleteRead(T0, true, null);
			window.CompleteRead(T0.AddSeconds(300), drained: false, lastRowTimeUtc: T0.AddSeconds(40));
			LogAssert.AreEqual<DateTime?>(T0.AddSeconds(30), window.Watermark, "a window behind the last row read, not behind the read's start");

			window.CompleteRead(T0.AddSeconds(301), drained: false, lastRowTimeUtc: T0.AddSeconds(35));
			LogAssert.AreEqual<DateTime?>(T0.AddSeconds(30), window.Watermark, "and it never moves backwards");
		}

		[Test]
		public void TheRelayReadSkipsWhatItHasSeenAndNeverReadsBackDiscord()
		{
			MethodInfo build = typeof(ChatService).GetMethod("BuildRelayFilterSql", BindingFlags.NonPublic | BindingFlags.Static);
			LogAssert.IsNotNull(build, "ChatService.BuildRelayFilterSql exists");
			string filter = (string)build.Invoke(null, null);
			StringAssert.Contains("c.time_created >= {0}", filter, "reads from the window's start, inclusive");
			StringAssert.Contains("c.id <> ALL({1}::bigint[])", filter, "skips rows already handled");
			StringAssert.Contains($"c.channel <> {(byte)FishMMO.Database.Data.Enums.ChatChannel.Discord}", filter, "never returns a line bridged in from Discord");
			StringAssert.DoesNotContain("c.id >", filter, "no ID cursor: IDs are taken at the INSERT, not the commit");
		}

		[Test]
		public void ResetStartsAgainFromNow()
		{
			var window = new ChatReadWindow(Window);
			window.CompleteRead(T0, true, null);
			window.Admit(1, T0.AddSeconds(1));
			window.Reset();
			LogAssert.IsFalse(window.Watermark.HasValue, "no start");
			LogAssert.AreEqual(0, window.SeenCount, "nothing held");
			LogAssert.IsFalse(window.LastReadStartedUtc.HasValue, "no read");
		}
	}
}
