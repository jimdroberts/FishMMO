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
		private string username = "";
		private string password = "";
		private bool register;
		private RSA rsa;
		private byte[] symmetricKey;
		private byte[] iv;
		private ClientSrpData SrpData;

		/// <summary>
		/// Client authentication event. Subscribe to this if you want something to happen after receiving authentication result from the server.
		/// </summary>
		public event Action<ClientAuthenticationResult> OnClientAuthenticationResult;

		/// <summary>
		/// We override this but never use it on the client...
		/// </summary>
#pragma warning disable CS0067
		public override event Action<NetworkConnection, bool> OnAuthenticationResult;
#pragma warning restore CS0067

		public Client Client { get; private set; }

		public override void InitializeOnce(NetworkManager networkManager)
		{
			base.InitializeOnce(networkManager);

			base.NetworkManager.ClientManager.OnClientConnectionState += ClientManager_OnClientConnectionState;
			base.NetworkManager.ClientManager.RegisterBroadcast<ServerHandshake>(OnClientServerHandshakeBroadcastReceived);
			base.NetworkManager.ClientManager.RegisterBroadcast<SrpVerifyBroadcast>(OnClientSrpVerifyBroadcastReceived);
			base.NetworkManager.ClientManager.RegisterBroadcast<SrpSuccessBroadcast>(OnClientSrpSuccessBroadcastReceived);
			base.NetworkManager.ClientManager.RegisterBroadcast<ClientAuthResultBroadcast>(OnClientAuthResultBroadcastReceived);
		}

		private void OnDestroy()
		{
			if (rsa != null)
			{
				rsa.Dispose();
				rsa = null;
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void SetClient(Client client)
		{
			Client = client;
		}

		/// <summary>
		/// Initial sign in to the login server.
		/// </summary>
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
		private void ClientManager_OnClientConnectionState(ClientConnectionStateArgs args)
		{
			if (args.ConnectionState == LocalConnectionState.Stopping ||
				args.ConnectionState == LocalConnectionState.Stopped)
			{
				if (rsa != null)
				{
					rsa.Dispose();
					rsa = null;
				}
			}

			if (args.ConnectionState != LocalConnectionState.Started)
				return;

			rsa = RSA.Create(2048);
			byte[] publicKey = CryptoHelper.ExportPublicKey(rsa);

			// Initiate a handshake with the server
			Client.Broadcast(new ClientHandshake()
			{
				PublicKey = publicKey,
			}, Channel.Reliable);
		}

		private void OnClientServerHandshakeBroadcastReceived(ServerHandshake msg, Channel channel)
		{
			if (msg.Key == null ||
				msg.IV == null)
			{
				Client.ForceDisconnect();
				return;
			}

			symmetricKey = rsa.Decrypt(msg.Key, RSAEncryptionPadding.Pkcs1);
			iv = rsa.Decrypt(msg.IV, RSAEncryptionPadding.Pkcs1);

			SrpData = new ClientSrpData(SrpParameters.Create2048<SHA512>());

			// Encrypt the username before sending
			byte[] encryptedUsername = CryptoHelper.EncryptAES(symmetricKey, iv, Encoding.UTF8.GetBytes(this.username));

			// Register a new account
			if (register)
			{
				SrpData.GetSaltAndVerifier(username, password, out string salt, out string verifier);

				// Encrypt the salt and verifier before sending
				byte[] encryptedSalt = CryptoHelper.EncryptAES(symmetricKey, iv, Encoding.UTF8.GetBytes(salt));
				byte[] encryptedVerifier = CryptoHelper.EncryptAES(symmetricKey, iv, Encoding.UTF8.GetBytes(verifier));

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
				byte[] encryptedClientEphemeral = CryptoHelper.EncryptAES(symmetricKey, iv, Encoding.UTF8.GetBytes(SrpData.ClientEphemeral.Public));

				Client.Broadcast(new SrpVerifyBroadcast()
				{
					S = encryptedUsername,
					PublicEphemeral = encryptedClientEphemeral,
				}, Channel.Reliable);
			}
		}

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
		private void OnClientAuthResultBroadcastReceived(ClientAuthResultBroadcast msg, Channel channel)
		{
			// Invoke result on the client
			OnClientAuthenticationResult(msg.Result);
			Log.Debug("ClientLoginAuthenticator", msg.Result.ToString());
		}
	}
}