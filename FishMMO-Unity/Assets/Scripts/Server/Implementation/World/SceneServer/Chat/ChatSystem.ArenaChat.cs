using FishNet.Connection;
using FishNet.Transporting;
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

			if (!Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var mappingData))
			{
				return false;
			}

			ChatBroadcast relay = new ChatBroadcast
			{
				Channel = ChatChannel.Team,
				SenderID = sender.ID,
				Text = msg.Text,
			};

			foreach (var kvp in mappingData.ConnectionCharacters)
			{
				IPlayerCharacter member = kvp.Value;
				if (member?.GameObject == null || member.Owner == null || !member.Owner.IsActive ||
					member.GameObject.scene.handle != sceneHandle ||
					ArenaTeamRegistry.GetTeam(sceneHandle, member.ID) != team)
				{
					continue;
				}

				Server.NetworkWrapper.Broadcast(member.Owner, relay, true, Channel.Reliable);
			}

			// Nothing to persist.
			return false;
		}
	}
}
