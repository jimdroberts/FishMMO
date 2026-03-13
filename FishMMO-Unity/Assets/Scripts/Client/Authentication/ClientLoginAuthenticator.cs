using FishNet.Authenticating;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using System;
using System.Security.Cryptography;
using System.Text;
using SecureRemotePassword;
using FishMMO.Shared;
using FishMMO.Logging;
using System.Runtime.CompilerServices;

namespace FishMMO.Client
{
	public class ClientLoginAuthenticator : Authenticator
	{
		/// <summary>
		/// The username used for authentication or registration.
		/// </summary>
		private string username = "";
		/// <summary>
		/// The password used for authentication or registration.
		/// </summary>
		/// <remarks>
		/// <b>DESIGN NOTE — password as managed string:</b>
		/// .NET strings are immutable and cannot be deterministically zeroed.
		/// Setting <c>password = null</c> only removes the reference; the string data
		/// lingers in the managed heap until overwritten by the GC.
		/// The SecureRemotePassword library requires string parameters, making
		/// <c>byte[]</c>-based storage impractical. Mitigation: the password is nulled
		/// as early as possible after SRP proof generation
		/// (see <see cref="OnClientSrpVerifyBroadcastReceived"/>).
		/// </remarks>
		private string password = "";
		/// <summary>
		/// The email used for multi-factor login identification.
		/// </summary>
		private string email = "";
		/// <summary>
		/// The age used for multi-factor login identification.
		/// </summary>
		private int age;
		/// <summary>
		/// Indicates whether the client is registering a new account.
		/// </summary>
		private bool register;
		/// <summary>
		/// Ephemeral X25519 keypair for ECDH key agreement during handshake.
		/// Private key is zeroed automatically after <c>DeriveSharedSecret</c> or on dispose.
		/// </summary>
		private CryptoHelper.X25519EphemeralKeyPair ephemeralKeyPair;
		/// <summary>
		/// Client→server AES-256 key derived via HKDF after handshake.
		/// </summary>
		private byte[] clientToServerKey;
		/// <summary>
		/// Server→client AES-256 key derived via HKDF after handshake.
		/// </summary>
		private byte[] serverToClientKey;
		/// <summary>
		/// Nonce context for client→server (send/encrypt) direction.
		/// Owns the client prefix and send counter.
		/// </summary>
		private CryptoHelper.GcmNonceContext sendNonceCtx;
		/// <summary>
		/// Nonce context for server→client (receive/decrypt) direction.
		/// Owns the server prefix and receive counter.
		/// </summary>
		private CryptoHelper.GcmNonceContext receiveNonceCtx;
		/// <summary>
		/// Negotiated protocol version for this connection.
		/// </summary>
		private ushort agreedVersion;
		/// <summary>
		/// SRP data for secure remote password authentication.
		/// </summary>
		private ClientSrpData SrpData;

		/// <summary>Guard to ignore duplicate SRP verify messages. Main-thread only — no volatile needed.</summary>
		private bool srpVerifyProcessed;

		/// <summary>Guard to ignore duplicate SRP success messages. Main-thread only — no volatile needed.</summary>
		private bool srpSuccessProcessed;

		/// <summary>
		/// Signed auth token received from the LoginServer after SRP success.
		/// Persists across connection changes (not cleared by ClearKeyMaterial).
		/// Used for token-based authentication with World/Scene servers.
		/// <para><b>Expiration:</b> Enforced server-side via the embedded UTC timestamp.
		/// The client does not independently track expiry — presenting an expired token
		/// results in <see cref="ClientAuthenticationResult.TokenExpired"/> which triggers
		/// <see cref="ClearAuthToken"/>. For proactive client-side expiry, callers can
		/// store the issuance time alongside and clear the token after a local timeout.</para>
		/// <para><b>Memory lifetime:</b> The token bytes remain in the managed heap until
		/// <see cref="ClearAuthToken"/> zeroes and nulls them. This is acceptable because
		/// the token is encrypted at rest (AES-GCM) and HMAC-signed.</para>
		/// </summary>
		private byte[] storedAuthToken;

		/// <summary>
		/// Guard to prevent echoing the cookie challenge more than once per connection.
		/// </summary>
		private bool cookieEchoed;

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
		/// Initializes the authenticator once with the provided network manager.
		/// Registers connection state and broadcast handlers.
		/// </summary>
		/// <param name="networkManager">The network manager instance.</param>
		public override void InitializeOnce(NetworkManager networkManager)
		{
			base.InitializeOnce(networkManager);

			base.NetworkManager.ClientManager.OnClientConnectionState += ClientManager_OnClientConnectionState;
			base.NetworkManager.ClientManager.RegisterBroadcast<ServerHandshake>(OnClientServerHandshakeBroadcastReceived);
			base.NetworkManager.ClientManager.RegisterBroadcast<SrpVerifyBroadcast>(OnClientSrpVerifyBroadcastReceived);
			base.NetworkManager.ClientManager.RegisterBroadcast<SrpSuccessBroadcast>(OnClientSrpSuccessBroadcastReceived);
			base.NetworkManager.ClientManager.RegisterBroadcast<ClientAuthResultBroadcast>(OnClientAuthResultBroadcastReceived);
			base.NetworkManager.ClientManager.RegisterBroadcast<TwoFactorSetupBroadcast>(OnClientTwoFactorSetupBroadcastReceived);
		}

		/// <summary>
		/// Unity event called when the object is destroyed. Disposes ephemeral keypair.
		/// </summary>
		private void OnDestroy()
		{
			ephemeralKeyPair?.Dispose();
			ephemeralKeyPair = null;
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
		/// Returns false if the basic credential format is invalid (username or password
		/// doesn't pass shared validation rules), preventing a wasteful connection attempt.
		/// </summary>
		/// <param name="username">The username.</param>
		/// <param name="password">The password.</param>
		/// <param name="register">True to register a new account; false to login.</param>
		/// <param name="email">The email address for multi-factor identification.</param>
		/// <param name="age">The age for multi-factor identification.</param>
		/// <returns><c>true</c> if credentials were accepted; <c>false</c> if rejected.</returns>
		public bool SetLoginCredentials(string username, string password, bool register = false, string email = "", int age = 0)
		{
			if (!Authentication.IsAllowedUsername(username) || !Authentication.IsAllowedPassword(password))
			{
				return false;
			}

			if (register && (string.IsNullOrWhiteSpace(email) || !Authentication.IsAllowedEmailUsername(email)))
			{
				return false;
			}

			this.username = username;
			this.password = password;
			this.register = register;
			this.email = email;
			this.age = age;
			return true;
		}

		/// <summary>
		/// Handles client connection state changes. Initiates handshake when connection starts.
		/// </summary>
		/// <param name="args">Connection state arguments.</param>
		private void ClientManager_OnClientConnectionState(ClientConnectionStateArgs args)
		{
			if (args.ConnectionState == LocalConnectionState.Stopping ||
				args.ConnectionState == LocalConnectionState.Stopped)
			{
				ClearKeyMaterial();
			}

			if (args.ConnectionState != LocalConnectionState.Started)
				return;

			// Generate ephemeral X25519 keypair — private key is never exposed.
			ephemeralKeyPair = new CryptoHelper.X25519EphemeralKeyPair();

			// Reset SRP guards and cookie echo guard for a fresh session
			srpVerifyProcessed = false;
			srpSuccessProcessed = false;
			cookieEchoed = false;

			// Initiate a handshake with the server, advertising our version range.
			Client.Broadcast(new ClientHandshake()
			{
				PublicKey = ephemeralKeyPair.PublicKey,
				MinVersion = CryptoHelper.MinSupportedProtocolVersion,
				MaxVersion = CryptoHelper.MaxSupportedProtocolVersion,
			}, Channel.Reliable);
		}

		/// <summary>
		/// Handles the server handshake broadcast. If the server sent a cookie challenge
		/// (PublicKey is null), echoes it back. Otherwise performs X25519 ECDH key agreement,
		/// derives directional AES-256 session keys, then initiates SRP or token-based auth.
		/// </summary>
		/// <param name="msg">The server handshake message.</param>
		/// <param name="channel">The network channel.</param>
		private void OnClientServerHandshakeBroadcastReceived(ServerHandshake msg, Channel channel)
		{
			// ── Cookie challenge ────────────────────────────────────────────
			// Server sent a stateless cookie for proof-of-reachability.
			// Echo it back with our public key to proceed to X25519.
			if (msg.PublicKey == null)
			{
				if (msg.Cookie == null || msg.Cookie.Length == 0 || ephemeralKeyPair == null)
				{
					Client.ForceDisconnect();
					return;
				}
				// Only echo the cookie once per connection to prevent replay abuse.
				if (cookieEchoed)
					return;
				cookieEchoed = true;
				Client.Broadcast(new ClientHandshake()
				{
					PublicKey = ephemeralKeyPair.PublicKey,
					Cookie = msg.Cookie,
					MinVersion = CryptoHelper.MinSupportedProtocolVersion,
					MaxVersion = CryptoHelper.MaxSupportedProtocolVersion,
				}, Channel.Reliable);
				return;
			}

			// ── Full handshake response ─────────────────────────────────────
			if (msg.PublicKey.Length != CryptoHelper.X25519PublicKeyLength)
			{
				Client.ForceDisconnect();
				return;
			}

			if (ephemeralKeyPair == null)
			{
				Log.Warning("ClientLoginAuthenticator", "Received server handshake but no client keypair exists.");
				Client.ForceDisconnect();
				return;
			}

			// Validate negotiated version is within our supported range.
			if (msg.AgreedVersion < CryptoHelper.MinSupportedProtocolVersion ||
				msg.AgreedVersion > CryptoHelper.MaxSupportedProtocolVersion)
			{
				Log.Warning("ClientLoginAuthenticator", $"Server agreed version {msg.AgreedVersion} is outside our range [{CryptoHelper.MinSupportedProtocolVersion}..{CryptoHelper.MaxSupportedProtocolVersion}].");
				ClearKeyMaterial();
				Client.ForceDisconnect();
				return;
			}

			agreedVersion = msg.AgreedVersion;

			try
			{
				// Compute transcript hash with domain separation and version binding, matching server order:
				// SHA256(domain || clientPub || serverPub || clientMin(2B) || clientMax(2B) || agreed(2B))
				byte[] transcriptHash;
				using (var sha = SHA256.Create())
				{
					sha.TransformBlock(CryptoHelper.HandshakeDomainSeparator, 0, CryptoHelper.HandshakeDomainSeparator.Length, null, 0);
					sha.TransformBlock(ephemeralKeyPair.PublicKey, 0, ephemeralKeyPair.PublicKey.Length, null, 0);
					sha.TransformBlock(msg.PublicKey, 0, msg.PublicKey.Length, null, 0);
					// Bind version negotiation into the transcript to match the server's computation.
					byte[] versionBytes = new byte[6];
					versionBytes[0] = (byte)(CryptoHelper.MinSupportedProtocolVersion >> 8);
					versionBytes[1] = (byte)CryptoHelper.MinSupportedProtocolVersion;
					versionBytes[2] = (byte)(CryptoHelper.MaxSupportedProtocolVersion >> 8);
					versionBytes[3] = (byte)CryptoHelper.MaxSupportedProtocolVersion;
					versionBytes[4] = (byte)(agreedVersion >> 8);
					versionBytes[5] = (byte)agreedVersion;
					sha.TransformFinalBlock(versionBytes, 0, versionBytes.Length);
					transcriptHash = sha.Hash;
				}

				// Derive shared secret via X25519 ECDH + HKDF.
				// DeriveSharedSecret auto-zeros the private key after use (single-use).
				byte[] sharedSecret = ephemeralKeyPair.DeriveSharedSecret(msg.PublicKey, transcriptHash);
				ephemeralKeyPair.Dispose();
				ephemeralKeyPair = null;

				// Derive directional session keys from shared secret + transcript hash.
				// DeriveSessionKeys zeroes masterSecret (sharedSecret) internally.
				var sessionKeys = CryptoHelper.DeriveSessionKeys(sharedSecret, transcriptHash);
				clientToServerKey = sessionKeys.ClientToServerKey;
				serverToClientKey = sessionKeys.ServerToClientKey;

				// Create nonce contexts that own copies of the session prefixes.
				// Client send = client→server direction
				// Client receive = server→client direction
				sendNonceCtx = new CryptoHelper.GcmNonceContext(sessionKeys.ClientPrefix, CryptoHelper.NonceSide.ClientToServer);
				receiveNonceCtx = new CryptoHelper.GcmNonceContext(sessionKeys.ServerPrefix, CryptoHelper.NonceSide.ServerToClient);

				// sharedSecret already zeroed by DeriveSessionKeys.

				// Zero transcript hash — no longer needed for key derivation.
				CryptographicOperations.ZeroMemory(transcriptHash);
			}
			catch (CryptographicException ex)
			{
				Log.Warning("ClientLoginAuthenticator", $"X25519 handshake failed: {ex.Message}");
				ClearKeyMaterial();
				Client.ForceDisconnect();
				return;
			}

			// SRP parameters are fixed at 2048-bit group with SHA-512.
			// If parameter negotiation is needed in the future, the server should
			// advertise supported parameters in the ServerHandshake and the client
			// should select from that set. ProtocolVersion in AAD would enforce
			// that both sides agree on the parameter set.
			SrpData = new ClientSrpData(SrpParameters.Create2048<SHA512>());

			// If we have a stored auth token (from a previous LoginServer SRP success),
			// send it encrypted to the World/Scene server for token-based authentication.
			if (storedAuthToken != null)
			{
				try
				{
					var (nonce, seq) = sendNonceCtx.NextNonce();
					byte[] aad = CryptoHelper.BuildAad((byte)CryptoHelper.AuthMessageType.TokenAuth, agreedVersion, seq);
					byte[] encryptedToken = CryptoHelper.EncryptAES(clientToServerKey, nonce, storedAuthToken, aad);

					Client.Broadcast(new TokenAuthBroadcast()
					{
						Token = encryptedToken,
						Seq = seq,
					}, Channel.Reliable);
				}
				catch (CryptographicException ex)
				{
					Log.Error("ClientLoginAuthenticator", $"AES encryption failed for token auth: {ex.Message}");
					ClearKeyMaterial();
					Client.ForceDisconnect();
				}
				return;
			}

			// Pre-validate all credentials before consuming any sequence numbers.
			// This prevents protocol desync if validation would fail after sequences are consumed.
			if (string.IsNullOrEmpty(this.username) || this.username.Length < 3 || this.username.Length > 32)
			{
				Log.Warning("ClientLoginAuthenticator", "Username is empty or outside allowed length (3-32 characters).");
				ClearKeyMaterial();
				Client.ForceDisconnect();
				return;
			}

			if (string.IsNullOrEmpty(this.password) || this.password.Length < 1)
			{
				Log.Warning("ClientLoginAuthenticator", "Password is empty.");
				ClearKeyMaterial();
				Client.ForceDisconnect();
				return;
			}

			if (register && string.IsNullOrEmpty(this.email))
			{
				Log.Warning("ClientLoginAuthenticator", "Email is required for registration.");
				ClearKeyMaterial();
				Client.ForceDisconnect();
				return;
			}

			// Encrypt the username before sending using an explicit sequence number.
			byte[] usernameBytes = Encoding.UTF8.GetBytes(this.username);
			byte[] encryptedUsername;
			try
			{
				var (nonce, seq) = sendNonceCtx.NextNonce();
				// Registration encrypts username with CreateAccount AAD (server-side expectation).
				// Login encrypts with SrpVerify AAD to match ServerAuthenticator's decrypt.
				var aadType = register
					? CryptoHelper.AuthMessageType.CreateAccount
					: CryptoHelper.AuthMessageType.SrpVerify;
				byte[] aad = CryptoHelper.BuildAad((byte)aadType, agreedVersion, seq);
				encryptedUsername = CryptoHelper.EncryptAES(clientToServerKey, nonce, usernameBytes, aad);
				// include Seq in broadcast
				// Note: CreateAccount/SrpVerify will get Seq set below when broadcasting
			}
			catch (CryptographicException ex)
			{
				Log.Error("ClientLoginAuthenticator", $"AES encryption failed for username: {ex.Message}");
				ClearKeyMaterial();
				Client.ForceDisconnect();
				return;
			}
			finally
			{
				CryptographicOperations.ZeroMemory(usernameBytes);
			}

			// Register a new account
			if (register)
			{
				// Design note — String heap retention (salt, verifier): GetSaltAndVerifier returns
				// .NET strings that persist on the managed heap until the GC collects them. These
				// contain the SRP salt (random) and verifier (derived from password). The verifier is
				// not the password itself — it is a one-way derivation — so heap retention does not
				// directly leak the password. Nevertheless, an attacker with memory-read access could
				// use the verifier for offline brute-force. No practical mitigation exists within the
				// .NET string/SRP library constraint; the byte[] intermediates are zeroed below.
				SrpData.GetSaltAndVerifier(username, password, out string salt, out string verifier);

				// Encrypt email and age before sending
				byte[] emailBytes = Encoding.UTF8.GetBytes(this.email ?? "");
				byte[] ageBytes = Encoding.UTF8.GetBytes(this.age.ToString());
				byte[] encryptedEmail;
				byte[] encryptedAge;
				try
				{
					var (nonceE, seqE) = sendNonceCtx.NextNonce();
					byte[] aadE = CryptoHelper.BuildAad((byte)CryptoHelper.AuthMessageType.CreateAccount, agreedVersion, seqE);
					encryptedEmail = CryptoHelper.EncryptAES(clientToServerKey, nonceE, emailBytes, aadE);

					var (nonceA, seqA) = sendNonceCtx.NextNonce();
					byte[] aadA = CryptoHelper.BuildAad((byte)CryptoHelper.AuthMessageType.CreateAccount, agreedVersion, seqA);
					encryptedAge = CryptoHelper.EncryptAES(clientToServerKey, nonceA, ageBytes, aadA);
				}
				catch (CryptographicException ex)
				{
					Log.Error("ClientLoginAuthenticator", $"AES encryption failed for email/age: {ex.Message}");
					ClearKeyMaterial();
					Client.ForceDisconnect();
					CryptographicOperations.ZeroMemory(emailBytes);
					CryptographicOperations.ZeroMemory(ageBytes);
					return;
				}
				CryptographicOperations.ZeroMemory(emailBytes);
				CryptographicOperations.ZeroMemory(ageBytes);

				// Encrypt the salt and verifier before sending
				byte[] saltBytes = Encoding.UTF8.GetBytes(salt);
				byte[] verifierBytes = Encoding.UTF8.GetBytes(verifier);
				byte[] encryptedSalt;
				byte[] encryptedVerifier;
				uint createAccountSeq = 0;
				try
				{
					var (nonce1, seq1) = sendNonceCtx.NextNonce();
					byte[] aad1 = CryptoHelper.BuildAad((byte)CryptoHelper.AuthMessageType.CreateAccount, agreedVersion, seq1);
					encryptedSalt = CryptoHelper.EncryptAES(clientToServerKey, nonce1, saltBytes, aad1);

					var (nonce2, seq2) = sendNonceCtx.NextNonce();
					createAccountSeq = seq2;
					byte[] aad2 = CryptoHelper.BuildAad((byte)CryptoHelper.AuthMessageType.CreateAccount, agreedVersion, createAccountSeq);
					encryptedVerifier = CryptoHelper.EncryptAES(clientToServerKey, nonce2, verifierBytes, aad2);
				}
				catch (CryptographicException ex)
				{
					Log.Error("ClientLoginAuthenticator", $"AES encryption failed for salt/verifier: {ex.Message}");
					ClearKeyMaterial();
					Client.ForceDisconnect();
					CryptographicOperations.ZeroMemory(saltBytes);
					CryptographicOperations.ZeroMemory(verifierBytes);
					return;
				}
				CryptographicOperations.ZeroMemory(saltBytes);
				CryptographicOperations.ZeroMemory(verifierBytes);

				// PROTOCOL NOTE — CreateAccountBroadcast implicit sequence encoding:
				// Seq is the verifier’s sequence. The server derives:
				//   username_seq = Seq - 4   email_seq = Seq - 3   age_seq = Seq - 2
				//   salt_seq = Seq - 1   verifier_seq = Seq
				// from consecutive Interlocked.Increment calls above. Do NOT insert
				// additional encrypted fields without updating the server's derivation logic.
				Client.Broadcast(new CreateAccountBroadcast()
				{
					Username = encryptedUsername,
					Email = encryptedEmail,
					Age = encryptedAge,
					Salt = encryptedSalt,
					Verifier = encryptedVerifier,
					Seq = createAccountSeq,
				}, Channel.Reliable);
			}
			// Try to login
			else
			{
				byte[] clientEphemeralBytes = Encoding.UTF8.GetBytes(SrpData.ClientEphemeral.Public);
				byte[] encryptedClientEphemeral;
				uint seqClientEphemeral = 0;
				try
				{
					var (nonce, seq) = sendNonceCtx.NextNonce();
					seqClientEphemeral = seq;
					byte[] aadEphemeral = CryptoHelper.BuildAad((byte)CryptoHelper.AuthMessageType.SrpVerify, agreedVersion, seqClientEphemeral);
					encryptedClientEphemeral = CryptoHelper.EncryptAES(clientToServerKey, nonce, clientEphemeralBytes, aadEphemeral);
				}
				catch (CryptographicException ex)
				{
					Log.Error("ClientLoginAuthenticator", $"AES encryption failed for client ephemeral: {ex.Message}");
					ClearKeyMaterial();
					Client.ForceDisconnect();
					CryptographicOperations.ZeroMemory(clientEphemeralBytes);
					return;
				}
				CryptographicOperations.ZeroMemory(clientEphemeralBytes);

				// PROTOCOL NOTE — SrpVerifyBroadcast implicit sequence encoding:
				// Seq is the ephemeral's sequence. The server derives:
				//   identifier_seq = Seq - 1   ephemeral_seq = Seq
				// The identifier (username or email) is sent in the S field.
				Client.Broadcast(new SrpVerifyBroadcast()
				{
					S = encryptedUsername,
					PublicEphemeral = encryptedClientEphemeral,
					Seq = seqClientEphemeral,
				}, Channel.Reliable);
			}
		}

		/// <summary>
		/// Handles the SRP verify broadcast, decrypts salt and server ephemeral, and sends client proof.
		/// </summary>
		/// <param name="msg">The SRP verify message.</param>
		/// <param name="channel">The network channel.</param>
		private void OnClientSrpVerifyBroadcastReceived(SrpVerifyBroadcast msg, Channel channel)
		{
			if (SrpData == null)
			{
				return;
			}

			if (srpVerifyProcessed)
			{
				// Ignore duplicate verifies
				return;
			}

			// Guard against messages arriving before handshake establishes encryption.
			if (receiveNonceCtx == null || serverToClientKey == null ||
				sendNonceCtx == null || clientToServerKey == null)
			{
				Client.ForceDisconnect();
				return;
			}

			byte[] decryptedSalt = null;
			byte[] decryptedRawPublicEphemeral = null;
			try
			{
				var (nonce1, rseq) = receiveNonceCtx.NextNonce();
				if (msg.S == null || msg.S.Length > CryptoHelper.MaxSrpPayloadBytes)
				{
					ClearKeyMaterial();
					Client.ForceDisconnect();
					return;
				}
				byte[] aad1 = CryptoHelper.BuildAad((byte)CryptoHelper.AuthMessageType.SrpVerifyResponse, agreedVersion, rseq);
				decryptedSalt = CryptoHelper.DecryptAES(serverToClientKey, nonce1, msg.S, aad1);

				var (nonce2, rseq2) = receiveNonceCtx.NextNonce();
				if (msg.PublicEphemeral == null || msg.PublicEphemeral.Length > CryptoHelper.MaxSrpPayloadBytes)
				{
					ClearKeyMaterial();
					Client.ForceDisconnect();
					return;
				}
				byte[] aad2 = CryptoHelper.BuildAad((byte)CryptoHelper.AuthMessageType.SrpVerifyResponse, agreedVersion, rseq2);
				decryptedRawPublicEphemeral = CryptoHelper.DecryptAES(serverToClientKey, nonce2, msg.PublicEphemeral, aad2);
			}
			catch (CryptographicException)
			{
				Log.Warning("ClientLoginAuthenticator", "AES decryption/authentication failed for SRP verify.");
				ClearKeyMaterial();
				if (decryptedSalt != null) CryptographicOperations.ZeroMemory(decryptedSalt);
				if (decryptedRawPublicEphemeral != null) CryptographicOperations.ZeroMemory(decryptedRawPublicEphemeral);
				Client.ForceDisconnect();
				return;
			}

			if (decryptedSalt == null || decryptedSalt.Length == 0 || decryptedSalt.Length > CryptoHelper.MaxSrpPayloadBytes ||
				decryptedRawPublicEphemeral == null || decryptedRawPublicEphemeral.Length == 0 || decryptedRawPublicEphemeral.Length > CryptoHelper.MaxSrpPayloadBytes)
			{
				CryptographicOperations.ZeroMemory(decryptedSalt);
				CryptographicOperations.ZeroMemory(decryptedRawPublicEphemeral);
				ClearKeyMaterial();
				Client.ForceDisconnect();
				return;
			}

			string salt;
			string publicServerEphemeral;
			try
			{
				salt = CryptoHelper.StrictUtf8.GetString(decryptedSalt);
				publicServerEphemeral = CryptoHelper.StrictUtf8.GetString(decryptedRawPublicEphemeral);
			}
			catch (DecoderFallbackException)
			{
				Log.Warning("ClientLoginAuthenticator", "Malformed UTF-8 in SRP verify response.");
				CryptographicOperations.ZeroMemory(decryptedSalt);
				CryptographicOperations.ZeroMemory(decryptedRawPublicEphemeral);
				ClearKeyMaterial();
				Client.ForceDisconnect();
				return;
			}
			CryptographicOperations.ZeroMemory(decryptedSalt);
			CryptographicOperations.ZeroMemory(decryptedRawPublicEphemeral);

			if (SrpData.GetProof(this.username, this.password, salt, publicServerEphemeral, out string proof))
			{
			// Credentials are no longer needed — clear immediately.
				username = null;
				password = null;
				email = null;
				age = 0;

				byte[] proofBytes = Encoding.UTF8.GetBytes(proof);
				byte[] encryptedProof;
				uint seqProof = 0;
				try
				{
					var (nonce, seq) = sendNonceCtx.NextNonce();
					seqProof = seq;
					byte[] aadProof = CryptoHelper.BuildAad((byte)CryptoHelper.AuthMessageType.SrpProof, agreedVersion, seqProof);
					encryptedProof = CryptoHelper.EncryptAES(clientToServerKey, nonce, proofBytes, aadProof);
				}
				catch (CryptographicException ex)
				{
					Log.Error("ClientLoginAuthenticator", $"AES encryption failed for client proof: {ex.Message}");
					Client.ForceDisconnect();
					CryptographicOperations.ZeroMemory(proofBytes);
					return;
				}
				CryptographicOperations.ZeroMemory(proofBytes);

				Client.Broadcast(new SrpProofBroadcast()
				{
					Proof = encryptedProof,
					Seq = seqProof,
				}, Channel.Reliable);

				// mark proof as sent to ignore duplicates
				srpVerifyProcessed = true;
			}
			else
			{
				username = null;
				password = null;
				email = null;
				age = 0;
				Client.ForceDisconnect();
			}
		}

		/// <summary>
		/// Handles the SRP success broadcast, verifies the client session, and invokes authentication result.
		/// </summary>
		/// <param name="msg">The SRP success message.</param>
		/// <param name="channel">The network channel.</param>
		private void OnClientSrpSuccessBroadcastReceived(SrpSuccessBroadcast msg, Channel channel)
		{
			if (SrpData == null)
			{
				return;
			}

			if (srpSuccessProcessed)
			{
				return;
			}

			// Guard against messages arriving before handshake establishes encryption.
			if (receiveNonceCtx == null || serverToClientKey == null)
			{
				Client.ForceDisconnect();
				return;
			}

			byte[] decryptedProof = null;
			try
			{
				var (nonce, rseq) = receiveNonceCtx.NextNonce();
				if (msg.Proof == null || msg.Proof.Length > CryptoHelper.MaxSrpPayloadBytes)
				{
					ClearKeyMaterial();
					Client.ForceDisconnect();
					return;
				}
				byte[] aadProof = CryptoHelper.BuildAad((byte)CryptoHelper.AuthMessageType.SrpSuccess, agreedVersion, rseq);
				decryptedProof = CryptoHelper.DecryptAES(serverToClientKey, nonce, msg.Proof, aadProof);
			}
			catch (CryptographicException)
			{
				Log.Warning("ClientLoginAuthenticator", "AES decryption/authentication failed for SRP success.");
				ClearKeyMaterial();
				if (decryptedProof != null) CryptographicOperations.ZeroMemory(decryptedProof);
				Client.ForceDisconnect();
				return;
			}

			if (decryptedProof == null || decryptedProof.Length == 0 || decryptedProof.Length > CryptoHelper.MaxSrpPayloadBytes)
			{
				if (decryptedProof != null) CryptographicOperations.ZeroMemory(decryptedProof);
				ClearKeyMaterial();
				Client.ForceDisconnect();
				return;
			}

			string proof;
			try
			{
				proof = CryptoHelper.StrictUtf8.GetString(decryptedProof);
			}
			catch (DecoderFallbackException)
			{
				Log.Warning("ClientLoginAuthenticator", "Malformed UTF-8 in SRP success proof.");
				CryptographicOperations.ZeroMemory(decryptedProof);
				ClearKeyMaterial();
				Client.ForceDisconnect();
				return;
			}
			CryptographicOperations.ZeroMemory(decryptedProof);

			// Verify the client session
			if (SrpData.Verify(proof, out string result))
			{
				// Latch the guard immediately to prevent a same-frame duplicate from
				// re-entering this block (single-threaded Unity, but belt-and-suspenders).
				srpSuccessProcessed = true;

				// Extract and store the auth token for World/Scene server authentication.
				// TOKEN DECRYPT FAILURE NOTE: If token decryption fails (CryptographicException),
				// we still report LoginSuccess to the client because the SRP proof was verified —
				// the user IS authenticated at the Login server. The token is only needed later for
				// World/Scene server authentication. A missing token means the user will be unable
				// to connect to game servers but the login itself is valid. The warning log below
				// alerts operators. The client could implement a retry or re-login on World server
				// rejection when storedAuthToken is null.
				//
				// NONCE GUARD: The proof nonce (receiveNonceCtx.NextNonce above) is consumed
				// unconditionally because the server always encrypts the proof in SrpSuccessBroadcast.
				// The token nonce is conditional — only consumed when the server signals LoginSuccess
				// and includes an encrypted token. For non-success results (e.g., AlreadyOnline) the
				// server omits the token, so decrypting here would advance receiveNonceCtx and desync.
				if (msg.Result == ClientAuthenticationResult.LoginSuccess &&
					msg.Token != null && msg.Token.Length > 0)
				{
					try
					{
						var (tokenNonce, tokenRseq) = receiveNonceCtx.NextNonce();
						byte[] tokenAad = CryptoHelper.BuildAad((byte)CryptoHelper.AuthMessageType.SrpSuccess, agreedVersion, tokenRseq);
						storedAuthToken = CryptoHelper.DecryptAES(serverToClientKey, tokenNonce, msg.Token, tokenAad);
					}
					catch (CryptographicException tokenEx)
					{
						Log.Warning("ClientLoginAuthenticator", $"Failed to decrypt auth token (non-fatal): {tokenEx.Message}");
						storedAuthToken = null;
					}
				}

				// Invoke result on the client
				OnClientAuthenticationResult?.Invoke(msg.Result);
				Log.Debug("ClientLoginAuthenticator", msg.Result.ToString());
				// Clear SRP state now that authentication decision has been made.
				SrpData.Clear();
				SrpData = null;
			}
			else
			{
				Client.ForceDisconnect();
			}
		}

		/// <summary>
		/// Handles the authentication result broadcast from the server and invokes the client authentication result event.
		/// </summary>
		/// <param name="msg">The authentication result message.</param>
		/// <param name="channel">The network channel.</param>
		private void OnClientAuthResultBroadcastReceived(ClientAuthResultBroadcast msg, Channel channel)
		{
			// Clear the stored token on terminal token failures to prevent infinite retry loops.
			if (msg.Result == ClientAuthenticationResult.TokenInvalid ||
				msg.Result == ClientAuthenticationResult.TokenExpired ||
				msg.Result == ClientAuthenticationResult.TokenRevoked)
			{
				ClearAuthToken();
			}

			// Invoke result on the client
			OnClientAuthenticationResult?.Invoke(msg.Result);
			Log.Debug("ClientLoginAuthenticator", msg.Result.ToString());
		}

		/// <summary>
		/// Zeroes all sensitive key material and resets counters.
		/// Does NOT clear storedAuthToken — it persists across connections.
		/// </summary>
		private void ClearKeyMaterial()
		{
			ephemeralKeyPair?.Dispose();
			ephemeralKeyPair = null;
			SrpData?.Clear();
			SrpData = null;
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
		/// Clears the stored authentication token and zeroes its memory.
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
		/// Encrypts and sends an account verification code to the server on the current connection.
		/// Must be called while the handshake-established encryption session is still active.
		/// </summary>
		/// <param name="username">The account username to verify.</param>
		/// <param name="verifyCode">The verification code entered by the user.</param>
		public void SendVerifyCode(string username, string verifyCode)
		{
			if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(verifyCode))
			{
				return;
			}

			if (sendNonceCtx == null || clientToServerKey == null)
			{
				Client.ForceDisconnect();
				return;
			}

			byte[] usernameBytes = Encoding.UTF8.GetBytes(username);
			byte[] codeBytes = Encoding.UTF8.GetBytes(verifyCode);
			byte[] encryptedUsername;
			byte[] encryptedCode;
			uint accountVerifySeq;
			try
			{
				var (nonceU, seqU) = sendNonceCtx.NextNonce();
				byte[] aadU = CryptoHelper.BuildAad((byte)CryptoHelper.AuthMessageType.AccountVerify, agreedVersion, seqU);
				encryptedUsername = CryptoHelper.EncryptAES(clientToServerKey, nonceU, usernameBytes, aadU);

				var (nonceC, seqC) = sendNonceCtx.NextNonce();
				accountVerifySeq = seqC;
				byte[] aadC = CryptoHelper.BuildAad((byte)CryptoHelper.AuthMessageType.AccountVerify, agreedVersion, seqC);
				encryptedCode = CryptoHelper.EncryptAES(clientToServerKey, nonceC, codeBytes, aadC);
			}
			catch (CryptographicException ex)
			{
				Log.Error("ClientLoginAuthenticator", $"AES encryption failed for verify code: {ex.Message}");
				ClearKeyMaterial();
				Client.ForceDisconnect();
				CryptographicOperations.ZeroMemory(usernameBytes);
				CryptographicOperations.ZeroMemory(codeBytes);
				return;
			}
			CryptographicOperations.ZeroMemory(usernameBytes);
			CryptographicOperations.ZeroMemory(codeBytes);

			// PROTOCOL NOTE — AccountVerifyBroadcast implicit sequence encoding:
			// Seq is the verifyCode's sequence. The server derives:
			//   username_seq = Seq - 1   verifyCode_seq = Seq
			Client.Broadcast(new AccountVerifyBroadcast()
			{
				Username = encryptedUsername,
				VerifyCode = encryptedCode,
				Seq = accountVerifySeq,
			}, Channel.Reliable);
		}

		/// <summary>
		/// Handles the 2FA setup broadcast from the server during account creation.
		/// Decrypts the otpauth URI and recovery codes, then fires the setup event.
		/// </summary>
		private void OnClientTwoFactorSetupBroadcastReceived(TwoFactorSetupBroadcast msg, Channel channel)
		{
			if (receiveNonceCtx == null || serverToClientKey == null)
			{
				return;
			}

			if (msg.OtpauthUri == null || msg.OtpauthUri.Length == 0 ||
				msg.RecoveryCodes == null || msg.RecoveryCodes.Length == 0)
			{
				return;
			}

			byte[] decryptedUri = null;
			byte[] decryptedCodes = null;
			try
			{
				var (nonce1, rseq1) = receiveNonceCtx.NextNonce();
				byte[] aad1 = CryptoHelper.BuildAad((byte)CryptoHelper.AuthMessageType.TwoFactorSetup, agreedVersion, rseq1);
				decryptedUri = CryptoHelper.DecryptAES(serverToClientKey, nonce1, msg.OtpauthUri, aad1);

				var (nonce2, rseq2) = receiveNonceCtx.NextNonce();
				byte[] aad2 = CryptoHelper.BuildAad((byte)CryptoHelper.AuthMessageType.TwoFactorSetup, agreedVersion, rseq2);
				decryptedCodes = CryptoHelper.DecryptAES(serverToClientKey, nonce2, msg.RecoveryCodes, aad2);
			}
			catch (CryptographicException)
			{
				Log.Warning("ClientLoginAuthenticator", "AES decryption failed for 2FA setup data.");
				if (decryptedUri != null) CryptographicOperations.ZeroMemory(decryptedUri);
				if (decryptedCodes != null) CryptographicOperations.ZeroMemory(decryptedCodes);
				return;
			}

			string otpauthUri;
			string[] recoveryCodes;
			try
			{
				otpauthUri = CryptoHelper.StrictUtf8.GetString(decryptedUri);
				string codesStr = CryptoHelper.StrictUtf8.GetString(decryptedCodes);
				recoveryCodes = codesStr.Split('\n');
			}
			catch (DecoderFallbackException)
			{
				Log.Warning("ClientLoginAuthenticator", "Malformed UTF-8 in 2FA setup data.");
				CryptographicOperations.ZeroMemory(decryptedUri);
				CryptographicOperations.ZeroMemory(decryptedCodes);
				return;
			}
			CryptographicOperations.ZeroMemory(decryptedUri);
			CryptographicOperations.ZeroMemory(decryptedCodes);

			OnTwoFactorSetupReceived?.Invoke(otpauthUri, recoveryCodes);
		}

		/// <summary>
		/// Encrypts and sends a TOTP code to the server for two-factor verification during login.
		/// </summary>
		/// <param name="code">The 6-digit TOTP code from the authenticator app.</param>
		public void SendTotpCode(string code)
		{
			if (string.IsNullOrEmpty(code))
			{
				return;
			}

			if (sendNonceCtx == null || clientToServerKey == null)
			{
				Client.ForceDisconnect();
				return;
			}

			byte[] codeBytes = Encoding.UTF8.GetBytes(code);
			byte[] encryptedCode;
			uint totpSeq;
			try
			{
				var (nonce, seq) = sendNonceCtx.NextNonce();
				totpSeq = seq;
				byte[] aad = CryptoHelper.BuildAad((byte)CryptoHelper.AuthMessageType.TwoFactorVerify, agreedVersion, totpSeq);
				encryptedCode = CryptoHelper.EncryptAES(clientToServerKey, nonce, codeBytes, aad);
			}
			catch (CryptographicException ex)
			{
				Log.Error("ClientLoginAuthenticator", $"AES encryption failed for TOTP code: {ex.Message}");
				ClearKeyMaterial();
				Client.ForceDisconnect();
				CryptographicOperations.ZeroMemory(codeBytes);
				return;
			}
			CryptographicOperations.ZeroMemory(codeBytes);

			Client.Broadcast(new TwoFactorVerifyBroadcast()
			{
				Code = encryptedCode,
				Seq = totpSeq,
			}, Channel.Reliable);
		}

		/// <summary>
		/// Returns the current login identifier (username or email) if still set.
		/// Used by UI controls to defer identifier capture until the server responds.
		/// Returns null after credentials are cleared (post-SRP proof or disconnect).
		/// </summary>
		public string PendingLoginIdentifier => username;

		/// <summary>
		/// Returns whether the client has a stored authentication token for World/Scene server connections.
		/// </summary>
		public bool HasAuthToken => storedAuthToken != null;
	}
}