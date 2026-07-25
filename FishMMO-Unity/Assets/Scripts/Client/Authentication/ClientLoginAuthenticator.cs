using FishNet.Authenticating;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using System;
using System.Collections;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using FishMMO.Auth.Core;
using FishMMO.Shared;
using FishMMO.Auth.Implementation;
using FishMMO.Logging;
using UnityEngine;

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
				// Require full readiness: FishNet.ClientManager.Started AND managerState=Started.
				// Sending when manager is still Starting was logging "sent OK" with zero server payload.
				if (!outer.IsFullyReadyToBroadcast())
				{
					outer.lastHandshakeSendSucceeded = false;
					Log.Warning("ClientLoginAuthenticator",
						$"Send ClientHandshake deferred: fishNetStarted={outer.IsFishNetClientStarted()} " +
						$"managerState={outer.GetManagerState()}. Need both Started.");
					return;
				}
				Log.Info("ClientLoginAuthenticator",
					$"Send ClientHandshake pubKeyLen={publicKey?.Length ?? 0} cookieLen={cookie?.Length ?? 0} " +
					$"tokenLen={connectionToken?.Length ?? 0} gameVersion={gameVersion ?? "?"} " +
					$"managerState={outer.GetManagerState()} (fully ready — Broadcast now)");
				try
				{
					// Client.Broadcast is the static helper on FishMMO.Client.Client.
					Client.Broadcast(new ClientHandshake()
					{
						PublicKey = publicKey,
						Cookie = cookie,
						ConnectionToken = connectionToken,
						MinVersion = minVersion,
						MaxVersion = maxVersion,
						GameVersion = gameVersion,
					}, Channel.Reliable);
					outer.lastHandshakeSendSucceeded = true;
				}
				catch (Exception ex)
				{
					outer.lastHandshakeSendSucceeded = false;
					// Do not rethrow: Client.OnLogMessage used to ForceDisconnect on network-stack
					// exceptions, which produced establish → instant TRANSPORT shutdown.
					Log.Error("ClientLoginAuthenticator", $"ClientHandshake Broadcast failed: {ex.Message}", ex);
				}
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
				if (!outer.IsFullyReadyToBroadcast())
				{
					Log.Error("ClientLoginAuthenticator",
						$"Send CreateAccountBroadcast aborted: fishNetStarted={outer.IsFishNetClientStarted()} " +
						$"managerState={outer.GetManagerState()}");
					return;
				}
				Log.Info("ClientLoginAuthenticator",
					$"Send CreateAccountBroadcast seq={seq} " +
					$"userEnc={encryptedUsername?.Length ?? 0} emailEnc={encryptedEmail?.Length ?? 0} " +
					$"ageEnc={encryptedAge?.Length ?? 0} saltEnc={encryptedSalt?.Length ?? 0} " +
					$"verifierEnc={encryptedVerifier?.Length ?? 0} (register path after ECDH — " +
					"LoginServer must log CreateAccountBroadcast received)");
				try
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
				catch (Exception ex)
				{
					Log.Error("ClientLoginAuthenticator", $"CreateAccountBroadcast failed: {ex.Message}", ex);
				}
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
		/// True after a successful initial <see cref="ClientHandshake"/> broadcast on this connection
		/// while fully ready (FishNet + connection manager both Started).
		/// Cleared on Stopped/Stopping so the next connect can send again.
		/// </summary>
		private bool initialClientHandshakeSent;

		/// <summary>
		/// Set by <see cref="LoginAuthenticatorCore.SendClientHandshake"/> so the outer can
		/// know whether the last send actually went out (vs deferred/aborted).
		/// </summary>
		private bool lastHandshakeSendSucceeded;

		/// <summary>
		/// Connection token held until handshake send succeeds (survives deferred retry).
		/// </summary>
		private string pendingHandshakeConnectionToken;

		/// <summary>
		/// True once any <see cref="ServerHandshake"/> is received this connection.
		/// "sent OK" alone is not progress — server must reply.
		/// </summary>
		private bool serverHandshakeReceived;

		/// <summary>Coroutine waiting for connection-manager Started before first handshake.</summary>
		private Coroutine waitForManagerReadyRoutine;

		/// <summary>Coroutine watching for ServerHandshake after ClientHandshake send.</summary>
		private Coroutine serverHandshakeWatchdogRoutine;

		/// <summary>How long to wait for ClientConnectionManager.ClientState == Started after FishNet starts.</summary>
		private const float ManagerReadyWaitSeconds = 10f;

		/// <summary>How long after ClientHandshake send to wait for ServerHandshake before declaring failure.</summary>
		private const float ServerHandshakeTimeoutSeconds = 12f;

		/// <summary>Resend ClientHandshake once if no ServerHandshake after this many seconds (wire path verified).</summary>
		private const float ClientHandshakeResendAfterSeconds = 2f;

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
		// state. Only proceed if FishNet is still Started.
		if (!IsFishNetClientStarted())
			return;
		// Force a full re-handshake (queue admit path); token already recovered.
		ResetHandshakeSessionState();
		pendingHandshakeConnectionToken = null;
		core.OnDisconnected();
		TrySendInitialClientHandshake("RetryHandshakeAsync");
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
			UnsubscribeConnectionReady();
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
		/// Subscribes to <see cref="ClientConnectionManager.OnConnectionSuccessful"/> so
		/// ClientHandshake can be retried after the connection manager reaches Started
		/// if the first attempt was deferred.
		/// </summary>
		/// <param name="client">The client instance.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void SetClient(Client client)
		{
			UnsubscribeConnectionReady();
			Client = client;
			if (Client?.Connection != null)
				Client.Connection.OnConnectionSuccessful += OnConnectionManagerSuccessful;
		}

		private void UnsubscribeConnectionReady()
		{
			if (Client?.Connection != null)
				Client.Connection.OnConnectionSuccessful -= OnConnectionManagerSuccessful;
		}

		/// <summary>
		/// True when FishNet reports the client transport as Started (broadcasts allowed).
		/// Prefers the cached networkManager from InitializeOnce; falls back to base.NetworkManager.
		/// </summary>
		private bool IsFishNetClientStarted()
		{
			var nm = networkManager ?? base.NetworkManager;
			return nm != null
				&& nm.ClientManager != null
				&& nm.ClientManager.Started;
		}

		/// <summary>
		/// True only when both FishNet and our connection manager report Started.
		/// Sending while managerState is still Starting marks "sent OK" but LoginServer
		/// often sees zero app payload — do not send until both are Started.
		/// </summary>
		private bool IsFullyReadyToBroadcast()
		{
			if (!IsFishNetClientStarted())
				return false;
			// When Client/Connection is wired, require managerState == Started.
			if (Client?.Connection != null
				&& Client.Connection.ClientState != LocalConnectionState.Started)
				return false;
			return true;
		}

		private LocalConnectionState GetManagerState()
		{
			return Client?.Connection?.ClientState ?? LocalConnectionState.Stopped;
		}

		/// <summary>
		/// Capture ConnectionToken into pending so deferred handshake retries keep it.
		/// </summary>
		private void CapturePendingToken()
		{
			if (pendingHandshakeConnectionToken == null && !string.IsNullOrEmpty(ConnectionToken))
			{
				pendingHandshakeConnectionToken = ConnectionToken;
				ConnectionToken = null;
			}
		}

		/// <summary>
		/// Backup path: ClientConnectionManager has applied Started. This is the preferred
		/// send path — managerState is guaranteed Started when this fires.
		/// </summary>
		private void OnConnectionManagerSuccessful()
		{
			CapturePendingToken();
			TrySendInitialClientHandshake("ClientConnection.OnConnectionSuccessful");
		}

		/// <summary>
		/// Sends the initial ClientHandshake once per connection, only when fully ready
		/// (FishNet.ClientManager.Started AND ClientConnectionManager.ClientState == Started).
		/// </summary>
		private void TrySendInitialClientHandshake(string source)
		{
			if (core == null || !initialized) return;
			if (initialClientHandshakeSent)
			{
				Log.Debug("ClientLoginAuthenticator",
					$"Skip ClientHandshake ({source}): already sent this connection");
				return;
			}

			CapturePendingToken();

			bool fishNet = IsFishNetClientStarted();
			var managerState = GetManagerState();
			if (!IsFullyReadyToBroadcast())
			{
				Log.Warning("ClientLoginAuthenticator",
					$"Defer ClientHandshake ({source}): fishNetStarted={fishNet} " +
					$"managerState={managerState} (need both Started — will retry on manager ready)");
				// Poll until manager catches up (covers missing OnConnectionSuccessful race).
				EnsureWaitForManagerReadyRoutine();
				return;
			}

			string token = pendingHandshakeConnectionToken;
			lastHandshakeSendSucceeded = false;

			Log.Info("ClientLoginAuthenticator",
				$"TrySendInitialClientHandshake source={source} tokenLen={token?.Length ?? 0} " +
				$"fishNetStarted=true managerState={managerState} (fully ready)");

			try
			{
				// Clear any partial crypto from a deferred prior attempt, then start clean.
				core.OnDisconnected();
				core.OnConnected(token);
			}
			catch (Exception ex)
			{
				Log.Error("ClientLoginAuthenticator",
					$"OnConnected/handshake failed ({source}): {ex.Message}", ex);
				return;
			}

			if (lastHandshakeSendSucceeded)
			{
				initialClientHandshakeSent = true;
				// Keep pendingHandshakeConnectionToken until ServerHandshake arrives so a
				// 2s resend still has tokenLen>0 (IPFetch token was already consumed once).
				pendingHandshakeConnectionToken = token;
				StopWaitForManagerReadyRoutine();
				Log.Info("ClientLoginAuthenticator",
					$"ClientHandshake Broadcast returned ({source}) managerState=Started " +
					$"tokenLenKept={token?.Length ?? 0} — NOT progress until WIRE SEND OK + ServerHandshake");
				LogWireStats("post-broadcast");
				// Only start watchdog once per connection (resend reuses it).
				if (serverHandshakeWatchdogRoutine == null && !serverHandshakeReceived)
					StartServerHandshakeWatchdog();
			}
			else
			{
				// Keep token for the next ready signal.
				pendingHandshakeConnectionToken = token;
				Log.Warning("ClientLoginAuthenticator",
					$"ClientHandshake not sent ({source}); will retry on next fully-ready signal");
				EnsureWaitForManagerReadyRoutine();
			}
		}

		private void EnsureWaitForManagerReadyRoutine()
		{
			if (waitForManagerReadyRoutine != null) return;
			if (!isActiveAndEnabled) return;
			waitForManagerReadyRoutine = StartCoroutine(WaitForManagerReadyThenHandshake());
		}

		private void StopWaitForManagerReadyRoutine()
		{
			if (waitForManagerReadyRoutine == null) return;
			StopCoroutine(waitForManagerReadyRoutine);
			waitForManagerReadyRoutine = null;
		}

		/// <summary>
		/// Polls until managerState == Started (or timeout/disconnect), then sends handshake once.
		/// </summary>
		private IEnumerator WaitForManagerReadyThenHandshake()
		{
			float deadline = Time.realtimeSinceStartup + ManagerReadyWaitSeconds;
			while (Time.realtimeSinceStartup < deadline)
			{
				if (initialClientHandshakeSent)
				{
					waitForManagerReadyRoutine = null;
					yield break;
				}
				if (!IsFishNetClientStarted())
				{
					Log.Warning("ClientLoginAuthenticator",
						"WaitForManagerReady aborted: FishNet no longer Started");
					waitForManagerReadyRoutine = null;
					yield break;
				}
				if (IsFullyReadyToBroadcast())
				{
					waitForManagerReadyRoutine = null;
					TrySendInitialClientHandshake("WaitForManagerReady.coroutine");
					yield break;
				}
				yield return null;
			}
			waitForManagerReadyRoutine = null;
			Log.Error("ClientLoginAuthenticator",
				$"Timed out after {ManagerReadyWaitSeconds:0}s waiting for managerState=Started " +
				$"(last managerState={GetManagerState()}). ClientHandshake never sent.");
		}

		private void StartServerHandshakeWatchdog()
		{
			serverHandshakeReceived = false;
			if (serverHandshakeWatchdogRoutine != null)
				StopCoroutine(serverHandshakeWatchdogRoutine);
			if (!isActiveAndEnabled) return;
			serverHandshakeWatchdogRoutine = StartCoroutine(ServerHandshakeWatchdog());
		}

		private void StopServerHandshakeWatchdog()
		{
			if (serverHandshakeWatchdogRoutine == null) return;
			StopCoroutine(serverHandshakeWatchdogRoutine);
			serverHandshakeWatchdogRoutine = null;
		}

		/// <summary>
		/// After ClientHandshake is queued, require ServerHandshake within a timeout.
		/// "Broadcast queued" is NOT success — prove ServerHandshake or wire silence.
		/// Optional one resend at ~2s if still no reply.
		/// </summary>
		private IEnumerator ServerHandshakeWatchdog()
		{
			float start = Time.realtimeSinceStartup;
			float deadline = start + ServerHandshakeTimeoutSeconds;
			bool resendAttempted = false;

			while (Time.realtimeSinceStartup < deadline)
			{
				if (serverHandshakeReceived)
				{
					serverHandshakeWatchdogRoutine = null;
					yield break;
				}
				if (!IsFishNetClientStarted())
				{
					serverHandshakeWatchdogRoutine = null;
					yield break;
				}

				// One resend after 2s if no ServerHandshake (only when still fully ready).
				if (!resendAttempted
					&& (Time.realtimeSinceStartup - start) >= ClientHandshakeResendAfterSeconds
					&& IsFullyReadyToBroadcast())
				{
					resendAttempted = true;
					LogWireStats("pre-resend");
					Log.Warning("ClientLoginAuthenticator",
						$"No ServerHandshake after {ClientHandshakeResendAfterSeconds:0.#}s — " +
						"resending ClientHandshake once (wire path re-prove)");
					// Allow TrySend to run again with same connection.
					initialClientHandshakeSent = false;
					lastHandshakeSendSucceeded = false;
					TrySendInitialClientHandshake("ServerHandshakeWatchdog.resend");
				}

				yield return null;
			}
			serverHandshakeWatchdogRoutine = null;
			if (serverHandshakeReceived) yield break;

			LogWireStats("timeout");
			Log.Error("ClientLoginAuthenticator",
				$"NO ServerHandshake within {ServerHandshakeTimeoutSeconds:0}s after ClientHandshake. " +
				"Wire send failed or transport silent (not server busy). " +
				"Check [FishWT] WIRE SEND OK / wireSentOk; LoginServer for FIRST_APP_PAYLOAD. " +
				"CreateAccount cannot run without ServerHandshake.");

			// Do NOT map to ServerBusy — that misleads the player. Unlock via disconnect.
			try
			{
				Client?.ForceDisconnect();
			}
			catch (Exception ex)
			{
				Log.Warning("ClientLoginAuthenticator",
					$"ForceDisconnect after handshake timeout failed: {ex.Message}");
			}
		}

		/// <summary>
		/// Log FishNet→WebTransport wire counters so we can see Broadcast queued vs WT send.
		/// </summary>
		private void LogWireStats(string tag)
		{
			try
			{
				var nm = networkManager ?? base.NetworkManager;
				var wt = nm?.TransportManager?.Transport as FishNet.Transporting.WebTransport.WebTransport;
				if (wt == null && nm?.TransportManager?.Transport is FishNet.Transporting.Multipass.Multipass mp)
					wt = mp.ClientTransport as FishNet.Transporting.WebTransport.WebTransport;

				if (wt == null)
				{
					Log.Warning("ClientLoginAuthenticator",
						$"WireStats[{tag}]: WebTransport client transport not found");
					return;
				}
				wt.GetClientWireStats(out long queued, out long sentOk, out long sentFail,
					out long sentBytes, out long dropNotStarted);
				Log.Info("ClientLoginAuthenticator",
					$"WireStats[{tag}]: queued={queued} wireSentOk={sentOk} wireSentFail={sentFail} " +
					$"wireBytes={sentBytes} dropNotStarted={dropNotStarted} " +
					$"(if queued>0 && wireSentOk==0 → FishNet→WT glue bug)");
				UnityEngine.Debug.Log(
					$"[FishWT] WireStats[{tag}] queued={queued} sentOk={sentOk} sentFail={sentFail} " +
					$"bytes={sentBytes} dropNotStarted={dropNotStarted}");
			}
			catch (Exception ex)
			{
				Log.Warning("ClientLoginAuthenticator", $"WireStats[{tag}] failed: {ex.Message}");
			}
		}

		private void ResetHandshakeSessionState()
		{
			initialClientHandshakeSent = false;
			lastHandshakeSendSucceeded = false;
			serverHandshakeReceived = false;
			StopWaitForManagerReadyRoutine();
			StopServerHandshakeWatchdog();
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
		/// Clears login/register credentials (username/password/email/register flag).
		/// Safe after a terminal auth UI result or user cancel; do not call between
		/// SetLoginCredentials and post-ECDH CreateAccount/SRP send.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void ClearCredentials()
		{
			core?.ClearCredentials();
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
		/// <c>Started</c> → send ClientHandshake (or schedule retry via connection-manager ready).
		/// <c>Stopping</c>/<c>Stopped</c> → clear per-connection crypto; credentials kept.
		/// </summary>
		private void ClientManager_OnClientConnectionState(ClientConnectionStateArgs args)
		{
			Log.Info("ClientLoginAuthenticator",
				$"ConnectionState={args.ConnectionState} fishNetStarted={IsFishNetClientStarted()} " +
				$"managerState={GetManagerState()} handshakeSent={initialClientHandshakeSent} " +
				$"serverHandshakeReceived={serverHandshakeReceived}");

			if (args.ConnectionState == LocalConnectionState.Stopping ||
				args.ConnectionState == LocalConnectionState.Stopped)
			{
				// Crypto only — credentials intentionally survive for create-account race.
				core.OnDisconnected();
				ResetHandshakeSessionState();
				// Drop pending token on full stop so a stale IPFetch token is not reused.
				if (args.ConnectionState == LocalConnectionState.Stopped)
					pendingHandshakeConnectionToken = null;
			}
			else if (args.ConnectionState == LocalConnectionState.Started)
			{
				CapturePendingToken();
				// Do NOT force-send here if managerState is still Starting — that was the
				// "Broadcast logged OK / zero server payload" bug. Fully-ready gate defers
				// until ClientConnection.OnConnectionSuccessful or the wait coroutine.
				TrySendInitialClientHandshake("FishNet.OnClientConnectionState.Started");
			}
		}

		// ── Incoming broadcast routers ────────────────────────────────────────

		/// <summary>
		/// Handles the server's handshake response (server public key, cookie echo, negotiated protocol
		/// version) and forwards it to the core to derive the shared encryption key.
		/// </summary>
		private void OnClientServerHandshakeBroadcastReceived(ServerHandshake msg, Channel channel)
		{
			// Phase 1 cookie challenge: PublicKey null/empty + Cookie set, agreedVersion often 0.
			// Phase 2 ECDH complete: PublicKey 32 bytes, agreedVersion negotiated.
			// Both are intentional — not mis-routed frames.
			bool isCookieChallenge = msg.PublicKey == null || msg.PublicKey.Length == 0;
			if (isCookieChallenge)
			{
				// Do not stop the ServerHandshake watchdog yet — wait for phase-2 ECDH.
				Log.Info("ClientLoginAuthenticator",
					$"ServerHandshake PHASE1 cookie-challenge pubKeyLen=0 " +
					$"cookieLen={msg.Cookie?.Length ?? 0} agreedVersion={msg.AgreedVersion} " +
					"(expected — client will echo cookie; ECDH on next ServerHandshake)");
			}
			else
			{
				serverHandshakeReceived = true;
				// Token no longer needed for resend once server completed ECDH.
				pendingHandshakeConnectionToken = null;
				StopServerHandshakeWatchdog();
				LogWireStats("server-handshake-received");
				Log.Info("ClientLoginAuthenticator",
					$"ServerHandshake PHASE2 ECDH pubKeyLen={msg.PublicKey?.Length ?? 0} " +
					$"cookieLen={msg.Cookie?.Length ?? 0} agreedVersion={msg.AgreedVersion} " +
					"(app payload path confirmed — session keys / CreateAccount / SRP can proceed)");
			}
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
			core.TryApplyRenewedToken(msg.Token);
		}
	}
}