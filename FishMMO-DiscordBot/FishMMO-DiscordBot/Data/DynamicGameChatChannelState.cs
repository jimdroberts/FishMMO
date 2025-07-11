using System;

namespace FishMMO.DiscordBot.Data
{
	public class DynamicGameChatChannelState
	{
		public ulong DiscordCategoryId { get; set; }
		public ulong DiscordChannelId { get; set; }
		public long WorldServerId { get; set; }
		public string WorldServerName { get; set; }
		public long SceneServerId { get; set; }
		public string SceneServerName { get; set; }
		public DateTime LastActivity { get; set; }
	}
}