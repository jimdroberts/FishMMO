using System.Threading.Tasks;
using FishMMO.Auth.Core;
using FishMMO.UnitTests.Harness;
using NUnit.Framework;
using OtpNet;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Any spelling of an account name, or its email address, signs in — and every sign-in is issued
	/// as the account row's own name (issue #267).
	/// </summary>
	/// <remarks>
	/// <para>
	/// SRP used to hash the typed name into the password verifier and into every proof, so a proof
	/// verified only if the name was typed exactly as it had been when the password was set. An
	/// account registered as "Jim" could not sign in as "jim"; and an email sign-in hashed the email,
	/// which never matched a verifier made from the username, so it could not succeed at all — the
	/// game client also refused to send an email before that. SRP now hashes one fixed identity
	/// (<c>SrpIdentity.Value</c>), the client and server both lowercase the identifier, and the name
	/// the player typed only chooses the account.
	/// </para>
	/// <para>
	/// The token is read back by the spelling it was issued for: <c>auth_tokens.account_name</c>
	/// references the lowercase <c>accounts.name</c>, and the world server keys characters by the
	/// same name, so a sign-in by email or in capitals must still be issued as the stored name.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class AccountNameIdentityTests
	{
		private const int AwaitTimeoutMs = 5000;
		private const string TotpSecret = "JBSWY3DPEHPK3PXP";

		[TestCase("JIM")]
		[TestCase("Jim")]
		[TestCase("jIm")]
		public async Task AnySpellingOfTheName_SignsIn_AsTheStoredName(string typed)
		{
			using AuthTestHarness h = new AuthTestHarness();
			h.Store.SeedAccount("jim", "right-password");

			ClientAuthenticationResult result = await h.Client.AttemptLogin(typed, "right-password", AwaitTimeoutMs);

			LogAssert.AreEqual(ClientAuthenticationResult.LoginSuccess, result, $"'{typed}' signs in to the account 'jim'");
			LogAssert.AreEqual("jim", h.Store.GetLastTokenAccountName("jim"),
				"and the token is issued for the account row's name, which the token table references");
		}

		[TestCase("jim@example.test")]
		[TestCase("Jim@Example.TEST")]
		public async Task TheEmailAddress_SignsIn_AsTheStoredName(string typed)
		{
			using AuthTestHarness h = new AuthTestHarness();
			h.Store.SeedAccount("jim", "right-password", email: "jim@example.test");

			ClientAuthenticationResult result = await h.Client.AttemptLogin(typed, "right-password", AwaitTimeoutMs);

			LogAssert.AreEqual(ClientAuthenticationResult.LoginSuccess, result, $"'{typed}' signs in to the account it belongs to");
			LogAssert.AreEqual("jim", h.Store.GetLastTokenAccountName("jim"),
				"issued as the account's name, not the email the player typed");
		}

		[Test]
		public async Task TheEmailAddress_WithTheWrongPassword_IsStillRefused()
		{
			// The control: email sign-in works because the proof is right, not because it is skipped.
			using AuthTestHarness h = new AuthTestHarness();
			h.Store.SeedAccount("jim", "right-password", email: "jim@example.test");

			ClientAuthenticationResult result = await h.Client.AttemptLogin("jim@example.test", "wrong-password", AwaitTimeoutMs);

			LogAssert.AreEqual(ClientAuthenticationResult.InvalidUsernameOrPassword, result, "a wrong password is refused by email too");
			LogAssert.IsNull(h.Store.GetLastTokenHash("jim"), "and no token is issued");
		}

		[Test]
		public async Task ACapitalisedSignIn_WithTwoFactor_IsIssuedTheStoredName()
		{
			using AuthTestHarness h = new AuthTestHarness();
			h.Store.SeedAccount("jim", "right-password", totpEnabled: true, totpSecret: TotpSecret);

			ClientAuthenticationResult first = await h.Client.AttemptLogin("JIM", "right-password", AwaitTimeoutMs);
			LogAssert.AreEqual(ClientAuthenticationResult.TwoFactorRequired, first, "fixture: the account is challenged for its second factor");

			string code = new Totp(Base32Encoding.ToBytes(TotpSecret)).ComputeTotp();
			ClientAuthenticationResult answer = await h.Client.SubmitTwoFactorCode(code, AwaitTimeoutMs);

			LogAssert.AreEqual(ClientAuthenticationResult.LoginSuccess, answer, "the code completes the sign-in");
			LogAssert.AreEqual("jim", h.Store.GetLastTokenAccountName("jim"),
				"the token the two-factor step issues carries the stored name too");
		}

		[Test]
		public async Task ALowercaseSignIn_IsUnchanged()
		{
			// The control: where the typed and stored names already agree, nothing moved.
			using AuthTestHarness h = new AuthTestHarness();
			h.Store.SeedAccount("ann", "right-password");

			ClientAuthenticationResult result = await h.Client.AttemptLogin("ann", "right-password", AwaitTimeoutMs);

			LogAssert.AreEqual(ClientAuthenticationResult.LoginSuccess, result, "a lowercase account signs in");
			LogAssert.AreEqual("ann", h.Store.GetLastTokenAccountName("ann"), "under its own name");
		}
	}
}
