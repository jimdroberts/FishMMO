using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FishMMO.Auth.Core;
using FishMMO.Auth.Implementation;
using System.Security.Cryptography;

namespace FishMMO.UnitTests.Harness
{
	/// <summary>
	/// Concrete <see cref="ClientAuthenticatorCore"/> for unit tests. Routes Send* methods
	/// to the paired <see cref="TestServerCore"/>'s On*Received entry points and exposes
	/// captured payloads + awaitable results for assertions.
	/// </summary>
	internal sealed class TestClientCore : ClientAuthenticatorCore
	{
		private TestServerCore? server;
		private int clientId;

		/// <summary>Captured create-account broadcast payloads (one entry per send).</summary>
		public readonly List<CreateAccountCapture> CreateAccountSends = new List<CreateAccountCapture>();
		/// <summary>Captured TwoFactorSetup callback payloads.</summary>
		public readonly List<TwoFactorSetupCapture> TwoFactorSetups = new List<TwoFactorSetupCapture>();
		/// <summary>Captured account-verify (email code) broadcast payloads.</summary>
		public readonly List<AccountVerifyCapture> AccountVerifySends = new List<AccountVerifyCapture>();
		/// <summary>Captured SrpVerify broadcast payloads (encrypted username + clientEphemeral).</summary>
		public readonly List<SrpVerifyCapture> SrpVerifySends = new List<SrpVerifyCapture>();
		/// <summary>Captured SrpProof broadcast payloads (encrypted M1).</summary>
		public readonly List<SrpProofCapture> SrpProofSends = new List<SrpProofCapture>();

		/// <summary>Optional hook that mutates / replaces the next outgoing SrpProof payload before it
		/// reaches the server. Used by security tests to validate tamper rejection. Return <c>null</c>
		/// to drop the message entirely (server will time out waiting for proof).</summary>
		public Func<byte[], byte[]?>? SrpProofInterceptor;
		/// <summary>Optional hook for SrpVerify (analogous to <see cref="SrpProofInterceptor"/>).</summary>
		public Func<byte[], byte[], (byte[] user, byte[] eph)?>? SrpVerifyInterceptor;

		/// <summary>Resolved when an auth result is received from the server.</summary>
		/// <summary>In-memory token store used for simplified token auth bypass in tests. Set by the harness.</summary>
		internal InMemoryAccountStore? TokenStore;

		/// <summary>Token string staged by <see cref="SetToken"/> for use in <see cref="SendTokenAuth"/>.</summary>
		private string? pendingTokenString;

		/// <summary>Sentinel byte used to signal that a token auth is pending to the base class.</summary>
		private static readonly byte[] TokenSentinel = new byte[] { 1 };

		public TaskCompletionSource<ClientAuthenticationResult> AuthResultTcs { get; private set; } =
			new TaskCompletionSource<ClientAuthenticationResult>(TaskCreationOptions.RunContinuationsAsynchronously);

		/// <summary>True after a successful SRP success message (with or without token).</summary>
		public bool ReceivedSuccess { get; private set; }
		/// <summary>Last result delivered by either SrpSuccess or generic AuthResult.</summary>
		public ClientAuthenticationResult? LastResult { get; private set; }
		/// <summary>True once <see cref="Disconnect"/> was invoked.</summary>
		public bool WasDisconnected { get; private set; }

		public TestClientCore() { }

		/// <summary>Pair with the test server (called by the harness after both are constructed).</summary>
		public void Pair(TestServerCore serverCore, int connectionId)
		{
			server = serverCore;
			clientId = connectionId;
		}

		/// <summary>Reset captures + result TCS for a fresh attempt with the same instance.</summary>
		public void ResetForNextAttempt()
		{
			AuthResultTcs = new TaskCompletionSource<ClientAuthenticationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
			ReceivedSuccess = false;
			LastResult = null;
			WasDisconnected = false;
			pendingTokenString = null;
			ClearAuthToken();
			CreateAccountSends.Clear();
			TwoFactorSetups.Clear();
			AccountVerifySends.Clear();
		}

		/// <summary>
		/// Simulates a client disconnect and reconnect under a new logical connection ID.
		/// Resets all per-session state (equivalent to <see cref="ResetForNextAttempt"/>) and
		/// updates the connection ID used when routing sends to the server. Does NOT clear the
		/// session-capture lists (<see cref="SrpVerifySends"/>, <see cref="SrpProofSends"/>)
		/// so that tests can compare material across multiple sessions on the same instance.
		/// </summary>
		/// <param name="newConnectionId">The connection ID the server will see for the next session.</param>
		public void ReconnectAs(int newConnectionId)
		{
			ResetForNextAttempt();
			clientId = newConnectionId;
		}

		#region Convenience helpers for tests

		/// <summary>
		/// Stages a token string for use in the next token auth flow.
		/// Returns <c>false</c> if the token is null or empty.
		/// </summary>
		/// <param name="token">Token identifier returned by <see cref="InMemoryAccountStore"/> issue methods.</param>
		public bool SetToken(string token)
		{
			if (string.IsNullOrEmpty(token))
				return false;
			pendingTokenString = token;
			SetStoredAuthToken(TokenSentinel);
			return true;
		}

		/// <summary>
		/// Drives a full SRP login and awaits the result.
		/// </summary>
		/// <param name="username">Account username.</param>
		/// <param name="password">Account password.</param>
		/// <param name="timeoutMs">Maximum milliseconds to wait for the result.</param>
		public async Task<ClientAuthenticationResult> AttemptLogin(string username, string password, int timeoutMs = 5000)
		{
			ResetForNextAttempt();
			SetLoginCredentials(username, password, register: false);
			OnConnected();
			Task<ClientAuthenticationResult> resultTask = AuthResultTcs.Task;
			Task completed = await Task.WhenAny(resultTask, Task.Delay(timeoutMs));
			if (!object.ReferenceEquals(resultTask, completed))
				throw new TimeoutException($"AttemptLogin did not complete within {timeoutMs} ms.");
			return await resultTask;
		}

		/// <summary>
		/// Stages a token then drives the full token auth flow and awaits the result.
		/// </summary>
		/// <param name="token">Token identifier returned by <see cref="InMemoryAccountStore"/> issue methods.</param>
		/// <param name="timeoutMs">Maximum milliseconds to wait for the result.</param>
		public async Task<ClientAuthenticationResult> AttemptTokenLogin(string token, int timeoutMs = 5000)
		{
			ResetForNextAttempt();
			if (!SetToken(token))
				return ClientAuthenticationResult.TokenInvalid;
			OnConnected();
			Task<ClientAuthenticationResult> resultTask = AuthResultTcs.Task;
			Task completed = await Task.WhenAny(resultTask, Task.Delay(timeoutMs));
			if (!object.ReferenceEquals(resultTask, completed))
				throw new TimeoutException($"AttemptTokenLogin did not complete within {timeoutMs} ms.");
			return await resultTask;
		}

		#endregion

		#region Send abstracts — route to server

		protected override void SendClientHandshake(byte[] publicKey, byte[]? cookie, ushort minVersion, ushort maxVersion)
		{
			AuthTestTrace.Log("Client", "SendClientHandshake", $"pk={AuthTestTrace.Hex(publicKey)} cookie={AuthTestTrace.Hex(cookie)} versions={minVersion}..{maxVersion}");
			// Server side expects null-cookie on phase 1, non-null on phase 2.
			server!.OnHandshakeReceived(clientId, publicKey, cookie!, minVersion, maxVersion);
		}

		protected override void SendTokenAuth(byte[] encryptedToken, uint seq)
		{
			AuthTestTrace.Log("Client", "SendTokenAuth", $"seq={seq} token={AuthTestTrace.Hex(encryptedToken)}");
			// Simplified token auth: bypass real crypto decryption and validate the pending token
			// directly against the in-memory store. This exercises auth logic without requiring a
			// full TokenAuthenticatorCore worker setup.
			if (TokenStore == null || string.IsNullOrEmpty(pendingTokenString))
			{
				OnAuthResultReceived(ClientAuthenticationResult.ServerBusy);
				return;
			}
			ClientAuthenticationResult result = TokenStore.ValidateToken(pendingTokenString);
			OnAuthResultReceived(result);
		}

		protected override void SendSrpVerify(byte[] encryptedUsername, byte[] encryptedClientEphemeral, uint seq)
		{
			AuthTestTrace.Log("Client", "SendSrpVerify", $"seq={seq} user={AuthTestTrace.Hex(encryptedUsername)} pkA={AuthTestTrace.Hex(encryptedClientEphemeral)}");
			SrpVerifySends.Add(new SrpVerifyCapture(encryptedUsername, encryptedClientEphemeral, seq));
			byte[] outUser = encryptedUsername;
			byte[] outEph = encryptedClientEphemeral;
			if (SrpVerifyInterceptor != null)
			{
				(byte[] user, byte[] eph)? replaced = SrpVerifyInterceptor(encryptedUsername, encryptedClientEphemeral);
				if (replaced is null)
				{
					AuthTestTrace.Log("Client", "SendSrpVerify.dropped");
					return;
				}
				outUser = replaced.Value.user;
				outEph = replaced.Value.eph;
			}
			server!.OnSrpVerifyReceived(clientId, outUser, outEph, seq);
		}

		protected override void SendSrpProof(byte[] encryptedProof, uint seq)
		{
			AuthTestTrace.Log("Client", "SendSrpProof", $"seq={seq} M1={AuthTestTrace.Hex(encryptedProof)}");
			SrpProofSends.Add(new SrpProofCapture(encryptedProof, seq));
			byte[]? outProof = SrpProofInterceptor is null ? encryptedProof : SrpProofInterceptor(encryptedProof);
			if (outProof is null)
			{
				AuthTestTrace.Log("Client", "SendSrpProof.dropped");
				return;
			}
			server!.OnSrpProofReceived(clientId, outProof, seq);
		}

		protected override void SendCreateAccount(byte[] encryptedUsername, byte[] encryptedEmail, byte[] encryptedAge,
			byte[] encryptedSalt, byte[] encryptedVerifier, uint seq)
		{
			AuthTestTrace.Log("Client", "SendCreateAccount", $"seq={seq} user={AuthTestTrace.Hex(encryptedUsername)} email={AuthTestTrace.Hex(encryptedEmail)} age={AuthTestTrace.Hex(encryptedAge)} salt={AuthTestTrace.Hex(encryptedSalt)} v={AuthTestTrace.Hex(encryptedVerifier)}");
			CreateAccountSends.Add(new CreateAccountCapture(encryptedUsername, encryptedEmail, encryptedAge, encryptedSalt, encryptedVerifier, seq));
		}

		protected override void SendAccountVerify(byte[] encryptedUsername, byte[] encryptedCode, uint seq)
		{
			AuthTestTrace.Log("Client", "SendAccountVerify", $"seq={seq} user={AuthTestTrace.Hex(encryptedUsername)} code={AuthTestTrace.Hex(encryptedCode)}");
			AccountVerifySends.Add(new AccountVerifyCapture(encryptedUsername, encryptedCode, seq));
		}

		protected override void SendTwoFactorVerify(byte[] encryptedCode, uint seq)
		{
			AuthTestTrace.Log("Client", "SendTwoFactorVerify", $"seq={seq} code={AuthTestTrace.Hex(encryptedCode)}");
			server!.OnTwoFactorVerifyReceived(clientId, encryptedCode, seq);
		}

		protected override void Disconnect()
		{
			AuthTestTrace.Log("Client", "Disconnect");
			WasDisconnected = true;
		}

		protected override void OnAuthResultCallback(ClientAuthenticationResult result)
		{
			AuthTestTrace.Log("Client", "OnAuthResultCallback", $"result={result}");
			LastResult = result;
			if (result == ClientAuthenticationResult.LoginSuccess)
				ReceivedSuccess = true;
			AuthResultTcs.TrySetResult(result);
		}

		protected override void OnTwoFactorSetupCallback(string otpauthUri, string[] recoveryCodes)
		{
			AuthTestTrace.Log("Client", "OnTwoFactorSetupCallback", $"uri.len={otpauthUri?.Length ?? 0} codes={recoveryCodes?.Length ?? 0}");
			TwoFactorSetups.Add(new TwoFactorSetupCapture(otpauthUri!, recoveryCodes!));
		}

		protected override bool IsAllowedUsername(string username) =>
			!string.IsNullOrEmpty(username) && username.Length >= 3 && username.Length <= 32;

		protected override bool IsAllowedPassword(string password) =>
			!string.IsNullOrEmpty(password) && password.Length >= 4;

		protected override bool IsAllowedEmailUsername(string email) =>
			!string.IsNullOrEmpty(email) && email.Contains('@');

		#endregion

		#region Capture DTOs

		public readonly struct CreateAccountCapture
		{
			public readonly byte[] EncryptedUsername;
			public readonly byte[] EncryptedEmail;
			public readonly byte[] EncryptedAge;
			public readonly byte[] EncryptedSalt;
			public readonly byte[] EncryptedVerifier;
			public readonly uint Sequence;
			public CreateAccountCapture(byte[] u, byte[] e, byte[] a, byte[] s, byte[] v, uint seq)
			{
				EncryptedUsername = u; EncryptedEmail = e; EncryptedAge = a;
				EncryptedSalt = s; EncryptedVerifier = v; Sequence = seq;
			}
		}

		public readonly struct AccountVerifyCapture
		{
			public readonly byte[] EncryptedUsername;
			public readonly byte[] EncryptedCode;
			public readonly uint Sequence;
			public AccountVerifyCapture(byte[] u, byte[] c, uint seq) { EncryptedUsername = u; EncryptedCode = c; Sequence = seq; }
		}

		public readonly struct TwoFactorSetupCapture
		{
			public readonly string OtpAuthUri;
			public readonly string[] RecoveryCodes;
			public TwoFactorSetupCapture(string uri, string[] codes) { OtpAuthUri = uri; RecoveryCodes = codes; }
		}

		public readonly struct SrpVerifyCapture
		{
			public readonly byte[] EncryptedUsername;
			public readonly byte[] EncryptedClientEphemeral;
			public readonly uint Sequence;
			public SrpVerifyCapture(byte[] u, byte[] e, uint seq) { EncryptedUsername = u; EncryptedClientEphemeral = e; Sequence = seq; }
		}

		public readonly struct SrpProofCapture
		{
			public readonly byte[] EncryptedProof;
			public readonly uint Sequence;
			public SrpProofCapture(byte[] m1, uint seq) { EncryptedProof = m1; Sequence = seq; }
		}

		#endregion
	}
}