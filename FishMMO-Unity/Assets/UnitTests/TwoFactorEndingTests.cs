using System;
using System.Diagnostics;
using System.Threading.Tasks;
using FishMMO.Auth.Core;
using FishMMO.UnitTests.Harness;
using NUnit.Framework;
using OtpNet;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// How a sign-in's two-factor step ends when it does not sign the player in, and what the
	/// server refuses to issue for a connection that closed while it was being checked.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The login server used to close the connection without a word when a two-factor prompt went
	/// unanswered for its window, and answered the last allowed wrong code with TwoFactorInvalid,
	/// which the client reads as "try again" — so it re-opened the prompt just as the connection
	/// closed under it. Both now end with <see cref="ClientAuthenticationResult.TwoFactorExpired"/>,
	/// and the client tells them apart by whether the result answered a code it sent.
	/// </para>
	/// <para>
	/// Both success paths, the SRP proof and a correct two-factor code, used to report the
	/// connection authenticated even if it had closed while the proof or code was being checked.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class TwoFactorEndingTests
	{
		private const string TotpSecret = "JBSWY3DPEHPK3PXP";

		/// <summary>The attempts one sign-in allows. Mirrors the core's private MaxTotpAttempts.</summary>
		private const int MaxTotpAttempts = 5;

		private static string CorrectCode() => new Totp(Base32Encoding.ToBytes(TotpSecret)).ComputeTotp();

		private static string WrongCode()
		{
			string correct = CorrectCode();
			return correct == "000000" ? "111111" : "000000";
		}

		private static async Task<AuthTestHarness> PromptedAsync(string account)
		{
			AuthTestHarness h = new AuthTestHarness();
			h.Store.SeedAccount(account, "right-password", totpEnabled: true, totpSecret: TotpSecret);
			ClientAuthenticationResult first = await h.Client.AttemptLogin(account, "right-password");
			LogAssert.AreEqual(ClientAuthenticationResult.TwoFactorRequired, first, "fixture: the account is asked for its second factor");
			return h;
		}

		/// <summary>Waits for an asynchronous check to finish, for a test whose connection closes and so gets no answer.</summary>
		private static async Task WaitUntilAsync(Func<bool> condition, string what, int timeoutMs = 5000)
		{
			Stopwatch clock = Stopwatch.StartNew();
			while (!condition())
			{
				if (clock.ElapsedMilliseconds > timeoutMs)
				{
					LogAssert.Fail($"timed out waiting until {what}");
				}
				await Task.Delay(10);
			}
		}

		// ── The window ──────────────────────────────────────────────────────

		[Test]
		public async Task AnUnansweredPrompt_IsToldTwoFactorExpired_WhenItsWindowRunsOut()
		{
			using AuthTestHarness h = await PromptedAsync("slowhand");
			int answersBefore = h.Server.AuthResultBroadcastCount;

			h.Server.ClockOffsetSeconds = h.Server.TwoFactorWindowSeconds - 1;
			h.Server.Tick();
			LogAssert.AreEqual(answersBefore, h.Server.AuthResultBroadcastCount, "inside the window nothing is said");
			LogAssert.IsFalse(h.Server.WasDisconnected, "and the prompt stays open");

			h.Server.ClockOffsetSeconds = h.Server.TwoFactorWindowSeconds + 1;
			h.Server.Tick();
			LogAssert.AreEqual(ClientAuthenticationResult.TwoFactorExpired, h.Client.LastResult,
				"an unanswered prompt is told it has ended, not dropped without a word");
			LogAssert.IsFalse(h.Client.LastResultAnsweredTwoFactorCode,
				"and it arrived unasked, which is how the client knows the time ran out");
			LogAssert.IsTrue(h.Server.WasDisconnected, "then the connection is closed");
			LogAssert.IsFalse(h.Server.IsAuthPending(1), "and its sign-in is purged");
		}

		[Test]
		public void AStallThatRunsOutOfTime_IsStillDroppedWithoutAWord()
		{
			using AuthTestHarness h = new AuthTestHarness();
			h.Client.OnConnected();          // a completed handshake, then nothing
			LogAssert.IsTrue(h.Server.IsAuthPending(1), "fixture: the handshake completed and the connection is pending");
			int answersBefore = h.Server.AuthResultBroadcastCount;

			h.Server.ClockOffsetSeconds = PendingAuthRules.ProgressTtlSeconds + 1;
			h.Server.Tick();
			LogAssert.IsTrue(h.Server.WasDisconnected, "a stalled exchange is dropped at its TTL");
			LogAssert.AreEqual(answersBefore, h.Server.AuthResultBroadcastCount, "with nothing said: it is machine work, not a person waiting");
		}

		// ── The attempts ────────────────────────────────────────────────────

		[Test]
		public async Task TheLastWrongCode_IsAnsweredTwoFactorExpired_AsTheAnswerToThatCode()
		{
			using AuthTestHarness h = await PromptedAsync("fumbler");

			for (int attempt = 1; attempt < MaxTotpAttempts; attempt++)
			{
				ClientAuthenticationResult wrong = await h.Client.SubmitTwoFactorCode(WrongCode());
				LogAssert.AreEqual(ClientAuthenticationResult.TwoFactorInvalid, wrong, $"attempt {attempt}: a wrong code is re-prompted");
				LogAssert.IsTrue(h.Client.LastResultAnsweredTwoFactorCode, "and the re-prompt answers the code");
				LogAssert.IsFalse(h.Server.WasDisconnected, "the sign-in goes on");
			}

			ClientAuthenticationResult last = await h.Client.SubmitTwoFactorCode(WrongCode());
			LogAssert.AreEqual(ClientAuthenticationResult.TwoFactorExpired, last,
				"the last allowed wrong code ends the step with an answer the client reads as the end, not as 'try again'");
			LogAssert.IsTrue(h.Client.LastResultAnsweredTwoFactorCode,
				"it answers the code, which is how the client knows the attempts ran out rather than the time");
			LogAssert.IsTrue(h.Server.WasDisconnected, "and the connection is closed");
			LogAssert.IsFalse(h.Server.IsAuthPending(1), "with its sign-in purged");
		}

		[Test]
		public async Task TheLastCodeThatCouldNotBeChecked_IsAnsweredServerBusy()
		{
			using AuthTestHarness h = await PromptedAsync("unlucky");
			h.Server.FailTwoFactorCheck = true;

			for (int attempt = 1; attempt < MaxTotpAttempts; attempt++)
			{
				ClientAuthenticationResult busy = await h.Client.SubmitTwoFactorCode(CorrectCode());
				LogAssert.AreEqual(ClientAuthenticationResult.ServerBusy, busy, $"attempt {attempt}: a code that could not be checked is not called wrong");
				LogAssert.IsFalse(h.Server.WasDisconnected, "and the server keeps the prompt");
			}

			ClientAuthenticationResult last = await h.Client.SubmitTwoFactorCode(CorrectCode());
			LogAssert.AreEqual(ClientAuthenticationResult.ServerBusy, last,
				"the last one is still ServerBusy: the code may have been right, so 'no attempts left' would misstate it");
			LogAssert.IsTrue(h.Server.WasDisconnected, "but the sign-in ends there");
		}

		[Test]
		public async Task TheClient_ForgetsAnUnansweredCode_WhenItsConnectionEnds()
		{
			using AuthTestHarness h = await PromptedAsync("forgetful");
			h.Server.DuringTwoFactorCheck = () => h.Server.CloseConnectionQuietly();
			h.Client.SendTotpCode(CorrectCode());
			await WaitUntilAsync(() => !h.Server.IsAuthPending(1), "the check is abandoned");

			h.Client.OnDisconnected();
			h.Client.OnAuthResultReceived(ClientAuthenticationResult.ServerBusy);
			LogAssert.IsFalse(h.Client.LastResultAnsweredTwoFactorCode,
				"a code sent on a connection that has ended is not what a later result answers");
		}

		// ── Nothing is issued for a closed connection ───────────────────────

		[Test]
		public async Task ACorrectCode_CheckedWhileTheConnectionClosed_IssuesNothing()
		{
			using AuthTestHarness h = await PromptedAsync("vanished");
			h.Server.DuringTwoFactorCheck = () => h.Server.CloseConnectionQuietly();

			h.Client.SendTotpCode(CorrectCode());
			await WaitUntilAsync(() => !h.Server.IsAuthPending(1), "the sign-in is purged");

			LogAssert.AreEqual(0, h.Server.AuthenticatedCount, "the closed connection is not reported authenticated");
			LogAssert.AreEqual(0, h.Server.SrpSuccessBroadcastCount, "no proof or token is sent to it");
			LogAssert.IsNull(h.Store.GetLastTokenHash("vanished"), "and no token is minted for it");
			LogAssert.IsFalse(h.Client.ReceivedSuccess, "the client saw no success");
		}

		[Test]
		public async Task AProof_CheckedWhileTheConnectionClosed_IssuesNothing()
		{
			using AuthTestHarness h = new AuthTestHarness();
			h.Store.SeedAccount("gone", "right-password");
			h.Server.DuringOnlineCheck = () => h.Server.CloseConnectionQuietly();

			h.Client.BeginLogin("gone", "right-password");
			await WaitUntilAsync(() => !h.Server.IsAuthPending(1), "the sign-in is purged");

			LogAssert.AreEqual(0, h.Server.AuthenticatedCount, "the closed connection is not reported authenticated");
			LogAssert.AreEqual(0, h.Server.SrpSuccessBroadcastCount, "no proof or token is sent to it");
			LogAssert.IsNull(h.Store.GetLastTokenHash("gone"), "and no token is minted: the check comes before the token");
		}

		[Test]
		public async Task AConnectionClosingAsItsTokenIsMinted_IsNotReportedAuthenticated()
		{
			using AuthTestHarness h = new AuthTestHarness();
			h.Store.SeedAccount("justmissed", "right-password");
			h.Server.DuringTokenPersist = () => h.Server.CloseConnectionQuietly();

			h.Client.BeginLogin("justmissed", "right-password");
			await WaitUntilAsync(() => !h.Server.IsAuthPending(1), "the sign-in is purged");

			LogAssert.AreEqual(0, h.Server.AuthenticatedCount,
				"completion checks the connection where the answer is final, after the token write");
			LogAssert.AreEqual(0, h.Server.SrpSuccessBroadcastCount, "and sends nothing to it");
			LogAssert.IsFalse(h.Client.ReceivedSuccess, "the client saw no success");
		}

		[Test]
		public async Task ALiveConnection_StillSignsIn()
		{
			using AuthTestHarness h = await PromptedAsync("present");
			ClientAuthenticationResult right = await h.Client.SubmitTwoFactorCode(CorrectCode());
			LogAssert.AreEqual(ClientAuthenticationResult.LoginSuccess, right, "the right code signs in");
			LogAssert.IsTrue(h.Client.LastResultAnsweredTwoFactorCode, "the success answers the code");
			LogAssert.AreEqual(1, h.Server.AuthenticatedCount, "reported authenticated exactly once");
			LogAssert.IsNotNull(h.Store.GetLastTokenHash("present"), "with its token minted");
		}
	}
}
