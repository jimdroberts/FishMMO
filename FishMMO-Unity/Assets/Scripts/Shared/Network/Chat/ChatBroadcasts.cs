using FishNet.Broadcast;

namespace FishMMO.Shared
{
	public struct ChatBroadcast : IBroadcast
	{
		public ChatChannel channel;
		public long senderID;
		public string text;
	}
}