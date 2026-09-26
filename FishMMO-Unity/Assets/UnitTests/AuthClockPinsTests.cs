using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using FishMMO.Server.Implementation.LoginServer;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;
using ServerLastSeenCache = FishMMO.Server.Core.Collections.LastSeenCacheTracker<int, string>;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The game-server side of the authentication clock and login-queue changes: the debounces and
	/// IP caches the authenticators keep are timed on the monotonic clock, the login queue admits at
	/// its new default, and the login panel says how a two-factor step ended.
	/// </summary>
	/// <remarks>
	/// The tracker half is behavioural. The rest are source scans in the style of
	/// <see cref="HotPathFixPassPinsTests"/>: each names a construct whose return would bring the old
	/// behaviour back without failing any behavioural test that can run without a server.
	/// </remarks>
	[TestFixture]
	public class AuthClockPinsTests
	{
		private const string Gui = "Assets/Scripts/Client/GUI/";

		/// <summary>The authentication files whose trackers were moved off the wall clock.</summary>
		private static readonly string[] AuthTrackerCallers =
		{
			"Assets/Scripts/Server/Implementation/Authentication/BaseServerAuthenticator.cs",
			"Assets/Scripts/Server/Implementation/Authentication/ServerAuthenticator.cs",
			"Assets/Scripts/Server/Implementation/World/WorldServer/Authentication/WorldServerAuthenticator.cs",
			"Assets/Scripts/Server/Implementation/LoginServer/AccountCreation/AccountCreationSystem.cs",
		};

		/// <summary>
		/// A call on one of the moved trackers that hands it the wall clock. The redeemed-token cache
		/// is not among them: it guards a wall-clock validity window and stays on that clock.
		/// </summary>
		private static readonly Regex WallClockTrackerCall = new Regex(
			@"(handshakeRateLimiter|tokenMintRateLimiter|revokeRateLimiter|revokeGlobalRateLimiter|connectionRealIps|ConnectionIpCache|ConnectionEncryptionCache|loginAttemptByAccount|verifyRateLimiter)\s*\??\.\s*(TryBegin|SweepExpired|TryGetAndTouch|Upsert)\s*\([^;]*DateTime\.UtcNow",
			RegexOptions.CultureInvariant);

		[Test]
		public void TheMonotonicCacheOverloads_ExpireByLastSeen()
		{
			var cache = new ServerLastSeenCache();
			TimeSpan ttl = TimeSpan.FromMinutes(30);
			cache.Upsert(1, "203.0.113.7", 1000.0);
			cache.Upsert(2, "198.51.100.9", 1010.0);

			LogAssert.IsTrue(cache.TryGetAndTouch(1, 1000.0 + 1700, out string ip) && ip == "203.0.113.7", "a live entry is served and touched");
			LogAssert.AreEqual(1, cache.SweepExpired(1010.0 + 1800, ttl, 64, 64), "the untouched entry lapses thirty minutes after it was last seen");
			LogAssert.IsTrue(cache.TryGetAndTouch(1, 1010.0 + 1800, out _), "the touched one is kept");
		}

		[Test]
		public void TheAuthenticatorsDebouncesAndIpCaches_AreNeverHandedTheWallClock()
		{
			foreach (string path in AuthTrackerCallers)
			{
				string code = SourceScanPins.ReadCode(path);
				Match found = WallClockTrackerCall.Match(code);
				LogAssert.IsFalse(found.Success,
					$"{path}: '{(found.Success ? found.Value : string.Empty)}' times a duration on the host clock; a stepped clock held every key for the size of the step");
			}
		}

		[Test]
		public void TheLoginQueue_AdmitsFiftyASecond_Unconfigured()
		{
			LogAssert.AreEqual(50f, LoginQueueSystem.DefaultAdmissionRatePerSecond,
				"five a second took a hundred seconds to drain a full queue while the SRP pipeline sat idle");
			string code = SourceScanPins.ReadCode("Assets/Scripts/Server/Implementation/LoginServer/LoginQueueSystem.cs");
			LogAssert.IsTrue(code.Contains("\"LoginQueueAdmissionRatePerSecond\", DefaultAdmissionRatePerSecond,"),
				"the configuration read falls back to the same default");
		}

		[Test]
		public void TheLoginServer_DefaultsItsPendingCapFromItsChannels()
		{
			string code = SourceScanPins.ReadCode("Assets/Scripts/Server/Implementation/Authentication/ServerAuthenticator.cs");
			LogAssert.IsTrue(code.Contains("PendingAuthRules.DefaultLoginPendingCap(core.VerifyChannelCapacity, core.ProofChannelCapacity)"),
				"unset, AuthMaxPendingConnections is what the configured verify and proof channels hold, so the login queue can engage");
		}

		[Test]
		public void TheLoginPanel_EndsTheTwoFactorStep_OnTwoFactorExpired()
		{
			string code = SourceScanPins.ReadCode(Gui + "Login/Login/UITKLogin.cs");
			string handler = SourceScanPins.Body(code, "private void Authenticator_OnClientAuthenticationResult(");
			LogAssert.IsNotNull(handler, "the result handler must still exist");
			LogAssert.IsTrue(handler.Contains("case ClientAuthenticationResult.TwoFactorExpired:"), "TwoFactorExpired must be handled, not left to the default");
			LogAssert.IsTrue(handler.Contains("OnTwoFactorExpired(Client.LoginAuthenticator.LastResultAnsweredTwoFactorCode);"),
				"with whether it answered a code, which is what says the attempts ran out rather than the time");

			string body = SourceScanPins.Body(code, "private void OnTwoFactorExpired(");
			LogAssert.IsNotNull(body, "OnTwoFactorExpired must still exist");
			string order = SourceScanPins.InOrder(body, "CloseTwoFactorPrompt();", "OnLoginAuthenticationDialog(");
			LogAssert.IsNull(order, "the prompt is taken down before the notice, which the dialog would otherwise refuse: " + order);
		}
	}
}
