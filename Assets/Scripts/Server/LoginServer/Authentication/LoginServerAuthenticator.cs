using FishNet.Authenticating;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using System;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Server.Services;
using UnityEngine;

namespace Server
{
	// Login Server Authenticator that allows Clients to connect with basic password authentication.
	public class LoginServerAuthenticator : Authenticator
	{
		/// <summary>
		/// Server Authentication event. Subscribe to this if you want something to happen immediately after client authentication success.
		/// </summary>
		public override event Action<NetworkConnection, bool> OnAuthenticationResult;
		public event Action<NetworkConnection, bool> OnClientAuthenticationResult;

		public const int accountNameMinLength = 3;
		public const int accountNameMaxLength = 32;

		public virtual bool IsAllowedUsername(string accountName)
		{
			return !string.IsNullOrWhiteSpace(accountName) && 
				   accountName.Length >= accountNameMinLength &&
				   accountName.Length <= accountNameMaxLength &&
				   Regex.IsMatch(accountName, @"^[a-zA-Z0-9_]+$");
		}

		public override void InitializeOnce(NetworkManager networkManager)
		{
			base.InitializeOnce(networkManager);

			networkManager.ServerManager.OnRemoteConnectionState += ServerManager_OnRemoteConnectionState;

			// Listen for broadcast from clients.
			networkManager.ServerManager.RegisterBroadcast<BeginClientAuthBroadcast>(OnServerBeginClientAuthBroadcastReceived, false);
		}

		/// <summary>
		/// Received on server when a Client sends the password broadcast message.
		/// </summary>
		internal void OnServerBeginClientAuthBroadcastReceived(NetworkConnection conn, BeginClientAuthBroadcast msg)
		{
			/* If client is already authenticated this could be an attack. Connections
			 * are removed when a client disconnects so there is no reason they should
			 * already be considered authenticated. */
			if (conn.Authenticated)
			{
				conn.Disconnect(true);
				return;
			}

			// attempt login authentication and return a result broadcast
			ClientAuthResultBroadcast rb = TryLogin(msg.username, msg.password);

			bool authenticated = rb.result != ClientAuthenticationResult.InvalidUsernameOrPassword &&
								 rb.result != ClientAuthenticationResult.Banned;

			// add the connection and account to the AccountManager
			if (authenticated)
				AccountManager.AddConnectionAccount(conn, msg.username);

			// tell the connecting client the result of the authentication
			base.NetworkManager.ServerManager.Broadcast(conn, rb, false);

			/* Invoke result. This is handled internally to complete the connection authentication or kick client.
			 * It's important to call this after sending the broadcast so that the broadcast
			 * makes it out to the client before the kick. */

			OnAuthentication(conn, authenticated);
			OnClientAuthenticationResult?.Invoke(conn, authenticated);
		}

		public virtual void OnAuthentication(NetworkConnection conn, bool authenticated)
		{
			OnAuthenticationResult?.Invoke(conn, authenticated);
		}

		/// <summary>
		/// Login Server TryLogin function.
		/// </summary>
		internal virtual ClientAuthResultBroadcast TryLogin(string username, string password)
		{
			ClientAuthenticationResult result = ClientAuthenticationResult.InvalidUsernameOrPassword;

			// if the username is valid get OR create the account for the client
			if (IsAllowedUsername(username))
			{
				// TODO: when the db context factory is passed from the server, use it instead
				var dbContextFactory = new ServerDbContextFactory();
				using var dbContext = dbContextFactory.CreateDbContext(new string[] {});
				
				result = AccountService.TryLogin(dbContext, username, password);
			}

			return new ClientAuthResultBroadcast() { result = result, };
		}

		// remove the connection from the AccountManager
		private void ServerManager_OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
		{
			if (args.ConnectionState == RemoteConnectionState.Stopped)
			{
				AccountManager.RemoveConnectionAccount(conn);
			}
		}
	}
}