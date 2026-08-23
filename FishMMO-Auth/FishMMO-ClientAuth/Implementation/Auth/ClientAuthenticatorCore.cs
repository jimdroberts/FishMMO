using System.Security.Cryptography;
using System.Threading;
using SecureRemotePassword;
using FishMMO.Auth.Core;
using FishMMO.Logging;

namespace FishMMO.Auth.Implementation
{
	/// <summary>
	/// Engine-independent client-side authenticator state machine.
	/// Implements the full SRP-6a + X25519 ECDH client auth flow, including
	/// cookie challenge echo, key agreement, token auth (World/Scene), SRP verify/proof,
	/// TOTP, and key material cleanup.
	/// <para>
	/// Concrete implementations (e.g., FishNet <c>ClientLoginAuthenticator</c>) implement
	/// abstract callbacks for broadcasting messages and notifying the application layer.
	/// </para>
	/// </summary>
	public abstract class ClientAuthenticatorCore
	{
		#region Constants

		/// <summary>Minimum allowed username length (inclusive).</summary>
		private const int UsernameMinLength = 3;
		/// <summary>Maximum allowed username length (inclusive).</summary>
		private const int UsernameMaxLength = 32;

		#endregion

		#region Fields

		/// <summary>Ephemeral X25519 keypair for ECDH key agreement. Zeroed after use or on cleanup.</summary>
		private CryptoHelper.X25519EphemeralKeyPair? ephemeralKeyPair;

		/// <summary>Client→server AES-256 key derived via HKDF.</summary>
		private byte[]? clientToServerKey;

		/// <summary>Server→client AES-256 key derived via HKDF.</summary>
		private byte[]? serverToClientKey;

		/// <summary>Nonce context for the client→server (send/encrypt) direction.</summary>
		private CryptoHelper.GcmNonceContext? sendNonceCtx;

		/// <summary>Nonce context for the server→client (receive/decrypt) direction.</summary>
		private CryptoHelper.GcmNonceContext? receiveNonceCtx;

		/// <summary>Negotiated protocol version.</summary>
		private ushort agreedVersion;

		/// <summary>SRP client state.</summary>
		private ClientSrpData? srpData;

		/// <summary>
		/// Guard to ignore duplicate SRP verify messages.
		/// <para>Thread-safety: uses <see cref="Interlocked.CompareExchange"/> for atomic test-and-set.</para>
		/// </summary>
		private int srpVerifyProcessed;

		/// <summary>
		/// Guard to ignore duplicate SRP success messages.
		/// <para>Thread-safety: uses <see cref="Interlocked.CompareExchange"/> for atomic test-and-set.</para>
		/// </summary>
		private int srpSuccessProcessed;

		/// <summary>
		/// Guard to prevent echoing the cookie challenge more than once per connection.
		/// <para>Thread-safety: uses <see cref="Interlocked.CompareExchange"/> for atomic test-and-set.</para>
		/// </summary>
		private int cookieEchoed;

		/// <summary>Signed auth token from the LoginServer. Persists across connections; used for World/Scene auth.</summary>
		/// <remarks>
		/// <b>No client-side expiration check.</b> The client presents this token to every World/Scene
		/// server until the server rejects it (e.g., <see cref="ClientAuthenticationResult.TokenExpired"/>).
		/// Adding a client-side expiry check would save at most one round-trip on an already-expired
		/// token, but the server <em>must</em> validate the token independently regardless — the client
		/// cannot be trusted to self-censor.  Server-side rejection is the authoritative check, and the
		/// wasted round-trip on expiry is negligible compared to the complexity and maintenance burden
		/// of duplicating the expiry logic client-side.
		/// </remarks>
		private byte[]? storedAuthToken;

		// ── Credential fields ──────────────────────────────────────────────────────────────────────
		/// <summary>Username or email address supplied via <see cref="SetLoginCredentials"/>. Cleared after SRP proof is sent.</summary>
		private string? username = "";
		/// <summary>Password supplied via <see cref="SetLoginCredentials"/>. Cleared after SRP proof is sent.</summary>
		private string? password = "";
		/// <summary>Email address supplied for registration. Cleared after the create-account broadcast is sent.</summary>
		private string? email = "";
		/// <summary>User age supplied for registration.</summary>
		private int age;
		/// <summary>True when the current flow is account registration rather than login.</summary>
		private bool register;
		/// <summary>Client game version string (e.g. "0.1.0"), sent during handshake for server validation.</summary>
		private string gameVersion = "";

		#endregion

		#region Game Version

		/// <summary>
		/// Stores the client game version to be sent during the handshake.
		/// Must be called before <see cref="OnConnected"/>.
		/// </summary>
		/// <param name="version">Game version string (e.g. "0.1.0").</param>
		public void SetGameVersion(string version)
		{
			gameVersion = version ?? "";
		}

		#endregion

		#region Properties

		/// <summary>
		/// Returns the pending login identifier (username or email) if still set.
		/// Returns null after credentials are cleared (post-SRP proof or disconnect).
		/// </summary>
		public string? PendingLoginIdentifier => username;

		/// <summary>
		/// Returns whether a stored auth token exists for World/Scene server authentication.
		/// </summary>
		public bool HasAuthToken => storedAuthToken != null;

		/// <summary>Log source tag for all log messages.</summary>
		protected virtual string LogPrefix => GetType().Name;

		#endregion

		#region Credential Setup

		/// <summary>
		/// Sets login credentials. Returns false if the format is invalid.
		/// </summary>
		/// <param name="username">Username or email used as login identifier.</param>
		/// <param name="password">Account password.</param>
		/// <param name="register">True to register a new account; false to login.</param>
		/// <param name="email">Email address (required for registration).</param>
		/// <param name="age">User age (required for registration).</param>
		/// <returns>True if credentials were accepted; false if rejected by validation rules.</returns>
		public bool SetLoginCredentials(string username, string password, bool register = false, string email = "", int age = 0)
		{
			if (!IsAllowedUsername(username) || !IsAllowedPassword(password))
				return false;

			if (register && (string.IsNullOrWhiteSpace(email) || !IsAllowedEmailUsername(email)))
				return false;

			this.username = username;
			this.password = password;
			this.register = register;
			this.email = email;
			this.age = age;
			return true;
		}

		#endregion

		#region Connection Lifecycle

		/// <summary>
		/// Call when a new transport connection is established.
		/// Generates the X25519 keypair and sends the initial <c>ClientHandshake</c>.
		/// </summary>
		/// <param name="connectionToken">
		/// One-time token from the IPFetch HTTP API. Used on initial Login Server
		/// connection for real-IP recovery. Null for World/Scene reconnections.
		/// </param>
		public void OnConnected(string? connectionToken = null)
		{
			ephemeralKeyPair = new CryptoHelper.X25519EphemeralKeyPair();
			srpVerifyProcessed = 0;
			srpSuccessProcessed = 0;
			cookieEchoed = 0;

			SendClientHandshake(
				ephemeralKeyPair.PublicKey,
				cookie: null,
				connectionToken,
				CryptoHelper.MinSupportedProtocolVersion,
				CryptoHelper.MaxSupportedProtocolVersion,
				gameVersion);
		}

		/// <summary>
		/// Call when the transport connection stops or is stopped.
		/// Clears all per-connection key material (not the stored auth token).
		/// </summary>
		public void OnDisconnected()
		{
			ClearKeyMaterial();
		}

		/// <summary>
		/// Resets the per-connection cryptographic state so a fresh <c>ClientHandshake</c> can
		/// be sent on the <em>same</em> transport connection, while keeping the credentials the
		/// pending login still needs.
		/// </summary>
		/// <remarks>
		/// The login queue defers the handshake before SRP begins and later invites the client
		/// to handshake again on the connection it has been holding open. That retry has to
		/// throw away the ephemeral keypair, the derived session keys and the nonce contexts —
		/// the server generates a new set for the new handshake — but it must NOT throw away the
		/// username and password, because SRP has not run yet and nothing will ever supply them
		/// again.
		/// <para>
		/// Reusing <see cref="OnDisconnected"/> for this did exactly that: it calls
		/// <see cref="ClearKeyMaterial"/>, which nulls the credentials along with the keys, so
		/// the re-handshake reached the credential pre-validation in
		/// <see cref="OnServerHandshakeReceived"/> with an empty username and disconnected
		/// itself. Every client that was queued was therefore dropped the instant it reached the
		/// front of the queue, with no message — the queue could never admit anybody.
		/// </para>
		/// <para>
		/// <see cref="OnConnected"/> is what the caller invokes next; it regenerates the keypair
		/// and resets the duplicate-message guards, which a second handshake on one connection
		/// also depends on.
		/// </para>
		/// </remarks>
		public void OnRehandshakeRequired()
		{
			string? keepUsername = username;
			string? keepPassword = password;
			string? keepEmail = email;
			bool keepRegister = register;
			int keepAge = age;

			ClearKeyMaterial();

			username = keepUsername;
			password = keepPassword;
			email = keepEmail;
			register = keepRegister;
			age = keepAge;
		}

		/// <summary>
		/// Disposes the ephemeral keypair. Call from the host object's destroy/dispose method.
		/// </summary>
		public void Dispose()
		{
			ephemeralKeyPair?.Dispose();
			ephemeralKeyPair = null;
			ClearKeyMaterial();
			ClearAuthToken();
		}

		#endregion

		#region Incoming Message Handlers

		/// <summary>
		/// Handles a server handshake response.
		/// Phase 1 (cookie challenge): echoes the cookie with the public key.
		/// Phase 2 (ECDH complete): derives session keys, then initiates SRP or token auth.
		/// </summary>
		/// <param name="serverPublicKey">Server's X25519 public key, or null for a cookie challenge.</param>
		/// <param name="cookie">Cookie from a phase-1 challenge.</param>
		/// <param name="agreedVersion">Negotiated protocol version (only meaningful on phase 2).</param>
		public void OnServerHandshakeReceived(byte[] serverPublicKey, byte[] cookie, ushort agreedVersion)
		{
			// ── Phase 1: Cookie challenge ──────────────────────────────────
			if (serverPublicKey == null)
			{
				if (cookie == null || cookie.Length == 0 || ephemeralKeyPair == null)
				{
					Disconnect();
					return;
				}
				if (Interlocked.CompareExchange(ref cookieEchoed, 1, 0) != 0) return;
				SendClientHandshake(
					ephemeralKeyPair.PublicKey,
					cookie,
					connectionToken: null,
					CryptoHelper.MinSupportedProtocolVersion,
					CryptoHelper.MaxSupportedProtocolVersion,
					gameVersion);
				return;
			}

			// ── Phase 2: ECDH key agreement ────────────────────────────────
			if (serverPublicKey.Length != CryptoHelper.X25519PublicKeyLength)
			{
				Disconnect();
				return;
			}

			if (ephemeralKeyPair == null)
			{
				_ = Log.Warning(LogPrefix, "Received server handshake but no client keypair exists.");
				Disconnect();
				return;
			}

			if (agreedVersion < CryptoHelper.MinSupportedProtocolVersion || agreedVersion > CryptoHelper.MaxSupportedProtocolVersion)
			{
				_ = Log.Warning(LogPrefix, $"Server agreed version {agreedVersion} is outside range [{CryptoHelper.MinSupportedProtocolVersion}..{CryptoHelper.MaxSupportedProtocolVersion}].");
				ClearKeyMaterial();
				Disconnect();
				return;
			}

			this.agreedVersion = agreedVersion;

			try
			{
				var kaResult = HandshakeService.ClientPerformKeyAgreement(
					serverPublicKey, ephemeralKeyPair,
					CryptoHelper.MinSupportedProtocolVersion, CryptoHelper.MaxSupportedProtocolVersion,
					this.agreedVersion);

				ephemeralKeyPair.Dispose();
				ephemeralKeyPair = null;

				if (!kaResult.Success)
				{
					_ = Log.Warning(LogPrefix, "X25519 key agreement failed.");
					ClearKeyMaterial();
					Disconnect();
					return;
				}

				clientToServerKey = kaResult.SessionKeys.ClientToServerKey;
				serverToClientKey = kaResult.SessionKeys.ServerToClientKey;
				sendNonceCtx = new CryptoHelper.GcmNonceContext(kaResult.SessionKeys.ClientPrefix, CryptoHelper.NonceSide.ClientToServer);
				receiveNonceCtx = new CryptoHelper.GcmNonceContext(kaResult.SessionKeys.ServerPrefix, CryptoHelper.NonceSide.ServerToClient);
			}
			catch (CryptographicException ex)
			{
				_ = Log.Warning(LogPrefix, $"X25519 handshake failed: {ex.Message}");
				ClearKeyMaterial();
				Disconnect();
				return;
			}

			srpData = new ClientSrpData(SrpParameters.Create2048<SHA512>());

			/* Which arm this takes is the whole question when a World/Scene hop bounces the
			 * player back to login, and nothing recorded it: the token path is silent on
			 * success, and the credential path only reports the empty username it was never
			 * going to be able to use. */
			bool haveCredentials = !string.IsNullOrEmpty(this.username) && !string.IsNullOrEmpty(this.password);
			_ = Log.Debug(LogPrefix, $"Handshake complete; auth token {(storedAuthToken != null ? "held" : "NOT held")}, credentials {(haveCredentials ? "supplied" : "NOT supplied")}.");

			/* Credentials win over a held token.
			 *
			 * This used to branch on the token alone, which asks "do I happen to have a token?"
			 * when the question is "what is this connection FOR". Credentials are set immediately
			 * before connecting to a Login Server and are nulled the moment the SRP proof is sent,
			 * so their presence is an exact statement of intent: a World/Scene hop and a reconnect
			 * never have them, and a sign-in always does.
			 *
			 * Branching on the token meant any stale token turned every later sign-in into a
			 * TokenAuthBroadcast aimed at a Login Server — which registers no handler for it, so
			 * FishNet drops it and no reply is ever sent. The player got a sign-in that hangs
			 * until the reply deadline and then fails the same way on every retry, because
			 * nothing on that path clears the token: "incorrect login blocks future login until
			 * the client is restarted". A token can be left behind by any return to the sign-in
			 * form that does not go through Client.QuitToLogin — the refusal dialog is one, and
			 * it is reachable for a full second after LoginSuccess while the panel still owns
			 * the flow. */
			if (storedAuthToken != null && haveCredentials)
			{
				/* And the token is dead either way. The player is authenticating from scratch;
				 * a successful SRP exchange mints a fresh one a few messages from here. Leaving
				 * the old bytes in place would just re-arm the same trap for the next connect. */
				_ = Log.Debug(LogPrefix, "Discarding a held auth token: this connection is authenticating with credentials.");
				ClearAuthToken();
			}

			// Token auth path (World/Scene server)
			if (storedAuthToken != null)
			{
				try
				{
					TokenService.ClientEncryptToken(storedAuthToken, clientToServerKey, sendNonceCtx, this.agreedVersion, out byte[] encryptedToken, out uint seq);
					SendTokenAuth(encryptedToken, seq);
				}
				catch (CryptographicException ex)
				{
					_ = Log.Error(LogPrefix, $"AES encryption failed for token auth: {ex.Message}");
					ClearKeyMaterial();
					Disconnect();
				}
				srpData?.Clear();
				srpData = null;
				return;
			}

			// Credential pre-validation
			if (string.IsNullOrEmpty(this.username) || this.username.Length < UsernameMinLength || this.username.Length > UsernameMaxLength)
			{
				/* Reaching the credential path at all is the real problem on a World/Scene hop:
				 * those connections are supposed to authenticate with the stored token, and the
				 * credentials are deliberately nulled after login. Say so, rather than reporting
				 * an empty username as though the player had mistyped it. */
				_ = Log.Warning(LogPrefix, $"Username is empty or outside allowed length ({UsernameMinLength}-{UsernameMaxLength} characters). No stored auth token was available, so this connection fell back to the credential path.");
				ClearKeyMaterial();
				Disconnect();
				return;
			}

			if (string.IsNullOrEmpty(this.password) || this.password.Length < 1)
			{
				_ = Log.Warning(LogPrefix, "Password is empty.");
				ClearKeyMaterial();
				Disconnect();
				return;
			}

			if (register && string.IsNullOrEmpty(this.email))
			{
				_ = Log.Warning(LogPrefix, "Email is required for registration.");
				ClearKeyMaterial();
				Disconnect();
				return;
			}

			byte[] encryptedUsername;
			uint usernameSeq;
			try
			{
				SrpService.ClientEncryptUsername(this.username, clientToServerKey, sendNonceCtx, this.agreedVersion, register, out encryptedUsername, out usernameSeq);
			}
			catch (CryptographicException ex)
			{
				_ = Log.Error(LogPrefix, $"AES encryption failed for username: {ex.Message}");
				ClearKeyMaterial();
				Disconnect();
				return;
			}

			if (register)
			{
				srpData.GetSaltAndVerifier(username, password, out string salt, out string verifier);

				byte[] encryptedEmail;
				byte[] encryptedAge;
				byte[] encryptedSalt;
				byte[] encryptedVerifier;
				uint createAccountSeq;
				try
				{
					SrpService.ClientEncryptRegistrationFields(
						this.email!, this.age, salt, verifier,
						clientToServerKey, sendNonceCtx, this.agreedVersion,
						out encryptedEmail, out encryptedAge, out encryptedSalt, out encryptedVerifier, out createAccountSeq);
				}
				catch (CryptographicException ex)
				{
					_ = Log.Error(LogPrefix, $"AES encryption failed for registration fields: {ex.Message}");
					ClearKeyMaterial();
					Disconnect();
					return;
				}

				SendCreateAccount(encryptedUsername, encryptedEmail, encryptedAge, encryptedSalt, encryptedVerifier, createAccountSeq);
			}
			else
			{
				byte[] encryptedClientEphemeral;
				uint seqClientEphemeral;
				try
				{
					SrpService.ClientEncryptEphemeral(srpData.ClientEphemeral!.Public, clientToServerKey, sendNonceCtx, this.agreedVersion, out encryptedClientEphemeral, out seqClientEphemeral);
				}
				catch (CryptographicException ex)
				{
					_ = Log.Error(LogPrefix, $"AES encryption failed for client ephemeral: {ex.Message}");
					ClearKeyMaterial();
					Disconnect();
					return;
				}

				SendSrpVerify(encryptedUsername, encryptedClientEphemeral, seqClientEphemeral);
			}
		}

		/// <summary>
		/// Handles the SRP verify response from the server.
		/// Decrypts salt + server ephemeral, computes and sends the SRP proof.
		/// </summary>
		/// <param name="encryptedSalt">Encrypted SRP salt from server.</param>
		/// <param name="encryptedServerEphemeral">Encrypted server public ephemeral from server.</param>
		public void OnSrpVerifyResponseReceived(byte[] encryptedSalt, byte[] encryptedServerEphemeral)
		{
			if (srpData == null || Interlocked.CompareExchange(ref srpVerifyProcessed, 1, 0) != 0) return;

			if (receiveNonceCtx == null || serverToClientKey == null ||
				sendNonceCtx == null || clientToServerKey == null)
			{
				Disconnect();
				return;
			}

			string salt;
			string publicServerEphemeral;
			try
			{
				if (encryptedSalt == null || encryptedSalt.Length > CryptoHelper.MaxSrpPayloadBytes ||
					encryptedServerEphemeral == null || encryptedServerEphemeral.Length > CryptoHelper.MaxSrpPayloadBytes)
				{
					ClearKeyMaterial();
					Disconnect();
					return;
				}
				SrpService.ClientDecryptVerifyResponse(encryptedSalt, encryptedServerEphemeral, serverToClientKey, receiveNonceCtx, agreedVersion, out salt, out publicServerEphemeral);
			}
			catch (CryptographicException)
			{
				_ = Log.Warning(LogPrefix, "AES decryption failed for SRP verify response.");
				ClearKeyMaterial();
				Disconnect();
				return;
			}

			if (srpData.GetProof(this.username!, this.password!, salt, publicServerEphemeral, out string proof))
			{
				username = null;
				password = null;
				email = null;
				age = 0;

				byte[] encryptedProof;
				uint seqProof;
				try
				{
					SrpService.ClientEncryptProof(proof, clientToServerKey, sendNonceCtx, agreedVersion, out encryptedProof, out seqProof);
				}
				catch (CryptographicException ex)
				{
					_ = Log.Error(LogPrefix, $"AES encryption failed for client proof: {ex.Message}");
					Disconnect();
					return;
				}

				SendSrpProof(encryptedProof, seqProof);
			}
			else
			{
				username = null;
				password = null;
				email = null;
				age = 0;
				Disconnect();
			}
		}

		/// <summary>
		/// Handles the SRP success response from the server.
		/// Verifies the server proof, extracts and stores the auth token on success.
		/// </summary>
		/// <param name="encryptedServerProof">Encrypted server proof.</param>
		/// <param name="result">Auth result code sent alongside the proof.</param>
		/// <param name="encryptedToken">Encrypted auth token (null if not a LoginSuccess).</param>
		public void OnSrpSuccessReceived(byte[] encryptedServerProof, ClientAuthenticationResult result, byte[] encryptedToken)
		{
			if (srpData == null || Interlocked.CompareExchange(ref srpSuccessProcessed, 1, 0) != 0) return;

			if (receiveNonceCtx == null || serverToClientKey == null)
			{
				Disconnect();
				return;
			}

			string proof;
			try
			{
				if (encryptedServerProof == null || encryptedServerProof.Length > CryptoHelper.MaxSrpPayloadBytes)
				{
					ClearKeyMaterial();
					Disconnect();
					return;
				}
				proof = SrpService.ClientDecryptServerProof(encryptedServerProof, serverToClientKey, receiveNonceCtx, agreedVersion);
			}
			catch (CryptographicException)
			{
				_ = Log.Warning(LogPrefix, "AES decryption failed for SRP success proof.");
				ClearKeyMaterial();
				Disconnect();
				return;
			}

			if (srpData.Verify(proof, out string _))
			{

				if (result == ClientAuthenticationResult.LoginSuccess &&
					encryptedToken != null && encryptedToken.Length > 0)
				{
					try
					{
						storedAuthToken = SrpService.ClientDecryptAuthToken(encryptedToken, serverToClientKey, receiveNonceCtx, agreedVersion);
						if (storedAuthToken == null || storedAuthToken.Length == 0)
						{
							if (storedAuthToken != null)
							{
								CryptographicOperations.ZeroMemory(storedAuthToken);
							}
							storedAuthToken = null;
							_ = Log.Warning(LogPrefix, "Decrypted auth token is null or empty.");
						}
					}
					catch (CryptographicException tokenEx)
					{
						_ = Log.Warning(LogPrefix, $"Failed to decrypt auth token (non-fatal): {tokenEx.Message}");
						storedAuthToken = null;
					}
				}

				ClientAuthenticationResult effectiveResult =
					result == ClientAuthenticationResult.LoginSuccess && storedAuthToken == null
						? ClientAuthenticationResult.TokenDecryptFailed
						: result;

				OnAuthResultCallback(effectiveResult);

				/* The effective result, not the raw one. Logging `result` reported LoginSuccess
				 * even when the token had failed to decrypt and TokenDecryptFailed was what the
				 * client actually acted on — so the log positively asserted the opposite of what
				 * happened, in exactly the case someone would be reading it to diagnose. */
				_ = Log.Debug(LogPrefix, effectiveResult.ToString());

				srpData.Clear();
				srpData = null;
			}
			else
			{
				Disconnect();
			}
		}

		/// <summary>
		/// Handles a generic auth result broadcast from the server.
		/// Clears the stored token on terminal token failures.
		/// </summary>
		/// <param name="result">The auth result code.</param>
		public void OnAuthResultReceived(ClientAuthenticationResult result)
		{
			if (result == ClientAuthenticationResult.TokenInvalid ||
				result == ClientAuthenticationResult.TokenExpired ||
				result == ClientAuthenticationResult.TokenRevoked)
			{
				ClearAuthToken();
			}

			OnAuthResultCallback(result);
			_ = Log.Debug(LogPrefix, result.ToString());
		}

		/// <summary>
		/// Handles a 2FA setup broadcast (received after successful account registration).
		/// Decrypts the otpauth URI and recovery codes then fires <see cref="OnTwoFactorSetupCallback"/>.
		/// </summary>
		/// <param name="encryptedOtpauthUri">Encrypted otpauth URI bytes.</param>
		/// <param name="encryptedRecoveryCodes">Encrypted recovery codes array.</param>
		public void OnTwoFactorSetupReceived(byte[] encryptedOtpauthUri, byte[] encryptedRecoveryCodes)
		{
			if (receiveNonceCtx == null || serverToClientKey == null) return;
			if (encryptedOtpauthUri == null || encryptedOtpauthUri.Length == 0 ||
				encryptedRecoveryCodes == null || encryptedRecoveryCodes.Length == 0) return;

			string otpauthUri;
			string[] recoveryCodes;
			try
			{
				SrpService.ClientDecryptTwoFactorSetup(encryptedOtpauthUri, encryptedRecoveryCodes, serverToClientKey, receiveNonceCtx, agreedVersion, out otpauthUri, out recoveryCodes);
			}
			catch (CryptographicException)
			{
				_ = Log.Warning(LogPrefix, "AES decryption failed for 2FA setup data.");
				return;
			}

			OnTwoFactorSetupCallback(otpauthUri, recoveryCodes);
		}

		#endregion

		#region Send Helpers

		/// <summary>
		/// Encrypts and sends an account verification code.
		/// </summary>
		/// <param name="username">Account username to verify.</param>
		/// <param name="verifyCode">The verification code received by the user.</param>
		public void SendVerifyCode(string username, string verifyCode)
		{
			if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(verifyCode)) return;
			if (sendNonceCtx == null || clientToServerKey == null)
			{
				Disconnect();
				return;
			}

			byte[] encryptedUsername;
			byte[] encryptedCode;
			uint accountVerifySeq;
			try
			{
				SrpService.ClientEncryptAccountVerify(username, verifyCode, clientToServerKey, sendNonceCtx, agreedVersion, out encryptedUsername, out encryptedCode, out accountVerifySeq);
			}
			catch (CryptographicException ex)
			{
				_ = Log.Error(LogPrefix, $"AES encryption failed for verify code: {ex.Message}");
				ClearKeyMaterial();
				Disconnect();
				return;
			}

			SendAccountVerify(encryptedUsername, encryptedCode, accountVerifySeq);
		}

		/// <summary>
		/// Encrypts and sends a TOTP code for two-factor verification.
		/// </summary>
		/// <param name="code">The 6-digit TOTP code from the authenticator app.</param>
		public void SendTotpCode(string code)
		{
			if (string.IsNullOrEmpty(code)) return;
			if (sendNonceCtx == null || clientToServerKey == null)
			{
				Disconnect();
				return;
			}

			byte[] encryptedCode;
			uint totpSeq;
			try
			{
				SrpService.ClientEncryptTotpCode(code, clientToServerKey, sendNonceCtx, agreedVersion, out encryptedCode, out totpSeq);
			}
			catch (CryptographicException ex)
			{
				_ = Log.Error(LogPrefix, $"AES encryption failed for TOTP code: {ex.Message}");
				ClearKeyMaterial();
				Disconnect();
				return;
			}

			SendTwoFactorVerify(encryptedCode, totpSeq);
		}

		#endregion

		#region Key Material Cleanup

		/// <summary>
		/// Zeroes all per-connection key material and resets state.
		/// Does NOT clear <see cref="storedAuthToken"/>.
		/// </summary>
		public void ClearKeyMaterial()
		{
			ephemeralKeyPair?.Dispose();
			ephemeralKeyPair = null;
			srpData?.Clear();
			srpData = null;
			if (clientToServerKey != null)
			{
				CryptographicOperations.ZeroMemory(clientToServerKey);
				clientToServerKey = null;
			}
			if (serverToClientKey != null)
			{
				CryptographicOperations.ZeroMemory(serverToClientKey);
				serverToClientKey = null;
			}
			sendNonceCtx?.Dispose();
			sendNonceCtx = null;
			receiveNonceCtx?.Dispose();
			receiveNonceCtx = null;
			agreedVersion = 0;
			username = null;
			password = null;
			email = null;
			register = false;
			age = 0;
		}

		/// <summary>
		/// Stores a raw auth token for use in the next token auth flow.
		/// Intended for test harnesses that need to inject a token without going through the full SRP flow.
		/// </summary>
		/// <param name="rawToken">Raw token bytes to store. Must not be null or empty.</param>
		protected void SetStoredAuthToken(byte[] rawToken)
		{
			if (storedAuthToken != null)
				CryptographicOperations.ZeroMemory(storedAuthToken);
			storedAuthToken = rawToken;
		}

		/// <summary>
		/// Zeroes and clears the stored auth token.
		/// Call on explicit logout or when the token is no longer needed.
		/// </summary>
		public void ClearAuthToken()
		{
			if (storedAuthToken != null)
			{
				CryptographicOperations.ZeroMemory(storedAuthToken);
				storedAuthToken = null;
			}
		}

		/// <summary>
		/// Returns a defensive copy of the currently stored auth token (the raw,
		/// HMAC-signed bytes the LoginServer issued) for the sole purpose of sending
		/// a server-side revocation request, then zeroes and clears the stored copy.
		/// Returns <c>false</c> if no token is currently held.
		///
		/// Security note: the returned bytes are transmitted to the server in cleartext
		/// (the auth pipeline's AES-GCM channel has typically been torn down by the time
		/// the user explicitly logs out). The server hashes the bytes and matches them
		/// against the persisted token-hash row, then marks it revoked. Because the token
		/// is being revoked anyway, any eavesdropper who captures it gains nothing.
		/// </summary>
		/// <param name="tokenCopy">Defensive copy of the stored token, or <c>null</c> when none was held.</param>
		/// <returns><c>true</c> when a token was returned; <c>false</c> when none was held.</returns>
		public bool TryConsumeStoredTokenForRevoke(out byte[]? tokenCopy)
		{
			if (storedAuthToken == null)
			{
				tokenCopy = null;
				return false;
			}
			tokenCopy = new byte[storedAuthToken.Length];
			System.Buffer.BlockCopy(storedAuthToken, 0, tokenCopy, 0, storedAuthToken.Length);
			CryptographicOperations.ZeroMemory(storedAuthToken);
			storedAuthToken = null;
			return true;
		}

		/// <summary>
		/// Decrypts a freshly-minted auth token received mid-session from a World/Scene
		/// server (over the existing AES-GCM session channel) and replaces the currently
		/// stored token. Used by the reconnect-only token-refresh flow so that future
		/// reconnect attempts continue working past the original token's expiration.
		/// </summary>
		/// <param name="encryptedToken">AES-GCM encrypted auth token bytes from the server.</param>
		/// <returns><c>true</c> if the token was decrypted and stored; <c>false</c> on
		/// missing session keys, empty payload, or decryption failure.</returns>
		public bool TryApplyRenewedToken(byte[]? encryptedToken)
		{
			if (encryptedToken == null || encryptedToken.Length == 0) return false;
			if (receiveNonceCtx == null || serverToClientKey == null) return false;

			try
			{
				byte[] newToken = SrpService.ClientDecryptAuthToken(encryptedToken, serverToClientKey, receiveNonceCtx, agreedVersion);
				if (newToken == null || newToken.Length == 0) return false;

				if (storedAuthToken != null)
				{
					CryptographicOperations.ZeroMemory(storedAuthToken);
				}
				storedAuthToken = newToken;
				return true;
			}
			catch (CryptographicException ex)
			{
				_ = Log.Warning(LogPrefix, $"Renewed auth token decryption failed (non-fatal): {ex.Message}");
				return false;
			}
		}

		#endregion

		#region Abstract Transport Callbacks

		/// <summary>
		/// Sends the initial or cookie-echo client handshake broadcast.
		/// </summary>
		/// <param name="publicKey">Client's ephemeral X25519 public key (32 bytes).</param>
		/// <param name="cookie">Cookie echoed from a prior challenge, or null on the initial handshake.</param>
		/// <param name="minVersion">Minimum protocol version supported by this client.</param>
		/// <param name="maxVersion">Maximum protocol version supported by this client.</param>
		protected abstract void SendClientHandshake(byte[] publicKey, byte[]? cookie, string? connectionToken, ushort minVersion, ushort maxVersion, string gameVersion);

		/// <summary>
		/// Sends a token auth broadcast (World/Scene server path).
		/// </summary>
		/// <param name="encryptedToken">AES-GCM encrypted auth token.</param>
		/// <param name="seq">Message sequence number.</param>
		protected abstract void SendTokenAuth(byte[] encryptedToken, uint seq);

		/// <summary>
		/// Sends the SRP verify broadcast (login path, phase 1).
		/// </summary>
		/// <param name="encryptedUsername">AES-GCM encrypted username bytes.</param>
		/// <param name="encryptedClientEphemeral">AES-GCM encrypted SRP client public ephemeral.</param>
		/// <param name="seq">Message sequence number.</param>
		protected abstract void SendSrpVerify(byte[] encryptedUsername, byte[] encryptedClientEphemeral, uint seq);

		/// <summary>
		/// Sends the SRP proof broadcast (login path, phase 2).
		/// </summary>
		/// <param name="encryptedProof">AES-GCM encrypted SRP client proof.</param>
		/// <param name="seq">Message sequence number.</param>
		protected abstract void SendSrpProof(byte[] encryptedProof, uint seq);

		/// <summary>
		/// Sends the account creation broadcast (registration path).
		/// </summary>
		/// <param name="encryptedUsername">AES-GCM encrypted username.</param>
		/// <param name="encryptedEmail">AES-GCM encrypted email address.</param>
		/// <param name="encryptedAge">AES-GCM encrypted age value.</param>
		/// <param name="encryptedSalt">AES-GCM encrypted SRP salt.</param>
		/// <param name="encryptedVerifier">AES-GCM encrypted SRP verifier.</param>
		/// <param name="seq">Message sequence number.</param>
		protected abstract void SendCreateAccount(
			byte[] encryptedUsername, byte[] encryptedEmail, byte[] encryptedAge,
			byte[] encryptedSalt, byte[] encryptedVerifier, uint seq);

		/// <summary>
		/// Sends an account verification code broadcast.
		/// </summary>
		/// <param name="encryptedUsername">AES-GCM encrypted username.</param>
		/// <param name="encryptedCode">AES-GCM encrypted verification code.</param>
		/// <param name="seq">Message sequence number.</param>
		protected abstract void SendAccountVerify(byte[] encryptedUsername, byte[] encryptedCode, uint seq);

		/// <summary>
		/// Sends a TOTP verify broadcast.
		/// </summary>
		/// <param name="encryptedCode">AES-GCM encrypted TOTP code.</param>
		/// <param name="seq">Message sequence number.</param>
		protected abstract void SendTwoFactorVerify(byte[] encryptedCode, uint seq);

		/// <summary>
		/// Disconnects the client. Called on fatal protocol errors.
		/// </summary>
		protected abstract void Disconnect();

		/// <summary>
		/// Invoked when an auth result is received from the server.
		/// Implementations should fire a UI event or property change.
		/// </summary>
		protected abstract void OnAuthResultCallback(ClientAuthenticationResult result);

		/// <summary>
		/// Invoked when the server sends 2FA setup data after account creation.
		/// </summary>
		/// <param name="otpauthUri">otpauth URI for authenticator app QR code.</param>
		/// <param name="recoveryCodes">Recovery codes array.</param>
		protected abstract void OnTwoFactorSetupCallback(string otpauthUri, string[] recoveryCodes);

		/// <summary>
		/// Validates a username according to project rules.
		/// Delegates to <c>Authentication.IsAllowedUsername</c>.
		/// </summary>
		/// <param name="username">The username string to validate.</param>
		/// <returns><c>true</c> if the username is allowed; otherwise, <c>false</c>.</returns>
		protected abstract bool IsAllowedUsername(string username);

		/// <summary>
		/// Validates a password according to project rules.
		/// Delegates to <c>Authentication.IsAllowedPassword</c>.
		/// </summary>
		/// <param name="password">The password string to validate.</param>
		/// <returns><c>true</c> if the password is allowed; otherwise, <c>false</c>.</returns>
		protected abstract bool IsAllowedPassword(string password);

		/// <summary>
		/// Validates an email-format username.
		/// Delegates to <c>Authentication.IsAllowedEmailUsername</c>.
		/// </summary>
		/// <param name="email">The email address to validate.</param>
		/// <returns><c>true</c> if the email is a valid login identifier; otherwise, <c>false</c>.</returns>
		protected abstract bool IsAllowedEmailUsername(string email);

		#endregion
	}
}