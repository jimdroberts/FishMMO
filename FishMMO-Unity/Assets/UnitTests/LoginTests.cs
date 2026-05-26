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
	}
}