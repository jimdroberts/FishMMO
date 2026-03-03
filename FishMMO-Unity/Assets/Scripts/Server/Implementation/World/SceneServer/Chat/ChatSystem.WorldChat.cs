using FishNet.Connection;
using FishNet.Transporting;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared.Core;
using FishMMO.Shared;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	// World-wide broadcast channel handlers: World and Trade.
	public partial class ChatSystem
	{
		/// <summary>
		/// Handles world chat messages, broadcasting to all characters in the specified world.
		/// </summary>
		/// <param name="sender">Player character sending the message.</param>
		/// <param name="msg">Chat broadcast message.</param>
		/// <returns>True if message was broadcast, false otherwise.</returns>
		public bool OnWorldChat(IPlayerCharacter sender, ChatBroadcast msg)
		{
			// get the world ID
			string wid = ChatHelper.GetWordAndTrimmed(msg.Text, out string trimmed);
			if (string.IsNullOrWhiteSpace(wid) || !long.TryParse(wid, out long worldID))
			{
				// no worldID in the message
				return false;
			}

			ChatBroadcast newMsg = new ChatBroadcast()
			{
				Channel = msg.Channel,
				SenderID = msg.SenderID,
				Text = trimmed,
			};

			if (Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var mappingData) &&
				mappingData.CharactersByWorld.TryGetValue(worldID, out var characters) &&
				Server.DataContainerRegistry.TryGet<IChatSystemRuntimeData>(out var chatData))
			{
				// Defensive copy into reusable buffer: Broadcast may trigger a disconnect callback that modifies the collection.
				var buffer = chatData.CharacterBroadcastBuffer;
				buffer.Clear();
				buffer.AddRange(characters.Values);
				for (int i = 0; i < buffer.Count; i++)
				{
					Server.NetworkWrapper.Broadcast(buffer[i].Owner, newMsg, true, Channel.Reliable);
				}
			}
			return true;
		}

		/// <summary>
		/// Handles trade chat messages, broadcasting to all characters in the specified world.
		/// </summary>
		/// <param name="sender">Player character sending the message.</param>
		/// <param name="msg">Chat broadcast message.</param>
		/// <returns>True if message was broadcast, false otherwise.</returns>
		public bool OnTradeChat(IPlayerCharacter sender, ChatBroadcast msg)
		{
			// get the world ID
			string wid = ChatHelper.GetWordAndTrimmed(msg.Text, out string trimmed);
			if (string.IsNullOrWhiteSpace(wid) || !long.TryParse(wid, out long worldID))
			{
				// no worldID in the message
				return false;
			}

			if (Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var mappingData))
			{
				ChatBroadcast newMsg = new ChatBroadcast()
				{
					Channel = msg.Channel,
					SenderID = msg.SenderID,
					Text = trimmed,
				};
				if (mappingData.CharactersByWorld.TryGetValue(worldID, out var characters) &&
					Server.DataContainerRegistry.TryGet<IChatSystemRuntimeData>(out var chatData))
				{
					// Defensive copy into reusable buffer: Broadcast may trigger a disconnect callback that modifies the collection.
					var buffer = chatData.CharacterBroadcastBuffer;
					buffer.Clear();
					buffer.AddRange(characters.Values);
					for (int i = 0; i < buffer.Count; i++)
					{
						Server.NetworkWrapper.Broadcast(buffer[i].Owner, newMsg, true, Channel.Reliable);
					}
				}
				return true;
			}
			return false;
		}
	}
}