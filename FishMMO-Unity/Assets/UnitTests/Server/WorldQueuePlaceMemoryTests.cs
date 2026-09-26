using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using FishMMO.Server.Implementation.World.WorldServer;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// A queued account keeps its place in the open-world line across a dropped connection and a
	/// stalled-line purge, for a grace window; a player who chose to leave does not (optional
	/// change O4).
	/// </summary>
	/// <remarks>
	/// The memory is behavioural, driven with arbitrary monotonic readings. The last tests are
	/// source pins on <c>WorldSceneSystem</c>, in the style of <see cref="WorldRoutingClockTests"/>:
	/// they name which ways out of the queue keep the place and which do not, because no
	/// behavioural test can drop a connection.
	/// </remarks>
	[TestFixture]
	public class WorldQueuePlaceMemoryTests
	{
		private const string WorldScene = "Assets/Scripts/Server/Implementation/World/WorldServer/WorldScene/WorldSceneSystem.cs";
		private const double Grace = 60.0;

		private static WorldQueuePlaceMemory Memory() => new WorldQueuePlaceMemory { GraceSeconds = Grace };

		[Test]
		public void APlace_IsResumedInTheSameQueue_WithinTheWindow()
		{
			var memory = Memory();
			memory.Remember("alice", "Starter", waitingSince: 100.0, now: 400.0);

			LogAssert.IsTrue(memory.TryResume("alice", "Starter", 459.0, out double since), "59 s after the wait ended");
			LogAssert.AreEqual(100.0, since, "the original start, which is what the line is ordered by");
		}

		[Test]
		public void APlace_IsSpentWhenResumed()
		{
			var memory = Memory();
			memory.Remember("alice", "Starter", 100.0, 400.0);
			memory.TryResume("alice", "Starter", 401.0, out _);

			LogAssert.IsFalse(memory.TryResume("alice", "Starter", 402.0, out _), "one wait cannot be claimed twice");
			LogAssert.AreEqual(0, memory.Count, "nothing is left held");
		}

		[Test]
		public void APlace_IsNotResumedAfterTheWindow_OrInAnotherQueue()
		{
			var memory = Memory();
			memory.Remember("alice", "Starter", 100.0, 400.0);
			LogAssert.IsFalse(memory.TryResume("alice", "Starter", 460.0, out _), "the window is half-open: 60 s later it has closed");

			memory.Remember("bob", "Starter", 100.0, 400.0);
			LogAssert.IsFalse(memory.TryResume("bob", "Harbour", 401.0, out _), "a place is in one scene's line");
			LogAssert.IsFalse(memory.TryResume("bob", "Starter", 402.0, out _), "and joining another line ends the old wait");
		}

		[Test]
		public void APlayerWhoLeft_LosesTheirPlace()
		{
			var memory = Memory();
			memory.Remember("alice", "Starter", 100.0, 400.0);
			memory.Forget("ALICE");
			LogAssert.IsFalse(memory.TryResume("alice", "Starter", 401.0, out _), "forgotten, whatever the case of the name");
		}

		[Test]
		public void AccountNames_IgnoreCase_SceneNamesDoNot()
		{
			var memory = Memory();
			memory.Remember("Alice", "Starter", 100.0, 400.0);
			LogAssert.IsTrue(memory.TryResume("alice", "Starter", 401.0, out _), "accounts compare as the account manager compares them");

			memory.Remember("alice", "Starter", 100.0, 400.0);
			LogAssert.IsFalse(memory.TryResume("alice", "starter", 401.0, out _), "scenes compare as the waiting queues key them");
		}

		[Test]
		public void TwoEndedWaitsInOneLine_KeepTheOlderPlace()
		{
			var memory = Memory();
			memory.Remember("alice", "Starter", 100.0, 400.0);
			memory.Remember("alice", "Starter", 350.0, 410.0);
			LogAssert.IsTrue(memory.TryResume("alice", "Starter", 411.0, out double since), "held");
			LogAssert.AreEqual(100.0, since, "a reconnect that raced the old connection's end is owed the older place");

			memory.Remember("bob", "Starter", 100.0, 400.0);
			memory.Remember("bob", "Harbour", 350.0, 410.0);
			LogAssert.IsTrue(memory.TryResume("bob", "Harbour", 411.0, out since) && since == 350.0, "a wait in another line replaces the old place");
		}

		[Test]
		public void AZeroWindow_HoldsNothing()
		{
			var memory = new WorldQueuePlaceMemory { GraceSeconds = 0.0 };
			memory.Remember("alice", "Starter", 100.0, 400.0);
			LogAssert.AreEqual(0, memory.Count, "disabled");
			LogAssert.IsFalse(memory.TryResume("alice", "Starter", 400.0, out _), "nothing to resume");
		}

		[Test]
		public void TheSweep_DropsOnlyClosedWindows()
		{
			var memory = Memory();
			memory.Remember("early", "Starter", 10.0, 100.0);
			memory.Remember("late", "Starter", 20.0, 150.0);

			LogAssert.AreEqual(1, memory.Sweep(170.0), "the early place's window closed at 160");
			LogAssert.AreEqual(1, memory.Count, "the late one's has not");
			LogAssert.IsTrue(memory.TryResume("late", "Starter", 171.0, out _), "and it is still resumable");
		}

		[Test]
		public void AResumedPlace_IsWhereTheLineTakesThePlayerFrom()
		{
			// Four waiting; the resumed player's connection is the newest, its account's wait the oldest.
			var memory = Memory();
			memory.Remember("alice", "Starter", 100.0, 400.0);
			memory.TryResume("alice", "Starter", 410.0, out double resumed);

			var candidates = new List<WorldSceneRoutingRules.QueueCandidate<string>>
			{
				new WorldSceneRoutingRules.QueueCandidate<string>("bob", 200.0, 1, false),
				new WorldSceneRoutingRules.QueueCandidate<string>("carol", 300.0, 2, false),
				new WorldSceneRoutingRules.QueueCandidate<string>("alice", resumed, 9, false),
				new WorldSceneRoutingRules.QueueCandidate<string>("dave", 405.0, 3, false),
			};
			var selected = new List<string>();
			WorldSceneRoutingRules.SelectForRouting(candidates, 1, selected);

			LogAssert.AreEqual(1, selected.Count, "one free slot");
			LogAssert.AreEqual("alice", selected[0], "the place she held, not the back of the line");
		}

		// ── Source pins ───────────────────────────────────────────────────────────────────────

		[Test]
		public void ADropAndAPurge_KeepThePlace_ARoutedOrLeavingConnectionDoesNot()
		{
			string code = CodeOnly(Read(WorldScene));

			LogAssert.IsTrue(MethodBody(code, "protected override void OnRemoteConnectionStopped(").Contains("EndWaitKeepingPlace(conn.ClientId)"),
				"a dropped connection keeps its account's place");
			LogAssert.IsTrue(MethodBody(code, "private void PurgeExpiredWaitingConnections(").Contains("EndWaitKeepingPlace(conn.ClientId)"),
				"a purged wait keeps it too: the purge sends a player back because the line stalled, and Try again must not cost them their place");

			string leave = MethodBody(code, "private void OnWorldSceneQueueLeave(");
			LogAssert.IsTrue(leave.Contains("queuePlaces.Forget(") && leave.Contains("ClearQueueTracking(conn.ClientId)") && !leave.Contains("EndWaitKeepingPlace"),
				"a player who chose to leave gives the place up");
			LogAssert.IsTrue(code.Contains("RegisterBroadcast<WorldSceneQueueLeaveBroadcast>(OnWorldSceneQueueLeave, true)"),
				"and the server listens for the choice, from authenticated connections only");

			string connect = MethodBody(code, "private void ApplySceneConnect(");
			LogAssert.IsTrue(connect.Contains("HasLeftQueue(conn)"), "a connection that has left is not sent to a scene server while it closes");
			LogAssert.IsTrue(connect.Contains("queuePlaces.Forget(") && !connect.Contains("EndWaitKeepingPlace"), "a routed account holds no place");
		}

		[Test]
		public void AResumedPlace_MovesTheOrderingClock_NotTheExpiryClock()
		{
			string code = CodeOnly(Read(WorldScene));
			string move = MethodBody(code, "private void MoveToOpenWorldQueueNow(");
			LogAssert.IsTrue(move.Contains("ResetQueueEntryTime(conn.ClientId)") && move.Contains("ResumeOrBeginPlace("),
				"joining the open-world line restarts the expiry clock and resumes any held place");
			LogAssert.IsTrue(move.IndexOf("ResetQueueEntryTime(conn.ClientId)", StringComparison.Ordinal) < move.IndexOf("ResumeOrBeginPlace(", StringComparison.Ordinal),
				"in that order");

			string resume = MethodBody(code, "private void ResumeOrBeginPlace(");
			LogAssert.IsTrue(resume.Contains("waitingSinceByClientId[clientId] = heldSince"), "the resumed start sets the clock the line is ordered by");
			LogAssert.IsFalse(resume.Contains("WaitingQueueEnteredAtByClientId"),
				"and leaves the expiry clock fresh, so a line that is still stalled does not purge the returning player on the next sweep");
		}

		// ── helpers ───────────────────────────────────────────────────────────────────────────

		private static string Read(string path)
		{
			LogAssert.IsTrue(File.Exists(path), path + " exists");
			return File.ReadAllText(path).Replace("\r\n", "\n");
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

			var kept = new System.Text.StringBuilder(source.Length);
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
