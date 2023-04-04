using FishNet.Broadcast;

public struct ChatBroadcast : IBroadcast
{
	public ChatChannel channel;
	public string text;
}