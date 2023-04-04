using FishNet.Connection;
using FishNet.Managing;
using System;

namespace Server
{
	// Advanced Login Server Authenticator, Relay Authenticator allows Scene Servers to connect with basic password authentication.
	public class RelayServerAuthenticator : LoginServerAuthenticator
	{
		public const string RelayServerPassword = "internal123456";

		/// <summary>
		/// Server Authentication event. Subscribe to this if you want something to happen immediately after authentication success.
		/// </summary>
		public event Action<NetworkConnection, bool> OnRelayAuthenticationResult;

		public override void InitializeOnce(NetworkManager networkManager)
		{
			base.InitializeOnce(networkManager);

			// Listen for auth broadcast from child servers.
			base.NetworkManager.ServerManager.RegisterBroadcast<SceneServerAuthBroadcast>(OnRelayServerSceneServerAuthBroadcastReceived, false);
		}

		/// <summary>
		/// Received on Relay Server when a Scene Server sends the password broadcast message.
		/// </summary>
		internal void OnRelayServerSceneServerAuthBroadcastReceived(NetworkConnection conn, SceneServerAuthBroadcast msg)
		{
			/* If client is already authenticated this could be an attack. Connections
			 * are removed when a client disconnects so there is no reason they should
			 * already be considered authenticated. */
			if (conn.Authenticated)
			{
				conn.Disconnect(true);
				return;
			}

			// password must match on server to server connections otherwise we disconnect immediately
			if (!msg.password.Equals(RelayServerPassword))
			{
				conn.Disconnect(true);
				return;
			}

			// tell the connecting Scene Server the connection was successful
			base.NetworkManager.ServerManager.Broadcast(conn, new SceneServerAuthResultBroadcast(), false);

			OnAuthentication(conn, true);
			OnRelayAuthenticationResult?.Invoke(conn, true);
		}
	}
}