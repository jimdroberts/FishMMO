using FishNet.Authenticating;
using FishNet.Connection;
using FishNet.Managing.Logging;
using FishNet.Managing;
using FishNet.Transporting;
using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using SecureRemotePassword;
using FishMMO.Shared;

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
			base.NetworkManager.ClientManager.RegisterBroadcast<SrpProofBroadcast>(OnClientSrpProofBroadcastReceived);
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
			if (args.ConnectionState == LocalConnectionState.Stopping)
			{
				if (rsa != null)
				{
					rsa.Dispose();
					rsa = null;
				}
			}

			/* If anything but the started state then exit early.
			 * Only try to authenticate on started state. The server
			* doesn't have to send an authentication request before client
			* can authenticate, that is entirely optional and up to you. In this
			* example the client tries to authenticate soon as they connect. */
			if (args.ConnectionState != LocalConnectionState.Started)
				return;

			rsa = RSA.Create(2048);
			byte[] publicKey = CryptoHelper.ExportPublicKey(rsa);

			// Initiate a handshake with the server
			Client.Broadcast(new ClientHandshake()
			{
				publicKey = publicKey,
			}, Channel.Reliable);
		}

		private void OnClientServerHandshakeBroadcastReceived(ServerHandshake msg, Channel channel)
		{
			if (msg.key == null ||
				msg.iv == null)
			{
				Client.ForceDisconnect();
				return;
			}

			symmetricKey = rsa.Decrypt(msg.key, RSAEncryptionPadding.Pkcs1);
			iv = rsa.Decrypt(msg.iv, RSAEncryptionPadding.Pkcs1);

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
					username = encryptedUsername,
					salt = encryptedSalt,
					verifier = encryptedVerifier,
				}, Channel.Reliable);
			}
			// Try to login
			else
			{
				Client.Broadcast(new SrpVerifyBroadcast()
				{
					s = encryptedUsername,
					publicEphemeral = SrpData.ClientEphemeral.Public,
				}, Channel.Reliable);
			}
		}

		private void OnClientSrpVerifyBroadcastReceived(SrpVerifyBroadcast msg, Channel channel)
		{
			if (SrpData == null)
			{
				return;
			}

			string salt = Convert.ToBase64String(msg.s);

			if (SrpData.GetProof(this.username, this.password, salt, msg.publicEphemeral, out string proof))
			{
				Client.Broadcast(new SrpProofBroadcast()
				{
					proof = proof,
				}, Channel.Reliable);
			}
			else
			{
				Client.ForceDisconnect();
			}
			//Debug.Log("Srp: " + proof);
		}

		private void OnClientSrpProofBroadcastReceived(SrpProofBroadcast msg, Channel channel)
		{
			if (SrpData == null)
			{
				return;
			}

			if (SrpData.Verify(msg.proof, out string result))
			{
				Client.Broadcast(new SrpSuccess(), Channel.Reliable);
			}
			else
			{
				Client.ForceDisconnect();
			}
			//Debug.Log("Srp: " + result);
		}

		/// <summary>
		/// Received on client after server sends an authentication response.
		/// </summary>
		private void OnClientAuthResultBroadcastReceived(ClientAuthResultBroadcast msg, Channel channel)
		{
			// invoke result on the client
			OnClientAuthenticationResult(msg.result);

			if (NetworkManagerExtensions.CanLog(LoggingType.Common))
				Debug.Log(msg.result);
		}
	}
}