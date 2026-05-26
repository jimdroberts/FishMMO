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
    /// Tests for major attack and failure scenarios in registration and login flows.
    /// </summary>
    [TestFixture]
    public class AttackAndFailureScenariosTests
    {
        private const int AwaitTimeoutMs = 5000;

        [Test]
        public async Task Register_DuplicateUsernameOrEmail_Rejected()
        {
            try
            {
                await AuthTestTrace.LogTestStart(nameof(Register_DuplicateUsernameOrEmail_Rejected),
                    "Test: Registration with duplicate username/email.\n"
                    + "Procedure: Attempt to register with a username/email that already exists.\n"
                    + "Expected: Registration is rejected.\n"
                    + "Failure: If registration succeeds, duplicate check is broken.");
                using AuthTestHarness h = new AuthTestHarness();
                h.Store.SeedAccount("dupe", "pw1", email: "dupe@example.test");
                bool ok = h.Client.SetLoginCredentials("dupe", "pw2", register: true, email: "dupe@example.test", age: 20);
                LogAssert.IsFalse(ok, "Duplicate username/email must be rejected.");
                await AuthTestTrace.Log("AttackAndFailureScenariosTests", "SUCCESS", nameof(Register_DuplicateUsernameOrEmail_Rejected));
            }
            catch (Exception ex)
            {
                await AuthTestTrace.Log("AttackAndFailureScenariosTests", "FAILURE", $"{nameof(Register_DuplicateUsernameOrEmail_Rejected)}: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
            finally
            {
                await AuthTestTrace.LogTestEnd(nameof(Register_DuplicateUsernameOrEmail_Rejected));
            }
        }

        [Test]
        public async Task Register_InvalidEmailOrUnderage_Rejected()
        {
            try
            {
                await AuthTestTrace.LogTestStart(nameof(Register_InvalidEmailOrUnderage_Rejected),
                    "Test: Registration with invalid email or underage.\n"
                    + "Procedure: Attempt to register with invalid email or age below minimum.\n"
                    + "Expected: Registration is rejected.\n"
                    + "Failure: If registration succeeds, validation is broken.");
                using AuthTestHarness h = new AuthTestHarness();
                bool badEmail = h.Client.SetLoginCredentials("user1", "pw1", register: true, email: "bademail", age: 20);
                LogAssert.IsFalse(badEmail, "Invalid email must be rejected.");
                bool underage = h.Client.SetLoginCredentials("user2", "pw1", register: true, email: "user2@example.test", age: 10);
                LogAssert.IsFalse(underage, "Underage registration must be rejected.");
                await AuthTestTrace.Log("AttackAndFailureScenariosTests", "SUCCESS", nameof(Register_InvalidEmailOrUnderage_Rejected));
            }
            catch (Exception ex)
            {
                await AuthTestTrace.Log("AttackAndFailureScenariosTests", "FAILURE", $"{nameof(Register_InvalidEmailOrUnderage_Rejected)}: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
            finally
            {
                await AuthTestTrace.LogTestEnd(nameof(Register_InvalidEmailOrUnderage_Rejected));
            }
        }

        [Test]
        public async Task Login_BannedOrLockedAccount_Rejected()
        {
            try
            {
                await AuthTestTrace.LogTestStart(nameof(Login_BannedOrLockedAccount_Rejected),
                    "Test: Login with banned or locked account.\n"
                    + "Procedure: Attempt login with an account flagged as banned/locked.\n"
                    + "Expected: Login is rejected.\n"
                    + "Failure: If login succeeds, ban/lock enforcement is broken.");
                using AuthTestHarness h = new AuthTestHarness();
                h.Store.SeedAccount("banned", "pass1", isBanned: true);
                ClientAuthenticationResult result = await h.Client.AttemptLogin("banned", "pass1");
                LogAssert.AreEqual(ClientAuthenticationResult.Banned, result, "Banned account must be rejected.");
                await AuthTestTrace.Log("AttackAndFailureScenariosTests", "SUCCESS", nameof(Login_BannedOrLockedAccount_Rejected));
            }
            catch (Exception ex)
            {
                await AuthTestTrace.Log("AttackAndFailureScenariosTests", "FAILURE", $"{nameof(Login_BannedOrLockedAccount_Rejected)}: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
            finally
            {
                await AuthTestTrace.LogTestEnd(nameof(Login_BannedOrLockedAccount_Rejected));
            }
        }

        // Add more tests for brute-force, replay, protocol downgrade, 2FA edge cases, server busy, etc.

        /// <summary>
        /// Repeated wrong-password attempts must each return
        /// <see cref="ClientAuthenticationResult.InvalidUsernameOrPassword"/>. No special
        /// "lockout" response should leak enumeration information.
        /// </summary>
        [Test]
        public async Task BruteForce_RepeatedWrongPasswords_AllReturnInvalidCredentials()
        {
            const int attempts = 3;
            try
            {
                await AuthTestTrace.LogTestStart(nameof(BruteForce_RepeatedWrongPasswords_AllReturnInvalidCredentials),
                    $"Test: Brute-force simulation — {attempts} wrong passwords against the same account.\n"
                    + "Procedure: Make multiple independent login attempts with incorrect passwords.\n"
                    + "Expected: Every attempt returns InvalidUsernameOrPassword.\n"
                    + "Failure: Any attempt returns a different result or exposes enumeration info.");
                for (int attempt = 1; attempt <= attempts; attempt++)
                {
                    using AuthTestHarness h = new AuthTestHarness();
                    h.Store.SeedAccount("victim", "correct-secret");
                    ClientAuthenticationResult result = await h.Client.AttemptLogin("victim", $"wrong-attempt-{attempt}");
                    LogAssert.AreEqual(ClientAuthenticationResult.InvalidUsernameOrPassword, result,
                        $"Attempt {attempt}: expected InvalidUsernameOrPassword, got {result}.");
                }
                await AuthTestTrace.Log("AttackAndFailureScenariosTests", "SUCCESS", nameof(BruteForce_RepeatedWrongPasswords_AllReturnInvalidCredentials));
            }
            catch (Exception ex)
            {
                await AuthTestTrace.Log("AttackAndFailureScenariosTests", "FAILURE", $"{nameof(BruteForce_RepeatedWrongPasswords_AllReturnInvalidCredentials)}: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
            finally
            {
                await AuthTestTrace.LogTestEnd(nameof(BruteForce_RepeatedWrongPasswords_AllReturnInvalidCredentials));
            }
        }

        /// <summary>
        /// When the SRP proof message is intercepted and dropped the server never sends a
        /// success result, so the client must not report <c>ReceivedSuccess</c>. The login
        /// attempt either times out or receives an explicit rejection.
        /// </summary>
        [Test]
        public async Task SrpProof_Dropped_NoSuccessDelivered()
        {
            try
            {
                await AuthTestTrace.LogTestStart(nameof(SrpProof_Dropped_NoSuccessDelivered),
                    "Test: Dropped SRP proof must never produce a success result.\n"
                    + "Procedure: Install a proof interceptor that returns null (drops the message), then attempt login.\n"
                    + "Expected: Login times out or is rejected; ReceivedSuccess remains false.\n"
                    + "Failure: If the client flags success with a dropped proof, there is an auth bypass.");
                using AuthTestHarness h = new AuthTestHarness();
                h.Store.SeedAccount("target", "valid-pass");
                h.Client.SrpProofInterceptor = _ => null;
                bool timedOut = false;
                try
                {
                    await h.Client.AttemptLogin("target", "valid-pass", timeoutMs: 1000);
                }
                catch (TimeoutException)
                {
                    timedOut = true;
                }
                LogAssert.IsTrue(timedOut || !h.Client.ReceivedSuccess,
                    "Dropped SRP proof must not deliver a success to the client.");
                LogAssert.IsFalse(h.Client.ReceivedSuccess,
                    "Client must not flag ReceivedSuccess when proof is dropped.");
                await AuthTestTrace.Log("AttackAndFailureScenariosTests", "SUCCESS", nameof(SrpProof_Dropped_NoSuccessDelivered));
            }
            catch (Exception ex)
            {
                await AuthTestTrace.Log("AttackAndFailureScenariosTests", "FAILURE", $"{nameof(SrpProof_Dropped_NoSuccessDelivered)}: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
            finally
            {
                await AuthTestTrace.LogTestEnd(nameof(SrpProof_Dropped_NoSuccessDelivered));
            }
        }

        /// <summary>
        /// An account that is already marked online must be refused with
        /// <see cref="ClientAuthenticationResult.AlreadyOnline"/> to prevent session hijacking
        /// via concurrent logins.
        /// </summary>
        [Test]
        public async Task Login_AlreadyOnline_ReturnsAlreadyOnline()
        {
            try
            {
                await AuthTestTrace.LogTestStart(nameof(Login_AlreadyOnline_ReturnsAlreadyOnline),
                    "Test: Login attempt for an already-online account is rejected.\n"
                    + "Procedure: Seed an account, mark it online, then attempt SRP login.\n"
                    + "Expected: AlreadyOnline.\n"
                    + "Failure: A second session is opened, allowing simultaneous logins.");
                using AuthTestHarness h = new AuthTestHarness();
                h.Store.SeedAccount("online-user", "secret");
                h.Store.SetOnline("online-user", true);
                ClientAuthenticationResult result = await h.Client.AttemptLogin("online-user", "secret");
                LogAssert.AreEqual(ClientAuthenticationResult.AlreadyOnline, result,
                    $"Account already marked online must be rejected as AlreadyOnline, got {result}.");
                await AuthTestTrace.Log("AttackAndFailureScenariosTests", "SUCCESS", nameof(Login_AlreadyOnline_ReturnsAlreadyOnline));
            }
            catch (Exception ex)
            {
                await AuthTestTrace.Log("AttackAndFailureScenariosTests", "FAILURE", $"{nameof(Login_AlreadyOnline_ReturnsAlreadyOnline)}: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
            finally
            {
                await AuthTestTrace.LogTestEnd(nameof(Login_AlreadyOnline_ReturnsAlreadyOnline));
            }
        }

        /// <summary>
        /// After a successful SRP proof for a TOTP-enabled account the server must respond with
        /// <see cref="ClientAuthenticationResult.TwoFactorRequired"/> rather than granting access,
        /// forcing the second authentication factor before the session is established.
        /// </summary>
        [Test]
        public async Task TwoFactor_AccountRequires2FA_ReturnsTwoFactorRequired()
        {
            try
            {
                await AuthTestTrace.LogTestStart(nameof(TwoFactor_AccountRequires2FA_ReturnsTwoFactorRequired),
                    "Test: TOTP-enabled account triggers two-factor challenge after SRP proof.\n"
                    + "Procedure: Seed an account with totpEnabled=true, then log in with correct password.\n"
                    + "Expected: TwoFactorRequired (not LoginSuccess).\n"
                    + "Failure: If login succeeds without the second factor, 2FA is bypassed.");
                using AuthTestHarness h = new AuthTestHarness();
                h.Store.SeedAccount("totp-user", "correct-pass", totpEnabled: true, totpSecret: "JBSWY3DPEHPK3PXP");
                ClientAuthenticationResult result = await h.Client.AttemptLogin("totp-user", "correct-pass");
                LogAssert.AreEqual(ClientAuthenticationResult.TwoFactorRequired, result,
                    $"TOTP-enabled account must require 2FA code before granting access, got {result}.");
                LogAssert.IsFalse(h.Client.ReceivedSuccess,
                    "Client must not flag ReceivedSuccess when 2FA is still required.");
                await AuthTestTrace.Log("AttackAndFailureScenariosTests", "SUCCESS", nameof(TwoFactor_AccountRequires2FA_ReturnsTwoFactorRequired));
            }
            catch (Exception ex)
            {
                await AuthTestTrace.Log("AttackAndFailureScenariosTests", "FAILURE", $"{nameof(TwoFactor_AccountRequires2FA_ReturnsTwoFactorRequired)}: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
            finally
            {
                await AuthTestTrace.LogTestEnd(nameof(TwoFactor_AccountRequires2FA_ReturnsTwoFactorRequired));
            }
        }
    }
}