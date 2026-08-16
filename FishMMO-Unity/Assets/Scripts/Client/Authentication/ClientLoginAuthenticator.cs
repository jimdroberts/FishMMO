using FishNet.Authenticating;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using System;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using FishMMO.Auth.Core;
using FishMMO.Shared;
using FishMMO.Auth.Implementation;

namespace FishMMO.Client
{
	/// <summary>
	/// FishNet client-side authenticator MonoBehaviour for the FishMMO login flow.
	/// Thin adapter over the engine-independent <see cref="ClientAuthenticatorCore"/> state machine:
	/// translates abstract send/disconnect/result callbacks into FishNet broadcasts and connection control,
	/// and routes incoming FishNet broadcasts back into the core.
	/// </summary>
	public class ClientLoginAuthenticator : Authenticator
	{
		#region Inner Core

		/// <summary>
		/// Concrete <see cref="ClientAuthenticatorCore"/> implementation that bridges
		/// the engine-independent authentication state machine (defined in
		/// FishMMO-Auth/FishMMO-ClientAuth) to FishNet broadcasts and the FishMMO
		/// <see cref="FishMMO.Client.Client"/>.
		/// See the FishMMO-Auth project for the full authentication state machine.
		/// </summary>
		private sealed class LoginAuthenticatorCore : ClientAuthenticatorCore
		{
			/// <summary>
			/// Owning <see cref="ClientLoginAuthenticator"/>, used to access the FishNet client and to raise
			/// outer events (auth result, two-factor setup) from core callbacks.
			/// </summary>
			private readonly ClientLoginAuthenticator outer;

			/// <summary>
			/// Creates a new core bound to the given outer authenticator.
			/// </summary>
			/// <param name="outer">The owning <see cref="ClientLoginAuthenticator"/> MonoBehaviour.</param>
			public LoginAuthenticatorCore(ClientLoginAuthenticator outer) => this.outer = outer;

			/// <summary>
			/// Log prefix used by base-class diagnostics.
			/// </summary>
			protected override string LogPrefix => "ClientLoginAuthenticator";

			/// <summary>
			/// Sends the initial client handshake (ephemeral public key, cookie, connection token,
			/// supported protocol version range) to the server as a <see cref="ClientHandshake"/> broadcast.
			/// </summary>
			protected override void SendClientHandshake(byte[] publicKey, byte[] cookie, string connectionToken, ushort minVersion, ushort maxVersion, string gameVersion)
			{
				Client.Broadcast(new ClientHandshake()
				{
					PublicKey = publicKey,
					Cookie = cookie,
					ConnectionToken = connectionToken,
					MinVersion = minVersion,
					MaxVersion = maxVersion,
					GameVersion = gameVersion,
				}, Channel.Reliable);
			}

			/// <summary>
			/// Sends an encrypted authentication token (for World/Scene server reconnects) to the server
			/// as a <see cref="TokenAuthBroadcast"/>.
			/// </summary>
			protected override void SendTokenAuth(byte[] encryptedToken, uint seq)
			{
				Client.Broadcast(new TokenAuthBroadcast()
				{
					Token = encryptedToken,
					Seq = seq,
				}, Channel.Reliable);
			}

			/// <summary>
			/// Sends the SRP-6a identification step (encrypted username + client ephemeral A) as a
			/// <see cref="SrpVerifyRequestBroadcast"/>.
			/// </summary>
			protected override void SendSrpVerify(byte[] encryptedUsername, byte[] encryptedClientEphemeral, uint seq)
			{
				Client.Broadcast(new SrpVerifyRequestBroadcast()
				{
					Username = encryptedUsername,
					PublicEphemeral = encryptedClientEphemeral,
					Seq = seq,
				}, Channel.Reliable);
			}

			/// <summary>
			/// Sends the SRP-6a client proof M1 (encrypted) as a <see cref="SrpProofBroadcast"/>.
			/// </summary>
			protected override void SendSrpProof(byte[] encryptedProof, uint seq)
			{
				Client.Broadcast(new SrpProofBroadcast()
				{
					Proof = encryptedProof,
					Seq = seq,
				}, Channel.Reliable);
			}

			/// <summary>
			/// Sends a new-account registration request (all fields encrypted under the handshake key)
			/// as a <see cref="CreateAccountBroadcast"/>.
			/// </summary>
			protected override void SendCreateAccount(
				byte[] encryptedUsername, byte[] encryptedEmail, byte[] encryptedAge,
				byte[] encryptedSalt, byte[] encryptedVerifier, uint seq)
			{
				Client.Broadcast(new CreateAccountBroadcast()
				{
					Username = encryptedUsername,
					Email = encryptedEmail,
					Age = encryptedAge,
					Salt = encryptedSalt,
					Verifier = encryptedVerifier,
					Seq = seq,
				}, Channel.Reliable);
			}

			/// <summary>
			/// Sends an account email-verification code (encrypted) as an <see cref="AccountVerifyBroadcast"/>.
			/// </summary>
			protected override void SendAccountVerify(byte[] encryptedUsername, byte[] encryptedCode, uint seq)
			{
				Client.Broadcast(new AccountVerifyBroadcast()
				{
					Username = encryptedUsername,
					VerifyCode = encryptedCode,
					Seq = seq,
				}, Channel.Reliable);
			}

			/// <summary>
			/// Sends a TOTP/2FA code (encrypted) for the in-progress login as a
			/// <see cref="TwoFactorVerifyBroadcast"/>.
			/// </summary>
			protected override void SendTwoFactorVerify(byte[] encryptedCode, uint seq)
			{
				Client.Broadcast(new TwoFactorVerifyBroadcast()
				{
					Code = encryptedCode,
					Seq = seq,
				}, Channel.Reliable);
			}

			/// <summary>
			/// Forces the underlying FishNet client connection to disconnect (used on fatal auth errors).
			/// </summary>
			protected override void Disconnect() => outer.Client.ForceDisconnect();

			/// <summary>
			/// Raises the outer <see cref="ClientLoginAuthenticator.OnClientAuthenticationResult"/> event.
			/// </summary>
			protected override void OnAuthResultCallback(ClientAuthenticationResult result) =>
				outer.OnClientAuthenticationResult?.Invoke(result);

			/// <summary>
			/// Raises the outer <see cref="ClientLoginAuthenticator.OnTwoFactorSetupReceived"/> event with
			/// the otpauth URI and recovery codes returned by the server after registration.
			/// </summary>
			protected override void OnTwoFactorSetupCallback(string otpauthUri, string[] recoveryCodes) =>
				outer.OnTwoFactorSetupReceived?.Invoke(otpauthUri, recoveryCodes);

			/// <summary>
			/// Validates a username against FishMMO's shared character/format rules.
			/// </summary>
			protected override bool IsAllowedUsername(string username) =>
				Authentication.IsAllowedUsername(username);

			/// <summary>
			/// Validates a password against FishMMO's shared length/character rules.
			/// </summary>
			protected override bool IsAllowedPassword(string password) =>
				Authentication.IsAllowedPassword(password);

			/// <summary>
			/// Validates an email address against FishMMO's shared format rules.
			/// </summary>
			protected override bool IsAllowedEmailUsername(string email) =>
				Authentication.IsAllowedEmailUsername(email);
		}

		#endregion

		/// <summary>
		/// Engine-independent authentication state machine instance. Created in <see cref="InitializeOnce"/>
		/// and disposed in <see cref="OnDestroy"/>; null before initialization and after teardown.
		/// </summary>
		private LoginAuthenticatorCore core;

		/// <summary>
		/// Stored reference to the NetworkManager for safe unregistration in <see cref="OnDestroy"/>,
		/// since <c>base.NetworkManager</c> may not be accessible after the object begins destruction.
		/// </summary>
		private NetworkManager networkManager;

		/// <summary>
		/// Tracks whether <see cref="InitializeOnce"/> completed successfully, so that
		/// <see cref="OnDestroy"/> only attempts to unregister handlers when registration occurred.
		/// </summary>
		private bool initialized;

		/// <summary>
		/// Client authentication event. Subscribe to receive authentication results from the server.
		/// </summary>
		public event Action<ClientAuthenticationResult> OnClientAuthenticationResult;

		/// <summary>
		/// Fired when the server sends 2FA setup data after account creation.
		/// Parameters: otpauth URI (for authenticator app), recovery codes array.
		/// </summary>
		public event Action<string, string[]> OnTwoFactorSetupReceived;

		/// <summary>
		/// Overridden authentication result event (not used on client).
		/// </summary>
#pragma warning disable CS0067
		public override event Action<NetworkConnection, bool> OnAuthenticationResult;
#pragma warning restore CS0067

		/// <summary>
		/// Reference to the client instance for broadcasting messages.
		/// </summary>
		public Client Client { get; private set; }

		/// <summary>
		/// Returns the current login identifier (username or email) if still set.
		/// Returns null after credentials are cleared (post-SRP proof or disconnect).
		/// Note: .NET strings are immutable and cannot be zeroed, so the identifier
		/// remains in managed memory until garbage collected.
		/// </summary>
		public string PendingLoginIdentifier => core?.PendingLoginIdentifier;

		/// <summary>
		/// One-time connection token from IPFetch for real-IP recovery on the Login Server.
		/// Set before ConnectToServer; null for World/Scene reconnections.
		/// </summary>
		public string ConnectionToken { get; set; }

		/// <summary>
		/// Returns whether the client has a stored authentication token for World/Scene server connections.
		/// </summary>
		public bool HasAuthToken => core?.HasAuthToken ?? false;

	/// <summary>
	/// Resets the authentication state and re-initiates the handshake on the
	/// current connection after a randomized jitter delay (0-1s). The jitter
	/// prevents all admitted queue clients from sending handshakes simultaneously,
	/// which would create a thundering herd at the server's SRP verify channel.
	/// </summary>
	/// <remarks>
	/// The connection token from IPFetch is NOT re-sent -- the real IP was already
	/// recovered by the initial handshake that triggered queueing.
	/// </remarks>
	public async System.Threading.Tasks.Task RetryHandshakeAsync()
	{
		if (core == null || !initialized) return;
		// Spread re-handshake attempts across ~1 second to prevent all admitted
		// clients from hitting the SRP verify channel at the same instant.
		int jitterMs = UnityEngine.Random.Range(0, 1000);
		if (jitterMs > 0)
			await System.Threading.Tasks.Task.Delay(jitterMs);
		// Guard: between the jitter delay and the OnConnected/OnDisconnected calls,
		// the transport may have fired connection state callbacks (e.g., disconnect
		// due to timeout, or the server dropped us). If we call OnConnected on a
		// stopped connection, the core state machine will be in an inconsistent
		// state. Only proceed if the connection is still valid.
		if (Client == null || Client.Connection == null ||
			Client.Connection.ClientState == LocalConnectionState.Stopped)
			return;
		core.OnDisconnected();
		core.OnConnected(connectionToken: null);
	}

		/// <summary>
		/// Initializes the authenticator once with the provided network manager.
		/// Registers connection state and broadcast handlers.
		/// </summary>
		/// <param name="networkManager">The network manager instance.</param>
		public override void InitializeOnce(NetworkManager networkManager)
		{
			if (initialized) return;
			base.InitializeOnce(networkManager);
			core = new LoginAuthenticatorCore(this);
			core.SetGameVersion(MainBootstrapSystem.GameVersion ?? "");
			this.networkManager = networkManager;

			base.NetworkManager.ClientManager.OnClientConnectionState += ClientManager_OnClientConnectionState;
			base.NetworkManager.ClientManager.RegisterBroadcast<ServerHandshake>(OnClientServerHandshakeBroadcastReceived);
			base.NetworkManager.ClientManager.RegisterBroadcast<SrpVerifyResponseBroadcast>(OnClientSrpVerifyBroadcastReceived);
			base.NetworkManager.ClientManager.RegisterBroadcast<SrpSuccessBroadcast>(OnClientSrpSuccessBroadcastReceived);
			base.NetworkManager.ClientManager.RegisterBroadcast<ClientAuthResultBroadcast>(OnClientAuthResultBroadcastReceived);
			base.NetworkManager.ClientManager.RegisterBroadcast<TwoFactorSetupBroadcast>(OnClientTwoFactorSetupBroadcastReceived);
			base.NetworkManager.ClientManager.RegisterBroadcast<RenewTokenResponseBroadcast>(OnClientRenewTokenResponseBroadcastReceived);
			initialized = true;
		}

		/// <summary>
		/// Unity lifecycle: disposes the core and zeroes all key material when this MonoBehaviour is destroyed.
		/// </summary>
		private void OnDestroy()
		{
			if (initialized && networkManager != null && networkManager.ClientManager != null)
			{
				networkManager.ClientManager.OnClientConnectionState -= ClientManager_OnClientConnectionState;
				networkManager.ClientManager.UnregisterBroadcast<ServerHandshake>(OnClientServerHandshakeBroadcastReceived);
				networkManager.ClientManager.UnregisterBroadcast<SrpVerifyResponseBroadcast>(OnClientSrpVerifyBroadcastReceived);
				networkManager.ClientManager.UnregisterBroadcast<SrpSuccessBroadcast>(OnClientSrpSuccessBroadcastReceived);
				networkManager.ClientManager.UnregisterBroadcast<ClientAuthResultBroadcast>(OnClientAuthResultBroadcastReceived);
				networkManager.ClientManager.UnregisterBroadcast<TwoFactorSetupBroadcast>(OnClientTwoFactorSetupBroadcastReceived);
				networkManager.ClientManager.UnregisterBroadcast<RenewTokenResponseBroadcast>(OnClientRenewTokenResponseBroadcastReceived);
			}
			core?.Dispose();
			core = null;
		}

		/// <summary>
		/// Sets the client instance for broadcasting messages.
		/// </summary>
		/// <param name="client">The client instance.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void SetClient(Client client)
		{
			Client = client;
		}

		/// <summary>
		/// Sets the login credentials for authentication or registration.
		/// Returns false if the basic credential format is invalid.
		/// </summary>
		/// <param name="username">The username.</param>
		/// <param name="password">The password.</param>
		/// <param name="register">True to register a new account; false to login.</param>
		/// <param name="email">The email address for multi-factor identification.</param>
		/// <param name="age">The age for multi-factor identification.</param>
		/// <returns><c>true</c> if credentials were accepted; <c>false</c> if rejected.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool SetLoginCredentials(string username, string password, bool register = false, string email = "", int age = 0)
		{
			return core.SetLoginCredentials(username, password, register, email, age);
		}

		/// <summary>
		/// Encrypts and sends an account verification code to the server on the current connection.
		/// </summary>
		/// <param name="username">The account username to verify.</param>
		/// <param name="verifyCode">The verification code entered by the user.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void SendVerifyCode(string username, string verifyCode)
		{
			core.SendVerifyCode(username, verifyCode);
		}

		/// <summary>
		/// Encrypts and sends a TOTP code to the server for two-factor verification during login.
		/// </summary>
		/// <param name="code">The 6-digit TOTP code from the authenticator app.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void SendTotpCode(string code)
		{
			core.SendTotpCode(code);
		}

		/// <summary>
		/// Clears the stored authentication token and zeroes its memory.
		/// Call on explicit logout or when the token is no longer needed.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void ClearAuthToken()
		{
			core?.ClearAuthToken();
		}

		/// <summary>
		/// Best-effort server-side token revocation: if a token is currently stored and
		/// the client connection is active, sends a <see cref="RevokeTokenBroadcast"/>
		/// to the LoginServer with the raw token bytes. The server will hash and revoke
		/// it via <c>IAuthTokenService</c>. Either way, the local copy is zeroed
		/// immediately so the token cannot be reused by this process.
		///
		/// Safe to call when there is no active connection — the broadcast is simply
		/// dropped by FishNet and the local token is still cleared.
		/// </summary>
		public async System.Threading.Tasks.Task RevokeAndClearAuthToken()
		{
			if (core == null) return;
			if (!core.TryConsumeStoredTokenForRevoke(out byte[] tokenCopy) || tokenCopy == null)
			{
				return;
			}
			try
			{
				if (Client != null)
				{
					// Bounded retry: a single FishNet broadcast failure (e.g. transport
					// state churn during logout) shouldn't silently lose the revocation.
					// We deliberately keep the loop tight (no awaitable delay) because
					// this is fire-and-forget and called on the UI thread.
					const int MaxRevokeAttempts = 3;
					Exception lastEx = null;
					for (int attempt = 0; attempt < MaxRevokeAttempts; attempt++)
					{
						try
						{
							Client.Broadcast(new RevokeTokenBroadcast { Token = tokenCopy }, Channel.Reliable);
							lastEx = null;
							break;
						}
						catch (Exception ex)
						{
							lastEx = ex;
							if (attempt < MaxRevokeAttempts - 1)
								await System.Threading.Tasks.Task.Delay(100);
						}
					}
					if (lastEx != null)
					{
						_ = FishMMO.Logging.Log.Warning("ClientLoginAuthenticator", $"RevokeTokenBroadcast send failed after {MaxRevokeAttempts} attempts: {lastEx.Message}");
					}
				}
			}
			catch (Exception ex)
			{
				_ = FishMMO.Logging.Log.Warning("ClientLoginAuthenticator", $"RevokeTokenBroadcast send failed: {ex.Message}");
			}
			finally
			{
				// Always zero the local copy — server-side revocation may have
				// missed delivery, but this process must not retain the bytes.
				// CryptographicOperations.ZeroMemory prevents the JIT from eliding
				// the write (Array.Clear can be optimized away as dead).
				if (tokenCopy != null)
				{
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP3_0_OR_GREATER
					System.Security.Cryptography.CryptographicOperations.ZeroMemory(tokenCopy);
#else
					Array.Clear(tokenCopy, 0, tokenCopy.Length);
#endif
				}
			}
		}

		// ── Connection lifecycle ──────────────────────────────────────────────

		/// <summary>
		/// FishNet callback invoked when the local client connection state changes.
		/// Forwards <c>Started</c> to <see cref="ClientAuthenticatorCore.OnConnected"/> and
		/// <c>Stopping</c>/<c>Stopped</c> to <see cref="ClientAuthenticatorCore.OnDisconnected"/>.
		/// </summary>
		private void ClientManager_OnClientConnectionState(ClientConnectionStateArgs args)
		{
			if (args.ConnectionState == LocalConnectionState.Stopping ||
				args.ConnectionState == LocalConnectionState.Stopped)
			{
				core.OnDisconnected();
			}
			else if (args.ConnectionState == LocalConnectionState.Started)
			{
				core.OnConnected(ConnectionToken); ConnectionToken = null;
			}
		}

		// ── Incoming broadcast routers ────────────────────────────────────────

		/// <summary>
		/// Handles the server's handshake response (server public key, cookie echo, negotiated protocol
		/// version) and forwards it to the core to derive the shared encryption key.
		/// </summary>
		private void OnClientServerHandshakeBroadcastReceived(ServerHandshake msg, Channel channel)
		{
			core.OnServerHandshakeReceived(msg.PublicKey, msg.Cookie, msg.AgreedVersion);
		}

		/// <summary>
		/// Handles the server's SRP-6a identification response (salt s and server ephemeral B) and
		/// forwards it to the core, which computes M1 and triggers <see cref="LoginAuthenticatorCore.SendSrpProof"/>.
		/// </summary>
		private void OnClientSrpVerifyBroadcastReceived(SrpVerifyResponseBroadcast msg, Channel channel)
		{
			core.OnSrpVerifyResponseReceived(msg.Salt, msg.PublicEphemeral);
		}

		/// <summary>
		/// Handles the server's SRP success message (server proof M2, auth result, optional reconnect token)
		/// and forwards it to the core for verification and token storage.
		/// </summary>
		private void OnClientSrpSuccessBroadcastReceived(SrpSuccessBroadcast msg, Channel channel)
		{
			core.OnSrpSuccessReceived(msg.Proof, msg.Result, msg.Token);
		}

		/// <summary>
		/// Handles a generic authentication result broadcast (e.g., banned, version mismatch, server full)
		/// and forwards it to the core.
		/// </summary>
		private void OnClientAuthResultBroadcastReceived(ClientAuthResultBroadcast msg, Channel channel)
		{
			core.OnAuthResultReceived(msg.Result);
		}

		/// <summary>
		/// Handles the post-registration 2FA setup broadcast (otpauth URI and recovery codes) and forwards
		/// it to the core, which raises <see cref="OnTwoFactorSetupReceived"/>.
		/// </summary>
		private void OnClientTwoFactorSetupBroadcastReceived(TwoFactorSetupBroadcast msg, Channel channel)
		{
			core.OnTwoFactorSetupReceived(msg.OtpauthUri, msg.RecoveryCodes);
		}

		/// <summary>
		/// Handles a server-pushed renewed auth token sent immediately after a successful
		/// <see cref="TokenAuthBroadcast"/> authentication on a World or Scene server.
		/// Decrypts the token over the existing AES-GCM session channel via the core and
		/// replaces the stored token used for future reconnect attempts.
		/// </summary>
		private void OnClientRenewTokenResponseBroadcastReceived(RenewTokenResponseBroadcast msg, Channel channel)
		{
			if (core == null) return;

			// The server only broadcasts on success, but it now says so explicitly. Applying a
			// payload the server did not vouch for would consume a receive sequence number the
			// server never spent, permanently desynchronising the AES-GCM channel and breaking
			// every later renewal on this connection.
			if (msg.Result != ClientAuthenticationResult.LoginSuccess)
			{
				_ = FishMMO.Logging.Log.Warning("ClientLoginAuthenticator",
					$"Ignoring token renewal with result {msg.Result}; keeping the existing token.");
				return;
			}

			if (!core.TryApplyRenewedToken(msg.Token))
			{
				// Not fatal on its own — the current token is still valid until it expires —
				// but it means renewals have stopped working, so surface it rather than
				// letting the session drift silently toward an expired token.
				_ = FishMMO.Logging.Log.Warning("ClientLoginAuthenticator",
					"Failed to apply a renewed auth token; the session will expire when the current token does.");
			}
		}
	}
}