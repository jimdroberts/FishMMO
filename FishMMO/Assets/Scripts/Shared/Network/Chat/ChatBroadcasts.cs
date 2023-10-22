using FishNet.Broadcast;

public struct ChatBroadcast : IBroadcast
{
	public ChatChannel channel;
	public long senderID;
	public string text;
}