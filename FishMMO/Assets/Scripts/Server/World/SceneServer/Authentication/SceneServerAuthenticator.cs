using FishNet.Managing;
using FishNet.Managing.Logging;
using FishNet.Transporting;
using FishMMO_DB;
using System;
using Server.Services;
using UnityEngine;

namespace Server
{
	// Scene Server Authenticator, Scene Authenticator connects with basic password authentication.
	public class SceneServerAuthenticator : LoginServerAuthenticator
	{
		/// <summary>
		/// Scene Server authentication event. Subscribe to this if you want something to happen after receiving authentication result from the Relay Server.
		/// </summary>
		public event Action OnSceneServerAuthenticationResult;

		public override void InitializeOnce(NetworkManager networkManager)
		{
			base.InitializeOnce(networkManager);

			// Listen for internal server broadcasts
			base.NetworkManager.ClientManager.OnClientConnectionState += ClientManager_OnSceneServerConnectionState;
			base.NetworkManager.ClientManager.RegisterBroadcast<SceneServerAuthResultBroadcast>(OnSceneServerAuthResultBroadcastReceived);
		}

		/// <summary>
		/// Called when a connection state changes for the Scene Server.
		/// We wait for the connection to be ready before proceeding with authentication.
		/// </summary>
		private void ClientManager_OnSceneServerConnectionState(ClientConnectionStateArgs args)
		{
			/* If anything but the started state then exit early.
			 * Only try to authenticate on started state. The server
			* doesn't have to send an authentication request before client
			* can authenticate, that is entirely optional and up to you. In this
			* example the client tries to authenticate soon as they connect. */
			if (args.ConnectionState != LocalConnectionState.Started)
				return;

			// send the Relay Server an auth request
			SceneServerAuthBroadcast msg = new SceneServerAuthBroadcast()
			{
				password = RelayServerAuthenticator.RelayServerPassword,
			};
			base.NetworkManager.ClientManager.Broadcast(msg);
		}

		/// <summary>
		/// Received on Scene after Relay Server sends an authentication response.
		/// </summary>
		private void OnSceneServerAuthResultBroadcastReceived(SceneServerAuthResultBroadcast msg)
		{
			// invoke result on the server
			OnSceneServerAuthenticationResult?.Invoke();

			if (base.NetworkManager.CanLog(LoggingType.Common))
				Debug.Log(msg);
		}

		/// <summary>
		/// Executed when a player tries to login to the Scene Server.
		/// </summary>
		internal override ClientAuthResultBroadcast TryLogin(string username, string password)
		{
			ClientAuthenticationResult result = ClientAuthenticationResult.InvalidUsernameOrPassword;

			// if the username is valid get OR create the account for the client
			if (DBContextFactory != null && IsAllowedUsername(username))
			{
				using ServerDbContext dbContext = DBContextFactory.CreateDbContext(new string[] { });
				if (!CharacterService.TryGetOnlineCharacter(dbContext, username))
				{
					result = AccountService.TryLogin(dbContext, username, password);
				}
				else
				{
					result = ClientAuthenticationResult.AlreadyOnline;
				}
			}

			// this is easier...
			if (result == ClientAuthenticationResult.LoginSuccess) result = ClientAuthenticationResult.SceneLoginSuccess;

			return new ClientAuthResultBroadcast() { result = result, };
		}
	}
}