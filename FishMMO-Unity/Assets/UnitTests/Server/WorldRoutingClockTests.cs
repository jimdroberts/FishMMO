using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using FishMMO.Server.Core.Collections;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The world server's routing and the scene servers' channel list time their waits on the
	/// process's monotonic clock, and judge a scene server's liveness by the pulse age the
	/// database measured — never by the host's wall clock (audit finding L17).
	/// </summary>
	/// <remarks>
	/// <para>
	/// The tracker half is behavioural, driven with arbitrary monotonic readings: nothing may
	/// depend on the clock's origin. The rest are source scans in the style of
	/// <see cref="PanelReopenStateTests"/>: each names a construct whose return would put a host
	/// clock back into a duration or a cross-process comparison without failing any behavioural
	/// test, because no test can step the host clock.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class WorldRoutingClockTests
	{
		private const string WorldScene = "Assets/Scripts/Server/Implementation/World/WorldServer/WorldScene/WorldSceneSystem.cs";
		private const string SceneChannel = "Assets/Scripts/Server/Implementation/World/SceneServer/SceneChannel/SceneChannelSystem.cs";
		private const string SceneServer = "Assets/Scripts/Server/Implementation/World/SceneServer/SceneServer/SceneServerSystem.cs";
		private const string TimedCacheSource = "Assets/Scripts/Server/Core/Collections/TimedCache.cs";
		private const string WorldServer = "Assets/Scripts/Server/Implementation/World/WorldServer/WorldServer/WorldServerSystem.cs";
		private const string SceneServerControl = "Assets/Scripts/Server/Implementation/World/SceneServer/SceneServer/SceneServerSystem.ServerControl.cs";
		private const string SceneServerAdmin = "Assets/Scripts/Server/Implementation/World/SceneServer/SceneServer/SceneServerSystem.AdminCommands.cs";

		// ── ExpiringKeyTracker on the monotonic clock ─────────────────────────────────────────

		[Test]
		public void AMonotonicDebounce_RefusesInsideItsWindow_AndAllowsAfter()
		{
			var tracker = new ExpiringKeyTracker<string>(StringComparer.OrdinalIgnoreCase);
			TimeSpan window = TimeSpan.FromSeconds(3);

			LogAssert.IsTrue(tracker.TryBegin("alice", 1000.0, window), "the first lookup is allowed");
			LogAssert.IsFalse(tracker.TryBegin("ALICE", 1002.9, window), "inside the window it is refused");
			LogAssert.IsTrue(tracker.TryBegin("bob", 1001.0, window), "another key is independent");
			LogAssert.IsTrue(tracker.TryBegin("alice", 1003.0, window), "and it is allowed once the window has passed");
		}

		[Test]
		public void AMonotonicSweep_RemovesOnlyWhatHasExpired()
		{
			var tracker = new ExpiringKeyTracker<string>();
			TimeSpan window = TimeSpan.FromSeconds(3);
			tracker.TryBegin("early", 10.0, window);
			tracker.TryBegin("late", 20.0, window);

			LogAssert.AreEqual(1, tracker.SweepExpired(15.0, 64, 64), "the early key's window ended at 13");
			LogAssert.AreEqual(1, tracker.Count, "the late key's has not");
			LogAssert.AreEqual(1, tracker.SweepExpired(23.0, 64, 64), "and it goes when its own does");
		}

		[Test]
		public void AMonotonicReadingNearZero_StillOpensAWindow()
		{
			var tracker = new ExpiringKeyTracker<int>();
			LogAssert.IsTrue(tracker.TryBegin(1, 0.0, TimeSpan.FromSeconds(3)), "a reading at the origin is a reading, not an error");
			LogAssert.IsFalse(tracker.TryBegin(1, 1.0, TimeSpan.FromSeconds(3)), "and its window holds");
		}

		// ── TimedCache ────────────────────────────────────────────────────────────────────────

		[Test]
		public void TheTimedCache_ServesWithinItsTtl_AndNeverWithAZeroTtl()
		{
			var cache = new TimedCache<string, int>();
			cache.Set("zone", 7);

			LogAssert.IsTrue(cache.TryGet("zone", TimeSpan.FromMinutes(5), out int value) && value == 7, "fresh within the TTL");
			LogAssert.IsFalse(cache.TryGet("zone", TimeSpan.Zero, out _), "a zero TTL disables the cache");
			LogAssert.AreEqual(0, cache.SweepExpired(TimeSpan.FromMinutes(5), 64, 64), "nothing has expired");
		}

		[Test]
		public void TheTimedCache_AgesOnTheMonotonicClock()
		{
			string code = CodeOnly(Read(TimedCacheSource));
			LogAssert.AreEqual(0, CountOccurrences(code, "DateTime.UtcNow"), "a TTL is a duration; a host clock stepped back kept every entry fresh for the size of the step");
			LogAssert.IsTrue(code.Contains("MonotonicClock.NowSeconds"), "entries are stamped and aged on the monotonic clock");
		}

		// ── Source pins ───────────────────────────────────────────────────────────────────────

		[Test]
		public void WorldRouting_TimesNoWaitOnTheHostWallClock()
		{
			string code = CodeOnly(Read(WorldScene));
			LogAssert.AreEqual(1, CountOccurrences(code, "DateTime.UtcNow"),
				"the only wall-clock read left is the persisted timestamp of an instance release; every wait (queue TTL, residency, debounce, combat-logout grace, caches) is monotonic");
			LogAssert.IsTrue(MethodBody(code, "private async Task ReleaseFromInstanceAsync(").Contains("DateTime.UtcNow"),
				"and that one is the row's own timestamp, which is a wall-clock instant by design");
		}

		[Test]
		public void SceneServerLiveness_IsTheDatabaseMeasuredPulseAge()
		{
			string world = CodeOnly(Read(WorldScene));
			LogAssert.IsTrue(MethodBody(world, "private static bool IsSceneServerLive(").Contains("PulseAgeSeconds"),
				"the world server routes by the pulse age the database measured as it read the row");
			LogAssert.AreEqual(0, CountOccurrences(world, ".LastPulse"), "never by subtracting last_pulse from this host's clock");
			LogAssert.IsTrue(MethodBody(world, "private async Task SweepStaleSceneRowsAsync(").Contains("worldServerID, SceneServerPulseStaleSeconds, StaleSceneRowMaxPerSweep"),
				"the dead-host sweep hands the database an age, not a cutoff instant from this host");

			string channel = CodeOnly(Read(SceneChannel));
			LogAssert.AreEqual(0, CountOccurrences(channel, "DateTime.UtcNow"), "the channel list times its cooldowns on the monotonic clock");
			LogAssert.AreEqual(0, CountOccurrences(channel, ".LastPulse"), "and judges a channel's host the way the world server does");
			LogAssert.IsTrue(channel.Contains("PulseAgeSeconds >= SceneServerPulseStaleSeconds"), "by its database-measured pulse age");
		}

		[Test]
		public void ScheduledShutdowns_CountDownFromTheDatabasesMeasure_NotTheHostClock()
		{
			string world = CodeOnly(Read(WorldServer));
			LogAssert.AreEqual(0, CountOccurrences(world, "DateTime.UtcNow"),
				"the world server stopped at shutdown_at_utc by its own clock, early or late by its skew from the database (O25)");
			LogAssert.IsTrue(world.Contains("shutdownCountdown.Adopt(reading)") && world.Contains("shutdownCountdown.IsDue(MonotonicClock.NowSeconds)"),
				"it adopts the database-measured countdown and runs it on the monotonic clock");
			LogAssert.IsTrue(MethodBody(world, "private async Task PulseAsync(").Contains("ServerControlReading.ArrivedNow(result.Data)"),
				"the reading is stamped as the reply arrives, not a pulse later when the main thread adopts it");

			string scene = CodeOnly(Read(SceneServerControl));
			LogAssert.AreEqual(0, CountOccurrences(scene, "DateTime.UtcNow"),
				"the scene server's own shutdown and the world shutdowns it enforces count down on the same shared measure");
			LogAssert.IsTrue(MethodBody(scene, "private bool ProcessControlState(").Contains("double now = MonotonicClock.NowSeconds"),
				"on the monotonic clock");

			string report = MethodBody(CodeOnly(Read(SceneServerAdmin)), "private void ReportStatus(");
			LogAssert.AreEqual(0, CountOccurrences(report, "DateTime.UtcNow"), "and /admin status reports the countdown the process is actually running");
		}

		[Test]
		public void RelativeShutdownRequests_AreTimedByTheDatabaseClock()
		{
			string admin = CodeOnly(Read(SceneServerAdmin));
			foreach (string signature in new[] { "private void ScheduleWorldShutdown(", "private void ScheduleSceneShutdown(" })
			{
				string body = MethodBody(admin, signature);
				LogAssert.AreEqual(0, CountOccurrences(body, "DateTime.UtcNow"),
					signature + " builds no deadline from this host's clock: '/admin shutdown 300' typed on a host two minutes slow gave players three minutes");
				LogAssert.IsTrue(body.Contains("SetShutdownInAsync("),
					signature + " hands the database the delay, which adds it to the clock every server counts the deadline down against");
				LogAssert.AreEqual(0, CountOccurrences(body, "SetShutdownAsync("),
					signature + " never uses the absolute API, which takes the caller's own instant");
			}
		}

		[Test]
		public void SceneLoads_AreClaimedInTheServersName()
		{
			string code = CodeOnly(Read(SceneServer));
			LogAssert.IsTrue(code.Contains("sceneService.DequeueAsync(serverID)"),
				"the dequeue names its claimant, which is what lets a retry after a lost reply find its own row");
		}

		// ── helpers ───────────────────────────────────────────────────────────────────────────

		private static string Read(string path)
		{
			LogAssert.IsTrue(File.Exists(path), path + " exists");
			return File.ReadAllText(path).Replace("\r\n", "\n");
		}

		private static int CountOccurrences(string text, string needle)
		{
			int count = 0;
			for (int i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = text.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
			{
				++count;
			}
			return count;
		}

		/// <summary>Brace-matches the body following the first occurrence of <paramref name="signature"/>.</summary>
		private static string MethodBody(string source, string signature)
		{
			int start = source.IndexOf(signature, StringComparison.Ordinal);
			LogAssert.IsTrue(start >= 0, "found: " + signature);
			int open = source.IndexOf('{', start);
			int depth = 0;
			for (int i = open; i < source.Length; ++i)
			{
				if (source[i] == '{') ++depth;
				else if (source[i] == '}' && --depth == 0)
				{
					return source.Substring(open, i - open + 1);
				}
			}
			LogAssert.Fail("unterminated body for " + signature);
			return string.Empty;
		}

		/// <summary>Strips block comments and comment lines so prose cannot trip a code scan.</summary>
		private static string CodeOnly(string source)
		{
			while (true)
			{
				int open = source.IndexOf("/*", StringComparison.Ordinal);
				if (open < 0)
				{
					break;
				}

				int close = source.IndexOf("*/", open + 2, StringComparison.Ordinal);
				source = close < 0 ? source.Substring(0, open) : source.Remove(open, close - open + 2);
			}

			StringBuilder kept = new StringBuilder(source.Length);
			foreach (string line in source.Split('\n'))
			{
				string trimmed = line.TrimStart();
				if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith("*", StringComparison.Ordinal))
				{
					continue;
				}

				kept.Append(line).Append('\n');
			}

			return kept.ToString();
		}
	}
}
