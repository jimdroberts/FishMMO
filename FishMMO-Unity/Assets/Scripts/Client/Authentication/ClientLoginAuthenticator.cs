using FishNet.Authenticating;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using System;
using System.Runtime.CompilerServices;
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
		/// Concrete <see cref="ClientAuthenticatorCore"/> implementation that bridges the engine-independent
		/// authentication state machine to FishNet broadcasts and the FishMMO <see cref="FishMMO.Client.Client"/>.
		/// All abstract send hooks marshal payloads into the matching broadcast types.
		/// </summary>
		private sealed class LoginAuthenticatorCore : ClientAuthenticatorCore
		{
			/// <summary>
			/// Owning <see cref="ClientLoginAuthenticator"/>, used to access the FishNet client and to raise
			/// outer events (auth result, two-factor setup) from core callbacks.
			/// </summary>
			private readonly ClientLoginAuthenticator _outer;

			/// <summary>
			/// Creates a new core bound to the given outer authenticator.
			/// </summary>
			/// <param name="outer">The owning <see cref="ClientLoginAuthenticator"/> MonoBehaviour.</param>
			public LoginAuthenticatorCore(ClientLoginAuthenticator outer) => _outer = outer;

			/// <summary>
			/// Log prefix used by base-class diagnostics.
			/// </summary>
			protected override string LogPrefix => "ClientLoginAuthenticator";

			/// <summary>
			/// Sends the initial client handshake (ephemeral public key, cookie, supported protocol version range)
			/// to the server as a <see cref="ClientHandshake"/> broadcast.
			/// </summary>
			protected override void SendClientHandshake(byte[] publicKey, byte[] cookie, ushort minVersion, ushort maxVersion)
			{
				Client.Broadcast(new ClientHandshake()
				{
					PublicKey = publicKey,
					Cookie = cookie,
					MinVersion = minVersion,
					MaxVersion = maxVersion,
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
			/// <see cref="SrpVerifyBroadcast"/>.
			/// </summary>
			protected override void SendSrpVerify(byte[] encryptedUsername, byte[] encryptedClientEphemeral, uint seq)
			{
				Client.Broadcast(new SrpVerifyBroadcast()
				{
					S = encryptedUsername,
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
			protected override void Disconnect() => _outer.Client.ForceDisconnect();

			/// <summary>
			/// Raises the outer <see cref="ClientLoginAuthenticator.OnClientAuthenticationResult"/> event.
			/// </summary>
			protected override void OnAuthResultCallback(ClientAuthenticationResult result) =>
				_outer.OnClientAuthenticationResult?.Invoke(result);

			/// <summary>
			/// Raises the outer <see cref="ClientLoginAuthenticator.OnTwoFactorSetupReceived"/> event with
			/// the otpauth URI and recovery codes returned by the server after registration.
			/// </summary>
			protected override void OnTwoFactorSetupCallback(string otpauthUri, string[] recoveryCodes) =>
				_outer.OnTwoFactorSetupReceived?.Invoke(otpauthUri, recoveryCodes);

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
		private LoginAuthenticatorCore _core;

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
		/// </summary>
		public string PendingLoginIdentifier => _core?.PendingLoginIdentifier;

		/// <summary>
		/// Returns whether the client has a stored authentication token for World/Scene server connections.
		/// </summary>
		public bool HasAuthToken => _core?.HasAuthToken ?? false;

		/// <summary>
		/// Initializes the authenticator once with the provided network manager.
		/// Registers connection state and broadcast handlers.
		/// </summary>
		/// <param name="networkManager">The network manager instance.</param>
		public override void InitializeOnce(NetworkManager networkManager)
		{
			base.InitializeOnce(networkManager);
			_core = new LoginAuthenticatorCore(this);

			base.NetworkManager.ClientManager.OnClientConnectionState += ClientManager_OnClientConnectionState;
			base.NetworkManager.ClientManager.RegisterBroadcast<ServerHandshake>(OnClientServerHandshakeBroadcastReceived);
			base.NetworkManager.ClientManager.RegisterBroadcast<SrpVerifyBroadcast>(OnClientSrpVerifyBroadcastReceived);
			base.NetworkManager.ClientManager.RegisterBroadcast<SrpSuccessBroadcast>(OnClientSrpSuccessBroadcastReceived);
			base.NetworkManager.ClientManager.RegisterBroadcast<ClientAuthResultBroadcast>(OnClientAuthResultBroadcastReceived);
			base.NetworkManager.ClientManager.RegisterBroadcast<TwoFactorSetupBroadcast>(OnClientTwoFactorSetupBroadcastReceived);
		}

		/// <summary>
		/// Unity lifecycle: disposes the core and zeroes all key material when this MonoBehaviour is destroyed.
		/// </summary>
		private void OnDestroy()
		{
			_core?.Dispose();
			_core = null;
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
			return _core.SetLoginCredentials(username, password, register, email, age);
		}

		/// <summary>
		/// Encrypts and sends an account verification code to the server on the current connection.
		/// </summary>
		/// <param name="username">The account username to verify.</param>
		/// <param name="verifyCode">The verification code entered by the user.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void SendVerifyCode(string username, string verifyCode)
		{
			_core.SendVerifyCode(username, verifyCode);
		}

		/// <summary>
		/// Encrypts and sends a TOTP code to the server for two-factor verification during login.
		/// </summary>
		/// <param name="code">The 6-digit TOTP code from the authenticator app.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void SendTotpCode(string code)
		{
			_core.SendTotpCode(code);
		}

		/// <summary>
		/// Clears the stored authentication token and zeroes its memory.
		/// Call on explicit logout or when the token is no longer needed.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void ClearAuthToken()
		{
			_core?.ClearAuthToken();
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
				_core.OnDisconnected();
			}
			else if (args.ConnectionState == LocalConnectionState.Started)
			{
				_core.OnConnected();
			}
		}

		// ── Incoming broadcast routers ────────────────────────────────────────

		/// <summary>
		/// Handles the server's handshake response (server public key, cookie echo, negotiated protocol
		/// version) and forwards it to the core to derive the shared encryption key.
		/// </summary>
		private void OnClientServerHandshakeBroadcastReceived(ServerHandshake msg, Channel channel)
		{
			_core.OnServerHandshakeReceived(msg.PublicKey, msg.Cookie, msg.AgreedVersion);
		}

		/// <summary>
		/// Handles the server's SRP-6a identification response (salt s and server ephemeral B) and
		/// forwards it to the core, which computes M1 and triggers <see cref="LoginAuthenticatorCore.SendSrpProof"/>.
		/// </summary>
		private void OnClientSrpVerifyBroadcastReceived(SrpVerifyBroadcast msg, Channel channel)
		{
			_core.OnSrpVerifyResponseReceived(msg.S, msg.PublicEphemeral);
		}

		/// <summary>
		/// Handles the server's SRP success message (server proof M2, auth result, optional reconnect token)
		/// and forwards it to the core for verification and token storage.
		/// </summary>
		private void OnClientSrpSuccessBroadcastReceived(SrpSuccessBroadcast msg, Channel channel)
		{
			_core.OnSrpSuccessReceived(msg.Proof, msg.Result, msg.Token);
		}

		/// <summary>
		/// Handles a generic authentication result broadcast (e.g., banned, version mismatch, server full)
		/// and forwards it to the core.
		/// </summary>
		private void OnClientAuthResultBroadcastReceived(ClientAuthResultBroadcast msg, Channel channel)
		{
			_core.OnAuthResultReceived(msg.Result);
		}

		/// <summary>
		/// Handles the post-registration 2FA setup broadcast (otpauth URI and recovery codes) and forwards
		/// it to the core, which raises <see cref="OnTwoFactorSetupReceived"/>.
		/// </summary>
		private void OnClientTwoFactorSetupBroadcastReceived(TwoFactorSetupBroadcast msg, Channel channel)
		{
			_core.OnTwoFactorSetupReceived(msg.OtpauthUri, msg.RecoveryCodes);
		}
	}
}