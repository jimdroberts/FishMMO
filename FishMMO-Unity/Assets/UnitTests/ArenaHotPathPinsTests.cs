using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Pins the shape of the arena coordinator's and the group finder pump's hot paths after the
	/// 2026-09-25 hot-path audit (S1, M19, L7), so the per-recipient and per-row patterns they
	/// replaced cannot grow back unnoticed.
	/// </summary>
	/// <remarks>
	/// Source scans, like their neighbours: they see what was written, not what runs. The rules
	/// they guard are behaviour a unit test cannot reach without a network manager and a
	/// database — who a message is sent to, how many round trips a pump makes, which thread
	/// clears a flag — and the failure each prevents compiles and passes everything else.
	/// </remarks>
	[TestFixture]
	public class ArenaHotPathPinsTests
	{
		private const string ArenaMatchPath =
			"Assets/Scripts/Server/Implementation/World/SceneServer/Interactable/InteractableSystem.ArenaMatch.cs";

		private const string ArenaPath =
			"Assets/Scripts/Server/Implementation/World/SceneServer/Interactable/InteractableSystem.Arena.cs";

		private const string GroupFinderPath =
			"Assets/Scripts/Server/Implementation/World/SceneServer/Interactable/InteractableSystem.GroupFinder.cs";

		/// <summary>Source text with line endings normalised.</summary>
		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path).Replace("\r\n", "\n");
		}

		/// <summary>The source with comment lines removed, so prose about a construct does not trip a scan for it.</summary>
		private static string CodeOnly(string source)
		{
			var code = new StringBuilder(source.Length);
			foreach (string line in source.Split('\n'))
			{
				string trimmed = line.TrimStart();
				if (trimmed.StartsWith("//", StringComparison.Ordinal) ||
					trimmed.StartsWith("/*", StringComparison.Ordinal) ||
					trimmed.StartsWith("*", StringComparison.Ordinal))
				{
					continue;
				}
				code.Append(line).Append('\n');
			}
			return code.ToString();
		}

		/// <summary>The body of the method declared by <paramref name="signature"/>, brace-matched.</summary>
		private static string MethodBody(string source, string signature)
		{
			int at = source.IndexOf(signature, StringComparison.Ordinal);
			LogAssert.IsTrue(at >= 0, $"the source must still declare {signature}");

			int open = source.IndexOf('{', at);
			LogAssert.IsTrue(open > at, $"the body of {signature} must be locatable");

			int depth = 0;
			for (int i = open; i < source.Length; ++i)
			{
				if (source[i] == '{')
				{
					++depth;
				}
				else if (source[i] == '}' && --depth == 0)
				{
					return source.Substring(open, i - open + 1);
				}
			}
			LogAssert.Fail($"the body of {signature} never closes");
			return string.Empty;
		}

		[Test]
		public void ArenaSceneSendsAreOneMulticastToTheScene()
		{
			/* S1. Both used to walk every character on the server, asking each for its scene with a
			 * native call and serialising the message again per occupant — on every kill, capture,
			 * announcement and state change. */
			string source = CodeOnly(ReadSource(ArenaMatchPath));

			foreach (string signature in new[]
			{
				"private void BroadcastArenaState(ArenaMatchState state",
				"private void BroadcastArenaEvent(ArenaMatchState state",
			})
			{
				string body = MethodBody(source, signature);
				LogAssert.IsTrue(body.Contains("Server.NetworkWrapper.BroadcastToScene(state.Scene") ||
					(body.Contains("Server.NetworkWrapper.TryGetSceneConnections(state.Scene") && body.Contains("Server.NetworkWrapper.Broadcast(occupants")),
					$"{signature} must send once to the instance's scene");
				LogAssert.IsFalse(body.Contains("CharactersByID"),
					$"{signature} must not walk the server's characters to find the scene's occupants");
				LogAssert.IsFalse(body.Contains(".scene.handle"),
					$"{signature} must not ask characters for their scene");
			}
		}

		[Test]
		public void TheTeamConnectionSetsHaveOneWriter()
		{
			/* A team send is one multicast only while the set is exactly the seats on the team
			 * channel. That holds while every write goes through SyncSeatConnection; a second
			 * writer is how the set drifts from the seats. */
			string source = CodeOnly(ReadSource(ArenaMatchPath));

			string sync = MethodBody(source, "private static void SyncSeatConnection(");
			LogAssert.IsTrue(sync.Contains("ArenaRules.IsOnTeamChannel(seat.Present, seat.Dropped)"),
				"membership of a team set must be decided by the pure rule");

			string outside = source.Replace(sync, string.Empty);
			LogAssert.IsFalse(Regex.IsMatch(outside, @"\.Connection\s*=(?!=)"),
				"only SyncSeatConnection may assign a seat's connection");
			LogAssert.IsFalse(outside.Contains("TeamConnections[seat.Team].Add(") || outside.Contains("TeamConnections[seat.Team].Remove("),
				"only SyncSeatConnection may add to or remove from a team set");
		}

		[Test]
		public void InFlightMarksAreClearedByTheWorkersThatOwnThem()
		{
			/* L7. Both marks used to be cleared by an action on the bounded main-thread queue; a
			 * queue that refused it left the instance never hosted, or no backfill ever seated. */
			string source = CodeOnly(ReadSource(ArenaMatchPath));

			string load = MethodBody(source, "private async Task LoadArenaMatchAsync(");
			int loadFinally = load.IndexOf("finally", StringComparison.Ordinal);
			LogAssert.IsTrue(loadFinally >= 0 && load.IndexOf("arenaMatchesLoading.TryRemove(instanceID", loadFinally, StringComparison.Ordinal) > loadFinally,
				"the match read must clear its loading mark in its own finally");

			string reload = MethodBody(source, "private async Task ReloadArenaSeatsAsync(");
			int reloadFinally = reload.IndexOf("finally", StringComparison.Ordinal);
			LogAssert.IsTrue(reloadFinally >= 0 && reload.IndexOf("Interlocked.Exchange(ref flight.SeatReloadInFlight, 0)", reloadFinally, StringComparison.Ordinal) > reloadFinally,
				"the seat re-read must clear its in-flight mark in its own finally");
			LogAssert.IsFalse(reload.Contains("SeatReloadInFlight = false"),
				"the in-flight mark must not also be cleared from the main thread");
		}

		[Test]
		public void ThePumpMakesNoRoundTripPerRowOrPerKey()
		{
			/* M19. The pump used to pulse and then read back (two round trips), read each matched
			 * row's party membership in series, and count each key on its own. */
			string pump = MethodBody(CodeOnly(ReadSource(GroupFinderPath)), "private async Task RunGroupFinderPumpAsync(");
			LogAssert.IsTrue(pump.Contains("queueService.PulseAsync(ids)"),
				"the heartbeat must return the rows it touched");
			LogAssert.IsFalse(pump.Contains("FetchByCharactersAsync"),
				"the rows must not be read back in a second round trip");
			LogAssert.IsFalse(pump.Contains("DispatchMatchedAsync("),
				"a matched row's membership comes with the pulse; it must not be read per row");
			LogAssert.IsTrue(pump.Contains("FetchBackfillOpeningsAsync("),
				"the backfill transaction must be gated by the cheap read of openings");

			string dungeon = MethodBody(CodeOnly(ReadSource(GroupFinderPath)), "private async Task ProcessWaitingGroupAsync(");
			string arena = MethodBody(CodeOnly(ReadSource(ArenaPath)), "private async Task ProcessWaitingArenaGroupAsync(");
			LogAssert.IsFalse(dungeon.Contains("CountWaitingAsync("), "a dungeon key must use the pump's one count, not its own");
			LogAssert.IsFalse(arena.Contains("CountWaitingAsync("), "an arena key must use the pump's one count, not its own");
			LogAssert.IsTrue(arena.Contains("backfillOpen &&"), "the backfill loop must not start without an opening");
		}
	}
}
