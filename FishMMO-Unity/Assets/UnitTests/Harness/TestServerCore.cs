using System;
using System.Threading.Tasks;
using FishMMO.Auth.Core;
using FishMMO.Auth.Implementation;
using OtpNet;

namespace FishMMO.UnitTests.Harness
{
	/// <summary>
	/// Concrete <see cref="SrpAuthenticatorCore{TConnection}"/> for unit tests. Routes all
	/// outgoing broadcasts directly into the paired <see cref="TestClientCore"/>'s
	/// <c>On*Received</c> entry points, and consults <see cref="InMemoryAccountStore"/>
	/// for account lookups. TConnection is <c>int</c> (a logical client id).
	/// </summary>
	internal sealed class TestServerCore : SrpAuthenticatorCore<int>
	{
		private readonly InMemoryAccountStore store;
		private TestClientCore? client;
		private bool disconnected;

		public TestServerCore(ISrpAccountManager<int> accountManager, InMemoryAccountStore store)
			: base(accountManager)
		{
			this.store = store;
		}

		/// <summary>Pair with the paired client (called by the harness after both are constructed).</summary>
		public void Pair(TestClientCore clientCore) => client = clientCore;

		/// <summary>
		/// Optional resolver for <see cref="GetConnectionAddress"/>. Lets rate-limiter tests
		/// simulate multiple connections behind one NAT IP (all resolve to the same address)
		/// or verify that distinct source IPs are never grouped under one rate-limit key.
		/// When unset, the default per-connection synthetic loopback address is used.
		/// </summary>
		public Func<int, string>? AddressResolver { get; set; }

		/// <summary>True once <see cref="DisconnectConnection"/> has been called for any reason.</summary>
		public bool WasDisconnected => disconnected;

		/// <summary>Total number of <see cref="DisconnectConnection"/> calls (server-initiated disconnects).</summary>
		public int DisconnectCount { get; private set; }

		/// <summary>Most recent cookie issued via <see cref="BroadcastCookieChallenge"/>.</summary>
		public byte[]? LastChallengeCookie { get; private set; }
		/// <summary>Most recent server X25519 public key emitted via <see cref="BroadcastServerHandshake"/>.</summary>
		public byte[]? LastServerPublicKey { get; private set; }
		/// <summary>Number of <see cref="BroadcastCookieChallenge"/> calls.</summary>
		public int CookieChallengeCount { get; private set; }
		/// <summary>Number of <see cref="BroadcastServerHandshake"/> calls.</summary>
		public int ServerHandshakeCount { get; private set; }
		/// <summary>Number of <see cref="BroadcastAuthResult"/> calls.</summary>
		public int AuthResultBroadcastCount { get; private set; }

		/// <summary>
		/// Optional hook invoked before the encrypted server proof (M2) is forwarded to the
		/// client in <see cref="BroadcastSrpSuccess"/>. Return a mutated copy to simulate a
		/// MITM tampering with M2; return <c>null</c> to drop the message entirely.
		/// If not set, the original proof is forwarded unchanged.
		/// </summary>
		public Func<byte[], byte[]?>? SrpSuccessInterceptor;

		#region BaseAuthenticatorCore<int> abstracts

		protected override bool IsConnectionAuthenticated(int conn) => false;
		protected override string GetConnectionAddress(int conn) => AddressResolver?.Invoke(conn) ?? $"127.0.0.{conn}";
		protected override float AccountVerifyDebounceSeconds => 0f;
		protected override int GetConnectionClientId(int conn) => conn;

		protected override void BroadcastCookieChallenge(int conn, byte[] cookie)
		{
			_ = AuthTestTrace.Log("Server", "BroadcastCookieChallenge", $"conn={conn} cookie={AuthTestTrace.Hex(cookie)}");
			LastChallengeCookie = cookie;
			CookieChallengeCount++;
			// Phase-1: serverPublicKey = null signals a cookie challenge to the client.
			client!.OnServerHandshakeReceived(serverPublicKey: null!, cookie: cookie, agreedVersion: 0);
		}

		protected override void BroadcastServerHandshake(int conn, byte[] serverPublicKey, ushort agreedVersion)
		{
			_ = AuthTestTrace.Log("Server", "BroadcastServerHandshake", $"conn={conn} pk={AuthTestTrace.Hex(serverPublicKey)} v={agreedVersion}");
			LastServerPublicKey = serverPublicKey;
			ServerHandshakeCount++;
			// Phase-2: ECDH complete; cookie is unused on the client at this point but must be non-null.
			client!.OnServerHandshakeReceived(serverPublicKey, cookie: Array.Empty<byte>(), agreedVersion: agreedVersion);
		}

		protected override void DisconnectConnection(int conn, bool graceful)
		{
			_ = AuthTestTrace.Log("Server", "DisconnectConnection", $"conn={conn} graceful={graceful}");
			disconnected = true;
			DisconnectCount++;
			client?.OnDisconnected();
		}

		#endregion

		#region SrpAuthenticatorCore<int> abstracts

		protected override void OnAuthenticationResult(int conn, bool authenticated) { /* test hook — captured via TCS in harness */ }

		protected override bool IsConnectionActive(int conn) => !disconnected;

		/// <summary>
		/// Marks the modelled connection live again, for a test that drives more than one
		/// attempt through the same harness.
		/// </summary>
		/// <remarks>
		/// <see cref="disconnected"/> stands in for a whole connection, and
		/// <see cref="DisconnectConnection"/> latches it one way. Every rejection funnels through
		/// the core's <c>RejectAndPurge</c>, which broadcasts the result only
		/// <c>if (IsConnectionActive(conn))</c> — so once one attempt had been refused, the next
		/// one was answered with silence and the client waited on a result that would never
		/// arrive. Any test making two consecutive REFUSED attempts therefore timed out on the
		/// second, which is why the per-account lockout tests could never reach the assertion
		/// they exist for: the lockout is only reached after ten failures.
		/// <para>
		/// A real server sees a genuinely new connection per attempt, with its own state, so
		/// clearing this at the start of an attempt is what models production. It is cleared at
		/// the START of the next attempt rather than at the end of the previous one so that
		/// <see cref="WasDisconnected"/> still reports what the attempt just made did.
		/// </para>
		/// </remarks>
		internal void BeginNewConnection(int conn)
		{
			/* A real transport is what tells the authenticator that a connection ended, and this
			 * harness has no transport — so state the core keys by connection id outlived the
			 * attempt that created it. Two separate stalls came out of that:
			 *
			 *  - After a REFUSED attempt, DisconnectConnection had latched `disconnected`, and
			 *    every rejection routes through the core's RejectAndPurge, which broadcasts only
			 *    `if (IsConnectionActive(conn))`. The next refusal was therefore answered with
			 *    silence.
			 *  - After a SUCCESSFUL attempt, nothing disconnected at all — correctly, the client
			 *    is authenticated — so the core still held authenticated state for this id and
			 *    ignored the next attempt's handshake as a replay on a live session. The client
			 *    waited for a cookie challenge that was never going to come.
			 *
			 * HandleConnectionStopped is the same entry point the hosting transport calls, so
			 * this models production rather than reaching around it. */
			HandleConnectionStopped(conn);
			disconnected = false;
			ConnectionEpoch++;
		}

		/// <summary>
		/// Counts the attempts driven through this harness. Stable for the whole of one attempt.
		/// </summary>
		/// <remarks>
		/// Exists so a test that wants a different source address per attempt has something to
		/// derive it from that does not change underneath the handshake.
		/// <see cref="GetConnectionAddress"/> is called at least twice per attempt — once to bind
		/// the cookie to an IP and again to verify the echoed cookie against it — so a resolver
		/// that advances on every CALL issues the challenge from one address and validates it
		/// from another. The cookie then cannot verify, by design, and the connection is dropped
		/// at the handshake before authentication is ever reached.
		/// </remarks>
		public int ConnectionEpoch { get; private set; }

		protected override void BroadcastAuthResult(int conn, ClientAuthenticationResult result, bool reliable)
		{
			_ = AuthTestTrace.Log("Server", "BroadcastAuthResult", $"conn={conn} result={result} reliable={reliable}");
			AuthResultBroadcastCount++;
			client!.OnAuthResultReceived(result);
		}

		protected override void BroadcastSrpVerifyResponse(int conn, byte[] encryptedSalt, byte[] encryptedPublicServerEphemeral)
		{
			_ = AuthTestTrace.Log("Server", "BroadcastSrpVerifyResponse", $"conn={conn} salt={AuthTestTrace.Hex(encryptedSalt)} pkB={AuthTestTrace.Hex(encryptedPublicServerEphemeral)}");
			client!.OnSrpVerifyResponseReceived(encryptedSalt, encryptedPublicServerEphemeral);
		}

		protected override void BroadcastSrpSuccess(int conn, byte[] encryptedServerProof, ClientAuthenticationResult result, byte[]? encryptedToken)
		{
			_ = AuthTestTrace.Log("Server", "BroadcastSrpSuccess", $"conn={conn} result={result} proof={AuthTestTrace.Hex(encryptedServerProof)} token={AuthTestTrace.Hex(encryptedToken)}");
			byte[]? outProof = SrpSuccessInterceptor is null ? encryptedServerProof : SrpSuccessInterceptor(encryptedServerProof);
			if (outProof is null)
			{
				_ = AuthTestTrace.Log("Server", "BroadcastSrpSuccess.dropped");
				return;
			}
			client!.OnSrpSuccessReceived(outProof, result, encryptedToken ?? Array.Empty<byte>());
		}

		protected override void EnqueueMainThread(int conn, Action action) => action();

		protected override bool IsAllowedUsername(string username) =>
			!string.IsNullOrEmpty(username) && username.Length >= 3 && username.Length <= 32;

		protected override bool IsAllowedEmailUsername(string username) =>
			!string.IsNullOrEmpty(username) && username.Contains('@') && username.Length <= 254;

		protected override Task<SrpAccountLookupResult> FetchAccountForLoginAsync(string identifier, bool isEmail)
		{
			if (store.TryGet(identifier, out InMemoryAccountStore.Lookup row))
			{
				return Task.FromResult(new SrpAccountLookupResult
				{
					IsSuccess = true,
					IsVerified = row.IsVerified,
					Salt = row.Salt,
					Verifier = row.Verifier,
					AccessLevel = row.AccessLevel,
					TotpEnabled = row.TotpEnabled,
				});
			}
			return Task.FromResult(new SrpAccountLookupResult { IsSuccess = false });
		}

		protected override Task<bool> CheckIsOnlineAsync(string username) => Task.FromResult(store.IsOnline(username));

		protected override Task<bool> CheckHasPendingKickAsync(string username) => Task.FromResult(store.HasPendingKick(username));

		protected override Task PersistKickRequestAsync(string username)
		{
			store.SetPendingKick(username, true);
			return Task.CompletedTask;
		}

		protected override Task PersistTokenHashAsync(string username, string tokenHash, int expirationMinutes)
		{
			store.PersistTokenHash(username, tokenHash, expirationMinutes);
			return Task.CompletedTask;
		}

		protected override Task<bool> VerifyTotpCodeAsync(string username, string totpCode, byte[] totpMasterKey)
		{
			string? secret = store.GetTotpSecret(username);
			if (string.IsNullOrEmpty(secret)) return Task.FromResult(false);
			byte[] secretBytes = Base32Encoding.ToBytes(secret);
			Totp totp = new Totp(secretBytes, mode: OtpHashMode.Sha1, step: 30, totpSize: 6);
			bool ok = totp.VerifyTotp(totpCode, out _, new VerificationWindow(previous: 1, future: 1));
			return Task.FromResult(ok);

		}
		protected override Task<bool> TryResendVerificationEmailIfExpiredAsync(string username, DateTime? verifyCodeExpiresUtc)
		{
			// Test harness: no email infrastructure.
			return Task.FromResult(false);
		}

		#endregion
	}
}