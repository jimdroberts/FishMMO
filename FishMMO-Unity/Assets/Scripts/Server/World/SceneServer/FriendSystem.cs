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
			if (Server != null &&
				ServerBehaviour.TryGet(out CharacterSystem characterSystem))
			{
				Server.RegisterBroadcast<FriendAddNewBroadcast>(OnServerFriendAddNewBroadcastReceived, true);
				Server.RegisterBroadcast<FriendRemoveBroadcast>(OnServerFriendRemoveBroadcastReceived, true);
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
				Server.UnregisterBroadcast<FriendAddNewBroadcast>(OnServerFriendAddNewBroadcastReceived);
				Server.UnregisterBroadcast<FriendRemoveBroadcast>(OnServerFriendRemoveBroadcastReceived);
			}
		}

		public void OnServerFriendAddNewBroadcastReceived(NetworkConnection conn, FriendAddNewBroadcast msg, Channel channel)
		{
			if (conn.FirstObject == null)
			{
				return;
			}
			IFriendController friendController = conn.FirstObject.GetComponent<IFriendController>();

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

			// are we trying to become our own friend again...
			if (friendController.Character.ID == msg.CharacterID)
			{
				return;
			}

			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
			CharacterEntity friendEntity = CharacterService.GetByID(dbContext, msg.CharacterID, true);
			if (friendEntity != null)
			{
				// add the friend to the database
				CharacterFriendService.Save(dbContext, friendController.Character.ID, friendEntity.ID);

				// add the friend to the characters friend controller
				friendController.AddFriend(friendEntity.ID);

				// tell the character they added a new friend!
				Server.Broadcast(conn, new FriendAddBroadcast()
				{
					CharacterID = friendEntity.ID,
					Online = friendEntity.Online,
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
			IFriendController friendController = conn.FirstObject.GetComponent<IFriendController>();

			// validate character
			if (friendController == null)
			{
				return;
			}

			// remove the friend if it exists
			if (friendController.Friends.Contains(msg.CharacterID))
			{
				// remove the character from the friend in the database
				using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
				if (CharacterFriendService.Delete(dbContext, friendController.Character.ID, msg.CharacterID))
				{
					// tell the character they removed a friend
					Server.Broadcast(conn, new FriendRemoveBroadcast()
					{
						CharacterID = msg.CharacterID,
					}, true, Channel.Reliable);
				}
			}
		}
	}
}