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
using Org.BouncyCastle.Crypto;

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
		private string password = "";
		/// <summary>
		/// Indicates whether the client is registering a new account.
		/// </summary>
		private bool register;
		/// <summary>
		/// RSA key pair for asymmetric encryption/decryption during handshake (BouncyCastle, no dispose needed).
		/// </summary>
		private AsymmetricCipherKeyPair rsaKeyPair;
		/// <summary>
		/// Symmetric key used for AES encryption after handshake.
		/// </summary>
		private byte[] symmetricKey;
		/// <summary>
		/// 4-byte random session prefix for GCM nonce derivation.
		/// </summary>
		private byte[] sessionPrefix;
		/// <summary>
		/// Monotonic counter for client→server (encrypt) nonces.
		/// </summary>
		private uint sendCounter;
		/// <summary>
		/// Monotonic counter for server→client (decrypt) nonces.
		/// </summary>
		private uint receiveCounter;
		/// <summary>
		/// SRP data for secure remote password authentication.
		/// </summary>
		private ClientSrpData SrpData;

		/// <summary>
		/// Client authentication event. Subscribe to this if you want something to happen after receiving authentication result from the server.
		/// </summary>
		/// <summary>
		/// Client authentication event. Subscribe to this if you want something to happen after receiving authentication result from the server.
		/// </summary>
		public event Action<ClientAuthenticationResult> OnClientAuthenticationResult;

		/// <summary>
		/// We override this but never use it on the client...
		/// </summary>
#pragma warning disable CS0067
		/// <summary>
		/// Overridden authentication result event (not used on client).
		/// </summary>
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
		/// Unity event called when the object is destroyed. Clears RSA key pair reference.
		/// </summary>
		private void OnDestroy()
		{
			rsaKeyPair = null;
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
		/// Initial sign in to the login server.
		/// </summary>
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
		/// Called when a connection state changes for the local client.
		/// We wait for the connection to be ready before proceeding with authentication.
		/// </summary>
		/// <summary>
		/// Handles client connection state changes. Initiates handshake when connection starts.
		/// </summary>
		/// <param name="args">Connection state arguments.</param>
		private void ClientManager_OnClientConnectionState(ClientConnectionStateArgs args)
		{
			if (args.ConnectionState == LocalConnectionState.Stopping ||
				args.ConnectionState == LocalConnectionState.Stopped)
			{
				rsaKeyPair = null;
				ClearKeyMaterial();
			}

			if (args.ConnectionState != LocalConnectionState.Started)
				return;

			rsaKeyPair = CryptoHelper.GenerateRsaKeyPair();
			byte[] publicKey = CryptoHelper.ExportPublicKey(rsaKeyPair);

			// Initiate a handshake with the server
			Client.Broadcast(new ClientHandshake()
			{
				PublicKey = publicKey,
			}, Channel.Reliable);
		}

		/// <summary>
		/// Handles the server handshake broadcast, decrypts symmetric key and IV, and initiates SRP or registration.
		/// </summary>
		/// <param name="msg">The server handshake message.</param>
		/// <param name="channel">The network channel.</param>
		private void OnClientServerHandshakeBroadcastReceived(ServerHandshake msg, Channel channel)
		{
			if (msg.Key == null ||
				msg.SessionPrefix == null)
			{
				Client.ForceDisconnect();
				return;
			}
			try
			{
				symmetricKey = CryptoHelper.DecryptRsaOaepSha256(rsaKeyPair.Private, msg.Key);
				sessionPrefix = CryptoHelper.DecryptRsaOaepSha256(rsaKeyPair.Private, msg.SessionPrefix);
			}
			catch (CryptographicException ex)
			{
				Log.Warning("ClientLoginAuthenticator", $"RSA decryption failed during handshake: {ex.Message}");
				Client.ForceDisconnect();
				return;
			}

			// Clear private key reference after use to reduce lifetime of sensitive material.
			rsaKeyPair = null;

			// Validate sizes
			if (symmetricKey == null || symmetricKey.Length != 32 ||
				sessionPrefix == null || sessionPrefix.Length != CryptoHelper.SessionPrefixLength)
			{
				Log.Warning("ClientLoginAuthenticator", "Invalid symmetric key or session prefix received from server.");
				Client.ForceDisconnect();
				return;
			}

			// Reset counters for the new session.
			sendCounter = 0;
			receiveCounter = 0;

			SrpData = new ClientSrpData(SrpParameters.Create2048<SHA512>());

			// Encrypt the username before sending
			byte[] usernameBytes = Encoding.UTF8.GetBytes(this.username);
			byte[] encryptedUsername;
			try
			{
				encryptedUsername = CryptoHelper.EncryptAES(symmetricKey, NextSendNonce(), usernameBytes);
			}
			catch (CryptographicException ex)
			{
				Log.Error("ClientLoginAuthenticator", $"AES encryption failed for username: {ex.Message}");
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
				try
				{
					encryptedSalt = CryptoHelper.EncryptAES(symmetricKey, NextSendNonce(), saltBytes);
					encryptedVerifier = CryptoHelper.EncryptAES(symmetricKey, NextSendNonce(), verifierBytes);
				}
				catch (CryptographicException ex)
				{
					Log.Error("ClientLoginAuthenticator", $"AES encryption failed for salt/verifier: {ex.Message}");
					Client.ForceDisconnect();
					CryptographicOperations.ZeroMemory(saltBytes);
					CryptographicOperations.ZeroMemory(verifierBytes);
					return;
				}
				CryptographicOperations.ZeroMemory(saltBytes);
				CryptographicOperations.ZeroMemory(verifierBytes);

				Client.Broadcast(new CreateAccountBroadcast()
				{
					Username = encryptedUsername,
					Salt = encryptedSalt,
					Verifier = encryptedVerifier,
				}, Channel.Reliable);
			}
			// Try to login
			else
			{
				byte[] clientEphemeralBytes = Encoding.UTF8.GetBytes(SrpData.ClientEphemeral.Public);
				byte[] encryptedClientEphemeral;
				try
				{
					encryptedClientEphemeral = CryptoHelper.EncryptAES(symmetricKey, NextSendNonce(), clientEphemeralBytes);
				}
				catch (CryptographicException ex)
				{
					Log.Error("ClientLoginAuthenticator", $"AES encryption failed for client ephemeral: {ex.Message}");
					Client.ForceDisconnect();
					CryptographicOperations.ZeroMemory(clientEphemeralBytes);
					return;
				}
				CryptographicOperations.ZeroMemory(clientEphemeralBytes);

				Client.Broadcast(new SrpVerifyBroadcast()
				{
					S = encryptedUsername,
					PublicEphemeral = encryptedClientEphemeral,
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

			byte[] decryptedSalt;
			byte[] decryptedRawPublicEphemeral;
			try
			{
				decryptedSalt = CryptoHelper.DecryptAES(symmetricKey, NextReceiveNonce(), msg.S);
				decryptedRawPublicEphemeral = CryptoHelper.DecryptAES(symmetricKey, NextReceiveNonce(), msg.PublicEphemeral);
			}
			catch (CryptographicException ex)
			{
				Log.Warning("ClientLoginAuthenticator", $"AES decryption failed for SRP verify: {ex.Message}");
				Client.ForceDisconnect();
				return;
			}

			string salt = Encoding.UTF8.GetString(decryptedSalt);
			string publicServerEphemeral = Encoding.UTF8.GetString(decryptedRawPublicEphemeral);
			CryptographicOperations.ZeroMemory(decryptedSalt);
			CryptographicOperations.ZeroMemory(decryptedRawPublicEphemeral);

			if (SrpData.GetProof(this.username, this.password, salt, publicServerEphemeral, out string proof))
			{
				// Credentials are no longer needed — clear immediately.
				username = null;
				password = null;

				byte[] proofBytes = Encoding.UTF8.GetBytes(proof);
				byte[] encryptedProof;
				try
				{
					encryptedProof = CryptoHelper.EncryptAES(symmetricKey, NextSendNonce(), proofBytes);
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
				}, Channel.Reliable);
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

			byte[] decryptedProof;
			try
			{
				decryptedProof = CryptoHelper.DecryptAES(symmetricKey, NextReceiveNonce(), msg.Proof);
			}
			catch (CryptographicException ex)
			{
				Log.Warning("ClientLoginAuthenticator", $"AES decryption failed for SRP success: {ex.Message}");
				Client.ForceDisconnect();
				return;
			}

			string proof = Encoding.UTF8.GetString(decryptedProof);
			CryptographicOperations.ZeroMemory(decryptedProof);

			// Verify the client session
			if (SrpData.Verify(proof, out string result))
			{
				// Invoke result on the client
				OnClientAuthenticationResult(msg.Result);
				Log.Debug("ClientLoginAuthenticator", msg.Result.ToString());
			}
			else
			{
				Client.ForceDisconnect();
			}
		}

		/// <summary>
		/// Received on client after server sends an authentication response.
		/// </summary>
		/// <summary>
		/// Handles the authentication result broadcast from the server and invokes the client authentication result event.
		/// </summary>
		/// <param name="msg">The authentication result message.</param>
		/// <param name="channel">The network channel.</param>
		private void OnClientAuthResultBroadcastReceived(ClientAuthResultBroadcast msg, Channel channel)
		{
			// Invoke result on the client
			OnClientAuthenticationResult(msg.Result);
			Log.Debug("ClientLoginAuthenticator", msg.Result.ToString());
		}

		/// <summary>
		/// Builds the next client→server GCM nonce and advances <see cref="sendCounter"/>.
		/// </summary>
		/// <exception cref="CryptographicException">Thrown when the counter would overflow.</exception>
		private byte[] NextSendNonce()
		{
			if (sendCounter == uint.MaxValue)
				throw new CryptographicException("AES-GCM send counter exhausted.");
			return CryptoHelper.BuildGcmNonce(sessionPrefix, sendCounter++, serverToClient: false);
		}

		/// <summary>
		/// Builds the next server→client GCM nonce and advances <see cref="receiveCounter"/>.
		/// </summary>
		/// <exception cref="CryptographicException">Thrown when the counter would overflow.</exception>
		private byte[] NextReceiveNonce()
		{
			if (receiveCounter == uint.MaxValue)
				throw new CryptographicException("AES-GCM receive counter exhausted.");
			return CryptoHelper.BuildGcmNonce(sessionPrefix, receiveCounter++, serverToClient: true);
		}

		/// <summary>
		/// Zeroes all sensitive key material and resets counters.
		/// </summary>
		private void ClearKeyMaterial()
		{
			if (symmetricKey != null)
			{
				CryptographicOperations.ZeroMemory(symmetricKey);
				symmetricKey = null;
			}
			if (sessionPrefix != null)
			{
				CryptographicOperations.ZeroMemory(sessionPrefix);
				sessionPrefix = null;
			}
			sendCounter = 0;
			receiveCounter = 0;
			username = null;
			password = null;
		}
	}
}