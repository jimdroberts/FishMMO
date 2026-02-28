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
		/// </summary>
		/// <param name="username">The username.</param>
		/// <param name="password">The password.</param>
		/// <param name="register">True to register a new account; false to login.</param>
		public void SetLoginCredentials(string username, string password, bool register = false)
		{
			this.username = username;
			this.password = password;
			this.register = register;
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
				// Client send = client→server = serverToClient: false
				// Client receive = server→client = serverToClient: true
				sendNonceCtx = new CryptoHelper.GcmNonceContext(sessionKeys.ClientPrefix, serverToClient: false);
				receiveNonceCtx = new CryptoHelper.GcmNonceContext(sessionKeys.ServerPrefix, serverToClient: true);

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

			// Pre-validate username length to fail fast before consuming a sequence number.
			// Server-side IsAllowedUsername enforces 3-32 chars; this client-side check
			// prevents sending payloads that will be rejected anyway.
			if (string.IsNullOrEmpty(this.username) || this.username.Length < 3 || this.username.Length > 32)
			{
				Log.Warning("ClientLoginAuthenticator", "Username is empty or outside allowed length (3-32 characters).");
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
				byte[] aad = CryptoHelper.BuildAad((byte)CryptoHelper.AuthMessageType.SrpVerify, agreedVersion, seq);
				encryptedUsername = CryptoHelper.EncryptAES(clientToServerKey, nonce, usernameBytes, aad);
				// include Seq in broadcast
				// Note: CreateAccount/SrpVerify will get Seq set below when broadcasting
			}
			catch (CryptographicException ex)
			{
				Log.Error("ClientLoginAuthenticator", $"AES encryption failed for username: {ex.Message}");
				ClearKeyMaterial();
				Client.ForceDisconnect();
				CryptographicOperations.ZeroMemory(usernameBytes);
				return;
			}
			CryptographicOperations.ZeroMemory(usernameBytes);

			// Register a new account
			if (register)
			{
				SrpData.GetSaltAndVerifier(username, password, out string salt, out string verifier);

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
				//   username_seq = Seq - 2   salt_seq = Seq - 1   verifier_seq = Seq
				// from consecutive Interlocked.Increment calls above. Do NOT insert
				// additional encrypted fields between username and verifier without
				// updating the server’s derivation logic.
				Client.Broadcast(new CreateAccountBroadcast()
				{
					Username = encryptedUsername,
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
				// Extract and store the auth token for World/Scene server authentication.
				if (msg.Token != null && msg.Token.Length > 0)
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
				srpSuccessProcessed = true;
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
		/// Returns whether the client has a stored authentication token for World/Scene server connections.
		/// </summary>
		public bool HasAuthToken => storedAuthToken != null;
	}
}