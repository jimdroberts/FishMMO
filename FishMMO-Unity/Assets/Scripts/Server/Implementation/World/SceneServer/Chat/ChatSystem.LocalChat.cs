using FishNet.Connection;
using FishNet.Transporting;
using System.Collections.Generic;
using UnityEngine;
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

			/* One multicast over FishNet's own connection set for the scene: the line is
			 * serialised once, not once per player in it (hot-path audit S1). */
			Server.NetworkWrapper.BroadcastToScene(scene, msg, true, Channel.Reliable);
			return false; // we return false here so the message is not written to the database
		}

		/// <summary>
		/// Handles say (local) chat messages, broadcasting to all observers of the sender.
		/// </summary>
		/// <remarks>
		/// Scoped by the sender's observer set, which is already range-limited: the player distance
		/// condition admits a viewer at up to 100 m, so an observer of the speaker is by
		/// construction someone standing near enough to hear them. Re-deriving earshot from
		/// positions here would be a second copy of a radius the interest system already applies,
		/// and the two would drift the moment either was retuned.
		/// </remarks>
		/// <param name="sender">Player character sending the message.</param>
		/// <param name="msg">Chat broadcast message.</param>
		/// <returns>False to prevent message from being written to the database.</returns>
		public bool OnSayChat(IPlayerCharacter sender, ChatBroadcast msg)
		{
			/* Multicast over the observer set itself, serialised once. The set is read and never
			 * kept, and nothing the send does changes it. */
			if (sender != null && sender.Observers != null && sender.Observers.Count > 0)
			{
				Server.NetworkWrapper.Broadcast(sender.Observers, msg, true, Channel.Reliable);
			}
			return false; // we return false here so the message is not written to the database
		}
	}
}