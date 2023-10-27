namespace FishMMO.Shared
{
	public interface IChatHelper
	{
		ChatCommand GetChannelCommand(ChatChannel channel);
		bool OnWorldChat(Character sender, ChatBroadcast msg);
		bool OnRegionChat(Character sender, ChatBroadcast msg);
		bool OnPartyChat(Character sender, ChatBroadcast msg);
		bool OnGuildChat(Character sender, ChatBroadcast msg);
		bool OnTellChat(Character sender, ChatBroadcast msg);
		bool OnTradeChat(Character sender, ChatBroadcast msg);
		bool OnSayChat(Character sender, ChatBroadcast msg);
	}
}