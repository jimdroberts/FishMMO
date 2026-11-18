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
    /// Integration tests for both ServerAuthenticator and TokenServerAuthenticator, including token issuance, renewal, revocation, and error handling.
    /// </summary>
    [TestFixture]
    public class ServerAuthenticatorIntegrationTests
    {
        private const int AwaitTimeoutMs = 5000;

        [Test]
        public async Task FullLoginFlow_SRPAndTokenAuthenticators_Success()
        {
            try
            {
                await AuthTestTrace.LogTestStart(nameof(FullLoginFlow_SRPAndTokenAuthenticators_Success),
                    "Test: Full login flow using both SRP and Token authenticators.\n"
                    + "Procedure: Register, login with SRP, receive token, then login with token.\n"
                    + "Expected: Both flows succeed.\n"
                    + "Failure: Any failure indicates a bug in authenticator integration.");
                // SRP login on harness 1 (login server).
                ClientAuthenticationResult srpResult;
                using (AuthTestHarness h1 = new AuthTestHarness())
                {
                    h1.Store.SeedAccount("alice", "pass1");
                    srpResult = await h1.Client.AttemptLogin("alice", "pass1");
                    LogAssert.AreEqual(ClientAuthenticationResult.LoginSuccess, srpResult, "SRP login should succeed.");
                }
                // Token login on harness 2 (world server — separate connection, matching real game flow).
                using (AuthTestHarness h2 = new AuthTestHarness())
                {
                    string token = h2.Store.IssueValidToken("alice");
                    ClientAuthenticationResult tokenResult = await h2.Client.AttemptTokenLogin(token);
                    LogAssert.AreEqual(ClientAuthenticationResult.LoginSuccess, tokenResult, "Token login should succeed.");
                }
                await AuthTestTrace.Log("ServerAuthenticatorIntegrationTests", "SUCCESS", nameof(FullLoginFlow_SRPAndTokenAuthenticators_Success));
            }
            catch (Exception ex)
            {
                await AuthTestTrace.Log("ServerAuthenticatorIntegrationTests", "FAILURE", $"{nameof(FullLoginFlow_SRPAndTokenAuthenticators_Success)}: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
            finally
            {
                await AuthTestTrace.LogTestEnd(nameof(FullLoginFlow_SRPAndTokenAuthenticators_Success));
            }
        }

        [Test]
        public async Task TokenIssuanceRenewalRevocation_ErrorHandling()
        {
            try
            {
                await AuthTestTrace.LogTestStart(nameof(TokenIssuanceRenewalRevocation_ErrorHandling),
                    "Test: Token issuance, renewal, revocation, and error handling.\n"
                    + "Procedure: Issue, renew, revoke token, and simulate DB/service errors.\n"
                    + "Expected: Correct handling and error reporting.\n"
                    + "Failure: Any incorrect handling indicates a bug in token management.");
                // Part 1 — revocation: fresh harness so each token flow gets a clean connection state.
                using (AuthTestHarness h1 = new AuthTestHarness())
                {
                    string token = h1.Store.IssueValidToken("bob");
                    string renewedToken = h1.Store.RenewToken(token);
                    LogAssert.IsNotNull(renewedToken, "Token renewal should succeed.");
                    h1.Store.RevokeToken(renewedToken);
                    ClientAuthenticationResult revokedResult = await h1.Client.AttemptTokenLogin(renewedToken);
                    LogAssert.AreEqual(ClientAuthenticationResult.TokenRevoked, revokedResult, "Revoked token should be rejected.");
                }
                // Part 2 — DB error: separate harness to avoid stale server connection state from part 1.
                using (AuthTestHarness h2 = new AuthTestHarness())
                {
                    string busyToken = h2.Store.IssueValidToken("bob");
                    h2.Store.SimulateDbError();
                    ClientAuthenticationResult dbErrorResult = await h2.Client.AttemptTokenLogin(busyToken);
                    LogAssert.AreEqual(ClientAuthenticationResult.ServerBusy, dbErrorResult, "DB/service error should return ServerBusy.");
                }
                await AuthTestTrace.Log("ServerAuthenticatorIntegrationTests", "SUCCESS", nameof(TokenIssuanceRenewalRevocation_ErrorHandling));
            }
            catch (Exception ex)
            {
                await AuthTestTrace.Log("ServerAuthenticatorIntegrationTests", "FAILURE", $"{nameof(TokenIssuanceRenewalRevocation_ErrorHandling)}: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
            finally
            {
                await AuthTestTrace.LogTestEnd(nameof(TokenIssuanceRenewalRevocation_ErrorHandling));
            }
        }
    }
}