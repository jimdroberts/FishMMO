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

		/// <summary>True once <see cref="DisconnectConnection"/> has been called for any reason.</summary>
		public bool WasDisconnected => disconnected;

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

		#region BaseAuthenticatorCore<int> abstracts

		protected override bool IsConnectionAuthenticated(int conn) => false;
		protected override string GetConnectionAddress(int conn) => "127.0.0.1";
		protected override int GetConnectionClientId(int conn) => conn;

		protected override void BroadcastCookieChallenge(int conn, byte[] cookie)
		{
			AuthTestTrace.Log("Server", "BroadcastCookieChallenge", $"conn={conn} cookie={AuthTestTrace.Hex(cookie)}");
			LastChallengeCookie = cookie;
			CookieChallengeCount++;
			// Phase-1: serverPublicKey = null signals a cookie challenge to the client.
			client!.OnServerHandshakeReceived(serverPublicKey: null!, cookie: cookie, agreedVersion: 0);
		}

		protected override void BroadcastServerHandshake(int conn, byte[] serverPublicKey, ushort agreedVersion)
		{
			AuthTestTrace.Log("Server", "BroadcastServerHandshake", $"conn={conn} pk={AuthTestTrace.Hex(serverPublicKey)} v={agreedVersion}");
			LastServerPublicKey = serverPublicKey;
			ServerHandshakeCount++;
			// Phase-2: ECDH complete; cookie is unused on the client at this point but must be non-null.
			client!.OnServerHandshakeReceived(serverPublicKey, cookie: Array.Empty<byte>(), agreedVersion: agreedVersion);
		}

		protected override void DisconnectConnection(int conn, bool graceful)
		{
			AuthTestTrace.Log("Server", "DisconnectConnection", $"conn={conn} graceful={graceful}");
			disconnected = true;
			client?.OnDisconnected();
		}

		#endregion

		#region SrpAuthenticatorCore<int> abstracts

		protected override void OnAuthenticationResult(int conn, bool authenticated) { /* test hook — captured via TCS in harness */ }

		protected override bool IsConnectionActive(int conn) => !disconnected;

		protected override void BroadcastAuthResult(int conn, ClientAuthenticationResult result, bool reliable)
		{
			AuthTestTrace.Log("Server", "BroadcastAuthResult", $"conn={conn} result={result} reliable={reliable}");
			AuthResultBroadcastCount++;
			client!.OnAuthResultReceived(result);
		}

		protected override void BroadcastSrpVerifyResponse(int conn, byte[] encryptedSalt, byte[] encryptedPublicServerEphemeral)
		{
			AuthTestTrace.Log("Server", "BroadcastSrpVerifyResponse", $"conn={conn} salt={AuthTestTrace.Hex(encryptedSalt)} pkB={AuthTestTrace.Hex(encryptedPublicServerEphemeral)}");
			client!.OnSrpVerifyResponseReceived(encryptedSalt, encryptedPublicServerEphemeral);
		}

		protected override void BroadcastSrpSuccess(int conn, byte[] encryptedServerProof, ClientAuthenticationResult result, byte[]? encryptedToken)
		{
			AuthTestTrace.Log("Server", "BroadcastSrpSuccess", $"conn={conn} result={result} proof={AuthTestTrace.Hex(encryptedServerProof)} token={AuthTestTrace.Hex(encryptedToken)}");
			client!.OnSrpSuccessReceived(encryptedServerProof, result, encryptedToken ?? Array.Empty<byte>());
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

		/// <summary>
		/// Returns <see cref="ClientAuthenticationResult.Banned"/> when the account has
		/// <see cref="AccessLevel.Banned"/>, allowing tests that seed banned accounts to
		/// verify rejection without a real database.
		/// </summary>
		protected override Task<ClientAuthenticationResult> TryLoginAsync(ClientAuthenticationResult defaultResult, string username)
		{
			if (store.TryGet(username, out InMemoryAccountStore.Lookup lookup) && lookup.AccessLevel == AccessLevel.Banned)
				return Task.FromResult(ClientAuthenticationResult.Banned);
			return Task.FromResult(defaultResult);
		}

		#endregion
	}
}