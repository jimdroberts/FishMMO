using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FishMMO.Auth.Core;
using FishMMO.Auth.Implementation;
using FishMMO.UnitTests.Harness;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Security-focused EditMode tests for the SRP auth stack. These tests exercise
	/// adversarial behaviour against the same harness used by <see cref="LoginTests"/>:
	/// tampered SRP proofs, replayed messages from prior sessions, plaintext leakage
	/// in wire payloads, protocol-version downgrade, anti-enumeration parity, and

	/// post-dispose key-material zeroization.
	/// </summary>
	[TestFixture]
	public class SecurityTests
	{
		private const int AwaitTimeoutMs = 5000;

		/// <summary>
		/// Drives the full login/auth flow and returns the authentication result, logging all steps.
		/// </summary>
		private static async Task<ClientAuthenticationResult> Drive(AuthTestHarness h, string user, string pw)
		{
			LogAssert.IsTrue(h.Client.SetLoginCredentials(user, pw, register: false),
				$"SetLoginCredentials returned false for user '{user}'.");
			h.Client.OnConnected();
			Task<ClientAuthenticationResult> resultTask = h.Client.AuthResultTcs.Task;
			Task completed = await Task.WhenAny(resultTask, Task.Delay(AwaitTimeoutMs));
			LogAssert.IsTrue(object.ReferenceEquals(resultTask, completed),
				$"Login did not complete within timeout for user '{user}'.");
			return await resultTask;
		}

		/// <summary>
		/// Drives the flow and resolves when either an auth result is delivered OR the server
		/// disconnects (some rejection paths drop the connection without broadcasting a result).
		/// </summary>
		private static async Task DriveUntilTerminated(AuthTestHarness h, string user, string pw)
		{
			LogAssert.IsTrue(h.Client.SetLoginCredentials(user, pw, register: false), "SetLoginCredentials returned false.");
			h.Client.OnConnected();
			Task<ClientAuthenticationResult> rt = h.Client.AuthResultTcs.Task;
			int waited = 0;
			while (waited < AwaitTimeoutMs)
			{
				if (rt.IsCompleted) return;
				if (h.Server.WasDisconnected) return;
				await Task.Delay(20);
				waited += 20;
			}
			LogAssert.Fail("Auth flow did not terminate (no result and no disconnect) within timeout.");
		}

		/// <summary>
		/// Anti-enumeration: an unknown account and a known account with the wrong password
		/// must both produce <c>InvalidUsernameOrPassword</c>, and the server must traverse
		/// the same number of round-trips for both — otherwise an attacker could distinguish
		/// "user does not exist" from "user exists, password wrong".
		/// </summary>
		[Test]
		public async Task Security_AntiEnumeration_UnknownAndWrongPassword_AreIndistinguishable()
		{
			try
			{
				await AuthTestTrace.LogTestStart(
					nameof(Security_AntiEnumeration_UnknownAndWrongPassword_AreIndistinguishable),
					"Test: Anti-enumeration parity for unknown user and wrong password.\n"
					+ "Procedure: Attempt login with a non-existent username, then with a valid username but wrong password.\n"
					+ "Expected: Both attempts must return InvalidUsernameOrPassword, and both must traverse the same number of SRP protocol rounds.\n"
					+ "Failure: If the server returns different results or a different number of protocol steps, an attacker could distinguish valid from invalid usernames (user enumeration risk).\n"
					+ "This test ensures the authentication protocol does not leak account existence through timing or result differences."
				);
				using AuthTestHarness hUnknown = new AuthTestHarness();
				ClientAuthenticationResult rUnknown = await Drive(hUnknown, "ghost", "any-password");

				using AuthTestHarness hWrong = new AuthTestHarness();
				hWrong.Store.SeedAccount("bob", "real-password");
				ClientAuthenticationResult rWrong = await Drive(hWrong, "bob", "wrong-password");

				LogAssert.AreEqual(ClientAuthenticationResult.InvalidUsernameOrPassword, rUnknown,
				 "Unknown user must produce the generic invalid-credentials result.");
				LogAssert.AreEqual(ClientAuthenticationResult.InvalidUsernameOrPassword, rWrong,
				 "Wrong password must produce the generic invalid-credentials result.");

				// Both flows must reach the SRP-proof stage; if the unknown-user case short-circuited
				// after SrpVerify (no SendSrpProof), an attacker timing the requests could enumerate.
				LogAssert.AreEqual(hWrong.Client.SrpProofSends.Count, hUnknown.Client.SrpProofSends.Count,
				 "Unknown-user flow must traverse the same number of SRP rounds as wrong-password.");
				LogAssert.IsTrue(hUnknown.Client.SrpProofSends.Count > 0,
				 "Server must run the full SRP exchange for unknown users (else enumeration is possible).");
				await AuthTestTrace.Log("SecurityTests", "SUCCESS", nameof(Security_AntiEnumeration_UnknownAndWrongPassword_AreIndistinguishable));
			}
			catch (Exception ex)
			{
				await AuthTestTrace.Log("SecurityTests", "FAILURE", $"{nameof(Security_AntiEnumeration_UnknownAndWrongPassword_AreIndistinguishable)}: {ex.Message}\n{ex.StackTrace}");
				throw;
			}
			finally
			{
				await AuthTestTrace.LogTestEnd(nameof(Security_AntiEnumeration_UnknownAndWrongPassword_AreIndistinguishable));
			}
		}

		/// <summary>
		/// Zero-knowledge: the cleartext password must never appear in any byte payload sent
		/// across the wire. This catches accidental logging / fallback-to-plaintext regressions.
		/// </summary>
		[Test]
		public async Task Security_Password_NeverAppearsInAnyWirePayload()
		{
			try
			{
				await AuthTestTrace.LogTestStart(
					nameof(Security_Password_NeverAppearsInAnyWirePayload),
					"Test: Zero-knowledge password secrecy.\n"
					+ "Procedure: Attempt login and capture all wire payloads.\n"
					+ "Expected: The cleartext password must not appear in any byte payload sent across the wire.\n"
					+ "Failure: If the password appears in any payload, it indicates a critical zero-knowledge regression or accidental plaintext leak.\n"
					+ "This test ensures password secrecy is preserved end-to-end."
				);
				const string password = "MySecretPassword_91!XYZ";
				using AuthTestHarness h = new AuthTestHarness();
				h.Store.SeedAccount("alice", password);
				ClientAuthenticationResult r = await Drive(h, "alice", password);
				LogAssert.AreEqual(ClientAuthenticationResult.LoginSuccess, r);

				byte[] needle = Encoding.UTF8.GetBytes(password);
				foreach (TestClientCore.SrpVerifyCapture v in h.Client.SrpVerifySends)
				{
					AssertNoSubsequence(v.EncryptedUsername, needle, "SrpVerify.encryptedUsername");
					AssertNoSubsequence(v.EncryptedClientEphemeral, needle, "SrpVerify.encryptedClientEphemeral");
				}
				if (h.Client.SrpProofSends.Count > 0)
				{
					foreach (TestClientCore.SrpProofCapture p in h.Client.SrpProofSends)
						AssertNoSubsequence(p.EncryptedProof, needle, "SrpProof.encryptedProof");
				}
				await AuthTestTrace.Log("SecurityTests", "SUCCESS", nameof(Security_Password_NeverAppearsInAnyWirePayload));
			}
			catch (Exception ex)
			{
				await AuthTestTrace.Log("SecurityTests", "FAILURE", $"{nameof(Security_Password_NeverAppearsInAnyWirePayload)}: {ex.Message}\n{ex.StackTrace}");
				throw;
			}
			finally
			{
				await AuthTestTrace.LogTestEnd(nameof(Security_Password_NeverAppearsInAnyWirePayload));
			}
		}

		/// <summary>
		/// Zero-knowledge: the cleartext username must never appear inside any encrypted
		/// payload the client emits (it is encrypted under the directional handshake key).
		/// </summary>
		[Test]
		public async Task Security_Username_NeverAppearsInAnyEncryptedPayload()
		{
			try
			{
				await AuthTestTrace.LogTestStart(
					nameof(Security_Username_NeverAppearsInAnyEncryptedPayload),
					"Test: Zero-knowledge username secrecy.\n"
					+ "Procedure: Attempt login and inspect all encrypted payloads emitted by the client.\n"
					+ "Expected: The cleartext username must never appear inside any encrypted payload.\n"
					+ "Failure: If the username appears, it indicates a failure to encrypt or sanitize sensitive fields.\n"
					+ "This test ensures username secrecy is preserved in all client emissions."
				);
				const string username = "alice_zk_probe";
				using AuthTestHarness h = new AuthTestHarness();
				h.Store.SeedAccount(username, "correct-password");
				ClientAuthenticationResult r = await Drive(h, username, "correct-password");
				LogAssert.AreEqual(ClientAuthenticationResult.LoginSuccess, r);

				byte[] needle = Encoding.UTF8.GetBytes(username);
				if (h.Client.SrpVerifySends.Count > 0)
				{
					foreach (TestClientCore.SrpVerifyCapture v in h.Client.SrpVerifySends)
						AssertNoSubsequence(v.EncryptedUsername, needle, "SrpVerify.encryptedUsername");
				}
				await AuthTestTrace.Log("SecurityTests", "SUCCESS", nameof(Security_Username_NeverAppearsInAnyEncryptedPayload));
			}
			catch (Exception ex)
			{
				await AuthTestTrace.Log("SecurityTests", "FAILURE", $"{nameof(Security_Username_NeverAppearsInAnyEncryptedPayload)}: {ex.Message}\n{ex.StackTrace}");
				throw;
			}
			finally
			{
				await AuthTestTrace.LogTestEnd(nameof(Security_Username_NeverAppearsInAnyEncryptedPayload));
			}
		}

		/// <summary>
		/// Non-determinism: two sessions with identical credentials must produce different
		/// encrypted SrpVerify payloads (because each session has a fresh ephemeral keypair
		/// and the AES nonce/IV must be unique). Equality would indicate IV reuse.
		/// </summary>
		[Test]
		public async Task Security_SameCredentialsTwoSessions_ProduceDifferentSrpVerifyCiphertexts()
		{
			try
			{
				await AuthTestTrace.LogTestStart(
					nameof(Security_SameCredentialsTwoSessions_ProduceDifferentSrpVerifyCiphertexts),
					"Test: Non-determinism of SRP session material.\n"
					+ "Procedure: Perform two logins with identical credentials and compare encrypted payloads.\n"
					+ "Expected: Each session must produce different encrypted SrpVerify payloads (fresh ephemeral keypair and unique AES nonce/IV).\n"
					+ "Failure: If payloads are identical, it indicates IV/nonce reuse or static key usage, which is a cryptographic vulnerability.\n"
					+ "This test ensures session uniqueness and cryptographic safety."
				);
				using AuthTestHarness h1 = new AuthTestHarness();
				h1.Store.SeedAccount("carol", "identical-password");
				LogAssert.AreEqual(ClientAuthenticationResult.LoginSuccess, await Drive(h1, "carol", "identical-password"));

				using AuthTestHarness h2 = new AuthTestHarness();
				h2.Store.SeedAccount("carol", "identical-password");
				LogAssert.AreEqual(ClientAuthenticationResult.LoginSuccess, await Drive(h2, "carol", "identical-password"));

				LogAssert.AreEqual(1, h1.Client.SrpVerifySends.Count);
				LogAssert.AreEqual(1, h2.Client.SrpVerifySends.Count);
				LogAssert.IsFalse(h1.Client.SrpVerifySends[0].EncryptedUsername.SequenceEqual(h2.Client.SrpVerifySends[0].EncryptedUsername),
				 "Encrypted username ciphertext repeats across sessions — IV/nonce reuse suspected.");
				LogAssert.IsFalse(h1.Client.SrpVerifySends[0].EncryptedClientEphemeral.SequenceEqual(h2.Client.SrpVerifySends[0].EncryptedClientEphemeral),
				 "Encrypted client ephemeral ciphertext repeats across sessions — IV/nonce reuse suspected.");
				LogAssert.IsFalse(h1.Client.SrpProofSends[0].EncryptedProof.SequenceEqual(h2.Client.SrpProofSends[0].EncryptedProof),
				 "Encrypted SRP proof ciphertext repeats across sessions — IV/nonce reuse suspected.");
				await AuthTestTrace.Log("SecurityTests", "SUCCESS", nameof(Security_SameCredentialsTwoSessions_ProduceDifferentSrpVerifyCiphertexts));
			}
			catch (Exception ex)
			{
				await AuthTestTrace.Log("SecurityTests", "FAILURE", $"{nameof(Security_SameCredentialsTwoSessions_ProduceDifferentSrpVerifyCiphertexts)}: {ex.Message}\n{ex.StackTrace}");
				throw;
			}
			finally
			{
				await AuthTestTrace.LogTestEnd(nameof(Security_SameCredentialsTwoSessions_ProduceDifferentSrpVerifyCiphertexts));
			}
		}

		/// <summary>
		/// Tampered SRP proof: flipping a bit in the encrypted M1 must cause the server to
		/// reject the login and disconnect — never accept it as success.
		/// </summary>
		[Test]
		public async Task Security_TamperedSrpProof_IsRejectedAndDisconnects()
		{
			try
			{
				await AuthTestTrace.LogTestStart(
					nameof(Security_TamperedSrpProof_IsRejectedAndDisconnects),
					"Test: Tampered SRP proof rejection.\n"
					+ "Procedure: Flip a bit in the encrypted SRP proof and attempt login.\n"
					+ "Expected: The server must reject the login and disconnect the client.\n"
					+ "Failure: If the tampered proof is accepted, it indicates a critical authentication bypass.\n"
					+ "This test ensures tampering is always detected and rejected."
				);
				using AuthTestHarness h = new AuthTestHarness();
				h.Store.SeedAccount("dan", "valid-password");
				h.Client.SrpProofInterceptor = (original) =>
				{
					byte[] tampered = (byte[])original.Clone();
					// Flip a bit deep in the ciphertext (not in any potential header).
					int idx = tampered.Length / 2;
					tampered[idx] ^= 0x40;
					return tampered;
				};

				ClientAuthenticationResult result = await Drive(h, "dan", "valid-password");
				LogAssert.AreNotEqual(ClientAuthenticationResult.LoginSuccess, result,
				 "Tampered SRP proof must NEVER be accepted as login success.");
				LogAssert.IsFalse(h.Client.ReceivedSuccess, "Client must not flag success on tampered proof.");
				LogAssert.IsTrue(h.Server.WasDisconnected, "Server must disconnect on tampered proof.");
				await AuthTestTrace.Log("SecurityTests", "SUCCESS", nameof(Security_TamperedSrpProof_IsRejectedAndDisconnects));
			}
			catch (Exception ex)
			{
				await AuthTestTrace.Log("SecurityTests", "FAILURE", $"{nameof(Security_TamperedSrpProof_IsRejectedAndDisconnects)}: {ex.Message}\n{ex.StackTrace}");
				throw;
			}
			finally
			{
				await AuthTestTrace.LogTestEnd(nameof(Security_TamperedSrpProof_IsRejectedAndDisconnects));
			}
		}

		/// <summary>
		/// Replay protection: an M1 captured from a completed session MUST NOT authenticate
		/// a fresh session, because each session uses fresh ephemerals (so the captured M1
		/// was bound to the prior session's shared secret).
		/// </summary>
		[Test]
		public async Task Security_ReplayedSrpProofAcrossSessions_IsRejected()
		{
			try
			{
				await AuthTestTrace.LogTestStart(
					nameof(Security_ReplayedSrpProofAcrossSessions_IsRejected),
					"Test: Replay protection for SRP proofs.\n"
					+ "Procedure: Capture an SRP proof from a successful session and replay it in a new session.\n"
					+ "Expected: The replayed proof must not authenticate the new session.\n"
					+ "Failure: If replay succeeds, it indicates a session binding or nonce bug.\n"
					+ "This test ensures each session is cryptographically unique and replay attacks are impossible."
				);
				// Session 1: complete a normal login and capture the M1 ciphertext.
				using AuthTestHarness h1 = new AuthTestHarness();
				h1.Store.SeedAccount("eve", "replay-target-pw");
				LogAssert.AreEqual(ClientAuthenticationResult.LoginSuccess, await Drive(h1, "eve", "replay-target-pw"));
				LogAssert.AreEqual(1, h1.Client.SrpProofSends.Count);
				byte[] capturedM1 = h1.Client.SrpProofSends[0].EncryptedProof;

				// Session 2: replay the captured M1 in place of the legitimate one.
				using AuthTestHarness h2 = new AuthTestHarness();
				h2.Store.SeedAccount("eve", "replay-target-pw");
				h2.Client.SrpProofInterceptor = _ => capturedM1;

				ClientAuthenticationResult result = await Drive(h2, "eve", "replay-target-pw");
				LogAssert.AreNotEqual(ClientAuthenticationResult.LoginSuccess, result,
				 "Replayed SRP proof must NEVER authenticate a fresh session.");
				LogAssert.IsFalse(h2.Client.ReceivedSuccess, "Client must not flag success on replayed proof.");
				await AuthTestTrace.Log("SecurityTests", "SUCCESS", nameof(Security_ReplayedSrpProofAcrossSessions_IsRejected));
			}
			catch (Exception ex)
			{
				await AuthTestTrace.Log("SecurityTests", "FAILURE", $"{nameof(Security_ReplayedSrpProofAcrossSessions_IsRejected)}: {ex.Message}\n{ex.StackTrace}");
				throw;
			}
			finally
			{
				await AuthTestTrace.LogTestEnd(nameof(Security_ReplayedSrpProofAcrossSessions_IsRejected));
			}
		}

		/// <summary>
		/// Protocol-version negotiation: a peer announcing only an unsupported version range
		/// must cause the server to refuse the handshake (no <c>BroadcastServerHandshake</c>
		/// is emitted; the connection is terminated).
		/// </summary>
		[Test]
		public void Security_UnsupportedProtocolVersion_HandshakeIsRefused()
		{
			try
			{
#pragma warning disable CS4014
				AuthTestTrace.Log("SecurityTests", "START", nameof(Security_UnsupportedProtocolVersion_HandshakeIsRefused));
#pragma warning restore CS4014
				using AuthTestHarness h = new AuthTestHarness();

				// Bypass the client entirely and directly inject a phase-1 handshake announcing
				// a future-only version the server doesn't support. The server should disconnect
				// without producing a cookie challenge or proceeding to phase-2.
				byte[] fakeClientPublicKey = new byte[32];
				new Random(0xC0DE).NextBytes(fakeClientPublicKey);

				bool threw = false;
				try
				{
					h.Server.OnHandshakeReceived(conn: 1, fakeClientPublicKey, cookie: null!,
						null, minVersion: 0xFFFE, maxVersion: 0xFFFF);
				}
				catch (Exception)
				{
					// Throwing is one valid refusal path (negotiation failure).
					threw = true;
				}

				LogAssert.IsTrue(h.Server.WasDisconnected || threw,
				 "Server must refuse (disconnect or reject) any connection that announces no overlapping protocol version.");
				LogAssert.AreEqual(0, h.Client.SrpVerifySends.Count,
				 "No SRP traffic must be initiated for a refused-version connection.");
				AuthTestTrace.Log("SecurityTests", "SUCCESS", nameof(Security_UnsupportedProtocolVersion_HandshakeIsRefused)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("SecurityTests", "FAILURE", $"{nameof(Security_UnsupportedProtocolVersion_HandshakeIsRefused)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.Log("SecurityTests", "END", nameof(Security_UnsupportedProtocolVersion_HandshakeIsRefused)).GetAwaiter().GetResult();
			}
		}

		/// <summary>
		/// Memory hygiene: after the client is disposed, the long-lived per-session secrets
		/// (directional GCM nonce contexts and the symmetric session keys) must be cleared so
		/// a post-mortem memory dump cannot recover the session's encryption state.
		/// </summary>
		[Test]
		public async Task Security_DisposedClient_ClearsSessionSecrets()
		{
			try
			{
				await AuthTestTrace.LogTestStart(
					nameof(Security_DisposedClient_ClearsSessionSecrets),
					"Test: Memory hygiene after client disposal.\n"
					+ "Procedure: Log in, then dispose the client and inspect all per-session secrets.\n"
					+ "Expected: All session secrets (GCM nonce contexts, symmetric keys, ephemeral keypair) must be cleared.\n"
					+ "Failure: If any secret remains, it indicates a memory hygiene regression and risk of post-mortem key recovery.\n"
					+ "This test ensures secrets are zeroized after disposal."
				);
#pragma warning disable CS4014
				AuthTestTrace.Log("SecurityTests", "START", nameof(Security_DisposedClient_ClearsSessionSecrets));
#pragma warning restore CS4014
				using AuthTestHarness h = new AuthTestHarness();
				h.Store.SeedAccount("frank", "post-dispose-pw");
				LogAssert.AreEqual(ClientAuthenticationResult.LoginSuccess, await Drive(h, "frank", "post-dispose-pw"));

				// Sanity: at least one directional context should currently be populated.
				var sendNonceCtx = GetField(h.Client, "sendNonceCtx");
				var receiveNonceCtx = GetField(h.Client, "receiveNonceCtx");
				LogAssert.IsNotNull(sendNonceCtx, "Precondition: sendNonceCtx populated after login.");
				LogAssert.IsNotNull(receiveNonceCtx, "Precondition: receiveNonceCtx populated after login.");

				h.Client.Dispose();

				var sendNonceCtxAfter = GetField(h.Client, "sendNonceCtx");
				var receiveNonceCtxAfter = GetField(h.Client, "receiveNonceCtx");
				var ephemeralKeyPairAfter = GetField(h.Client, "ephemeralKeyPair");
				LogAssert.IsNull(sendNonceCtxAfter, "sendNonceCtx must be null after Dispose.");
				LogAssert.IsNull(receiveNonceCtxAfter, "receiveNonceCtx must be null after Dispose.");
				LogAssert.IsNull(ephemeralKeyPairAfter, "ephemeralKeyPair must be null after Dispose.");
				await AuthTestTrace.Log("SecurityTests", "SUCCESS", nameof(Security_DisposedClient_ClearsSessionSecrets));
			}
			catch (Exception ex)
			{
				await AuthTestTrace.Log("SecurityTests", "FAILURE", $"{nameof(Security_DisposedClient_ClearsSessionSecrets)}: {ex.Message}\n{ex.StackTrace}");
				throw;
			}
			finally
			{
#pragma warning disable CS4014
				AuthTestTrace.Log("SecurityTests", "END", nameof(Security_DisposedClient_ClearsSessionSecrets));
#pragma warning restore CS4014
			}
		}

		// ───────────────────────── handshake-layer attacks ─────────────────────────

		/// <summary>
		/// DLL provenance: every auth type the tests rely on must resolve to a precompiled
		/// assembly under <c>Assets/Dependencies/</c>. If a stray .cs copy of the auth source
		/// ever lands in <c>Assets/</c>, the C# compiler will silently prefer the in-project
		/// copy and our tests would no longer exercise the shipping DLL surface.
		/// </summary>
		[Test]
		public void Security_AuthTypes_ResolveToPrecompiledDependencyDlls()
		{
			try
			{
				AuthTestTrace.LogTestStart(
					nameof(Security_AuthTypes_ResolveToPrecompiledDependencyDlls),
					"Test: DLL provenance for auth types.\n"
					+ "Procedure: Check that all auth types resolve to precompiled DLLs under Assets/Dependencies.\n"
					+ "Expected: All types must resolve to a DLL, not an in-project .cs file.\n"
					+ "Failure: If a type resolves to a .cs file, the test suite is not exercising the shipping DLL.\n"
					+ "This test ensures test coverage of the correct binary surface."
				).GetAwaiter().GetResult();
				Type[] surface = new[]
				{
					typeof(ClientAuthenticatorCore),
					typeof(SrpAuthenticatorCore<int>),
					typeof(ClientAuthenticationResult),
					typeof(CryptoHelper),
				};
				foreach (Type t in surface)
				{
					string loc = t.Assembly.Location ?? string.Empty;
					LogAssert.IsFalse(string.IsNullOrEmpty(loc),
					 $"Assembly {t.Assembly.GetName().Name} has no location — likely in-memory compile, not the shipping DLL.");
					string normalised = loc.Replace('\\', '/');
					LogAssert.IsTrue(normalised.Contains("/Assets/Dependencies/") || normalised.Contains("/Library/ScriptAssemblies/PrecompiledAssemblies/"),
					 $"Type {t.FullName} resolved to {loc}; expected a DLL under Assets/Dependencies/.");
					StringAssert.Contains("FishMMO-", System.IO.Path.GetFileName(loc),
					 $"Type {t.FullName} resolved to a non-FishMMO assembly file ({loc}).");
				}
				AuthTestTrace.Log("SecurityTests", "SUCCESS", nameof(Security_AuthTypes_ResolveToPrecompiledDependencyDlls)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("SecurityTests", "FAILURE", $"{nameof(Security_AuthTypes_ResolveToPrecompiledDependencyDlls)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(Security_AuthTypes_ResolveToPrecompiledDependencyDlls)).GetAwaiter().GetResult();
			}
		}

		/// <summary>
		/// Malformed X25519 public keys (null, wrong length, or zero-filled) must be rejected
		/// at the handshake gate without progressing to ECDH or cookie issuance.
		/// </summary>
		[TestCase(0)]
		[TestCase(31)]
		[TestCase(33)]
		[TestCase(64)]
		public void Security_MalformedHandshakePublicKey_IsRejected(int badLen)
		{
			try
			{
				AuthTestTrace.LogTestStart(
					nameof(Security_MalformedHandshakePublicKey_IsRejected),
					$"Test: Malformed handshake public key rejection.\n"
					+ $"Procedure: Attempt handshake with public key of length {badLen}.\n"
					+ "Expected: The server must immediately disconnect and not progress to ECDH or cookie issuance.\n"
					+ "Failure: If the server processes the key, it indicates a protocol validation bug.\n"
					+ "This test ensures strict length validation for handshake keys."
				).GetAwaiter().GetResult();
				using AuthTestHarness h = new AuthTestHarness();
				byte[] bad = new byte[badLen];
				// Note: server treats null OR wrong-length as immediate disconnect.
				try { h.Server.OnHandshakeReceived(1, bad, cookie: null!, null, minVersion: 1, maxVersion: 1); }
				catch { /* tolerate throw */ }
				LogAssert.IsTrue(h.Server.WasDisconnected, $"Server must reject handshake with publicKey length {badLen}.");
				LogAssert.AreEqual(0, h.Server.CookieChallengeCount, "No cookie challenge must be issued for malformed pubkey.");
				LogAssert.AreEqual(0, h.Server.ServerHandshakeCount, "No server handshake must be sent for malformed pubkey.");
				AuthTestTrace.Log("SecurityTests", "SUCCESS", nameof(Security_MalformedHandshakePublicKey_IsRejected)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("SecurityTests", "FAILURE", $"{nameof(Security_MalformedHandshakePublicKey_IsRejected)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(Security_MalformedHandshakePublicKey_IsRejected)).GetAwaiter().GetResult();
			}
		}

		/// <summary>
		/// Forged cookie: a phase-2 handshake (non-null cookie) carrying random bytes that
		/// were never issued by the server's HMAC must be rejected — otherwise an off-path
		/// attacker could bypass the proof-of-work / IP-binding gate.
		/// </summary>
		[Test]
		public void Security_ForgedCookieOnPhase2Handshake_IsRejected()
		{
			try
			{
				AuthTestTrace.LogTestStart(
					nameof(Security_ForgedCookieOnPhase2Handshake_IsRejected),
					"Test: Forged cookie rejection in phase-2 handshake.\n"
					+ "Procedure: Attempt handshake with a random, never-issued cookie.\n"
					+ "Expected: The server must disconnect and not emit a server-handshake response.\n"
					+ "Failure: If the handshake proceeds, it indicates a cookie validation bypass.\n"
					+ "This test ensures cookie integrity and anti-forgery."
				).GetAwaiter().GetResult();

				using AuthTestHarness h = new AuthTestHarness();
				byte[] pk = new byte[32];
				new Random(0xBEEF).NextBytes(pk);
				byte[] forgedCookie = new byte[32];
				new Random(0xDEAD).NextBytes(forgedCookie);

				h.Server.OnHandshakeReceived(1, pk, forgedCookie, null, minVersion: 1, maxVersion: 1);

				LogAssert.IsTrue(h.Server.WasDisconnected, "Server must disconnect on forged cookie.");
				LogAssert.AreEqual(0, h.Server.ServerHandshakeCount, "No server-handshake response must be emitted for a forged cookie.");
				AuthTestTrace.Log("SecurityTests", "SUCCESS", nameof(Security_ForgedCookieOnPhase2Handshake_IsRejected)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("SecurityTests", "FAILURE", $"{nameof(Security_ForgedCookieOnPhase2Handshake_IsRejected)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(Security_ForgedCookieOnPhase2Handshake_IsRejected)).GetAwaiter().GetResult();
			}
		}

		/// <summary>
		/// Cookie/key binding: the cookie issued during phase-1 is bound to (ip, publicKey,
		/// time-bucket). Replaying that cookie with a DIFFERENT public key must be rejected.
		/// </summary>
		[Test]
		public void Security_HandshakeCookie_IsBoundToPublicKey()
		{
			try
			{
				AuthTestTrace.LogTestStart(
					nameof(Security_HandshakeCookie_IsBoundToPublicKey),
					"Test: Cookie/public key binding.\n"
					+ "Procedure: Obtain a cookie for one public key, then attempt phase-2 with a different key.\n"
					+ "Expected: The server must reject the handshake and not complete phase-2.\n"
					+ "Failure: If the handshake completes, it indicates a binding or replay bug.\n"
					+ "This test ensures cookies are bound to the correct public key."
				).GetAwaiter().GetResult();
				using AuthTestHarness h = new AuthTestHarness();
				byte[] pkA = new byte[32]; new Random(1).NextBytes(pkA);
				byte[] pkB = new byte[32]; new Random(2).NextBytes(pkB);

				// Phase-1 with pkA → server issues a cookie.
				h.Server.OnHandshakeReceived(1, pkA, cookie: null!, null, minVersion: 1, maxVersion: 1);
				LogAssert.AreEqual(1, h.Server.CookieChallengeCount, "Server must issue exactly one cookie for the phase-1 request.");
				byte[] cookie = h.Server.LastChallengeCookie;
				LogAssert.IsNotNull(cookie);

				// Phase-2 replaying the cookie but with a DIFFERENT public key must fail.
				h.Server.OnHandshakeReceived(2, pkB, cookie!, null, minVersion: 1, maxVersion: 1);
				LogAssert.IsTrue(h.Server.WasDisconnected, "Cookie reused with a different public key must be rejected.");
				LogAssert.AreEqual(0, h.Server.ServerHandshakeCount, "No phase-2 handshake must complete with mismatched (cookie, pubkey).");
				AuthTestTrace.Log("SecurityTests", "SUCCESS", nameof(Security_HandshakeCookie_IsBoundToPublicKey)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("SecurityTests", "FAILURE", $"{nameof(Security_HandshakeCookie_IsBoundToPublicKey)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(Security_HandshakeCookie_IsBoundToPublicKey)).GetAwaiter().GetResult();
			}
		}

		// ───────────────────────── SRP-state-machine attacks ─────────────────────────

		/// <summary>
		/// Out-of-order: a client that emits SrpProof before the server has even processed
		/// SrpVerify must be ignored (state machine cannot advance). This prevents an
		/// attacker from confusing the server into accepting a proof against unknown state.
		/// </summary>
		[Test]
		public void Security_SrpProofBeforeVerify_IsIgnored()
		{
			try
			{
				AuthTestTrace.LogTestStart(
					nameof(Security_SrpProofBeforeVerify_IsIgnored),
					"Test: Out-of-order SrpProof is ignored.\n"
					+ "Procedure: Emit SrpProof before SrpVerify is processed.\n"
					+ "Expected: No authentication result is produced.\n"
					+ "Failure: If a result is produced, the state machine is vulnerable to confusion attacks.\n"
					+ "This test ensures correct SRP state sequencing."
				).GetAwaiter().GetResult();

				using AuthTestHarness h = new AuthTestHarness();
				h.Server.OnSrpProofReceived(conn: 1, encryptedProof: new byte[16], seq: 0);
				LogAssert.AreEqual(0, h.Server.AuthResultBroadcastCount, "Premature proof must not produce any auth result.");
				// IsConnectionAuthenticated returns false in the harness so the gate falls through silently.
				AuthTestTrace.Log("SecurityTests", "SUCCESS", nameof(Security_SrpProofBeforeVerify_IsIgnored)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("SecurityTests", "FAILURE", $"{nameof(Security_SrpProofBeforeVerify_IsIgnored)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(Security_SrpProofBeforeVerify_IsIgnored)).GetAwaiter().GetResult();
			}
		}

		/// <summary>
		/// Out-of-order: a client that emits SrpVerify before a successful handshake (no
		/// encryption data established) must be rejected without progressing.
		/// </summary>
		[Test]
		public void Security_SrpVerifyBeforeHandshake_IsRejected()
		{
			try
			{
				AuthTestTrace.LogTestStart(
					nameof(Security_SrpVerifyBeforeHandshake_IsRejected),
					"Test: Out-of-order SrpVerify is rejected.\n"
					+ "Procedure: Emit SrpVerify before handshake is complete.\n"
					+ "Expected: No handshake or authentication result is produced.\n"
					+ "Failure: If a result is produced, the protocol is vulnerable to state confusion.\n"
					+ "This test ensures handshake state is enforced."
				).GetAwaiter().GetResult();
				using AuthTestHarness h = new AuthTestHarness();
				h.Server.OnSrpVerifyReceived(conn: 1, encryptedUsername: new byte[8], encryptedPublicEphemeral: new byte[8], seq: 0);
				LogAssert.AreEqual(0, h.Server.ServerHandshakeCount, "No handshake completed.");
				LogAssert.AreEqual(0, h.Server.AuthResultBroadcastCount, "Verify-before-handshake must not authenticate.");
				AuthTestTrace.Log("SecurityTests", "SUCCESS", nameof(Security_SrpVerifyBeforeHandshake_IsRejected)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("SecurityTests", "FAILURE", $"{nameof(Security_SrpVerifyBeforeHandshake_IsRejected)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(Security_SrpVerifyBeforeHandshake_IsRejected)).GetAwaiter().GetResult();
			}
		}

		/// <summary>
		/// Length cap: a payload larger than <c>CryptoHelper.MaxSrpPayloadBytes</c> must be
		/// rejected at the gate — never allocated through decryption.
		/// </summary>
		[Test]
		public async Task Security_OversizedSrpVerifyPayload_IsRejected()
		{
			try
			{
				await AuthTestTrace.LogTestStart(
					nameof(Security_OversizedSrpVerifyPayload_IsRejected),
					"Test: Oversized SRP payload rejection.\n"
					+ "Procedure: Attempt login with an SRP payload larger than the allowed maximum.\n"
					+ "Expected: The server must reject the payload, disconnect, and never allocate or process it.\n"
					+ "Failure: If the payload is accepted or processed, it indicates a DoS or buffer overflow risk.\n"
					+ "This test ensures strict input validation and safe memory handling."
				);
				using AuthTestHarness h = new AuthTestHarness();
				h.Store.SeedAccount("greta", "valid-password");
				h.Client.SrpVerifyInterceptor = (_, eph) =>
				{
					byte[] huge = new byte[CryptoHelper.MaxSrpPayloadBytes + 1];
					return (huge, eph);
				};
				await DriveUntilTerminated(h, "greta", "valid-password");
				LogAssert.IsFalse(h.Client.ReceivedSuccess,
				 "Oversized SrpVerify payload must NEVER be accepted as success.");
				LogAssert.AreNotEqual(ClientAuthenticationResult.LoginSuccess, h.Client.LastResult);
				LogAssert.IsTrue(h.Server.WasDisconnected, "Oversized payload must trigger a disconnect.");
				await AuthTestTrace.Log("SecurityTests", "SUCCESS", nameof(Security_OversizedSrpVerifyPayload_IsRejected));
			}
			catch (Exception ex)
			{
				await AuthTestTrace.Log("SecurityTests", "FAILURE", $"{nameof(Security_OversizedSrpVerifyPayload_IsRejected)}: {ex.Message}\n{ex.StackTrace}");
				throw;
			}
			finally
			{
				await AuthTestTrace.LogTestEnd(nameof(Security_OversizedSrpVerifyPayload_IsRejected));
			}
		}

		/// <summary>
		/// Tampered SrpVerify payload: any flipped bit in either the encrypted username or
		/// encrypted ephemeral must be detected (AES-GCM tag failure) and abort the flow.
		/// </summary>
		[Test]
		public async Task Security_TamperedSrpVerifyCiphertext_IsRejected()
		{
			try
			{
				await AuthTestTrace.LogTestStart(
					nameof(Security_TamperedSrpVerifyCiphertext_IsRejected),
					"Test: Tampered SrpVerify ciphertext rejection.\n"
					+ "Procedure: Flip a bit in the encrypted username or ephemeral and attempt login.\n"
					+ "Expected: The server must detect the tampering and abort the flow.\n"
					+ "Failure: If authentication succeeds, it indicates a cryptographic validation failure.\n"
					+ "This test ensures all ciphertexts are authenticated and validated."
				);
				using AuthTestHarness h = new AuthTestHarness();
				h.Store.SeedAccount("hank", "valid-password");
				h.Client.SrpVerifyInterceptor = (user, eph) =>
				{
					byte[] flipped = (byte[])eph.Clone();
					flipped[flipped.Length / 2] ^= 0x80;
					return (user, flipped);
				};
				await DriveUntilTerminated(h, "hank", "valid-password");
				LogAssert.IsFalse(h.Client.ReceivedSuccess,
				 "Tampered SrpVerify ciphertext must never authenticate.");
				LogAssert.AreNotEqual(ClientAuthenticationResult.LoginSuccess, h.Client.LastResult);
				await AuthTestTrace.Log("SecurityTests", "SUCCESS", nameof(Security_TamperedSrpVerifyCiphertext_IsRejected));
			}
			catch (Exception ex)
			{
				await AuthTestTrace.Log("SecurityTests", "FAILURE", $"{nameof(Security_TamperedSrpVerifyCiphertext_IsRejected)}: {ex.Message}\n{ex.StackTrace}");
				throw;
			}
			finally
			{
				await AuthTestTrace.LogTestEnd(nameof(Security_TamperedSrpVerifyCiphertext_IsRejected));
			}
		}

		// ───────────────────────── input-validation attacks ─────────────────────────

		/// <summary>
		/// Validators must reject empty/null/whitespace/oversized credentials before they
		/// ever reach the wire (prevents DoS by giant payloads + plaintext-credential leaks).
		/// </summary>
		[TestCase(null, "valid-password", Description = "null username")]
		[TestCase("", "valid-password", Description = "empty username")]
		[TestCase("ab", "valid-password", Description = "username < 3 chars")]
		[TestCase("alice", null, Description = "null password")]
		[TestCase("alice", "", Description = "empty password")]
		[TestCase("alice", "pw1", Description = "password < 4 chars")]
		public void Security_InvalidCredentials_RejectedAtClientValidator(string user, string pw)
		{
			try
			{
				AuthTestTrace.LogTestStart(
					nameof(Security_InvalidCredentials_RejectedAtClientValidator),
					$"Test: Invalid credentials are rejected at client validator.\n"
					+ $"Procedure: Attempt login with user=\"{user}\", pw=\"{pw}\".\n"
					+ "Expected: Credentials are rejected by the client, no SRP traffic is emitted.\n"
					+ "Failure: If credentials are accepted or traffic is emitted, client-side validation is broken.\n"
					+ "This test ensures only valid credentials reach the wire."
				).GetAwaiter().GetResult();
				using AuthTestHarness h = new AuthTestHarness();
				bool accepted = h.Client.SetLoginCredentials(user, pw, register: false);
				LogAssert.IsFalse(accepted, "Invalid credentials must be rejected by the client validator (never sent on the wire).");
				LogAssert.AreEqual(0, h.Client.SrpVerifySends.Count, "No SrpVerify must be emitted for invalid creds.");
				AuthTestTrace.Log("SecurityTests", "SUCCESS", nameof(Security_InvalidCredentials_RejectedAtClientValidator)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("SecurityTests", "FAILURE", $"{nameof(Security_InvalidCredentials_RejectedAtClientValidator)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(Security_InvalidCredentials_RejectedAtClientValidator)).GetAwaiter().GetResult();
			}
		}

		/// <summary>
		/// Oversized inputs at the client validator: a multi-MB username must be rejected
		/// long before encryption / SRP math could be invoked.
		/// </summary>
		[Test]
		public void Security_OversizedUsername_RejectedAtValidator()
		{
			try
			{
				AuthTestTrace.LogTestStart(
					nameof(Security_OversizedUsername_RejectedAtValidator),
					"Test: Oversized username is rejected at client validator.\n"
					+ "Procedure: Attempt login with a multi-megabyte username.\n"
					+ "Expected: The client must reject the username before any encryption or SRP math.\n"
					+ "Failure: If the username is accepted, it indicates a DoS or memory bug.\n"
					+ "This test ensures input size limits are enforced."
				).GetAwaiter().GetResult();
				using AuthTestHarness h = new AuthTestHarness();
				string huge = new string('A', 1 << 20); // 1 MiB
				LogAssert.IsFalse(h.Client.SetLoginCredentials(huge, "valid-password", register: false),
				 "Multi-MB username must be rejected by the validator.");
				AuthTestTrace.Log("SecurityTests", "SUCCESS", nameof(Security_OversizedUsername_RejectedAtValidator)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("SecurityTests", "FAILURE", $"{nameof(Security_OversizedUsername_RejectedAtValidator)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(Security_OversizedUsername_RejectedAtValidator)).GetAwaiter().GetResult();
			}
		}

		// ───────────────────────── output-uniqueness attacks ─────────────────────────

		/// <summary>
		/// Per-session uniqueness: across N independent successful logins, every emitted
		/// server-handshake public key, cookie, encrypted-salt, and encrypted-token must be
		/// pairwise distinct — collisions would imply nonce reuse or static-key reuse.
		/// </summary>
		[Test]
		public async Task Security_SuccessfulSessions_ProduceUniquePerSessionMaterial()
		{
			try
			{
				await AuthTestTrace.LogTestStart(
					nameof(Security_SuccessfulSessions_ProduceUniquePerSessionMaterial),
					"Test: Per-session uniqueness of handshake and SRP material.\n"
					+ "Procedure: Perform multiple successful logins and collect all handshake/session material.\n"
					+ "Expected: All per-session materials (public keys, cookies, encrypted salt/token) must be pairwise distinct.\n"
					+ "Failure: If any collision occurs, it indicates nonce/key reuse or a cryptographic bug.\n"
					+ "This test ensures every session is cryptographically unique."
				);

				const int N = 4;
				byte[][] serverPubKeys = new byte[N][];
				byte[][] cookies = new byte[N][];
				byte[][] verifyEphCipher = new byte[N][];
				byte[][] proofCipher = new byte[N][];

				for (int i = 0; i < N; i++)
				{
					using AuthTestHarness h = new AuthTestHarness();
					h.Store.SeedAccount("ivy", "uniq-pw");
					await AuthTestTrace.Log("SecurityTests", "STEP", $"Login attempt {i + 1}/{N} for user 'ivy'...");
					LogAssert.AreEqual(ClientAuthenticationResult.LoginSuccess, await Drive(h, "ivy", "uniq-pw"));
					serverPubKeys[i] = h.Server.LastServerPublicKey != null ? h.Server.LastServerPublicKey : Array.Empty<byte>();
					cookies[i] = h.Server.LastChallengeCookie != null ? h.Server.LastChallengeCookie : Array.Empty<byte>();
					verifyEphCipher[i] = h.Client.SrpVerifySends.Count > 0 ? h.Client.SrpVerifySends[0].EncryptedClientEphemeral : Array.Empty<byte>();
					proofCipher[i] = h.Client.SrpProofSends.Count > 0 ? h.Client.SrpProofSends[0].EncryptedProof : Array.Empty<byte>();
				}

				await AuthTestTrace.Log("SecurityTests", "STEP", "Checking for uniqueness of all per-session materials...");
				AssertAllDistinct(serverPubKeys, "server X25519 public key");
				AssertAllDistinct(cookies, "handshake cookie");
				AssertAllDistinct(verifyEphCipher, "encrypted SRP client ephemeral");
				AssertAllDistinct(proofCipher, "encrypted SRP proof");
				await AuthTestTrace.Log("SecurityTests", "SUCCESS", nameof(Security_SuccessfulSessions_ProduceUniquePerSessionMaterial));
			}
			catch (Exception ex)
			{
				await AuthTestTrace.Log("SecurityTests", "FAILURE", $"{nameof(Security_SuccessfulSessions_ProduceUniquePerSessionMaterial)}: {ex.Message}\n{ex.StackTrace}");
				throw;
			}
			finally
			{
				await AuthTestTrace.LogTestEnd(nameof(Security_SuccessfulSessions_ProduceUniquePerSessionMaterial));
			}
		}

		// ───────────────────────── X25519 low-order-point attack ─────────────────────────

		/// <summary>
		/// X25519 low-order point: a valid-length (32-byte) all-zero public key is the
		/// canonical low-order point that produces an all-zero shared secret regardless of
		/// the server's private key. A well-hardened server must reject it before performing
		/// ECDH, treating it the same as any other weak/malformed key.
		/// </summary>
		[Test]
		public void Security_ZeroFilledX25519PublicKey_IsRejected()
		{
			try
			{
				AuthTestTrace.LogTestStart(
					nameof(Security_ZeroFilledX25519PublicKey_IsRejected),
					"Test: Zero-filled X25519 public key (low-order point) is rejected.\n"
					+ "Procedure: Submit a valid-length (32-byte) all-zero public key during phase-1 handshake.\n"
					+ "Expected: Server must disconnect and not issue a cookie or progress to ECDH.\n"
					+ "Failure: If the server proceeds, it will compute an all-zero shared secret, breaking forward secrecy.\n"
					+ "This test ensures the server rejects X25519 low-order-point inputs."
				).GetAwaiter().GetResult();

				using AuthTestHarness h = new AuthTestHarness();
				// 32 bytes, all zeros — valid length but the X25519 low-order point.
				byte[] zeroKey = new byte[32];
				bool threw = false;
				try { h.Server.OnHandshakeReceived(1, zeroKey, cookie: null!, null, minVersion: 1, maxVersion: 1); }
				catch { threw = true; }

				LogAssert.IsTrue(h.Server.WasDisconnected || threw,
					"Server must refuse (disconnect or throw) on a zero-filled X25519 public key.");
				LogAssert.AreEqual(0, h.Server.CookieChallengeCount,
					"No cookie challenge must be issued for a zero-filled (low-order-point) public key.");
				LogAssert.AreEqual(0, h.Server.ServerHandshakeCount,
					"No server handshake must be emitted for a zero-filled public key.");
				AuthTestTrace.Log("SecurityTests", "SUCCESS", nameof(Security_ZeroFilledX25519PublicKey_IsRejected)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("SecurityTests", "FAILURE", $"{nameof(Security_ZeroFilledX25519PublicKey_IsRejected)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(Security_ZeroFilledX25519PublicKey_IsRejected)).GetAwaiter().GetResult();
			}
		}

		// ───────────────────────── server-proof (M2) tampering ─────────────────────────

		/// <summary>
		/// Tampered server proof (M2): flipping a bit in the encrypted M2 that the server
		/// broadcasts back to the client must cause the client to reject the session — it
		/// must not flag <c>ReceivedSuccess</c>. If the client ignores M2 verification and
		/// always accepts success, a MITM could strip or corrupt M2 and observe the client
		/// completing authentication without the server having completed SRP.
		/// </summary>
		[Test]
		public async Task Security_TamperedServerProof_M2_ClientDoesNotAccept()
		{
			try
			{
				await AuthTestTrace.LogTestStart(
					nameof(Security_TamperedServerProof_M2_ClientDoesNotAccept),
					"Test: Tampered server SRP proof (M2) is rejected by the client.\n"
					+ "Procedure: Flip a bit in the encrypted M2 (server→client) before it reaches the client, then complete the login.\n"
					+ "Expected: Client must not flag ReceivedSuccess — M2 verification must fail and the session must not be established.\n"
					+ "Failure: If the client accepts the tampered M2, it indicates the client does not verify the server proof.\n"
					+ "This test ensures mutual authentication: both sides verify each other's SRP proof."
				);

				using AuthTestHarness h = new AuthTestHarness();
				h.Store.SeedAccount("mallory", "tamper-m2-pw");

				// Install a server-side interceptor that flips a bit in the encrypted M2
				// before it is forwarded to the client.
				h.Server.SrpSuccessInterceptor = (original) =>
				{
					byte[] tampered = (byte[])original.Clone();
					int idx = tampered.Length / 2;
					tampered[idx] ^= 0x20;
					return tampered;
				};

				ClientAuthenticationResult result = await Drive(h, "mallory", "tamper-m2-pw");
				LogAssert.AreNotEqual(ClientAuthenticationResult.LoginSuccess, result,
					"Tampered M2 must NEVER be accepted by the client as login success.");
				LogAssert.IsFalse(h.Client.ReceivedSuccess,
					"Client must not flag ReceivedSuccess when the server proof (M2) is corrupted.");
				await AuthTestTrace.Log("SecurityTests", "SUCCESS", nameof(Security_TamperedServerProof_M2_ClientDoesNotAccept));
			}
			catch (Exception ex)
			{
				await AuthTestTrace.Log("SecurityTests", "FAILURE", $"{nameof(Security_TamperedServerProof_M2_ClientDoesNotAccept)}: {ex.Message}\n{ex.StackTrace}");
				throw;
			}
			finally
			{
				await AuthTestTrace.LogTestEnd(nameof(Security_TamperedServerProof_M2_ClientDoesNotAccept));
			}
		}

		private static void AssertAllDistinct(byte[][] samples, string label)
		{
			for (int i = 0; i < samples.Length; i++)
				for (int j = i + 1; j < samples.Length; j++)
					LogAssert.IsFalse(samples[i].SequenceEqual(samples[j]),
					 $"Two sessions emitted identical {label} (index {i} == index {j}).");
		}

		private static object GetField(object instance, string name) =>
			typeof(ClientAuthenticatorCore)
				.GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
				.GetValue(instance);

		// ---------------------------------------------------------------------

		private static void AssertNoSubsequence(byte[] haystack, byte[] needle, string label)
		{
			if (needle.Length == 0 || haystack.Length < needle.Length) return;
			for (int i = 0; i <= haystack.Length - needle.Length; i++)
			{
				bool match = true;
				for (int j = 0; j < needle.Length; j++)
				{
					if (haystack[i + j] != needle[j]) { match = false; break; }
				}
				if (match)
					LogAssert.Fail($"{label}: plaintext secret found at offset {i} (len={needle.Length}, haystack.len={haystack.Length}).");
			}
		}
	}
}