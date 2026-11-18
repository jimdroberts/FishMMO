using System;
using System.Threading.Tasks;
using FishMMO.Auth.Core;
using FishMMO.UnitTests.Harness;
using NUnit.Framework;
using OtpNet;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// A database fault during sign-in is answered as a fault — ServerBusy — and never as the
	/// player's mistake (issue #267).
	/// </summary>
	/// <remarks>
	/// <para>
	/// Three hooks of <c>SrpAuthenticatorCore</c> could not say "the database failed", so each fault
	/// took the path of a wrong answer: a token whose hash was never recorded still reported
	/// LoginSuccess and was then refused as revoked at the world server; an unreachable account
	/// lookup was treated as a missing account, answered "wrong password" and counted as a failed
	/// sign-in; and a two-factor code that could not be checked was reported TwoFactorInvalid and
	/// counted toward the two-factor lockout. The hooks now report the fault, and these tests drive
	/// each one through the real client and server cores.
	/// </para>
	/// <para>
	/// ServerBusy depends on no account, so answering with it is no enumeration oracle.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class AuthDatabaseFaultTests
	{
		private const int AwaitTimeoutMs = 5000;
		private const string TotpSecret = "JBSWY3DPEHPK3PXP";

		[Test]
		public async Task ATokenWhoseHashWasNotRecorded_IsRefused_NotReportedAsSuccess()
		{
			using AuthTestHarness h = new AuthTestHarness();
			h.Store.SeedAccount("tokenless", "right-password");
			h.Server.FailTokenPersist = true;

			ClientAuthenticationResult result = await h.Client.AttemptLogin("tokenless", "right-password", AwaitTimeoutMs);

			LogAssert.AreEqual(ClientAuthenticationResult.ServerBusy, result,
				"a login whose token the database does not know must be refused, not reported as a success the world server will then revoke");
			LogAssert.IsFalse(h.Client.ReceivedSuccess, "the client must not have been told it signed in");
			LogAssert.IsNull(h.Store.GetLastTokenHash("tokenless"), "and no token hash was recorded");
		}

		[Test]
		public async Task AnUnreachableAccountLookup_IsServerBusy_NotAWrongPassword()
		{
			using AuthTestHarness h = new AuthTestHarness();
			h.Store.SeedAccount("blip", "right-password");
			h.Server.FailAccountLookup = true;

			ClientAuthenticationResult result = await h.Client.AttemptLogin("blip", "right-password", AwaitTimeoutMs);

			LogAssert.AreEqual(ClientAuthenticationResult.ServerBusy, result,
				"a lookup the database could not answer is not a missing account");
			LogAssert.AreEqual(0, h.Store.LoginFailureRecordCount("blip"),
				"and it is not counted as a failed sign-in toward the lockout");
		}

		[Test]
		public async Task ATwoFactorCodeThatCouldNotBeChecked_IsServerBusy_AndNotCounted()
		{
			using AuthTestHarness h = new AuthTestHarness();
			h.Store.SeedAccount("totp-blip", "right-password", totpEnabled: true, totpSecret: TotpSecret);

			ClientAuthenticationResult first = await h.Client.AttemptLogin("totp-blip", "right-password", AwaitTimeoutMs);
			LogAssert.AreEqual(ClientAuthenticationResult.TwoFactorRequired, first, "fixture: the account is challenged for its second factor");

			h.Server.FailTwoFactorCheck = true;
			string correctCode = new Totp(Base32Encoding.ToBytes(TotpSecret)).ComputeTotp();
			ClientAuthenticationResult answer = await h.Client.SubmitTwoFactorCode(correctCode, AwaitTimeoutMs);

			LogAssert.AreEqual(ClientAuthenticationResult.ServerBusy, answer,
				"a correct code the database could not check must not be reported as TwoFactorInvalid");
			LogAssert.AreEqual(0, h.Server.TwoFactorFailuresRecorded,
				"and must not count toward the two-factor lockout");
		}

		[Test]
		public async Task AWrongTwoFactorCode_IsStillInvalid_AndStillCounted()
		{
			// The control: only a fault changed. A code that WAS checked and is wrong keeps its answer.
			using AuthTestHarness h = new AuthTestHarness();
			h.Store.SeedAccount("totp-wrong", "right-password", totpEnabled: true, totpSecret: TotpSecret);

			ClientAuthenticationResult first = await h.Client.AttemptLogin("totp-wrong", "right-password", AwaitTimeoutMs);
			LogAssert.AreEqual(ClientAuthenticationResult.TwoFactorRequired, first, "fixture: the account is challenged for its second factor");

			string correctCode = new Totp(Base32Encoding.ToBytes(TotpSecret)).ComputeTotp();
			string wrongCode = correctCode == "000000" ? "111111" : "000000";
			ClientAuthenticationResult answer = await h.Client.SubmitTwoFactorCode(wrongCode, AwaitTimeoutMs);

			LogAssert.AreEqual(ClientAuthenticationResult.TwoFactorInvalid, answer, "a wrong code is still TwoFactorInvalid");
			LogAssert.AreEqual(1, h.Server.TwoFactorFailuresRecorded, "and still counts toward the lockout");
		}
	}
}
