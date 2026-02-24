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
		/// Initialization vector for AES encryption after handshake.
		/// </summary>
		private byte[] iv;
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
				msg.IV == null)
			{
				Client.ForceDisconnect();
				return;
			}
			try
			{
				symmetricKey = CryptoHelper.DecryptRsaOaepSha256(rsaKeyPair.Private, msg.Key);
				iv = CryptoHelper.DecryptRsaOaepSha256(rsaKeyPair.Private, msg.IV);
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
			if (symmetricKey == null || symmetricKey.Length != 32 || iv == null || iv.Length != 12)
			{
				Log.Warning("ClientLoginAuthenticator", "Invalid symmetric key or IV received from server.");
				Client.ForceDisconnect();
				return;
			}

			SrpData = new ClientSrpData(SrpParameters.Create2048<SHA512>());

			// Encrypt the username before sending
			byte[] usernameBytes = Encoding.UTF8.GetBytes(this.username);
			byte[] encryptedUsername;
			try
			{
				encryptedUsername = CryptoHelper.EncryptAES(symmetricKey, iv, usernameBytes);
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
					encryptedSalt = CryptoHelper.EncryptAES(symmetricKey, iv, saltBytes);
					encryptedVerifier = CryptoHelper.EncryptAES(symmetricKey, iv, verifierBytes);
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
					encryptedClientEphemeral = CryptoHelper.EncryptAES(symmetricKey, iv, clientEphemeralBytes);
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

			byte[] decryptedSalt = CryptoHelper.DecryptAES(symmetricKey, iv, msg.S);
			byte[] decryptedRawPublicEphemeral = CryptoHelper.DecryptAES(symmetricKey, iv, msg.PublicEphemeral);

			string salt = Encoding.UTF8.GetString(decryptedSalt);
			string publicServerEphemeral = Encoding.UTF8.GetString(decryptedRawPublicEphemeral);

			if (SrpData.GetProof(this.username, this.password, salt, publicServerEphemeral, out string proof))
			{
				byte[] encryptedProof = CryptoHelper.EncryptAES(symmetricKey, iv, Encoding.UTF8.GetBytes(proof));

				Client.Broadcast(new SrpProofBroadcast()
				{
					Proof = encryptedProof,
				}, Channel.Reliable);
			}
			else
			{
				Client.ForceDisconnect();
			}
			//Log.Debug("ClientLoginAuthenticator", "Srp: " + proof);
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

			byte[] decryptedProof = CryptoHelper.DecryptAES(symmetricKey, iv, msg.Proof);

			string proof = Encoding.UTF8.GetString(decryptedProof);

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
			//Log.Debug("ClientLoginAuthenticator", "Srp: " + result);
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
	}
}