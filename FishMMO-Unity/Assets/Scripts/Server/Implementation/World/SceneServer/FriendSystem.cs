using UnityEngine;
using UnityEngine.SceneManagement;
using FishNet.Connection;
using FishNet.Transporting;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Server.DatabaseServices;
using FishMMO.Shared;
using FishMMO.Database.Npgsql.Entities;

namespace FishMMO.Server.Implementation.SceneServer
{
	/// <summary>
	/// Server friend system.
	/// </summary>
	public class FriendSystem : ServerBehaviour, IFriendSystem
	{
		[SerializeField]
		private int maxFriends = 100;

		/// <summary>
		/// Maximum number of friends allowed per character.
		/// </summary>
		public int MaxFriends { get { return maxFriends; } }

		/// <summary>
		/// Initializes the friend system, registering broadcast handlers for friend add and remove requests.
		/// </summary>
		public override void InitializeOnce()
		{
			if (Server != null &&
				Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, Scene> characterSystem))
			{
				Server.NetworkWrapper.RegisterBroadcast<FriendAddNewBroadcast>(OnServerFriendAddNewBroadcastReceived, true);
				Server.NetworkWrapper.RegisterBroadcast<FriendRemoveBroadcast>(OnServerFriendRemoveBroadcastReceived, true);
			}
			else
			{
				enabled = false;
			}
		}

		/// <summary>
		/// Cleans up the friend system, unregistering broadcast handlers.
		/// </summary>
		public override void Destroying()
		{
			if (Server != null)
			{
				Server.NetworkWrapper.UnregisterBroadcast<FriendAddNewBroadcast>(OnServerFriendAddNewBroadcastReceived);
				Server.NetworkWrapper.UnregisterBroadcast<FriendRemoveBroadcast>(OnServerFriendRemoveBroadcastReceived);
			}
		}

		/// <summary>
		/// Handles broadcast to add a new friend for a player character.
		/// Validates character, friend count, and prevents self-friending. Adds friend to database and notifies client.
		/// </summary>
		/// <param name="conn">Network connection of the requesting client.</param>
		/// <param name="msg">FriendAddNewBroadcast message containing friend data.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		public void OnServerFriendAddNewBroadcastReceived(NetworkConnection conn, FriendAddNewBroadcast msg, Channel channel)
		{
			if (conn.FirstObject == null)
			{
				return;
			}
			IFriendController friendController = conn.FirstObject.GetComponent<IFriendController>();

			// Validate character
			if (friendController == null ||
				friendController.Friends.Count > maxFriends)
			{
				return;
			}

			// Validate friend invite
			if (Server == null || Server.CoreServer.NpgsqlDbContextFactory == null)
			{
				return;
			}

			// Are we trying to become our own friend again...
			if (friendController.Character.ID == msg.CharacterID)
			{
				return;
			}

			using var dbContext = Server.CoreServer.NpgsqlDbContextFactory.CreateDbContext();
			CharacterEntity friendEntity = CharacterService.GetByID(dbContext, msg.CharacterID, true);
			if (friendEntity != null)
			{
				// Add the friend to the database
				CharacterFriendService.Save(dbContext, friendController.Character.ID, friendEntity.ID);

				// Add the friend to the characters friend controller
				friendController.AddFriend(friendEntity.ID);

				// Tell the character they added a new friend!
				Server.NetworkWrapper.Broadcast(conn, new FriendAddBroadcast()
				{
					CharacterID = friendEntity.ID,
					Online = friendEntity.Online,
				}, true, Channel.Reliable);
			}
		}

		/// <summary>
		/// Handles broadcast to remove a friend for a player character.
		/// Validates character and removes friend from database and notifies client if successful.
		/// </summary>
		/// <param name="conn">Network connection of the requesting client.</param>
		/// <param name="msg">FriendRemoveBroadcast message containing friend removal data.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		public void OnServerFriendRemoveBroadcastReceived(NetworkConnection conn, FriendRemoveBroadcast msg, Channel channel)
		{
			if (Server.CoreServer.NpgsqlDbContextFactory == null)
			{
				return;
			}
			if (conn.FirstObject == null)
			{
				return;
			}
			IFriendController friendController = conn.FirstObject.GetComponent<IFriendController>();

			// Validate character
			if (friendController == null)
			{
				return;
			}

			// Remove the friend if it exists
			if (friendController.Friends.Contains(msg.CharacterID))
			{
				// Remove the character from the friend in the database
				using var dbContext = Server.CoreServer.NpgsqlDbContextFactory.CreateDbContext();
				if (CharacterFriendService.Delete(dbContext, friendController.Character.ID, msg.CharacterID))
				{
					// Tell the character they removed a friend
					Server.NetworkWrapper.Broadcast(conn, new FriendRemoveBroadcast()
					{
						CharacterID = msg.CharacterID,
					}, true, Channel.Reliable);
				}
			}
		}
	}
}