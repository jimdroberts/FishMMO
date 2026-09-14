using FishMMO.Database.Data;
using FishMMO.Database.Data.Enums;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Which verification codes an account is sent and asked for: <see cref="AccountVerificationRules"/>, the
	/// one rule the login server and the Control Panel share.
	/// </summary>
	/// <remarks>
	/// Pure functions, so the decision table is pinned here directly rather than through a sign-in. The
	/// owner's decisions it holds: any one channel verifies the account; a choice the server has switched off,
	/// or the account cannot receive, falls back to email rather than letting the account in; and only an
	/// account that nothing enabled can reach is let in without a code.
	/// </remarks>
	[TestFixture]
	public class AccountVerificationRulesTests
	{
		private const AccountVerificationChannels None = AccountVerificationChannels.None;
		private const AccountVerificationChannels Email = AccountVerificationChannels.Email;
		private const AccountVerificationChannels Sms = AccountVerificationChannels.Sms;
		private const AccountVerificationChannels Discord = AccountVerificationChannels.Discord;
		private const AccountVerificationChannels All = AccountVerificationRules.All;

		private static AccountData Account(bool verified, AccountVerificationChannels chosen, string email = "player@example.test", string phone = null, string discordUsername = null)
		{
			return new AccountData(
				name: "player",
				salt: "salt",
				verifier: "verifier",
				accessLevel: 1,
				email: email,
				age: 20,
				totpEnabled: false,
				totpSecret: null,
				totpVerifiedAt: null,
				lastTotpWindow: 0,
				verified: verified,
				verifyCode: 0,
				verifyCodeExpiresUtc: null,
				verificationEmailSentAt: null,
				created: default,
				lastLogin: default,
				verificationChannels: (byte)chosen,
				phone: phone,
				discordUsername: discordUsername);
		}

		[Test]
		public void EveryChosenChannelThatIsOnAndReachableIsSent()
		{
			LogAssert.AreEqual(All, AccountVerificationRules.Effective(All, All, All), "Everything chosen, on and reachable is sent.");
			LogAssert.AreEqual(Sms | Discord, AccountVerificationRules.Effective(Sms | Discord, All, All), "Only what was chosen is sent.");
			LogAssert.AreEqual(Sms, AccountVerificationRules.Effective(Sms | Discord, Email | Sms, All), "A chosen channel that is off is dropped while another chosen one remains.");
		}

		[Test]
		public void ASwitchedOffOrUnreachableChoiceFallsBackToEmail()
		{
			LogAssert.AreEqual(Email, AccountVerificationRules.Effective(Discord, Email | Sms, All), "Discord chosen but switched off: an email code, not a free pass.");
			LogAssert.AreEqual(Email, AccountVerificationRules.Effective(Sms, All, Email), "SMS chosen with no number: an email code.");
			LogAssert.AreEqual(Email, AccountVerificationRules.Effective(None, All, All), "Nothing chosen means email.");
		}

		[Test]
		public void WithEmailOffTheFallbackIsTheNextReachableChannel()
		{
			LogAssert.AreEqual(Sms, AccountVerificationRules.Effective(Discord, Sms | Discord, Email | Sms), "Email off and no Discord username: SMS.");
			LogAssert.AreEqual(Discord, AccountVerificationRules.Effective(Email, Discord, All), "Only Discord on: Discord.");
		}

		[Test]
		public void NothingIsAskedWhenTheServerVerifiesNothing()
		{
			LogAssert.AreEqual(None, AccountVerificationRules.Effective(All, None, All), "A server with every switch off asks for nothing.");

			AccountData account = Account(verified: false, chosen: Email);
			LogAssert.AreEqual(None, AccountVerificationRules.Outstanding(account, None), "Nothing is outstanding.");
			LogAssert.IsFalse(AccountVerificationRules.IsWaived(account, None), "Not a waiver: verification is not required at all.");
		}

		[Test]
		public void AnAccountNothingEnabledCanReachIsWaived()
		{
			AccountData account = Account(verified: false, chosen: Sms, email: null, phone: "+447700900123");
			LogAssert.AreEqual(None, AccountVerificationRules.Outstanding(account, Discord), "Only Discord is on and the account has no Discord username.");
			LogAssert.IsTrue(AccountVerificationRules.IsWaived(account, Discord), "No code could ever reach it, so it is let in.");

			AccountData reachable = Account(verified: false, chosen: Sms, phone: "+447700900123");
			LogAssert.AreEqual(Email, AccountVerificationRules.Outstanding(reachable, Email | Discord), "Control: with an email address it is asked for an email code.");
			LogAssert.IsFalse(AccountVerificationRules.IsWaived(reachable, Email | Discord), "Control: not waived.");
		}

		[Test]
		public void AVerifiedAccountOwesNothing()
		{
			AccountData account = Account(verified: true, chosen: All, phone: "+447700900123", discordUsername: "fishfan");
			LogAssert.AreEqual(None, AccountVerificationRules.Outstanding(account, All), "A verified account is asked for nothing.");
			LogAssert.IsFalse(AccountVerificationRules.IsWaived(account, All), "A verified account is not waived.");

			AccountData unverified = Account(verified: false, chosen: All, phone: "+447700900123", discordUsername: "fishfan");
			LogAssert.AreEqual(All, AccountVerificationRules.Outstanding(unverified, All), "Control: the same account unverified is asked on every channel.");
		}

		[Test]
		public void ReceivableFollowsWhatTheAccountHolds()
		{
			LogAssert.AreEqual(None, AccountVerificationRules.Receivable(null, " ", ""), "Blank details receive nothing.");
			LogAssert.AreEqual(All, AccountVerificationRules.Receivable("player@example.test", "+447700900123", "fishfan#1234"), "Every detail receives its channel.");
		}

		[Test]
		public void AThirdIncorrectCodeOpensTheTicket()
		{
			LogAssert.AreEqual(3, AccountVerificationRules.FailedCodesBeforeTicket, "The owner asked for a support ticket after three incorrect codes.");
		}
	}
}
