using System;
using System.Threading.Tasks;
using FishMMO.Auth.Core;
using FishMMO.UnitTests.Harness;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;
using AuthTestTrace = FishMMO.UnitTests.Harness.AuthTestTrace;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// EditMode tests exercising the full client↔server SRP login flow in-process.
	/// No FishNet, no real sockets — both authenticator cores are paired via
	/// <see cref="AuthTestHarness"/> and routed synchronously.
	/// </summary>
	[TestFixture]
	public class LoginTests
	{
		private const int AwaitTimeoutMs = 5000;

		private static async Task<ClientAuthenticationResult> DriveLogin(AuthTestHarness h, string user, string password)
		{
			LogAssert.IsTrue(h.Client.SetLoginCredentials(user, password, register: false),
				"SetLoginCredentials returned false for valid credentials.");
			h.Client.OnConnected();

			Task<ClientAuthenticationResult> resultTask = h.Client.AuthResultTcs.Task;
			Task completed = await Task.WhenAny(resultTask, Task.Delay(AwaitTimeoutMs));
			LogAssert.IsTrue(object.ReferenceEquals(resultTask, completed), "Login did not complete within timeout.");
			return await resultTask;
		}

		[Test]
		public async Task Login_CorrectCredentials_ReturnsSuccess()
		{
			try
			{
				await AuthTestTrace.LogTestStart(nameof(Login_CorrectCredentials_ReturnsSuccess),
					"Test: Login with correct credentials.\n"
					+ "Procedure: Attempt login with valid username and password.\n"
					+ "Expected: LoginSuccess.\n"
					+ "Failure: Any other result indicates a bug in the login flow.");
				using AuthTestHarness h = new AuthTestHarness();
				h.Store.SeedAccount("alice", "correct horse battery staple");
				ClientAuthenticationResult result = await DriveLogin(h, "alice", "correct horse battery staple");
				LogAssert.AreEqual(ClientAuthenticationResult.LoginSuccess, result,
					$"Expected LoginSuccess for valid credentials, got {result}.");
				LogAssert.IsTrue(h.Client.ReceivedSuccess, "Client did not flag ReceivedSuccess on SRP success.");
				await AuthTestTrace.Log("LoginTests", "SUCCESS", nameof(Login_CorrectCredentials_ReturnsSuccess));
			}
			catch (Exception ex)
			{
				await AuthTestTrace.Log("LoginTests", "FAILURE", $"{nameof(Login_CorrectCredentials_ReturnsSuccess)}: {ex.Message}\n{ex.StackTrace}");
				throw;
			}
			finally
			{
				await AuthTestTrace.LogTestEnd(nameof(Login_CorrectCredentials_ReturnsSuccess));
			}
		}

		[Test]
		public async Task Login_WrongPassword_ReturnsInvalidUsernameOrPassword()
		{
			try
			{
				await AuthTestTrace.LogTestStart(nameof(Login_WrongPassword_ReturnsInvalidUsernameOrPassword),
					"Test: Login with wrong password.\n"
					+ "Procedure: Attempt login with valid username and wrong password.\n"
					+ "Expected: InvalidUsernameOrPassword.\n"
					+ "Failure: Any other result indicates a bug in password validation.");
				using AuthTestHarness h = new AuthTestHarness();
				h.Store.SeedAccount("bob", "real-password");
				ClientAuthenticationResult result = await DriveLogin(h, "bob", "wrong-password");
				LogAssert.AreEqual(ClientAuthenticationResult.InvalidUsernameOrPassword, result,
					$"Expected InvalidUsernameOrPassword on bad password, got {result}.");
				await AuthTestTrace.Log("LoginTests", "SUCCESS", nameof(Login_WrongPassword_ReturnsInvalidUsernameOrPassword));
			}
			catch (Exception ex)
			{
				await AuthTestTrace.Log("LoginTests", "FAILURE", $"{nameof(Login_WrongPassword_ReturnsInvalidUsernameOrPassword)}: {ex.Message}\n{ex.StackTrace}");
				throw;
			}
			finally
			{
				await AuthTestTrace.LogTestEnd(nameof(Login_WrongPassword_ReturnsInvalidUsernameOrPassword));
			}
		}

		[Test]
		public async Task Login_UnknownUser_ReturnsInvalidUsernameOrPasswordWithoutEnumeration()
		{
			try
			{
				await AuthTestTrace.LogTestStart(nameof(Login_UnknownUser_ReturnsInvalidUsernameOrPasswordWithoutEnumeration),
					"Test: Login with unknown user.\n"
					+ "Procedure: Attempt login with a username that does not exist.\n"
					+ "Expected: InvalidUsernameOrPassword.\n"
					+ "Failure: Any other result indicates a bug or user enumeration risk.");
				using AuthTestHarness h = new AuthTestHarness();
				ClientAuthenticationResult result = await DriveLogin(h, "ghost", "any-password");
				LogAssert.AreEqual(ClientAuthenticationResult.InvalidUsernameOrPassword, result,
					"Unknown account must produce InvalidUsernameOrPassword to avoid user enumeration.");
				await AuthTestTrace.Log("LoginTests", "SUCCESS", nameof(Login_UnknownUser_ReturnsInvalidUsernameOrPasswordWithoutEnumeration));
			}
			catch (Exception ex)
			{
				await AuthTestTrace.Log("LoginTests", "FAILURE", $"{nameof(Login_UnknownUser_ReturnsInvalidUsernameOrPasswordWithoutEnumeration)}: {ex.Message}\n{ex.StackTrace}");
				throw;
			}
			finally
			{
				await AuthTestTrace.LogTestEnd(nameof(Login_UnknownUser_ReturnsInvalidUsernameOrPasswordWithoutEnumeration));
			}
		}

		[Test]
		public async Task Login_UnverifiedAccount_ReturnsAccountUnverifiedAfterCorrectProof()
		{
			try
			{
				await AuthTestTrace.LogTestStart(nameof(Login_UnverifiedAccount_ReturnsAccountUnverifiedAfterCorrectProof),
					"Test: Login with unverified account.\n"
					+ "Procedure: Attempt login with a valid but unverified account.\n"
					+ "Expected: AccountUnverified.\n"
					+ "Failure: Any other result indicates a bug in account verification logic.");
				using AuthTestHarness h = new AuthTestHarness();
				h.Store.SeedAccount("eve", "unverified-pass", isVerified: false);
				ClientAuthenticationResult result = await DriveLogin(h, "eve", "unverified-pass");
				LogAssert.AreEqual(ClientAuthenticationResult.AccountUnverified, result,
					$"Expected AccountUnverified after correct SRP M1 proof against unverified account, got {result}.");
				await AuthTestTrace.Log("LoginTests", "SUCCESS", nameof(Login_UnverifiedAccount_ReturnsAccountUnverifiedAfterCorrectProof));
			}
			catch (Exception ex)
			{
				await AuthTestTrace.Log("LoginTests", "FAILURE", $"{nameof(Login_UnverifiedAccount_ReturnsAccountUnverifiedAfterCorrectProof)}: {ex.Message}\n{ex.StackTrace}");
				throw;
			}
			finally
			{
				await AuthTestTrace.LogTestEnd(nameof(Login_UnverifiedAccount_ReturnsAccountUnverifiedAfterCorrectProof));
			}
		}

		[Test]
		public async Task Login_SequentialSessionsSameServer_StateProperlyReset()
		{
			try
			{
				await AuthTestTrace.LogTestStart(nameof(Login_SequentialSessionsSameServer_StateProperlyReset),
					"Test: Two sequential auth sessions through the same server instance.\n"
					+ "Procedure: Log in with connection ID 1, then simulate a reconnect (connection ID 2) via ReconnectAs() and log in again through the same server core.\n"
					+ "Expected: Both sessions return LoginSuccess. The server must isolate per-connection state by connection ID so that state from session 1 (conn=1) does not interfere with session 2 (conn=2).\n"
					+ "Also verifies that each session issues fresh ephemeral material: the server public key and handshake cookie must differ between sessions.\n"
					+ "Failure: If session 2 fails or returns stale/duplicate material, the server's per-connection state is not correctly keyed or reset between connections.");

				using AuthTestHarness h = new AuthTestHarness();
				h.Store.SeedAccount("dan", "first-pass");

				// ── Session 1: connection ID 1 ───────────────────────────────────
				await AuthTestTrace.Log("LoginTests", "STEP", "Starting session 1 (conn=1)...");
				ClientAuthenticationResult r1 = await DriveLogin(h, "dan", "first-pass");
				LogAssert.AreEqual(ClientAuthenticationResult.LoginSuccess, r1,
					$"Session 1 must succeed, got {r1}.");
				LogAssert.IsTrue(h.Client.ReceivedSuccess, "Session 1: client must flag ReceivedSuccess.");

				// Snapshot session-1 material for distinctness assertions after session 2.
				byte[]? pubKey1 = h.Server.LastServerPublicKey != null
					? (byte[])h.Server.LastServerPublicKey.Clone() : null;
				byte[]? cookie1 = h.Server.LastChallengeCookie != null
					? (byte[])h.Server.LastChallengeCookie.Clone() : null;
				int srpVerifyCountAfterSession1 = h.Client.SrpVerifySends.Count;
				int srpProofCountAfterSession1 = h.Client.SrpProofSends.Count;

				// ── Reconnect: simulate disconnect → reconnect under new connection ID ───
				await AuthTestTrace.Log("LoginTests", "STEP", "Reconnecting as conn=2 (simulated disconnect + reconnect)...");
				h.Client.ReconnectAs(2);

				// ── Session 2: connection ID 2 ───────────────────────────────────
				await AuthTestTrace.Log("LoginTests", "STEP", "Starting session 2 (conn=2)...");
				ClientAuthenticationResult r2 = await DriveLogin(h, "dan", "first-pass");
				LogAssert.AreEqual(ClientAuthenticationResult.LoginSuccess, r2,
					$"Session 2 must succeed on the same server instance, got {r2}. "
					+ "If this fails, the server is not correctly isolating per-connection state by connection ID.");
				LogAssert.IsTrue(h.Client.ReceivedSuccess, "Session 2: client must flag ReceivedSuccess.");

				// Session 2 must have emitted its own SRP messages (not short-circuited).
				LogAssert.IsTrue(h.Client.SrpVerifySends.Count > srpVerifyCountAfterSession1,
					"Session 2 must emit its own SrpVerify — the server must have accepted a fresh handshake on conn=2.");
				LogAssert.IsTrue(h.Client.SrpProofSends.Count > srpProofCountAfterSession1,
					"Session 2 must emit its own SrpProof — the full SRP exchange must have run for the new connection.");

				// The server must issue fresh ephemeral material for each connection: same key reuse
				// would indicate the server is sharing or caching per-connection ECDH state.
				if (pubKey1 != null && h.Server.LastServerPublicKey != null)
				{
					bool sameKey = pubKey1.Length == h.Server.LastServerPublicKey.Length;
					if (sameKey)
					{
						for (int i = 0; i < pubKey1.Length; i++)
						{
							if (pubKey1[i] != h.Server.LastServerPublicKey[i]) { sameKey = false; break; }
						}
					}
					LogAssert.IsFalse(sameKey,
						"Session 2 server public key must differ from session 1 — X25519 keys must be per-connection ephemeral.");
				}
				if (cookie1 != null && h.Server.LastChallengeCookie != null)
				{
					bool sameCookie = cookie1.Length == h.Server.LastChallengeCookie.Length;
					if (sameCookie)
					{
						for (int i = 0; i < cookie1.Length; i++)
						{
							if (cookie1[i] != h.Server.LastChallengeCookie[i]) { sameCookie = false; break; }
						}
					}
					LogAssert.IsFalse(sameCookie,
						"Session 2 handshake cookie must differ from session 1 — cookies must be freshly generated per connection.");
				}

				await AuthTestTrace.Log("LoginTests", "SUCCESS", nameof(Login_SequentialSessionsSameServer_StateProperlyReset));
			}
			catch (Exception ex)
			{
				await AuthTestTrace.Log("LoginTests", "FAILURE", $"{nameof(Login_SequentialSessionsSameServer_StateProperlyReset)}: {ex.Message}\n{ex.StackTrace}");
				throw;
			}
			finally
			{
				await AuthTestTrace.LogTestEnd(nameof(Login_SequentialSessionsSameServer_StateProperlyReset));
			}
		}

		[Test]
		public async Task Login_SameCredentials_CaseSensitivePassword_Rejected()
		{
			try
			{
				await AuthTestTrace.LogTestStart(nameof(Login_SameCredentials_CaseSensitivePassword_Rejected),
					"Test: SRP password matching is case-sensitive.\n"
					+ "Procedure: Seed an account with a mixed-case password, then attempt login with the all-uppercase version.\n"
					+ "Expected: InvalidUsernameOrPassword — SRP key derivation must not normalize case.\n"
					+ "Failure: If login succeeds, the SRP derivation path is silently lowercasing the password, which weakens the credential space and may cause cross-platform auth mismatches.");
				using AuthTestHarness h = new AuthTestHarness();
				h.Store.SeedAccount("casey", "CorrectPassword123");
				ClientAuthenticationResult result = await DriveLogin(h, "casey", "CORRECTPASSWORD123");
				LogAssert.AreEqual(ClientAuthenticationResult.InvalidUsernameOrPassword, result,
					$"Uppercase variant of the password must not authenticate (SRP is case-sensitive), got {result}.");
				await AuthTestTrace.Log("LoginTests", "SUCCESS", nameof(Login_SameCredentials_CaseSensitivePassword_Rejected));
			}
			catch (Exception ex)
			{
				await AuthTestTrace.Log("LoginTests", "FAILURE", $"{nameof(Login_SameCredentials_CaseSensitivePassword_Rejected)}: {ex.Message}\n{ex.StackTrace}");
				throw;
			}
			finally
			{
				await AuthTestTrace.LogTestEnd(nameof(Login_SameCredentials_CaseSensitivePassword_Rejected));
			}
		}

		/// <summary>
		/// Number of wrong-password attempts that must trip the per-username lockout.
		/// </summary>
		/// <remarks>
		/// Mirrors <c>SrpAuthenticatorCore.MaxLoginFailuresPerUsername</c>. The constant is
		/// protected rather than public, so this is a deliberate duplicate: if the production value
		/// is lowered this test still passes (it simply overshoots), and if it is raised the test
		/// fails loudly, which is the direction that matters.
		/// </remarks>
		private const int LockoutThreshold = 10;

		/// <summary>
		/// Gives each login attempt a distinct source IP, so the per-IP debounce cannot be what
		/// stops the attack.
		/// </summary>
		/// <remarks>
		/// This is the whole point of the test. Password guessing used to be limited only by
		/// <c>IpAuthAttemptDebounceSeconds</c> — one attempt per second <i>per IP</i> — which is no
		/// limit at all to an attacker with more than one host. Handing every attempt a fresh
		/// address reproduces exactly that, so a pass here can only come from a per-account limit.
		/// </remarks>
		private static void SpreadAttemptsAcrossDistinctIps(AuthTestHarness h)
		{
			/* Keyed on the attempt, not on the call. GetConnectionAddress is consulted twice
			 * during a single handshake — to bind the cookie to an IP, then to verify the echo
			 * against that same IP — so a counter that advanced per call handed out two
			 * different addresses within one attempt, the cookie correctly refused to verify,
			 * and the connection was dropped at the handshake. Every attempt then ended in
			 * silence rather than in an auth result, and these tests timed out long before
			 * reaching the lockout they exist to prove. */
			h.Server.AddressResolver = (_) => $"203.0.113.{(h.Server.ConnectionEpoch % 251) + 1}";
		}

		[Test]
		public async Task Login_DistributedPasswordGuessing_LocksTheAccountOut()
		{
			try
			{
				await AuthTestTrace.LogTestStart(nameof(Login_DistributedPasswordGuessing_LocksTheAccountOut),
					$"Test: per-account lockout after {LockoutThreshold} wrong passwords (S1).\n"
					+ "Procedure: Drive wrong-password attempts against one account, each from a different source IP so the per-IP debounce is bypassed, then present the CORRECT password.\n"
					+ "Expected: every wrong attempt returns InvalidUsernameOrPassword, and the correct password is also refused with InvalidUsernameOrPassword while the lockout holds.\n"
					+ "Failure: if the correct password authenticates, distributed password guessing is unbounded — the per-IP debounce alone does not limit an attacker with more than one host.");
				using AuthTestHarness h = new AuthTestHarness();
				SpreadAttemptsAcrossDistinctIps(h);
				h.Store.SeedAccount("target", "correct horse battery staple");

				for (int attempt = 1; attempt <= LockoutThreshold; attempt++)
				{
					ClientAuthenticationResult wrong = await h.Client.AttemptLogin("target", $"guess-{attempt}");
					LogAssert.AreEqual(ClientAuthenticationResult.InvalidUsernameOrPassword, wrong,
						$"Attempt {attempt}: expected InvalidUsernameOrPassword, got {wrong}.");
				}

				ClientAuthenticationResult afterLockout = await h.Client.AttemptLogin("target", "correct horse battery staple");
				LogAssert.AreEqual(ClientAuthenticationResult.InvalidUsernameOrPassword, afterLockout,
					$"The account must stay locked out even for the correct password, got {afterLockout}.");
				LogAssert.IsTrue(!h.Client.ReceivedSuccess,
					"The client must not have been flagged successful while the account is locked out.");

				await AuthTestTrace.Log("LoginTests", "SUCCESS", nameof(Login_DistributedPasswordGuessing_LocksTheAccountOut));
			}
			catch (Exception ex)
			{
				await AuthTestTrace.Log("LoginTests", "FAILURE", $"{nameof(Login_DistributedPasswordGuessing_LocksTheAccountOut)}: {ex.Message}\n{ex.StackTrace}");
				throw;
			}
			finally
			{
				await AuthTestTrace.LogTestEnd(nameof(Login_DistributedPasswordGuessing_LocksTheAccountOut));
			}
		}

		[Test]
		public async Task Login_CorrectPasswordBeforeThreshold_ClearsTheFailureCount()
		{
			try
			{
				await AuthTestTrace.LogTestStart(nameof(Login_CorrectPasswordBeforeThreshold_ClearsTheFailureCount),
					"Test: a successful sign-in resets the per-account failure count (S1).\n"
					+ "Procedure: fail one short of the lockout threshold, sign in correctly, then fail that many times again and sign in once more.\n"
					+ "Expected: both correct sign-ins return LoginSuccess.\n"
					+ "Failure: if the second sign-in is refused, the counter is not reset on success and an owner who mistypes a few times is one typo away from a lockout for the rest of the window.");
				using AuthTestHarness h = new AuthTestHarness();
				SpreadAttemptsAcrossDistinctIps(h);
				h.Store.SeedAccount("owner", "correct horse battery staple");

				for (int round = 0; round < 2; round++)
				{
					for (int attempt = 1; attempt < LockoutThreshold; attempt++)
					{
						ClientAuthenticationResult wrong = await h.Client.AttemptLogin("owner", $"typo-{round}-{attempt}");
						LogAssert.AreEqual(ClientAuthenticationResult.InvalidUsernameOrPassword, wrong,
							$"Round {round} attempt {attempt}: expected InvalidUsernameOrPassword, got {wrong}.");
					}

					ClientAuthenticationResult ok = await h.Client.AttemptLogin("owner", "correct horse battery staple");
					LogAssert.AreEqual(ClientAuthenticationResult.LoginSuccess, ok,
						$"Round {round}: the correct password must still authenticate below the threshold, got {ok}.");
				}

				await AuthTestTrace.Log("LoginTests", "SUCCESS", nameof(Login_CorrectPasswordBeforeThreshold_ClearsTheFailureCount));
			}
			catch (Exception ex)
			{
				await AuthTestTrace.Log("LoginTests", "FAILURE", $"{nameof(Login_CorrectPasswordBeforeThreshold_ClearsTheFailureCount)}: {ex.Message}\n{ex.StackTrace}");
				throw;
			}
			finally
			{
				await AuthTestTrace.LogTestEnd(nameof(Login_CorrectPasswordBeforeThreshold_ClearsTheFailureCount));
			}
		}

		/// <summary>
		/// Regression test for issue #118 — "incorrect login blocks future login until the
		/// client is restarted".
		/// </summary>
		/// <remarks>
		/// The handshake used to choose the token path whenever a token happened to be held,
		/// without asking what the connection was for. A token outlives a disconnect by design
		/// (World and Scene hops need it), so any return to the sign-in form that did not go
		/// through <c>Client.QuitToLogin</c> left one behind — and the next sign-in then aimed a
		/// <c>TokenAuthBroadcast</c> at a Login Server, which registers no handler for it. The
		/// broadcast is dropped, no reply is ever sent, and every retry does the same thing,
		/// because nothing on that path clears the token.
		/// <para>
		/// Credentials are the discriminator: they are set immediately before a Login Server
		/// connect and nulled the moment the SRP proof is sent, so a hop or a reconnect never
		/// has them and a sign-in always does.
		/// </para>
		/// </remarks>
		[Test]
		public async Task Login_WithStaleTokenHeld_AuthenticatesWithCredentials()
		{
			try
			{
				await AuthTestTrace.LogTestStart(nameof(Login_WithStaleTokenHeld_AuthenticatesWithCredentials),
					"Test: sign in while a token from an earlier session is still held.\n"
					+ "Procedure: stage a token, then set credentials and connect as the login panel does.\n"
					+ "Expected: LoginSuccess, reached over SRP — a verify message must go out.\n"
					+ "Failure: no SRP verify means the client took the token path, which a Login "
					+ "Server never answers; that is issue #118.");
				using AuthTestHarness h = new AuthTestHarness();
				h.Store.SeedAccount("carol", "correct horse battery staple");

				/* Exactly what a previous successful login leaves behind. It does not need to be
				 * a valid token — holding one at all is the condition that used to divert the
				 * flow, and a stale one is the realistic case. */
				LogAssert.IsTrue(h.Client.SetToken("stale-token-from-a-previous-session"),
					"SetToken refused to stage the stale token.");

				int srpVerifyBefore = h.Client.SrpVerifySends.Count;

				LogAssert.IsTrue(h.Client.SetLoginCredentials("carol", "correct horse battery staple", register: false),
					"SetLoginCredentials returned false for valid credentials.");
				h.Client.OnConnected();

				Task<ClientAuthenticationResult> resultTask = h.Client.AuthResultTcs.Task;
				Task completed = await Task.WhenAny(resultTask, Task.Delay(AwaitTimeoutMs));
				LogAssert.IsTrue(object.ReferenceEquals(resultTask, completed),
					"Login did not complete within timeout while a stale token was held.");
				ClientAuthenticationResult result = await resultTask;

				LogAssert.IsTrue(h.Client.SrpVerifySends.Count > srpVerifyBefore,
					"No SRP verify was sent: the client took the token path even though credentials were supplied.");
				LogAssert.AreEqual(ClientAuthenticationResult.LoginSuccess, result,
					$"Expected LoginSuccess while holding a stale token, got {result}.");

				await AuthTestTrace.Log("LoginTests", "SUCCESS", nameof(Login_WithStaleTokenHeld_AuthenticatesWithCredentials));
			}
			catch (Exception ex)
			{
				await AuthTestTrace.Log("LoginTests", "FAILURE", $"{nameof(Login_WithStaleTokenHeld_AuthenticatesWithCredentials)}: {ex.Message}\n{ex.StackTrace}");
				throw;
			}
			finally
			{
				await AuthTestTrace.LogTestEnd(nameof(Login_WithStaleTokenHeld_AuthenticatesWithCredentials));
			}
		}
	}
}