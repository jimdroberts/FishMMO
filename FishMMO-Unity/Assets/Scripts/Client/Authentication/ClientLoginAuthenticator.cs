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
	public class ClientLoginAuthenticator : Authenticator
	{
		#region Inner Core

		private sealed class LoginAuthenticatorCore : ClientAuthenticatorCore
		{
			private readonly ClientLoginAuthenticator _outer;

			public LoginAuthenticatorCore(ClientLoginAuthenticator outer) => _outer = outer;

			protected override string LogPrefix => "ClientLoginAuthenticator";

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

			protected override void SendTokenAuth(byte[] encryptedToken, uint seq)
			{
				Client.Broadcast(new TokenAuthBroadcast()
				{
					Token = encryptedToken,
					Seq = seq,
				}, Channel.Reliable);
			}

			protected override void SendSrpVerify(byte[] encryptedUsername, byte[] encryptedClientEphemeral, uint seq)
			{
				Client.Broadcast(new SrpVerifyBroadcast()
				{
					S = encryptedUsername,
					PublicEphemeral = encryptedClientEphemeral,
					Seq = seq,
				}, Channel.Reliable);
			}

			protected override void SendSrpProof(byte[] encryptedProof, uint seq)
			{
				Client.Broadcast(new SrpProofBroadcast()
				{
					Proof = encryptedProof,
					Seq = seq,
				}, Channel.Reliable);
			}

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

			protected override void SendAccountVerify(byte[] encryptedUsername, byte[] encryptedCode, uint seq)
			{
				Client.Broadcast(new AccountVerifyBroadcast()
				{
					Username = encryptedUsername,
					VerifyCode = encryptedCode,
					Seq = seq,
				}, Channel.Reliable);
			}

			protected override void SendTwoFactorVerify(byte[] encryptedCode, uint seq)
			{
				Client.Broadcast(new TwoFactorVerifyBroadcast()
				{
					Code = encryptedCode,
					Seq = seq,
				}, Channel.Reliable);
			}

			protected override void Disconnect() => _outer.Client.ForceDisconnect();

			protected override void OnAuthResultCallback(ClientAuthenticationResult result) =>
				_outer.OnClientAuthenticationResult?.Invoke(result);

			protected override void OnTwoFactorSetupCallback(string otpauthUri, string[] recoveryCodes) =>
				_outer.OnTwoFactorSetupReceived?.Invoke(otpauthUri, recoveryCodes);

			protected override bool IsAllowedUsername(string username) =>
				Authentication.IsAllowedUsername(username);

			protected override bool IsAllowedPassword(string password) =>
				Authentication.IsAllowedPassword(password);

			protected override bool IsAllowedEmailUsername(string email) =>
				Authentication.IsAllowedEmailUsername(email);
		}

		#endregion

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

		private void OnClientServerHandshakeBroadcastReceived(ServerHandshake msg, Channel channel)
		{
			_core.OnServerHandshakeReceived(msg.PublicKey, msg.Cookie, msg.AgreedVersion);
		}

		private void OnClientSrpVerifyBroadcastReceived(SrpVerifyBroadcast msg, Channel channel)
		{
			_core.OnSrpVerifyResponseReceived(msg.S, msg.PublicEphemeral);
		}

		private void OnClientSrpSuccessBroadcastReceived(SrpSuccessBroadcast msg, Channel channel)
		{
			_core.OnSrpSuccessReceived(msg.Proof, msg.Result, msg.Token);
		}

		private void OnClientAuthResultBroadcastReceived(ClientAuthResultBroadcast msg, Channel channel)
		{
			_core.OnAuthResultReceived(msg.Result);
		}

		private void OnClientTwoFactorSetupBroadcastReceived(TwoFactorSetupBroadcast msg, Channel channel)
		{
			_core.OnTwoFactorSetupReceived(msg.OtpauthUri, msg.RecoveryCodes);
		}
	}
}