using System;
using System.Threading.Tasks;
using FishMMO.Auth.Implementation;
using FishMMO.UnitTests.Harness;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;
using AuthTestTrace = FishMMO.UnitTests.Harness.AuthTestTrace;

namespace FishMMO.UnitTests
{
    /// <summary>
    /// Regression tests for the handshake rate limiters.
    /// <para>
    /// Background: the per-IP Phase-2 limiter was originally a fixed single-deadline
    /// debounce. That design assumed a handshake round trip always takes longer than the
    /// debounce interval, so a second completion from the same IP inside the window had to
    /// be an attacker. It was not: on loopback / sub-10 ms links the cookie-challenge echo
    /// completes almost instantly, and two players behind the same NAT (or a fast
    /// reconnect cycle) produce two legitimate completions inside the window. Every one of
    /// those was silently disconnected.
    /// </para>
    /// <para>
    /// The limiter is now a burst window keyed per IP: up to
    /// <c>BaseAuthenticatorCore.HandshakeIpBurstLimit</c> (8) completions per
    /// <c>HandshakeIpWindowSeconds</c> (2 s) pass per IP — the same sustained rate (4/s)
    /// as the old debounce — and only the flood beyond the burst is throttled. The
    /// Unity-side <c>BaseServerAuthenticator</c> gate was likewise narrowed to cookieless
    /// Phase-1 handshakes only, so the cookie echo itself can never trip a limiter.
    /// These tests pin both properties: legitimate bursts complete, floods throttle.
    /// </para>
    /// </summary>
    [TestFixture]
    public class RateLimiterTests
    {
        /// <summary>Mirror of <c>BaseAuthenticatorCore.HandshakeIpBurstLimit</c> (protected const, not visible here).</summary>
        private const int ExpectedBurst = 8;

        /// <summary>Shared NAT IP all burst/flood connections resolve to.</summary>
        private const string SharedNatIp = "203.0.113.9";

        /// <summary>
        /// Drives one full cookie-challenge handshake for a synthetic connection directly
        /// against the server core: Phase-1 cookieless handshake, capture the issued
        /// cookie, then Phase-2 echo — with zero delay between the two messages,
        /// mirroring a sub-10 ms RTT link.
        /// </summary>
        private static void DriveSyntheticHandshake(TestServerCore server, int connId)
        {
            using CryptoHelper.X25519EphemeralKeyPair kp = new CryptoHelper.X25519EphemeralKeyPair();
            byte[] pk = (byte[])kp.PublicKey.Clone();
            // Phase 1: cookieless → server issues a cookie bound to (ip, pk, clientId).
            server.OnHandshakeReceived(connId, pk, cookie: null!, null,
                CryptoHelper.MinSupportedProtocolVersion, CryptoHelper.MaxSupportedProtocolVersion);
            byte[] cookie = server.LastChallengeCookie!;
            // Phase 2: the "natural echo" — sent immediately after the challenge.
            server.OnHandshakeReceived(connId, pk, cookie, null,
                CryptoHelper.MinSupportedProtocolVersion, CryptoHelper.MaxSupportedProtocolVersion);
        }

        /// <summary>
        /// A burst of near-simultaneous handshakes from one IP (household behind a NAT,
        /// fast reconnect cycle) must all complete. Under the old single-deadline debounce,
        /// every completion after the first inside the window was silently disconnected —
        /// this is the regression the burst window fixes.
        /// </summary>
        [Test]
        public async Task Handshake_BurstOfSimultaneousLoginsFromOneIp_AllComplete()
        {
            try
            {
                await AuthTestTrace.LogTestStart(nameof(Handshake_BurstOfSimultaneousLoginsFromOneIp_AllComplete),
                    "Test: Burst of simultaneous handshakes from one shared-NAT IP.\n"
                    + "Procedure: Complete a full cookie-challenge round trip through the real client path, then "
                    + $"{ExpectedBurst - 1} more from the same IP with zero delay between challenge and echo.\n"
                    + "Expected: Every connection completes phase 2; no disconnects.\n"
                    + "Failure: Any completion beyond the first is rate-limited — the low-RTT echo is still tripping a limiter.");
                using AuthTestHarness h = new AuthTestHarness();
                h.Server.AddressResolver = _ => SharedNatIp;

                // Connection 1: the real client path. The echo is immediate (in-process,
                // sub-millisecond — standing in for a sub-10 ms RTT link).
                h.Client.OnConnected();
                LogAssert.AreEqual(1, h.Server.ServerHandshakeCount,
                    "The client-driven cookie-challenge round trip must complete phase 2.");

                // Connections 2..ExpectedBurst: same IP, zero delay between Phase-1 challenge and Phase-2 echo.
                for (int id = 2; id <= ExpectedBurst; id++)
                {
                    DriveSyntheticHandshake(h.Server, id);
                }

                LogAssert.AreEqual(ExpectedBurst, h.Server.ServerHandshakeCount,
                    $"All {ExpectedBurst} handshakes from the shared-NAT IP must complete phase 2.");
                LogAssert.AreEqual(0, h.Server.DisconnectCount,
                    "No connection behind the shared NAT may be rate-limited during a legitimate burst.");
                await AuthTestTrace.Log("RateLimiterTests", "SUCCESS", nameof(Handshake_BurstOfSimultaneousLoginsFromOneIp_AllComplete));
            }
            catch (Exception ex)
            {
                await AuthTestTrace.Log("RateLimiterTests", "FAILURE", $"{nameof(Handshake_BurstOfSimultaneousLoginsFromOneIp_AllComplete)}: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
            finally
            {
                await AuthTestTrace.LogTestEnd(nameof(Handshake_BurstOfSimultaneousLoginsFromOneIp_AllComplete));
            }
        }

        /// <summary>
        /// The sustained per-IP throttle must still hold: beyond the burst allowance, every
        /// additional completion from the same IP inside the window is disconnected. This is
        /// the DoS protection the limiter exists for — the burst fix must not remove it.
        /// </summary>
        [Test]
        public async Task Handshake_SustainedFloodFromOneIp_IsThrottledBeyondBurst()
        {
            try
            {
                const int totalConnections = ExpectedBurst + 4;
                await AuthTestTrace.LogTestStart(nameof(Handshake_SustainedFloodFromOneIp_IsThrottledBeyondBurst),
                    "Test: Sustained handshake flood from one IP.\n"
                    + $"Procedure: Complete {totalConnections} handshakes from one IP inside one window.\n"
                    + $"Expected: Exactly {ExpectedBurst} complete; the remaining {totalConnections - ExpectedBurst} are disconnected.\n"
                    + "Failure: Either a legitimate connection is dropped (limit too tight) or the flood passes (DoS protection lost).");
                using AuthTestHarness h = new AuthTestHarness();
                h.Server.AddressResolver = _ => SharedNatIp;

                h.Client.OnConnected(); // connection 1
                for (int id = 2; id <= totalConnections; id++)
                {
                    DriveSyntheticHandshake(h.Server, id);
                }

                LogAssert.AreEqual(ExpectedBurst, h.Server.ServerHandshakeCount,
                    $"Exactly the {ExpectedBurst}-connection burst allowance may complete per IP per window.");
                LogAssert.AreEqual(totalConnections - ExpectedBurst, h.Server.DisconnectCount,
                    "Every completion beyond the burst allowance must be disconnected (sustained throttle).");
                LogAssert.IsTrue(h.Server.WasDisconnected, "The flood must trip the per-IP limiter.");
                await AuthTestTrace.Log("RateLimiterTests", "SUCCESS", nameof(Handshake_SustainedFloodFromOneIp_IsThrottledBeyondBurst));
            }
            catch (Exception ex)
            {
                await AuthTestTrace.Log("RateLimiterTests", "FAILURE", $"{nameof(Handshake_SustainedFloodFromOneIp_IsThrottledBeyondBurst)}: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
            finally
            {
                await AuthTestTrace.LogTestEnd(nameof(Handshake_SustainedFloodFromOneIp_IsThrottledBeyondBurst));
            }
        }

        /// <summary>
        /// Rate limiting is keyed per source IP, never globally: more than the burst limit
        /// from DISTINCT IPs must all pass, because each IP gets its own window. A future
        /// regression that keys the limiter on a shared/global key (or drops the key
        /// resolution) would fail here.
        /// </summary>
        [Test]
        public async Task Handshake_DistinctSourceIps_AreNotGroupedUnderOneKey()
        {
            try
            {
                const int totalConnections = ExpectedBurst + 2; // 10 > burst — passes only because each IP is its own key.
                await AuthTestTrace.LogTestStart(nameof(Handshake_DistinctSourceIps_AreNotGroupedUnderOneKey),
                    "Test: Handshakes from distinct source IPs are never grouped under one rate-limit key.\n"
                    + $"Procedure: Complete {totalConnections} handshakes, each from a different IP (default resolver).\n"
                    + "Expected: All complete; no disconnects.\n"
                    + "Failure: The limiter is keyed globally or key resolution is broken.");
                using AuthTestHarness h = new AuthTestHarness();

                for (int id = 1; id <= totalConnections; id++)
                {
                    DriveSyntheticHandshake(h.Server, id);
                }

                LogAssert.AreEqual(totalConnections, h.Server.ServerHandshakeCount,
                    "Handshakes from distinct IPs must not consume a shared burst budget.");
                LogAssert.AreEqual(0, h.Server.DisconnectCount,
                    "No connection from a distinct IP may be rate-limited.");
                await AuthTestTrace.Log("RateLimiterTests", "SUCCESS", nameof(Handshake_DistinctSourceIps_AreNotGroupedUnderOneKey));
            }
            catch (Exception ex)
            {
                await AuthTestTrace.Log("RateLimiterTests", "FAILURE", $"{nameof(Handshake_DistinctSourceIps_AreNotGroupedUnderOneKey)}: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
            finally
            {
                await AuthTestTrace.LogTestEnd(nameof(Handshake_DistinctSourceIps_AreNotGroupedUnderOneKey));
            }
        }
    }
}
