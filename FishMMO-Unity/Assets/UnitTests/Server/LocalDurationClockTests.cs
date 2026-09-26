using System;
using NUnit.Framework;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Server.Implementation.World.SceneServer;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The last of the server's local durations run on the process's monotonic clock, not on the
	/// host's wall clock (audit finding L17, finished).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>What was wrong.</b> A timer measured as the difference of two <c>DateTime.UtcNow</c>
	/// readings moves when NTP or an operator steps the host clock: forward, and every lifetime
	/// inside the step ends at once (combat-logout bodies despawned, handshake watchdogs
	/// disconnecting every loading client, arena phases skipped, every pending party and guild
	/// invitation expired); back, and nothing ages for the size of the step (cooldowns and
	/// throttles held, retries stalled, the chat gate refusing every sender and flood-muting them).
	/// </para>
	/// <para>
	/// <b>What stays on the wall clock, and why.</b> An instant that is persisted, compared with a
	/// database row or shown as a calendar time: a row's <c>last_saved</c> or <c>time_created</c>,
	/// ban, mute, deserter and staff-lock "until" values, verification-code expiries, the guild log's
	/// stamp, the chat line's legal receipt stamp, and the party and guild update pumps' marks,
	/// which are compared with the update rows' database stamps under a skew allowance. Each is named
	/// below with its snippet, so a new wall-clock read in these files fails its pin until someone
	/// decides which kind it is.
	/// </para>
	/// <para>
	/// The pins are source scans with their own control runs (<see cref="SourceScanPins"/>), because
	/// no test can step the host clock.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class LocalDurationClockTests
	{
		private const string SceneServer = "Assets/Scripts/Server/Implementation/World/SceneServer/";
		private const string LoginServer = "Assets/Scripts/Server/Implementation/LoginServer/";

		/// <summary>A file, and every wall-clock read it is allowed, each as a snippet that holds exactly one.</summary>
		private sealed class ClockCase
		{
			public string Path;
			public string[] WallClockSnippets;
			public ClockCase(string path, params string[] wallClockSnippets)
			{
				Path = path;
				WallClockSnippets = wallClockSnippets;
			}
		}

		private static readonly ClockCase[] Cases =
		{
			new ClockCase(SceneServer + "Character/CharacterSystem.CombatLogout.cs"),
			new ClockCase(SceneServer + "Character/CharacterSystem.Connection.cs"),
			new ClockCase(SceneServer + "Character/CharacterSystem.Unstuck.cs"),
			new ClockCase(SceneServer + "Character/CharacterSystem.Saving.cs",
				// A log line's label, and the row's persisted save time.
				"Log.Debug(\"CharacterSystem\", \"Save\" + \"[\" + DateTime.UtcNow + \"]\");",
				"lastSaved: DateTime.UtcNow,"),
			new ClockCase(SceneServer + "Character/CharacterSystem.Loading.cs",
				// A staff lock's persisted "until"; the row's persisted timestamp; a mute's persisted "until".
				"if (lockState.IsLocked(DateTime.UtcNow))",
				"clearedFlags, charData.Version + 1, DateTime.UtcNow);",
				"ChatMutePolicy.ResolveUntilTicks(ctx.ChatMute.Value, DateTime.UtcNow, out string muteReason);"),
			// The update pump reads the database's clock with each fetch (O39), so none is left here.
			new ClockCase(SceneServer + "Party/PartySystem.cs"),
			new ClockCase(SceneServer + "Party/PartySystemRuntimeData.cs",
				"LastFetchTime = DateTime.UtcNow;",
				"LastFetchTime = DateTime.UtcNow;"),
			new ClockCase(SceneServer + "Guild/GuildSystem.cs",
				// The guild log entry's persisted stamp.
				"detail ?? string.Empty,\n\t\t\t\t\t\tDateTime.UtcNow);"),
			new ClockCase(SceneServer + "Guild/GuildSystemRuntimeData.cs",
				"LastFetchTime = DateTime.UtcNow;",
				"LastFetchTime = DateTime.UtcNow;"),
			new ClockCase(SceneServer + "Housing/HousingSystem.Plots.cs"),
			new ClockCase(SceneServer + "Interactable/InteractableSystem.GroupFinder.cs"),
			// Deserter locks are a duration the database adds to its own now, and read back as the
			// seconds it measures (O40), so neither file keeps a wall-clock read.
			new ClockCase(SceneServer + "Interactable/InteractableSystem.Arena.cs"),
			new ClockCase(SceneServer + "Interactable/InteractableSystem.ArenaMatch.cs"),
			new ClockCase(SceneServer + "Naming/NamingSystem.cs"),
			new ClockCase(SceneServer + "Naming/NamingSystemRuntimeData.cs"),
			new ClockCase(SceneServer + "CharacterInventory/CharacterInventorySystem.cs"),
			new ClockCase(SceneServer + "SceneServer/SceneServerSystem.cs"),
			new ClockCase(SceneServer + "SceneServer/SceneServerSystem.StaffConsole.cs",
				// The roster's "muted" flag, against a mute's persisted "until".
				"long now = DateTime.UtcNow.Ticks;\n\n\t\t\tvar rows = new List<StaffRosterEntry>();"),
			new ClockCase(SceneServer + "Chat/ChatSystem.ReportPlayer.cs"),
			new ClockCase(SceneServer + "Chat/ChatSystem.cs",
				// The line's legal receipt stamp, persisted with it. The persist retry window is measured
				// on each line's monotonic first-queued time (O42).
				"msg.ReceivedUtcTicks = DateTime.UtcNow.Ticks;"),
			new ClockCase(LoginServer + "CharacterSelect/CharacterSelectSystem.cs",
				"if (lockResult.Data.IsLocked(DateTime.UtcNow))"),
			new ClockCase(LoginServer + "CharacterCreate/CharacterCreateSystem.cs",
				"timeCreated: DateTime.UtcNow,",
				"lastSaved: DateTime.UtcNow"),
			new ClockCase(LoginServer + "AccountCreation/AccountCreationSystem.cs",
				// Verification codes' persisted expiries.
				"DateTime verifyExpiresUtc = DateTime.UtcNow.AddHours(24);",
				"DateTime verifyExpiresUtc = DateTime.UtcNow.AddHours(24);"),
			new ClockCase("Assets/Scripts/Server/Implementation/MainThreadQueueHelper.cs"),
			new ClockCase("Assets/Scripts/Server/Implementation/KickRequest/KickRequestSystem.cs"),
			new ClockCase("Assets/Scripts/Server/Implementation/KickRequest/KickRequestSystemQueueData.cs"),
			new ClockCase("Assets/Scripts/Server/Core/Collections/SingleFlightCache.cs"),
		};

		/// <summary>The wall-clock reads a scan looks for.</summary>
		private static readonly string[] WallClockReads = { "DateTime.UtcNow", "DateTimeOffset.UtcNow", "DateTime.Now", "DateTimeOffset.Now" };

		// ── Source pins ───────────────────────────────────────────────────────────────────────

		[Test]
		public void EveryWallClockReadLeft_IsANamedInstantNotADuration()
		{
			foreach (ClockCase clockCase in Cases)
			{
				ClockCase c = clockCase;
				SourceScanPins.HoldsAndFires(c.Path, SourceScanPins.ReadCode(c.Path),
					code => UnlistedWallClockRead(code, c.WallClockSnippets),
					// The defect put back: one of the file's own monotonic readings taken from the host clock.
					code => ReplaceFirst(code, "MonotonicClock.Now", "DateTime.UtcNow"),
					"a local duration read from the host's wall clock");
			}
		}

		[Test]
		public void TheChatGate_IsChargedOnTheMonotonicClock_NotOnTheLegalReceiptStamp()
		{
			const string ChatPath = SceneServer + "Chat/ChatSystem.cs";
			SourceScanPins.HoldsAndFires("chat rate gate", SourceScanPins.ReadCode(ChatPath),
				code =>
				{
					string receive = SourceScanPins.Body(code, "private void OnServerChatBroadcastReceived(");
					string order = SourceScanPins.InOrder(receive,
						"msg.ReceivedUtcTicks = DateTime.UtcNow.Ticks;",
						"long rateTicks = MonotonicClock.NowTicks;",
						"TryChargeChatRate(sender, rateTicks)",
						"RecordIncomingOverflow(rateTicks);");
					if (order != null)
					{
						return order;
					}
					return receive.Contains("TryChargeChatRate(sender, msg.ReceivedUtcTicks)")
						? "the bucket is charged on the wall-clock receipt stamp; a host stepped back refused every sender for the size of the step"
						: null;
				},
				SourceScanPins.Replace("TryChargeChatRate(sender, rateTicks)", "TryChargeChatRate(sender, msg.ReceivedUtcTicks)"),
				"the gate charged on the receipt stamp");
		}

		[Test]
		public void ACharactersChatRateFields_StartAtZero_NotAtEitherSidesClock()
		{
			const string PlayerCharacterPath = "Assets/Scripts/Shared/Implementation/Entity/PlayerCharacter.cs";
			SourceScanPins.HoldsAndFires("PlayerCharacter chat fields", SourceScanPins.ReadCode(PlayerCharacterPath),
				code =>
				{
					foreach (string signature in new[] { "public override void OnAwake(", "public override void ResetState(" })
					{
						string body = SourceScanPins.Body(code, signature);
						if (body == null)
						{
							return signature + " is missing";
						}
						if (!body.Contains("NextChatMessageTicks = 0;") || !body.Contains("ChatTokenLastRefillTicks = 0;"))
						{
							return signature + " seeds the chat rate fields from a clock: a wall-clock stamp read by the server's monotonic gate refused every line";
						}
					}
					return null;
				},
				SourceScanPins.Replace("NextChatMessageTicks = 0;", "NextChatMessageTicks = DateTime.UtcNow.Ticks;"),
				"the gap seeded from the wall clock");
		}

		[Test]
		public void TheInstanceCountdown_IsHandedOutAsADuration()
		{
			const string InstancePath = SceneServer + "Character/CharacterSystem.Instance.cs";
			SourceScanPins.HoldsAndFires("instance countdown", SourceScanPins.ReadCode(InstancePath),
				code =>
				{
					if (!code.Contains("TryGetInstanceRemainingSeconds(instanceSceneID, out double remaining)"))
					{
						return "the instance panel no longer asks for the time left";
					}
					return UnlistedWallClockRead(code, Array.Empty<string>());
				},
				SourceScanPins.Replace("TryGetInstanceRemainingSeconds(instanceSceneID, out double remaining)",
					"TryGetInstanceExpiry(instanceSceneID, out DateTime expiresUtc) && (remaining = (expiresUtc - DateTime.UtcNow).TotalSeconds) > double.MinValue"),
				"the deadline re-expressed on the wall clock and subtracted from it again");
		}

		[Test]
		public void TheKickPoll_KeepsItsPlaceOnTheDatabaseClock()
		{
			const string KickPath = "Assets/Scripts/Server/Implementation/KickRequest/KickRequestSystem.cs";
			SourceScanPins.HoldsAndFires("kick poll", SourceScanPins.ReadCode(KickPath),
				code =>
				{
					string body = SourceScanPins.Body(code, "private async Task ProcessKickRequestsAsync(");
					string order = SourceScanPins.InOrder(body,
						"window.BuildQuery(UpdateFetchCount, MonotonicClock.NowSeconds - data.WatchStartedAt)",
						"kickRequestService.FetchAsync(query)",
						"window.MarkHandled(kickRequest.ID, kickRequest.TimeCreated);",
						"window.CompleteRead(page.ReadFromUtc, page.ReadStartedUtc,");
					if (order != null)
					{
						return order;
					}
					return body.Contains("LastFetchTime") || body.Contains("DateTime.UtcNow")
						? "the poll keeps a cursor on this host's clock again"
						: null;
				},
				SourceScanPins.InsertBefore("KickRequestPollQuery query =", "data.LastFetchTime = DateTime.UtcNow;\n"),
				"a host-clock cursor");

			const string ServicePath = "../FishMMO-Database/FishMMO-DB/Npgsql/Services/World/KickRequestService.cs";
			SourceScanPins.HoldsAndFires("kick service", SourceScanPins.ReadCode(ServicePath),
				code =>
				{
					if (code.Contains("DateTime.UtcNow"))
					{
						return "the kick service reads this process's clock; every instant it compares is the database's";
					}
					string pending = SourceScanPins.Body(code, "public async Task<DatabaseResult<bool>> HasPendingAsync(");
					if (pending == null || !pending.Contains("{DatabaseUtcClockSql} - {{1}}::interval"))
					{
						return "a pending kick is no longer aged by the database's clock";
					}
					string fetch = SourceScanPins.Body(code, "public async Task<DatabaseResult<KickRequestPage>> FetchAsync(");
					return fetch != null && fetch.Contains("ReadDatabaseUtcNowAsync(dbContext, cancellationToken)")
						? null
						: "the poll no longer reports the database's clock";
				},
				SourceScanPins.Replace("{DatabaseUtcClockSql} - {{1}}::interval", "{{2}}"),
				"a pending kick aged against a caller's instant");

			const string AccountServicePath = "../FishMMO-Database/FishMMO-DB/Npgsql/Services/Login/AccountService.cs";
			SourceScanPins.HoldsAndFires("last login", SourceScanPins.ReadCode(AccountServicePath),
				code =>
				{
					string body = SourceScanPins.Body(code, "public async Task<DatabaseResult> PersistLastLoginAsync(");
					if (body == null)
					{
						return "PersistLastLoginAsync is missing";
					}
					if (body.Contains("DateTime.UtcNow"))
					{
						return "last_login is stamped by the login server's clock, but compared with kicks the database stamped";
					}
					return body.Contains("SET last_login = {DatabaseUtcClockSql}") ? null : "last_login is not stamped by the database's clock";
				},
				SourceScanPins.Replace("SET last_login = {DatabaseUtcClockSql}", "SET last_login = {{1}}"),
				"last_login stamped by the host");
		}

		// ── Behaviour ─────────────────────────────────────────────────────────────────────────

		[Test]
		public void NowTicks_IsNowSecondsInTimeSpanTicks()
		{
			double before = MonotonicClock.NowSeconds;
			long ticks = MonotonicClock.NowTicks;
			double after = MonotonicClock.NowSeconds;
			LogAssert.IsTrue(ticks >= (long)(before * TimeSpan.TicksPerSecond) && ticks <= (long)(after * TimeSpan.TicksPerSecond) + 1,
				"the tick reading lies between two second readings taken around it");
		}

		[Test]
		public void AFreshCharactersChatGate_WorksFromZero_OnSmallMonotonicReadings()
		{
			/* The state a character starts with (all zero, bucket marked full), charged at readings a
			 * monotonic clock gives shortly after boot. With a wall-clock seed the gap was ~6e17 ticks
			 * in the future and every line was refused. */
			var state = new ChatRateGate.State { IsFull = true };
			long t = TimeSpan.TicksPerSecond * 5;
			LogAssert.IsTrue(ChatRateGate.TryCharge(ref state, t, 3, 1.0, 500.0), "the first line goes");
			LogAssert.IsFalse(ChatRateGate.TryCharge(ref state, t + TimeSpan.TicksPerMillisecond * 100, 3, 1.0, 500.0), "the gap holds the next");
			LogAssert.IsTrue(ChatRateGate.TryCharge(ref state, t + TimeSpan.TicksPerMillisecond * 600, 3, 1.0, 500.0), "and lets it through once it has passed");
		}

		[Test]
		public void AnInFlightLookup_GoesStaleOnTheMonotonicClock()
		{
			var table = new InFlightLookupTable<long, string>(8, 4);
			TimeSpan stale = TimeSpan.FromSeconds(10);
			LogAssert.AreEqual(InFlightLookupTable<long, string>.JoinResult.Started, table.Join(1, "a", 100.0, stale), "the first request starts the fetch");
			LogAssert.AreEqual(InFlightLookupTable<long, string>.JoinResult.Joined, table.Join(1, "b", 109.9, stale), "inside the bound it is joined");
			LogAssert.AreEqual(InFlightLookupTable<long, string>.JoinResult.Started, table.Join(1, "c", 110.0, stale), "at the bound it is presumed lost and restarted");
			LogAssert.AreEqual(0, table.SweepStale(119.9, stale), "the restart is fresh");
			LogAssert.AreEqual(1, table.SweepStale(120.0, stale), "and is swept once it too is stale");
		}

		// ── helpers ───────────────────────────────────────────────────────────────────────────

		/// <summary>
		/// Null when every wall-clock read in <paramref name="code"/> is inside one of the listed
		/// snippets (each found, each holding one read); otherwise why not.
		/// </summary>
		private static string UnlistedWallClockRead(string code, string[] snippets)
		{
			if (code == null)
			{
				return "the file was not found";
			}
			string rest = code;
			foreach (string snippet in snippets)
			{
				if (CountReads(snippet) != 1)
				{
					return $"the allowed snippet '{snippet}' must hold exactly one wall-clock read";
				}
				int at = rest.IndexOf(snippet, StringComparison.Ordinal);
				if (at < 0)
				{
					return $"the allowed wall-clock read '{snippet}' is gone; re-anchor the list";
				}
				rest = rest.Remove(at, snippet.Length);
			}
			foreach (string read in WallClockReads)
			{
				int at = rest.IndexOf(read, StringComparison.Ordinal);
				if (at >= 0)
				{
					int lineStart = rest.LastIndexOf('\n', at) + 1;
					int lineEnd = rest.IndexOf('\n', at);
					string line = rest.Substring(lineStart, (lineEnd < 0 ? rest.Length : lineEnd) - lineStart).Trim();
					return $"'{line}' reads the host's wall clock and is not one of the named instants; a duration belongs on MonotonicClock";
				}
			}
			return null;
		}

		private static int CountReads(string text)
		{
			int count = 0;
			foreach (string read in new[] { "DateTime.UtcNow", "DateTimeOffset.UtcNow" })
			{
				for (int i = text.IndexOf(read, StringComparison.Ordinal); i >= 0; i = text.IndexOf(read, i + read.Length, StringComparison.Ordinal))
				{
					++count;
				}
			}
			return count;
		}

		private static string ReplaceFirst(string text, string find, string replacement)
		{
			int at = text.IndexOf(find, StringComparison.Ordinal);
			return at < 0 ? text : text.Substring(0, at) + replacement + text.Substring(at + find.Length);
		}
	}
}
