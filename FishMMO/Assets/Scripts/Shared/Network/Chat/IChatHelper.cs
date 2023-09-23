public interface IChatHelper
{
	ChatCommand GetChannelCommand(ChatChannel channel);
	void OnWorldChat(Character sender, ChatBroadcast msg);
	void OnRegionChat(Character sender, ChatBroadcast msg);
	void OnPartyChat(Character sender, ChatBroadcast msg);
	void OnGuildChat(Character sender, ChatBroadcast msg);
	void OnTellChat(Character sender, ChatBroadcast msg);
	void OnTradeChat(Character sender, ChatBroadcast msg);
	void OnSayChat(Character sender, ChatBroadcast msg);
}