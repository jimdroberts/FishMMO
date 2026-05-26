using System;
using System.Threading.Tasks;
using FishMMO.UnitTests.Harness;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// EditMode tests covering the client-side registration flow through
	/// <c>ClientAuthenticatorCore</c>. Account creation is processed server-side by
	/// <c>AccountCreationSystem</c> (Unity), which lives outside the FishMMO-Auth DLLs;
	/// these tests therefore stop after asserting that the encrypted
	/// <c>CreateAccount</c> broadcast was emitted with well-formed payloads.
	/// </summary>
	[TestFixture]
	public class RegisterTests
	{
		private static async Task DriveHandshakeAndCapture(AuthTestHarness h, string user, string password, string email, int age)
		{
			await AuthTestTrace.Log("RegisterTests", "STEP", $"Attempting registration handshake for user '{user}' with email '{email}' and age {age}.");
			LogAssert.IsTrue(h.Client.SetLoginCredentials(user, password, register: true, email: email, age: age),
				"SetLoginCredentials returned false for valid registration credentials.");
			h.Client.OnConnected();
			await Task.Yield();
		}

		[Test]
		public async Task Register_HappyPath_SendsEncryptedCreateAccountBroadcast()
		{
			try
			{
				await AuthTestTrace.LogTestStart(
					nameof(Register_HappyPath_SendsEncryptedCreateAccountBroadcast),
					"Test: Happy path registration.\n"
					+ "Procedure: Register a new user with valid credentials, email, and age.\n"
					+ "Expected: A well-formed encrypted CreateAccount broadcast is emitted, and the client does not disconnect.\n"
					+ "Failure: If the broadcast is missing, malformed, or the client disconnects, registration flow is broken.\n"
					+ "This test ensures the registration happy path works end-to-end."
				);
				using AuthTestHarness h = new AuthTestHarness();
				await DriveHandshakeAndCapture(h, "frank", "p@ssword1!", "frank@example.test", age: 21);

				await AuthTestTrace.Log("RegisterTests", "STEP", "Checking CreateAccount broadcast count and payloads...");
				LogAssert.AreEqual(1, h.Client.CreateAccountSends.Count,
					"Exactly one CreateAccount broadcast should be emitted on the happy path.");

				TestClientCore.CreateAccountCapture sent = h.Client.CreateAccountSends[0];
				LogAssert.IsNotNull(sent.EncryptedUsername);
				LogAssert.IsTrue(sent.EncryptedUsername.Length > 0, "Encrypted username payload was empty.");
				LogAssert.IsTrue(sent.EncryptedEmail.Length > 0, "Encrypted email payload was empty.");
				LogAssert.IsTrue(sent.EncryptedAge.Length > 0, "Encrypted age payload was empty.");
				LogAssert.IsTrue(sent.EncryptedSalt.Length > 0, "Encrypted salt payload was empty.");
				LogAssert.IsTrue(sent.EncryptedVerifier.Length > 0, "Encrypted verifier payload was empty.");
				LogAssert.IsFalse(h.Client.WasDisconnected, "Client should not have disconnected on a valid handshake.");
				await AuthTestTrace.Log("RegisterTests", "SUCCESS", nameof(Register_HappyPath_SendsEncryptedCreateAccountBroadcast));
			}
			catch (Exception ex)
			{
				await AuthTestTrace.Log("RegisterTests", "FAILURE", $"{nameof(Register_HappyPath_SendsEncryptedCreateAccountBroadcast)}: {ex.Message}\n{ex.StackTrace}");
				throw;
			}
			finally
			{
				await AuthTestTrace.LogTestEnd(nameof(Register_HappyPath_SendsEncryptedCreateAccountBroadcast));
			}
		}

		[Test]
		public async Task Register_EmptyEmail_DisconnectsBeforeCreateAccount()
		{
			try
			{
				await AuthTestTrace.LogTestStart(
					nameof(Register_EmptyEmail_DisconnectsBeforeCreateAccount),
					"Test: Registration with empty email.\n"
					+ "Procedure: Attempt registration with an empty email field.\n"
					+ "Expected: The client rejects the registration before handshake, and no CreateAccount broadcast is sent.\n"
					+ "Failure: If the client accepts or emits a broadcast, email validation is broken.\n"
					+ "This test ensures email is required for registration."
				);
				using AuthTestHarness h = new AuthTestHarness();
				await AuthTestTrace.Log("RegisterTests", "STEP", "Attempting registration with empty email...");
				bool accepted = h.Client.SetLoginCredentials("grace", "pw1", register: true, email: "", age: 25);
				LogAssert.IsFalse(accepted, "Empty email must be rejected by SetLoginCredentials.");
				LogAssert.AreEqual(0, h.Client.CreateAccountSends.Count, "No broadcast should have been sent.");
				await AuthTestTrace.Log("RegisterTests", "SUCCESS", nameof(Register_EmptyEmail_DisconnectsBeforeCreateAccount));
			}
			catch (Exception ex)
			{
				await AuthTestTrace.Log("RegisterTests", "FAILURE", $"{nameof(Register_EmptyEmail_DisconnectsBeforeCreateAccount)}: {ex.Message}\n{ex.StackTrace}");
				throw;
			}
			finally
			{
				await AuthTestTrace.LogTestEnd(nameof(Register_EmptyEmail_DisconnectsBeforeCreateAccount));
			}
		}

		[Test]
		public void Register_InvalidUsername_RejectedByClient()
		{
			try
			{
				AuthTestTrace.LogTestStart(
					nameof(Register_InvalidUsername_RejectedByClient),
					"Test: Registration with invalid username.\n"
					+ "Procedure: Attempt registration with a username shorter than 3 characters.\n"
					+ "Expected: The client rejects the registration before handshake.\n"
					+ "Failure: If the client accepts or proceeds, username validation is broken.\n"
					+ "This test ensures username length rules are enforced."
				).GetAwaiter().GetResult();
				using AuthTestHarness h = new AuthTestHarness();
				AuthTestTrace.Log("RegisterTests", "STEP", "Attempting registration with username < 3 chars...").GetAwaiter().GetResult();
				bool ok = h.Client.SetLoginCredentials("ab", "pw1", register: true, email: "x@y.test", age: 20);
				LogAssert.IsFalse(ok, "Usernames shorter than 3 chars must be rejected by SetLoginCredentials.");
				AuthTestTrace.Log("RegisterTests", "SUCCESS", nameof(Register_InvalidUsername_RejectedByClient)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("RegisterTests", "FAILURE", $"{nameof(Register_InvalidUsername_RejectedByClient)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(Register_InvalidUsername_RejectedByClient)).GetAwaiter().GetResult();
			}
		}

		[Test]
		public void Register_InvalidPassword_RejectedByClient()
		{
			try
			{
				AuthTestTrace.LogTestStart(
					nameof(Register_InvalidPassword_RejectedByClient),
					"Test: Registration with empty password.\n"
					+ "Procedure: Attempt registration with an empty password.\n"
					+ "Expected: The client rejects the registration before handshake.\n"
					+ "Failure: If the client accepts or proceeds, password validation is broken.\n"
					+ "This test ensures password presence is enforced."
				).GetAwaiter().GetResult();
				using AuthTestHarness h = new AuthTestHarness();
				AuthTestTrace.Log("RegisterTests", "STEP", "Attempting registration with empty password...").GetAwaiter().GetResult();
				bool ok = h.Client.SetLoginCredentials("henry", "", register: true, email: "x@y.test", age: 20);
				LogAssert.IsFalse(ok, "Empty password must be rejected by SetLoginCredentials.");
				AuthTestTrace.Log("RegisterTests", "SUCCESS", nameof(Register_InvalidPassword_RejectedByClient)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("RegisterTests", "FAILURE", $"{nameof(Register_InvalidPassword_RejectedByClient)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(Register_InvalidPassword_RejectedByClient)).GetAwaiter().GetResult();
			}
		}

		[Test]
		public async Task Register_DifferentCredentials_ProduceDifferentEncryptedPayloads()
		{
			try
			{
				await AuthTestTrace.LogTestStart(
					nameof(Register_DifferentCredentials_ProduceDifferentEncryptedPayloads),
					"Test: Registration with different credentials produces unique encrypted verifiers.\n"
					+ "Procedure: Register two users with different credentials and compare the encrypted verifier payloads.\n"
					+ "Expected: Each registration produces a unique encrypted verifier.\n"
					+ "Failure: If verifiers are identical, it indicates a cryptographic or registration bug.\n"
					+ "This test ensures registration is unique and secure for each user."
				);
				using AuthTestHarness h1 = new AuthTestHarness();
				await DriveHandshakeAndCapture(h1, "ivan", "passwordA", "ivan@example.test", age: 22);

				using AuthTestHarness h2 = new AuthTestHarness();
				await DriveHandshakeAndCapture(h2, "judy", "passwordB", "judy@example.test", age: 33);

				await AuthTestTrace.Log("RegisterTests", "STEP", "Checking that each registration produced a unique encrypted verifier...");
				LogAssert.AreEqual(1, h1.Client.CreateAccountSends.Count);
				LogAssert.AreEqual(1, h2.Client.CreateAccountSends.Count);

				byte[] v1 = h1.Client.CreateAccountSends[0].EncryptedVerifier;
				byte[] v2 = h2.Client.CreateAccountSends[0].EncryptedVerifier;
				LogAssert.AreNotEqual(System.Convert.ToBase64String(v1), System.Convert.ToBase64String(v2),
					"Two independent registrations must not produce identical encrypted verifiers.");
				await AuthTestTrace.Log("RegisterTests", "SUCCESS", nameof(Register_DifferentCredentials_ProduceDifferentEncryptedPayloads));
			}
			catch (Exception ex)
			{
				await AuthTestTrace.Log("RegisterTests", "FAILURE", $"{nameof(Register_DifferentCredentials_ProduceDifferentEncryptedPayloads)}: {ex.Message}\n{ex.StackTrace}");
				throw;
			}
			finally
			{
				await AuthTestTrace.LogTestEnd(nameof(Register_DifferentCredentials_ProduceDifferentEncryptedPayloads));
			}
		}
	}
}