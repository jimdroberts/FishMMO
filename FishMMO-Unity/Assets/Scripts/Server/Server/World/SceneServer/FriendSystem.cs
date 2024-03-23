using FishNet.Connection;
using FishNet.Transporting;
using FishMMO.Server.DatabaseServices;
using FishMMO.Shared;
using FishMMO.Database.Npgsql.Entities;


namespace FishMMO.Server
{
	/// <summary>
	/// Server friend system.
	/// </summary>
	public class FriendSystem : ServerBehaviour
	{
		public int MaxFriends = 100;

		public override void InitializeOnce()
		{
			if (ServerManager != null &&
				ServerBehaviour.TryGet(out CharacterSystem characterSystem))
			{
				ServerManager.OnServerConnectionState += ServerManager_OnServerConnectionState;
			}
			else
			{
				enabled = false;
			}
		}

		private void ServerManager_OnServerConnectionState(ServerConnectionStateArgs args)
		{
			if (args.ConnectionState == LocalConnectionState.Started)
			{
				ServerManager.RegisterBroadcast<FriendAddNewBroadcast>(OnServerFriendAddNewBroadcastReceived, true);
				ServerManager.RegisterBroadcast<FriendRemoveBroadcast>(OnServerFriendRemoveBroadcastReceived, true);
			}
			else if (args.ConnectionState == LocalConnectionState.Stopped)
			{
				ServerManager.UnregisterBroadcast<FriendAddNewBroadcast>(OnServerFriendAddNewBroadcastReceived);
				ServerManager.UnregisterBroadcast<FriendRemoveBroadcast>(OnServerFriendRemoveBroadcastReceived);
			}
		}

		public void OnServerFriendAddNewBroadcastReceived(NetworkConnection conn, FriendAddNewBroadcast msg, Channel channel)
		{
			if (conn.FirstObject == null)
			{
				return;
			}
			FriendController friendController = conn.FirstObject.GetComponent<FriendController>();

			// validate character
			if (friendController == null ||
				friendController.Friends.Count > MaxFriends)
			{
				return;
			}

			// validate friend invite
			if (Server == null || Server.NpgsqlDbContextFactory == null)
			{
				return;
			}
			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
			CharacterEntity friendEntity = CharacterService.GetByName(dbContext, msg.characterName, true);
			if (friendEntity != null)
			{
				// are we trying to become our own friend again...
				if (friendController.Character.ID == friendEntity.ID)
                {
					return;
                }

				// add the friend to the database
				CharacterFriendService.Save(dbContext, friendController.Character.ID, friendEntity.ID);

				// add the friend to the characters friend controller
				friendController.AddFriend(friendEntity.ID);

				// tell the character they added a new friend!
				Server.Broadcast(conn, new FriendAddBroadcast()
				{
					characterID = friendEntity.ID,
					online = friendEntity.Online,
				}, true, Channel.Reliable);
			}
		}

		public void OnServerFriendRemoveBroadcastReceived(NetworkConnection conn, FriendRemoveBroadcast msg, Channel channel)
		{
			if (Server.NpgsqlDbContextFactory == null)
			{
				return;
			}
			if (conn.FirstObject == null)
			{
				return;
			}
			FriendController friendController = conn.FirstObject.GetComponent<FriendController>();

			// validate character
			if (friendController == null)
			{
				return;
			}

			// remove the friend if it exists
			if (friendController.Friends.Contains(msg.characterID))
			{
				// remove the character from the friend in the database
				using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
				if (CharacterFriendService.Delete(dbContext, friendController.Character.ID, msg.characterID))
				{
					// tell the character they removed a friend
					Server.Broadcast(conn, new FriendRemoveBroadcast()
					{
						characterID = msg.characterID,
					}, true, Channel.Reliable);
				}
			}
		}
	}
}