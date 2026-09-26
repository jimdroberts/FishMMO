using FishNet.Connection;
using FishNet.Transporting;
using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishMMO.Server.Core.World.SceneServer;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Arena team chat.
	/// </summary>
	public partial class ChatSystem
	{
		/// <summary>
		/// Delivers a team message to the sender's teammates in the arena they are standing in.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Local and synchronous. Every seat of a match is connected to the scene server hosting its
		/// instance, so there is no other server to reach and no database to go through; the team
		/// registry the coordinator publishes says who is on which side. A sender who is not seated
		/// in a live-or-pending arena match is told so rather than silently dropped.
		/// </para>
		/// <para>
		/// Not persisted. The channel exists only for the duration of a match, and a chat history
		/// query has no team to resolve it against afterwards.
		/// </para>
		/// </remarks>
		public bool OnTeamChat(IPlayerCharacter sender, ChatBroadcast msg)
		{
			if (sender?.GameObject == null || sender.Owner == null)
			{
				return false;
			}

			int sceneHandle = sender.GameObject.scene.handle;
			int team = ArenaTeamRegistry.GetTeam(sceneHandle, sender.ID);
			if (team < 0)
			{
				Server.NetworkWrapper.Broadcast(sender.Owner, new ChatBroadcast
				{
					Channel = ChatChannel.System,
					Text = "You are not on an arena team.",
				}, true, Channel.Reliable);
				return false;
			}

			if (!Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var mappingData) ||
				!Server.DataContainerRegistry.TryGet<IChatSystemRuntimeData>(out var chatData) ||
				chatData.ConnectionBroadcastSet == null)
			{
				return false;
			}

			ChatBroadcast relay = new ChatBroadcast
			{
				Channel = ChatChannel.Team,
				SenderID = sender.ID,
				Text = msg.Text,
			};

			/* Only the arena's own connections are considered, from FishNet's per-scene set.
			 *
			 * This walked every character on the server and asked each for its scene — a native
			 * call per player per team line, a thousand of them on a full server to find the
			 * handful in one match (hot-path audit L3). Everybody who can be on a team here is
			 * connected to the arena scene; the set is read, never kept or changed, and the
			 * recipients are copied out of it before anything is sent. */
			if (!Server.NetworkWrapper.TryGetSceneConnections(sender.GameObject.scene, out HashSet<NetworkConnection> sceneConnections))
			{
				return false;
			}

			HashSet<NetworkConnection> recipients = chatData.ConnectionBroadcastSet;
			recipients.Clear();
			try
			{
				foreach (NetworkConnection conn in sceneConnections)
				{
					if (conn == null ||
						!conn.IsActive ||
						!mappingData.ConnectionCharacters.TryGetValue(conn, out IPlayerCharacter member) ||
						member == null ||
						ArenaTeamRegistry.GetTeam(sceneHandle, member.ID) != team)
					{
						continue;
					}
					recipients.Add(conn);
				}

				if (recipients.Count > 0)
				{
					Server.NetworkWrapper.Broadcast(recipients, relay, true, Channel.Reliable);
				}
			}
			finally
			{
				recipients.Clear();
			}

			// Nothing to persist.
			return false;
		}
	}
}
