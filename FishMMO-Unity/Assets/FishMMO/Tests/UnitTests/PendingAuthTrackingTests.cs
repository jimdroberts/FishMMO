using System.Threading.Tasks;
using FishMMO.Auth.Core;
using FishMMO.Auth.Implementation;
using FishMMO.UnitTests.Harness;
using NUnit.Framework;
using OtpNet;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The authenticator core's pending-authentication tracking: the cap that decides when the
	/// login queue engages, and which connections hold a slot under it.
	/// </summary>
	/// <remarks>
	/// Before <see cref="BaseAuthenticatorCore{TConnection}.EndAuthTracking"/> nothing ended
	/// tracking when a connection authenticated. Every signed-in connection went on holding a
	/// slot for the full stale TTL, and the stale sweep then reported it as a recycled connection.
	/// Each connection here comes from its own address, so the per-IP burst limiter never
	/// interferes.
	/// </remarks>
	[TestFixture]
	public class PendingAuthTrackingTests
	{
		/// <summary>
		/// One full cookie-challenge handshake for a synthetic connection, straight against the
		/// server core, with no delay between challenge and echo.
		/// </summary>
		private static void DriveHandshake(TestServerCore server, int connId)
		{
			using CryptoHelper.X25519EphemeralKeyPair kp = new CryptoHelper.X25519EphemeralKeyPair();
			byte[] pk = (byte[])kp.PublicKey.Clone();
			server.OnHandshakeReceived(connId, pk, cookie: null!, null,
				CryptoHelper.MinSupportedProtocolVersion, CryptoHelper.MaxSupportedProtocolVersion);
			byte[] cookie = server.LastChallengeCookie!;
			server.OnHandshakeReceived(connId, pk, cookie, null,
				CryptoHelper.MinSupportedProtocolVersion, CryptoHelper.MaxSupportedProtocolVersion);
		}

		[Test]
		public void TheCap_DefersHandshakesPastIt_AndAnAuthenticatedConnectionFreesItsSlot()
		{
			using AuthTestHarness h = new AuthTestHarness();
			h.Server.MaxPendingAuthConnections = 2;

			h.Client.OnConnected();          // connection 1, through the real client path
			DriveHandshake(h.Server, 2);
			LogAssert.AreEqual(2, h.Server.ServerHandshakeCount, "two handshakes complete under a cap of two");
			LogAssert.AreEqual(2, h.Server.PendingAuthCount, "and both are pending");

			DriveHandshake(h.Server, 3);
			LogAssert.AreEqual(2, h.Server.ServerHandshakeCount, "a third is deferred (no login queue here, so dropped)");
			LogAssert.AreEqual(2, h.Server.PendingAuthCount, "and takes no slot");

			h.Server.EndAuthTracking(2);     // connection 2 authenticated
			LogAssert.AreEqual(1, h.Server.PendingAuthCount, "an authenticated connection leaves the pending set at once, not after the stale TTL");

			DriveHandshake(h.Server, 4);
			LogAssert.AreEqual(3, h.Server.ServerHandshakeCount, "so the next handshake gets its slot");
		}

		[Test]
		public void TheCap_IsNeverBelowOne()
		{
			using AuthTestHarness h = new AuthTestHarness();
			h.Server.MaxPendingAuthConnections = 0;
			LogAssert.AreEqual(1, h.Server.MaxPendingAuthConnections, "a misconfigured zero would refuse every sign-in");
		}

		/// <summary>
		/// The Login Server's cap defaults to what its SRP channels hold between them, where the login
		/// queue starts serving players better than admission does; World and Scene servers keep
		/// 10,000, a memory ceiling with no queue behind it.
		/// </summary>
		/// <remarks>
		/// This pinned 10,000 on the SRP core too. At that figure the login queue could never engage:
		/// pending sign-ins are their arrival rate times how long each takes, and the global handshake
		/// limit keeps that far below 10,000 unless the pipeline has stalled.
		/// </remarks>
		[Test]
		public void TheCap_DefaultsToTheSrpChannels_OnTheLoginServer_AndTo10000Elsewhere()
		{
			using AuthTestHarness h = new AuthTestHarness();
			LogAssert.AreEqual(PendingAuthRules.DefaultLoginPendingCap(h.Server.VerifyChannelCapacity, h.Server.ProofChannelCapacity),
				h.Server.MaxPendingAuthConnections, "unconfigured, the login server's cap is what its verify and proof channels hold");
			LogAssert.AreEqual(1000, h.Server.MaxPendingAuthConnections, "which is 1,000 with the default 500 + 500");

			LogAssert.AreEqual(5000, PendingAuthRules.DefaultLoginPendingCap(2000, 3000), "enlarged channels move the threshold with them");
			LogAssert.AreEqual(1 + PendingAuthRules.MaxSrpChannelCapacity, PendingAuthRules.DefaultLoginPendingCap(0, 1_000_000),
				"each channel counts as the capacity it is actually built with");

			LogAssert.AreEqual(10000, BaseAuthenticatorCore<int>.DefaultMaxPendingAuthConnections,
				"the token servers' default is unchanged");
		}

		/// <summary>
		/// The hand-off from the host's handshake timeout to the core: the handshake reports its own
		/// completion, and at that moment the core is tracking the connection.
		/// </summary>
		/// <remarks>
		/// The host used to time the handshake until authentication, so its fifteen-second handshake
		/// timeout bounded the whole sign-in and cut off players at the two-factor prompt. It now
		/// ends the window when this returns true, and nothing else may make it return true.
		/// </remarks>
		[Test]
		public void TheHandshake_ReportsItsCompletion_AtTheMomentTheCoreStartsTracking()
		{
			using AuthTestHarness h = new AuthTestHarness();
			h.Server.MaxPendingAuthConnections = 1;

			using CryptoHelper.X25519EphemeralKeyPair kp = new CryptoHelper.X25519EphemeralKeyPair();
			byte[] pk = (byte[])kp.PublicKey.Clone();
			LogAssert.IsFalse(h.Server.OnHandshakeReceived(2, pk, cookie: null!, null,
				CryptoHelper.MinSupportedProtocolVersion, CryptoHelper.MaxSupportedProtocolVersion),
				"a cookie challenge is not a completed handshake");
			LogAssert.IsFalse(h.Server.IsAuthPending(2), "and starts no tracking");

			byte[] cookie = h.Server.LastChallengeCookie!;
			LogAssert.IsTrue(h.Server.OnHandshakeReceived(2, pk, cookie, null,
				CryptoHelper.MinSupportedProtocolVersion, CryptoHelper.MaxSupportedProtocolVersion),
				"the cookie echo completes it");
			LogAssert.IsTrue(h.Server.TryGetPendingAuthPhase(2, out PendingAuthPhase phase) && phase == PendingAuthPhase.Authenticating,
				"and the core is tracking the connection the moment it says so, so no connection is left bounded by neither limit");

			LogAssert.IsFalse(h.Server.OnHandshakeReceived(2, pk, cookie, null,
				CryptoHelper.MinSupportedProtocolVersion, CryptoHelper.MaxSupportedProtocolVersion),
				"a duplicate echo reports nothing new");

			using CryptoHelper.X25519EphemeralKeyPair kp3 = new CryptoHelper.X25519EphemeralKeyPair();
			byte[] pk3 = (byte[])kp3.PublicKey.Clone();
			h.Server.OnHandshakeReceived(3, pk3, cookie: null!, null,
				CryptoHelper.MinSupportedProtocolVersion, CryptoHelper.MaxSupportedProtocolVersion);
			LogAssert.IsFalse(h.Server.OnHandshakeReceived(3, pk3, h.Server.LastChallengeCookie!, null,
				CryptoHelper.MinSupportedProtocolVersion, CryptoHelper.MaxSupportedProtocolVersion),
				"a handshake deferred at the cap is not complete, so the host keeps timing it (the login queue exempts it there)");
			LogAssert.IsFalse(h.Server.IsAuthPending(3), "and the core is not tracking it");
		}

		private const string TotpSecret = "JBSWY3DPEHPK3PXP";

		/// <summary>
		/// A player at the two-factor prompt is on the prompt's window, not the progress TTL, and
		/// each answer moves the connection where it belongs.
		/// </summary>
		[Test]
		public async Task ATwoFactorPrompt_IsTrackedAsAwaitingTheCode_AndEachAnswerMovesIt()
		{
			using AuthTestHarness h = new AuthTestHarness();
			h.Store.SeedAccount("prompted", "right-password", totpEnabled: true, totpSecret: TotpSecret);

			ClientAuthenticationResult first = await h.Client.AttemptLogin("prompted", "right-password");
			LogAssert.AreEqual(ClientAuthenticationResult.TwoFactorRequired, first, "fixture: the account is asked for its second factor");
			LogAssert.IsTrue(h.Server.TryGetPendingAuthPhase(1, out PendingAuthPhase phase), "the prompted connection is still pending");
			LogAssert.AreEqual(PendingAuthPhase.AwaitingTwoFactor, phase, "on the two-factor window, set before the prompt went out");

			string correctCode = new Totp(Base32Encoding.ToBytes(TotpSecret)).ComputeTotp();
			string wrongCode = correctCode == "000000" ? "111111" : "000000";
			ClientAuthenticationResult wrong = await h.Client.SubmitTwoFactorCode(wrongCode);
			LogAssert.AreEqual(ClientAuthenticationResult.TwoFactorInvalid, wrong, "fixture: a wrong code is re-prompted");
			LogAssert.IsTrue(h.Server.TryGetPendingAuthPhase(1, out phase) && phase == PendingAuthPhase.AwaitingTwoFactor,
				"a re-prompt puts the connection back on a window");

			ClientAuthenticationResult right = await h.Client.SubmitTwoFactorCode(correctCode);
			LogAssert.AreEqual(ClientAuthenticationResult.LoginSuccess, right, "the right code signs in");
			LogAssert.IsTrue(h.Server.TryGetPendingAuthPhase(1, out phase) && phase == PendingAuthPhase.Authenticating,
				"checking it was machine work (this harness has no host to call EndAuthTracking on success)");
		}

		[Test]
		public void TheTwoFactorWindow_DefaultsToTwoMinutes_AndAMisconfiguredOneIsClamped()
		{
			LogAssert.AreEqual(120f, BaseAuthenticatorCore<int>.DefaultTwoFactorWindowSeconds, "the core's default is two minutes");

			using AuthTestHarness h = new AuthTestHarness();
			LogAssert.AreEqual(120f, h.Server.TwoFactorWindowSeconds, "unconfigured, a prompt gets the default");
			h.Server.TwoFactorWindowSeconds = 1f;
			LogAssert.AreEqual((float)PendingAuthRules.MinTwoFactorWindowSeconds, h.Server.TwoFactorWindowSeconds,
				"a window no person could use is raised, not honoured");
			h.Server.TwoFactorWindowSeconds = 300f;
			LogAssert.AreEqual(300f, h.Server.TwoFactorWindowSeconds, "a sensible one is kept");
		}
	}
}
