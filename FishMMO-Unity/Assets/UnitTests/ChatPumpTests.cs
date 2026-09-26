using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Server.Implementation.World.SceneServer;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The cross-server chat pump and the chat rate gate (hot-path audit H5, H6, M17, L2, L3).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>H5.</b> The pump paged the chat table by a strict <c>(time, id)</c> cursor, and a row
	/// stamped before another but committed after it was behind the cursor by the time it became
	/// visible: lost for good on every other scene server. <see cref="ChatPumpCursor"/> reads a
	/// commit window behind what it has settled and dedupes by row ID. The database half (the
	/// DB-clock INSERT, the relevance filter, and a two-writer out-of-order commit read before and
	/// after) was run against a throwaway PostgreSQL; these pin the cursor rule and the SQL text.
	/// </para>
	/// <para>
	/// <b>L2.</b> The per-sender bucket was charged when the incoming queue drained, so the queue's
	/// global cap kicked whoever arrived next. It is charged at the door now, and the cap drops.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class ChatPumpTests
	{
		private const string ChatDirectory = "Assets/Scripts/Server/Implementation/World/SceneServer/Chat";
		private static readonly DateTime T0 = new DateTime(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);
		private static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

		private static ChatPumpCursor Started(params ChatPumpKey[] keys)
		{
			var cursor = new ChatPumpCursor(Window);
			cursor.BeginRead(new HashSet<ChatPumpKey>(keys));
			cursor.CompleteRead(T0, true, null);
			return cursor;
		}

		private static void Read(ChatPumpCursor cursor, DateTime readStarted, ICollection<ChatPumpKey> keys, bool drained = true, DateTime? lastRow = null)
		{
			cursor.BeginRead(keys);
			cursor.CompleteRead(readStarted, drained, lastRow);
		}

		// ── The window ────────────────────────────────────────────────────────

		[Test]
		public void TheFirstReadSetsAFloorSoNothingBeforeTheServerStartedIsReplayed()
		{
			ChatPumpCursor cursor = Started(ChatPumpKey.World(1));
			LogAssert.AreEqual<DateTime?>(T0, cursor.Watermark, "the first read starts the window at its own start, not a window earlier");

			Read(cursor, T0.AddSeconds(2), new[] { ChatPumpKey.World(1) });
			LogAssert.AreEqual<DateTime?>(T0, cursor.Watermark, "inside the first window the floor holds the start where the server began reading");
		}

		[Test]
		public void TheWatermarkTrailsTheReadByTheCommitWindowAndNeverMovesBack()
		{
			ChatPumpCursor cursor = Started(ChatPumpKey.World(1));
			Read(cursor, T0.AddSeconds(30), new[] { ChatPumpKey.World(1) });
			LogAssert.AreEqual<DateTime?>(T0.AddSeconds(20), cursor.Watermark, "a drained read settles everything up to its start less the window");

			Read(cursor, T0.AddSeconds(25), new[] { ChatPumpKey.World(1) });
			LogAssert.AreEqual<DateTime?>(T0.AddSeconds(20), cursor.Watermark, "a read reporting an earlier clock must not pull the window back");
		}

		[Test]
		public void AReadStoppedAtItsPageLimitSettlesOnlyUpToItsLastRow()
		{
			ChatPumpCursor cursor = Started(ChatPumpKey.World(1));
			Read(cursor, T0.AddSeconds(100), new[] { ChatPumpKey.World(1) }, drained: false, lastRow: T0.AddSeconds(50));
			LogAssert.AreEqual<DateTime?>(T0.AddSeconds(40), cursor.Watermark, "a backlog is resumed a window behind its last row, not skipped to the read's start");
		}

		[Test]
		public void ARowCommittedOutOfStampOrderInsideTheWindowIsStillDelivered()
		{
			ChatPumpKey world = ChatPumpKey.World(1);
			ChatPumpCursor cursor = Started(world);

			// Read at T0+2 delivers B (stamped T0+1.1); A (stamped T0+1.0) has not committed.
			cursor.BeginRead(new[] { world });
			LogAssert.IsTrue(cursor.Admit(2, T0.AddSeconds(1.1), world), "B is delivered");
			cursor.CompleteRead(T0.AddSeconds(2), true, T0.AddSeconds(1.1));

			// A strict cursor would now start after T0+1.1. The window starts at or before T0+1.0.
			LogAssert.IsTrue(cursor.Watermark.Value <= T0.AddSeconds(1.0), "the next read still reaches back past A's stamp");
			CollectionAssert.Contains(cursor.SnapshotSeenIds(), 2L, "B is skipped by ID on the next read");

			cursor.BeginRead(new[] { world });
			LogAssert.IsTrue(cursor.Admit(1, T0.AddSeconds(1.0), world), "A, committed late, is delivered");
			LogAssert.IsFalse(cursor.Admit(2, T0.AddSeconds(1.1), world), "B is not delivered twice");
		}

		[Test]
		public void HandledRowsAreForgottenOnceTheWindowHasPassedThem()
		{
			ChatPumpKey world = ChatPumpKey.World(1);
			ChatPumpCursor cursor = Started(world);
			cursor.BeginRead(new[] { world });
			cursor.Admit(7, T0.AddSeconds(1), world);
			cursor.CompleteRead(T0.AddSeconds(2), true, T0.AddSeconds(1));
			LogAssert.AreEqual(1, cursor.SeenCount, "a handled row inside the window is remembered");

			Read(cursor, T0.AddSeconds(30), new[] { world });
			LogAssert.AreEqual(0, cursor.SeenCount, "behind the watermark it can never come back, so it is dropped");
		}

		// ── Keys that become relevant ─────────────────────────────────────────

		[Test]
		public void APlayerWhoArrivesIsNotReplayedTheWindowTheirLastServerAlreadyShowedThem()
		{
			ChatPumpKey world = ChatPumpKey.World(1);
			ChatPumpKey bob = ChatPumpKey.Tell("Bob");
			ChatPumpCursor cursor = Started(world);
			Read(cursor, T0.AddSeconds(20), new[] { world });
			Read(cursor, T0.AddSeconds(22), new[] { world });

			// Bob arrives between the read at T0+22 and the next one.
			cursor.BeginRead(new[] { world, bob });
			LogAssert.IsFalse(cursor.Admit(1, T0.AddSeconds(21), bob), "a whisper stamped before the previous read belonged to the server Bob came from");
			LogAssert.IsTrue(cursor.Admit(2, T0.AddSeconds(22.5), bob), "one stamped since the previous read is Bob's here, exactly as a strict cursor would have delivered it");
			LogAssert.IsTrue(cursor.Admit(3, T0.AddSeconds(15), world), "a key that was already relevant is not held back");
		}

		[Test]
		public void AKeyThatLeavesAndReturnsStartsAgain()
		{
			ChatPumpKey party = ChatPumpKey.Party(7);
			ChatPumpCursor cursor = Started(party);
			Read(cursor, T0.AddSeconds(20), new[] { party });
			Read(cursor, T0.AddSeconds(22), Array.Empty<ChatPumpKey>());
			LogAssert.AreEqual(0, cursor.KeyCount, "a key nobody here holds any more is forgotten");

			cursor.BeginRead(new[] { party });
			LogAssert.IsFalse(cursor.Admit(1, T0.AddSeconds(21), party), "its return is a new arrival");
		}

		[Test]
		public void ARowWhoseKeyTheReadDidNotAskForIsNotSuppressed()
		{
			ChatPumpCursor cursor = Started(ChatPumpKey.World(1));
			cursor.BeginRead(new[] { ChatPumpKey.World(1) });
			LogAssert.IsTrue(cursor.Admit(1, T0.AddSeconds(1), default), "losing a line is worse than a rare duplicate");
		}

		[Test]
		public void KeysParseTheWayTheSqlMatches()
		{
			LogAssert.AreEqual("7", ChatPumpKey.FirstWord("7 hello there"), "the first word, as split_part(message, ' ', 1)");
			LogAssert.AreEqual("bob", ChatPumpKey.FirstWord("bob"), "no space: the whole message");
			LogAssert.AreEqual(string.Empty, ChatPumpKey.FirstWord(null), "nothing: empty");
			LogAssert.IsTrue(ChatPumpKey.Tell("BoB").Equals(ChatPumpKey.Tell("bob")), "tell targets compare case-insensitively, as lower() does");
			LogAssert.IsTrue(FishMMO.Shared.ChatTellAddress.TryParseAddress("\"Aragorn of Arnor\" hello", out string quoted) && quoted == "Aragorn of Arnor",
				"a tell row's key is its address: the quoted name whole, as the SQL's substring takes it");
			LogAssert.IsTrue(FishMMO.Shared.ChatTellAddress.TryParseAddress("bob hello", out string single) && single == ChatPumpKey.FirstWord("bob hello"),
				"and an unquoted one is the first word, exactly as before");
			LogAssert.IsFalse(ChatPumpKey.Party(9).Equals(ChatPumpKey.Guild(9)), "a party and a guild with one ID are different keys");
		}

		// ── The SQL (static text, no database) ────────────────────────────────

		private static string InvokeStatic(string method, params object[] args)
		{
			Type service = typeof(FishMMO.Database.Npgsql.Services.ChatService);
			MethodInfo info = service.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static);
			LogAssert.IsNotNull(info, $"ChatService.{method} must exist");
			return (string)info.Invoke(null, args);
		}

		[Test]
		public void EveryChatRowIsStampedByTheDatabaseClockAndIsIdempotentPerRow()
		{
			string insert = InvokeStatic("BuildInsertSql", "public.chat");
			StringAssert.Contains("clock_timestamp() AT TIME ZONE 'UTC'", insert, "time_created comes from the database clock, never a writer's");
			StringAssert.Contains("ON CONFLICT (request_key) WHERE request_key IS NOT NULL DO NOTHING", insert, "a retried write lands once per row");
			StringAssert.DoesNotContain("{{", insert, "placeholders survive the interpolation as {n}");
			LogAssert.IsTrue(FishMMO.Database.Npgsql.Services.ChatService.PumpCommitWindowSeconds >= 5.0, "the commit window is a stall bound, not a hair trigger");
		}

		[Test]
		public void ThePumpSkipsWhatItHasSeenAndOnlyReadsWhatIsRelevantHere()
		{
			string filter = InvokeStatic("BuildPumpFilterSql");
			StringAssert.Contains("c.time_created >= {0}", filter, "reads from the window's start, inclusive");
			StringAssert.Contains("c.id <> ALL({1}::bigint[])", filter, "skips rows already handled");
			StringAssert.Contains("c.world_server_id = ANY({3}::bigint[])", filter, "World, Trade and Discord by world");
			StringAssert.Contains("split_part(c.message, ' ', 1) = ANY({4}::text[])", filter, "party lines by the party ID prefix");
			StringAssert.Contains("lower(COALESCE(substring(c.message from '^\"([^\"]+)\"'), split_part(c.message, ' ', 1))) = ANY({6}::text[])", filter,
				"tells by target, case-insensitively: the quoted name when the row starts with a quote, else the first word (O9)");
			StringAssert.DoesNotContain("lower(split_part(c.message, ' ', 1)) = ANY({6}", filter,
				"the first word alone is \"aragorn for a quoted name, and a whisper to a name with a space reaches nobody off-server");
		}

		// ── The rate gate (L2) ────────────────────────────────────────────────

		private static ChatRateGate.State Fresh(long now) => new ChatRateGate.State { IsFull = true, LastRefillTicks = now };

		[Test]
		public void TheBucketAdmitsItsBurstThenRefillsAtItsRate()
		{
			long now = T0.Ticks;
			ChatRateGate.State state = Fresh(now);
			for (int i = 0; i < 5; ++i)
			{
				LogAssert.IsTrue(ChatRateGate.TryCharge(ref state, now, 5, 1.0, 0.0), $"message {i + 1} of the burst of 5");
			}
			LogAssert.IsFalse(ChatRateGate.TryCharge(ref state, now, 5, 1.0, 0.0), "the sixth in the same instant is refused");

			now += TimeSpan.TicksPerSecond * 2;
			LogAssert.IsTrue(ChatRateGate.TryCharge(ref state, now, 5, 1.0, 0.0), "two seconds refill two tokens: one");
			LogAssert.IsTrue(ChatRateGate.TryCharge(ref state, now, 5, 1.0, 0.0), "two");
			LogAssert.IsFalse(ChatRateGate.TryCharge(ref state, now, 5, 1.0, 0.0), "and no third");
		}

		[Test]
		public void ABucketMarkedFullIsFullOnTheFirstMessageEvenInTheSameTick()
		{
			long now = T0.Ticks;
			ChatRateGate.State state = Fresh(now);
			LogAssert.IsTrue(ChatRateGate.TryCharge(ref state, now, 5, 1.0, 0.0), "a new character's first message is not refused against an empty bucket");
			LogAssert.IsFalse(state.IsFull, "the flag is spent");
			LogAssert.IsTrue(Math.Abs(state.Tokens - 4.0) < 1e-9, "five tokens, one spent");
		}

		[Test]
		public void TheMinimumGapRefusesEvenWithTokensLeft()
		{
			long now = T0.Ticks;
			ChatRateGate.State state = Fresh(now);
			LogAssert.IsTrue(ChatRateGate.TryCharge(ref state, now, 5, 1.0, 500.0), "first");
			LogAssert.IsFalse(ChatRateGate.TryCharge(ref state, now + TimeSpan.TicksPerMillisecond * 100, 5, 1.0, 500.0), "100 ms later: inside the gap");
			LogAssert.IsTrue(ChatRateGate.TryCharge(ref state, now + TimeSpan.TicksPerMillisecond * 600, 5, 1.0, 500.0), "600 ms later: past it");
		}

		// ── Source pins, each with its control run ────────────────────────────

		[Test]
		public void TheRateIsChargedBeforeQueueingAndTheQueueCapNeverKicks()
		{
			string code = SourceScanPins.ReadCode(ChatDirectory + "/ChatSystem.cs");
			Func<string, string> check = c =>
			{
				string receive = SourceScanPins.Body(c, "private void OnServerChatBroadcastReceived(");
				string order = SourceScanPins.InOrder(receive, "TryChargeChatRate(", "IncrementIncomingQueueSize()", "IncomingChatQueue.Enqueue(");
				if (order != null)
				{
					return order;
				}
				if (receive.Contains(".Kick("))
				{
					return "the incoming queue kicks a connection again";
				}
				string process = SourceScanPins.Body(c, "private void ProcessNewChatMessage(");
				if (process == null)
				{
					return "ProcessNewChatMessage is missing";
				}
				return process.Contains("ChatTokens") ? "the bucket is charged a second time when the queue drains" : null;
			};
			SourceScanPins.HoldsAndFires("L2 enqueue", code, check,
				SourceScanPins.InsertBefore("RecordIncomingOverflow(rateTicks);", "conn.Kick(FishNet.Managing.Server.KickReason.ExploitExcessiveData);"),
				"kicking at the global cap");
		}

		[Test]
		public void GroupChatFindsLocalMembersInTheTrackersNotTheDatabase()
		{
			string code = SourceScanPins.ReadCode(ChatDirectory + "/ChatSystem.GroupChat.cs");
			Func<string, string> check = c =>
			{
				if (c.Contains("FetchManyAsync("))
				{
					return "a roster is read from the database to find local recipients";
				}
				if (!c.Contains("PartyCharacterTracker") || !c.Contains("GuildCharacterTracker"))
				{
					return "the trackers are no longer consulted";
				}
				return null;
			};
			SourceScanPins.HoldsAndFires("M17 group", code, check,
				SourceScanPins.InsertBefore("SendToLocalGroupMembers(partyID", "_ = partyService.FetchManyAsync(partyID);\n"),
				"a roster read per line");
		}

		[Test]
		public void ATellIsResolvedLocallyBeforeAnyDatabaseLookupAndAPumpedOneNeverLooksUp()
		{
			string code = SourceScanPins.ReadCode(ChatDirectory + "/ChatSystem.TellChat.cs");
			Func<string, string> check = c => SourceScanPins.InOrder(SourceScanPins.Body(c, "public bool OnTellChat("),
				"CharactersByLowerCaseName.TryGetValue(", "if (sender == null)", "EnqueuePersistence(");
			SourceScanPins.HoldsAndFires("M17 tell", code, check,
				SourceScanPins.Replace("if (sender == null)\n\t\t\t{\n\t\t\t\treturn false;\n\t\t\t}\n\n\t\t\tif (Server?.Database", "if (Server?.Database"),
				"a pumped whisper falling through to the database lookup");
		}

		[Test]
		public void TeamChatWalksTheArenaSceneNotTheWholeServer()
		{
			string code = SourceScanPins.ReadCode(ChatDirectory + "/ChatSystem.ArenaChat.cs");
			Func<string, string> check = c =>
			{
				string body = SourceScanPins.Body(c, "public bool OnTeamChat(");
				if (body == null)
				{
					return "OnTeamChat is missing";
				}
				if (!body.Contains("TryGetSceneConnections("))
				{
					return "the arena scene's connection set is not used";
				}
				return body.Contains("in mappingData.ConnectionCharacters") ? "every connected character is walked" : null;
			};
			SourceScanPins.HoldsAndFires("L3 team", code, check,
				SourceScanPins.Replace("foreach (NetworkConnection conn in sceneConnections)", "foreach (var kvp in mappingData.ConnectionCharacters)"),
				"a walk of every character");
		}
	}
}
