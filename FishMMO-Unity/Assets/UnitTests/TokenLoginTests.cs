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
    /// Tests for token-based authentication (TokenServerAuthenticator) covering login, renewal, revocation, and edge/failure cases.
    /// </summary>
    [TestFixture]
    public class TokenLoginTests
    {
        private const int AwaitTimeoutMs = 5000;

        // Simulates a full token-based login flow.
        private static async Task<ClientAuthenticationResult> DriveTokenLogin(AuthTestHarness h, string token)
        {
            LogAssert.IsTrue(h.Client.SetToken(token), "SetToken returned false for valid token.");
            h.Client.OnConnected();
            Task<ClientAuthenticationResult> resultTask = h.Client.AuthResultTcs.Task;
            Task completed = await Task.WhenAny(resultTask, Task.Delay(AwaitTimeoutMs));
            LogAssert.IsTrue(object.ReferenceEquals(resultTask, completed), "Token login did not complete within timeout.");
            return await resultTask;
        }

        [Test]
        public async Task TokenLogin_ValidToken_ReturnsSuccess()
        {
            try
            {
                await AuthTestTrace.LogTestStart(nameof(TokenLogin_ValidToken_ReturnsSuccess),
                    "Test: Token login with valid token.\n"
                    + "Procedure: Attempt login with a valid, unexpired token.\n"
                    + "Expected: LoginSuccess.\n"
                    + "Failure: Any other result indicates a bug in token login flow.");
                using AuthTestHarness h = new AuthTestHarness();
                string validToken = h.Store.IssueValidToken("alice");
                ClientAuthenticationResult result = await DriveTokenLogin(h, validToken);
                LogAssert.AreEqual(ClientAuthenticationResult.LoginSuccess, result,
                    $"Expected LoginSuccess for valid token, got {result}.");
                await AuthTestTrace.Log("TokenLoginTests", "SUCCESS", nameof(TokenLogin_ValidToken_ReturnsSuccess));
            }
            catch (Exception ex)
            {
                await AuthTestTrace.Log("TokenLoginTests", "FAILURE", $"{nameof(TokenLogin_ValidToken_ReturnsSuccess)}: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
            finally
            {
                await AuthTestTrace.LogTestEnd(nameof(TokenLogin_ValidToken_ReturnsSuccess));
            }
        }

        [Test]
        public async Task TokenLogin_ExpiredToken_ReturnsTokenExpired()
        {
            try
            {
                await AuthTestTrace.LogTestStart(nameof(TokenLogin_ExpiredToken_ReturnsTokenExpired),
                    "Test: Token login with expired token.\n"
                    + "Procedure: Attempt login with an expired token.\n"
                    + "Expected: TokenExpired.\n"
                    + "Failure: Any other result indicates a bug in token expiry handling.");
                using AuthTestHarness h = new AuthTestHarness();
                string expiredToken = h.Store.IssueExpiredToken("bob");
                ClientAuthenticationResult result = await DriveTokenLogin(h, expiredToken);
                LogAssert.AreEqual(ClientAuthenticationResult.TokenExpired, result,
                    $"Expected TokenExpired for expired token, got {result}.");
                await AuthTestTrace.Log("TokenLoginTests", "SUCCESS", nameof(TokenLogin_ExpiredToken_ReturnsTokenExpired));
            }
            catch (Exception ex)
            {
                await AuthTestTrace.Log("TokenLoginTests", "FAILURE", $"{nameof(TokenLogin_ExpiredToken_ReturnsTokenExpired)}: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
            finally
            {
                await AuthTestTrace.LogTestEnd(nameof(TokenLogin_ExpiredToken_ReturnsTokenExpired));
            }
        }

        [Test]
        public async Task TokenLogin_RevokedToken_ReturnsTokenRevoked()
        {
            try
            {
                await AuthTestTrace.LogTestStart(nameof(TokenLogin_RevokedToken_ReturnsTokenRevoked),
                    "Test: Token login with revoked token.\n"
                    + "Procedure: Attempt login with a revoked token.\n"
                    + "Expected: TokenRevoked.\n"
                    + "Failure: Any other result indicates a bug in token revocation handling.");
                using AuthTestHarness h = new AuthTestHarness();
                string revokedToken = h.Store.IssueRevokedToken("carol");
                ClientAuthenticationResult result = await DriveTokenLogin(h, revokedToken);
                LogAssert.AreEqual(ClientAuthenticationResult.TokenRevoked, result,
                    $"Expected TokenRevoked for revoked token, got {result}.");
                await AuthTestTrace.Log("TokenLoginTests", "SUCCESS", nameof(TokenLogin_RevokedToken_ReturnsTokenRevoked));
            }
            catch (Exception ex)
            {
                await AuthTestTrace.Log("TokenLoginTests", "FAILURE", $"{nameof(TokenLogin_RevokedToken_ReturnsTokenRevoked)}: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
            finally
            {
                await AuthTestTrace.LogTestEnd(nameof(TokenLogin_RevokedToken_ReturnsTokenRevoked));
            }
        }

        [Test]
        public async Task TokenLogin_InvalidToken_ReturnsInvalidToken()
        {
            try
            {
                await AuthTestTrace.LogTestStart(nameof(TokenLogin_InvalidToken_ReturnsInvalidToken),
                    "Test: Token login with invalid token.\n"
                    + "Procedure: Attempt login with a malformed or random token.\n"
                    + "Expected: InvalidToken.\n"
                    + "Failure: Any other result indicates a bug in token validation.");
                using AuthTestHarness h = new AuthTestHarness();
                string invalidToken = "not-a-real-token";
                ClientAuthenticationResult result = await DriveTokenLogin(h, invalidToken);
                LogAssert.AreEqual(ClientAuthenticationResult.TokenInvalid, result,
                    $"Expected TokenInvalid for malformed token, got {result}.");
                await AuthTestTrace.Log("TokenLoginTests", "SUCCESS", nameof(TokenLogin_InvalidToken_ReturnsInvalidToken));
            }
            catch (Exception ex)
            {
                await AuthTestTrace.Log("TokenLoginTests", "FAILURE", $"{nameof(TokenLogin_InvalidToken_ReturnsInvalidToken)}: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
            finally
            {
                await AuthTestTrace.LogTestEnd(nameof(TokenLogin_InvalidToken_ReturnsInvalidToken));
            }
        }

        // Add more edge/failure cases as needed (e.g., server busy, DB unavailable, replay attack, etc.)

        /// <summary>
        /// When the token store simulates a database error the next validation call must
        /// return <see cref="ClientAuthenticationResult.ServerBusy"/> instead of succeeding.
        /// </summary>
        [Test]
        public async Task TokenLogin_ServerBusy_ReturnsServerBusy()
        {
            try
            {
                await AuthTestTrace.LogTestStart(nameof(TokenLogin_ServerBusy_ReturnsServerBusy),
                    "Test: Token login when the backing store returns a DB error.\n"
                    + "Procedure: Trigger SimulateDbError, then attempt token login with a valid token.\n"
                    + "Expected: ServerBusy.\n"
                    + "Failure: Any other result indicates the error path is not surfaced correctly.");
                using AuthTestHarness h = new AuthTestHarness();
                string token = h.Store.IssueValidToken("dave");
                h.Store.SimulateDbError();
                ClientAuthenticationResult result = await DriveTokenLogin(h, token);
                LogAssert.AreEqual(ClientAuthenticationResult.ServerBusy, result,
                    $"Expected ServerBusy when DB error is simulated, got {result}.");
                await AuthTestTrace.Log("TokenLoginTests", "SUCCESS", nameof(TokenLogin_ServerBusy_ReturnsServerBusy));
            }
            catch (Exception ex)
            {
                await AuthTestTrace.Log("TokenLoginTests", "FAILURE", $"{nameof(TokenLogin_ServerBusy_ReturnsServerBusy)}: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
            finally
            {
                await AuthTestTrace.LogTestEnd(nameof(TokenLogin_ServerBusy_ReturnsServerBusy));
            }
        }

        /// <summary>
        /// Passing an empty string to <see cref="TestClientCore.SetToken"/> must be rejected
        /// immediately (returns <c>false</c>) without starting a network flow.
        /// </summary>
        [Test]
        public async Task TokenLogin_EmptyToken_SetTokenReturnsFalse()
        {
            try
            {
                await AuthTestTrace.LogTestStart(nameof(TokenLogin_EmptyToken_SetTokenReturnsFalse),
                    "Test: SetToken with empty string is rejected before any network activity.\n"
                    + "Procedure: Call SetToken(\"\") on a fresh client.\n"
                    + "Expected: Returns false; no connection is initiated.\n"
                    + "Failure: If SetToken accepts an empty string, the guard is missing.");
                using AuthTestHarness h = new AuthTestHarness();
                bool accepted = h.Client.SetToken("");
                LogAssert.IsFalse(accepted, "SetToken must reject an empty token string.");
                await AuthTestTrace.Log("TokenLoginTests", "SUCCESS", nameof(TokenLogin_EmptyToken_SetTokenReturnsFalse));
            }
            catch (Exception ex)
            {
                await AuthTestTrace.Log("TokenLoginTests", "FAILURE", $"{nameof(TokenLogin_EmptyToken_SetTokenReturnsFalse)}: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
            finally
            {
                await AuthTestTrace.LogTestEnd(nameof(TokenLogin_EmptyToken_SetTokenReturnsFalse));
            }
        }

        /// <summary>
        /// A token created by <see cref="InMemoryAccountStore.RenewToken"/> must itself
        /// be accepted as a valid login credential.
        /// </summary>
        [Test]
        public async Task TokenLogin_RenewedToken_IsValid()
        {
            try
            {
                await AuthTestTrace.LogTestStart(nameof(TokenLogin_RenewedToken_IsValid),
                    "Test: A renewed token is accepted by the server.\n"
                    + "Procedure: Issue a token, renew it to obtain a second token, then login with the renewed token.\n"
                    + "Expected: LoginSuccess.\n"
                    + "Failure: The renewed token is not stored correctly or is marked invalid.");
                using AuthTestHarness h = new AuthTestHarness();
                string original = h.Store.IssueValidToken("eve");
                string renewed = h.Store.RenewToken(original);
                LogAssert.IsNotNull(renewed, "RenewToken must return a non-null token ID.");
                ClientAuthenticationResult result = await DriveTokenLogin(h, renewed);
                LogAssert.AreEqual(ClientAuthenticationResult.LoginSuccess, result,
                    $"Renewed token must be accepted as valid, got {result}.");
                await AuthTestTrace.Log("TokenLoginTests", "SUCCESS", nameof(TokenLogin_RenewedToken_IsValid));
            }
            catch (Exception ex)
            {
                await AuthTestTrace.Log("TokenLoginTests", "FAILURE", $"{nameof(TokenLogin_RenewedToken_IsValid)}: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
            finally
            {
                await AuthTestTrace.LogTestEnd(nameof(TokenLogin_RenewedToken_IsValid));
            }
        }

        /// <summary>
        /// Revoking one token must not affect the validity of independent tokens issued
        /// for the same account — only the explicitly revoked identifier is invalidated.
        /// </summary>
        [Test]
        public async Task TokenLogin_RevokingOneToken_DoesNotAffectOtherValidTokens()
        {
            try
            {
                await AuthTestTrace.LogTestStart(nameof(TokenLogin_RevokingOneToken_DoesNotAffectOtherValidTokens),
                    "Test: Selective token revocation leaves sibling tokens intact.\n"
                    + "Procedure: Issue two tokens for the same account, revoke the first, then login with the second.\n"
                    + "Expected: LoginSuccess for the non-revoked token.\n"
                    + "Failure: Revocation of one token erroneously invalidates another.");
                using AuthTestHarness h = new AuthTestHarness();
                string tokenA = h.Store.IssueValidToken("frank");
                string tokenB = h.Store.IssueValidToken("frank");
                h.Store.RevokeToken(tokenA);
                ClientAuthenticationResult result = await DriveTokenLogin(h, tokenB);
                LogAssert.AreEqual(ClientAuthenticationResult.LoginSuccess, result,
                    $"Non-revoked token must remain valid after a sibling token is revoked, got {result}.");
                await AuthTestTrace.Log("TokenLoginTests", "SUCCESS", nameof(TokenLogin_RevokingOneToken_DoesNotAffectOtherValidTokens));
            }
            catch (Exception ex)
            {
                await AuthTestTrace.Log("TokenLoginTests", "FAILURE", $"{nameof(TokenLogin_RevokingOneToken_DoesNotAffectOtherValidTokens)}: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
            finally
            {
                await AuthTestTrace.LogTestEnd(nameof(TokenLogin_RevokingOneToken_DoesNotAffectOtherValidTokens));
            }
        }
    }
}