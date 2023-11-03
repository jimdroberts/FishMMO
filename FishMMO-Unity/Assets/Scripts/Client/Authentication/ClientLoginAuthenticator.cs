using FishNet.Authenticating;
using FishNet.Connection;
using FishNet.Managing.Logging;
using FishNet.Managing;
using FishNet.Transporting;
using System;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
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

		public const int AccountNameMinLength = 3;
		public const int AccountNameMaxLength = 32;

		public const int AccountPasswordMinLength = 3;
		public const int AccountPasswordMaxLength = 32;

		public virtual bool IsAllowedUsername(string accountName)
		{
			return accountName.Length >= AccountNameMinLength &&
				   accountName.Length <= AccountNameMaxLength &&
				   Regex.IsMatch(accountName, @"^[a-zA-Z0-9_]+$");
		}

		public virtual bool IsAllowedPassword(string accountPassword)
		{
			return accountPassword.Length >= AccountNameMinLength &&
				   accountPassword.Length <= AccountNameMaxLength &&
				   Regex.IsMatch(accountPassword, @"^[a-zA-Z0-9_]+$");
		}

		public override void InitializeOnce(NetworkManager networkManager)
		{
			base.InitializeOnce(networkManager);

			base.NetworkManager.ClientManager.OnClientConnectionState += ClientManager_OnClientConnectionState;
			base.NetworkManager.ClientManager.RegisterBroadcast<SRPVerifyBroadcast>(OnClientSRPVerifyBroadcastReceived);
			base.NetworkManager.ClientManager.RegisterBroadcast<SRPProofBroadcast>(OnClientSRPProofBroadcastReceived);
			base.NetworkManager.ClientManager.RegisterBroadcast<ClientAuthResultBroadcast>(OnClientAuthResultBroadcastReceived);
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
			/* If anything but the started state then exit early.
			 * Only try to authenticate on started state. The server
			* doesn't have to send an authentication request before client
			* can authenticate, that is entirely optional and up to you. In this
			* example the client tries to authenticate soon as they connect. */
			if (args.ConnectionState != LocalConnectionState.Started)
				return;

			SrpData = new ClientSrpData(SrpParameters.Create2048<SHA512>());

			// register a new account?
			if (register)
			{
				SrpData.GetSaltAndVerifier(username, password, out string salt, out string verifier);
				CreateAccountBroadcast msg = new CreateAccountBroadcast()
				{
					username = this.username,
					salt = salt,
					verifier = verifier,
				};
				base.NetworkManager.ClientManager.Broadcast(msg, Channel.Reliable);
			}
			else
			{
				SRPVerifyBroadcast msg = new SRPVerifyBroadcast()
				{
					s = this.username,
					publicEphemeral = SrpData.ClientEphemeral.Public,
				};
				base.NetworkManager.ClientManager.Broadcast(msg, Channel.Reliable);
			}
		}

		private void OnClientSRPVerifyBroadcastReceived(SRPVerifyBroadcast msg)
		{
			if (SrpData == null)
			{
				return;
			}

			if (SrpData.GetProof(this.username, this.password, msg.s, msg.publicEphemeral, out string proof))
			{
				base.NetworkManager.ClientManager.Broadcast(new SRPProofBroadcast()
				{
					proof = proof,
				}, Channel.Reliable);
			}
			else
			{
				Client.ForceDisconnect();
			}
			//Debug.Log("SRP: " + proof);
		}

		private void OnClientSRPProofBroadcastReceived(SRPProofBroadcast msg)
		{
			if (SrpData == null)
			{
				return;
			}

			if (SrpData.Verify(msg.proof, out string result))
			{
				base.NetworkManager.ClientManager.Broadcast(new SRPSuccess(), Channel.Reliable);
			}
			else
			{
				Client.ForceDisconnect();
			}
			//Debug.Log("SRP: " + result);
		}

		/// <summary>
		/// Received on client after server sends an authentication response.
		/// </summary>
		private void OnClientAuthResultBroadcastReceived(ClientAuthResultBroadcast msg)
		{
			// invoke result on the client
			OnClientAuthenticationResult(msg.result);

			if (NetworkManager.CanLog(LoggingType.Common))
				Debug.Log(msg.result);
		}
	}
}