using FishNet.Connection;
using FishNet.Transporting;
using FishMMO.Server.DatabaseServices;
using FishMMO.Shared;

namespace FishMMO.Server
{
	/// <summary>
	/// Account Creation system.
	/// </summary>
	public class AccountCreationSystem : ServerBehaviour
	{
		public override void InitializeOnce()
		{
			if (ServerManager != null)
			{
				ServerManager.OnServerConnectionState += ServerManager_OnServerConnectionState;
			}
			else
			{
				enabled = false;
			}
		}

		private void ServerManager_OnServerConnectionState(ServerConnectionStateArgs obj)
		{
			if (obj.ConnectionState == LocalConnectionState.Started)
			{
				ServerManager.RegisterBroadcast<CreateAccountBroadcast>(OnServerCreateAccountBroadcastReceived, false);
			}
			else if (obj.ConnectionState == LocalConnectionState.Stopped)
			{
				ServerManager.UnregisterBroadcast<CreateAccountBroadcast>(OnServerCreateAccountBroadcastReceived);
			}
		}

		private void OnServerCreateAccountBroadcastReceived(NetworkConnection conn, CreateAccountBroadcast msg)
		{
			ClientAuthenticationResult result = ClientAuthenticationResult.InvalidUsernameOrPassword;
			if (!string.IsNullOrWhiteSpace(msg.username) &&
				!string.IsNullOrWhiteSpace(msg.salt) &&
				!string.IsNullOrWhiteSpace(msg.verifier) &&
				Server.NpgsqlDbContextFactory != null)
			{
				using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
				if (dbContext != null)
				{
					result = AccountService.TryCreate(dbContext, msg.username, msg.salt, msg.verifier);
				}
			}
			conn.Broadcast(new ClientAuthResultBroadcast() { result = result }, false, Channel.Reliable);
		}
	}
}