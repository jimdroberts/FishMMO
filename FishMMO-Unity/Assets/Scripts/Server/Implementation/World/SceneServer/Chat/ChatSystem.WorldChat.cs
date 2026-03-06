using FishNet.Connection;
using FishNet.Transporting;
using System.Collections.Generic;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared.Core;
using FishMMO.Shared;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	// World-wide broadcast channel handlers: World and Trade.
	// Live player messages are buffered into OutboundWorldBroadcastBuffer and flushed
	// periodically by OnPeriodicOutboundFlush (outbound broadcast batching).
	// Pump-sourced messages (sender == null) are still broadcast immediately because
	// they arrive pre-batched from the database pump and must not re-enter the buffer.
	public partial class ChatSystem
	{
		/// <summary>
		/// Handles world chat messages.
		/// Live messages are buffered per-world for batched delivery.
		/// Pump-sourced messages (sender == null) are broadcast immediately.
		/// </summary>
		/// <param name="sender">Player character sending the message, or null for pump-sourced.</param>
		/// <param name="msg">Chat broadcast message.</param>
		/// <returns>True if message was accepted (live sender), false otherwise.</returns>
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

			// Pump-sourced messages: broadcast immediately (already persisted, pre-batched).
			if (sender == null)
			{
				BroadcastToWorld(worldID, newMsg);
				return false;
			}

			// Live player message: buffer for batched outbound delivery.
			if (Server.DataContainerRegistry.TryGet(out IChatSystemRuntimeData chatData) &&
				chatData.OutboundWorldBroadcastBuffer != null)
			{
				if (!chatData.OutboundWorldBroadcastBuffer.TryGetValue(worldID, out var list))
				{
					list = new List<ChatBroadcast>();
					chatData.OutboundWorldBroadcastBuffer[worldID] = list;
				}
				list.Add(newMsg);

				// Safety cap: drop oldest messages if the buffer grows beyond the hard limit
				// (e.g., flushes stalling). Prevents unbounded memory growth.
				if (list.Count > maxBufferedWorldMessages)
				{
					list.RemoveRange(0, list.Count - maxBufferedWorldMessages);
				}
			}
			return true;
		}

		/// <summary>
		/// Handles trade chat messages.
		/// Live messages are buffered per-world for batched delivery.
		/// Pump-sourced messages (sender == null) are broadcast immediately.
		/// </summary>
		/// <param name="sender">Player character sending the message, or null for pump-sourced.</param>
		/// <param name="msg">Chat broadcast message.</param>
		/// <returns>True if message was accepted (live sender), false otherwise.</returns>
		public bool OnTradeChat(IPlayerCharacter sender, ChatBroadcast msg)
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

			// Pump-sourced messages: broadcast immediately (already persisted, pre-batched).
			if (sender == null)
			{
				BroadcastToWorld(worldID, newMsg);
				return false;
			}

			// Live player message: buffer for batched outbound delivery.
			if (Server.DataContainerRegistry.TryGet(out IChatSystemRuntimeData chatData) &&
				chatData.OutboundWorldBroadcastBuffer != null)
			{
				if (!chatData.OutboundWorldBroadcastBuffer.TryGetValue(worldID, out var list))
				{
					list = new List<ChatBroadcast>();
					chatData.OutboundWorldBroadcastBuffer[worldID] = list;
				}
				list.Add(newMsg);

				// Safety cap: drop oldest messages if the buffer grows beyond the hard limit
				// (e.g., flushes stalling). Prevents unbounded memory growth.
				if (list.Count > maxBufferedWorldMessages)
				{
					list.RemoveRange(0, list.Count - maxBufferedWorldMessages);
				}
			}
			return true;
		}

		/// <summary>
		/// Broadcasts a message to all characters in the specified world immediately.
		/// Used for pump-sourced (already-persisted) messages and Discord relay.
		/// </summary>
		private void BroadcastToWorld(long worldID, ChatBroadcast msg)
		{
			if (Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var mappingData) &&
				mappingData.CharactersByWorld.TryGetValue(worldID, out var characters) &&
				Server.DataContainerRegistry.TryGet<IChatSystemRuntimeData>(out var chatData))
			{
				// Defensive copy into reusable buffer: Broadcast may trigger a disconnect callback that modifies the collection.
				// Manual loop avoids boxing the Dictionary.ValueCollection struct enumerator.
				var buffer = chatData.CharacterBroadcastBuffer;
				buffer.Clear();
				foreach (var character in characters.Values)
				{
					buffer.Add(character);
				}
				for (int i = 0; i < buffer.Count; i++)
				{
					Server.NetworkWrapper.Broadcast(buffer[i].Owner, msg, true, Channel.Reliable);
				}
			}
		}
	}
}