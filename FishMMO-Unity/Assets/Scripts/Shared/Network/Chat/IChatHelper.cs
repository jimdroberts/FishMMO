namespace FishMMO.Shared
{
	public interface IChatHelper
	{
		ChatCommand GetChannelCommand(ChatChannel channel);
		bool OnWorldChat(IPlayerCharacter sender, ChatBroadcast msg);
		bool OnRegionChat(IPlayerCharacter sender, ChatBroadcast msg);
		bool OnPartyChat(IPlayerCharacter sender, ChatBroadcast msg);
		bool OnGuildChat(IPlayerCharacter sender, ChatBroadcast msg);
		bool OnTellChat(IPlayerCharacter sender, ChatBroadcast msg);
		bool OnTradeChat(IPlayerCharacter sender, ChatBroadcast msg);
		bool OnSayChat(IPlayerCharacter sender, ChatBroadcast msg);
	}
}