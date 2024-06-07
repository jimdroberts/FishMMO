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
			if (Server != null)
			{
				Server.RegisterBroadcast<CreateAccountBroadcast>(OnServerCreateAccountBroadcastReceived, false);
			}
			else
			{
				enabled = false;
			}
		}

		public override void Destroying()
		{
			if (Server != null)
			{
				Server.UnregisterBroadcast<CreateAccountBroadcast>(OnServerCreateAccountBroadcastReceived);
			}
		}

		private void OnServerCreateAccountBroadcastReceived(NetworkConnection conn, CreateAccountBroadcast msg, Channel channel)
		{
			ClientAuthenticationResult result = ClientAuthenticationResult.InvalidUsernameOrPassword;
			if (Server.NpgsqlDbContextFactory != null)
			{
				using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
				if (dbContext != null)
				{
					result = AccountService.TryCreate(dbContext, msg.username, msg.salt, msg.verifier);
				}
			}
			Server.Broadcast(conn, new ClientAuthResultBroadcast() { result = result }, false, Channel.Reliable);
		}
	}
}