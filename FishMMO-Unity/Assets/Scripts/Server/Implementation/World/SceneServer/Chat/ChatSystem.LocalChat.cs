using FishNet.Connection;
using FishNet.Transporting;
using System.Collections.Generic;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared.Core;
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
			/* The scene the sender is actually in, taken from the spawned object.
			 *
			 * This used to resolve the scene by name from IPlayerCharacter.SceneName, which is
			 * wrong twice over. Scene stacking means several instances of one scene are loaded
			 * at once under the same name, and GetSceneByName returns whichever was loaded
			 * first — so every channel's region chat was delivered to the occupants of channel
			 * one, and players on the other channels neither saw their own messages nor were
			 * spared anyone else's. And inside an instance SceneName names the open-world scene
			 * the character will return to, not the dungeon it is standing in, so region chat
			 * from inside a dungeon was broadcast to whoever was in that open-world scene — or,
			 * if this server does not host it, to nobody.
			 *
			 * The spawned object's scene is the one the character was placed in, and it is what
			 * SceneConnections is keyed by. */
			UnityEngine.SceneManagement.Scene scene = sender.GameObject != null
				? sender.GameObject.scene
				: default;

			if (scene.IsValid() &&
				Server.NetworkWrapper.NetworkManager != null &&
				Server.NetworkWrapper.NetworkManager.SceneManager != null)
			{
				if (Server.NetworkWrapper.NetworkManager.SceneManager.SceneConnections.TryGetValue(scene, out HashSet<NetworkConnection> connections) &&
					Server.DataContainerRegistry.TryGet<IChatSystemRuntimeData>(out var chatData))
				{
					// Defensive copy into reusable buffer: Broadcast may trigger a disconnect callback that modifies the set.
					// Manual loop avoids boxing the HashSet struct enumerator.
					var buffer = chatData.ConnectionBroadcastBuffer;
					buffer.Clear();
					foreach (var conn in connections)
					{
						buffer.Add(conn);
					}
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
				// Manual loop avoids boxing the HashSet struct enumerator.
				var buffer = chatData.ConnectionBroadcastBuffer;
				buffer.Clear();
				foreach (var conn in sender.Observers)
				{
					buffer.Add(conn);
				}
				for (int i = 0; i < buffer.Count; i++)
				{
					Server.NetworkWrapper.Broadcast(buffer[i], msg, true, Channel.Reliable);
				}
			}
			return false; // we return false here so the message is not written to the database
		}
	}
}