using System;
using System.IO;
using NUnit.Framework;
using FishMMO.Server.Core.World.SceneServer;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Source pins for fixes made during the 2026-09-25 server hot-path fix pass that no
	/// behavioural test can reach without a server: an admission gate, a phase transition's
	/// writes, and the dispatcher's isolation of each behaviour and callback.
	/// </summary>
	/// <remarks>
	/// Text scans in the style of <c>AuditFollowUpPinsTests</c>. Each exists to stop the old shape
	/// quietly coming back in a refactor.
	/// </remarks>
	[TestFixture]
	public class HotPathFixPassPinsTests
	{
		private const string AuthenticatorPath = "Assets/Scripts/Server/Implementation/Authentication/BaseServerAuthenticator.cs";
		private const string ArenaMatchPath = "Assets/Scripts/Server/Implementation/World/SceneServer/Interactable/InteractableSystem.ArenaMatch.cs";
		private const string ServerPath = "Assets/Scripts/Server/Implementation/Server.cs";
		private const string HousingTaxPath = "Assets/Scripts/Server/Implementation/World/SceneServer/Housing/HousingSystem.Tax.cs";

		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path).Replace("\r\n", "\n");
		}

		private static string MethodBody(string source, string signature, string nextSymbol)
		{
			int start = source.IndexOf(signature, StringComparison.Ordinal);
			LogAssert.IsTrue(start >= 0, $"the source must still declare {signature}");

			int end = source.IndexOf(nextSymbol, start + signature.Length, StringComparison.Ordinal);
			LogAssert.IsTrue(end > start, $"the end of {signature} must be locatable");

			return source.Substring(start, end - start);
		}

		/// <summary>
		/// A signed-in client revokes its token on logout over the connection it is authenticated
		/// on. The handshake-window map loses that connection at authentication, so a gate on the
		/// map alone dropped every logout's revocation and left the token valid until expiry. It
		/// also loses the connection when its handshake completes, so a connection still signing in
		/// past that point is admitted by the core's pending tracking.
		/// </summary>
		[Test]
		public void TokenRevocation_AdmitsAuthenticatedConnections()
		{
			string body = MethodBody(ReadSource(AuthenticatorPath),
				"internal void OnServerRevokeTokenBroadcastReceived(",
				"revokeRateLimiter.TryBegin(");

			LogAssert.IsTrue(body.Contains("if (!conn.IsAuthenticated && !connectionStartTimes.ContainsKey(conn.ClientId) && !(Core?.IsAuthPending(conn) ?? false))"),
				"the revoke gate must admit an authenticated connection, one inside its handshake window, and one past its handshake that is still signing in");
		}

		/// <summary>
		/// The handshake timeout bounds the handshake, not the sign-in. It ran from connect until
		/// authentication, so a player who took more than about fifteen seconds to type a
		/// two-factor code was disconnected; it now ends when the core reports the handshake
		/// complete, the moment the core's own limits take over.
		/// </summary>
		[Test]
		public void HandshakeTimeout_EndsWhenTheCoreCompletesTheHandshake()
		{
			string source = ReadSource(AuthenticatorPath);

			string handler = MethodBody(source,
				"internal async Task OnServerClientHandshakeReceivedAsync(",
				"private void EndHandshakeWindow(int clientId)");
			int completed = handler.IndexOf("else if (Core.OnHandshakeReceived(", StringComparison.Ordinal);
			LogAssert.IsTrue(completed >= 0, "the host must act on the core's report that the handshake completed");
			LogAssert.IsTrue(handler.IndexOf("EndHandshakeWindow(clientId);", completed, StringComparison.Ordinal) > completed,
				"and end the handshake window when it does");

			string end = MethodBody(source, "private void EndHandshakeWindow(int clientId)", "private async Task<bool> ProcessConnectionTokenAsync(");
			LogAssert.IsTrue(end.Contains("connectionStartTimes.TryRemove(clientId, out _);"),
				"ending the window removes the start time, which retires its queued deadline");
		}

		/// <summary>
		/// A two-factor prompt whose connection closed used to stay on screen: the code typed into it
		/// went nowhere and thirty seconds later the player was told the server had not responded.
		/// </summary>
		[Test]
		public void LoginPanel_ClosesAndExplainsATwoFactorPromptWhoseConnectionClosed()
		{
			string body = MethodBody(ReadSource("Assets/Scripts/Client/GUI/Login/Login/UITKLogin.cs"),
				"private void ClientManager_OnClientConnectionState(",
				"private void ShowUnexplainedDisconnect()");

			LogAssert.IsTrue(body.Contains("bool twoFactorStepCut = isAuthFlowActive && twoFactorStepActive;"),
				"the Stopped handler must notice that the two-factor step was cut off");
			LogAssert.IsTrue(body.Contains("ShowTwoFactorStepEnded();"),
				"and close the prompt with an explanation");
		}

		/// <summary>
		/// A seat dropped at the gathering timeout belongs to a player who never arrived. Once the
		/// match goes live their row must be vacated like every other absentee's, or the seat can
		/// never be backfilled and both finders refuse the player until the match ends.
		/// </summary>
		[Test]
		public void GoingLive_VacatesDroppedSeats()
		{
			string body = MethodBody(ReadSource(ArenaMatchPath),
				"private void GoLive(ArenaMatchState state)",
				"PersistArenaStatus(state, ArenaMatchStatus.Live);");

			int dropped = body.IndexOf("if (seat.Dropped)", StringComparison.Ordinal);
			LogAssert.IsTrue(dropped >= 0, "GoLive must handle dropped seats explicitly");
			int vacate = body.IndexOf("PersistSeatVacated(state.MatchID, seat.CharacterID);", dropped, StringComparison.Ordinal);
			LogAssert.IsTrue(vacate > dropped, "a dropped seat must be written as vacated when the match goes live");
		}

		/// <summary>
		/// One behaviour that throws must not skip every later behaviour and the frame's periodic
		/// callbacks, and the callbacks must receive the time that really passed.
		/// </summary>
		[Test]
		public void Dispatcher_IsolatesEachBehaviour_AndPassesRealElapsedTime()
		{
			string source = ReadSource(ServerPath);

			string behaviours = MethodBody(source, "private void UpdateServerBehaviours(float deltaTime)", "private void UpdatePeriodicCallbacks(");
			int call = behaviours.IndexOf("behaviour.OnLateUpdate(deltaTime);", StringComparison.Ordinal);
			int tryAt = behaviours.LastIndexOf("try", call, StringComparison.Ordinal);
			LogAssert.IsTrue(call > 0 && tryAt > 0 && tryAt > behaviours.IndexOf("for (int i = 0; i < behaviourSnapshot.Count", StringComparison.Ordinal),
				"each behaviour's OnLateUpdate must run inside its own try, within the loop");

			string callbacks = MethodBody(source, "private void UpdatePeriodicCallbacks(float deltaTime)", "private float RandomInitialPhase(");
			LogAssert.IsTrue(callbacks.Contains("data.Callback?.Invoke(elapsed);"),
				"a periodic callback must receive the real elapsed time, not its nominal interval");
			LogAssert.IsTrue(callbacks.Contains("data.TimeRemaining += data.Interval;"),
				"the overshoot must be carried into the next period, not dropped");
			LogAssert.IsFalse(callbacks.Contains("ex.Message}\");"),
				"a callback's failure must be logged with its full exception");
		}

		/// <summary>
		/// A first pump read whose rows are lost before they reach the cursor must not let the next
		/// read start at a later "now": pinning its start makes the next read cover the span.
		/// </summary>
		[Test]
		public void ChatPump_LostFirstRead_PinsTheFloorAtItsStart()
		{
			var cursor = new ChatPumpCursor(TimeSpan.FromSeconds(10));
			DateTime firstReadStarted = new DateTime(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);

			LogAssert.IsFalse(cursor.Watermark.HasValue, "before any read the cursor starts at the database's now");

			cursor.PinFloor(firstReadStarted);
			LogAssert.AreEqual(firstReadStarted, cursor.Watermark.Value, "the next read starts where the lost first read did");

			// A completed read settles normally, never below the pinned floor.
			cursor.CompleteRead(firstReadStarted.AddSeconds(2), true, null);
			LogAssert.IsTrue(cursor.Watermark.Value >= firstReadStarted, "the watermark never drops below the floor");

			// Once a read has completed, a late pin changes nothing.
			DateTime before = cursor.Watermark.Value;
			cursor.PinFloor(firstReadStarted.AddSeconds(-60));
			LogAssert.AreEqual(before, cursor.Watermark.Value, "a pin after a completed read is ignored");
		}

		/// <summary>
		/// The first tax sweep runs shortly after startup, and retries until something is ready to
		/// sweep, rather than waiting up to a whole interval.
		/// </summary>
		[Test]
		public void HousingTax_FirstSweepRunsSoonAfterStartup()
		{
			string source = ReadSource(HousingTaxPath);

			string phases = MethodBody(source, "private void RandomiseSweepPhases()", "private bool IsTaxEnabled");
			LogAssert.IsTrue(phases.Contains("TaxStartupSweepMinSeconds"),
				"the first tax sweep must be scheduled inside the startup window");
			LogAssert.IsTrue(phases.Contains("taxStartupSweepPending = true;"),
				"startup must arm the retry-until-first-sweep rule");
		}
	}
}
