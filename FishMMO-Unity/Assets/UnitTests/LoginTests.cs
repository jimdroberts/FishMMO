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
		public async Task Login_TwoSequentialAttempts_BothComplete()
		{
			try
			{
				await AuthTestTrace.LogTestStart(nameof(Login_TwoSequentialAttempts_BothComplete),
					"Test: Two sequential login attempts.\n"
					+ "Procedure: Perform two logins in sequence with the same credentials.\n"
					+ "Expected: Both logins succeed.\n"
					+ "Failure: Any failure indicates a bug in session handling or state cleanup.");
				using AuthTestHarness h1 = new AuthTestHarness();
				h1.Store.SeedAccount("dan", "first-pass");
				ClientAuthenticationResult r1 = await DriveLogin(h1, "dan", "first-pass");
				LogAssert.AreEqual(ClientAuthenticationResult.LoginSuccess, r1);

				using AuthTestHarness h2 = new AuthTestHarness();
				h2.Store.SeedAccount("dan", "first-pass");
				ClientAuthenticationResult r2 = await DriveLogin(h2, "dan", "first-pass");
				LogAssert.AreEqual(ClientAuthenticationResult.LoginSuccess, r2);
				await AuthTestTrace.Log("LoginTests", "SUCCESS", nameof(Login_TwoSequentialAttempts_BothComplete));
			}
			catch (Exception ex)
			{
				await AuthTestTrace.Log("LoginTests", "FAILURE", $"{nameof(Login_TwoSequentialAttempts_BothComplete)}: {ex.Message}\n{ex.StackTrace}");
				throw;
			}
			finally
			{
				await AuthTestTrace.LogTestEnd(nameof(Login_TwoSequentialAttempts_BothComplete));
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