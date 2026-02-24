using FishNet.Connection;
using FishNet.Transporting;
using System.Collections.Generic;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	// Local / scene-scoped broadcast channel handlers: Region and Say.
	public partial class ChatSystem
	{
		/// <summary>
		/// Handles region chat messages, broadcasting to all connections in the sender's scene.
		/// </summary>
		/// <param name="sender">Player character sending the message.</param>
		/// <param name="msg">Chat broadcast message.</param>
		/// <returns>False to prevent message from being written to the database.</returns>
		public bool OnRegionChat(IPlayerCharacter sender, ChatBroadcast msg)
		{
			if (sender == null)
			{
				return false;
			}
			// get the senders observed scene
			UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetSceneByName(sender.SceneName);
			if (scene.IsValid() &&
				Server.NetworkWrapper.NetworkManager != null &&
				Server.NetworkWrapper.NetworkManager.SceneManager != null)
			{
				if (Server.NetworkWrapper.NetworkManager.SceneManager.SceneConnections.TryGetValue(scene, out HashSet<NetworkConnection> connections) &&
					Server.DataContainerRegistry.TryGet<IChatSystemRuntimeData>(out var chatData))
				{
					// Defensive copy into reusable buffer: Broadcast may trigger a disconnect callback that modifies the set.
					var buffer = chatData.ConnectionBroadcastBuffer;
					buffer.Clear();
					buffer.AddRange(connections);
					for (int i = 0; i < buffer.Count; i++)
					{
						Server.NetworkWrapper.Broadcast(buffer[i], msg, true, Channel.Reliable);
					}
				}
			}
			return false; // we return false here so the message is not written to the database
		}

		/// <summary>
		/// Handles say (local) chat messages, broadcasting to all observers of the sender.
		/// </summary>
		/// <param name="sender">Player character sending the message.</param>
		/// <param name="msg">Chat broadcast message.</param>
		/// <returns>False to prevent message from being written to the database.</returns>
		public bool OnSayChat(IPlayerCharacter sender, ChatBroadcast msg)
		{
			if (sender != null && sender.Observers != null &&
				Server.DataContainerRegistry.TryGet<IChatSystemRuntimeData>(out var chatData))
			{
				// Defensive copy into reusable buffer: Broadcast may trigger a disconnect callback that modifies the set.
				var buffer = chatData.ConnectionBroadcastBuffer;
				buffer.Clear();
				buffer.AddRange(sender.Observers);
				for (int i = 0; i < buffer.Count; i++)
				{
					Server.NetworkWrapper.Broadcast(buffer[i], msg, true, Channel.Reliable);
				}
			}
			return false; // we return false here so the message is not written to the database
		}
	}
}